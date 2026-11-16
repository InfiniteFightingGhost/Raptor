import { promises as fs } from "node:fs"
import path from "node:path"
import { Plugin } from "@opencode/plugin"
import type { LogEntry, PluginContext, ToolStatus } from "./types.ts"
import {
  PLUGIN_ID,
  PLUGIN_VERSION,
  UNKNOWN,
  ensureDir,
  exportDir,
  loadConfig,
  logFile,
  rawSessionsDir,
} from "./config.ts"
import { ensureState, loadState, runBackfill } from "./backfill.ts"
import {
  findSession,
  normalizeDirectory,
  projectIdsForDirectory,
  readMessagesDetailed,
} from "./db.ts"
import {
  nextRecordId,
  readLog,
  readRecords,
  runExport,
  writeRecords,
} from "./export.ts"
import { runCheck } from "./check.ts"
import { redactRaw } from "./redact.ts"

const SESSION_FILE = /^ses_[A-Za-z0-9]+\.json$/

function nowIso(): string {
  return new Date().toISOString()
}

async function appendLog(ctx: PluginContext, entry: LogEntry): Promise<void> {
  try {
    const file = logFile(ctx)
    await ensureDir(path.dirname(file))
    await fs.appendFile(file, `${JSON.stringify(redactRaw(entry))}\n`, "utf8")
  } catch {}
}

function extractMessages(result: unknown): unknown[] | undefined {
  if (Array.isArray(result)) return result
  if (!result || typeof result !== "object") return undefined
  const record = result as { data?: unknown; messages?: unknown }
  if (Array.isArray(record.messages)) return record.messages
  if (Array.isArray(record.data)) return record.data
  if (record.data && typeof record.data === "object") {
    const nested = (record.data as { messages?: unknown }).messages
    if (Array.isArray(nested)) return nested
  }
  return undefined
}

function sessionInScope(ctx: PluginContext, sessionID: string): boolean {
  const directory = ctx.location.directory
  const target = normalizeDirectory(directory)
  if (target.length === 0) return false
  const ids = new Set<string>()
  if (ctx.location.project?.id) ids.add(ctx.location.project.id)
  try {
    for (const id of projectIdsForDirectory(directory)) ids.add(id)
  } catch {}
  try {
    const session = findSession(sessionID)
    if (!session) return false
    if (normalizeDirectory(session.directory ?? "") !== target) return false
    return ids.has(String(session.projectID ?? ""))
  } catch {
    return false
  }
}

async function captureSession(ctx: PluginContext, sessionID: string): Promise<void> {
  try {
    if (!sessionInScope(ctx, sessionID)) return
    let messages: unknown[] | undefined
    try {
      const info = readMessagesDetailed(sessionID)
      if (info.hasRows) messages = info.messages
    } catch {}
    if (messages === undefined) {
      const api = (ctx.session as unknown as { context?: unknown }).context
      if (typeof api === "function") {
        try {
          const result = await (
            api as (input: unknown) => Promise<unknown>
          ).call(ctx.session, { sessionID })
          messages = extractMessages(result)
        } catch {}
      }
    }
    if (!messages || messages.length === 0) return
    const dir = rawSessionsDir(ctx)
    await ensureDir(dir)
    await fs.writeFile(
      path.join(dir, `${sessionID}.jsonl`),
      messages.map((message) => JSON.stringify(redactRaw(message))).join("\n"),
      "utf8",
    )
    await fs.writeFile(
      path.join(dir, `${sessionID}.json`),
      JSON.stringify(redactRaw({ sessionID, capturedAt: nowIso(), messages }), null, 2),
      "utf8",
    )
    await appendLog(ctx, { ts: nowIso(), type: "session", sessionID, status: "completed" })
  } catch {}
}

async function countSessionFiles(dir: string): Promise<number> {
  try {
    const names = await fs.readdir(dir)
    return names.filter((name) => SESSION_FILE.test(name)).length
  } catch {
    return 0
  }
}

async function reply(ctx: PluginContext, sessionID: string, text: string): Promise<void> {
  try {
    await ctx.session.prompt({ sessionID, text })
  } catch {}
}

function toolInput(schema: Record<string, unknown>): any {
  return schema
}

function toolResult(content: string): { content: string } {
  return { content }
}

function errorResult(error: unknown): { content: string } {
  const message = error instanceof Error ? error.message : String(error)
  return { content: `Грешка в NOIT: ${message}` }
}

