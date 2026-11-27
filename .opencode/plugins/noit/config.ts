import { promises as fs } from "node:fs"
import path from "node:path"
import { fileURLToPath } from "node:url"
import type {
  BaseConfig,
  LocalConfig,
  ManualFields,
  NoitConfig,
  ParticipantInfo,
  PluginContext,
  ToolInfo,
} from "./types.ts"

const here = path.dirname(fileURLToPath(import.meta.url))

export const NOIT_DIR = ".noit"
export const PLUGIN_ID = "noit.ai-declaration"
export const PLUGIN_VERSION = "0.1.0"
export const UNKNOWN = "непознато"
export const NOT_APPLICABLE = "неприложимо"
export const BASE_SCHEMA = "noit-config/2"
export const LOCAL_SCHEMA = "noit-local/1"
export const MAX_PARTICIPANTS = 10

const SCOPE_DEPRECATION = "backfill.scope is always project-scoped"

const MANUAL_KEYS = [
  "name",
  "egn",
  "class",
  "school",
  "city",
  "projectNumber",
  "participants",
  "author",
  "organization",
  "role",
  "goals",
  "taskDescription",
  "documentation",
  "aiAsFunctionality",
  "model",
  "provider",
  "settings",
  "environment",
  "access",
  "serviceChanges",
  "ownWork",
  "verification",
  "limitations",
] as const

const PARTICIPANT_KEYS = ["name", "egn", "class", "school", "city", "projectNumber"] as const

const TOOL_ID_RE = /^A\d{2,}$/

const loggedDeprecations = new Set<string>()

export function noitRoot(ctx: PluginContext): string {
  return path.join(ctx.location.directory, NOIT_DIR)
}

export function exportDir(ctx: PluginContext): string {
  return path.join(noitRoot(ctx), "export", "AI")
}

export function rawSessionsDir(ctx: PluginContext): string {
  return path.join(noitRoot(ctx), "raw", "sessions")
}

export function recordsFile(ctx: PluginContext): string {
  return path.join(noitRoot(ctx), "records", "index.json")
}

export function logFile(ctx: PluginContext): string {
  return path.join(noitRoot(ctx), "ai-log.jsonl")
}

export function configFile(ctx: PluginContext): string {
  return path.join(noitRoot(ctx), "config.json")
}

export function localConfigFile(ctx: PluginContext): string {
  return path.join(noitRoot(ctx), "config.local.json")
}

export async function ensureDir(dir: string): Promise<void> {
  await fs.mkdir(dir, { recursive: true })
}

export async function pathExists(target: string): Promise<boolean> {
  try {
    await fs.access(target)
    return true
  } catch {
    return false
  }
}

function projectName(ctx: PluginContext): string {
  const directory = ctx.location.directory
  return path.basename(directory) || directory
}

function defaultManual(): ManualFields {
  return {
    name: UNKNOWN,
    egn: UNKNOWN,
    class: UNKNOWN,
    school: UNKNOWN,
    city: UNKNOWN,
    projectNumber: UNKNOWN,
    participants: UNKNOWN,
    author: UNKNOWN,
    organization: UNKNOWN,
    role: UNKNOWN,
    goals: UNKNOWN,
    taskDescription: UNKNOWN,
    documentation: UNKNOWN,
    aiAsFunctionality: UNKNOWN,
    model: UNKNOWN,
    provider: UNKNOWN,
    settings: UNKNOWN,
    environment: UNKNOWN,
    access: UNKNOWN,
    serviceChanges: UNKNOWN,
    ownWork: UNKNOWN,
    verification: UNKNOWN,
    limitations: UNKNOWN,
  }
}

export function defaultConfig(ctx: PluginContext): NoitConfig {
  return {
    schema: BASE_SCHEMA,
    project: { name: projectName(ctx), directory: ctx.location.directory },
    environment: {
      opencodeVersion: ctx.app.version,
      plugin: PLUGIN_ID,
      pluginVersion: PLUGIN_VERSION,
    },
    manual: defaultManual(),
    participants: [],
    export: {
      records: [
        { id: "B01", title: "Общ дневник на ИИ сесиите", task: UNKNOWN, sessionIDs: [] },
      ],
      includeKarta: true,
      includeDeclaration: true,
      public: false,
      tools: [],
    },
    backfill: {
      sync: false,
      limit: 100,
      concurrency: 4,
      scope: "project",
      projectId: ctx.location.project.id,
      directory: ctx.location.directory,
    },
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value)
}

