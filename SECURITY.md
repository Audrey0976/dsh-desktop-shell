# Security policy / 安全策略

## 中文

预览期仅支持最新的 DSH Desktop Shell 版本。对于尚未由桌面壳验证的新 dsh 兼容系列，桌面壳可能主动阻止升级。

涉及凭据泄露、任意导航、更新完整性、安装器权限或进程归属的漏洞，请勿创建公开 Issue；请使用仓库的 GitHub 私密安全报告表单。复现材料不得包含真实 API Key 或对话内容。项目计划在七天内确认收到完整报告，但不提供漏洞赏金，也不承诺固定修复时限。

如果问题完全来自上游 DeepSeek Harness，也请同时报告给上游维护者。本项目不会静默替换桌面壳可执行文件；首个预览版未签名，桌面壳更新只会打开 GitHub Releases 下载页。

## English

Only the newest DSH Desktop Shell release is supported during the preview. The shell may deliberately block a newer dsh compatibility line until a shell release has validated it.

Do not open a public issue for a vulnerability involving credential exposure, arbitrary navigation, update integrity, installer privilege, or process ownership. Use the repository's private GitHub Security Advisory reporting form. Include a minimal reproduction without real API keys or conversations. The project aims to acknowledge a complete report within seven days; there is no bug bounty or guaranteed remediation timeline.

Reports caused solely by upstream DeepSeek Harness should also be reported to its maintainers. This project does not silently replace the shell executable; the first preview is unsigned and links users to GitHub Releases for shell updates.
