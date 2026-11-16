import { promises as fs } from "node:fs"
import path from "node:path"
import crypto from "node:crypto"
import type {
  ExportSummary,
  LogEntry,
  NoitConfig,
  PluginContext,
  RawSession,
  RecordEntry,
  RecordSpec,
  ToolInfo,
} from "./types.ts"
import {
  NOT_APPLICABLE,
  UNKNOWN,
  ensureDir,
  exportDir,
  isUnknown,
  logFile,
  noitRoot,
  pathExists,
  rawSessionsDir,
  recordsFile,
} from "./config.ts"
import { makeCollector, redact, type RedactionCollector } from "./redact.ts"

const MAX_SNIPPET = 700

export async function readRecords(ctx: PluginContext): Promise<RecordEntry[]> {
  const file = recordsFile(ctx)
  if (!(await pathExists(file))) return []
  try {
    const parsed = JSON.parse(await fs.readFile(file, "utf8"))
    return Array.isArray(parsed) ? (parsed as RecordEntry[]) : []
  } catch {
    return []
  }
}

export async function writeRecords(ctx: PluginContext, records: RecordEntry[]): Promise<void> {
  const file = recordsFile(ctx)
  await ensureDir(path.dirname(file))
  await fs.writeFile(file, JSON.stringify(records, null, 2), "utf8")
}

export function nextRecordId(records: RecordEntry[]): string {
  return `B${String(records.length + 2).padStart(2, "0")}`
}

function statusLabel(status: unknown): string {
  if (status === "pending") return "чакащ"
  if (status === "completed") return "завършен"
  if (status === "error") return "грешка"
  return "?"
}

interface ManualRecordPlan {
  id: string
  order: number
  record: RecordEntry
}

function recordTimestamp(record: RecordEntry): number {
  if (typeof record.startedAt !== "string") return Number.POSITIVE_INFINITY
  const ms = Date.parse(record.startedAt)
  return Number.isFinite(ms) ? ms : Number.POSITIVE_INFINITY
}

function planManualRecords(records: RecordEntry[], used: Set<string>): ManualRecordPlan[] {
  const sorted = records
    .filter(
      (record): record is RecordEntry =>
        Boolean(record) && typeof record === "object" && typeof record.title === "string",
    )
    .sort((a, b) => {
      const delta = recordTimestamp(a) - recordTimestamp(b)
      if (Number.isFinite(delta) && delta !== 0) return delta
      return String(a.id ?? "").localeCompare(String(b.id ?? ""))
    })
  const plans: ManualRecordPlan[] = []
  sorted.forEach((record, index) => {
    const stored =
      typeof record.id === "string" && /^B\d{2,}$/.test(record.id) ? record.id : undefined
    let id: string
    if (stored !== undefined && !used.has(stored)) {
      id = stored
      used.add(stored)
    } else {
      id = nextAutoId(used)
    }
    plans.push({ id, order: index, record })
  })
  return plans
}

export async function readLog(ctx: PluginContext): Promise<LogEntry[]> {
  const file = logFile(ctx)
  if (!(await pathExists(file))) return []
  try {
    const text = await fs.readFile(file, "utf8")
    return text
      .split("\n")
      .filter((line) => line.trim().length > 0)
      .map((line) => {
        try {
          return JSON.parse(line) as LogEntry
        } catch {
          return undefined
        }
      })
      .filter((entry): entry is LogEntry => Boolean(entry))
  } catch {
    return []
  }
}

export async function readRawSessions(ctx: PluginContext): Promise<RawSession[]> {
  const dir = rawSessionsDir(ctx)
  let names: string[]
  try {
    names = await fs.readdir(dir)
  } catch {
    return []
  }
  const ids = names
    .filter((name) => name.endsWith(".json") && name !== "index.json")
    .map((name) => name.slice(0, -".json".length))
  const out: RawSession[] = []
  for (const id of ids) {
    try {
      const parsed = JSON.parse(await fs.readFile(path.join(dir, `${id}.json`), "utf8"))
      const messages = Array.isArray(parsed) ? parsed : Array.isArray(parsed.messages) ? parsed.messages : []
      const title = parsed.session?.title ?? parsed.title ?? id
      out.push({ id, title, messages })
    } catch {
      try {
        const text = await fs.readFile(path.join(dir, `${id}.jsonl`), "utf8")
        const messages = text
          .split("\n")
          .filter(Boolean)
          .map((line) => {
            try {
              return JSON.parse(line)
            } catch {
              return undefined
            }
          })
          .filter((value) => value !== undefined)
        out.push({ id, title: id, messages })
      } catch {}
    }
  }
  return out.sort((a, b) => a.id.localeCompare(b.id))
}

function truncate(value: string, max: number): string {
  const normalized = (value ?? "").replace(/\s+/g, " ").trim()
  return normalized.length <= max ? normalized : `${normalized.slice(0, max)}…`
}

function userTexts(messages: unknown[]): string[] {
  const out: string[] = []
  for (const message of messages) {
    const item = message as { type?: string; text?: unknown }
    if (item?.type === "user" && typeof item.text === "string") out.push(item.text)
  }
  return out
}

function assistantTexts(messages: unknown[]): string[] {
  const out: string[] = []
  for (const message of messages) {
    const item = message as { type?: string; content?: unknown }
    if (item?.type !== "assistant" || !Array.isArray(item.content)) continue
    for (const part of item.content) {
      const piece = part as { type?: string; text?: unknown }
      if (piece?.type === "text" && typeof piece.text === "string") out.push(piece.text)
    }
  }
  return out
}

function toolInputs(messages: unknown[]): { name: string; input: unknown }[] {
  const out: { name: string; input: unknown }[] = []
  for (const message of messages) {
    const item = message as { type?: string; content?: unknown }
    if (item?.type !== "assistant" || !Array.isArray(item.content)) continue
    for (const part of item.content) {
      const piece = part as { type?: string; name?: string; tool?: string; state?: { input?: unknown }; input?: unknown }
      if (piece?.type === "tool") {
        out.push({
          name: piece.name ?? piece.tool ?? "инструмент",
          input: piece.state?.input ?? piece.input ?? {},
        })
      }
    }
  }
  return out
}

async function writeRedacted(
  collector: RedactionCollector,
  file: string,
  content: string,
  written: string[],
): Promise<void> {
  const result = redact(content)
  collector.add(result)
  await fs.writeFile(file, result.text, "utf8")
  written.push(file)
}

function appendixLink(id: string): string {
  return `../../../raw/sessions/${id}.jsonl`
}

function link(id: string): string {
  return `[пълен запис](${appendixLink(id)})`
}

function header(title: string, config: NoitConfig): string {
  return [
    `# ${title}`,
    "",
    `- Проект: ${config.project.name}`,
    `- Директория: ${config.project.directory}`,
    `- Генерирано: ${new Date().toISOString()}`,
    `- Плъгин: ${config.environment.plugin}@${config.environment.pluginVersion} (opencode ${config.environment.opencodeVersion})`,
    "",
  ].join("\n")
}

async function generateRequests(
  sessions: RawSession[],
  collector: RedactionCollector,
  written: string[],
  dir: string,
  config: NoitConfig,
): Promise<void> {
  const lines = [header("Заявки към ИИ", config)]
  for (const session of sessions) {
    const texts = userTexts(session.messages)
    lines.push(`## ${session.title} (${session.id})`, "", `- ${link(session.id)}`, "")
    if (texts.length === 0) {
      lines.push(`- ${NOT_APPLICABLE}: няма записани потребителски заявки.`, "")
      continue
    }
    texts.forEach((text, index) => {
      lines.push(`${index + 1}. ${truncate(text, MAX_SNIPPET)}`)
    })
    lines.push("")
  }
  await writeRedacted(collector, path.join(dir, "index.md"), lines.join("\n"), written)
}

async function generateOutputs(
  sessions: RawSession[],
  collector: RedactionCollector,
  written: string[],
  dir: string,
  config: NoitConfig,
): Promise<void> {
  const lines = [header("Отговори от ИИ", config)]
  for (const session of sessions) {
    const texts = assistantTexts(session.messages)
    lines.push(`## ${session.title} (${session.id})`, "", `- ${link(session.id)}`, "")
    if (texts.length === 0) {
      lines.push(`- ${NOT_APPLICABLE}: няма записани отговори.`, "")
      continue
    }
    for (const text of texts) lines.push(`- ${truncate(text, MAX_SNIPPET)}`)
    lines.push("")
  }
  await writeRedacted(collector, path.join(dir, "index.md"), lines.join("\n"), written)
}

async function generateInputs(
  sessions: RawSession[],
  log: LogEntry[],
  collector: RedactionCollector,
  written: string[],
  dir: string,
  config: NoitConfig,
): Promise<void> {
  const lines = [header("Входни данни и извиквания на инструменти", config)]
  const logged = log.filter((entry) => entry.type === "tool")
  if (logged.length > 0) {
    lines.push("## Дневник на инструментите", "")
    for (const entry of logged) {
      const detail = truncate(JSON.stringify(entry.input ?? {}), MAX_SNIPPET)
      lines.push(`- [${statusLabel(entry.status)}] ${entry.tool ?? "инструмент"} (${entry.sessionID ?? "?"}): ${detail}`)
    }
    lines.push("")
  }
  for (const session of sessions) {
    const calls = toolInputs(session.messages)
    lines.push(`## ${session.title} (${session.id})`, "", `- ${link(session.id)}`, "")
    if (calls.length === 0) {
      lines.push(`- ${NOT_APPLICABLE}: няма записани инструменти.`, "")
      continue
    }
    for (const call of calls) {
      lines.push(`- ${call.name}: ${truncate(JSON.stringify(call.input ?? {}), MAX_SNIPPET)}`)
    }
    lines.push("")
  }
  await writeRedacted(collector, path.join(dir, "index.md"), lines.join("\n"), written)
}