function sanitizeParticipants(
  input: unknown,
  source: string,
  ignored: string[],
): ParticipantInfo[] {
  if (input === undefined) return []
  if (!Array.isArray(input)) {
    ignored.push(source)
    return []
  }
  const out: ParticipantInfo[] = []
  input.forEach((raw, index) => {
    if (out.length >= MAX_PARTICIPANTS) {
      ignored.push(`${source}[${index}]`)
      return
    }
    if (!isRecord(raw)) {
      ignored.push(`${source}[${index}]`)
      return
    }
    const participant: ParticipantInfo = {
      name: "",
      egn: "",
      class: "",
      school: "",
      city: "",
      projectNumber: "",
    }
    let filled = false
    for (const key of PARTICIPANT_KEYS) {
      const value = raw[key]
      if (typeof value === "string") {
        const trimmed = value.trim()
        participant[key] = trimmed
        if (trimmed.length > 0) filled = true
      } else if (value !== undefined && value !== null) {
        ignored.push(`${source}[${index}].${key}`)
      }
    }
    if (filled) out.push(participant)
    else ignored.push(`${source}[${index}]`)
  })
  return out
}

function sanitizeTools(input: unknown, source: string, ignored: string[]): ToolInfo[] | undefined {
  if (input === undefined) return undefined
  if (!Array.isArray(input)) {
    ignored.push(source)
    return undefined
  }
  const tools: ToolInfo[] = []
  const seen = new Set<string>()
  input.forEach((raw, index) => {
    const label = `${source}[${index}]`
    if (!isRecord(raw)) {
      ignored.push(label)
      return
    }
    const id = typeof raw.id === "string" ? raw.id.trim().toUpperCase() : ""
    if (!TOOL_ID_RE.test(id) || id === "A01") {
      ignored.push(`${label}.id`)
      return
    }
    if (seen.has(id)) {
      ignored.push(`${label}.id`)
      return
    }
    const name = typeof raw.name === "string" ? raw.name.trim() : ""
    if (name.length === 0) {
      ignored.push(`${label}.name`)
      return
    }
    const tool: ToolInfo = { id, name }
    if (raw.provider !== undefined) {
      const provider = typeof raw.provider === "string" ? raw.provider.trim() : ""
      if (provider.length > 0) tool.provider = provider
      else ignored.push(`${label}.provider`)
    }
    if (raw.notes !== undefined) {
      const notes = typeof raw.notes === "string" ? raw.notes.trim() : ""
      if (notes.length > 0) tool.notes = notes
      else ignored.push(`${label}.notes`)
    }
    seen.add(id)
    tools.push(tool)
  })
  return tools
}

function deriveParticipants(
  manual: ManualFields,
  participants: ParticipantInfo[] | undefined,
): ParticipantInfo[] {
  const provided = sanitizeParticipants(participants, "participants", [])
  if (provided.length > 0) return provided
  return [
    {
      name: manual.name,
      egn: manual.egn,
      class: manual.class,
      school: manual.school,
      city: manual.city,
      projectNumber: manual.projectNumber,
    },
  ]
}

