import { promises as fs } from "node:fs"
import path from "node:path"
import type {
  BackfillState,
  BackfillSummary,
  NoitConfig,
  PluginContext,
  SessionSummary,
} from "./types.ts"
import { ensureDir, rawSessionsDir } from "./config.ts"
import {
  listSessions,
  normalizeDirectory,
  projectIdsForDirectory,
  readMessagesDetailed,
  withBusyRetry,
  type SessionMessagesInfo,
} from "./db.ts"
import { redactRaw } from "./redact.ts"

export function stateKey(ctx: PluginContext): string {
  const project = ctx.location.project?.id ?? "unknown"
  return `backfill/${project}`
}

export async function loadState(ctx: PluginContext): Promise<BackfillState | undefined> {
  const value = await ctx.storage.get(stateKey(ctx))
  if (!value || typeof value !== "object") return undefined
  return value as unknown as BackfillState
}

export async function saveState(ctx: PluginContext, state: BackfillState): Promise<void> {
  await ctx.storage.set(stateKey(ctx), state as unknown as never)
}

export function emptyState(ctx?: PluginContext): BackfillState {
  return {
    installedAt: Date.now(),
    lastBackfill: null,
    imported: [],
    failures: {},
    projectID: ctx?.location.project?.id,
    directory: ctx?.location.directory,
    source: "db",
  }
}

export async function ensureState(ctx: PluginContext): Promise<BackfillState> {
  const existing = await loadState(ctx)
  if (existing) return existing
  const fresh = emptyState(ctx)
  await saveState(ctx, fresh)
  return fresh
}

function allowedProjectIds(ctx: PluginContext, config: NoitConfig, directory: string): Set<string> {
  const ids = new Set<string>()
  const configured = config.backfill.projectId || ctx.location.project?.id
  if (configured) ids.add(configured)
  try {
    for (const id of projectIdsForDirectory(directory)) ids.add(id)
  } catch {}
  return ids
}

function matchesScope(session: SessionSummary, projectIds: Set<string>, directory: string): boolean {
  const target = normalizeDirectory(directory)
  if (target.length === 0) return false
  const sessionDirectory = normalizeDirectory(session.directory ?? session.location?.directory ?? "")
  if (sessionDirectory !== target) return false
  const project = session.projectID ? String(session.projectID) : ""
  return project.length > 0 && projectIds.has(project)
}

async function listProjectSessions(
  ctx: PluginContext,
  config: NoitConfig,
): Promise<SessionSummary[]> {
  const directory = config.backfill.directory || ctx.location.directory
  const projectId = config.backfill.projectId || ctx.location.project?.id
  const projectIds = allowedProjectIds(ctx, config, directory)

  const api = (ctx.session as unknown as { list?: unknown }).list
  if (typeof api === "function") {
    const limit = Math.min(Math.max(config.backfill.limit || 100, 1), 100)
    const collected: SessionSummary[] = []
    let cursor: string | undefined
    let pages = 0
    try {
      do {
        const page = (await (
          api as (input: unknown) => Promise<unknown>
        ).call(ctx.session, { directory, limit, cursor })) as unknown as {
          data?: SessionSummary[]
          items?: SessionSummary[]
          cursor?: { next?: string | null }
        }
        const batch = page.data ?? page.items ?? []
        for (const session of batch) collected.push({ ...session, source: "api" })
        cursor = page.cursor?.next ?? undefined
        pages += 1
        if (pages >= 1000) break
      } while (cursor)
    } catch {}
    const scoped = collected.filter((session) => matchesScope(session, projectIds, directory))
    if (scoped.length > 0) return scoped
  }

  let fromDb: SessionSummary[] = []
  try {
    fromDb = (await withBusyRetry(() => listSessions(projectId, directory))).map((session) => ({
      ...session,
      source: session.source ?? "db",
    }))
  } catch {
    fromDb = []
  }
  return fromDb.filter((session) => matchesScope(session, projectIds, directory))
}

async function writeIndex(
  ctx: PluginContext,
  state: BackfillState,
  sessions: SessionSummary[],
): Promise<void> {
  const dir = rawSessionsDir(ctx)
  const payload = {
    projectID: state.projectID ?? ctx.location.project?.id ?? null,
    projectDirectory: state.directory ?? ctx.location.directory,
    generatedAt: new Date().toISOString(),
    installedAt: state.installedAt,
    lastBackfill: state.lastBackfill,
    source: state.source ?? "db",
    sessions: sessions.map((session) => ({
      id: session.id,
      title: session.title ?? null,
      directory: session.directory ?? null,
      projectID: session.projectID ?? null,
      source: session.source ?? null,
      updated: session.time?.updated ?? null,
      imported: state.imported.includes(session.id),
      file: `${session.id}.json`,
    })),
  }
  await ensureDir(dir)
  await fs.writeFile(path.join(dir, "index.json"), JSON.stringify(payload, null, 2), "utf8")
}

