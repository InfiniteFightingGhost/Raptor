import { promises as fs } from "node:fs"
import path from "node:path"
import type { CheckItem, CheckResult, NoitConfig, PluginContext } from "./types.ts"
import {
  MAX_PARTICIPANTS,
  NOIT_DIR,
  UNKNOWN,
  ensureDir,
  exportDir,
  isUnknown,
  rawSessionsDir,
} from "./config.ts"
import { scanSecrets } from "./redact.ts"

const MANUAL_FIELDS = [
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

interface KartaField {
  label: string
  source: "label" | "heading"
}

const KARTA_FIELDS: KartaField[] = [
  { label: "Име на инструмента", source: "label" },
  { label: "Идентификатор в декларацията", source: "label" },
  { label: "Доставчик/разработчик", source: "label" },
  { label: "Версия на инструмента", source: "label" },
  { label: "Дата на инсталиране/достъп", source: "label" },
  { label: "Начин на достъп (акаунт/абонамент/безплатен план)", source: "label" },
  { label: "Роля", source: "label" },
  { label: "Цели", source: "label" },
  { label: "Свързани дневници", source: "label" },
  { label: "Настройки и параметри", source: "label" },
  { label: "Среда и зависимости", source: "label" },
  { label: "Версия на opencode", source: "label" },
  { label: "Модели, използвани в сесиите", source: "label" },
  { label: "Агенти, използвани в сесиите", source: "label" },
  { label: "Ограничения", source: "label" },
  { label: "Промени в услугата/версията през периода", source: "heading" },
  { label: "Проверка на резултатите от човека", source: "heading" },
  { label: "Собствен принос на участника и разграничение от приноса на ИИ", source: "heading" },
  { label: "Подпис", source: "label" },
  { label: "Дата", source: "label" },
]

const A02_MANDATORY_LABELS = new Set(["Име на инструмента", "Идентификатор в декларацията"])

const PUBLIC_FLAG_KEYS = ["publicExport", "public", "includePublic", "publicCopy"] as const
const PUBLIC_DIR_KEYS = ["publicDir", "publicDirectory", "publicPath"] as const

const PERSONAL_FIELDS = [
  "name",
  "egn",
  "class",
  "school",
  "city",
  "projectNumber",
  "participants",
  "author",
  "organization",
  "signature",
] as const

const EGN_BACKSTOP = /(?<!\d)\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])\s?\d{4}(?!\d)/

const RECORD_BLOCKS = [
  { key: "задача", title: "задача" },
  { key: "входни данни", title: "входни данни" },
  { key: "резултати", title: "резултати" },
  { key: "стъпки за възпроизвеждане", title: "стъпки за възпроизвеждане" },
] as const

interface AreaRow {
  area: string
  yes: boolean
  no: boolean
}

type DirectoryState = "content" | "empty" | "missing" | "unreadable"

async function readText(file: string): Promise<string | undefined> {
  try {
    return await fs.readFile(file, "utf8")
  } catch {
    return undefined
  }
}

function isPlaceholder(value: unknown): boolean {
  if (typeof value !== "string") return true
  const trimmed = value.trim()
  if (trimmed.length === 0) return true
  const lower = trimmed.toLowerCase()
  if (lower === "null" || lower === "undefined") return true
  if (lower === "-" || lower === "—" || lower === "–") return true
  if (lower.includes("непознато")) return true
  if (lower.includes("попълнете ръчно")) return true
  if (lower.includes("todo") || lower.includes("tbd")) return true
  return false
}

function toRelative(base: string, file: string): string {
  return path.relative(base, file).split(path.sep).join("/")
}

function normalizeListedPath(value: string): string {
  return value.replaceAll("\\", "/").replace(/^\.\//, "")
}

function isWithin(base: string, target: string): boolean {
  const relative = path.relative(base, target)
  return relative.length === 0 || (!relative.startsWith("..") && !path.isAbsolute(relative))
}

async function isFile(target: string): Promise<boolean> {
  try {
    const info = await fs.stat(target)
    return info.isFile()
  } catch {
    return false
  }
}

async function exists(target: string): Promise<boolean> {
  try {
    await fs.stat(target)
    return true
  } catch {
    return false
  }
}

async function collectFiles(dir: string): Promise<{ files: string[]; errors: string[] }> {
  const files: string[] = []
  const errors: string[] = []
  const walk = async (current: string): Promise<void> => {
    try {
      const entries = await fs.readdir(current, { withFileTypes: true })
      for (const entry of entries) {
        const full = path.join(current, entry.name)
        if (entry.isDirectory()) await walk(full)
        else if (entry.isFile()) files.push(full)
      }
    } catch (error) {
      errors.push(`${current}: ${error instanceof Error ? error.message : String(error)}`)
    }
  }
  await walk(dir)
  return { files, errors }
}

async function directoryState(dir: string): Promise<DirectoryState> {
  let entries
  try {
    entries = await fs.readdir(dir, { withFileTypes: true })
  } catch (error) {
    const code = (error as { code?: string } | undefined)?.code
    return code === "ENOENT" ? "missing" : "unreadable"
  }
  for (const entry of entries) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) {
      const nested = await directoryState(full)
      if (nested === "content" || nested === "unreadable") return nested
    } else if (entry.isFile()) {
      try {
        const info = await fs.stat(full)
        if (info.size > 0) return "content"
      } catch {
        return "unreadable"
      }
    }
  }
  return "empty"
}

