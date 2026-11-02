param([string]$RgbdsBin = $env:GB_RGBDS_BIN)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ([string]::IsNullOrWhiteSpace($RgbdsBin)) { $RgbdsBin = Join-Path $repo 'artifacts/toolchains/rgbds' }
Push-Location $repo
try {
    New-Item artifacts/netcart -ItemType Directory -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $repo 'artifacts/netcart/life-tiles.bin'), [byte[]]::new(3088))
    $map = [byte[]]::new(1024)
    for ($i=0; $i -lt $map.Length; $i++) { $map[$i] = 192 }
    for ($y=0; $y -lt 12; $y++) { for ($x=0; $x -lt 16; $x++) { $map[($y+3)*32+$x+2] = $y*16+$x } }
    [IO.File]::WriteAllBytes((Join-Path $repo 'artifacts/netcart/life-map.bin'), $map)
    dotnet run --project GB.Compiler.Cli -c Release -- --out artifacts/netcart/network-life.gb --title 'GB LIFE' --rgbds-bin $RgbdsBin --background-tiles artifacts/netcart/life-tiles.bin --background-map artifacts/netcart/life-map.bin --wram-start 51456 Samples/NetworkLife/NetworkLife.cs GB.Sdk/Target/GameNetwork.cs GB.Sdk/Target/GameNetworkStream.cs
    if ($LASTEXITCODE -ne 0) { throw 'Life firmware compilation failed.' }
    Copy-Item artifacts/netcart/network-life.gb GB.Roms/NETWORK-LIFE.gb
} finally { Pop-Location }
