import test from 'node:test'
import assert from 'node:assert/strict'
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { resolveCurrentInstallation, writeCurrentPointer } from '../lib/files.mjs'

async function makeInstallation(directory, version) {
  const packageDirectory = join(directory, 'node_modules', '@deepseek-ai', 'dsh')
  await mkdir(join(packageDirectory, 'lib'), { recursive: true })
  await writeFile(join(packageDirectory, 'package.json'), JSON.stringify({ name: '@deepseek-ai/dsh', version }))
  await writeFile(join(packageDirectory, 'lib', 'bin.js'), 'process.exit(0)\n')
}

test('invalid current pointer falls back atomically to bundled runtime', async (context) => {
  const root = await mkdtemp(join(tmpdir(), 'dsh-shell-files-'))
  context.after(() => rm(root, { recursive: true, force: true }))
  const configuration = {
    dataDirectory: join(root, 'data'),
    bundledDirectory: join(root, 'bundle'),
    bundledVersion: '0.1.0-rc.6',
  }
  await makeInstallation(configuration.bundledDirectory, configuration.bundledVersion)
  await writeCurrentPointer(configuration, { source: 'managed', version: '0.1.0-rc.7' })

  const current = await resolveCurrentInstallation(configuration)
  assert.equal(current.pointer.source, 'bundled')
  assert.equal(current.pointer.version, '0.1.0-rc.6')
  const pointer = JSON.parse(await readFile(join(configuration.dataDirectory, 'runtime', 'current.json'), 'utf8'))
  assert.equal(pointer.version, '0.1.0-rc.6')
})
