# Runtime compatibility policy

DSH Desktop Shell and `@deepseek-ai/dsh` have independent versions. The shell
ships one known-good fallback and can activate newer compatible npm releases
without rebuilding the Windows host.

For shell `v0.1.x`:

- minimum: `0.1.0-rc.6`
- maximum: `<0.2.0`
- `0.2.0-*` prereleases are also treated as a boundary
- failed candidates are quarantined for seven days

Every candidate is installed by exact version into
`runtime/versions/<version>`, using npm registry integrity metadata. The
bootstrap starts it on a random loopback port and requires a URL readiness
signal plus a successful HTTP probe within 90 seconds. Only then does an
atomic `current.json` write activate it. Otherwise the previous known-good
runtime is restarted and remains current.

When upstream reaches stable 1.x, the intended policy is same-major updates
within a shell-tested range. A new major dsh line requires a shell release.

DOM-based convenience adapters are explicitly outside the core compatibility
contract: if upstream labels change, `Ctrl+B` or “New conversation” may be
temporarily unavailable while the Web UI and all server functions remain
usable.
