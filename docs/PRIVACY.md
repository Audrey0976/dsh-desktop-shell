# Privacy

DSH Desktop Shell adds no analytics, advertising identifier, crash upload, or
silent executable update. It contacts:

- the local random loopback origin to display dsh;
- the npm registry at most once every 24 hours to check compatible dsh
  versions, and when the user confirms an update;
- GitHub only when the user explicitly checks for a shell update or opens a
  project/release link.

Lifecycle logs are stored locally, capped/rotated, length-limited, and redact
common token/API-key forms. The shell does not intentionally write prompt or
conversation bodies. Do not publish logs without reviewing them.

The upstream dsh runtime and configured model providers can make their own
network requests. Their behavior is not controlled or represented by this
desktop shell; review the upstream project and your provider configuration.
