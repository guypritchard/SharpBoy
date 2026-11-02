# SDK-built lease telemetry

`NetworkTelemetry.cs` calls the shared DHCP guest, then constructs a `GBT1` record from its acquired
values and emits it through the selected cartridge transport. Both the DHCP and telemetry execute
as native SM83 instructions. `Build.ps1` compiles this program and the worker sample, reusing the
DHCP source and `GB.Sdk/Target/GameNetwork.cs`.

```powershell
./Samples/NetworkTelemetry/Build.ps1
dotnet run --project GB.Experiments -c Release -- --network-cartridge --telemetry --udp
dotnet run --project GB.Experiments -c Release -- --network-cartridge --telemetry --tcp
```

See [receiver commands, packet layouts and cluster experiments](../../Docs/network-telemetry-and-cluster.md).