function sanitizeLayer(
  input: unknown,
  source: string,
  ignored: string[],
  deprecations: string[],
): Partial<NoitConfig> {
  const overlay: Partial<NoitConfig> = {}
  if (input === undefined) return overlay
  if (!isRecord(input)) {
    ignored.push(source.length > 0 ? source : "options")
    return overlay
  }

  const label = (key: string): string => (source.length > 0 ? `${source}:${key}` : key)
  const text = (value: unknown): string | undefined =>
    typeof value === "string" && value.trim().length > 0 ? value : undefined
  const bool = (value: unknown): boolean | undefined =>
    typeof value === "boolean" ? value : undefined
  const num = (value: unknown): number | undefined =>
    typeof value === "number" && Number.isFinite(value) ? value : undefined

  const schema = text(input.schema)
  if (schema !== undefined) overlay.schema = schema

  if (input.project !== undefined) {
    if (!isRecord(input.project)) ignored.push(label("project"))
    else {
      const project: Record<string, string> = {}
      if (input.project.name !== undefined) {
        const value = text(input.project.name)
        if (value !== undefined) project.name = value
        else ignored.push(label("project.name"))
      }
      if (input.project.directory !== undefined) {
        const value = text(input.project.directory)
        if (value !== undefined) project.directory = value
        else ignored.push(label("project.directory"))
      }
      if (Object.keys(project).length > 0) overlay.project = project as NoitConfig["project"]
    }
  }

  if (input.environment !== undefined) {
    if (!isRecord(input.environment)) ignored.push(label("environment"))
    else {
      const environment: Record<string, string> = {}
      for (const key of ["opencodeVersion", "plugin", "pluginVersion"]) {
        const raw = input.environment[key]
        if (raw === undefined) continue
        const value = text(raw)
        if (value !== undefined) environment[key] = value
        else ignored.push(label(`environment.${key}`))
      }
      if (Object.keys(environment).length > 0) {
        overlay.environment = environment as NoitConfig["environment"]
      }
    }
  }

  if (input.manual !== undefined) {
    if (!isRecord(input.manual)) ignored.push(label("manual"))
    else {
      const manual: Record<string, string> = {}
      for (const key of MANUAL_KEYS) {
        const raw = input.manual[key]
        if (raw === undefined) continue
        const value = text(raw)
        if (value !== undefined) manual[key] = value
        else ignored.push(label(`manual.${key}`))
      }
      if (Object.keys(manual).length > 0) overlay.manual = manual as ManualFields
    }
  }

  if (input.participants !== undefined) {
    const participants = sanitizeParticipants(input.participants, label("participants"), ignored)
    if (participants.length > 0) overlay.participants = participants
  }

  if (input.export !== undefined) {
    if (!isRecord(input.export)) ignored.push(label("export"))
    else {
      const exported: Partial<NoitConfig["export"]> = {}
      if (input.export.includeKarta !== undefined) {
        const value = bool(input.export.includeKarta)
        if (value !== undefined) exported.includeKarta = value
        else ignored.push(label("export.includeKarta"))
      }
      if (input.export.includeDeclaration !== undefined) {
        const value = bool(input.export.includeDeclaration)
        if (value !== undefined) exported.includeDeclaration = value
        else ignored.push(label("export.includeDeclaration"))
      }
      if (input.export.public !== undefined) {
        const value = bool(input.export.public)
        if (value !== undefined) exported.public = value
        else ignored.push(label("export.public"))
      }
      if (input.export.tools !== undefined) {
        const tools = sanitizeTools(input.export.tools, label("export.tools"), ignored)
        if (tools !== undefined) exported.tools = tools
      }
      if (input.export.records !== undefined) {
        if (!Array.isArray(input.export.records)) ignored.push(label("export.records"))
        else {
          const records: NoitConfig["export"]["records"] = []
          input.export.records.forEach((raw, index) => {
            if (!isRecord(raw)) {
              ignored.push(label(`export.records[${index}]`))
              return
            }
            const id = text(raw.id)
            const title = text(raw.title)
            const sessionIDs = Array.isArray(raw.sessionIDs)
              ? raw.sessionIDs.filter(
                  (value): value is string => typeof value === "string" && value.length > 0,
                )
              : []
            if (id === undefined || title === undefined) {
              ignored.push(label(`export.records[${index}]`))
              return
            }
            const task = text(raw.task)
            records.push(task !== undefined ? { id, title, task, sessionIDs } : { id, title, sessionIDs })
          })
          if (records.length > 0) exported.records = records
          else ignored.push(label("export.records"))
        }
      }
      if (Object.keys(exported).length > 0) overlay.export = exported as NoitConfig["export"]
    }
  }

  if (input.backfill !== undefined) {
    if (!isRecord(input.backfill)) ignored.push(label("backfill"))
    else {
      const backfill: Partial<NoitConfig["backfill"]> = {}
      if (input.backfill.sync !== undefined) {
        const value = bool(input.backfill.sync)
        if (value !== undefined) backfill.sync = value
        else ignored.push(label("backfill.sync"))
      }
      if (input.backfill.limit !== undefined) {
        const value = num(input.backfill.limit)
        if (value !== undefined) backfill.limit = value
        else ignored.push(label("backfill.limit"))
      }
      if (input.backfill.concurrency !== undefined) {
        const value = num(input.backfill.concurrency)
        if (value !== undefined) backfill.concurrency = value
        else ignored.push(label("backfill.concurrency"))
      }
      if (input.backfill.scope !== undefined) deprecations.push(SCOPE_DEPRECATION)
      if (input.backfill.projectId !== undefined) {
        const value = text(input.backfill.projectId)
        if (value !== undefined) backfill.projectId = value
        else ignored.push(label("backfill.projectId"))
      }
      if (input.backfill.directory !== undefined) {
        const value = text(input.backfill.directory)
        if (value !== undefined) backfill.directory = value
        else ignored.push(label("backfill.directory"))
      }
      if (input.backfill.databasePath !== undefined) {
        const value = text(input.backfill.databasePath)
        if (value !== undefined) backfill.databasePath = value
        else ignored.push(label("backfill.databasePath"))
      }
      if (Object.keys(backfill).length > 0) overlay.backfill = backfill as NoitConfig["backfill"]
    }
  }

  return overlay
}

