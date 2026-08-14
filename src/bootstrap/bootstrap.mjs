#!/usr/bin/env node
import { join, resolve } from 'node:path'
import { mkdir } from 'node:fs/promises'
import { BackendSupervisor } from './lib/backend.mjs'
import { createLogger } from './lib/log.mjs'
import { createProtocol, readCommands } from './lib/protocol.mjs'
import { resolveCurrentInstallation, validateInstallation, writeCurrentPointer } from './lib/files.mjs'
import { checkForUpdates, installVersion, markVersionFailed, metadataForExactVersion } from './lib/updater.mjs'

const MINIMUM_DSH = '0.1.0-rc.6'
const MAXIMUM_DSH_EXCLUSIVE = '0.2.0'

function argumentsToObject(arguments_) {
  const result = {}
  for (let index = 0; index < arguments_.length; index += 1) {
    const key = arguments_[index]
    if (!key.startsWith('--')) throw new Error(`unexpected argument ${JSON.stringify(key)}`)
    const value = arguments_[index + 1]
    if (value === undefined || value.startsWith('--')) throw new Error(`missing value for ${key}`)
    result[key.slice(2)] = value
    index += 1
  }
  return result
}

function configurationFromArguments(arguments_) {
  const values = argumentsToObject(arguments_)
  for (const required of ['app-dir', 'data-dir', 'bundled-version', 'bundled-dir']) {
    if (values[required] === undefined) throw new Error(`--${required} is required`)
  }
  return {
    appDirectory: resolve(values['app-dir']),
    dataDirectory: resolve(values['data-dir']),
    bundledVersion: values['bundled-version'],
    bundledDirectory: resolve(values['bundled-dir']),
    startupTimeoutMs: Number(values['startup-timeout-ms'] ?? 90000),
    updateIntervalMs: Number(values['update-interval-ms'] ?? 24 * 60 * 60 * 1000),
    registry: values.registry ?? 'https://registry.npmjs.org',
    compatibility: { minimum: MINIMUM_DSH, maximumExclusive: MAXIMUM_DSH_EXCLUSIVE },
  }
}

const protocol = createProtocol()
let configuration
let logger
let backend
let installation
let shuttingDown = false
let commandQueue = Promise.resolve()

function status(phase, message, extra = {}) {
  protocol.status(phase, message, {
    version: installation?.pointer.version,
    logPath: logger?.path,
    ...extra,
  })
  void logger?.write(phase, message)
}

async function startInstallation(nextInstallation, beforeReady) {
  status('backend-start', `Starting dsh ${nextInstallation.pointer.version}`)
  const url = await backend.start(nextInstallation, configuration.startupTimeoutMs)
  installation = nextInstallation
  if (beforeReady !== undefined) await beforeReady()
  protocol.emit('ready', {
    url,
    version: installation.pointer.version,
    logPath: logger.path,
  })
  return url
}

async function restartCurrent() {
  status('backend-restart', 'Restarting the local dsh service')
  await backend.stop()
  await startInstallation(installation)
}

async function updateTo(version) {
  if (version === installation.pointer.version) return
  status('update-download', `Preparing dsh ${version}`, { candidateVersion: version })
  let candidate
  try {
    const exact = await metadataForExactVersion(configuration, version)
    candidate = await installVersion(configuration, version, exact.integrity, logger)
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    await markVersionFailed(configuration, version, reason)
    status('update-failed', `Could not install dsh ${version}; the current version is still running`, {
      candidateVersion: version,
    })
    protocol.emit('ready', {
      url: backend.url,
      version: installation.pointer.version,
      logPath: logger.path,
    })
    return
  }
  const previous = installation
  status('update-verify', `Health-checking dsh ${version}`, { candidateVersion: version })
  await backend.stop()
  try {
    await startInstallation(candidate, () => writeCurrentPointer(configuration, candidate.pointer, previous.pointer))
    status('update-complete', `dsh ${version} is active`, { candidateVersion: version })
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    await logger.write('update', `candidate ${version} failed: ${reason}`)
    await markVersionFailed(configuration, version, reason)
    status('update-rollback', `dsh ${version} failed its health check; restoring ${previous.pointer.version}`, {
      candidateVersion: version,
    })
    await backend.stop()
    await startInstallation(previous, () => writeCurrentPointer(configuration, previous.pointer, undefined))
  }
}

