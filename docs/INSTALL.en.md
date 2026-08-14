# Windows installation

Download the installer or portable ZIP and `SHA256SUMS.txt` from the same
GitHub release. Verify with `Get-FileHash <file> -Algorithm SHA256`.

The installer is per-user and writes to
`%LOCALAPPDATA%\Programs\DSH Desktop Shell`; it does not require administrator
rights. It detects .NET Framework 4.8 and Evergreen WebView2 Runtime and opens
Microsoft's installation guidance when either prerequisite is absent. The
initial preview is unsigned, so SmartScreen can report an unknown publisher.

The portable ZIP must remain intact. `portable.marker` causes runtime state,
logs, WebView data, and the default Harness home to live under `data\` beside
the executable. That directory can contain API keys and conversations; never
publish a used portable folder.

Uninstall removes the program and shortcuts but intentionally preserves app
data and `DSH_HOME`/`~/.dsh`.
