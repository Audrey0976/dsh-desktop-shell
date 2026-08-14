# Contributing / 贡献指南

## 中文

感谢参与 DSH Desktop Shell。请先在 Issue 中说明 Windows 版本、桌面壳版本、dsh 版本、用户问题和最小复现步骤。不要粘贴 API Key、凭据、对话正文或完整 WebView 数据目录。

提交前请运行：

```powershell
npm test --prefix src/bootstrap
dotnet build DSH.Desktop.Shell.sln -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File scripts/Test-RepositoryHygiene.ps1
```

运行时管理改动必须覆盖离线、安装失败、健康检查失败和回滚协议测试。依赖上游 DOM 的 UI 增强必须安全失效，不能替换或修改上游资源。行为变更应更新 `CHANGELOG.md`。提交贡献即表示你同意以本仓库的 MIT 许可证授权该贡献。

## English

Thank you for contributing. Open an issue with the Windows, shell, and dsh versions, the user-visible problem, and minimal reproduction steps. Never attach API keys, credentials, conversation content, or an entire WebView profile.

Run these checks before submitting:

```powershell
npm test --prefix src/bootstrap
dotnet build DSH.Desktop.Shell.sln -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File scripts/Test-RepositoryHygiene.ps1
```

Runtime-management changes must cover offline behavior, installation failure, health-check failure, and rollback in protocol tests. UI enhancements that query upstream DOM must fail closed and must not replace or modify upstream assets. Update `CHANGELOG.md` for behavior changes. By contributing, you agree that your contribution is licensed under this repository's MIT license.
