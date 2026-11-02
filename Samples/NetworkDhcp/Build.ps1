param([string]$RgbdsBin = $env:GB_RGBDS_BIN)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ([string]::IsNullOrWhiteSpace($RgbdsBin)) {
    $RgbdsBin = Join-Path $repo 'artifacts/toolchains/rgbds'
}
Push-Location $repo
try {
    dotnet run --project GB.Compiler.Cli -c Release -- --out artifacts/netcart/network-dhcp.gb --title 'DHCP NETCART' --rgbds-bin $RgbdsBin --text-font --wram-start 49408 Samples/NetworkDhcp/EntryPoint.cs Samples/NetworkDhcp/NetworkDhcp.cs GB.Sdk/Target/GameTextNumbers.cs
    if ($LASTEXITCODE -ne 0) { throw 'SDK DHCP firmware compilation failed.' }
    Copy-Item -LiteralPath artifacts/netcart/network-dhcp.gb -Destination GB.Roms/NETWORK-DHCP.gb
    Write-Output "Embedded firmware refreshed. Rebuild GB.Experiments/Windows to use it."
} finally { Pop-Location }