async function generateChanges(
  ctx: PluginContext,
  collector: RedactionCollector,
  written: string[],
  dir: string,
  config: NoitConfig,
): Promise<void> {
  const lines = [header("Собствени промени", config)]
  let diffText = ""
  try {
    const output = (await ctx.vcs.diff({ mode: "working", context: 3 })) as unknown as {
      data?: { file: string; patch: string; additions: number; deletions: number; status: string }[]
    }
    const files = output?.data ?? []
    lines.push(`Открити файлове: ${files.length}`, "")
    for (const file of files) {
      lines.push(`- [${file.status}] ${file.file} (+${file.additions}/-${file.deletions})`)
      diffText += `\n### ${file.file}\n\n\`\`\`diff\n${file.patch}\n\`\`\`\n`
    }
    if (files.length === 0) lines.push(`- ${NOT_APPLICABLE}: няма промени в работното дърво.`, "")
  } catch {
    lines.push(`- ${UNKNOWN}: VCS информацията не е налична.`, "")
  }
  await writeRedacted(collector, path.join(dir, "index.md"), lines.join("\n"), written)
  await writeRedacted(collector, path.join(dir, "working.diff"), diffText, written)
}

async function generateTests(
  log: LogEntry[],
  collector: RedactionCollector,
  written: string[],
  dir: string,
  config: NoitConfig,
): Promise<void> {
  const testCalls = log.filter((entry) => {
    if (entry.type !== "tool") return false
    const name = `${entry.tool ?? ""} ${JSON.stringify(entry.input ?? {})}`.toLowerCase()
    return name.includes("test") || name.includes("pytest") || name.includes("jest") || name.includes("vitest")
  })
  const lines = [
    header("Тестове и ограничения", config),
    "## Изпълнени проверки",
    "",
  ]
  if (testCalls.length === 0) {
    lines.push(`- ${UNKNOWN}: не са открити автоматично разпознати тестови изпълнения.`, "")
  } else {
    for (const call of testCalls) {
      lines.push(`- [${statusLabel(call.status)}] ${truncate(JSON.stringify(call.input ?? {}), MAX_SNIPPET)}`)
    }
    lines.push("")
  }
  lines.push("## Ограничения", "", `- ${UNKNOWN}: опишете ръчно ограниченията на решението.`, "")
  await writeRedacted(collector, path.join(dir, "limitations.md"), lines.join("\n"), written)
}

function generateKarta(
  config: NoitConfig,
  hasSessions: boolean,
  sessions: RawSession[],
): string {
  const MANUAL = "«попълнете ръчно»"
  const areas = [
    "Разработване на проекта — идеи, код, данни, визуални и други материали",
    "Документация, презентация и материали за представяне",
    "ИИ като част от функционалността на проекта",
  ]

  const known = (value: string | undefined): string => {
    if (typeof value !== "string") return MANUAL
    const trimmed = value.trim()
    if (trimmed.length === 0 || trimmed === UNKNOWN || trimmed === NOT_APPLICABLE) return MANUAL
    return trimmed
  }

  const listOrManual = (values: Set<string>): string => {
    const list = [...values].sort((a, b) => a.localeCompare(b))
    return list.length > 0 ? list.join(", ") : MANUAL
  }

  const messageTime = (message: unknown): number | undefined => {
    const created = (message as { time?: { created?: unknown } })?.time?.created
    return typeof created === "number" && Number.isFinite(created) ? created : undefined
  }

  const messageModel = (message: unknown): string | undefined => {
    const model = (message as { model?: { id?: unknown; modelID?: unknown } })?.model
    const id = model?.id ?? model?.modelID
    return typeof id === "string" && id.length > 0 ? id : undefined
  }

  const messageAgent = (message: unknown): string | undefined => {
    const agent = (message as { agent?: unknown })?.agent
    return typeof agent === "string" && agent.length > 0 ? agent : undefined
  }

  const touchedFiles = (input: unknown): string[] => {
    if (!input || typeof input !== "object") return []
    const record = input as Record<string, unknown>
    const out: string[] = []
    for (const key of ["filePath", "path", "file"]) {
      const value = record[key]
      if (typeof value !== "string") continue
      const trimmed = value.trim()
      if (trimmed.length === 0 || trimmed === "." || trimmed === "..") continue
      out.push(trimmed)
    }
    return out
  }

  const times: number[] = []
  const models = new Set<string>()
  const agents = new Set<string>()
  const toolNames = new Set<string>()
  const files = new Set<string>()
  let userCount = 0
  let assistantCount = 0
  let toolCount = 0

  for (const session of sessions) {
    userCount += userTexts(session.messages).length
    assistantCount += assistantTexts(session.messages).length
    for (const message of session.messages) {
      const time = messageTime(message)
      if (time !== undefined) times.push(time)
      const model = messageModel(message)
      if (model) models.add(model)
      const agent = messageAgent(message)
      if (agent) agents.add(agent)
    }
    for (const call of toolInputs(session.messages)) {
      toolCount += 1
      toolNames.add(call.name)
      for (const file of touchedFiles(call.input)) files.add(file)
    }
  }

  times.sort((a, b) => a - b)
  const day = (ms: number): string => new Date(ms).toISOString().slice(0, 10)
  const sessionRange =
    sessions.length > 0
      ? `${sessions[0]?.id ?? UNKNOWN} → ${sessions[sessions.length - 1]?.id ?? UNKNOWN}`
      : MANUAL
  const dateRange =
    times.length > 0 ? `${day(times[0])} → ${day(times[times.length - 1])} (UTC)` : MANUAL

  const fileList = [...files].sort((a, b) => a.localeCompare(b))
  const fileLimit = 20
  const filesText =
    fileList.length === 0
      ? MANUAL
      : fileList.length <= fileLimit
        ? fileList.join(", ")
        : `${fileList.slice(0, fileLimit).join(", ")} … и още ${fileList.length - fileLimit}`

  const records = config.export.records.map((record) => record.id).join(", ")
  const participants = Array.isArray(config.participants) ? config.participants : []
  const checkbox = (checked: boolean): string => (checked ? "[x]" : "[ ]")

  return [
    header("Приложение 5 — Карта на използван ИИ инструмент (A01)", config),
    "> Полетата, които не могат да се извлекат от внесените сесии, са отбелязани с «попълнете ръчно» и се попълват от участника.",
    "",
    "## Идентификация на инструмента",
    "",
    "- Име на инструмента: opencode",
    "- Идентификатор в декларацията: A01",
    `- Доставчик/разработчик: ${known(config.manual.provider)}`,
    `- Версия на инструмента: opencode ${config.environment.opencodeVersion} (от плъгина и внесените сесии)`,
    `- Дата на инсталиране/достъп: ${MANUAL}`,
    "",
    "## Начин и условия на достъп",
    "",
    `- Начин на достъп (акаунт/абонамент/безплатен план): ${known(config.manual.access)}`,
    "",
    "## Роля на инструмента в проекта",
    "",
    `- Роля: ${known(config.manual.role)}`,
    `- Цели: ${known(config.manual.goals)}`,
    `- Свързани дневници: ${records || NOT_APPLICABLE}`,
    "",
    "## Области на използване",
    "",
    `- ${checkbox(hasSessions)} ${areas[0]}`,
    `- ${checkbox(false)} ${areas[1]}`,
    `- ${checkbox(false)} ${areas[2]}`,
    "",
    hasSessions
      ? `> Само „${areas[0]}“ се отбелязва автоматично (при внесени сесии). Останалите области остават празни — попълнете ръчно.`
      : "> Няма внесени сесии — областите остават празни: попълнете ръчно.",
    "",
    "## Конфигурация и настройки",
    "",
    `- Настройки и параметри: ${known(config.manual.settings)}`,
    "",
    "## Среда и модел",
    "",
    `- Среда и зависимости: ${known(config.manual.environment)}`,
    `- Версия на opencode: ${config.environment.opencodeVersion} (от плъгина и внесените сесии)`,
    `- Модели, използвани в сесиите: ${listOrManual(models)}`,
    `- Агенти, използвани в сесиите: ${listOrManual(agents)}`,
    "",
    "## Ограничения и забранени употреби",
    "",
    `- Ограничения: ${known(config.manual.limitations)}`,
    "- Забранени употреби: пароли, API ключове и данни за лични профили не се събират.",
    "",
    "## Промени в услугата/версията през периода",
    "",
    `- ${known(config.manual.serviceChanges)}`,
    "",
    "## Проверка на резултатите от човека",
    "",
    `- ${known(config.manual.verification)}`,
    "",
    "## Собствен принос на участника и разграничение от приноса на ИИ",
    "",
    `- ${known(config.manual.ownWork)}`,
    ...(participants.length > 0
      ? [
          `- Участници: виж [Декларация](./declaration.md) — списъкът е в раздел „${participants.length > 1 ? "Участници" : "Участник"}“; данните не се повтарят в тази карта.`,
        ]
      : []),
    "",
    "## Данни от внесените сесии (автоматично)",
    "",
    `- Диапазон на сесиите: ${sessionRange}`,
    `- Брой сесии: ${sessions.length}`,
    `- Потребителски заявки: ${userCount}`,
    `- Отговори от ИИ: ${assistantCount}`,
    `- Извиквания на инструменти: ${toolCount}`,
    `- Период на използване: ${dateRange}`,
    `- Инструменти, използвани в сесиите: ${listOrManual(toolNames)}`,
    `- Файлове, докоснати от инструментите: ${filesText}`,
    "",
    "## Подписи",
    "",
    `- Подпис: ${UNKNOWN}`,
    `- Дата: ${UNKNOWN}`,
    "",
    "## Препратки",
    "",
    "- [Дневник B01](./records-B01.md)",
    "- [Декларация](./declaration.md)",
    "- [Раздел V](./section-V.md)",
    "",
    "> За всеки друг ИИ инструмент извън opencode (A02, A03, …) създайте отделна карта ръчно.",
    "",
  ].join("\n")
}

