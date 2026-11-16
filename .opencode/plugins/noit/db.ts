import { DatabaseSync } from "node:sqlite"
import os from "node:os"
import path from "node:path"

export interface DbSession {
  id: string
  projectID?: string
  parentID?: string
  directory?: string
  title?: string
  time?: { created?: number; updated?: number }
  version?: string
  source: "session_v2" | "session"
}

export interface SessionMessagesInfo {
  messages: unknown[]
  hasRows: boolean
}

export function normalizeDirectory(value: string | null | undefined): string {
  if (!value) return ""
  return value.replaceAll("\\", "/").replace(/\/+$/, "").toLowerCase()
}

export function databasePath(): string {
  return (
    process.env.OPENCODE_DB ||
    path.join(os.homedir(), ".local", "share", "opencode", "opencode.db")
  )
}

export function isBusyError(error: unknown): boolean {
  if (typeof error === "string") return /SQLITE_BUSY|SQLITE_LOCKED|database is locked/i.test(error)
  if (!error || typeof error !== "object") return false
  const record = error as { code?: unknown; errcode?: unknown; message?: unknown }
  if (record.errcode === 5 || record.errcode === "5") return true
  if (typeof record.code === "string" && /SQLITE_BUSY|SQLITE_LOCKED/i.test(record.code)) return true
  return (
    typeof record.message === "string" &&
    /SQLITE_BUSY|SQLITE_LOCKED|database is locked|database table is locked/i.test(record.message)
  )
}

export async function withBusyRetry<T>(task: () => T): Promise<T> {
  try {
    return task()
  } catch (error) {
    if (!isBusyError(error)) throw error
    await new Promise((resolve) => setTimeout(resolve, 150))
    return task()
  }
}

function openDb(): DatabaseSync | undefined {
  try {
    return new DatabaseSync(databasePath(), { readOnly: true })
  } catch (error) {
    if (isBusyError(error)) throw error
    return undefined
  }
}

export function projectIdsForDirectory(directory: string): string[] {
  const target = normalizeDirectory(directory)
  if (target.length === 0) return []
  const db = openDb()
  if (!db) return []
  try {
    const rows = db.prepare("SELECT id, worktree FROM project").all() as any[]
    const out: string[] = []
    for (const row of rows) {
      if (normalizeDirectory(row.worktree) !== target) continue
      const id = row.id ? String(row.id) : ""
      if (id.length > 0 && !out.includes(id)) out.push(id)
    }
    return out
  } catch (error) {
    if (isBusyError(error)) throw error
    return []
  } finally {
    db.close()
  }
}

export function listSessions(
  projectId: string | undefined,
  directory: string,
): DbSession[] {
  const db = openDb()
  if (!db) return []
  const target = normalizeDirectory(directory)
  const out: DbSession[] = []
  const seen = new Set<string>()
  try {
    for (const table of ["session_v2", "session"] as const) {
      try {
        const rows = db
          .prepare(
            `SELECT id, project_id, parent_id, directory, title, time_created, time_updated, version FROM ${table}`,
          )
          .all() as any[]
        for (const row of rows) {
          const id = String(row.id)
          if (seen.has(id)) continue
          const rowDir = normalizeDirectory(row.directory)
          const rowProject = row.project_id ? String(row.project_id) : undefined
          const dirMatch = rowDir.length > 0 && rowDir === target
          const projectMatch = Boolean(projectId) && rowProject === projectId
          if (!dirMatch && !projectMatch) continue
          seen.add(id)
          out.push({
            id,
            projectID: rowProject,
            parentID: row.parent_id ?? undefined,
            directory: row.directory ?? undefined,
            title: row.title ?? undefined,
            time: {
              created: row.time_created ?? undefined,
              updated: row.time_updated ?? undefined,
            },
            version: row.version ?? undefined,
            source: table,
          })
        }
      } catch (error) {
        if (isBusyError(error)) throw error
      }
    }
  } finally {
    db.close()
  }
  return out.sort((a, b) => (b.time?.updated ?? 0) - (a.time?.updated ?? 0))
}