export default Plugin.define({
  id: PLUGIN_ID,
  async setup(ctx) {
    const config = await loadConfig(ctx)
    await appendLog(ctx, {
      ts: nowIso(),
      type: "session",
      status: "pending",
      text: `setup ${PLUGIN_ID}@${PLUGIN_VERSION} for ${config.project.name}`,
    })

    const state = await loadState(ctx)
    if (!state) {
      await ensureState(ctx)
    }

    try {
      const base = (ctx as any).location?.directory ?? (ctx as any).directory ?? "."
      const groups = [
        "session",
        "tool",
        "event",
        "storage",
        "vcs",
        "app",
        "location",
        "options",
        "agent",
        "aisdk",
        "command",
        "experimental",
        "generate",
        "model",
        "provider",
        "integration",
        "mcp",
        "permission",
        "plugin",
        "reference",
        "rpc",
        "skill",
        "websearch",
        "worktree",
        "shell",
      ]
      const groupKeys: Record<string, string[]> = {}
      for (const name of groups) {
        try {
          groupKeys[name] = Object.keys((ctx as any)[name] ?? {})
        } catch {
          groupKeys[name] = ["<error>"]
        }
      }
      const debug = {
        ctxKeys: Object.keys(ctx),
        ctxProto: Object.getOwnPropertyNames(Object.getPrototypeOf(ctx)),
        sessionKeys: Object.keys((ctx as any).session ?? {}),
        sessionProto: Object.getOwnPropertyNames(Object.getPrototypeOf((ctx as any).session ?? {})),
        groupKeys,
        location: {
          directory: (ctx as any).location?.directory,
          workspaceID: (ctx as any).location?.workspaceID,
          project: (ctx as any).location?.project,
        },
        options: (ctx as any).options,
      }
      await fs.mkdir(path.join(base, ".noit"), { recursive: true })
      await fs.writeFile(path.join(base, ".noit", "debug-ctx.json"), JSON.stringify(debug, null, 2), "utf8")
    } catch {}
    const tracked = new Set<Promise<unknown>>()
    const track = <T>(promise: Promise<T>): Promise<T> => {
      tracked.add(promise)
      void promise.then(
        () => tracked.delete(promise),
        () => tracked.delete(promise),
      )
      return promise
    }
    let reconcileRunning: Promise<void> | null = null
    let reconcileQueued = false
    const startReconcile = (): Promise<void> => {
      reconcileQueued = true
      if (reconcileRunning) return reconcileRunning
      const promise = (async () => {
        try {
          while (reconcileQueued) {
            reconcileQueued = false
            try {
              await runBackfill(ctx, config, { force: config.backfill.sync })
            } catch {}
          }
        } finally {
          reconcileRunning = null
        }
      })()
      reconcileRunning = promise
      return track(promise)
    }

    const flushSession = async (sessionID: string): Promise<void> => {
      await captureSession(ctx, sessionID)
      await startReconcile()
    }

    const started = startReconcile()
    void started.catch(() => {})

    try {
      await ctx.session.hook("experimental.idle", async (event) => {
        try {
          const sessionID = (event as { sessionID?: string } | undefined)?.sessionID
          if (sessionID) await flushSession(sessionID)
        } catch {}
      })
    } catch {}

    await ctx.session.hook("prompt", async (event) => {
      await appendLog(ctx, {
        ts: nowIso(),
        type: "prompt",
        sessionID: event.sessionID,
        text: event.prompt?.text ?? "",
      })
    })

    await ctx.tool.hook("execute.before", async (event) => {
      await appendLog(ctx, {
        ts: nowIso(),
        type: "tool",
        sessionID: event.sessionID,
        tool: event.tool,
        input: event.input,
        status: "pending",
      })
    })

    await ctx.tool.hook("execute.after", async (event) => {
      const status: ToolStatus = event.status === "completed" ? "completed" : "error"
      const entry: LogEntry = {
        ts: nowIso(),
        type: "tool",
        sessionID: event.sessionID,
        tool: event.tool,
        input: event.input,
        status,
      }
      if (event.status === "completed") entry.result = event.result
      else entry.error = event.error?.message ?? String(event.error)
      await appendLog(ctx, entry)
    })

    const controller = new AbortController()
    const terminalTypes = new Set([
      "session.idle",
      "session.execution.succeeded",
      "session.execution.failed",
      "session.execution.interrupted",
    ])
    void (async () => {
      try {
        for await (const event of ctx.event.subscribe({
          types: [...terminalTypes] as never,
          signal: controller.signal,
        })) {
          if (!terminalTypes.has(event.type)) continue
          const sessionID = (event as { data?: { sessionID?: string } }).data?.sessionID
          if (!sessionID) continue
          await flushSession(sessionID)
        }
      } catch {}
    })()

    await ctx.tool.transform((editor) => {
      editor.namespace({
        name: "noit",
        description: "NOIT AI Declaration: local AI usage evidence and export tools",
      })

      editor.add({
        name: "export",
        description:
          "Export the NOIT AI declaration as Markdown into .noit/export/AI/ (requests, outputs, inputs, changes, tests, karta-A01.md, records-B01.md, section-V.md, declaration.md, checklist.md, manifest.json, redaction-report.json).",
        input: toolInput({
          type: "object",
          properties: { force: { type: "boolean", description: "Overwrite existing export files." } },
          additionalProperties: false,
        }),
        async execute(raw) {
          try {
            const current = await loadConfig(ctx)
            const summary = await runExport(ctx, current, { force: Boolean((raw as { force?: boolean })?.force) })
            return toolResult(summary.message)
          } catch (error) {
            return errorResult(error)
          }
        },
      })

      editor.add({
        name: "check",
        description:
          "Run the conservative 10-point NOIT checklist over the exported artifacts and write checklist.md.",
        input: toolInput({
          type: "object",
          properties: {
            strict: {
              type: "boolean",
              description:
                "Strict mode affects checklist wording only; it never changes pass/fail.",
            },
          },
          additionalProperties: false,
        }),
        async execute(raw) {
          try {
            const current = await loadConfig(ctx)
            const result = await runCheck(ctx, current, { strict: Boolean((raw as { strict?: boolean })?.strict) })
            const lines = result.items.map((item) => `${item.passed ? "УСПЕХ" : "НЕУСПЕХ"} ${item.title}: ${item.detail}`)
            return toolResult([result.message, ...lines].join("\n"))
          } catch (error) {
            return errorResult(error)
          }
        },
      })

      editor.add({
        name: "record_start",
        description:
          "Start a named B record capturing the current session for the NOIT declaration export.",
        input: toolInput({
          type: "object",
          properties: {
            title: { type: "string", description: "Record title." },
            task: { type: "string", description: "Task description." },
          },
          required: ["title", "task"],
          additionalProperties: false,
        }),
        async execute(raw, context) {
          try {
            const input = raw as { title: string; task: string }
            const records = await readRecords(ctx)
            const entry = {
              id: nextRecordId(records),
              title: input.title,
              task: input.task,
              status: "open" as const,
              startedAt: nowIso(),
              endedAt: null,
              sessionIDs: context?.sessionID ? [context.sessionID] : [],
            }
            records.push(entry)
            await writeRecords(ctx, records)
            return toolResult(`Запис ${entry.id} стартиран: ${entry.title}`)
          } catch (error) {
            return errorResult(error)
          }
        },
      })

      editor.add({
        name: "record_end",
        description: "Close the most recently opened B record and optionally rename it.",
        input: toolInput({
          type: "object",
          properties: { title: { type: "string", description: "Optional new title." } },
          additionalProperties: false,
        }),
        async execute(raw) {
          try {
            const records = await readRecords(ctx)
            const open = [...records].reverse().find((record) => record.status === "open")
            if (!open) return toolResult("Няма отворен запис.")
            open.status = "closed"
            open.endedAt = nowIso()
            const title = (raw as { title?: string })?.title
            if (title) open.title = title
            await writeRecords(ctx, records)
            return toolResult(`Запис ${open.id} затворен: ${open.title}`)
          } catch (error) {
            return errorResult(error)
          }
        },
      })

      editor.add({
        name: "status",
        description: "Report NOIT configuration, backfill state, captured sessions and records.",
        input: toolInput({ type: "object", properties: {}, additionalProperties: false }),
        async execute() {
          try {
            const current = await loadConfig(ctx)
            const backfill = await loadState(ctx)
            const sessions = await countSessionFiles(rawSessionsDir(ctx))
            const log = await readLog(ctx)
            const records = await readRecords(ctx)
            const lines = [
              `Проект: ${current.project.name} (${current.project.directory})`,
              `ID на проекта: ${current.backfill.projectId ?? UNKNOWN}`,
              `Обхват на backfill: ${current.backfill.scope}, източник: ${backfill?.source ?? UNKNOWN}`,
              `opencode: ${current.environment.opencodeVersion}, плъгин: ${current.environment.plugin}@${current.environment.pluginVersion}`,
              `Backfill: installedAt=${backfill?.installedAt ?? UNKNOWN}, lastBackfill=${backfill?.lastBackfill ?? UNKNOWN}, imported=${backfill?.imported.length ?? 0}`,
              `Сесии в .noit/raw/sessions: ${sessions}`,
              `Записи в дневника: ${log.length}`,
              `Записи (дневници): ${records.length}`,
              `Директория за експорт: ${exportDir(ctx)}`,
            ]
            return toolResult(lines.join("\n"))
          } catch (error) {
            return errorResult(error)
          }
        },
      })

      editor.add({
        name: "backfill",
        description:
          "Import this project's sessions into .noit/raw/sessions for the NOIT declaration.",
        input: toolInput({ type: "object", properties: {}, additionalProperties: false }),
        async execute() {
          try {
            const current = await loadConfig(ctx)
            const summary = await runBackfill(ctx, current, { force: true })
            return toolResult(summary.message)
          } catch (error) {
            return errorResult(error)
          }
        },
      })
    })

    await ctx.command.transform((editor) => {
      editor.add({
        name: "noit-export",
        description: "Export the NOIT AI declaration (Markdown)",
        execute: async ({ sessionID }) => {
          try {
            const current = await loadConfig(ctx)
            const summary = await runExport(ctx, current, {})
            await reply(ctx, sessionID, summary.message)
          } catch (error) {
            await reply(ctx, sessionID, errorResult(error).content)
          }
        },
      })

      editor.add({
        name: "noit-check",
        description: "Run the NOIT checklist",
        execute: async ({ sessionID }) => {
          try {
            const current = await loadConfig(ctx)
            const result = await runCheck(ctx, current, {})
            await reply(ctx, sessionID, result.message)
          } catch (error) {
            await reply(ctx, sessionID, errorResult(error).content)
          }
        },
      })

      editor.add({
        name: "noit-record",
        description: "Start a NOIT record (first line: title, rest: task)",
        execute: async ({ sessionID, prompt }) => {
          try {
            const text = prompt?.text ?? ""
            const lines = text.split("\n")
            const title = (lines.shift() ?? "").trim() || "Запис"
            const task = lines.join("\n").trim()
            const records = await readRecords(ctx)
            const entry = {
              id: nextRecordId(records),
              title,
              task,
              status: "open" as const,
              startedAt: nowIso(),
              endedAt: null,
              sessionIDs: [sessionID],
            }
            records.push(entry)
            await writeRecords(ctx, records)
            await reply(ctx, sessionID, `Запис ${entry.id} стартиран: ${entry.title}`)
          } catch (error) {
            await reply(ctx, sessionID, errorResult(error).content)
          }
        },
      })

      editor.add({
        name: "noit-status",
        description: "Show the NOIT status",
        execute: async ({ sessionID }) => {
          try {
            const current = await loadConfig(ctx)
            const backfill = await loadState(ctx)
            const sessions = await countSessionFiles(rawSessionsDir(ctx))
            await reply(
              ctx,
              sessionID,
              `NOIT: ${current.project.name} | сесии ${sessions} | lastBackfill ${backfill?.lastBackfill ?? UNKNOWN}`,
            )
          } catch (error) {
            await reply(ctx, sessionID, errorResult(error).content)
          }
        },
      })

      editor.add({
        name: "noit-backfill",
        description: "Import this project's sessions for NOIT",
        execute: async ({ sessionID }) => {
          try {
            const current = await loadConfig(ctx)
            const summary = await runBackfill(ctx, current, { force: true })
            await reply(ctx, sessionID, summary.message)
          } catch (error) {
            await reply(ctx, sessionID, errorResult(error).content)
          }
        },
      })
    })

    return async () => {
      controller.abort()
      let guard = 0
      while (tracked.size > 0 && guard < 100) {
        guard += 1
        await Promise.allSettled([...tracked])
      }
      try {
        await runBackfill(ctx, config, { force: config.backfill.sync })
      } catch {}
    }
  },
})
