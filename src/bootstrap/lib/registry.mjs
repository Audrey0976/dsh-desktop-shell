import { compareVersions, isCompatible, newestCompatibleVersion, newestVersion } from './semver.mjs'

const DEFAULT_REGISTRY = 'https://registry.npmjs.org'

export async function fetchPackageMetadata(registry = DEFAULT_REGISTRY, timeoutMs = 12000) {
  const endpoint = `${registry.replace(/\/$/u, '')}/@deepseek-ai%2Fdsh`
  const response = await fetch(endpoint, {
    headers: {
      accept: 'application/vnd.npm.install-v1+json, application/json',
      'user-agent': 'DSH-Desktop-Shell/0.1.0',
    },
    redirect: 'error',
    signal: AbortSignal.timeout(timeoutMs),
  })
  if (!response.ok) throw new Error(`npm registry returned HTTP ${response.status}`)
  const metadata = await response.json()
  if (metadata?.name !== '@deepseek-ai/dsh' || metadata?.versions === undefined) {
    throw new Error('npm registry returned invalid dsh metadata')
  }
  return metadata
}

export function updateDecision(metadata, currentVersion, compatibility, blockedVersions = new Set()) {
  const versions = Object.keys(metadata.versions ?? {})
  const compatibleCandidate = newestCompatibleVersion(
    versions,
    currentVersion,
    compatibility.minimum,
    compatibility.maximumExclusive,
    blockedVersions,
  )
  const overallLatest = newestVersion(versions)
  if (compatibleCandidate !== undefined) {
    return {
      kind: 'compatible',
      currentVersion,
      candidateVersion: compatibleCandidate,
      integrity: metadata.versions[compatibleCandidate]?.dist?.integrity,
    }
  }
  if (overallLatest !== undefined
    && compareVersions(overallLatest, currentVersion) > 0
    && !isCompatible(overallLatest, compatibility.minimum, compatibility.maximumExclusive)) {
    return { kind: 'boundary', currentVersion, candidateVersion: overallLatest }
  }
  return { kind: 'none', currentVersion }
}
