import { createInterface } from 'node:readline'

export function createProtocol(output = process.stdout) {
  return {
    emit(type, payload = {}) {
      output.write(`${JSON.stringify({ type, ...payload })}\n`)
    },
    status(phase, message, payload = {}) {
      this.emit('status', { phase, message, ...payload })
    },
  }
}

export function readCommands(input, onCommand, onEnd) {
  const lines = createInterface({ input, crlfDelay: Infinity })
  lines.on('line', (line) => {
    if (line.trim() === '') return
    try {
      const value = JSON.parse(line)
      if (value === null || typeof value !== 'object' || Array.isArray(value)) throw new Error('command must be an object')
      onCommand(value)
    } catch (error) {
      onCommand({ command: 'invalid', error: error instanceof Error ? error.message : String(error) })
    }
  })
  lines.once('close', onEnd)
  return lines
}
