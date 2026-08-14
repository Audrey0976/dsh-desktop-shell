[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [string]$DshVersion = '0.1.0-rc.6',
    [string]$NodeVersion = '24.18.1',
    [string]$Configuration = 'Release',
    [string]$OutputRoot,
    [string]$InnoCompiler,
    [switch]$SkipInstaller,
    [switch]$SkipRuntimeSmoke
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([String]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repoRoot 'artifacts' }
$output = [IO.Path]::GetFullPath($OutputRoot)
$work = Join-Path $output 'work'
$baseStage = Join-Path $work 'app'
$portableStage = Join-Path $work 'portable'
$release = Join-Path $output 'release'
$cache = Join-Path $repoRoot '.cache'
$project = Join-Path $repoRoot 'src\windows\DSH.Desktop.Shell\DSH.Desktop.Shell.csproj'

function Assert-ChildPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside repository: $resolved"
    }
    return $resolved
}

function Reset-Directory([string]$Path) {
    $safe = Assert-ChildPath $Path
    if (Test-Path -LiteralPath $safe) { Remove-Item -LiteralPath $safe -Recurse -Force }
    New-Item -ItemType Directory -Path $safe -Force | Out-Null
}

function Copy-Directory([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    & robocopy.exe $Source $Destination /E /COPY:DAT /DCOPY:DAT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "robocopy failed with exit code $LASTEXITCODE while copying $Source" }
}

function New-DeterministicZip([string]$Source, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath (Assert-ChildPath $Destination) -Force }
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $timestamp = if ($env:SOURCE_DATE_EPOCH) {
                [DateTimeOffset]::FromUnixTimeSeconds([Int64]$env:SOURCE_DATE_EPOCH)
            } else { [DateTimeOffset]::Parse('2026-08-14T00:00:00Z') }
            Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
                $relative = $_.FullName.Substring($sourceRoot.Length + 1).Replace('\', '/')
                $entry = $archive.CreateEntry($relative, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $timestamp
                $input = [IO.File]::OpenRead($_.FullName)
                $outputStream = $entry.Open()
                try { $input.CopyTo($outputStream) } finally { $outputStream.Dispose(); $input.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK is required to build the Windows shell.' }
if ((dotnet --list-sdks).Count -eq 0) { throw '.NET SDK is required; only a runtime was found.' }

Reset-Directory $work
Reset-Directory $release
New-Item -ItemType Directory -Path $cache -Force | Out-Null

Write-Host 'Building Windows shell...'
& dotnet publish $project -c $Configuration -p:Platform=x64 -p:Version=$Version -o $baseStage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
Get-ChildItem -LiteralPath $baseStage -Filter *.pdb -File | Remove-Item -Force

$bootstrapTarget = Join-Path $baseStage 'runtime\bootstrap'
New-Item -ItemType Directory -Path $bootstrapTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\bootstrap\bootstrap.mjs') -Destination $bootstrapTarget
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\bootstrap\package.json') -Destination $bootstrapTarget
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\bootstrap\lib') -Destination $bootstrapTarget -Recurse

$nodeArchiveName = "node-v$NodeVersion-win-x64.zip"
$nodeArchive = Join-Path $cache $nodeArchiveName
$nodeChecksums = Join-Path $cache "node-v$NodeVersion-SHASUMS256.txt"
$nodeBaseUrl = "https://nodejs.org/dist/v$NodeVersion"
if (-not (Test-Path -LiteralPath $nodeArchive)) { Invoke-WebRequest "$nodeBaseUrl/$nodeArchiveName" -OutFile $nodeArchive -UseBasicParsing }
Invoke-WebRequest "$nodeBaseUrl/SHASUMS256.txt" -OutFile $nodeChecksums -UseBasicParsing
$checksumLine = Get-Content -LiteralPath $nodeChecksums | Where-Object { $_ -match "\s+$([Regex]::Escape($nodeArchiveName))$" } | Select-Object -First 1
if ([String]::IsNullOrWhiteSpace($checksumLine)) { throw "Node checksum file does not list $nodeArchiveName" }
$expectedNodeHash = ($checksumLine -split '\s+')[0].ToLowerInvariant()
$actualNodeHash = (Get-FileHash -LiteralPath $nodeArchive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualNodeHash -ne $expectedNodeHash) { throw "Node archive SHA-256 mismatch: expected $expectedNodeHash, got $actualNodeHash" }

$nodeExtract = Join-Path $work 'node-extract'
Reset-Directory $nodeExtract
Expand-Archive -LiteralPath $nodeArchive -DestinationPath $nodeExtract -Force
$nodeSource = Join-Path $nodeExtract "node-v$NodeVersion-win-x64"
$nodeTarget = Join-Path $baseStage 'runtime\node'
Copy-Directory $nodeSource $nodeTarget
$bundledNode = Join-Path $nodeTarget 'node.exe'
$npmCli = Join-Path $nodeTarget 'node_modules\npm\bin\npm-cli.js'

$integrityJson = & $bundledNode $npmCli view "@deepseek-ai/dsh@$DshVersion" dist.integrity --json
if ($LASTEXITCODE -ne 0) { throw 'Could not read dsh integrity metadata from npm.' }
$dshIntegrity = $integrityJson | ConvertFrom-Json
if (-not ([string]$dshIntegrity).StartsWith('sha512-')) { throw 'dsh npm metadata did not contain SHA-512 integrity.' }

$dshTarget = Join-Path $baseStage "runtime\versions\$DshVersion"
$dshRuntimeCache = Join-Path $cache "runtimes\dsh-$DshVersion-node-$NodeVersion-win-x64"
$dshRuntimeCacheMetadata = "$dshRuntimeCache.json"
$useRuntimeCache = $false
if ((Test-Path -LiteralPath $dshRuntimeCacheMetadata) -and (Test-Path -LiteralPath $dshRuntimeCache)) {
    try {
        $cached = Get-Content -LiteralPath $dshRuntimeCacheMetadata -Raw -Encoding UTF8 | ConvertFrom-Json
        $cachedPackage = Get-Content -LiteralPath (Join-Path $dshRuntimeCache 'node_modules\@deepseek-ai\dsh\package.json') -Raw -Encoding UTF8 | ConvertFrom-Json
        $useRuntimeCache = $cached.dshVersion -eq $DshVersion -and $cached.nodeVersion -eq $NodeVersion `
            -and $cached.integrity -eq [string]$dshIntegrity -and $cachedPackage.version -eq $DshVersion
    } catch { $useRuntimeCache = $false }
}
if ($useRuntimeCache) {
    Write-Host "Using verified local runtime cache for dsh $DshVersion."
    Copy-Directory $dshRuntimeCache $dshTarget
} else {
    New-Item -ItemType Directory -Path $dshTarget -Force | Out-Null
    $npmCache = Join-Path $cache 'npm'
    & $bundledNode $npmCli install --prefix $dshTarget --omit=dev --no-audit --no-fund --package-lock=false --ignore-scripts --cache $npmCache "@deepseek-ai/dsh@$DshVersion"
    if ($LASTEXITCODE -ne 0) { throw "npm install for dsh failed with exit code $LASTEXITCODE" }
    Reset-Directory $dshRuntimeCache
    Copy-Directory $dshTarget $dshRuntimeCache
    $cacheMetadata = [ordered]@{ dshVersion = $DshVersion; nodeVersion = $NodeVersion; integrity = [string]$dshIntegrity }
    [IO.File]::WriteAllText($dshRuntimeCacheMetadata, ($cacheMetadata | ConvertTo-Json) + "`n", (New-Object Text.UTF8Encoding($false)))
}
$dshManifestPath = Join-Path $dshTarget 'node_modules\@deepseek-ai\dsh\package.json'
$dshManifest = Get-Content -LiteralPath $dshManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($dshManifest.name -ne '@deepseek-ai/dsh' -or $dshManifest.version -ne $DshVersion) { throw 'Packaged dsh identity does not match the requested exact version.' }
$bundledManifest = [ordered]@{
    schemaVersion = 1
    shellVersion = $Version
    dshVersion = $DshVersion
    dshIntegrity = [string]$dshIntegrity
    nodeVersion = $NodeVersion
    nodeArchive = $nodeArchiveName
    nodeSha256 = $actualNodeHash
    webView2SdkVersion = '1.0.4078.44'
}
[IO.File]::WriteAllText((Join-Path $baseStage 'runtime\bundled-version.json'), ($bundledManifest | ConvertTo-Json -Depth 4) + "`n", (New-Object Text.UTF8Encoding($false)))

Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $baseStage
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.txt') -Destination $baseStage
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination (Join-Path $baseStage 'README.zh-CN.md')
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.en.md') -Destination $baseStage

if (-not $SkipRuntimeSmoke) {
    Write-Host 'Running packaged runtime smoke test...'
    & $bundledNode (Join-Path $repoRoot 'scripts\Smoke-Runtime.mjs') $baseStage
    if ($LASTEXITCODE -ne 0) { throw "Packaged runtime smoke failed with exit code $LASTEXITCODE" }
}

Reset-Directory $portableStage
Copy-Directory $baseStage $portableStage
New-Item -ItemType File -Path (Join-Path $portableStage 'portable.marker') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\PORTABLE_WARNING.zh-CN.txt') -Destination (Join-Path $portableStage 'PORTABLE-DATA-WARNING.zh-CN.txt')
$portableName = "DSH-Desktop-Shell-$Version-win-x64-portable.zip"
New-DeterministicZip $portableStage (Join-Path $release $portableName)

if (-not $SkipInstaller) {
    if ([String]::IsNullOrWhiteSpace($InnoCompiler)) {
        $candidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
            (Join-Path $repoRoot '.tools\inno\ISCC.exe')
        )
        $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if ([String]::IsNullOrWhiteSpace($InnoCompiler) -or -not (Test-Path -LiteralPath $InnoCompiler)) {
        throw 'Inno Setup 6 compiler was not found. Pass -InnoCompiler or use -SkipInstaller.'
    }
    $payloadZip = Join-Path $release $portableName
    & $InnoCompiler "/DAppVersion=$Version" "/DStageDir=$baseStage" "/DPayloadZip=$payloadZip" "/DReleaseDir=$release" (Join-Path $repoRoot 'installer\DSHDesktopShell.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
}

$sbomName = "DSH-Desktop-Shell-$Version-win-x64.spdx.json"
& (Join-Path $repoRoot 'scripts\New-Sbom.ps1') -StageDirectory $baseStage -OutputPath (Join-Path $release $sbomName) -ShellVersion $Version -DshVersion $DshVersion -NodeVersion $NodeVersion
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.txt') -Destination (Join-Path $release 'THIRD_PARTY_NOTICES.txt')

$hashTargets = Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name
$hashLines = foreach ($file in $hashTargets) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($file.Name)"
}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'), $hashLines, (New-Object Text.UTF8Encoding($false)))
Write-Host "Release artifacts are ready in $release"
Get-ChildItem -LiteralPath $release -File | Select-Object Name, Length
