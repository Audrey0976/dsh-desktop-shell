import { spawn } from 'node:child_process'
import { request } from 'node:http'
import { createInterface } from 'node:readline'

function quoteForLog(value) {
  return /\s/u.test(value) ? JSON.stringify(value) : value
}

function probe(url, timeoutMs = 1500) {
  return new Promise((resolve) => {
    const target = new URL(url)
    const check = request({
      protocol: target.protocol,
      hostname: target.hostname,
      port: target.port,
      path: '/',
      method: 'GET',
      headers: { 'user-agent': 'DSH-Desktop-Shell/0.1.0' },
      timeout: timeoutMs,
    }, (response) => {
      response.resume()
      resolve(response.statusCode !== undefined && response.statusCode >= 200 && response.statusCode < 500)
    })
    check.once('timeout', () => { check.destroy(); resolve(false) })
    check.once('error', () => resolve(false))
    check.end()
  })
}

async function waitForHealthy(url, deadline) {
  while (Date.now() < deadline) {
    if (await probe(url)) return true
    await new Promise(resolve => setTimeout(resolve, 120))
  }
  return false
}

export class BackendSupervisor {
  constructor({ logger, onUnexpectedExit }) {
    this.logger = logger
    this.onUnexpectedExit = onUnexpectedExit
    this.child = undefined
    this.expectedStop = false
    this.ready = false
    this.url = undefined
  }

  async start(installation, timeoutMs) {
    if (this.child !== undefined) throw new Error('backend is already running')
    this.expectedStop = false
    this.ready = false
    const arguments_ = [installation.executable, 'web', '--host', '127.0.0.1', '--port', '0']
    await this.logger.write('backend', `starting dsh ${installation.pointer.version}: ${arguments_.map(quoteForLog).join(' ')}`)
    const child = spawn(process.execPath, arguments_, {
      cwd: installation.directory,
      env: {
        ...process.env,
        NO_COLOR: '1',
        PATH: `${process.execPath.substring(0, process.execPath.lastIndexOf(process.platform === 'win32' ? '\\' : '/'))}${process.platform === 'win32' ? ';' : ':'}${process.env.PATH ?? ''}`,
      },
      windowsHide: true,
      detached: process.platform !== 'win32',
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    this.child = child

    const deadline = Date.now() + timeoutMs
    const url = await new Promise((resolve, reject) => {
      let settled = false
      const finish = (error, value) => {
        if (settled) return
        settled = true
        clearTimeout(timer)
        if (error === undefined) resolve(value)
        else reject(error)
      }
      const inspectLine = (channel, line) => {
        if (!this.ready) void this.logger.write(`backend-${channel}`, line)
        const match = /dsh web:\s*(http:\/\/(?:127\.0\.0\.1|localhost):\d+)/u.exec(line)
        if (match !== null) finish(undefined, match[1])
      }
      createInterface({ input: child.stdout, crlfDelay: Infinity }).on('line', line => inspectLine('stdout', line))
      createInterface({ input: child.stderr, crlfDelay: Infinity }).on('line', line => inspectLine('stderr', line))
      child.once('error', error => finish(error))
      child.once('exit', (code, signal) => {
        if (!settled) finish(new Error(`dsh exited before readiness (code ${String(code)}, signal ${String(signal)})`))
      })
      const timer = setTimeout(() => finish(new Error(`dsh did not report a URL within ${timeoutMs} ms`)), timeoutMs)
    })

    if (!await waitForHealthy(url, deadline)) {
      await this.stop()
      throw new Error(`dsh reported ${url}, but its health check did not pass`)
    }
    this.ready = true
    this.url = url
    await this.logger.write('backend', `ready at ${url}`)
    child.once('exit', (code, signal) => {
      const wasExpected = this.expectedStop
      this.child = undefined
      this.ready = false
      this.url = undefined
      if (!wasExpected) this.onUnexpectedExit?.({ code, signal })
    })
    return url
  }

  async stop() {
    const child = this.child
    if (child === undefined) return
    this.expectedStop = true
    await this.logger.write('backend', `stopping process ${child.pid}`)
    if (process.platform === 'win32') {
      await new Promise((resolve) => {
        const killer = spawn('taskkill.exe', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' })
        killer.once('error', resolve)
        killer.once('exit', resolve)
      })
    } else {
      try { process.kill(-child.pid, 'SIGTERM') } catch { }
      await Promise.race([
        new Promise(resolve => child.once('exit', resolve)),
        new Promise(resolve => setTimeout(resolve, 4000)),
      ])
      if (child.exitCode === null) {
        try { process.kill(-child.pid, 'SIGKILL') } catch { }
      }
    }
    this.child = undefined
    this.ready = false
    this.url = undefined
  }
}
