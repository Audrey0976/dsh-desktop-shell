import { spawn } from 'node:child_process'
import { cp, mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises'
import { join } from 'node:path'
import { randomUUID } from 'node:crypto'
import { cleanStagingDirectory, runtimePaths, validateInstallation, writeJsonAtomic } from './files.mjs'
import { fetchPackageMetadata, updateDecision } from './registry.mjs'
import { isCompatible } from './semver.mjs'

const FAILED_VERSION_COOLDOWN_MS = 7 * 24 * 60 * 60 * 1000

export function activeFailedVersions(state, now = Date.now()) {
  return new Set(Object.entries(state?.failedVersions ?? {})
    .filter(([, failure]) => now - Date.parse(failure.failedAt) < FAILED_VERSION_COOLDOWN_MS)
    .map(([version]) => version))
}

export async function readUpdateState(configuration) {
  try {
    return JSON.parse(await readFile(runtimePaths(configuration.dataDirectory).updateState, 'utf8'))
  } catch {
    return { schemaVersion: 1, failedVersions: {} }
  }
}

export async function writeUpdateState(configuration, state) {
  await writeJsonAtomic(runtimePaths(configuration.dataDirectory).updateState, {
    schemaVersion: 1,
    failedVersions: {},
    ...state,
  })
}

export async function markVersionFailed(configuration, version, reason) {
  const state = await readUpdateState(configuration)
  state.failedVersions ??= {}
  state.failedVersions[version] = {
    failedAt: new Date().toISOString(),
    reason: String(reason).slice(0, 300),
  }
  await writeUpdateState(configuration, state)
}

async function runNpmInstall(configuration, version, stagingDirectory, logger) {
  const npmCli = join(configuration.appDirectory, 'runtime', 'node', 'node_modules', 'npm', 'bin', 'npm-cli.js')
  await writeFile(join(stagingDirectory, 'package.json'), `${JSON.stringify({ private: true })}\n`, 'utf8')
  const arguments_ = [
    npmCli, 'install', '--prefix', stagingDirectory, '--omit=dev', '--no-audit', '--no-fund',
    '--package-lock=false', '--ignore-scripts', '--registry', configuration.registry,
    `@deepseek-ai/dsh@${version}`,
  ]
  await logger.write('update', `installing exact npm package @deepseek-ai/dsh@${version}`)
  await new Promise((resolve, reject) => {
    const child = spawn(process.execPath, arguments_, {
      cwd: stagingDirectory,
      env: { ...process.env, NO_COLOR: '1' },
      windowsHide: true,
      stdio: ['ignore', 'ignore', 'pipe'],
    })
    let stderr = ''
    child.stderr.setEncoding('utf8')
    child.stderr.on('data', chunk => { stderr = `${stderr}${chunk}`.slice(-4000) })
    child.once('error', reject)
    child.once('exit', code => {
      if (code === 0) resolve()
      else reject(new Error(`npm install failed with code ${String(code)}: ${stderr.replace(/[\r\n]+/gu, ' ').slice(-800)}`))
    })
  })
}

async function installFromTestFixture(configuration, version, stagingDirectory) {
  const source = join(process.env.DSH_DESKTOP_TEST_FIXTURES_DIR, version)
  await cp(source, stagingDirectory, { recursive: true })
}

export async function installVersion(configuration, version, expectedIntegrity, logger) {
  if (!isCompatible(version, configuration.compatibility.minimum, configuration.compatibility.maximumExclusive)) {
    throw new Error(`dsh ${version} is outside this shell's compatibility range`)
  }
  if (typeof expectedIntegrity !== 'string' || !expectedIntegrity.startsWith('sha512-')) {
    throw new Error(`npm metadata for dsh ${version} has no SHA-512 integrity value`)
  }
  const paths = runtimePaths(configuration.dataDirectory)
  await mkdir(paths.versions, { recursive: true })
  const destination = join(paths.versions, version)
  const existing = await validateInstallation({ source: 'managed', version }, configuration)
  if (existing !== undefined) return existing

  const staging = join(paths.versions, `.staging-${version}-${randomUUID()}`)
  await cleanStagingDirectory(staging)
  await mkdir(staging, { recursive: true })
  try {
    if (process.env.NODE_ENV === 'test' && process.env.DSH_DESKTOP_TEST_FIXTURES_DIR !== undefined) {
      await installFromTestFixture(configuration, version, staging)
    } else {
      await runNpmInstall(configuration, version, staging, logger)
    }
    const packageManifest = JSON.parse(await readFile(join(staging, 'node_modules', '@deepseek-ai', 'dsh', 'package.json'), 'utf8'))
    if (packageManifest.name !== '@deepseek-ai/dsh' || packageManifest.version !== version) {
      throw new Error(`installed package identity mismatch for dsh ${version}`)
    }
    await writeFile(join(staging, 'install.json'), `${JSON.stringify({
      schemaVersion: 1,
      package: '@deepseek-ai/dsh',
      version,
      integrity: expectedIntegrity,
      installedAt: new Date().toISOString(),
    }, null, 2)}\n`, 'utf8')
    await rm(destination, { recursive: true, force: true })
    await rename(staging, destination)
    const installed = await validateInstallation({ source: 'managed', version }, configuration)
    if (installed === undefined) throw new Error(`installed dsh ${version} failed validation`)
    return installed
  } catch (error) {
    await cleanStagingDirectory(staging)
    throw error
  }
}

export async function checkForUpdates(configuration, currentVersion, force = false) {
  const state = await readUpdateState(configuration)
  const lastChecked = Date.parse(state.lastCheckedAt ?? '')
  if (!force && Number.isFinite(lastChecked) && Date.now() - lastChecked < configuration.updateIntervalMs) {
    return { kind: 'skipped' }
  }
  const metadata = await fetchPackageMetadata(configuration.registry)
  const decision = updateDecision(
    metadata,
    currentVersion,
    configuration.compatibility,
    activeFailedVersions(state),
  )
  state.lastCheckedAt = new Date().toISOString()
  state.lastObservedVersion = decision.candidateVersion ?? currentVersion
  await writeUpdateState(configuration, state)
  return { ...decision, metadata }
}

export async function metadataForExactVersion(configuration, version) {
  const metadata = await fetchPackageMetadata(configuration.registry)
  const manifest = metadata.versions?.[version]
  if (manifest === undefined) throw new Error(`dsh ${version} is not published on the configured npm registry`)
  return { metadata, manifest, integrity: manifest.dist?.integrity }
}
