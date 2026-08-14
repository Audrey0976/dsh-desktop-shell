import { appendFile, mkdir, rename, stat } from 'node:fs/promises'
import { dirname } from 'node:path'

const SECRET_PATTERNS = [
  /\bsk-[A-Za-z0-9_-]{8,}\b/gu,
  /\b(api[_ -]?key|authorization|token|secret|password)\b\s*[:=]\s*[^\s,;]+/giu,
  /\bBearer\s+[A-Za-z0-9._~+/-]+=*/giu,
]

export function sanitizeLogValue(value) {
  let result = String(value).replace(/[\r\n]+/gu, ' ').slice(0, 1200)
  for (const pattern of SECRET_PATTERNS) result = result.replace(pattern, '[REDACTED]')
  return result
}

export async function createLogger(path) {
  await mkdir(dirname(path), { recursive: true })
  try {
    if ((await stat(path)).size > 2 * 1024 * 1024) await rename(path, `${path}.previous`)
  } catch { }

  let queue = Promise.resolve()
  return {
    path,
    write(category, message) {
      const line = `[${new Date().toISOString()}] [${sanitizeLogValue(category)}] ${sanitizeLogValue(message)}\n`
      queue = queue.then(() => appendFile(path, line, { encoding: 'utf8', mode: 0o600 })).catch(() => {})
      return queue
    },
    flush() { return queue },
  }
}