function generateToolKarta(config: NoitConfig, tool?: ToolInfo): string {
  const MANUAL = "«попълнете ръчно»"
  const checkbox = "[ ]"
  const areas = [
    "Разработване на проекта — идеи, код, данни, визуални и други материали",
    "Документация, презентация и материали за представяне",
    "ИИ като част от функционалността на проекта",
  ]
  const records = config.export.records.map((record) => record.id).join(", ")
  const id = tool?.id ?? "A02"
  const name = tool?.name ?? MANUAL
  const provider = tool?.provider ?? MANUAL
  const intro = tool
    ? `> Карта за декларирания в конфигурацията ИИ инструмент „${name}“ (${id}). Полетата с «попълнете ръчно» се попълват от участника.`
    : "> Общ шаблон за втори ИИ инструмент (A02, A03, …): в конфигурацията няма списък с инструменти, затова е генерирана една обща карта. Полетата с «попълнете ръчно» се попълват от участника; при втори инструмент копирайте файла като karta-A03.md и т.н."
  const identification = [
    `- Име на инструмента: ${name}`,
    `- Идентификатор в декларацията: ${id}`,
    `- Доставчик/разработчик: ${provider}`,
  ]
  if (tool?.notes !== undefined) identification.push(`- Бележки: ${tool.notes}`)
  return [
    header(`Приложение 5 — Карта на използван ИИ инструмент (${id})`, config),
    intro,
    "",
    "## Идентификация на инструмента",
    "",
    ...identification,
    `- Версия на инструмента: ${MANUAL}`,
    `- Дата на инсталиране/достъп: ${MANUAL}`,
    "",
    "## Начин и условия на достъп",
    "",
    `- Начин на достъп (акаунт/абонамент/безплатен план): ${MANUAL}`,
    "",
    "## Роля на инструмента в проекта",
    "",
    `- Роля: ${MANUAL}`,
    `- Цели: ${MANUAL}`,
    `- Свързани дневници: ${records || NOT_APPLICABLE}`,
    "",
    "## Области на използване",
    "",
    `- ${checkbox} ${areas[0]}`,
    `- ${checkbox} ${areas[1]}`,
    `- ${checkbox} ${areas[2]}`,
    "",
    "## Конфигурация и настройки",
    "",
    `- Настройки и параметри: ${MANUAL}`,
    "",
    "## Среда и модел",
    "",
    `- Среда и зависимости: ${MANUAL}`,
    `- Версия на opencode: ${MANUAL}`,
    `- Модели, използвани в сесиите: ${MANUAL}`,
    `- Агенти, използвани в сесиите: ${MANUAL}`,
    "",
    "## Ограничения и забранени употреби",
    "",
    `- Ограничения: ${MANUAL}`,
    "- Забранени употреби: пароли, API ключове и данни за лични профили не се събират.",
    "",
    "## Промени в услугата/версията през периода",
    "",
    `- ${MANUAL}`,
    "",
    "## Проверка на резултатите от човека",
    "",
    `- ${MANUAL}`,
    "",
    "## Собствен принос на участника и разграничение от приноса на ИИ",
    "",
    `- ${MANUAL}`,
    "",
    "## Данни от внесените сесии (автоматично)",
    "",
    `- Диапазон на сесиите: ${MANUAL}`,
    "- Брой сесии: неприложимо — инструментът е извън opencode.",
    "- Потребителски заявки: неприложимо — инструментът е извън opencode.",
    "- Отговори от ИИ: неприложимо — инструментът е извън opencode.",
    "- Извиквания на инструменти: неприложимо — инструментът е извън opencode.",
    `- Период на използване: ${MANUAL}`,
    `- Инструменти, използвани в сесиите: ${MANUAL}`,
    `- Файлове, докоснати от инструментите: ${MANUAL}`,
    "",
    "## Подписи",
    "",
    `- Подпис: ${MANUAL}`,
    `- Дата: ${MANUAL}`,
    "",
    "## Препратки",
    "",
    "- [Дневник B01](./records-B01.md)",
    "- [Декларация](./declaration.md)",
    "- [Раздел V](./section-V.md)",
    "",
    `> За всеки друг ИИ инструмент извън opencode (A02, A03, …) създайте отделна карта ръчно.`,
    tool
      ? "> Тази карта е генерирана автоматично от списъка export.tools в конфигурацията."
      : "> Тази карта е генерирана като общ шаблон, защото конфигурацията още не съдържа списък с инструменти; при добавяне на такъв ще се генерира отделна карта за всеки инструмент.",
    "",
  ].join("\n")
}

function generateSectionV(
  config: NoitConfig,
  sessions: RawSession[],
  written: string[],
  base: string,
): string {
  const relative = written.map((file) => path.relative(base, file).replaceAll("\\", "/"))
  const present = new Set(relative)
  const rows: { path: string; description: string }[] = []
  const directories: { path: string; description: string }[] = [
    { path: "requests/", description: "Заявки и контекст" },
    { path: "outputs/", description: "Пълни използвани отговори" },
    { path: "inputs/", description: "Входни файлове и версии" },
    { path: "changes/", description: "Собствени промени" },
    { path: "tests/", description: "Тестове и резултати" },
  ]
  for (const entry of directories) {
    if (relative.some((file) => file.startsWith(entry.path))) rows.push(entry)
  }
  if (present.has("karta-A01.md")) rows.push({ path: "karta-A01.md", description: "Карта A01" })
  const kartaFiles = relative
    .filter((file) => /^karta-A[^/]*\.md$/.test(file) && file !== "karta-A01.md")
    .sort((a, b) => a.localeCompare(b))
  for (const karta of kartaFiles) {
    const id = karta.replace(/^karta-/, "").replace(/\.md$/, "")
    rows.push({ path: karta, description: `Карта ${id}` })
  }
  const records = relative
    .filter((file) => /^records-B[^/]+\.md$/.test(file))
    .sort((a, b) => a.localeCompare(b))
  for (const record of records) {
    const id = record.replace(/^records-/, "").replace(/\.md$/, "")
    rows.push({ path: record, description: `Дневник ${id}` })
  }
  if (present.has("declaration.md")) rows.push({ path: "declaration.md", description: "Декларация" })
  rows.push({ path: "checklist.md", description: "Контролен списък" })

  const lines = [
    header("Раздел V", config),
    "## V. Опис на доказателствата",
    "",
    "| Път | Съдържание |",
    "| --- | --- |",
    ...rows.map((row) => `| \`${row.path}\` | ${row.description} |`),
    "",
    "## Сесии",
    "",
  ]
  if (sessions.length === 0) {
    lines.push(`- ${NOT_APPLICABLE}: няма сесии за този проект.`)
  } else {
    for (const session of sessions) {
      lines.push(`- ${session.id} — ${session.title ?? UNKNOWN}`)
    }
  }
  lines.push("", "## Забележки", "", `- ${UNKNOWN}: опишете ръчно неприложимите точки.`, "")
  return lines.join("\n")
}

