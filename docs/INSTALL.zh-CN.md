# Windows 安装说明

## 安装版

1. 从 GitHub Release 下载 `DSH-Desktop-Shell-<版本>-win-x64-setup.exe` 和 `SHA256SUMS.txt`。
2. 使用 `Get-FileHash <安装包> -Algorithm SHA256` 对照校验。
3. 运行安装包。安装器按当前用户写入 `%LOCALAPPDATA%\Programs\DSH Desktop Shell`，不要求管理员权限。
4. 如果缺少 .NET Framework 4.8 或 Evergreen WebView2 Runtime，安装器会说明原因并提供微软官方下载流程。
5. 首版未签名，Windows SmartScreen 可能显示“未知发布者”；请只从本仓库 Release 下载并核对哈希。

卸载只移除程序目录和快捷方式，默认保留 `%LOCALAPPDATA%\DSH Desktop Shell`、`DSH_HOME` 或 `~/.dsh` 中的数据。

## 便携版

解压完整 ZIP 后运行 `DSH Desktop Shell.exe`，不要只把 EXE 单独复制出来。`portable.marker` 会让运行时、日志、WebView 数据和默认 Harness home 写入同目录的 `data\`。

`data\dsh-home` 可能包含 API Key、工作区路径和会话。不要把用过的便携目录直接上传、同步到公开网盘或交给他人。

## 数据位置

| 内容 | 安装版 | 便携版 |
| --- | --- | --- |
| 程序 | `%LOCALAPPDATA%\Programs\DSH Desktop Shell` | 解压目录 |
| runtime 更新/日志/WebView | `%LOCALAPPDATA%\DSH Desktop Shell` | `<解压目录>\data` |
| 会话与凭据 | `$DSH_HOME`，否则 `~/.dsh` | `$DSH_HOME`，否则 `data\dsh-home` |

## 真正退出

点击关闭按钮只隐藏到托盘。右键托盘鲸鱼图标，选择“退出 DSH Desktop Shell”，bootstrap 会先请求 dsh 正常停止；超时才终止其进程树。
