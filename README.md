# DSH Desktop Shell

[English](README.en.md) · [安装说明](docs/INSTALL.zh-CN.md) · [兼容策略](docs/COMPATIBILITY.md)

**非官方社区桌面壳（Unofficial）**：把官方
[`@deepseek-ai/dsh`](https://github.com/deepseek-ai/deepseek-harness)
Web UI 作为本地 Windows 应用运行。项目不 fork、不修改官方 Web UI，也不代表 DeepSeek 官方。

## 首版能力

- Windows 10/11 x64 原生 WinForms + WebView2 窗口。
- 32px 一体化标题栏、Windows 原生菜单、深浅主题、金属灰鲸鱼启动动画。
- 关闭窗口后驻留系统托盘；托盘菜单可重新打开、重启服务、检查更新、查看日志和真正退出。
- 内置 Node 24 与已验证的 `@deepseek-ai/dsh@0.1.0-rc.6`，无需预装 Node。
- 后台使用随机回环端口，只允许 WebView 导航到本次启动返回的精确本地 origin。
- dsh 同一兼容系列可独立提示更新；安装失败或 90 秒健康检查失败时自动回滚。
- `Ctrl+B`、`Ctrl+N` 等增强通过容错 DOM 适配器调用官方 UI；适配器失效不会阻止主界面运行。

## 下载与安装

首个公开版本为未签名的 `v0.1.0` Pre-release，会触发 Windows SmartScreen 提示。
Release 提供：

- `DSH-Desktop-Shell-0.1.0-win-x64-setup.exe`：当前用户安装到
  `%LOCALAPPDATA%\Programs\DSH Desktop Shell`。
- `DSH-Desktop-Shell-0.1.0-win-x64-portable.zip`：解压即用，数据写入自身 `data\`。
- `SHA256SUMS.txt`、SPDX SBOM 和第三方许可证说明。

安装版默认沿用 `DSH_HOME`；未设置时使用官方默认 `~/.dsh`，因此可与 CLI/浏览器版共享会话与凭据。
便携版默认使用自身 `data\dsh-home`，其中可能含 API Key，复制 U 盘或上传压缩包前请先检查。

完整步骤见[安装说明](docs/INSTALL.zh-CN.md)。

## 更新边界

桌面壳与 dsh 分别版本化：

- 桌面壳：`v0.1.0`
- 内置回退 dsh：`0.1.0-rc.6`
- 当前自动更新范围：`>=0.1.0-rc.6 <0.2.0`，并明确排除 `0.2.0-*`

兼容更新下载到应用数据目录的不可变版本文件夹，成功后原子切换 `current.json`；失败版本会回滚并隔离七天。
出现 `0.2.x` 等兼容边界时，只提示更新桌面壳，不直接安装。详见[兼容策略](docs/COMPATIBILITY.md)。

## 隐私与安全

- 桌面壳不加入遥测、崩溃上传或静默自更新。
- 日志仅记录启动/更新生命周期，限制长度并脱敏常见密钥格式；不主动记录对话正文。
- API Key、会话、WebView 用户数据、日志、Node、dsh 和构建产物不会提交进 Git。
- 外部链接交给系统浏览器；WebView 只接受 bootstrap 返回的随机本地 origin。

这里描述的是桌面壳自身行为；上游 dsh 的网络请求和数据策略由其版本及用户配置决定。
详见[隐私说明](docs/PRIVACY.md)和[安全策略](SECURITY.md)。

## 开发

要求：Windows、.NET 8 SDK（用于构建 `net48` 项目）、Node 24、Inno Setup 6。

```powershell
npm test --prefix src/bootstrap
dotnet build DSH.Desktop.Shell.sln -c Release -p:Platform=x64
powershell -ExecutionPolicy Bypass -File scripts/Build-Windows.ps1
```

构建脚本固定 Node/dsh/WebView2 版本，校验 Node 官方 SHA-256，并生成 Setup、Portable、SHA256SUMS 和 SPDX SBOM。
更多信息见[贡献指南](CONTRIBUTING.md)与[发布指南](docs/RELEASING.md)。

## 许可证

桌面壳采用 [MIT](LICENSE) 许可证。DeepSeek Harness、Node.js、WebView2 和其他依赖遵循各自许可证，见
[THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)。
