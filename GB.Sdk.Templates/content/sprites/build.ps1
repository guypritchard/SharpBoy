param([string]$RgbdsBin = $env:GB_RGBDS_BIN)
$ErrorActionPreference = 'Stop'
if (-not $RgbdsBin) { throw 'Supply -RgbdsBin or set GB_RGBDS_BIN to the RGBDS 0.9.0 binary directory.' }
Push-Location $PSScriptRoot
try {
    New-Item -ItemType Directory -Force out | Out-Null
    $romTitle = (Get-Content -Raw (Join-Path $PSScriptRoot 'rom-title.txt')).TrimEnd([char[]]"`r`n")
    if (-not $romTitle) { throw 'ROM title cannot be empty.' }
    & gbc --rgbds-bin $RgbdsBin --out out/TemplateProject.gb --title $romTitle `
        --background-tiles Assets/background.2bpp --background-map Assets/background.tilemap `
        --sprite-tiles Assets/sprite.2bpp Game.cs
    if ($LASTEXITCODE -ne 0) { throw "gbc failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }
