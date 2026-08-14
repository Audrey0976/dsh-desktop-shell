[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$self = [IO.Path]::GetFullPath($PSCommandPath)
$ignoredDirectories = @('.git', 'artifacts', '.cache', '.tools', 'bin', 'obj', 'node_modules', 'data')
$textExtensions = @('.cs', '.csproj', '.sln', '.mjs', '.js', '.json', '.md', '.txt', '.ps1', '.yml', '.yaml', '.iss', '.config', '.manifest', '.svg')
$failures = New-Object Collections.Generic.List[string]

$files = Get-ChildItem -LiteralPath $repoRoot -Recurse -File | Where-Object {
    $relative = $_.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
    $segments = $relative -split '[\\/]'
    -not ($segments | Where-Object { $ignoredDirectories -contains $_ })
}

foreach ($file in $files) {
    $relative = $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
    if ($relative -match '(?i)(^|[\/])(?:EBWebView|WebView2)([\/]|$)' -or
        $relative -match '(?i)\.(?:bak|pfx|snk|user|suo|log|pid)$') {
        $failures.Add("Disallowed local/runtime artifact: $relative")
    }
    if ($file.Extension -in @('.exe', '.dll', '.zip', '.7z', '.nupkg')) {
        $failures.Add("Binary build/download artifact must not be committed: $relative")
    }
    if ($textExtensions -notcontains $file.Extension.ToLowerInvariant() -or $file.FullName -eq $self) { continue }
    $content = [IO.File]::ReadAllText($file.FullName)
    if ($content -match '(?i)[A-Z]:[\\/]Users[\\/][^%<\\/]+') {
        $failures.Add("Personal absolute path in $relative")
    }
    $legacyDrive = 'D:' + '\' + 'deepseek-harness'
    $legacyInstall = 'D:' + '\' + 'DeepSeek Harness'
    if ($content.IndexOf($legacyDrive, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $content.IndexOf($legacyInstall, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        $failures.Add("Legacy D-drive path in $relative")
    }
    if ($content -match '\bsk-[A-Za-z0-9_-]{16,}\b' -or
        $content -match '(?im)^\s*(?:DEEPSEEK_API_KEY|OPENAI_API_KEY|ANTHROPIC_API_KEY)\s*=\s*\S+') {
        $failures.Add("Possible API key or populated secret in $relative")
    }
}

if ($failures.Count -gt 0) {
    $failures | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
    throw "Repository hygiene check failed with $($failures.Count) finding(s)."
}

Write-Host "Repository hygiene check passed ($($files.Count) files inspected)."