function generateDeclaration(config: NoitConfig, hasSessions: boolean): string {
  const manual = config.manual
  const participants = Array.isArray(config.participants) ? config.participants : []
  const areas = [
    "Разработване на проекта — идеи, код, данни, визуални и други материали",
    "Документация, презентация и материали за представяне",
    "ИИ като част от функционалността на проекта",
  ]
  const checkbox = (checked: boolean): string => (checked ? "[x]" : "[ ]")
  const cell = (input: string | undefined): string => {
    if (typeof input !== "string") return UNKNOWN
    const text = input.replace(/\s+/g, " ").trim()
    if (text.length === 0 || isUnknown(text)) return UNKNOWN
    return text.replaceAll("|", "\\|")
  }
  const lines = [
    header("ДЕКЛАРАЦИЯ за използване на средства с изкуствен интелект (ИИ)", config),
  ]
  if (participants.length > 0) {
    const plural = participants.length > 1
    lines.push(
      `## ${plural ? "Участници" : "Участник"}`,
      "",
      "| Име | Клас | Училище | Град | Рег. номер на проекта |",
      "| --- | --- | --- | --- | --- |",
      ...participants.map(
        (participant) =>
          `| ${cell(participant?.name)} | ${cell(participant?.class)} | ${cell(participant?.school)} | ${cell(participant?.city)} | ${cell(participant?.projectNumber)} |`,
      ),
      "",
      `> ЕГН е лични данни: не се показва в главната форма — попълва се само в ${plural ? "блоковете" : "блока"} за подпис по-долу.`,
      "",
    )
  } else {
    lines.push(
      "## Данни",
      "",
      `- Име: ${manual.name}`,
      `- ЕГН: ${manual.egn}`,
      `- Клас: ${manual.class}`,
      `- Училище: ${manual.school}`,
      `- Град: ${manual.city}`,
      `- Рег. номер на проекта: ${manual.projectNumber}`,
      "",
    )
  }
  lines.push(
    "## Главна форма",
    "",
    "| Област | Да | Не |",
    "| --- | --- | --- |",
    `| ${areas[0]} | ${checkbox(hasSessions)} | [ ] |`,
    `| ${areas[1]} | [ ] | [ ] |`,
    `| ${areas[2]} | [ ] | [ ] |`,
    "",
    hasSessions
      ? "> Само ред 1 е отметнат автоматично в „Да“ заради внесените сесии. Редове 2 и 3 остават празни — попълнете ръчно."
      : "> Няма внесени сесии — всички клетки остават празни: попълнете ръчно.",
    "",
    "## Декларации",
    "",
    "- Сведенията са верни и пълни.",
    "- При поне едно „Да“ сме описали всичко в А/Б/В и сме приложили записи.",
    "- Разграничили сме приноса на ИИ от своя.",
    "- Можем да обясним резултати/промени/проверки.",
    "- Носим отговорност за авторски права и лични данни.",
    "- Запознати сме с възможността за дисквалификация при невярна декларация.",
    "",
  )
  if (participants.length > 0) {
    const plural = participants.length > 1
    lines.push(`## ${plural ? "Подписи" : "Подпис"}`, "")
    for (const participant of participants) {
      lines.push(
        `### Участник: ${cell(participant?.name)}`,
        "",
        `- ЕГН: ${cell(participant?.egn)}`,
        `- Подпис: ${UNKNOWN}`,
        `- Дата: ${UNKNOWN}`,
        "",
      )
    }
  } else {
    lines.push(
      "## Подписи",
      "",
      `- Участник: ${manual.participants}`,
      `- Дата: ${UNKNOWN}`,
      `- Подпис: ${UNKNOWN}`,
      "",
    )
  }
  lines.push(
    "## Приложения",
    "",
    "- [Карта A01](./karta-A01.md)",
    "- [Дневник B01](./records-B01.md)",
    "- [Раздел V](./section-V.md)",
    "- [Контролен списък](./checklist.md)",
    "",
  )
  return lines.join("\n")
}

interface ToolCallInfo {
  name: string
  input: Record<string, unknown>
  status?: string
  output?: string
  exitCode?: number
}

interface FileStat {
  path: string
  count: number
  attachment: boolean
}

interface SessionDigest {
  id: string
  title: string
  prompts: string[]
  replies: string[]
  startedAt?: number
  endedAt?: number
  calls: ToolCallInfo[]
  fileStats: Map<string, FileStat>
  toolCounts: Map<string, number>
  agents: string[]
  models: string[]
}

interface RecordPlan {
  spec: RecordSpec
  aggregate: boolean
  digests: SessionDigest[]
}

interface Topic {
  key: string
  label: string
  digests: SessionDigest[]
}

interface LogGroup {
  tool: string
  count: number
  outcome: string
  innerTools: string[]
  sessionIDs: string[]
  order: number
}

const MAX_COMMAND = 150
const MAX_OUTCOME = 140
const MAX_PROMPT = 160
const MAX_REPLY_LINE = 400
const MAX_RESULT_LINES = 48
const SUBSTANTIAL_CALLS = 5
const PROMPT_ESSENCE = 70
const ANSI_RE = /\u001b\[[0-9;]*m/g
const TOOL_FILE_KEYS = ["filePath", "path", "file"]
const TOPIC_RULES = [
  {
    key: "noit",
    label: "noit (backfill/export/check)",
    test: /noit|backfill|\btools\.(?:export|check|status|backfill)|_export\b|_check\b/i,
  },
  { key: "review", label: "преглед за бъгове", test: /bug|error|spelling|review|грешк|бъг/i },
  { key: "greeting", label: "поздрави", test: /\bsay hi\b|\bhello\b|\bhi\b|\bздрав/i },
  { key: "other", label: "други заявки", test: /(?:)/ },
]

function compact(value: string, max: number): string {
  const plain = (value ?? "").replace(ANSI_RE, "").replace(/\s+/g, " ").trim()
  if (plain.length <= max) return plain
  const slice = plain.slice(0, max)
  const cut = slice.lastIndexOf(" ")
  return `${cut > max / 2 ? slice.slice(0, cut) : slice}…`
}

function messageCreated(message: unknown): number | undefined {
  const created = (message as { time?: { created?: unknown } })?.time?.created
  return typeof created === "number" && Number.isFinite(created) ? created : undefined
}

function contentText(content: unknown): string | undefined {
  if (!Array.isArray(content)) return undefined
  const texts: string[] = []
  for (const entry of content) {
    const piece = entry as { type?: string; text?: unknown }
    if (piece?.type === "text" && typeof piece.text === "string") texts.push(piece.text)
  }
  return texts.length > 0 ? texts.join("\n") : undefined
}

function resultText(result: unknown): string | undefined {
  if (typeof result === "string") return result
  if (!result || typeof result !== "object") return undefined
  const record = result as Record<string, unknown>
  const texts: string[] = []
  if (Array.isArray(record.content)) {
    for (const entry of record.content) {
      const piece = entry as { type?: string; text?: unknown }
      if (piece?.type === "text" && typeof piece.text === "string") texts.push(piece.text)
    }
  }
  const output = record.output
  if (texts.length === 0 && output && typeof output === "object") {
    const inner = (output as Record<string, unknown>).output
    if (typeof inner === "string") texts.push(inner)
  }
  if (texts.length === 0 && typeof output === "string") texts.push(output)
  return texts.length > 0 ? texts.join("\n") : undefined
}

function pickOutcomeLine(text: string): string {
  const lines = text
    .replace(ANSI_RE, "")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
  const marker = /pass|fail|error|succeed|warning|not recognized|exited|found \d+/i
  const index = lines.findIndex((line) => marker.test(line))
  if (index >= 0) {
    const chosen = lines[index]
    if (/[.!?…]$/.test(chosen)) return compact(chosen, MAX_OUTCOME)
    return compact(lines.slice(index).join(" "), MAX_OUTCOME)
  }
  return compact(lines[0] ?? "", MAX_OUTCOME)
}

function toProjectPath(value: string, directory: string): string {
  const normalized = value.replaceAll("\\", "/").trim()
  const base = directory.replaceAll("\\", "/").replace(/\/+$/, "")
  const relative = base.length > 0 && normalized.startsWith(`${base}/`)
    ? normalized.slice(base.length + 1)
    : normalized
  return relative.replace(/^\.\//, "")
}

function looksLikeFile(value: string): boolean {
  const name = value.replaceAll("\\", "/").split("/").pop() ?? ""
  return name.includes(".") && /[A-Za-z0-9]/.test(name)
}

function sessionToolCalls(messages: unknown[]): ToolCallInfo[] {
  const out: ToolCallInfo[] = []
  for (const message of messages) {
    const item = message as { type?: string; content?: unknown }
    if (item?.type !== "assistant" || !Array.isArray(item.content)) continue
    for (const part of item.content) {
      const piece = part as {
        type?: string
        name?: string
        tool?: string
        input?: unknown
        state?: { status?: unknown; input?: unknown; content?: unknown; metadata?: unknown }
      }
      if (piece?.type !== "tool") continue
      const state = piece.state ?? {}
      const rawInput = state.input ?? piece.input ?? {}
      const input =
        rawInput && typeof rawInput === "object" ? (rawInput as Record<string, unknown>) : {}
      const metadata =
        state.metadata && typeof state.metadata === "object"
          ? (state.metadata as Record<string, unknown>)
          : {}
      out.push({
        name: piece.name ?? piece.tool ?? "инструмент",
        input,
        status: typeof state.status === "string" ? state.status : undefined,
        output: contentText(state.content),
        exitCode: typeof metadata.exit === "number" ? metadata.exit : undefined,
      })
    }
  }
  return out
}

function digestSession(session: RawSession, directory: string): SessionDigest {
  const times: number[] = []
  const models = new Set<string>()
  const agents = new Set<string>()
  for (const message of session.messages) {
    const created = messageCreated(message)
    if (created !== undefined) times.push(created)
    const model = (message as { model?: { id?: unknown; modelID?: unknown } })?.model
    const modelID = model?.id ?? model?.modelID
    if (typeof modelID === "string" && modelID.length > 0) models.add(modelID)
    const agent = (message as { agent?: unknown })?.agent
    if (typeof agent === "string" && agent.length > 0) agents.add(agent)
  }
  times.sort((a, b) => a - b)

  const calls = sessionToolCalls(session.messages)
  const fileStats = new Map<string, FileStat>()
  const toolCounts = new Map<string, number>()
  for (const call of calls) {
    toolCounts.set(call.name, (toolCounts.get(call.name) ?? 0) + 1)
    for (const key of TOOL_FILE_KEYS) {
      const value = call.input[key]
      if (typeof value !== "string") continue
      const file = toProjectPath(value, directory)
      if (!looksLikeFile(file)) continue
      const dedupe = file.toLowerCase()
      const existing = fileStats.get(dedupe)
      if (existing) existing.count += 1
      else fileStats.set(dedupe, { path: file, count: 1, attachment: false })
      break
    }
  }
  for (const message of session.messages) {
    const item = message as { type?: string; files?: unknown }
    if (item?.type !== "user" || !Array.isArray(item.files)) continue
    for (const entry of item.files) {
      const record = entry as { name?: unknown; filename?: unknown }
      const name =
        typeof record.name === "string"
          ? record.name
          : typeof record.filename === "string"
            ? record.filename
            : undefined
      if (name === undefined) continue
      const file = toProjectPath(name, directory)
      if (!looksLikeFile(file)) continue
      const dedupe = file.toLowerCase()
      const existing = fileStats.get(dedupe)
      if (existing) existing.attachment = true
      else fileStats.set(dedupe, { path: file, count: 0, attachment: true })
    }
  }

  return {
    id: session.id,
    title: session.title,
    prompts: userTexts(session.messages),
    replies: assistantTexts(session.messages),
    startedAt: times[0],
    endedAt: times[times.length - 1],
    calls,
    fileStats,
    toolCounts,
    agents: [...agents],
    models: [...models],
  }
}

function isSubstantial(digest: SessionDigest): boolean {
  if (digest.calls.length >= SUBSTANTIAL_CALLS) return true
  return digest.calls.length >= 2 && digest.replies.join("\n").length >= 200
}

function nextAutoId(used: Set<string>): string {
  let index = 2
  let id = `B${String(index).padStart(2, "0")}`
  while (used.has(id)) {
    index += 1
    id = `B${String(index).padStart(2, "0")}`
  }
  used.add(id)
  return id
}

function planRecords(
  config: NoitConfig,
  sessions: RawSession[],
  digests: Map<string, SessionDigest>,
  manualIds: string[],
): RecordPlan[] {
  const claimed = new Set<string>()
  for (const spec of config.export.records) {
    for (const id of spec.sessionIDs) claimed.add(id)
  }
  const substantial = new Set(
    sessions
      .filter((session) => {
        const digest = digests.get(session.id)
        return digest !== undefined && isSubstantial(digest)
      })
      .map((session) => session.id),
  )
  const plans: RecordPlan[] = []
  for (const spec of config.export.records) {
    const scoped =
      spec.sessionIDs.length > 0
        ? sessions.filter((session) => spec.sessionIDs.includes(session.id))
        : sessions.filter((session) => !substantial.has(session.id))
    plans.push({
      spec,
      aggregate: spec.sessionIDs.length === 0,
      digests: scoped
        .map((session) => digests.get(session.id))
        .filter((digest): digest is SessionDigest => digest !== undefined),
    })
  }
  const used = new Set<string>([...config.export.records.map((record) => record.id), ...manualIds])
  const auto = sessions
    .filter((session) => substantial.has(session.id) && !claimed.has(session.id))
    .sort((a, b) => (digests.get(a.id)?.startedAt ?? 0) - (digests.get(b.id)?.startedAt ?? 0))
  for (const session of auto) {
    const digest = digests.get(session.id)
    if (digest === undefined) continue
    const id = nextAutoId(used)
    plans.push({
      spec: {
        id,
        title: session.title,
        task: digest.prompts.length > 0 ? compact(digest.prompts[0], MAX_PROMPT) : undefined,
        sessionIDs: [session.id],
      },
      aggregate: false,
      digests: [digest],
    })
  }
  return plans
}

function stamp(ms: number): string {
  return `${new Date(ms).toISOString().slice(0, 19)}Z`
}

function periodText(digests: SessionDigest[]): string {
  const starts = digests.map((digest) => digest.startedAt).filter((value): value is number => value !== undefined)
  const ends = digests.map((digest) => digest.endedAt).filter((value): value is number => value !== undefined)
  if (starts.length === 0 || ends.length === 0) return UNKNOWN
  return `${stamp(Math.min(...starts))} → ${stamp(Math.max(...ends))} (UTC)`
}

function toolSummary(digests: SessionDigest[]): { total: number; text: string } {
  const counts = new Map<string, number>()
  for (const digest of digests) {
    for (const [name, count] of digest.toolCounts) counts.set(name, (counts.get(name) ?? 0) + count)
  }
  const total = [...counts.values()].reduce((sum, value) => sum + value, 0)
  const text = [...counts.entries()]
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([name, count]) => `${name} ×${count}`)
    .join(", ")
  return { total, text }
}

function nonCommandSummary(digests: SessionDigest[]): string {
  const counts = new Map<string, number>()
  for (const digest of digests) {
    for (const call of digest.calls) {
      if (callCommand(call) !== undefined) continue
      counts.set(call.name, (counts.get(call.name) ?? 0) + 1)
    }
  }
  return [...counts.entries()]
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([name, count]) => `${name} ×${count}`)
    .join(", ")
}

