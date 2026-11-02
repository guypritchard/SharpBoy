param([string]$RgbdsBin = $env:GB_RGBDS_BIN)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ([string]::IsNullOrWhiteSpace($RgbdsBin)) { $RgbdsBin = Join-Path $repo 'artifacts/toolchains/rgbds' }
Push-Location $repo
try {
    New-Item artifacts/netcart -ItemType Directory -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $repo 'artifacts/netcart/fractal-tiles.bin'), [byte[]]::new(3088))
    $map = [byte[]]::new(1024)
    for ($i=0; $i -lt $map.Length; $i++) { $map[$i] = 192 }
    for ($y=0; $y -lt 12; $y++) { for ($x=0; $x -lt 16; $x++) { $map[($y+3)*32+$x+2] = $y*16+$x } }
    [IO.File]::WriteAllBytes((Join-Path $repo 'artifacts/netcart/fractal-map.bin'), $map)
    dotnet run --project GB.Compiler.Cli -c Release -- --out artifacts/netcart/network-fractal.gb --title 'GB FRACTAL' --rgbds-bin $RgbdsBin --background-tiles artifacts/netcart/fractal-tiles.bin --background-map artifacts/netcart/fractal-map.bin --wram-start 51200 Samples/NetworkFractal/NetworkFractal.cs GB.Sdk/Target/GameNetwork.cs
    if ($LASTEXITCODE -ne 0) { throw 'Fractal firmware compilation failed.' }
    Copy-Item artifacts/netcart/network-fractal.gb GB.Roms/NETWORK-FRACTAL.gb
} finally { Pop-Location }
