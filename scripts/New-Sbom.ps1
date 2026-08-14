[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$StageDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][string]$ShellVersion,
    [Parameter(Mandatory = $true)][string]$DshVersion,
    [Parameter(Mandatory = $true)][string]$NodeVersion,
    [string]$WebView2Version = '1.0.4078.44'
)

$ErrorActionPreference = 'Stop'
$stage = [IO.Path]::GetFullPath($StageDirectory)
$seen = @{}
$packages = New-Object Collections.Generic.List[object]
$relationships = New-Object Collections.Generic.List[object]

function ConvertTo-SpdxId([string]$Name, [string]$Version) {
    $value = ($Name + '-' + $Version) -replace '[^A-Za-z0-9.-]', '-'
    return 'SPDXRef-Package-' + $value.Trim('-')
}

function Add-Package([string]$Name, [string]$Version, [string]$License, [string]$DownloadLocation) {
    $key = "$Name@$Version"
    if ($seen.ContainsKey($key)) { return $seen[$key] }
    $id = ConvertTo-SpdxId $Name $Version
    $seen[$key] = $id
    $packages.Add([ordered]@{
        name = $Name
        SPDXID = $id
        versionInfo = $Version
        downloadLocation = $(if ([String]::IsNullOrWhiteSpace($DownloadLocation)) { 'NOASSERTION' } else { $DownloadLocation })
        filesAnalyzed = $false
        licenseConcluded = 'NOASSERTION'
        licenseDeclared = $(if ([String]::IsNullOrWhiteSpace($License)) { 'NOASSERTION' } else { $License })
        copyrightText = 'NOASSERTION'
    })
    return $id
}

$shellPackageId = Add-Package 'DSH Desktop Shell' $ShellVersion 'MIT' 'NOASSERTION'
$nodeId = Add-Package 'Node.js' $NodeVersion 'MIT' "https://nodejs.org/dist/v$NodeVersion/"
$dshId = Add-Package '@deepseek-ai/dsh' $DshVersion 'MIT' "https://registry.npmjs.org/@deepseek-ai/dsh/-/dsh-$DshVersion.tgz"
$webViewId = Add-Package 'Microsoft.Web.WebView2' $WebView2Version 'NOASSERTION' "https://www.nuget.org/packages/Microsoft.Web.WebView2/$WebView2Version"

$relationships.Add([ordered]@{ spdxElementId = 'SPDXRef-DOCUMENT'; relationshipType = 'DESCRIBES'; relatedSpdxElement = $shellPackageId })
foreach ($dependency in @($nodeId, $dshId, $webViewId)) {
    $relationships.Add([ordered]@{ spdxElementId = $shellPackageId; relationshipType = 'DEPENDS_ON'; relatedSpdxElement = $dependency })
}

$nodeModules = Join-Path $stage "runtime\versions\$DshVersion\node_modules"
if (Test-Path -LiteralPath $nodeModules) {
    Get-ChildItem -LiteralPath $nodeModules -Recurse -Filter package.json -File | ForEach-Object {
        try {
            $manifest = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([String]::IsNullOrWhiteSpace($manifest.name) -or [String]::IsNullOrWhiteSpace($manifest.version)) { return }
            $license = if ($manifest.license -is [string]) { $manifest.license } else { 'NOASSERTION' }
            $download = "https://registry.npmjs.org/$([Uri]::EscapeDataString([string]$manifest.name))"
            $dependencyId = Add-Package ([string]$manifest.name) ([string]$manifest.version) $license $download
            if ($dependencyId -ne $dshId) {
                $relationships.Add([ordered]@{ spdxElementId = $dshId; relationshipType = 'DEPENDS_ON'; relatedSpdxElement = $dependencyId })
            }
        } catch {
            Write-Warning "Skipping unreadable package manifest $($_.FullName): $($_.Exception.Message)"
        }
    }
}

$created = if ($env:SOURCE_DATE_EPOCH) {
    [DateTimeOffset]::FromUnixTimeSeconds([Int64]$env:SOURCE_DATE_EPOCH).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')
} else {
    [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
}
$document = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = "DSH-Desktop-Shell-$ShellVersion-win-x64"
    documentNamespace = "https://github.com/Audrey0976/dsh-desktop-shell/spdx/v$ShellVersion/win-x64"
    creationInfo = [ordered]@{
        created = $created
        creators = @('Tool: DSH Desktop Shell scripts/New-Sbom.ps1')
    }
    packages = $packages.ToArray()
    relationships = $relationships.ToArray()
}

$parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Path $parent -Force | Out-Null
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($document | ConvertTo-Json -Depth 12) + "`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Wrote SPDX SBOM with $($packages.Count) packages to $OutputPath"