/**
 * Precedence for configuration (highest wins, evaluated key by key):
 * 1. `opencode.json` plugin options (`ctx.options`) — values explicitly provided by the user.
 * 2. `.noit/config.local.json` — the local, gitignored PII overlay (`noit-local/1`).
 * 3. `.noit/config.json` — the committable base configuration (`noit-config/2`).
 * 4. `config.template.json` / `config.local.template.json` / built-in defaults.
 * A malformed value is ignored (the lower layer is kept) and reported to `.noit/ai-log.jsonl`.
 * `export.public` and `export.tools` follow the same order: a higher layer replaces the
 * whole `tools` array (so a per-machine tool entry or an empty list can be set locally)
 * and overrides the `public` flag.
 */
function mergeLayers(...layers: (Partial<NoitConfig> | undefined)[]): Partial<NoitConfig> {
  const merged: Partial<NoitConfig> = {}
  for (const layer of layers) {
    if (!layer) continue
    if (layer.schema !== undefined) merged.schema = layer.schema
    if (layer.project !== undefined) merged.project = { ...(merged.project ?? {}), ...layer.project }
    if (layer.environment !== undefined) {
      merged.environment = { ...(merged.environment ?? {}), ...layer.environment }
    }
    if (layer.manual !== undefined) merged.manual = { ...(merged.manual ?? {}), ...layer.manual }
    if (layer.participants !== undefined) merged.participants = layer.participants
    if (layer.export !== undefined) merged.export = { ...(merged.export ?? {}), ...layer.export }
    if (layer.backfill !== undefined) merged.backfill = { ...(merged.backfill ?? {}), ...layer.backfill }
  }
  return merged
}

export function normalizeConfig(input: Partial<NoitConfig>, ctx: PluginContext): NoitConfig {
  const base = defaultConfig(ctx)
  const records = input.export?.records
  const manual: ManualFields = { ...base.manual, ...(input.manual ?? {}) }
  return {
    schema: input.schema ?? base.schema,
    project: {
      name: input.project?.name || base.project.name,
      directory: input.project?.directory || base.project.directory,
    },
    environment: {
      opencodeVersion: input.environment?.opencodeVersion || base.environment.opencodeVersion,
      plugin: input.environment?.plugin || base.environment.plugin,
      pluginVersion: input.environment?.pluginVersion || base.environment.pluginVersion,
    },
    manual,
    participants: deriveParticipants(manual, input.participants),
    export: {
      records: records && records.length > 0 ? records : base.export.records,
      includeKarta: input.export?.includeKarta ?? base.export.includeKarta,
      includeDeclaration: input.export?.includeDeclaration ?? base.export.includeDeclaration,
      public: input.export?.public ?? base.export.public,
      tools: input.export?.tools ?? base.export.tools ?? [],
    },
    backfill: {
      ...base.backfill,
      ...(input.backfill ?? {}),
      scope: "project",
      projectId: input.backfill?.projectId ?? base.backfill.projectId,
      directory: input.backfill?.directory ?? base.backfill.directory,
    },
  }
}

