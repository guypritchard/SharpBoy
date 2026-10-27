# Native network worker

`NetworkWorker.cs` is constrained SDK C# compiled to a native ROM. Each independent SM83 receives
a range, performs a 16-bit sum, and returns the result with the original node/job identifiers.
There is no .NET execution of this source inside the emulator.

```powershell
./Samples/NetworkTelemetry/Build.ps1
dotnet run --project GB.Experiments -c Release -- --network-cartridge --cluster --tcp --workers 4
```

See [UDP mode, fragmented TCP, protocol and limitations](../../Docs/network-telemetry-and-cluster.md).
