import type { Plugin } from "@opencode/plugin"

export type PluginContext = Plugin.Context

export interface ProjectInfo {
  name: string
  directory: string
}

export interface EnvironmentInfo {
  opencodeVersion: string
  plugin: string
  pluginVersion: string
}

export interface ManualFields {
  name: string
  egn: string
  class: string
  school: string
  city: string
  projectNumber: string
  participants: string
  author: string
  organization: string
  role: string
  goals: string
  taskDescription: string
  documentation: string
  aiAsFunctionality: string
  model: string
  provider: string
  settings: string
  environment: string
  access: string
  serviceChanges: string
  ownWork: string
  verification: string
  limitations: string
}

export interface ParticipantInfo {
  name: string
  egn: string
  class: string
  school: string
  city: string
  projectNumber: string
}

export interface RecordSpec {
  id: string
  title: string
  task?: string
  sessionIDs: string[]
}

export interface ToolInfo {
  id: string
  name: string
  provider?: string
  notes?: string
}

export interface ExportOptions {
  records: RecordSpec[]
  includeKarta: boolean
  includeDeclaration: boolean
  /** When true, a PII-stripped mirror of the export is written to `.noit/public/`. */
  public: boolean
  /**
   * Additional AI tools (A02, A03, …). When empty or absent the exporter keeps the
   * single generic A02 placeholder card; otherwise it writes one card per entry.
   */
  tools?: ToolInfo[]
}

export interface BackfillOptions {
  sync: boolean
  limit: number
  concurrency: number
  /** @deprecated The plugin is always project-scoped; this value is ignored and reported once. */
  scope?: "project" | "all"
  projectId?: string
  directory?: string
  databasePath?: string
}

export interface NoitConfig {
  schema: string
  project: ProjectInfo
  environment: EnvironmentInfo
  manual: ManualFields
  participants: ParticipantInfo[]
  export: ExportOptions
  backfill: BackfillOptions
}

export interface BaseConfig {
  schema: string
  project: ProjectInfo
  environment: EnvironmentInfo
  export: ExportOptions
  backfill: BackfillOptions
}

export interface LocalConfig {
  schema: string
  manual: ManualFields
  participants: ParticipantInfo[]
}

export interface SessionSummary {
  id: string
  title?: string
  parentID?: string
  projectID?: string
  directory?: string
  time?: { created?: number; updated?: number }
  version?: string
  source?: "session_v2" | "session" | "api"
  location?: { directory?: string }
}

export interface BackfillState {
  installedAt: number
  lastBackfill: string | null
  imported: string[]
  failures: Record<string, string>
  projectID?: string
  directory?: string
  source?: string
}

export interface BackfillSummary {
  scanned: number
  eligible: number
  imported: number
  failed: number
  skipped: number
  lastBackfill: string | null
  message: string
}

export type ToolStatus = "pending" | "completed" | "error"

export interface LogEntry {
  ts: string
  type: "prompt" | "tool" | "session"
  sessionID?: string
  tool?: string
  input?: unknown
  result?: unknown
  error?: string
  status?: ToolStatus
  text?: string
}

export interface RawSession {
  id: string
  title: string
  messages: unknown[]
}

export interface RedactionFinding {
  rule: string
  replacement: string
  reason: string
  count: number
}

export interface RedactionResult {
  text: string
  findings: RedactionFinding[]
}

export interface RecordEntry {
  id: string
  title: string
  task: string
  status: "open" | "closed"
  startedAt: string
  endedAt: string | null
  sessionIDs: string[]
}

export interface ExportSummary {
  generatedAt: string
  sessions: number
  files: string[]
  redactions: number
  message: string
}

export interface CheckItem {
  id: string
  title: string
  passed: boolean
  detail: string
  required: boolean
}

export interface CheckResult {
  passed: boolean
  strict: boolean
  items: CheckItem[]
  failedRequired: number
  file: string
  message: string
}
