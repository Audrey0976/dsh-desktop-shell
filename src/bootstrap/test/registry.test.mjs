import test from 'node:test'
import assert from 'node:assert/strict'
import { updateDecision } from '../lib/registry.mjs'

const compatibility = { minimum: '0.1.0-rc.6', maximumExclusive: '0.2.0' }

function metadata(...versions) {
  return {
    name: '@deepseek-ai/dsh',
    versions: Object.fromEntries(versions.map(version => [version, { dist: { integrity: `sha512-${version}` } }])),
  }
}

test('compatible update wins before a later compatibility boundary', () => {
  const decision = updateDecision(
    metadata('0.1.0-rc.6', '0.1.0-rc.7', '0.2.0'),
    '0.1.0-rc.6',
    compatibility,
  )
  assert.equal(decision.kind, 'compatible')
  assert.equal(decision.candidateVersion, '0.1.0-rc.7')
})

test('new incompatible line asks for a shell update', () => {
  const decision = updateDecision(metadata('0.1.0-rc.6', '0.2.0'), '0.1.0-rc.6', compatibility)
  assert.equal(decision.kind, 'boundary')
  assert.equal(decision.candidateVersion, '0.2.0')
})

test('no false boundary is reported when current is newest', () => {
  assert.equal(updateDecision(metadata('0.1.0-rc.6'), '0.1.0-rc.6', compatibility).kind, 'none')
})

test('failed versions are skipped during cooldown', () => {
  const decision = updateDecision(
    metadata('0.1.0-rc.6', '0.1.0-rc.7'),
    '0.1.0-rc.6',
    compatibility,
    new Set(['0.1.0-rc.7']),
  )
  assert.equal(decision.kind, 'none')
})