async function hasSessions(ctx: PluginContext): Promise<boolean> {
  try {
    const names = await fs.readdir(rawSessionsDir(ctx))
    return names.some(
      (name) => name !== "index.json" && (name.endsWith(".json") || name.endsWith(".jsonl")),
    )
  } catch (error) {
    const code = (error as { code?: string } | undefined)?.code
    return code !== "ENOENT"
  }
}

function splitTableRow(line: string): string[] {
  const trimmed = line.trim()
  if (!trimmed.startsWith("|")) return []
  const inner = trimmed.replace(/^\|/, "").replace(/\|$/, "")
  return inner.split("|").map((cell) => cell.trim())
}

function isSeparatorRow(cells: string[]): boolean {
  return cells.length > 0 && cells.every((cell) => /^:?-{3,}:?$/.test(cell.replace(/\s/g, "")))
}

function parseMainForm(text: string): AreaRow[] {
  const lines = text.split(/\r?\n/)
  const rows: AreaRow[] = []
  let inTable = false
  for (const line of lines) {
    const cells = splitTableRow(line)
    if (cells.length === 0) {
      if (inTable) break
      continue
    }
    if (!inTable) {
      if (cells.length >= 3 && /^област$/i.test(cells[0].replace(/\*/g, "").trim())) inTable = true
      continue
    }
    if (isSeparatorRow(cells)) continue
    if (cells.length < 3) continue
    const yesCell = cells[1]
    const noCell = cells[2]
    const yes =
      /\[[xX]\]/.test(yesCell) || /^(да|yes|x)$/i.test(yesCell.replace(/\*/g, "").trim())
    const no = /\[[xX]\]/.test(noCell) || /^(не|no|x)$/i.test(noCell.replace(/\*/g, "").trim())
    rows.push({ area: cells[0], yes, no })
  }
  return rows
}

function isPlaceholderDocument(text: string): boolean {
  const trimmed = text.trim()
  if (trimmed.length === 0) return true
  const meaningful = trimmed.split(/\r?\n/).map((line) => line.trim()).filter((line) => {
    if (line.length === 0) return false
    if (line.startsWith("#")) return false
    if (line.startsWith(">")) return false
    if (line.startsWith("|")) return false
    return true
  })
  if (meaningful.length === 0) return true
  return meaningful.every((line) => isPlaceholder(line.replace(/^[-*]\s*/, "")))
}

function labelValue(text: string, label: string): string | undefined {
  const target = label.toLowerCase()
  let value: string | undefined
  for (const raw of text.split(/\r?\n/)) {
    const match = raw.trim().match(/^[-*]\s+(.+?)\s*:\s*(.*)$/)
    if (!match) continue
    if (match[1].trim().toLowerCase() !== target) continue
    value = match[2].trim()
  }
  return value
}

function headingValue(text: string, title: string): string | undefined {
  const lines = text.split(/\r?\n/)
  const target = title.toLowerCase()
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim()
    if (!line.startsWith("#")) continue
    const heading = line.replace(/^#+\s*/, "").toLowerCase()
    if (!heading.includes(target)) continue
    for (let next = index + 1; next < lines.length; next += 1) {
      const value = lines[next].trim()
      if (value.startsWith("#")) break
      if (value.length === 0) continue
      return value.replace(/^[-*]\s*/, "").trim()
    }
  }
  return undefined
}

function matchesBlock(label: string, block: (typeof RECORD_BLOCKS)[number]): boolean {
  const lower = label.toLowerCase()
  if (block.key === "задача") return lower.includes("задача")
  if (block.key === "входни данни") return lower.includes("входни данни")
  if (block.key === "резултати") return lower.includes("резултат")
  return lower.includes("възпроизвеждане") || lower.includes("стъпки")
}

function blockPresent(text: string, block: (typeof RECORD_BLOCKS)[number]): boolean {
  const lines = text.split(/\r?\n/)
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim()
    if (!line.startsWith("#")) continue
    const heading = line.replace(/^#+\s*/, "")
    if (!matchesBlock(heading, block)) continue
    for (let next = index + 1; next < lines.length; next += 1) {
      const value = lines[next].trim()
      if (value.startsWith("#")) break
      if (value.length === 0) continue
      if (!isPlaceholder(value.replace(/^[-*]\s*/, ""))) return true
    }
  }
  for (const raw of lines) {
    const match = raw.trim().match(/^[-*]\s+(.+?)\s*:\s*(.*)$/)
    if (!match) continue
    if (!matchesBlock(match[1], block)) continue
    if (!isPlaceholder(match[2])) return true
  }
  return false
}

