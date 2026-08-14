import test from 'node:test'
import assert from 'node:assert/strict'
import {
  compareVersions,
  isCompatible,
  newestCompatibleVersion,
  parseVersion,
} from '../lib/semver.mjs'

test('semantic versions order release candidates correctly', () => {
  assert.ok(compareVersions('0.1.0-rc.6', '0.1.0-rc.5') > 0)
  assert.ok(compareVersions('0.1.0', '0.1.0-rc.99') > 0)
  assert.ok(compareVersions('0.1.1', '0.1.0') > 0)
  assert.equal(compareVersions('1.2.3+build.1', '1.2.3+build.2'), 0)
})

test('preview compatibility boundary is enforced', () => {
  assert.equal(isCompatible('0.1.0-rc.6', '0.1.0-rc.6', '0.2.0'), true)
  assert.equal(isCompatible('0.1.9', '0.1.0-rc.6', '0.2.0'), true)
  assert.equal(isCompatible('0.2.0-rc.1', '0.1.0-rc.6', '0.2.0'), false)
  assert.equal(isCompatible('0.1.0-rc.5', '0.1.0-rc.6', '0.2.0'), false)
})

test('newest compatible version excludes failed candidates', () => {
  const candidate = newestCompatibleVersion(
    ['0.1.0-rc.6', '0.1.0-rc.7', '0.1.0-rc.8', '0.2.0'],
    '0.1.0-rc.6',
    '0.1.0-rc.6',
    '0.2.0',
    new Set(['0.1.0-rc.8']),
  )
  assert.equal(candidate, '0.1.0-rc.7')
})

test('invalid versions fail closed', () => {
  assert.throws(() => parseVersion('latest'))
  assert.equal(isCompatible('latest', '0.1.0-rc.6', '0.2.0'), false)
})
