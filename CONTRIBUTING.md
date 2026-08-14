# Contributing / 贡献指南

感谢参与 DSH Desktop Shell。请先在 Issue 中说明用户问题、Windows 版本、桌面壳版本、dsh 版本和可复现步骤；不要粘贴 API Key、凭据、对话正文或完整 WebView 数据目录。

Thank you for contributing. Open an issue with the Windows, shell, and dsh
versions plus minimal reproduction steps. Never attach API keys, credentials,
conversation content, or an entire WebView profile.

## Development checks

```powershell
npm test --prefix src/bootstrap
dotnet build DSH.Desktop.Shell.sln -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File scripts/Test-RepositoryHygiene.ps1
```

Runtime-management changes must include protocol tests for offline behavior,
installation failure, health-check failure, and rollback. UI enhancements that
query upstream DOM must fail closed and must not modify or inject replacement
upstream assets.

Pull requests should update `CHANGELOG.md` when behavior changes. By
contributing, you agree that your contribution is licensed under this
repository's MIT license.
