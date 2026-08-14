[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Tag)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$normalized = $Tag.Trim()
if ($normalized.StartsWith('v', [StringComparison]::OrdinalIgnoreCase)) { $normalized = $normalized.Substring(1) }
if ($normalized -notmatch '^\d+\.\d+\.\d+$') { throw "Release tag must be vMAJOR.MINOR.PATCH, got $Tag" }
$productInfo = [IO.File]::ReadAllText((Join-Path $repoRoot 'src\windows\DSH.Desktop.Shell\ProductInfo.cs'))
$match = [Regex]::Match($productInfo, 'internal const string Version = "([^"]+)"')
if (-not $match.Success) { throw 'Could not read ProductInfo.Version.' }
if ($match.Groups[1].Value -ne $normalized) {
    throw "Tag version $normalized does not match ProductInfo.Version $($match.Groups[1].Value)."
}
Write-Host "Release tag $Tag matches product version $normalized."
