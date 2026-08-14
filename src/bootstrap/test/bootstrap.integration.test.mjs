import test from 'node:test'
import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { createServer } from 'node:http'
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { dirname, join } from 'node:path'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'

const bootstrap = join(dirname(fileURLToPath(import.meta.url)), '..', 'bootstrap.mjs')

const healthyCli = `
import { createServer } from 'node:http'
const server = createServer((_request, response) => {
  response.writeHead(200, { 'content-type': 'text/html' })
  response.end('<title>DeepSeek Harness</title>')
})
server.listen(0, '127.0.0.1', () => {
  const address = server.address()
  console.log('dsh web: http://127.0.0.1:' + address.port)
})
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => server.close(() => process.exit(0)))
`

const failingCli = `
console.error('fixture failed before readiness')
process.exit(23)
`

async function makeInstallation(directory, version, source = healthyCli) {
  const packageDirectory = join(directory, 'node_modules', '@deepseek-ai', 'dsh')
  await mkdir(join(packageDirectory, 'lib'), { recursive: true })
  await writeFile(join(packageDirectory, 'package.json'), JSON.stringify({ name: '@deepseek-ai/dsh', version }))
  await writeFile(join(packageDirectory, 'lib', 'bin.js'), source)
}

async function registryServer(versions) {
  const server = createServer((_request, response) => {
    response.writeHead(200, { 'content-type': 'application/json' })
    response.end(JSON.stringify({
      name: '@deepseek-ai/dsh',
      versions: Object.fromEntries(versions.map(version => [version, {
        name: '@deepseek-ai/dsh',
        version,
        dist: { integrity: `sha512-${Buffer.from(version).toString('base64')}` },
      }])),
    }))
  })
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  return {
    url: `http://127.0.0.1:${server.address().port}`,
    close: () => new Promise(resolve => server.close(resolve)),
  }
}

async function startFixture(context, candidateSource = healthyCli) {
  const root = await mkdtemp(join(tmpdir(), 'dsh-shell-bootstrap-'))
  const app = join(root, 'app')
  const data = join(root, 'data')
  const bundled = join(app, 'runtime', 'versions', '0.1.0-rc.6')
  const fixtures = join(root, 'fixtures')
  await makeInstallation(bundled, '0.1.0-rc.6')
  await makeInstallation(join(fixtures, '0.1.0-rc.7'), '0.1.0-rc.7', candidateSource)
  const registry = await registryServer(['0.1.0-rc.6', '0.1.0-rc.7'])
  const child = spawn(process.execPath, [
    bootstrap,
    '--app-dir', app,
    '--data-dir', data,
    '--bundled-version', '0.1.0-rc.6',
    '--bundled-dir', bundled,
    '--startup-timeout-ms', '4000',
    '--update-interval-ms', '1',
    '--registry', registry.url,
  ], {
    env: { ...process.env, NODE_ENV: 'test', DSH_DESKTOP_TEST_FIXTURES_DIR: fixtures },
    stdio: ['pipe', 'pipe', 'pipe'],
  })
  const messages = []
  const waiters = []
  createInterface({ input: child.stdout, crlfDelay: Infinity }).on('line', line => {
    const message = JSON.parse(line)
    messages.push(message)
    for (const waiter of [...waiters]) {
      if (waiter.predicate(message)) {
        waiters.splice(waiters.indexOf(waiter), 1)
        clearTimeout(waiter.timer)
        waiter.resolve(message)
      }
    }
  })
  let stderr = ''
  child.stderr.on('data', value => { stderr += value })
  function waitFor(predicate, timeoutMs = 10000) {
    const existing = messages.find(predicate)
    if (existing !== undefined) return Promise.resolve(existing)
    return new Promise((resolve, reject) => {
      const waiter = { predicate, resolve, reject }
      waiter.timer = setTimeout(() => {
        waiters.splice(waiters.indexOf(waiter), 1)
        reject(new Error(`timed out waiting for bootstrap message; stderr=${stderr}`))
      }, timeoutMs)
      waiters.push(waiter)
    })
  }
  async function stop() {
    if (child.exitCode === null) child.stdin.write(`${JSON.stringify({ command: 'shutdown' })}\n`)
    await Promise.race([
      new Promise(resolve => child.once('exit', resolve)),
      new Promise(resolve => setTimeout(resolve, 5000)),
    ])
    if (child.exitCode === null) child.kill('SIGKILL')
    await registry.close()
    await rm(root, { recursive: true, force: true })
  }
  context.after(stop)
  return { root, app, data, child, messages, waitFor, stop }
}

test('bootstrap reports a random loopback origin and shuts down cleanly', async (context) => {
  const fixture = await startFixture(context)
  const ready = await fixture.waitFor(message => message.type === 'ready')
  const url = new URL(ready.url)
  assert.equal(url.hostname, '127.0.0.1')
  assert.notEqual(url.port, '0')
  assert.equal(ready.version, '0.1.0-rc.6')
  const pointer = JSON.parse(await readFile(join(fixture.data, 'runtime', 'current.json'), 'utf8'))
  assert.equal(pointer.source, 'bundled')
})

test('compatible candidate is installed, health-checked and activated', async (context) => {
  const fixture = await startFixture(context)
  await fixture.waitFor(message => message.type === 'ready' && message.version === '0.1.0-rc.6')
  await fixture.waitFor(message => message.type === 'update-available' && message.compatible === true)
  fixture.child.stdin.write(`${JSON.stringify({ command: 'update', version: '0.1.0-rc.7' })}\n`)
  await fixture.waitFor(message => message.type === 'ready' && message.version === '0.1.0-rc.7')
  const pointer = JSON.parse(await readFile(join(fixture.data, 'runtime', 'current.json'), 'utf8'))
  assert.equal(pointer.source, 'managed')
  assert.equal(pointer.version, '0.1.0-rc.7')
  assert.equal(pointer.previous.version, '0.1.0-rc.6')
})

test('unhealthy candidate automatically rolls back and is quarantined', async (context) => {
  const fixture = await startFixture(context, failingCli)
  await fixture.waitFor(message => message.type === 'ready' && message.version === '0.1.0-rc.6')
  await fixture.waitFor(message => message.type === 'update-available')
  fixture.child.stdin.write(`${JSON.stringify({ command: 'update', version: '0.1.0-rc.7' })}\n`)
  await fixture.waitFor(message => message.type === 'status' && message.phase === 'update-rollback')
  await fixture.waitFor(message => message.type === 'ready' && message.version === '0.1.0-rc.6')
  const pointer = JSON.parse(await readFile(join(fixture.data, 'runtime', 'current.json'), 'utf8'))
  const updateState = JSON.parse(await readFile(join(fixture.data, 'runtime', 'update-state.json'), 'utf8'))
  assert.equal(pointer.version, '0.1.0-rc.6')
  assert.ok(updateState.failedVersions['0.1.0-rc.7'])
})
