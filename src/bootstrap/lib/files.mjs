import { mkdir, open, readFile, rename, rm, stat } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { randomUUID } from 'node:crypto'

export async function readJson(path, fallback = undefined) {
  try {
    return JSON.parse(await readFile(path, 'utf8'))
  } catch (error) {
    if (error?.code === 'ENOENT' || error instanceof SyntaxError) return fallback
    throw error
  }
}

export async function writeJsonAtomic(path, value) {
  await mkdir(dirname(path), { recursive: true })
  const temporary = `${path}.${process.pid}.${randomUUID()}.tmp`
  const handle = await open(temporary, 'wx', 0o600)
  try {
    await handle.writeFile(`${JSON.stringify(value, null, 2)}\n`, 'utf8')
    await handle.sync()
  } finally {
    await handle.close()
  }
  await rename(temporary, path)
}

export function runtimePaths(dataDirectory) {
  const root = join(dataDirectory, 'runtime')
  return {
    root,
    versions: join(root, 'versions'),
    current: join(root, 'current.json'),
    updateState: join(root, 'update-state.json'),
  }
}

export function pointerDirectory(pointer, configuration) {
  if (pointer?.source === 'bundled' && pointer.version === configuration.bundledVersion) {
    return resolve(configuration.bundledDirectory)
  }
  if (pointer?.source === 'managed') {
    return resolve(runtimePaths(configuration.dataDirectory).versions, pointer.version)
  }
  return undefined
}

export function cliPath(installDirectory) {
  if (installDirectory === undefined) return undefined
  return join(installDirectory, 'node_modules', '@deepseek-ai', 'dsh', 'lib', 'bin.js')
}

export async function validateInstallation(pointer, configuration) {
  const directory = pointerDirectory(pointer, configuration)
  if (directory === undefined) return undefined
  const packagePath = join(directory, 'node_modules', '@deepseek-ai', 'dsh', 'package.json')
  const manifest = await readJson(packagePath)
  if (manifest?.name !== '@deepseek-ai/dsh' || manifest?.version !== pointer.version) return undefined
  const executable = cliPath(directory)
  try {
    const info = await stat(executable)
    if (!info.isFile()) return undefined
  } catch {
    return undefined
  }
  return { pointer, directory, executable, manifest }
}

export async function resolveCurrentInstallation(configuration) {
  const paths = runtimePaths(configuration.dataDirectory)
  await mkdir(paths.versions, { recursive: true })
  const saved = await readJson(paths.current)
  const current = await validateInstallation(saved, configuration)
  if (current !== undefined) return current

  const bundledPointer = { source: 'bundled', version: configuration.bundledVersion }
  const bundled = await validateInstallation(bundledPointer, configuration)
  if (bundled === undefined) {
    throw new Error(`Bundled dsh ${configuration.bundledVersion} is missing or invalid`)
  }
  await writeCurrentPointer(configuration, bundledPointer, undefined)
  return bundled
}

export async function writeCurrentPointer(configuration, pointer, previous) {
  await writeJsonAtomic(runtimePaths(configuration.dataDirectory).current, {
    schemaVersion: 1,
    source: pointer.source,
    version: pointer.version,
    previous: previous === undefined ? null : { source: previous.source, version: previous.version },
    updatedAt: new Date().toISOString(),
  })
}

export async function cleanStagingDirectory(path) {
  await rm(path, { recursive: true, force: true })
}
