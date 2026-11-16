# noit.ai-declaration

Project-scoped [opencode](https://opencode.ai) plugin that generates the Bulgarian НОИТ declaration **«ДЕКЛАРАЦИЯ за използване на средства с изкуствен интелект (ИИ)» (Приложение 5)** from your real opencode session logs. It imports this project's sessions from the opencode database, captures prompts and tool calls live while opencode runs, and exports a redacted Markdown package into `.noit/export/AI/`: the main declaration form (three Yes/No areas — only area 1 «Разработване на проекта — идеи, код, данни, визуални и други материали» is auto-ticked when sessions exist), Section A (`karta-A01.md`, plus one `karta-A<id>.md` per tool declared in `export.tools` — or a generic `karta-A02.md` when none are), Section B (`records-B01.md` aggregate, `records-B02.md`… per substantial task, and one `records-B<id>.md` per manual record), Section C / Раздел V (`section-V.md`, the relative-path evidence table), plus `declaration.md`, `checklist.md`, `manifest.json` and `redaction-report.json`. With `export.public: true` it also mirrors a PII-stripped copy into `.noit/public/`. A conservative 10-item checker validates completeness. All capture data stays local — the plugin makes no network calls.

AI use outside opencode (browser ChatGPT, Copilot in the IDE, etc.) cannot be captured automatically: declare those tools in `export.tools` (which generates the Section A card skeletons) and use manual `/noit-record` records for their Section B entries.

## Requirements

- **opencode 2.0.18** — the version this plugin was tested with (recorded as `environment.opencodeVersion` in `.noit/config.json`). Built against **`@opencode/plugin` 2.2.7**, declared in `package.json`.
- **Node.js 22.13 or newer** — reads the opencode database with `node:sqlite`, available without flags from 22.13 (and 23.4+); on **Windows, macOS or Linux** Windows paths are normalized when matching sessions and building evidence paths.

## Installation (new project)

1. **Copy the plugin folder** into your project so that this path exists:

   ```
   <your project>/.opencode/plugins/noit/index.ts
   ```

   Copy the whole `noit` folder (`index.ts`, `config.ts`, `types.ts`, `backfill.ts`, `db.ts`, `export.ts`, `check.ts`, `redact.ts`, `config.template.json`, `config.local.template.json`, `package.json`, `package-lock.json`). Both config templates must ship with the plugin: `config.template.json` seeds the committable base and `config.local.template.json` seeds the local overlay with sane defaults (`provider: "opencode"`, `model: "не се предоставят от доставчика"`, …).

2. **Install the plugin dependency** (one dependency: `@opencode/plugin`). Run inside the plugin folder — this is how it was installed in this repository:

   ```sh
   cd .opencode/plugins/noit && npm install
   ```

3. **Register the plugin** in your project's `opencode.json` — the exact entry used in this repository:

   ```json
   {
     "$schema": "https://opencode.ai/config.json",
     "plugins": [
       { "package": "./.opencode/plugins/noit", "options": { "backfill": { "scope": "project" } } }
     ]
   }
   ```

   Note: `options` from `opencode.json` **are** honored. Configuration precedence, merged key by key (highest first), is: plugin `options` > `.noit/config.local.json` > `.noit/config.json` > `config.template.json` / `config.local.template.json` / built-in defaults. A malformed value is ignored (the lower layer is kept) and logged to `.noit/ai-log.jsonl` as `ignored malformed values: …`. A higher layer **replaces whole arrays** (`export.tools`, `export.records`, `participants`) — there is no per-element merge. Recent opencode versions also auto-discover plugin directories directly under `.opencode/plugins/`, but registering the entry explicitly (as this repository does) is deterministic.

4. **Restart opencode.** On first load the plugin creates `.noit/config.json` (the committable base: `project`, `environment`, `export`, `backfill`) and `.noit/config.local.json` (the local overlay: `manual.*`, `participants`), runs a project-scoped reconcile (imports this project's sessions that are not yet in its state), and starts capturing.

5. **Verify it loaded:**
   - `.noit/config.json`, `.noit/config.local.json` and `.noit/debug-ctx.json` appear in the project root;
   - `.noit/ai-log.jsonl` gets a line `setup noit.ai-declaration@0.1.0 for <project>`;
   - `/noit-status` prints a one-line summary (project, captured session count, last backfill); ask the model to call the `noit_status` tool for the full report — project and directory, project id, backfill scope/source, versions, `installedAt`/`lastBackfill`/`imported`, `ses_*.json` count, ai-log entries, record count, export directory.

## Quick start

1. **Fill `.noit/config.local.json`.** The configuration is two files:
   - `.noit/config.json` — the committable base: `project`, `environment`, `export` (including `export.public` and `export.tools`) and `backfill`. It contains no personal data and is designed to be committed, even to a public repository.
   - `.noit/config.local.json` — the local, gitignored overlay: `manual.*` (the declaration data a human must fill in) and `participants`. **This is the file you edit** for names, ЕГН, class, school, city, project number, goals, access, own work, verification and limitations.

   On a fresh install both files are materialized from their templates. Open `config.local.json` and replace every placeholder in `manual`. All fields are strings; the checker treats empty values, `непознато`, `попълнете ръчно`, dashes (`-`, `—`, `–`), `null`/`undefined` and anything containing `todo`/`tbd` as missing. `неприложимо` is accepted — use it only when the field genuinely does not apply.

   | Field | What to put |
   | --- | --- |
   | `name` | Full name as it must appear on the declaration |
   | `egn` | ЕГН (personal ID — local only, never commit) |
   | `class` | Class, e.g. `11 Б` |
   | `school` | School name |
   | `city` | City |
   | `projectNumber` | Project registration number from the competition |
   | `participants` | Free-text participant names printed in the declaration signature block |
   | `author` | Author(s) of the work |
   | `organization` | School/organization submitting |
   | `role` | Role of the AI tool in the project |
   | `goals` | Goals of the AI use |
   | `taskDescription` | Tasks for which AI was used |
   | `documentation` | Documentation produced/used with AI |
   | `aiAsFunctionality` | Whether AI is part of the project's functionality (template default: `неприложимо`) |
   | `model` | Model(s) used (template default: `не се предоставят от доставчика`) |
   | `provider` | Provider (template default: `opencode`) |
   | `settings` | Settings and parameters used (template default: `неприложимо`) |
   | `environment` | Environment and dependencies |
   | `access` | Access method — account / subscription / free plan |
   | `serviceChanges` | Changes in the service/version during the period (template default: `неприложимо`) |
   | `ownWork` | Own contribution vs. the AI contribution |
   | `verification` | How the human verified the results |
   | `limitations` | Limitations of the AI use |

   `participants` (the array, next to `manual`) is the structured signer list: `{"name", "egn", "class", "school", "city", "projectNumber"}` per entry. Values are trimmed; the list is capped at **10** entries (extra entries are dropped and logged). When the array is empty, the plugin falls back to a single participant derived from the six matching `manual.*` fields, so the classic one-person workflow stays valid. The list feeds the `participants` checker item („Участници: име, подпис и дата“), the PII stripping of the public copy and the declaration rendering. Note: the declaration prints the list as a participants table (name, class, school, city, project number — ЕГН is deliberately left out of the table) plus one signature block per participant, with ЕГН appearing only in the signature block; signatures/dates remain placeholders until filled by hand.

2. **Backfill** (normally not needed — it already runs at every startup and after each captured session, incrementally). To force re-import of every strictly scoped session, including ones already imported:

   ```
   /noit-backfill
   ```

3. **Work normally.** Prompts and tool calls are appended to `.noit/ai-log.jsonl` and redacted as they are written; a full transcript dump lands in `.noit/raw/sessions/` the moment the session reaches a terminal signal (idle / succeeded / failed / interrupted — no delay). Section B records for substantial sessions are generated automatically at export. `/noit-record` (or `noit_record_start` / `noit_record_end`) opens and closes named records in `.noit/records/index.json`; every manual record is exported as its own `records-B<id>.md` with the `> Ръчен запис: …` marker and cross-references instead of duplicated session content (see Export below).

4. **Export:**

   ```
   /noit-export
   ```

   This writes the whole package into `.noit/export/AI/`, mirrors the PII-stripped copy into `.noit/public/` when `export.public: true`, and immediately runs the checker.

5. **Check:**

   ```
   /noit-check
   ```

   Initially expect failures on `manual-fields` and `participants` (until step 1 is done) and on Section A fields that can only be filled by hand (`Дата на инсталиране/достъп`, `Подпис`, `Дата`). `pii` fails only if `export.public` is on and the public copy still contains personal data. If there are no sessions, `Модели…` and `Агенти…` also stay as `«попълнете ръчно»`. Areas 2 and 3 of the main form are not validated — tick them by hand. Fix the files, then run `/noit-check` again to refresh `checklist.md`.

6. **What to submit.** The contents of `.noit/export/AI/` — the Markdown package. Review every generated file first (see Privacy below), fill all remaining `«попълнете ръчно»` placeholders, tick the correct areas in `declaration.md`, sign and date it. If your repository is public, submit the `.noit/public/` mirror instead (see `export.public`) and still review it by hand.

## Output layout and what to commit

```
<project>/.noit/
  config.json               # committable base (project, environment, export, backfill) — no personal data
  config.local.json         # manual fields + participants — LOCAL, gitignored, never commit
  ai-log.jsonl              # live prompt/tool log (redacted at write) — LOCAL, never commit
  debug-ctx.json            # plugin context dump written at setup — LOCAL, never commit
  records/index.json        # manual B records opened with noit_record_start — LOCAL; exported as records-B<id>.md
  raw/sessions/             # imported/live session dumps + index.json (redacted at write) — LOCAL
  export/AI/                # THE DELIVERABLE — real data; commit only to a private repo
    requests/index.md       # Section evidence: user requests
    outputs/index.md        # Section evidence: AI answers
    inputs/index.md         # Section evidence: tool calls / inputs
    changes/index.md        # Section evidence: working-tree changes (+ working.diff)
    tests/limitations.md    # detected test runs + limitations note
    karta-A01.md            # Section A — Карта на използван ИИ инструмент (A01)
    karta-A02.md            # generic tool card when export.tools is empty (else karta-A<id>.md per tool)
    records-B01.md          # Section B — aggregate record of routine sessions
    records-B02.md …        # Section B — one record per substantial session or manual record
    declaration.md          # main form + declarations + signatures
    section-V.md            # Section C / Раздел V — relative-path evidence table
    checklist.md            # checker output (rewritten by every check)
    manifest.json           # all export files except itself, with size and sha256
    redaction-report.json   # what the redactor found and replaced
  public/                   # PII-stripped mirror of export/AI (only when export.public: true)
```

For a private/competition repository, keep the raw data local and commit the base config and the export:

```gitignore
.noit/*
!.noit/config.json
!.noit/export/
```

For a **public** repository, commit only the base config and the PII-stripped mirror — `.noit/export/AI/` and `.noit/config.local.json` stay local:

```gitignore
.noit/*
!.noit/config.json
!.noit/public/
```

**Warning:** `declaration.md` contains your ЕГН and personal data, and `.noit/config.local.json` holds it for every document. Never commit either to a public repository. If your project repo is public, use the `export.public` mirror (and still review it), or keep the declaration out of the tree and submit the file directly to the competition.

## Configuration reference

Two files make the configuration:

- `.noit/config.json` (`schema: "noit-config/2"`) — the committable base: `project`, `environment`, `export`, `backfill`. Personal data is never written here. If an older single-file config still contains `manual`/`participants`, the plugin migrates them to `config.local.json` on load, keeps a timestamped `config.backup.<ts>.json`, rewrites the base without them, and logs the migration to `.noit/ai-log.jsonl`.
- `.noit/config.local.json` (`schema: "noit-local/1"`) — the local overlay: `manual.*` (all strings, defaults per the Quick start table) and `participants` (array of `{name,egn,class,school,city,projectNumber}`, capped at 10 entries, falling back to a single participant derived from the six matching `manual.*` fields when empty).

Every key can also be supplied through plugin `options` in `opencode.json`. Precedence (highest first), evaluated key by key: plugin `options` > `.noit/config.local.json` > `.noit/config.json` > `config.template.json` / `config.local.template.json` / built-in defaults. Object sections (`manual`, `project`, `environment`, `export`, `backfill`) merge per key; arrays (`export.tools`, `export.records`, `participants`) are replaced wholesale by the higher layer. Malformed values are ignored (the lower layer is kept) and logged to `.noit/ai-log.jsonl`. Missing keys fall back to built-in defaults (`непознато` for every `manual.*` field).

### Base — `.noit/config.json`

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `schema` | string | `noit-config/2` | File format marker. |
| `project.name` | string | project folder name | Used in every document header. |
| `project.directory` | string | opencode location directory | Absolute project directory. |
| `environment.*` | string | auto-filled | `opencodeVersion` (running version, recorded in `manifest.json`), `plugin` (`noit.ai-declaration`), `pluginVersion` (`0.1.0`). |
| `export.records` | array of `{id,title,task,sessionIDs}` | `[{id:"B01", …}]` | Section B record specs. A record with empty `sessionIDs` becomes the aggregate record of all non-substantial sessions; records with `sessionIDs` claim those sessions. |
| `export.includeKarta` | boolean | `true` | Write `karta-A01.md` and the tool card(s). |
| `export.includeDeclaration` | boolean | `true` | Write `declaration.md`. |
| `export.public` | boolean | `false` | Mirror a PII-stripped copy of the whole export into `.noit/public/` after each export. |
| `export.tools` | array of `{id,name,provider?,notes?}` | `[]` | Extra AI tool cards A02, A03, … — see below. |
| `backfill.sync` | boolean | `false` | When `true`, every automatic reconcile (setup, after each capture, teardown) forces re-import of all scoped sessions instead of only the not-yet-imported ones. |
| `backfill.limit` | number | `100` | Page size when listing sessions through the opencode API (clamped 1–100; `0`/absent falls back to `100`). |
| `backfill.concurrency` | number | `4` | Parallel session imports (clamped 1–8). |
| `backfill.scope` | `project` \| `all` | `project` | **Deprecated and ignored** — the plugin is always project-scoped. A supplied value is logged once to `.noit/ai-log.jsonl`; `noit_status` prints `project`. |
| `backfill.projectId` | string | current project id | Allowed `project_id` set, combined with ids derived from the database `project` table for the directory. |
| `backfill.directory` | string | project directory | Normalized directory every session must match (case-insensitive, slashes normalized). |
| `backfill.databasePath` | string | — | When set, the plugin temporarily exports it as `OPENCODE_DB` while reading the opencode SQLite database, then restores the previous value. |

### `export.tools` — extra AI tool cards

- Each entry is `{id, name, provider?, notes?}`. `id` must match `A##` (two or more digits, e.g. `A02`) and cannot be `A01` — that identifier is reserved for opencode itself.
- `name` is required. `provider` and `notes` are optional; empty values after trimming are dropped and logged. Malformed entries (wrong shape, invalid/reserved/duplicate `id`, missing `name`) are ignored and reported to `.noit/ai-log.jsonl`. Ids are trimmed, uppercased, and deduplicated — the first entry wins.
- When `export.tools` is empty or absent, the export writes `karta-A01.md` plus a single generic `karta-A02.md` placeholder that explains how to copy it for A03 and beyond.
- When it is set, one `karta-<id>.md` is written per entry with «Име на инструмента» (plus «Доставчик/разработчик» and «Бележки» when those are set) filled; every other field remains `«попълнете ръчно»`.

### `export.public` — the PII-stripped copy

- Default `false`. When `true`, after every export the plugin walks `.noit/export/AI/` and writes a mirrored copy into `.noit/public/`.
- Before a file is written, every configured personal value (the personal `manual.*` fields and every `participants` field) is replaced with `[ЛИЧНИ ДАННИ]`, EGN-shaped digit runs are caught by a backstop pattern, and the secret redactor runs again.
- The normal package in `.noit/export/AI/` keeps the real data. Use the mirror when the repository is public: commit only `.noit/public/` (and the base `.noit/config.json`); never commit `.noit/export/AI/`, and never commit `.noit/config.local.json`.

### How sessions are found

**Scope.** A session belongs to this project only when both hold: its normalized directory equals `backfill.directory` (case-insensitive, slashes normalized) **and** its `project_id` is in the allowed set (`backfill.projectId` plus ids derived from the opencode `project` table whose `worktree` matches the directory). Live capture is guarded by the same check, so another project's session can never be written into this project's `.noit/`.

**Candidate listing.** The opencode session API is queried first (filtered by `backfill.directory`, paged with the clamped `backfill.limit`, up to 1000 pages) and the results are scope-filtered. Only if that yields no scoped sessions does the plugin fall back to reading the opencode SQLite database directly at `$OPENCODE_DB` (or `backfill.databasePath` when set) or `~/.local/share/opencode/opencode.db` (tables `session_v2` and `session`), listing rows that match the directory **or** the project id, then applying the same strict scope check.

**Eligibility.** Every strictly scoped session that is not already in `state.imported` is eligible — there is no install-time cutoff, so sessions created after installation are imported too. Dedupe is solely `state.imported`; `/noit-backfill` (or `backfill.sync: true`) forces re-import of all scoped sessions. Backfill state (`installedAt`, `lastBackfill`, `imported`, `failures`) lives in opencode's plugin storage, not under `.noit/`; `installedAt` is still reported by `noit_status` but no longer gates eligibility.

**Live capture.** Prompts and tool calls are appended to `.noit/ai-log.jsonl` as they happen. A full transcript is captured the moment a session reaches a terminal signal: the awaited `session.hook("experimental.idle")`, which holds the host open until capture and reconcile finish, plus the event stream (`session.idle`, `session.execution.succeeded`, `session.execution.failed`, `session.execution.interrupted`). There is no idle delay — dumps are written immediately. On teardown the event subscription is aborted, in-flight captures are drained, and one final project-scoped reconcile runs. Headless capture was verified with a real `opencode run` session.

**Robustness.** Zero-message sessions are marked handled without writing a file; idle-only transcripts are written; a database row with no message rows yet is skipped unmarked and retried on a later run; `SQLITE_BUSY`/`SQLITE_LOCKED` is retried once after ~150 ms and, if it still fails, recorded in `state.failures` without throwing; a missing database is a safe no-op.

## Tools and commands

Tools are registered in the `noit` namespace (effective names use `_`, e.g. `noit_export`).

| Tool | Input | What it does |
| --- | --- | --- |
| `noit_export` | `force?` | Export the declaration package into `.noit/export/AI/`, mirror `.noit/public/` when `export.public` is on, and run the checker. Files are always overwritten; `force` is accepted but ignored (see Roadmap). |
| `noit_check` | `strict?` | Run the 10-point checklist and rewrite `checklist.md`. `strict` changes wording only, never pass/fail. |
| `noit_record_start` | `title`, `task` | Open a named B record in `.noit/records/index.json`, bound to the current session. The record is exported as its own `records-B<id>.md`. |
| `noit_record_end` | `title?` | Close the most recently opened record; optionally rename it. |
| `noit_status` | — | Full report: config, backfill state, captured `ses_*.json` count, ai-log entries and records. |
| `noit_backfill` | — | Force re-import of every strictly scoped session (overwrites existing dumps). |

| Command | What it does |
| --- | --- |
| `/noit-export` | Same as `noit_export` (no `force`). |
| `/noit-check` | Same as `noit_check` (no `strict`). |
| `/noit-record` | Open a record: first line is the title, the rest is the task. Exported as its own `records-B<id>.md`. |
| `/noit-status` | One-line summary (project, session count, last backfill); use the `noit_status` tool for the full report. |
| `/noit-backfill` | Same as `noit_backfill`. |

### The checklist

Ten required checks, all conservative (a false FAIL is acceptable, a false PASS is not):

- `main-form` — `declaration.md`, `karta-A01.md`, the `records-B*.md` files and `section-V.md` exist and are not empty placeholders (only when a "Да" area is ticked or sessions exist).
- `section-a` — every mandatory karta field is filled. `karta-A01.md` gets the full field set; additional `karta-A<id>.md` cards only need «Име на инструмента» and «Идентификатор в декларацията».
- `section-b` — every record has Задача / Входни данни / Резултати / Стъпки за възпроизвеждане and a session reference or the `ръчен запис` marker.
- `section-c` — every path listed in `section-V.md` resolves inside the export directory.
- `evidence` — `requests/`, `outputs/`, `inputs/` non-empty; `changes/`, `tests/` present or explicitly marked «не е приложимо».
- `manifest` — `manifest.json` lists exactly the files on disk.
- `manual-fields` — every `manual.*` key in the merged configuration is filled (the same placeholder rules as above).
- `pii` — scans every file in the public copy for personal values configured in `manual.*`/`participants`, and for EGN-shaped numbers. It runs **only when `export.public` is true**; otherwise it passes with a "no scan" note. A missing or unreadable public directory fails.
- `participants` — the list is non-empty and at most 10 entries; each participant has a non-placeholder `name`; a provided `signature`/`date` must not be a placeholder; duplicate name+ЕГН pairs are reported.
- `secrets` — no unredacted secrets in the exported `.md`/`.json` files.

## What this plugin can and cannot do

**Can:**
- Import every strictly scoped session not already imported — including sessions created after the plugin was installed (no install-time window).
- Capture prompts and tool calls live while the plugin is loaded, including headless `opencode run` sessions handled by the same opencode server (verified).
- Group sessions into Section B records (aggregate + per substantial task), export manual records as their own `records-B<id>.md`, and build the evidence tables.
- Generate Section A tool cards from `export.tools` (A02, A03, …) and mirror a PII-stripped copy into `.noit/public/` for public repositories.
- Redact secrets from everything it writes, and validate the exported package conservatively (`checklist.md` + `manifest.json`).

**Cannot:**
- Detect or record AI use outside opencode — browser ChatGPT, Copilot in your IDE, other editors or chat apps are invisible to it. Declare such tools with `export.tools` (cards) and manual records (their content must be written by hand).
- Fill in facts it does not have: your name, ЕГН, school, class, goals, access method, dates and signatures are placeholders until a human fills them.
- Sign, certify or take responsibility. The declaration is a legal/competition document; **you are responsible for its accuracy**.
- Guarantee perfect redaction. The redactor covers common secret shapes (private keys, PEM blocks, AWS/Google/Anthropic/OpenAI/GitHub/Slack/Stripe keys, JWTs, Azure keys and SAS tokens, URL credentials, `Bearer` tokens, and `api_key`/`client_secret`/`secret`/`password`/`token` assignments), but you must review the output before submitting.
- Produce PDF or DOCX (roadmap — Markdown only for now).

## Privacy and security

- `.noit/config.local.json` holds personal data (`name`, `egn`, `school`, `class`, `participants`). It is gitignored; never commit it. The base `.noit/config.json` is written without personal data and is safe to commit.
- `.noit/ai-log.jsonl` and `.noit/raw/` contain your prompts, code and tool output. Everything is redacted when written and the export redacts again, but they are still local raw data — keep them gitignored.
- The plugin makes no network requests; nothing leaves your machine.
- `redaction-report.json` lists every rule that fired and how many replacements were made. The `secrets` check fails if the scan finds anything unredacted in the exported `.md`/`.json` files.
- With `export.public: true`, the `.noit/public/` mirror replaces configured personal values with `[ЛИЧНИ ДАННИ]`, catches EGN-shaped numbers and re-runs the secret redactor; the `pii` check scans it. The normal `.noit/export/AI/` package is untouched and keeps the real data — do not commit it to a public repository.
- Review every generated file before submitting, and especially before committing anything to a public repository.

## Troubleshooting

**The plugin does not load.**
- The plugin must be a folder at `.opencode/plugins/noit` containing `index.ts` — not a bare `.ts` file.
- Confirm the `opencode.json` entry from the Installation section, and that `npm install` was run inside the plugin folder (you should see `node_modules/@opencode/plugin`).
- Restart opencode, then look for the `setup …` line in `.noit/ai-log.jsonl` or run `/noit-status`.

**No sessions found.**
- Scope is strict: the session directory (normalized, case-insensitive) must equal `backfill.directory` **and** its `project_id` must belong to this directory. Run `/noit-status` or the `noit_status` tool to see the scope and source, then adjust `backfill.directory`/`backfill.projectId` (or the plugin options) if needed.
- The database is read from `$OPENCODE_DB` — or `backfill.databasePath` when configured — or `~/.local/share/opencode/opencode.db` on every OS (on Windows that is `C:\Users\<you>\.local\share\opencode\opencode.db`). A missing database is a safe no-op, and a busy one is retried once after ~150 ms before being recorded in `state.failures`.

**The checker fails on `manual-fields`.**
- Expected until every `manual.*` field is filled in `.noit/config.local.json`. Placeholders that fail: empty, `непознато`, `попълнете ръчно`, `-`/`—`/`–`, `null`, `undefined`, anything containing `todo`/`tbd`. `неприложимо` passes.

**The checker fails on `participants`.**
- Fill `participants` in `.noit/config.local.json` (up to 10 entries, each with a `name`) or fill the six matching `manual.*` fields the fallback derives from. Duplicate name+ЕГН pairs fail.

**The checker fails on `pii`.**
- Only runs when `export.public` is true. It means the public copy is missing, unreadable, or still contains a configured personal value or an EGN-shaped number — re-run `/noit-export` to rebuild the mirror, then review `.noit/public/` by hand before committing.

**The checker fails on Section A (`karta-A01.md`).**
- `Дата на инсталиране/достъп`, `Подпис` and `Дата` are always manual. If there are no sessions, `Модели…` and `Агенти…` also stay as `«попълнете ръчно»`. Fill them in the file, then re-run `/noit-check`.

**The checker fails on Section C (`section-V.md`).**
- Every path in the table must exist relative to `.noit/export/AI/`. Do not hand-edit the path rows; re-run `/noit-export` to rebuild them.

**A session is missing.**
- If the session is not strictly scoped (different directory or unknown `project_id`) or the database is unavailable, neither capture nor backfill will import it. As a workaround, drop a session dump as `.noit/raw/sessions/<id>.json` (a `.jsonl` next to it is optional) — the exporter reads every `.json` in that folder (except `index.json`) and re-redacts the content on export — then review it manually and re-run export. Such a manual dump is not added to `state.imported`.

## Limitations and roadmap

- **Markdown only.** PDF/DOCX export is planned, not implemented.
- **Dedupe.** A session is skipped once its ID is in `state.imported`; deleting a raw dump by hand does not schedule a re-import — use `/noit-backfill` or `backfill.sync: true` to force one.
- **`backfill.scope`** is deprecated: any supplied value is ignored and logged once — the plugin is always project-scoped.
- **`noit_export`'s `force` flag** is accepted but currently ignored (export always overwrites).
- **`export.public` is a snapshot**: the mirror is rebuilt from scratch on every export; edit the export files and re-run `/noit-export` rather than editing `.noit/public/` directly.
- **Participants.** The `participants` array (max 10) drives the `participants` check, the PII stripping and the declaration rendering: the declaration prints a participants table and one signature block per participant, with ЕГН only in the signature block. Signatures/dates remain placeholders until filled by hand, and there is no per-participant AI attribution.
- **AI use outside opencode** must still be filled in by hand: `export.tools` generates the Section A card skeletons and `/noit-record` the Section B records, but their content is written by the participant.
- **Windows path normalization** is handled (backslashes are normalized and directory matching is case-insensitive), but evidence paths are shown relative to the project directory.
- **Headless sessions** are captured as long as they run through the same opencode server with this project's plugin loaded; verified with a real `opencode run`. Capture is event-driven (the awaited `experimental.idle` hook plus `session.idle`/`session.execution.*` events), not TUI-driven.

## Contributing

Issues and pull requests are welcome. When contributing: test with opencode 2.0.18 and Node 22.13+, keep the checker conservative (a false FAIL is acceptable, a false PASS is not), and never commit anything from a `.noit/` data directory — only plugin source and documentation.

## License

MIT — see [LICENSE](./LICENSE). © 2026 InfiniteFightingGhost.
