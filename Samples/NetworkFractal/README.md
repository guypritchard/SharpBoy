# Native nine-node Mandelbrot worker

`NetworkFractal.cs` calculates one 32×24 tile using Q5 arithmetic on the SM83, draws it into native
DMG background tiles, and returns each row over the network cartridge. Nine independent instances
produce the shared 96×72 image. All artwork is computed; the build supplies only blank tiles/map.

```powershell
./Samples/NetworkFractal/Build.ps1
dotnet run --project GB.Emulator.Windows -c Release -- --cluster
```

See [dashboard, exports, arithmetic and protocol](../../Docs/visual-gameboy-cluster.md).
