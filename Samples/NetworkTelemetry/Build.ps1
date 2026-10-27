param([string]$RgbdsBin = $env:GB_RGBDS_BIN)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ([string]::IsNullOrWhiteSpace($RgbdsBin)) { $RgbdsBin = Join-Path $repo 'artifacts/toolchains/rgbds' }
Push-Location $repo
try {
    & ./Samples/NetworkDhcp/Build.ps1 -RgbdsBin $RgbdsBin
    dotnet run --project GB.Compiler.Cli -c Release -- --out artifacts/netcart/network-telemetry.gb --title 'GB TELEMETRY' --rgbds-bin $RgbdsBin --text-font --wram-start 49408 Samples/NetworkTelemetry/NetworkTelemetry.cs Samples/NetworkDhcp/NetworkDhcp.cs GB.Sdk/Target/GameTextNumbers.cs GB.Sdk/Target/GameNetwork.cs
    if ($LASTEXITCODE -ne 0) { throw 'Telemetry firmware compilation failed.' }
    Copy-Item artifacts/netcart/network-telemetry.gb GB.Roms/NETWORK-TELEMETRY.gb
    dotnet run --project GB.Compiler.Cli -c Release -- --out artifacts/netcart/network-worker.gb --title 'GB NET WORKER' --rgbds-bin $RgbdsBin --wram-start 49408 Samples/NetworkWorker/NetworkWorker.cs GB.Sdk/Target/GameNetwork.cs
    if ($LASTEXITCODE -ne 0) { throw 'Worker firmware compilation failed.' }
    Copy-Item artifacts/netcart/network-worker.gb GB.Roms/NETWORK-WORKER.gb
} finally { Pop-Location }
