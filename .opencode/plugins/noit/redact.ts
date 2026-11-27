import type { RedactionFinding, RedactionResult } from "./types.ts"

interface Rule {
  name: string
  pattern: RegExp
  reason: string
  apply: (match: string, groups: (string | undefined)[]) => string
}

function placeholder(name: string): string {
  return `[REDACTED:${name}]`
}

const ALREADY_REDACTED =
  /\[(?:REDACTED(?::[a-z0-9-]+)?|API_KEY|TOKEN|PASSWORD|SECRET|CLIENT_SECRET|PRIVATE_KEY)\]/
const NOT_REDACTED_VALUE = String.raw`(?!\[(?:REDACTED(?::[a-z0-9-]+)?|API_KEY|TOKEN|PASSWORD|SECRET|CLIENT_SECRET|PRIVATE_KEY)\])`
const NOT_WORD_BEFORE = String.raw`(?<![A-Za-z0-9])`

const RULES: Rule[] = [
  {
    name: "private-key-block",
    pattern: /-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----/g,
    reason: "Private key material",
    apply: () => placeholder("private-key-block"),
  },
  {
    name: "pem-block",
    pattern: /-----BEGIN [A-Z0-9 ]+-----[\s\S]*?-----END [A-Z0-9 ]+-----/g,
    reason: "PEM block of any type",
    apply: () => placeholder("pem-block"),
  },
  {
    name: "aws-access-key",
    pattern: /\bAKIA[0-9A-Z]{16}\b/g,
    reason: "AWS access key id",
    apply: () => placeholder("aws-access-key"),
  },
  {
    name: "aws-secret-key",
    pattern: new RegExp(
      String.raw`(${NOT_WORD_BEFORE}(?:aws[_-]?)?secret[_-]?(?:access[_-]?)?key["']?\s*[:=]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([A-Za-z0-9/+=]{32,})\2`,
      "gi",
    ),
    reason: "AWS secret access key",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("aws-secret-key")}${groups[1] ?? ""}`,
  },
  {
    name: "aws-secret-access-key",
    pattern: new RegExp(
      String.raw`(${NOT_WORD_BEFORE}(?:aws[_-]?)?secret[_-]?access[_-]?key["']?\s*[:=]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([^\s"']{6,})\2`,
      "gi",
    ),
    reason: "AWS secret access key (short/test value)",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("aws-secret-access-key")}${groups[1] ?? ""}`,
  },
  {
    name: "google-api-key",
    pattern: /\bAIza[0-9A-Za-z_\-]{35}\b/g,
    reason: "Google API key",
    apply: () => placeholder("google-api-key"),
  },
  {
    name: "anthropic-key",
    pattern: /\bsk-ant-[A-Za-z0-9_\-]{16,}\b/g,
    reason: "Anthropic API key",
    apply: () => placeholder("anthropic-key"),
  },
  {
    name: "openai-key",
    pattern: /\bsk-(?:proj-|svcacct-|admin-)?[A-Za-z0-9_\-]{20,}\b/g,
    reason: "OpenAI API key",
    apply: () => placeholder("openai-key"),
  },
  {
    name: "github-token",
    pattern: /\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}\b/g,
    reason: "GitHub token",
    apply: () => placeholder("github-token"),
  },
  {
    name: "github-pat",
    pattern: /\bgithub_pat_[A-Za-z0-9_]{20,}\b/g,
    reason: "GitHub fine-grained token",
    apply: () => placeholder("github-pat"),
  },
  {
    name: "slack-token",
    pattern: /\b(?:xox[a-z]?[.-]|xapp-)[A-Za-z0-9.-]{10,}\b/g,
    reason: "Slack token",
    apply: () => placeholder("slack-token"),
  },
  {
    name: "stripe-key",
    pattern: /\b(?:sk|rk)_(?:live|test)_[A-Za-z0-9]{16,}\b/g,
    reason: "Stripe API key",
    apply: () => placeholder("stripe-key"),
  },
  {
    name: "jwt",
    pattern: /\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b/g,
    reason: "JSON Web Token",
    apply: () => placeholder("jwt"),
  },
  {
    name: "azure-storage-key",
    pattern: new RegExp(
      String.raw`(\b["']?AccountKey["']?\s*[=:]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([^;"'\s]+)\2`,
      "gi",
    ),
    reason: "Azure storage account key",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("azure-storage-key")}${groups[1] ?? ""}`,
  },
  {
    name: "azure-sas-header",
    pattern: /(\bSharedAccessSignature\s+)(?!\[REDACTED:)([A-Za-z0-9%+/=&._-]{20,})/gi,
    reason: "Azure SharedAccessSignature header",
    apply: (_match, groups) => `${groups[0] ?? ""}${placeholder("azure-sas-header")}`,
  },
  {
    name: "azure-sas-token",
    pattern: new RegExp(
      String.raw`(\b(?:sig|SharedAccessSignature)\s*[=:]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([A-Za-z0-9%+/=]{20,})\2`,
      "gi",
    ),
    reason: "Azure SAS token",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("azure-sas-token")}${groups[1] ?? ""}`,
  },
  {
    name: "url-credentials",
    pattern: /(\b[a-z][a-z0-9+.-]*:\/\/)(?!\[REDACTED:)([^\s\/:@\[\]]+):([^\s\/@\[\]]+)@/gi,
    reason: "Credentials embedded in a URL or connection string",
    apply: (_match, groups) => `${groups[0] ?? ""}${placeholder("url-credentials")}@`,
  },
  {
    name: "bearer-token",
    pattern: /\bBearer\s+(?!\[REDACTED:)[A-Za-z0-9._\-]{10,}/g,
    reason: "Authorization bearer token",
    apply: () => `Bearer ${placeholder("bearer-token")}`,
  },
  {
    name: "api-key-assignment",
    pattern: new RegExp(
      String.raw`(${NOT_WORD_BEFORE}(?:api[_-]?key|apikey)["']?\s*[:=]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([^\s"']+)\2`,
      "gi",
    ),
    reason: "API key assignment",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("api-key-assignment")}${groups[1] ?? ""}`,
  },
  {
    name: "client-secret-assignment",
    pattern: new RegExp(
      String.raw`(${NOT_WORD_BEFORE}(?:client[_-]?secret|clientsecret)["']?\s*[:=]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([^\s"']+)\2`,
      "gi",
    ),
    reason: "Client secret assignment",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("client-secret-assignment")}${groups[1] ?? ""}`,
  },
  {
    name: "secret-assignment",
    pattern: new RegExp(
      String.raw`(${NOT_WORD_BEFORE}(?:secret[_-]?key|secretkey|app[_-]?secret|api[_-]?secret|client[_-]?secret|secret)["']?\s*[:=]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([^\s"']+)\2`,
      "gi",
    ),
    reason: "Secret assignment",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("secret-assignment")}${groups[1] ?? ""}`,
  },
  {
    name: "password-assignment",
    pattern: new RegExp(
      String.raw`(${NOT_WORD_BEFORE}(?:password|passwd)["']?\s*[:=]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([^\s"']+)\2`,
      "gi",
    ),
    reason: "Password value",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("password-assignment")}${groups[1] ?? ""}`,
  },
  {
    name: "token-assignment",
    pattern: new RegExp(
      String.raw`(${NOT_WORD_BEFORE}(?:access_token|auth_token|api_token|token)["']?\s*[:=]\s*)(["']?)` +
        NOT_REDACTED_VALUE +
        String.raw`([^\s"']+)\2`,
      "gi",
    ),
    reason: "Token value",
    apply: (_match, groups) =>
      `${groups[0] ?? ""}${groups[1] ?? ""}${placeholder("token-assignment")}${groups[1] ?? ""}`,
  },
]