function recordSegments(text: string, fallbackId: string): { id: string; text: string }[] {
  const lines = text.split(/\r?\n/)
  const starts: { id: string; index: number }[] = []
  for (let index = 0; index < lines.length; index += 1) {
    const match = lines[index].match(/^#{1,3}\s+.*?\b(B\d{2,})\b/)
    if (match) starts.push({ id: match[1], index })
  }
  if (starts.length === 0) return [{ id: fallbackId, text }]
  const merged = new Map<string, string>()
  for (let index = 0; index < starts.length; index += 1) {
    const end = index + 1 < starts.length ? starts[index + 1].index : lines.length
    const chunk = lines.slice(starts[index].index, end).join("\n")
    merged.set(starts[index].id, `${merged.get(starts[index].id) ?? ""}\n${chunk}`)
  }
  return [...merged.entries()].map(([id, value]) => ({ id, text: value }))
}

function parsePathTable(text: string): { present: boolean; paths: string[] } {
  const lines = text.split(/\r?\n/)
  const paths: string[] = []
  let present = false
  for (const line of lines) {
    const cells = splitTableRow(line)
    if (!present) {
      if (cells.length > 0 && /^(път|path)$/i.test(cells[0].replace(/`/g, "").trim())) present = true
      continue
    }
    if (cells.length === 0) break
    if (isSeparatorRow(cells)) continue
    const first = cells[0]
    const ticks = [...first.matchAll(/`([^`]+)`/g)].map((match) => match[1].trim()).filter(Boolean)
    if (ticks.length > 0) {
      paths.push(...ticks)
    } else {
      const value = first.replace(/\*\*/g, "").trim()
      if (value.length > 0 && !/^(път|path)$/i.test(value)) paths.push(value)
    }
  }
  return { present, paths: [...new Set(paths)] }
}

function notApplicableDocumented(text: string | undefined): boolean {
  if (text === undefined) return false
  return text.split(/\r?\n/).some((raw) => {
    const line = raw.trim()
    if (line.length === 0) return false
    if (!/не\s+е\s+приложимо|неприложимо/i.test(line)) return false
    const lower = line.toLowerCase()
    if (lower.includes("непознато")) return false
    if (lower.includes("попълнете ръчно")) return false
    return true
  })
}

async function recordFilesIn(base: string): Promise<string[]> {
  try {
    const entries = await fs.readdir(base, { withFileTypes: true })
    return entries
      .filter((entry) => entry.isFile() && /^records-B.*\.md$/i.test(entry.name))
      .map((entry) => entry.name)
      .sort((a, b) => a.localeCompare(b))
  } catch {
    return []
  }
}

async function topLevelMarkdown(base: string): Promise<string[]> {
  try {
    const entries = await fs.readdir(base, { withFileTypes: true })
    return entries
      .filter((entry) => entry.isFile() && entry.name.toLowerCase().endsWith(".md"))
      .map((entry) => entry.name)
      .sort((a, b) => a.localeCompare(b))
  } catch {
    return []
  }
}

function readBoolean(value: unknown, keys: readonly string[]): boolean | undefined {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return undefined
  const record = value as Record<string, unknown>
  for (const key of keys) {
    const candidate = record[key]
    if (typeof candidate === "boolean") return candidate
    if (candidate !== null && typeof candidate === "object" && !Array.isArray(candidate)) {
      const nested = candidate as Record<string, unknown>
      if (typeof nested.enabled === "boolean") return nested.enabled
      if (typeof nested.include === "boolean") return nested.include
    }
  }
  return undefined
}

function publicFlagFromConfig(config: NoitConfig): boolean | undefined {
  const root = config as unknown as Record<string, unknown>
  const exported = root.export
  const fromExport = readBoolean(exported, PUBLIC_FLAG_KEYS)
  if (fromExport !== undefined) return fromExport
  return readBoolean(root, PUBLIC_FLAG_KEYS)
}

async function readRawConfigRecords(ctx: PluginContext): Promise<Record<string, unknown>[]> {
  const root = path.join(ctx.location.directory, NOIT_DIR)
  const records: Record<string, unknown>[] = []
  for (const name of ["config.local.json", "config.json"]) {
    const text = await readText(path.join(root, name))
    if (text === undefined) continue
    try {
      const parsed: unknown = JSON.parse(text)
      if (parsed !== null && typeof parsed === "object" && !Array.isArray(parsed)) {
        records.push(parsed as Record<string, unknown>)
      }
    } catch {}
  }
  return records
}

async function publicFlagFromRaw(ctx: PluginContext): Promise<boolean | undefined> {
  for (const record of await readRawConfigRecords(ctx)) {
    const fromExport = readBoolean(record.export, PUBLIC_FLAG_KEYS)
    if (fromExport !== undefined) return fromExport
    const fromRoot = readBoolean(record, PUBLIC_FLAG_KEYS)
    if (fromRoot !== undefined) return fromRoot
  }
  return undefined
}

async function isPublicEnabled(ctx: PluginContext, config: NoitConfig): Promise<boolean> {
  const fromConfig = publicFlagFromConfig(config)
  if (fromConfig !== undefined) return fromConfig
  const options = (ctx as unknown as { options?: { export?: { public?: unknown } } }).options
  const fromOptions = options?.export?.public
  if (typeof fromOptions === "boolean") return fromOptions
  const fromRaw = await publicFlagFromRaw(ctx)
  return fromRaw === true
}

function publicDirFromRecord(
  record: Record<string, unknown>,
  root: string,
): string | undefined {
  const records = [record.export, record]
  for (const value of records) {
    if (value === null || typeof value !== "object" || Array.isArray(value)) continue
    const nested = value as Record<string, unknown>
    for (const key of PUBLIC_DIR_KEYS) {
      const candidate = nested[key]
      if (typeof candidate === "string" && candidate.trim().length > 0) {
        return path.resolve(root, candidate.trim())
      }
    }
    for (const key of ["public", "publicExport"]) {
      const candidate = nested[key]
      if (typeof candidate === "string" && candidate.trim().length > 0) {
        return path.resolve(root, candidate.trim())
      }
      if (candidate !== null && typeof candidate === "object" && !Array.isArray(candidate)) {
        const publicRecord = candidate as Record<string, unknown>
        for (const nestedKey of ["dir", "directory", "path"]) {
          const nestedValue = publicRecord[nestedKey]
          if (typeof nestedValue === "string" && nestedValue.trim().length > 0) {
            return path.resolve(root, nestedValue.trim())
          }
        }
      }
    }
  }
  return undefined
}

async function publicDirFromConfig(
  ctx: PluginContext,
  config: NoitConfig,
): Promise<string | undefined> {
  const root = ctx.location.directory
  return publicDirFromRecord(config as unknown as Record<string, unknown>, root)
}

async function publicDirFromRaw(ctx: PluginContext): Promise<string | undefined> {
  const root = ctx.location.directory
  for (const record of await readRawConfigRecords(ctx)) {
    const dir = publicDirFromRecord(record, root)
    if (dir !== undefined) return dir
  }
  return undefined
}

async function publicDirFromGitignore(ctx: PluginContext): Promise<string | undefined> {
  const root = ctx.location.directory
  const text = await readText(path.join(root, ".gitignore"))
  if (text === undefined) return undefined
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim()
    if (!line.startsWith("!")) continue
    const value = line
      .slice(1)
      .trim()
      .replace(/\\/g, "/")
      .replace(/^\.\//, "")
      .replace(/^\/+/, "")
      .replace(/\/+$/, "")
    if (value.length === 0 || value.includes("*")) continue
    if (!/public/i.test(value)) continue
    return path.resolve(root, value)
  }
  return undefined
}

async function resolvePublicDir(ctx: PluginContext, config: NoitConfig): Promise<string> {
  const fromConfig = await publicDirFromConfig(ctx, config)
  if (fromConfig !== undefined) return fromConfig
  const fromRaw = await publicDirFromRaw(ctx)
  if (fromRaw !== undefined) return fromRaw
  const fromGitignore = await publicDirFromGitignore(ctx)
  if (fromGitignore !== undefined) return fromGitignore
  return path.join(ctx.location.directory, NOIT_DIR, "public")
}

function isPersonalValue(value: string): boolean {
  if (isUnknown(value)) return false
  const lower = value.trim().toLowerCase()
  if (lower.length === 0) return false
  if (lower === "opencode" || lower === "ai" || lower === "ии") return false
  if (lower.includes("не се предоставят") || lower.includes("доставчика")) return false
  if (lower.includes("попълнете ръчно")) return false
  if (lower.includes("непознато")) return false
  if (lower.includes("null") || lower.includes("undefined")) return false
  return true
}

function collectPersonalValues(config: NoitConfig): Map<string, string> {
  const values = new Map<string, string>()
  const add = (value: unknown, field: string): void => {
    if (typeof value !== "string") return
    const trimmed = value.trim()
    if (!isPersonalValue(trimmed)) return
    if (!values.has(trimmed)) values.set(trimmed, field)
  }
  const manual = config.manual as unknown as Record<string, unknown>
  for (const field of PERSONAL_FIELDS) add(manual[field], field)
  const root = config as unknown as Record<string, unknown>
  for (const field of PERSONAL_FIELDS) add(root[field], field)
  const participants = Array.isArray(config.participants) ? config.participants : []
  for (const participant of participants) {
    if (participant === null || typeof participant !== "object") continue
    const record = participant as unknown as Record<string, unknown>
    for (const field of PERSONAL_FIELDS) add(record[field], field)
  }
  return values
}

function scanPersonalText(text: string, values: Map<string, string>): string[] {
  const found: string[] = []
  const seen = new Set<string>()
  for (const [value, field] of values) {
    if (seen.has(field)) continue
    if (text.includes(value)) {
      seen.add(field)
      found.push(field)
    }
  }
  if (!seen.has("egn") && !seen.has("EGN") && EGN_BACKSTOP.test(text)) {
    seen.add("EGN")
    found.push("EGN")
  }
  return found
}

async function scanPublicDirectory(
  dir: string,
  values: Map<string, string>,
): Promise<{ problems: string[]; files: number; missing: boolean; unreadable: boolean }> {
  const state = await directoryState(dir)
  if (state === "missing") {
    return { problems: [`публичната директория липсва: ${dir}`], files: 0, missing: true, unreadable: false }
  }
  if (state === "unreadable") {
    return {
      problems: [`публичната директория не може да се прочете: ${dir}`],
      files: 0,
      missing: false,
      unreadable: true,
    }
  }
  if (state === "empty") return { problems: [], files: 0, missing: false, unreadable: false }
  const problems: string[] = []
  let files = 0
  const collected = await collectFiles(dir)
  for (const error of collected.errors) problems.push(`публичната директория: ${error}`)
  for (const file of collected.files) {
    files += 1
    const text = await readText(file)
    if (text === undefined) {
      problems.push(`${toRelative(dir, file)}: не може да се прочете`)
      continue
    }
    for (const field of scanPersonalText(text, values)) {
      problems.push(`${toRelative(dir, file)}: ${field}`)
    }
  }
  return { problems, files, missing: false, unreadable: false }
}

export async function runCheck(
  ctx: PluginContext,
  config: NoitConfig,
  options: { strict?: boolean } = {},
): Promise<CheckResult> {
  const strict = options.strict === true
  const base = exportDir(ctx)
  await ensureDir(base)

  const sessionsExist = await hasSessions(ctx)
  const items: CheckItem[] = []

  const declarationPath = path.join(base, "declaration.md")
  const declarationText = await readText(declarationPath)
  const areaRows = declarationText ? parseMainForm(declarationText) : []
  const anyAreaYes = areaRows.some((row) => row.yes)
  const needsAbc = anyAreaYes || sessionsExist
  const mainFormProblems: string[] = []
  if (needsAbc) {
    const requiredDocuments = [
      { name: "declaration.md", text: declarationText },
      { name: "karta-A01.md", text: await readText(path.join(base, "karta-A01.md")) },
      { name: "section-V.md", text: await readText(path.join(base, "section-V.md")) },
    ]
    for (const document of requiredDocuments) {
      if (document.text === undefined) mainFormProblems.push(`${document.name}: липсва`)
      else if (isPlaceholderDocument(document.text)) {
        mainFormProblems.push(`${document.name}: само шаблонен текст`)
      }
    }
    const recordsPresent = await recordFilesIn(base)
    const expectedRecords = config.export.records
      .map((record) => record.id)
      .filter((id) => typeof id === "string" && id.trim().length > 0)
    if (expectedRecords.length > 0) {
      for (const id of expectedRecords) {
        const name = `records-${id}.md`
        if (!recordsPresent.some((entry) => entry.toLowerCase() === name.toLowerCase())) {
          mainFormProblems.push(`${name}: липсва`)
        }
      }
    } else if (recordsPresent.length === 0) {
      mainFormProblems.push("records-B*.md: липсва")
    }
    for (const name of recordsPresent) {
      const text = await readText(path.join(base, name))
      if (text === undefined) mainFormProblems.push(`${name}: не може да се прочете`)
      else if (isPlaceholderDocument(text)) {
        mainFormProblems.push(`${name}: само шаблонен текст`)
      }
    }
  }
  items.push({
    id: "main-form",
    title: "Главна форма: изискуеми документи A/B/C",
    passed: mainFormProblems.length === 0,
    required: true,
    detail:
      mainFormProblems.length === 0
        ? needsAbc
          ? "declaration.md, karta-A01.md, records-B*.md и section-V.md са налични и не са празни шаблонни документи."
          : "Няма отметната област „Да“ и няма сесии; A/B/C не се изискват."
        : `Липсващи или празни документи: ${mainFormProblems.join("; ")}.`,
  })

  const kartaProblems: string[] = []
  const kartaFiles = (await topLevelMarkdown(base)).filter((name) => /^karta-A.*\.md$/i.test(name))
  const kartaA01Present = kartaFiles.some((name) => name.toLowerCase() === "karta-a01.md")
  if (needsAbc && !kartaA01Present) {
    kartaProblems.push("karta-A01.md: липсва (изисква се при сесии или отметната област „Да“)")
  }
  for (const name of kartaFiles) {
    const text = await readText(path.join(base, name))
    if (text === undefined) {
      kartaProblems.push(`${name}: не може да се прочете`)
      continue
    }
    const isA01 = name.toLowerCase() === "karta-a01.md"
    const fields = isA01
      ? KARTA_FIELDS
      : KARTA_FIELDS.filter((field) => A02_MANDATORY_LABELS.has(field.label))
    for (const field of fields) {
      const value =
        field.source === "label" ? labelValue(text, field.label) : headingValue(text, field.label)
      if (isPlaceholder(value)) kartaProblems.push(`${name}: ${field.label}`)
    }
  }
  items.push({
    id: "section-a",
    title: "Раздел А: карти A01–A02+ — задължителни полета",
    passed: kartaProblems.length === 0,
    required: true,
    detail:
      kartaProblems.length === 0
        ? kartaFiles.length === 0
          ? "Няма karta-A*.md; при липса на сесии и без отметната област „Да“ не се изисква."
          : `Всички задължителни полета в ${kartaFiles.join(", ")} са попълнени.`
        : `Празни или шаблонни полета („файл: поле“): ${kartaProblems.join("; ")}.`,
  })

  const recordProblems: string[] = []
  const recordFiles = await recordFilesIn(base)
  if (recordFiles.length === 0 && needsAbc) {
    recordProblems.push("records-B*.md: липсва (изисква се при сесии или отметната област „Да“)")
  }
  for (const name of recordFiles) {
    const text = await readText(path.join(base, name))
    if (text === undefined) {
      recordProblems.push(`${name}: не може да се прочете`)
      continue
    }
    const fallbackId = name.match(/records-(B\d+)\.md/i)?.[1] ?? name
    for (const segment of recordSegments(text, fallbackId)) {
      for (const block of RECORD_BLOCKS) {
        if (!blockPresent(segment.text, block)) {
          recordProblems.push(`${name} (${segment.id}): липсва блок „${block.title}“`)
        }
      }
      const hasSessionReference =
        /\bses_[A-Za-z0-9]{6,}\b/.test(segment.text) ||
        /raw\/sessions\//i.test(segment.text) ||
        /sessionID/i.test(segment.text)
      if (!hasSessionReference && !/ръчен запис/i.test(segment.text)) {
        recordProblems.push(
          `${name} (${segment.id}): няма препратка към сесия и няма отметка „ръчен запис“`,
        )
      }
    }
  }
  items.push({
    id: "section-b",
    title: "Раздел Б: дневници B — четири блока и препратки",
    passed: recordProblems.length === 0,
    required: true,
    detail:
      recordProblems.length === 0
        ? recordFiles.length === 0
          ? "Няма records-B*.md; при липса на сесии и без отметната област „Да“ не се изисква."
          : `Всички записи в ${recordFiles.join(", ")} имат четирите блока и препратка към сесия.`
        : `Проблеми: ${recordProblems.join("; ")}.`,
  })

  const sectionProblems: string[] = []
  const sectionVName = "section-V.md"
  const sectionVText = await readText(path.join(base, sectionVName))
  let pathTable: { present: boolean; paths: string[] } = { present: false, paths: [] }
  if (sectionVText !== undefined) pathTable = parsePathTable(sectionVText)
  if (!pathTable.present) {
    if (sessionsExist) {
      sectionProblems.push(
        sectionVText === undefined
          ? "section-V.md: липсва (изисква се при налични сесии)"
          : "section-V.md: липсва таблица с относителни пътища",
      )
    }
  } else if (pathTable.paths.length === 0) {
    if (sessionsExist) sectionProblems.push("section-V.md: таблицата с относителни пътища е празна")
  } else {
    for (const entry of pathTable.paths) {
      const value = entry.trim()
      if (value.length === 0) continue
      if (/^[a-z][a-z0-9+.-]*:\/\//i.test(value)) {
        sectionProblems.push(`section-V.md: ${value} (външен URL, не е относителен път)`)
        continue
      }
      if (/^[A-Za-z]:[\\/]/.test(value) || path.isAbsolute(value)) {
        sectionProblems.push(`section-V.md: ${value} (абсолютен път)`)
        continue
      }
      const normalized = normalizeListedPath(value)
      const resolved = path.resolve(base, normalized)
      if (!isWithin(base, resolved)) {
        sectionProblems.push(`section-V.md: ${value} (извън директорията за експорт)`)
        continue
      }
      if (!(await exists(resolved))) {
        sectionProblems.push(`section-V.md: ${value} (липсва спрямо директорията за експорт)`)
      }
    }
  }
  items.push({
    id: "section-c",
    title: "Раздел В: относителни пътища в Раздел V",
    passed: sectionProblems.length === 0,
    required: true,
    detail:
      sectionProblems.length === 0
        ? pathTable.present && pathTable.paths.length > 0
          ? `Всички ${pathTable.paths.length} пътя от Раздел V са валидни спрямо директорията за експорт.`
          : "Няма таблица с относителни пътища; при липса на сесии не се изисква."
        : `Счупени препратки: ${sectionProblems.join("; ")}.`,
  })

  const evidenceProblems: string[] = []
  for (const name of ["requests", "outputs", "inputs"]) {
    const state = await directoryState(path.join(base, name))
    if (state === "content") continue
    if (state === "unreadable") evidenceProblems.push(`${name}/: не може да се прочете`)
    else evidenceProblems.push(`${name}/: ${state === "missing" ? "липсва" : "е празна"}`)
  }
  const notesText = [
    await readText(path.join(base, "tests", "limitations.md")),
    await readText(path.join(base, "changes", "index.md")),
    await readText(path.join(base, "section-V.md")),
  ]
    .filter((value): value is string => typeof value === "string")
    .join("\n")
  const notApplicable = notApplicableDocumented(notesText)
  for (const name of ["changes", "tests"]) {
    const state = await directoryState(path.join(base, name))
    if (state === "content") continue
    if (state === "unreadable") {
      evidenceProblems.push(`${name}/: не може да се прочете`)
      continue
    }
    if (!notApplicable) {
      evidenceProblems.push(
        `${name}/: ${state === "missing" ? "липсва" : "е празна"} и няма отметка „не е приложимо“`,
      )
    }
  }
  items.push({
    id: "evidence",
    title: "Доказателства: requests, outputs, inputs, changes, tests",
    passed: evidenceProblems.length === 0,
    required: true,
    detail:
      evidenceProblems.length === 0
        ? "requests/, outputs/, inputs/ са непразни; changes/ и tests/ са налични или отбелязани като „не е приложимо“."
        : `Проблеми: ${evidenceProblems.join("; ")}.`,
  })

  const manifestProblems: string[] = []
  const manifestPath = path.join(base, "manifest.json")
  const manifestText = await readText(manifestPath)
  let manifestUsable = false
  let listedPaths: string[] = []
  if (manifestText === undefined) {
    manifestProblems.push("manifest.json: липсва или не може да се прочете")
  } else {
    let parsed: unknown
    let parsedOk = false
    try {
      parsed = JSON.parse(manifestText)
      parsedOk = true
    } catch {
      manifestProblems.push("manifest.json: не е валиден JSON")
    }
    if (parsedOk) {
      if (parsed === null || typeof parsed !== "object") {
        manifestProblems.push("manifest.json: очаква се JSON обект")
      } else {
        const record = parsed as Record<string, unknown>
        if (!Array.isArray(record.files)) {
          manifestProblems.push("manifest.json: липсва масив files")
        } else {
          manifestUsable = true
          const entries = record.files as unknown[]
          for (let index = 0; index < entries.length; index += 1) {
            const entry = entries[index]
            if (!entry || typeof entry !== "object") {
              manifestProblems.push(`manifest.json: невалиден запис #${index + 1}`)
              continue
            }
            const pathValue = (entry as Record<string, unknown>).path
            if (typeof pathValue !== "string" || pathValue.trim().length === 0) {
              manifestProblems.push(`manifest.json: невалиден запис #${index + 1} (липсва path)`)
              continue
            }
            const normalized = normalizeListedPath(pathValue)
            const resolved = path.resolve(base, normalized)
            if (normalized.length === 0 || !isWithin(base, resolved)) {
              manifestProblems.push(`manifest.json: невалиден път ${normalized}`)
              continue
            }
            listedPaths.push(normalized)
            if (!(await isFile(resolved))) {
              manifestProblems.push(`manifest.json: изброеният файл липсва на диска: ${normalized}`)
            }
          }
        }
      }
    }
  }
  if (manifestUsable) {
    const actual = await collectFiles(base)
    for (const error of actual.errors) manifestProblems.push(`директория недостъпна: ${error}`)
    const listed = new Set(listedPaths)
    for (const file of actual.files) {
      const relative = toRelative(base, file)
      if (relative === "manifest.json") continue
      if (!listed.has(relative)) {
        manifestProblems.push(`manifest.json не изброява файла на диска: ${relative}`)
      }
    }
  }
  items.push({
    id: "manifest",
    title: "Опис на файловете: пълен и съответстващ на диска",
    passed: manifestProblems.length === 0,
    required: true,
    detail:
      manifestProblems.length === 0
        ? `manifest.json изброява всички ${listedPaths.length} файла и няма неописани файлове.`
        : `Проблеми: ${manifestProblems.join("; ")}.`,
  })

  const manualRecord = config.manual as unknown as Record<string, unknown>
  const missingManual = MANUAL_FIELDS.filter((key) => isPlaceholder(manualRecord[key]))
  items.push({
    id: "manual-fields",
    title: "Ръчно попълнени полета в config.local.json (обединена конфигурация)",
    passed: missingManual.length === 0,
    required: true,
    detail:
      missingManual.length === 0
        ? "Всички ръчни полета са попълнени в обединената конфигурация (config.json + config.local.json)."
        : `Непопълнени ръчни полета („${UNKNOWN}“ / „попълнете ръчно“ / празно / null / невалидна стойност): ${missingManual.join(", ")}.`,
  })

  const publicEnabled = await isPublicEnabled(ctx, config)
  const publicDir = await resolvePublicDir(ctx, config)
  const publicScan = publicEnabled
    ? await scanPublicDirectory(publicDir, collectPersonalValues(config))
    : { problems: [] as string[], files: 0, missing: false, unreadable: false }
  const publicProblems = publicScan.problems
  items.push({
    id: "pii",
    title: "Публично копие: без лични данни",
    passed: publicProblems.length === 0,
    required: true,
    detail: !publicEnabled
      ? "Публично копие не е заявено (флагът е изключен); няма сканиране."
      : publicScan.missing
        ? `Публичното копие липсва: ${publicDir}.`
        : publicScan.unreadable
          ? `Публичната директория не може да се прочете: ${publicDir}.`
          : publicProblems.length === 0
            ? `Публичното копие в ${publicDir} е сканирано (${publicScan.files} файла); не са открити лични данни.`
            : `Открити лични данни (файл: поле): ${publicProblems.join("; ")}.`,
  })

  const participants = Array.isArray(config.participants) ? config.participants : []
  const participantProblems: string[] = []
  if (participants.length === 0) {
    participantProblems.push("participants: списъкът е празен")
  } else if (participants.length > MAX_PARTICIPANTS) {
    participantProblems.push(`participants: ${participants.length} > ${MAX_PARTICIPANTS}`)
  }
  const duplicateSeen = new Map<string, number>()
  participants.forEach((participant, index) => {
    const label = `участник #${index + 1}`
    if (participant === null || typeof participant !== "object") {
      participantProblems.push(`${label}: невалиден запис`)
      return
    }
    const record = participant as unknown as Record<string, unknown>
    const name = typeof record.name === "string" ? record.name : ""
    const egn = typeof record.egn === "string" ? record.egn : ""
    const optionalFields = ["egn", "class", "school", "city", "projectNumber"] as const
    const allPlaceholder = ["name", ...optionalFields, "signature", "date"].every((key) => {
      const value = record[key]
      if (value === undefined) return true
      return isPlaceholder(value)
    })
    const unfilled: string[] = []
    if (isPlaceholder(name)) unfilled.push("name")
    if (record.signature !== undefined && isPlaceholder(record.signature)) unfilled.push("signature")
    if (record.date !== undefined && isPlaceholder(record.date)) unfilled.push("date")
    if (allPlaceholder) {
      for (const key of optionalFields) {
        if (isPlaceholder(record[key])) unfilled.push(key)
      }
    }
    if (unfilled.length > 0) participantProblems.push(`${label}: ${unfilled.join(", ")}`)
    const trimmedName = name.trim().toLowerCase()
    const trimmedEgn = egn.trim()
    if (trimmedName.length > 0 && trimmedEgn.length > 0) {
      const key = `${trimmedName}\u0000${trimmedEgn}`
      const previous = duplicateSeen.get(key)
      if (previous !== undefined) {
        participantProblems.push(`${label}: дубликат на участник #${previous + 1} (name+EGN)`)
      } else {
        duplicateSeen.set(key, index)
      }
    }
  })
  const participantManual = config.manual as unknown as Record<string, unknown>
  if (participantManual.signature !== undefined && isPlaceholder(participantManual.signature)) {
    participantProblems.push("manual: signature")
  }
  if (participantManual.date !== undefined && isPlaceholder(participantManual.date)) {
    participantProblems.push("manual: date")
  }
  items.push({
    id: "participants",
    title: "Участници: име, подпис и дата",
    passed: participantProblems.length === 0,
    required: true,
    detail:
      participantProblems.length === 0
        ? `Всички ${participants.length} участника са валидни (име, подпис/дата, без дубликати).`
        : `Проблеми: ${participantProblems.join("; ")}.`,
  })

  const secretProblems: string[] = []
  const scanned = await collectFiles(base)
  for (const error of scanned.errors) secretProblems.push(`директория недостъпна: ${error}`)
  for (const file of scanned.files) {
    if (!/\.(?:md|json)$/i.test(file)) continue
    let text: string
    try {
      text = await fs.readFile(file, "utf8")
    } catch {
      secretProblems.push(`${toRelative(base, file)}: не може да се прочете`)
      continue
    }
    for (const finding of scanSecrets(text)) {
      secretProblems.push(`${toRelative(base, file)}: ${finding.rule} (${finding.count})`)
    }
  }
  items.push({
    id: "secrets",
    title: "Без тайни в генерираните .md/.json файлове",
    passed: secretProblems.length === 0,
    required: true,
    detail:
      secretProblems.length === 0
        ? "Не са открити тайни в генерираните .md/.json файлове."
        : `Открити тайни (файл: шаблон): ${secretProblems.join("; ")}.`,
  })

  const failedRequired = items.filter((item) => item.required && !item.passed).length
  const passed = failedRequired === 0

  const lines = [
    "# Контролен списък (checklist)",
    "",
    `- Проект: ${config.project.name}`,
    `- Режим: ${strict ? "стриктен" : "обикновен"}`,
    `- Генерирано: ${new Date().toISOString()}`,
    `- Резултат: ${passed ? "ИЗДЪРЖАН" : "НЕИЗДЪРЖАН"}`,
    "",
    "## Точки",
    "",
  ]
  for (const item of items) {
    lines.push(`- [${item.passed ? "x" : " "}] ${item.title}`)
    lines.push(`  - ${item.detail}`)
  }
  lines.push(
    "",
    "## Забележка",
    "",
    "- Политика: консервативна — фалшив НЕИЗДЪРЖАН е приемлив, фалшив ИЗДЪРЖАН не е.",
    strict
      ? "- Стриктният режим променя само формулировката, не резултата."
      : "- Обикновеният режим не разрешава пропускане на проверки.",
    "",
  )
  await fs.writeFile(path.join(base, "checklist.md"), lines.join("\n"), "utf8")

  return {
    passed,
    strict,
    items,
    failedRequired,
    file: path.join(base, "checklist.md"),
    message: `Проверка (${config.project.name}): ${passed ? "УСПЕХ" : `${failedRequired} неуспешни точки`} в checklist.md.`,
  }
}