async function announceUpdates(force = false) {
  try {
    status('update-check', 'Checking the npm registry for compatible dsh updates')
    const decision = await checkForUpdates(configuration, installation.pointer.version, force)
    if (decision.kind === 'compatible') {
      protocol.emit('update-available', {
        currentVersion: decision.currentVersion,
        candidateVersion: decision.candidateVersion,
        compatible: true,
        requiresShellUpdate: false,
        logPath: logger.path,
      })
    } else if (decision.kind === 'boundary') {
      protocol.emit('update-available', {
        currentVersion: decision.currentVersion,
        candidateVersion: decision.candidateVersion,
        compatible: false,
        requiresShellUpdate: true,
        logPath: logger.path,
      })
    } else if (force && decision.kind === 'none') {
      status('update-current', 'The installed dsh version is the newest compatible release')
    }
  } catch (error) {
    await logger.write('update-check', `continuing offline: ${error instanceof Error ? error.message : String(error)}`)
    status('update-offline', 'Update check was unavailable; continuing with the installed dsh version')
  }
}

async function shutdown(exitCode = 0) {
  if (shuttingDown) return
  shuttingDown = true
  status('shutdown', 'Stopping the local dsh service')
  await backend?.stop()
  await logger?.flush()
  process.exitCode = exitCode
  setImmediate(() => process.exit(exitCode))
}

async function handleCommand(command) {
  switch (command.command) {
    case 'update':
      if (typeof command.version !== 'string') throw new Error('update requires a version')
      await updateTo(command.version)
      break
    case 'restart':
      await restartCurrent()
      break
    case 'shutdown':
      await shutdown(0)
      break
    case 'check-update':
      await announceUpdates(true)
      break
    default:
      status('invalid-command', `Ignoring unknown bootstrap command ${JSON.stringify(command.command)}`)
  }
}

async function main() {
  configuration = configurationFromArguments(process.argv.slice(2))
  await mkdir(join(configuration.dataDirectory, 'logs'), { recursive: true })
  logger = await createLogger(join(configuration.dataDirectory, 'logs', 'bootstrap.log'))
  status('bootstrap', 'Runtime bootstrap started')

  backend = new BackendSupervisor({
    logger,
    onUnexpectedExit: ({ code, signal }) => {
      if (shuttingDown) return
      protocol.emit('fatal', {
        phase: 'backend-exit',
        message: `The local dsh service exited unexpectedly (code ${String(code)}, signal ${String(signal)})`,
        version: installation?.pointer.version,
        logPath: logger.path,
      })
    },
  })

  status('runtime-resolve', 'Resolving the current dsh runtime')
  installation = await resolveCurrentInstallation(configuration)
  try {
    await startInstallation(installation)
  } catch (firstError) {
    const currentPointer = installation.pointer
    const saved = JSON.parse(await (await import('node:fs/promises')).readFile(join(configuration.dataDirectory, 'runtime', 'current.json'), 'utf8'))
    const fallbackPointers = [saved.previous, { source: 'bundled', version: configuration.bundledVersion }]
    let recovered = false
    for (const fallbackPointer of fallbackPointers) {
      if (fallbackPointer === null || fallbackPointer === undefined
        || (fallbackPointer.source === currentPointer.source && fallbackPointer.version === currentPointer.version)) continue
      const fallback = await validateInstallation(fallbackPointer, configuration)
      if (fallback === undefined) continue
      try {
        await backend.stop()
        status('startup-rollback', `Restoring dsh ${fallback.pointer.version}`)
        await startInstallation(fallback, () => writeCurrentPointer(configuration, fallback.pointer, undefined))
        await markVersionFailed(configuration, currentPointer.version, firstError instanceof Error ? firstError.message : String(firstError))
        recovered = true
        break
      } catch (fallbackError) {
        await logger.write('startup-rollback', fallbackError instanceof Error ? fallbackError.message : String(fallbackError))
      }
    }
    if (!recovered) throw firstError
  }

  readCommands(process.stdin, (command) => {
    commandQueue = commandQueue.then(() => handleCommand(command)).catch(async (error) => {
      const message = error instanceof Error ? error.message : String(error)
      await logger.write('command', message)
      status('command-error', message)
    })
  }, () => { void shutdown(0) })
  process.once('SIGINT', () => { void shutdown(0) })
  process.once('SIGTERM', () => { void shutdown(0) })
  void announceUpdates(false)
}

main().catch(async (error) => {
  const message = error instanceof Error ? error.message : String(error)
  protocol.emit('fatal', {
    phase: 'bootstrap',
    message,
    version: installation?.pointer.version,
    logPath: logger?.path,
  })
  await logger?.write('fatal', message)
  await backend?.stop()
  await logger?.flush()
  process.exitCode = 1
})