function collectGroups(args: unknown[]): (string | undefined)[] {
  const groups: (string | undefined)[] = []
  for (const value of args) {
    if (typeof value === "number") break
    groups.push(value as string | undefined)
  }
  return groups
}

function isAlreadyRedacted(match: string): boolean {
  return ALREADY_REDACTED.test(match)
}

export function redact(text: string): RedactionResult {
  let output = text
  const findings: RedactionFinding[] = []
  for (const rule of RULES) {
    let count = 0
    output = output.replace(rule.pattern, (match: string, ...rest: unknown[]) => {
      if (isAlreadyRedacted(match)) return match
      const replaced = rule.apply(match, collectGroups(rest))
      if (replaced === match) return match
      count += 1
      return replaced
    })
    if (count > 0) {
      findings.push({
        rule: rule.name,
        replacement: placeholder(rule.name),
        reason: rule.reason,
        count,
      })
    }
  }
  return { text: output, findings }
}

export function redactText(text: string): string {
  return redact(text).text
}

export function redactValue<T>(value: T): T {
  if (typeof value === "string") return redact(value).text as T
  if (Array.isArray(value)) return value.map((item) => redactValue(item)) as T
  if (value && typeof value === "object") {
    const prototype = Object.getPrototypeOf(value)
    if (prototype !== Object.prototype && prototype !== null) return value
    const output: Record<string, unknown> = {}
    for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
      output[key] = redactValue(item)
    }
    return output as T
  }
  return value
}

export function redactRaw<T>(value: T): T {
  return redactValue(value)
}

export function scanSecrets(text: string): RedactionFinding[] {
  const findings: RedactionFinding[] = []
  for (const rule of RULES) {
    let count = 0
    for (const match of text.matchAll(rule.pattern)) {
      if (isAlreadyRedacted(match[0])) continue
      count += 1
    }
    if (count > 0) {
      findings.push({
        rule: rule.name,
        replacement: placeholder(rule.name),
        reason: rule.reason,
        count,
      })
    }
  }
  return findings
}

export function makeCollector() {
  const map = new Map<string, RedactionFinding>()
  return {
    add(result: RedactionResult): void {
      for (const finding of result.findings) {
        const existing = map.get(finding.rule)
        if (existing) existing.count += finding.count
        else map.set(finding.rule, { ...finding })
      }
    },
    list(): RedactionFinding[] {
      return [...map.values()]
    },
    total(): number {
      let total = 0
      for (const finding of map.values()) total += finding.count
      return total
    },
  }
}

export type RedactionCollector = ReturnType<typeof makeCollector>
