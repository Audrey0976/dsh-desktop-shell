import { spawn } from 'node:child_process'
import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { createInterface } from 'node:readline'

const stage = resolve(process.argv[2] ?? '')
if (stage === '') throw new Error('usage: node scripts/Smoke-Runtime.mjs <packaged-app-directory>')
const manifest = JSON.parse(await readFile(join(stage, 'runtime', 'bundled-version.json'), 'utf8'))
const data = await mkdtemp(join(tmpdir(), 'dsh-desktop-shell-smoke-'))
const node = join(stage, 'runtime', 'node', process.platform === 'win32' ? 'node.exe' : 'bin/node')
const bootstrap = join(stage, 'runtime', 'bootstrap', 'bootstrap.mjs')
const bundled = join(stage, 'runtime', 'versions', manifest.dshVersion)
const child = spawn(node, [
  bootstrap,
  '--app-dir', stage,
  '--data-dir', data,
  '--bundled-version', manifest.dshVersion,
  '--bundled-dir', bundled,
  '--startup-timeout-ms', '90000',
], {
  env: { ...process.env, DSH_HOME: join(data, 'dsh-home') },
  stdio: ['pipe', 'pipe', 'pipe'],
  windowsHide: true,
})

let stderr = ''
child.stderr.on('data', chunk => { stderr = `${stderr}${chunk}`.slice(-8000) })
const ready = await new Promise((resolveReady, reject) => {
  const timer = setTimeout(() => reject(new Error(`runtime smoke timed out; stderr=${stderr}`)), 100000)
  createInterface({ input: child.stdout, crlfDelay: Infinity }).on('line', line => {
    const message = JSON.parse(line)
    if (message.type === 'fatal') {
      clearTimeout(timer)
      reject(new Error(message.message))
    }
    if (message.type === 'ready') {
      clearTimeout(timer)
      resolveReady(message)
    }
  })
  child.once('error', error => { clearTimeout(timer); reject(error) })
  child.once('exit', code => { if (code !== 0) { clearTimeout(timer); reject(new Error(`bootstrap exited ${code}; stderr=${stderr}`)) } })
})

const origin = new URL(ready.url)
if (origin.protocol !== 'http:' || origin.hostname !== '127.0.0.1' || origin.port === '0') {
  throw new Error(`bootstrap returned unsafe or non-random origin ${ready.url}`)
}
const response = await fetch(ready.url, { signal: AbortSignal.timeout(5000) })
if (!response.ok) throw new Error(`packaged runtime returned HTTP ${response.status}`)
child.stdin.write(`${JSON.stringify({ command: 'shutdown' })}\n`)
await Promise.race([
  new Promise(resolveExit => child.once('exit', resolveExit)),
  new Promise((_, reject) => setTimeout(() => reject(new Error('bootstrap did not shut down')), 10000)),
])
await rm(data, { recursive: true, force: true })
process.stdout.write(`Packaged runtime smoke passed: dsh ${ready.version} at a random loopback origin.\n`)