async function runBackfillCore(
  ctx: PluginContext,
  config: NoitConfig,
  options: { force?: boolean } = {},
): Promise<BackfillSummary> {
  const state = await ensureState(ctx)
  state.projectID = state.projectID ?? config.backfill.projectId ?? ctx.location.project?.id
  state.directory = state.directory ?? config.backfill.directory ?? ctx.location.directory
  state.source = state.source ?? "db"
  const dir = rawSessionsDir(ctx)
  await ensureDir(dir)

  const all = await listProjectSessions(ctx, config)
  const eligible = all
  const pending = options.force ? eligible : eligible.filter((s) => !state.imported.includes(s.id))

  const concurrency = Math.max(1, Math.min(config.backfill.concurrency || 4, 8))
  let cursorIndex = 0
  let imported = 0
  let failed = 0

  const markImported = (id: string): void => {
    if (!state.imported.includes(id)) state.imported.push(id)
    delete state.failures[id]
  }

  const worker = async (): Promise<void> => {
    while (true) {
      const current = cursorIndex
      cursorIndex += 1
      if (current >= pending.length) return
      const session = pending[current]
      let info: SessionMessagesInfo
      try {
        info = await withBusyRetry(() => readMessagesDetailed(session.id))
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error)
        state.failures[session.id] = message
        failed += 1
        continue
      }
      if (!info.hasRows) continue
      if (info.messages.length === 0) {
        markImported(session.id)
        continue
      }
      try {
        await fs.writeFile(
          path.join(dir, `${session.id}.json`),
          JSON.stringify(redactRaw({ session, messages: info.messages }), null, 2),
          "utf8",
        )
        await fs.writeFile(
          path.join(dir, `${session.id}.jsonl`),
          info.messages.map((message) => JSON.stringify(redactRaw(message))).join("\n"),
          "utf8",
        )
        markImported(session.id)
        imported += 1
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error)
        state.failures[session.id] = message
        failed += 1
      }
    }
  }

  await Promise.all(
    Array.from({ length: Math.min(concurrency, Math.max(pending.length, 1)) }, () => worker()),
  )

  const latest = await loadState(ctx)
  if (latest) {
    for (const id of latest.imported) {
      if (!state.imported.includes(id)) state.imported.push(id)
    }
  }
  state.lastBackfill = new Date().toISOString()
  await saveState(ctx, state)
  await writeIndex(ctx, state, all)

  const scanned = all.length
  const skipped = scanned - imported - failed
  return {
    scanned,
    eligible: eligible.length,
    imported,
    failed,
    skipped,
    lastBackfill: state.lastBackfill,
    message:
      `Backfill (${config.project.name}, ${state.source}, ${state.directory}): ` +
      `scanned ${scanned}, eligible ${eligible.length}, imported ${imported}, ` +
      `skipped ${skipped}, failed ${failed}.`,
  }
}

async function withDatabaseOverride<T>(
  config: NoitConfig,
  task: () => Promise<T>,
): Promise<T> {
  const override = config.backfill.databasePath
  if (!override) return await task()
  const previous = process.env.OPENCODE_DB
  process.env.OPENCODE_DB = override
  try {
    return await task()
  } finally {
    if (previous === undefined) delete process.env.OPENCODE_DB
    else process.env.OPENCODE_DB = previous
  }
}

async function runBackfillOnce(
  ctx: PluginContext,
  config: NoitConfig,
  options: { force?: boolean } = {},
): Promise<BackfillSummary> {
  return await withDatabaseOverride(config, () => runBackfillCore(ctx, config, options))
}

let backfillQueue: Promise<unknown> = Promise.resolve()

function serialize<T>(task: () => Promise<T>): Promise<T> {
  const run = backfillQueue.then(task, task)
  backfillQueue = run.then(
    () => undefined,
    () => undefined,
  )
  return run
}

export function runBackfill(
  ctx: PluginContext,
  config: NoitConfig,
  options: { force?: boolean } = {},
): Promise<BackfillSummary> {
  return serialize(() => runBackfillOnce(ctx, config, options))
}
