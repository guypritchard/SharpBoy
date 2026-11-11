param([string]$RgbdsBin = $env:GB_RGBDS_BIN)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ([string]::IsNullOrWhiteSpace($RgbdsBin)) { $RgbdsBin = Join-Path $repo 'artifacts/toolchains/rgbds' }
Push-Location $repo
try {
    dotnet run --project GB.Compiler.Cli -c Release -- --out artifacts/link-lobby/link-lounge.gb --title 'LINK LOUNGE' --rgbds-bin $RgbdsBin --text-font Samples/LinkLounge/LinkLounge.cs GB.Sdk/Target/GameTextNumbers.cs
    if ($LASTEXITCODE -ne 0) { throw 'Native link demo compilation failed.' }
    Copy-Item -LiteralPath artifacts/link-lobby/link-lounge.gb -Destination GB.Roms/LINK-LOUNGE.gb
} finally { Pop-Location }