function renderTemplate(template: string, ctx: PluginContext): string {
  return template
    .replaceAll("{{PROJECT_NAME}}", projectName(ctx))
    .replaceAll("{{PROJECT_DIRECTORY}}", ctx.location.directory)
    .replaceAll("{{PROJECT_ID}}", ctx.location.project.id)
    .replaceAll("{{OPENCODE_VERSION}}", ctx.app.version)
    .replaceAll("{{PLUGIN_ID}}", PLUGIN_ID)
    .replaceAll("{{PLUGIN_VERSION}}", PLUGIN_VERSION)
}

async function readTemplate(ctx: PluginContext): Promise<Partial<NoitConfig>> {
  const templatePath = path.join(here, "config.template.json")
  try {
    const rendered = renderTemplate(await fs.readFile(templatePath, "utf8"), ctx)
    return JSON.parse(rendered) as Partial<NoitConfig>
  } catch {
    return {}
  }
}

async function readLocalTemplate(ctx: PluginContext): Promise<Partial<LocalConfig>> {
  const templatePath = path.join(here, "config.local.template.json")
  try {
    const rendered = renderTemplate(await fs.readFile(templatePath, "utf8"), ctx)
    return JSON.parse(rendered) as Partial<LocalConfig>
  } catch {
    return {}
  }
}

async function appendLogEntry(ctx: PluginContext, text: string): Promise<void> {
  try {
    const file = logFile(ctx)
    await ensureDir(path.dirname(file))
    const entry = {
      ts: new Date().toISOString(),
      type: "session",
      status: "pending",
      text,
    }
    await fs.appendFile(file, `${JSON.stringify(entry)}\n`, "utf8")
  } catch {}
}

async function logIgnored(ctx: PluginContext, ignored: string[]): Promise<void> {
  if (ignored.length === 0) return
  await appendLogEntry(ctx, `ignored malformed values: ${ignored.join(", ")}`)
}

async function logDeprecations(ctx: PluginContext, messages: string[]): Promise<void> {
  const fresh: string[] = []
  for (const message of messages) {
    if (loggedDeprecations.has(message) || fresh.includes(message)) continue
    fresh.push(message)
  }
  if (fresh.length === 0) return
  for (const message of fresh) loggedDeprecations.add(message)
  await appendLogEntry(ctx, `ignored: ${fresh.join("; ")}`)
}

interface LocalMigration {
  manual: Record<string, string>
  participants: ParticipantInfo[]
  base: Record<string, unknown>
  ignored: string[]
  backup: string
}

async function migrateBaseFile(
  ctx: PluginContext,
  baseFile: string,
  rawText: string,
  record: Record<string, unknown>,
): Promise<LocalMigration | undefined> {
  const manualRaw = record.manual
  const participantsRaw = record.participants
  const hasManual = isRecord(manualRaw) && Object.keys(manualRaw).length > 0
  const hasParticipants = Array.isArray(participantsRaw) && participantsRaw.length > 0
  if (!hasManual && !hasParticipants) return undefined

  const ignored: string[] = []
  const manual: Record<string, string> = {}
  if (isRecord(manualRaw)) {
    for (const [key, value] of Object.entries(manualRaw)) {
      if (typeof value === "string") manual[key] = value
      else if (value !== undefined && value !== null) ignored.push(`config.json:manual.${key}`)
    }
  }
  const participants = sanitizeParticipants(participantsRaw, "config.json:participants", ignored)

  const backup = path.join(
    noitRoot(ctx),
    `config.backup.${new Date().toISOString().replace(/[:.]/g, "-")}.json`,
  )
  await fs.writeFile(backup, rawText, "utf8")

  const base: Record<string, unknown> = { ...record }
  delete base.manual
  delete base.participants
  base.schema = BASE_SCHEMA
  await fs.writeFile(baseFile, JSON.stringify(base, null, 2), "utf8")
  await appendLogEntry(
    ctx,
    `migrated personal data from config.json to config.local.json (backup: ${path.basename(backup)})`,
  )

  return { manual, participants, base, ignored, backup }
}

function baseFileContent(config: NoitConfig): BaseConfig {
  const backfill: NoitConfig["backfill"] = {
    sync: config.backfill.sync,
    limit: config.backfill.limit,
    concurrency: config.backfill.concurrency,
  }
  if (config.backfill.projectId !== undefined) backfill.projectId = config.backfill.projectId
  if (config.backfill.directory !== undefined) backfill.directory = config.backfill.directory
  if (config.backfill.databasePath !== undefined) backfill.databasePath = config.backfill.databasePath
  return {
    schema: BASE_SCHEMA,
    project: config.project,
    environment: config.environment,
    export: config.export,
    backfill,
  }
}

