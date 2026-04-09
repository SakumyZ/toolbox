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
$buildOutput = Join-Path $root "bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

dotnet build -c Release -p:Platform=x64

if (-not (Test-Path $buildOutput))
{
    throw "Release build output not found: $buildOutput"
}

if (-not (Test-Path (Join-Path $root "dist")))
{
    New-Item -ItemType Directory -Path (Join-Path $root "dist") | Out-Null
}

if (Test-Path $zipPath)
{
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $buildOutput "*") -DestinationPath $zipPath -Force

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