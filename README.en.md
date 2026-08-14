# DSH Desktop Shell

[简体中文](README.md) · [Installation](docs/INSTALL.en.md) · [Compatibility policy](docs/COMPATIBILITY.md)

**Unofficial community desktop shell.** It runs the official
[`@deepseek-ai/dsh`](https://github.com/deepseek-ai/deepseek-harness) Web UI as
a local Windows application. This project does not fork or patch the upstream
Web UI and is not affiliated with or endorsed by DeepSeek.

## v0.1 features

- Native Windows 10/11 x64 WinForms + WebView2 host.
- Integrated 32 px title/menu bar, native Windows menus, light/dark chrome,
  and a metal-outline whale startup animation.
- Close-to-tray behavior with open, reload, service restart, update, logs, and
  true-exit commands.
- Self-contained Node 24 and a verified `@deepseek-ai/dsh@0.1.0-rc.6` fallback.
- Random loopback port on every service start; WebView navigation is restricted
  to the exact origin returned by the runtime bootstrap.
- Prompted compatible dsh updates with atomic activation, a 90-second health
  check, rollback, and failed-version quarantine.
- Version-tolerant DOM adapters for conveniences such as `Ctrl+B`; a broken
  adapter disables only that convenience, not the upstream UI.

## Downloads

The initial `v0.1.0` release is an unsigned GitHub pre-release and can trigger
Windows SmartScreen. Releases contain a per-user installer, a portable ZIP,
SHA-256 checksums, an SPDX SBOM, and third-party notices.

The installed edition honors `DSH_HOME`, falling back to upstream's `~/.dsh`.
The portable edition defaults to `data\dsh-home` beside the executable. That
folder may contain API keys; inspect it before copying or uploading the folder.

See [Installation](docs/INSTALL.en.md) for details.

## Independent update tracks

The shell starts at `v0.1.0`; its bundled fallback is dsh `0.1.0-rc.6`. During
the upstream preview, automatic runtime updates are constrained to
`>=0.1.0-rc.6 <0.2.0`, excluding `0.2.0-*`. A compatibility-boundary release
prompts for a new shell instead of being installed in place.

## Privacy and security

The shell adds no telemetry, crash upload, or silent self-update. Lifecycle
logs are length-limited and redact common credential forms; they do not
intentionally record conversation content. This statement applies to the
desktop shell itself—upstream dsh behavior depends on its version and user
configuration. See [Privacy](docs/PRIVACY.md) and [Security](SECURITY.md).

## Build

On Windows, install the .NET 8 SDK, Node 24, and Inno Setup 6, then run:

```powershell
npm test --prefix src/bootstrap
dotnet build DSH.Desktop.Shell.sln -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File scripts/Build-Windows.ps1
```

The shell is MIT licensed. Third-party components retain their own licenses;
see [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).