export async function loadConfig(ctx: PluginContext): Promise<NoitConfig> {
  const root = noitRoot(ctx)
  await ensureDir(root)
  const baseFile = configFile(ctx)
  const localFile = localConfigFile(ctx)
  const ignored: string[] = []
  const deprecations: string[] = []

  const optionsOverlay = sanitizeLayer(
    (ctx as unknown as { options?: unknown }).options,
    "",
    ignored,
    deprecations,
  )

  let baseRecord: Record<string, unknown> = {}
  let baseExists = false
  let baseText: string | undefined
  if (await pathExists(baseFile)) {
    baseExists = true
    try {
      baseText = await fs.readFile(baseFile, "utf8")
      const parsed: unknown = JSON.parse(baseText)
      if (isRecord(parsed)) baseRecord = parsed
      else ignored.push("config.json")
    } catch {
      ignored.push("config.json")
      baseText = undefined
    }
  } else {
    baseRecord = (await readTemplate(ctx)) as Record<string, unknown>
  }

  let migration: LocalMigration | undefined
  if (baseExists && baseText !== undefined && Object.keys(baseRecord).length > 0) {
    migration = await migrateBaseFile(ctx, baseFile, baseText, baseRecord)
    if (migration) {
      baseRecord = migration.base
      ignored.push(...migration.ignored)
    }
  }

  let localRecord: Record<string, unknown> = {}
  let localExists = false
  if (await pathExists(localFile)) {
    localExists = true
    try {
      const parsed: unknown = JSON.parse(await fs.readFile(localFile, "utf8"))
      if (isRecord(parsed)) localRecord = parsed
      else ignored.push("config.local.json")
    } catch {
      ignored.push("config.local.json")
    }
  } else {
    localRecord = (await readLocalTemplate(ctx)) as Record<string, unknown>
  }

  const baseLayer = sanitizeLayer(baseRecord, "config.json", ignored, deprecations)
  const localLayer = sanitizeLayer(localRecord, "config.local.json", ignored, deprecations)
  delete localLayer.schema

  if (migration) {
    const manual: Record<string, string> = {
      ...((localLayer.manual ?? {}) as Record<string, string>),
    }
    for (const key of MANUAL_KEYS) {
      const moved = migration.manual[key]
      if (moved !== undefined && isUnknown(manual[key])) manual[key] = moved
    }
    for (const [key, value] of Object.entries(migration.manual)) {
      if (manual[key] === undefined) manual[key] = value
    }
    localLayer.manual = manual as ManualFields
    if ((localLayer.participants?.length ?? 0) === 0 && migration.participants.length > 0) {
      localLayer.participants = migration.participants
    }
  }

  if (!localExists || migration) {
    const manual: Record<string, string> = {}
    for (const key of MANUAL_KEYS) {
      const value = (localLayer.manual as Record<string, string> | undefined)?.[key]
      manual[key] = typeof value === "string" ? value : UNKNOWN
    }
    for (const [key, value] of Object.entries(localLayer.manual ?? {})) {
      if (manual[key] === undefined && typeof value === "string") manual[key] = value
    }
    const content: LocalConfig = {
      schema: LOCAL_SCHEMA,
      manual: manual as unknown as ManualFields,
      participants: localLayer.participants ?? [],
    }
    await fs.writeFile(localFile, JSON.stringify(content, null, 2), "utf8")
  }

  const merged = mergeLayers(baseLayer, localLayer, optionsOverlay)
  const config = normalizeConfig(merged, ctx)

  if (!baseExists) {
    await fs.writeFile(baseFile, JSON.stringify(baseFileContent(config), null, 2), "utf8")
  }

  await logIgnored(ctx, ignored)
  await logDeprecations(ctx, deprecations)
  return config
}

export function isUnknown(value: unknown): boolean {
  if (typeof value !== "string") return true
  const trimmed = value.trim()
  return trimmed.length === 0 || trimmed === UNKNOWN || trimmed === NOT_APPLICABLE
}