function callCommand(call: ToolCallInfo): string | undefined {
  const command = call.input.command
  if (typeof command === "string") return compact(command, MAX_COMMAND)
  if (Array.isArray(command)) {
    return compact(command.filter((value) => typeof value === "string").join(" "), MAX_COMMAND)
  }
  const code = call.input.code
  if (typeof code === "string") return compact(code, MAX_COMMAND)
  return undefined
}

function callOutcome(call: ToolCallInfo): string {
  if (call.exitCode !== undefined && call.exitCode !== 0) {
    const detail = call.output ? pickOutcomeLine(call.output) : call.status ?? "грешка"
    return `изходен код ${call.exitCode}: ${detail}`
  }
  if (call.output) {
    const line = pickOutcomeLine(call.output)
    if (line.length > 0) return line
  }
  return call.status ?? "без записан резултат"
}

function groupLog(log: LogEntry[], scopedIDs?: Set<string>): LogGroup[] {
  const map = new Map<
    string,
    {
      tool: string
      pending: number
      settled: number
      outcomes: string[]
      inner: Set<string>
      sessions: Set<string>
      order: number
    }
  >()
  let order = 0
  for (const entry of log) {
    if (entry.type !== "tool") continue
    if (scopedIDs && entry.sessionID && !scopedIDs.has(entry.sessionID)) continue
    const tool = entry.tool ?? "инструмент"
    const key = `${entry.sessionID ?? "-"}|${tool}`
    let group = map.get(key)
    if (group === undefined) {
      group = { tool, pending: 0, settled: 0, outcomes: [], inner: new Set(), sessions: new Set(), order: order++ }
      map.set(key, group)
    }
    if (entry.status === "pending") group.pending += 1
    else group.settled += 1
    if (entry.sessionID) group.sessions.add(entry.sessionID)
    if (tool === "execute") {
      const input = entry.input as { code?: unknown } | null | undefined
      const code = typeof input?.code === "string" ? input.code : ""
      for (const match of code.matchAll(/tools\.([A-Za-z][A-Za-z0-9_]*)\s*\(/g)) {
        group.inner.add(match[1])
      }
    }
    if (entry.status !== "pending") {
      const text = resultText(entry.result) ?? entry.error
      if (typeof text === "string" && text.length > 0) {
        const line = pickOutcomeLine(text)
        if (line.length > 0 && !group.outcomes.includes(line) && group.outcomes.length < 3) {
          group.outcomes.push(line)
        }
      }
    }
  }
  return [...map.values()]
    .sort((a, b) => a.order - b.order)
    .map((group) => ({
      tool: group.tool,
      count: Math.max(group.pending, group.settled, 1),
      outcome: group.outcomes.join("; "),
      innerTools: [...group.inner],
      sessionIDs: [...group.sessions],
      order: group.order,
    }))
}

function mergeGroups(groups: LogGroup[]): LogGroup[] {
  const map = new Map<string, LogGroup>()
  for (const group of groups) {
    const existing = map.get(group.tool)
    if (existing === undefined) {
      map.set(group.tool, { ...group, innerTools: [...group.innerTools], sessionIDs: [...group.sessionIDs] })
      continue
    }
    existing.count += group.count
    for (const tool of group.innerTools) {
      if (!existing.innerTools.includes(tool)) existing.innerTools.push(tool)
    }
    for (const id of group.sessionIDs) {
      if (!existing.sessionIDs.includes(id)) existing.sessionIDs.push(id)
    }
    if (existing.outcome.length === 0) existing.outcome = group.outcome
  }
  return [...map.values()].sort((a, b) => a.order - b.order)
}

function logStep(group: LogGroup): string {
  const name = group.tool === "execute" ? "execute (обвивка)" : `tools.${group.tool}()`
  const inner = group.innerTools.length > 0 ? ` (вътре: ${[...group.innerTools].sort().join(", ")})` : ""
  const outcome =
    group.tool === "execute"
      ? ""
      : group.outcome.length > 0
        ? ` → ${group.outcome}`
        : " → без записан резултат"
  return `${name} ×${group.count}${inner}${outcome}`
}

function classifyTopics(digests: SessionDigest[]): Topic[] {
  const topics: Topic[] = []
  for (const digest of digests) {
    const text = `${digest.prompts.join(" ")} ${digest.title}`
    const rule =
      TOPIC_RULES.find((candidate) => candidate.test.test(text)) ?? TOPIC_RULES[TOPIC_RULES.length - 1]
    let topic = topics.find((candidate) => candidate.key === rule.key)
    if (topic === undefined) {
      topic = { key: rule.key, label: rule.label, digests: [] }
      topics.push(topic)
    }
    topic.digests.push(digest)
  }
  return topics.sort(
    (a, b) =>
      TOPIC_RULES.findIndex((rule) => rule.key === a.key) -
      TOPIC_RULES.findIndex((rule) => rule.key === b.key),
  )
}

function promptEssences(digests: SessionDigest[]): string {
  const counts = new Map<string, number>()
  for (const digest of digests) {
    for (const prompt of digest.prompts) {
      const text = compact(prompt.replace(/^["'«„]+/, "").replace(/["'»“”]+$/, ""), PROMPT_ESSENCE)
      if (text.length === 0) continue
      counts.set(text, (counts.get(text) ?? 0) + 1)
    }
  }
  if (counts.size === 0) return "без заявки"
  const entries = [...counts.entries()].sort((a, b) => b[1] - a[1])
  const shown = entries
    .slice(0, 2)
    .map(([text, count]) => (count > 1 ? `„${text}“ ×${count}` : `„${text}“`))
  const rest = entries.length - shown.length
  return `${shown.join("; ")}${rest > 0 ? ` …+още ${rest}` : ""}`
}

function replyEssences(digests: SessionDigest[], max: number): string {
  const counts = new Map<string, number>()
  for (const digest of digests) {
    for (const reply of digest.replies) {
      const text = compact(reply.replaceAll("**", "").replaceAll("`", ""), max)
      if (text.length === 0) continue
      counts.set(text, (counts.get(text) ?? 0) + 1)
    }
  }
  return [...counts.entries()]
    .map(([text, count]) => (count > 1 ? `„${text}“ ×${count}` : `„${text}“`))
    .join("; ")
}

function replyLines(replies: string[]): string[] {
  const out: string[] = []
  const seen = new Set<string>()
  for (const reply of replies) {
    const cleaned = reply.replaceAll("**", "").replaceAll("`", "")
    for (const line of cleaned.split(/\r?\n/)) {
      const text = compact(line, MAX_REPLY_LINE)
      if (text.length === 0) continue
      const key = text.slice(0, 100).toLowerCase()
      if (seen.has(key)) continue
      seen.add(key)
      out.push(text)
      if (out.length >= MAX_RESULT_LINES) return out
    }
  }
  return out
}

function artifactsLine(digests: SessionDigest[], prompts: string): string[] {
  const merged = new Map<string, FileStat>()
  for (const digest of digests) {
    for (const [key, stat] of digest.fileStats) {
      const existing = merged.get(key)
      if (existing) {
        existing.count += stat.count
        existing.attachment = existing.attachment || stat.attachment
      } else {
        merged.set(key, { path: stat.path, count: stat.count, attachment: stat.attachment })
      }
    }
  }
  const lowered = prompts.toLowerCase()
  const mentioned = (file: string): boolean => {
    if (lowered.includes(file.toLowerCase())) return true
    const base = file.split("/").pop()?.toLowerCase() ?? ""
    return base.length > 0 && lowered.includes(base)
  }
  const entries = [...merged.values()].filter((stat) => stat.count > 0 || !mentioned(stat.path))
  if (entries.length === 0) return ["- Файлове: няма файлови операции в тези сесии."]
  entries.sort((a, b) => a.path.localeCompare(b.path))
  const parts = entries.map((stat) => {
    const markers: string[] = []
    if (stat.count > 1) markers.push(`×${stat.count}`)
    if (stat.attachment && stat.count === 0) markers.push("прикачен")
    return markers.length > 0 ? `${stat.path} (${markers.join(", ")})` : stat.path
  })
  return [`- Файлове (${entries.length}): ${parts.join(", ")}.`]
}

function topicOutcome(topic: Topic, groups: LogGroup[]): string {
  const ids = new Set(topic.digests.map((digest) => digest.id))
  const outcomes: string[] = []
  for (const group of groups) {
    if (group.tool === "execute") continue
    if (!group.sessionIDs.some((id) => ids.has(id))) continue
    const text = group.outcome.length > 0 ? group.outcome : "без записан резултат"
    if (!outcomes.includes(text) && outcomes.length < 3) outcomes.push(text)
  }
  if (outcomes.length > 0) return outcomes.join("; ")
  const replies = replyEssences(topic.digests, 90)
  if (replies.length > 0) return replies
  return "без отговор и без изпълнение"
}

function globalCoverage(plans: RecordPlan[], sessions: RawSession[], log: LogEntry[]): string {
  const covered = new Set<string>()
  let prompts = 0
  let calls = 0
  for (const plan of plans) {
    for (const digest of plan.digests) {
      covered.add(digest.id)
      prompts += digest.prompts.length
      calls += digest.calls.length
    }
  }
  const uncovered = sessions.filter((session) => !covered.has(session.id)).map((session) => session.id)
  const logCalls = mergeGroups(groupLog(log)).reduce((sum, group) => sum + group.count, 0)
  const status = uncovered.length === 0 ? "пълно покритие" : `непокрити: ${uncovered.join(", ")}`
  return `Общо в дневници B: ${covered.size} сесии / ${prompts} заявки / ${calls} извиквания на инструменти (сесии) + ${logCalls} извиквания от ai-log — ${status}.`
}

function buildAggregateRecord(
  plan: RecordPlan,
  plans: RecordPlan[],
  sessions: RawSession[],
  log: LogEntry[],
  config: NoitConfig,
): string {
  const digests = plan.digests
  const scopedIDs = new Set(digests.map((digest) => digest.id))
  const rawGroups = groupLog(log, scopedIDs)
  const groups = mergeGroups(rawGroups)
  const topics = classifyTopics(digests)
  const { total: callTotal, text: callText } = toolSummary(digests)
  const promptTotal = digests.reduce((sum, digest) => sum + digest.prompts.length, 0)
  const logEntries = log.filter(
    (entry) => entry.type === "tool" && scopedIDs.has(entry.sessionID ?? ""),
  ).length
  const setupEntries = log.filter((entry) => entry.type === "session").length

  const lines = [header(`Дневник ${plan.spec.id}: ${plan.spec.title}`, config)]
  lines.push("## Задача")
  if (plan.spec.task && !isUnknown(plan.spec.task)) lines.push(`- ${plan.spec.task}`)
  lines.push(
    `- Общ запис за рутинна помощ, групиран по теми вместо по отделни съобщения: ${digests.length} сесии, ${topics.length} теми.`,
  )
  lines.push("")
  lines.push("## Входни данни")
  lines.push(`- Период: ${periodText(digests)}.`)
  lines.push(
    `- Заявки: ${promptTotal}; извиквания на инструменти: ${callTotal}${callText.length > 0 ? ` (${callText})` : ""} + ${logEntries} записа от ai-log.`,
  )
  for (const topic of topics) {
    lines.push(`- Тема „${topic.label}“ (${topic.digests.length} сесии): ${promptEssences(topic.digests)}.`)
  }
  lines.push(...artifactsLine(digests, digests.flatMap((digest) => digest.prompts).join(" ")))
  lines.push("- Пълни записи: [raw/sessions/](../../../raw/sessions/).")
  lines.push("")
  lines.push("## Резултати")
  if (topics.length === 0) lines.push("- Няма сесии в обхвата.")
  for (const topic of topics) {
    const outcome = topicOutcome(topic, rawGroups)
    lines.push(`- Тема „${topic.label}“: ${outcome.endsWith(".") ? outcome : `${outcome}.`}`)
  }
  lines.push("")
  lines.push("## Стъпки за възпроизвеждане")
  const steps: string[] = []
  for (const group of groups) steps.push(`${steps.length + 1}. ${logStep(group)}`)
  for (const topic of topics) {
    const hasGroups = rawGroups.some((group) =>
      group.sessionIDs.some((id) => topic.digests.some((digest) => digest.id === id)),
    )
    const hasCalls = topic.digests.some((digest) => digest.calls.length > 0)
    if (hasGroups || hasCalls) continue
    const replies = replyEssences(topic.digests, 70)
    steps.push(
      `${steps.length + 1}. Тема „${topic.label}“: без команди${
        replies.length > 0 ? ` — отговор ${replies}` : ` (${topic.digests.length} сесии без отговор)`
      }.`,
    )
  }
  if (steps.length === 0) steps.push("1. Няма изпълнени команди.")
  lines.push(...steps)
  lines.push("")
  lines.push("## Покритие")
  const sessionMap = topics
    .map((topic) => `${topic.label} — ${topic.digests.map((digest) => digest.id).join(", ")}`)
    .join("; ")
  lines.push(`- Сесии (${digests.length}): ${sessionMap}.`)
  const logDetail =
    groups.length > 0
      ? ` → ${groups.reduce((sum, group) => sum + group.count, 0)} извиквания (${groups
          .map((group) => `${group.tool} ×${group.count}`)
          .join(", ")}; чакащи/завършени слети)`
      : ""
  lines.push(
    `- Заявки: ${promptTotal}; инструменти: сесии ${callTotal}${logEntries > 0 ? `, ${logEntries} записа от ai-log${logDetail}` : ""}.`,
  )
  if (setupEntries > 0) {
    lines.push(`- Служебни записи „setup“: ${setupEntries} — без решение, извън дневника.`)
  }
  const separate = plans.filter((candidate) => !candidate.aggregate)
  if (separate.length > 0) {
    lines.push(
      `- Отделни записи за съществени задачи: ${separate
        .map(
          (candidate) =>
            `[${candidate.spec.id}](./records-${candidate.spec.id}.md) — ${candidate.digests
              .map((digest) => digest.id)
              .join(", ")}`,
        )
        .join("; ")}.`,
    )
  }
  lines.push(`- ${globalCoverage(plans, sessions, log)}`)
  lines.push("")
  return lines.join("\n")
}

function buildTaskRecord(
  plan: RecordPlan,
  plans: RecordPlan[],
  sessions: RawSession[],
  log: LogEntry[],
  config: NoitConfig,
): string {
  const digests = plan.digests
  const scopedIDs = new Set(digests.map((digest) => digest.id))
  const groups = mergeGroups(groupLog(log, scopedIDs))
  const { total: callTotal, text: callText } = toolSummary(digests)
  const promptTotal = digests.reduce((sum, digest) => sum + digest.prompts.length, 0)
  const logEntries = log.filter(
    (entry) => entry.type === "tool" && scopedIDs.has(entry.sessionID ?? ""),
  ).length

  const lines = [header(`Дневник ${plan.spec.id}: ${plan.spec.title}`, config)]
  lines.push("## Задача")
  const task =
    plan.spec.task && !isUnknown(plan.spec.task)
      ? plan.spec.task
      : digests[0]?.prompts[0]
        ? compact(digests[0].prompts[0], MAX_PROMPT)
        : UNKNOWN
  lines.push(`- ${task}`)
  if (digests.length > 1) {
    lines.push(`- Обхват: ${digests.length} сесии: ${digests.map((digest) => digest.id).join(", ")}.`)
  }
  lines.push("")
  lines.push("## Входни данни")
  lines.push(
    `- Период: ${periodText(digests)}; заявки: ${promptTotal}; извиквания на инструменти: ${callTotal}${
      callText.length > 0 ? ` (${callText})` : ""
    }${logEntries > 0 ? ` + ${logEntries} записа от ai-log` : ""}.`,
  )
  lines.push(...artifactsLine(digests, digests.flatMap((digest) => digest.prompts).join(" ")))
  for (const digest of digests) lines.push(`- ${digest.id} — ${link(digest.id)}.`)
  lines.push("")
  lines.push("## Резултати")
  const results = replyLines(digests.flatMap((digest) => digest.replies))
  if (results.length === 0) {
    lines.push("- Няма текстов отговор от ИИ в сесията.")
  } else {
    for (const result of results) lines.push(`- ${result}`)
  }
  lines.push("")
  lines.push("## Стъпки за възпроизвеждане")
  const steps: string[] = []
  for (const digest of digests) {
    const who = [
      digest.agents[0] ? `агент ${digest.agents[0]}` : "",
      digest.models[0] ? `модел ${digest.models[0]}` : "",
    ]
      .filter((value) => value.length > 0)
      .join(", ")
    steps.push(
      `${steps.length + 1}. opencode — сесия ${digest.id}${who.length > 0 ? ` (${who})` : ""}.`,
    )
  }
  const nonCommand = nonCommandSummary(digests)
  if (nonCommand.length > 0) {
    steps.push(`${steps.length + 1}. ${nonCommand} по файловете от „Входни данни“.`)
  }
  for (const group of groups) steps.push(`${steps.length + 1}. ${logStep(group)}`)
  const commandCounts = new Map<string, number>()
  for (const digest of digests) {
    for (const call of digest.calls) {
      const command = callCommand(call)
      if (command === undefined) continue
      if (groups.length > 0 && call.name === "execute") continue
      const line = `\`${command}\` → ${callOutcome(call)}`
      commandCounts.set(line, (commandCounts.get(line) ?? 0) + 1)
    }
  }
  for (const [line, count] of commandCounts) {
    steps.push(`${steps.length + 1}. ${line}${count > 1 ? ` ×${count}` : ""}`)
  }
  if (steps.length === 0) steps.push("1. Няма изпълнени команди.")
  lines.push(...steps)
  lines.push("")
  lines.push("## Покритие")
  lines.push(
    `- Сесии (${digests.length}): ${digests.map((digest) => digest.id).join(", ")}; заявки: ${promptTotal}; извиквания на инструменти: ${callTotal}${
      callText.length > 0 ? ` (${callText})` : ""
    }.`,
  )
  if (logEntries > 0) {
    lines.push(
      `- записи от ai-log: ${logEntries} → ${groups
        .reduce((sum, group) => sum + group.count, 0)} извиквания (${groups
        .map((group) => `${group.tool} ×${group.count}`)
        .join(", ")}).`,
    )
  }
  lines.push(`- ${globalCoverage(plans, sessions, log)}`)
  lines.push("")
  return lines.join("\n")
}

function buildRecord(
  plan: RecordPlan,
  plans: RecordPlan[],
  sessions: RawSession[],
  log: LogEntry[],
  config: NoitConfig,
): string {
  return plan.aggregate
    ? buildAggregateRecord(plan, plans, sessions, log, config)
    : buildTaskRecord(plan, plans, sessions, log, config)
}

function oneLine(value: unknown, fallback: string): string {
  if (typeof value !== "string") return fallback
  const text = value.replace(/\s+/g, " ").trim()
  return text.length > 0 ? text : fallback
}

function buildManualRecord(
  manual: ManualRecordPlan,
  plans: RecordPlan[],
  config: NoitConfig,
): string {
  const record = manual.record
  const id = manual.id
  const title = oneLine(record.title, `Ръчен запис ${id}`)
  const task = oneLine(record.task, "")
  const taskText =
    task.length > 0 && !isUnknown(task)
      ? task
      : `Ръчен запис „${title}“ без допълнително описание на задачата.`
  const sessions = Array.isArray(record.sessionIDs)
    ? record.sessionIDs.filter((value): value is string => typeof value === "string" && value.length > 0)
    : []

  const coverBy = new Map<string, string[]>()
  for (const plan of plans) {
    for (const digest of plan.digests) {
      const list = coverBy.get(digest.id) ?? []
      if (!list.includes(plan.spec.id)) list.push(plan.spec.id)
      coverBy.set(digest.id, list)
    }
  }

  const lines = [header(`Дневник ${id}: ${title}`, config)]
  lines.push("> Ръчен запис: въведен от участника (извън автоматичното каптиране на opencode).")
  lines.push("")
  lines.push("## Задача")
  lines.push(`- ${taskText}`)
  lines.push("")
  lines.push("## Входни данни")
  lines.push(
    `- Ръчен запис ${id}: „${title}“; статус: ${record.status === "closed" ? "затворен" : "отворен"}.`,
  )
  lines.push(
    `- Период: ${oneLine(record.startedAt, UNKNOWN)} → ${oneLine(record.endedAt, UNKNOWN)}.`,
  )
  if (sessions.length === 0) {
    lines.push("- Сесии: няма — записът е изцяло ръчен (извън opencode).")
  } else {
    for (const sessionID of sessions) {
      const covering = coverBy.get(sessionID)
      if (covering !== undefined && covering.length > 0) {
        const refs = covering.map((candidate) => `[${candidate}](./records-${candidate}.md)`).join(", ")
        lines.push(
          `- Сесия ${sessionID}: обхваната в запис ${refs} — съдържанието на сесията не се дублира тук.`,
        )
      } else {
        lines.push(
          `- Сесия ${sessionID}: не е сред внесените в .noit/raw/sessions; записът остава ръчен.`,
        )
      }
    }
  }
  lines.push("")
  lines.push("## Резултати")
  lines.push(
    record.status === "closed"
      ? `- Ръчен запис ${id}: приключен на ${oneLine(record.endedAt, UNKNOWN)}; резултатите са отчетени от участника.`
      : `- Ръчен запис ${id}: отворен; резултатите се отчитат от участника при завършване.`,
  )
  lines.push("")
  lines.push("## Стъпки за възпроизвеждане")
  const steps: string[] = []
  steps.push(`Ръчен запис ${id} „${compact(title, MAX_PROMPT)}“ — въведен от участника.`)
  if (task.length > 0) steps.push(`Задача: ${compact(task, MAX_REPLY_LINE)}.`)
  if (sessions.length === 0) {
    steps.push("Изпълнете описаните от участника действия в съответния ИИ инструмент извън opencode.")
  } else {
    for (const sessionID of sessions) {
      const covering = coverBy.get(sessionID)
      if (covering !== undefined && covering.length > 0) {
        steps.push(`Сесия ${sessionID} — виж запис ${covering.join(", ")}.`)
      } else {
        steps.push(`Сесия ${sessionID} — няма внесен запис; приложете ръчно описаните действия.`)
      }
    }
  }
  steps.forEach((step, index) => lines.push(`${index + 1}. ${step}`))
  lines.push("")
  return lines.join("\n")
}

async function sha256(file: string): Promise<string> {
  const data = await fs.readFile(file)
  return crypto.createHash("sha256").update(data).digest("hex")
}

const PII_PLACEHOLDER = "[ЛИЧНИ ДАННИ]"

const PII_MANUAL_KEYS = [
  "name",
  "egn",
  "class",
  "school",
  "city",
  "projectNumber",
  "author",
  "participants",
] as const

function piiValues(config: NoitConfig): string[] {
  const values = new Set<string>()
  const push = (value: unknown): void => {
    if (typeof value !== "string") return
    const trimmed = value.trim()
    if (trimmed.length === 0 || isUnknown(trimmed)) return
    values.add(trimmed)
  }
  const manual = config.manual as unknown as Record<string, unknown>
  for (const key of PII_MANUAL_KEYS) push(manual[key])
  for (const participant of config.participants ?? []) {
    push(participant?.name)
    push(participant?.egn)
    push(participant?.class)
    push(participant?.school)
    push(participant?.city)
    push(participant?.projectNumber)
  }
  return [...values].sort((a, b) => b.length - a.length || a.localeCompare(b))
}

function replaceExactValue(text: string, value: string): string {
  const escaped = value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
  try {
    const pattern = new RegExp(`(?<![\\p{L}\\p{N}])${escaped}(?![\\p{L}\\p{N}])`, "gu")
    return text.replace(pattern, PII_PLACEHOLDER)
  } catch {
    return text.split(value).join(PII_PLACEHOLDER)
  }
}

const EGN_BACKSTOP = /(?<!\d)\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])\s?\d{4}(?!\d)/g

function stripPersonalData(text: string, config: NoitConfig): string {
  let output = text
  for (const value of piiValues(config)) output = replaceExactValue(output, value)
  return output.replace(EGN_BACKSTOP, PII_PLACEHOLDER)
}

function publicExportEnabled(ctx: PluginContext, config: NoitConfig): boolean {
  const fromConfig = (config.export as { public?: unknown }).public
  if (typeof fromConfig === "boolean") return fromConfig
  const options = (ctx as unknown as { options?: { export?: { public?: unknown } } }).options
  return options?.export?.public === true
}

async function mirrorPublicCopy(
  config: NoitConfig,
  exportBase: string,
  publicBase: string,
): Promise<number> {
  let count = 0
  const walk = async (dir: string): Promise<void> => {
    const entries = await fs.readdir(dir, { withFileTypes: true })
    for (const entry of entries) {
      const source = path.join(dir, entry.name)
      if (entry.isDirectory()) {
        await walk(source)
        continue
      }
      if (!entry.isFile()) continue
      const relative = path.relative(exportBase, source)
      const target = path.join(publicBase, relative)
      const text = stripPersonalData(await fs.readFile(source, "utf8"), config)
      const safe = redact(text).text
      await ensureDir(path.dirname(target))
      await fs.writeFile(target, safe, "utf8")
      count += 1
    }
  }
  await walk(exportBase)
  return count
}

export async function runExport(
  ctx: PluginContext,
  config: NoitConfig,
  _options: { force?: boolean } = {},
): Promise<ExportSummary> {
  const base = exportDir(ctx)
  const requestsDir = path.join(base, "requests")
  const outputsDir = path.join(base, "outputs")
  const inputsDir = path.join(base, "inputs")
  const changesDir = path.join(base, "changes")
  const testsDir = path.join(base, "tests")
  for (const dir of [base, requestsDir, outputsDir, inputsDir, changesDir, testsDir]) {
    await ensureDir(dir)
  }

  const written: string[] = []
  const collector = makeCollector()

  const sessions = await readRawSessions(ctx)
  const log = await readLog(ctx)

  await generateRequests(sessions, collector, written, requestsDir, config)
  await generateOutputs(sessions, collector, written, outputsDir, config)
  await generateInputs(sessions, log, collector, written, inputsDir, config)
  await generateChanges(ctx, collector, written, changesDir, config)
  await generateTests(log, collector, written, testsDir, config)

  if (config.export.includeKarta) {
    await writeRedacted(
      collector,
      path.join(base, "karta-A01.md"),
      generateKarta(config, sessions.length > 0, sessions),
      written,
    )
    const tools = (Array.isArray(config.export.tools) ? config.export.tools : []).filter(
      (tool): tool is ToolInfo =>
        Boolean(tool) &&
        typeof tool === "object" &&
        typeof tool.id === "string" &&
        /^A\d{2,}$/.test(tool.id) &&
        tool.id !== "A01" &&
        typeof tool.name === "string" &&
        tool.name.trim().length > 0,
    )
    if (tools.length > 0) {
      for (const tool of tools) {
        await writeRedacted(
          collector,
          path.join(base, `karta-${tool.id}.md`),
          generateToolKarta(config, tool),
          written,
        )
      }
    } else {
      await writeRedacted(
        collector,
        path.join(base, "karta-A02.md"),
        generateToolKarta(config),
        written,
      )
    }
  }

  const digests = new Map<string, SessionDigest>()
  for (const session of sessions) {
    digests.set(session.id, digestSession(session, config.project.directory))
  }
  const manualRecords = await readRecords(ctx)
  const usedIds = new Set<string>()
  for (const spec of config.export.records) {
    if (typeof spec.id === "string" && spec.id.length > 0) usedIds.add(spec.id)
  }
  const manualPlans = planManualRecords(manualRecords, usedIds)
  const plans = planRecords(
    config,
    sessions,
    digests,
    manualPlans.map((plan) => plan.id),
  )
  for (const plan of plans) {
    await writeRedacted(
      collector,
      path.join(base, `records-${plan.spec.id}.md`),
      buildRecord(plan, plans, sessions, log, config),
      written,
    )
  }
  for (const manual of manualPlans) {
    await writeRedacted(
      collector,
      path.join(base, `records-${manual.id}.md`),
      buildManualRecord(manual, plans, config),
      written,
    )
  }

  if (config.export.includeDeclaration) {
    await writeRedacted(
      collector,
      path.join(base, "declaration.md"),
      generateDeclaration(config, sessions.length > 0),
      written,
    )
  }

  await writeRedacted(
    collector,
    path.join(base, "section-V.md"),
    generateSectionV(config, sessions, written, base),
    written,
  )

  const findings = collector.list()
  const redactionReport = {
    generatedAt: new Date().toISOString(),
    project: config.project.name,
    total: collector.total(),
    findings,
    note: "Суровите .noit/ai-log.jsonl и .noit/raw/ не се редактират и са в .gitignore.",
  }
  await fs.writeFile(
    path.join(base, "redaction-report.json"),
    JSON.stringify(redactionReport, null, 2),
    "utf8",
  )
  written.push(path.join(base, "redaction-report.json"))

  const checklistFile = path.join(base, "checklist.md")
  if (!(await pathExists(checklistFile))) {
    await fs.writeFile(
      checklistFile,
      "# Контролен списък\n\n- Този файл се презаписва от проверката при експорт.\n",
      "utf8",
    )
  }

  const manifestFile = path.join(base, "manifest.json")
  const listExportFiles = async (dir: string): Promise<string[]> => {
    const files: string[] = []
    const entries = await fs.readdir(dir, { withFileTypes: true })
    for (const entry of entries) {
      const full = path.join(dir, entry.name)
      if (entry.isDirectory()) files.push(...(await listExportFiles(full)))
      else if (entry.isFile()) files.push(full)
    }
    return files
  }
  const buildManifest = async (): Promise<{ path: string; bytes: number; sha256: string }[]> => {
    const files: { path: string; bytes: number; sha256: string }[] = []
    for (const file of await listExportFiles(base)) {
      const relative = path.relative(base, file).replaceAll("\\", "/")
      if (relative === "manifest.json") continue
      const stat = await fs.stat(file)
      files.push({ path: relative, bytes: stat.size, sha256: await sha256(file) })
    }
    return files.sort((a, b) => a.path.localeCompare(b.path))
  }
  const writeManifest = async () => {
    const data = {
      generatedAt: new Date().toISOString(),
      project: config.project.name,
      directory: config.project.directory,
      plugin: `${config.environment.plugin}@${config.environment.pluginVersion}`,
      opencodeVersion: config.environment.opencodeVersion,
      sessions: sessions.map((session) => session.id),
      redactions: collector.total(),
      files: await buildManifest(),
    }
    await fs.writeFile(manifestFile, JSON.stringify(data, null, 2), "utf8")
    return data
  }

  await writeManifest()

  const { runCheck } = await import("./check.ts")
  await runCheck(ctx, config, { strict: false })
  const manifest = await writeManifest()

  let publicNote = ""
  if (publicExportEnabled(ctx, config)) {
    const publicBase = path.join(noitRoot(ctx), "public")
    const copied = await mirrorPublicCopy(config, base, publicBase)
    publicNote = `; публично копие без лични данни: ${copied} файла в ${publicBase}`
  }

  return {
    generatedAt: manifest.generatedAt,
    sessions: sessions.length,
    files: [...manifest.files.map((f) => f.path), "manifest.json"],
    redactions: collector.total(),
    message:
      `Експорт: ${sessions.length} сесии, ${manifest.files.length + 1} файла, ` +
      `${collector.total()} редакции в ${base}${publicNote}.`,
  }
}

