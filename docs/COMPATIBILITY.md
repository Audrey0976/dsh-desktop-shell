# Runtime compatibility policy / 运行时兼容策略

## 中文

DSH Desktop Shell 与 `@deepseek-ai/dsh` 独立版本化。桌面壳内置一个已知可用的回退版本，也可以在不重建 Windows 宿主的情况下启用兼容的新 npm 版本。

桌面壳 `v0.1.x` 的兼容范围：

- 最低 `0.1.0-rc.6`
- 最高（不含）`0.2.0`
- `0.2.0-*` 预发布版同样视为兼容边界
- 启动失败的候选版本屏蔽七天

候选版本按精确版本安装到 `runtime/versions/<version>`，并使用 npm registry integrity 元数据。bootstrap 在随机回环端口启动候选版本；90 秒内必须收到 URL 就绪信号且 HTTP 探测成功，之后才原子更新 `current.json`。否则会重新启动上一已知可用版本并保持其为当前版本。离线或检查失败时继续使用现有版本。

上游进入稳定 1.x 后，计划在桌面壳验证范围内按同一主版本升级；新的 dsh 主版本需要新的桌面壳版本。依赖 DOM 的快捷功能不属于核心兼容合同：上游标签变化时，`Ctrl+B` 或“新会话”可能暂时不可用，但 Web UI 与服务端功能仍应正常运行。

## English

DSH Desktop Shell and `@deepseek-ai/dsh` have independent versions. The shell ships one known-good fallback and can activate newer compatible npm releases without rebuilding the Windows host.

For shell `v0.1.x`:

- minimum: `0.1.0-rc.6`
- maximum (exclusive): `0.2.0`
- `0.2.0-*` prereleases are also treated as a boundary
- failed candidates are quarantined for seven days

Every candidate is installed by exact version into `runtime/versions/<version>`, using npm registry integrity metadata. The bootstrap starts it on a random loopback port and requires a URL readiness signal plus a successful HTTP probe within 90 seconds. Only then does an atomic `current.json` write activate it. Otherwise the previous known-good runtime is restarted and remains current. Offline or failed checks continue with the installed version.

When upstream reaches stable 1.x, the intended policy is same-major updates within a shell-tested range. A new major dsh line requires a shell release. DOM-based convenience adapters are outside the core compatibility contract: if upstream labels change, `Ctrl+B` or “New conversation” may be temporarily unavailable while the Web UI and server functions remain usable.
