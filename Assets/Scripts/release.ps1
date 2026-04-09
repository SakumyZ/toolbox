param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Repository = "SakumyZ/toolbox"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent $root
Set-Location $root

$normalizedVersion = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
$zipPath = Join-Path $root "dist\ToolBox-$normalizedVersion-win-x64.zip"

if (-not (Test-Path $zipPath))
{
    throw "Release artifact not found: $zipPath"
}

git push origin main
git tag -f $normalizedVersion
git push origin $normalizedVersion --force

$existingRelease = gh release view $normalizedVersion --repo $Repository 2>$null
if ($LASTEXITCODE -eq 0)
{
    gh release upload $normalizedVersion $zipPath --repo $Repository --clobber
}
else
{
    gh release create $normalizedVersion $zipPath --repo $Repository --title $normalizedVersion --notes "ToolBox $normalizedVersion release"
}

Write-Host "Release completed: $normalizedVersion"