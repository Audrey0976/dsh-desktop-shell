const VERSION_PATTERN = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$/u

export function parseVersion(value) {
  const match = VERSION_PATTERN.exec(String(value))
  if (match === null) throw new Error(`Invalid semantic version: ${JSON.stringify(value)}`)
  return {
    raw: String(value),
    major: Number(match[1]),
    minor: Number(match[2]),
    patch: Number(match[3]),
    prerelease: match[4] === undefined ? [] : match[4].split('.'),
  }
}

function compareIdentifier(left, right) {
  const leftNumeric = /^\d+$/u.test(left)
  const rightNumeric = /^\d+$/u.test(right)
  if (leftNumeric && rightNumeric) return Number(left) - Number(right)
  if (leftNumeric) return -1
  if (rightNumeric) return 1
  return left.localeCompare(right, 'en')
}

export function compareVersions(leftValue, rightValue) {
  const left = typeof leftValue === 'string' ? parseVersion(leftValue) : leftValue
  const right = typeof rightValue === 'string' ? parseVersion(rightValue) : rightValue
  for (const key of ['major', 'minor', 'patch']) {
    if (left[key] !== right[key]) return left[key] - right[key]
  }
  if (left.prerelease.length === 0 && right.prerelease.length === 0) return 0
  if (left.prerelease.length === 0) return 1
  if (right.prerelease.length === 0) return -1
  const length = Math.max(left.prerelease.length, right.prerelease.length)
  for (let index = 0; index < length; index += 1) {
    if (left.prerelease[index] === undefined) return -1
    if (right.prerelease[index] === undefined) return 1
    const compared = compareIdentifier(left.prerelease[index], right.prerelease[index])
    if (compared !== 0) return compared
  }
  return 0
}

export function isCompatible(version, minimum, maximumExclusive) {
  try {
    const parsed = parseVersion(version)
    const floor = parseVersion(minimum)
    // During 0.x development, a minor bump is the compatibility boundary even
    // when the candidate is a prerelease that sorts below the final boundary.
    if (floor.major === 0 && (parsed.major !== 0 || parsed.minor !== floor.minor)) return false
    return compareVersions(parsed, floor) >= 0 && compareVersions(parsed, maximumExclusive) < 0
  } catch {
    return false
  }
}

export function newestVersion(versions) {
  return [...versions]
    .filter((version) => {
      try { parseVersion(version); return true } catch { return false }
    })
    .sort((left, right) => compareVersions(right, left))[0]
}

export function newestCompatibleVersion(versions, current, minimum, maximumExclusive, blocked = new Set()) {
  return [...versions]
    .filter(version => !blocked.has(version)
      && isCompatible(version, minimum, maximumExclusive)
      && compareVersions(version, current) > 0)
    .sort((left, right) => compareVersions(right, left))[0]
}