export function findSession(sessionId: string): DbSession | undefined {
  const db = openDb()
  if (!db) return undefined
  try {
    for (const table of ["session_v2", "session"] as const) {
      try {
        const row = db
          .prepare(
            `SELECT id, project_id, parent_id, directory, title, time_created, time_updated, version FROM ${table} WHERE id = ?`,
          )
          .get(sessionId) as any
        if (row) {
          return {
            id: String(row.id),
            projectID: row.project_id ? String(row.project_id) : undefined,
            parentID: row.parent_id ?? undefined,
            directory: row.directory ?? undefined,
            title: row.title ?? undefined,
            time: {
              created: row.time_created ?? undefined,
              updated: row.time_updated ?? undefined,
            },
            version: row.version ?? undefined,
            source: table,
          }
        }
      } catch (error) {
        if (isBusyError(error)) throw error
      }
    }
    return undefined
  } catch (error) {
    if (isBusyError(error)) throw error
    return undefined
  } finally {
    db.close()
  }
}

export function readMessages(sessionId: string): unknown[] {
  return readMessagesDetailed(sessionId).messages
}

export function readMessagesDetailed(sessionId: string): SessionMessagesInfo {
  const db = openDb()
  if (!db) return { messages: [], hasRows: false }
  try {
    const rows = db
      .prepare(
        "SELECT type, seq, time_created, data FROM session_message WHERE session_id = ? ORDER BY seq ASC",
      )
      .all(sessionId) as any[]
    if (rows.length > 0) {
      const messages: unknown[] = []
      for (const row of rows) {
        try {
          messages.push(normalizeSessionMessage(row))
        } catch {}
      }
      return { messages, hasRows: true }
    }

    const messages = db
      .prepare(
        "SELECT id, time_created, data FROM message WHERE session_id = ? ORDER BY time_created ASC",
      )
      .all(sessionId) as any[]
    if (messages.length === 0) return { messages: [], hasRows: false }

    const parts = db
      .prepare(
        "SELECT id, message_id, time_created, data FROM part WHERE session_id = ? ORDER BY time_created ASC",
      )
      .all(sessionId) as any[]
    const byMessage = new Map<string, any[]>()
    for (const part of parts) {
      const key = String(part.message_id)
      const list = byMessage.get(key) ?? []
      list.push(JSON.parse(part.data))
      byMessage.set(key, list)
    }
    const built: unknown[] = []
    for (const message of messages) {
      try {
        const data = JSON.parse(message.data)
        const role = data.role ?? "user"
        const content = byMessage.get(String(message.id)) ?? []
        if (role === "user") {
          const text = content
            .filter((part) => part.type === "text")
            .map((part) => part.text)
            .join("\n")
          built.push({
            type: "user",
            text,
            time: data.time,
            files: data.files ?? [],
            summary: data.summary,
          })
        } else {
          built.push({
            type: "assistant",
            content,
            time: data.time,
            model: data.model,
            agent: data.agent,
            mode: data.mode,
          })
        }
      } catch {}
    }
    return { messages: built, hasRows: true }
  } catch (error) {
    if (isBusyError(error)) throw error
    return { messages: [], hasRows: false }
  } finally {
    db.close()
  }
}

function normalizeSessionMessage(row: any): unknown {
  const data = JSON.parse(row.data)
  if (row.type === "user") {
    return {
      type: "user",
      text: data.text ?? "",
      files: data.files ?? [],
      time: data.time ?? { created: row.time_created },
      seq: row.seq,
    }
  }
  if (row.type === "assistant") {
    return {
      type: "assistant",
      content: data.content ?? [],
      model: data.model,
      agent: data.agent,
      time: data.time ?? { created: row.time_created },
      seq: row.seq,
    }
  }
  if (row.type === "idle") {
    return {
      type: "idle",
      time: data.time ?? { created: row.time_created },
      outcome: data.outcome,
      seq: row.seq,
    }
  }
  return { type: row.type, data, seq: row.seq }
}
