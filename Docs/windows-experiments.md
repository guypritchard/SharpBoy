# Windows experiments application

The regular player and experimental labs have separate executables and test projects.

```powershell
# ROM playback, inspection, audio and the 3D desktop presentation.
dotnet run --project GB.Emulator.Windows -c Release

# Separate experiment launcher, with no player or 3D assets.
dotnet run --project GB.Experiments.Windows -c Release

# Direct entry points retain the labs' existing options.
dotnet run --project GB.Experiments.Windows -c Release -- --dhcp
dotnet run --project GB.Experiments.Windows -c Release -- --cluster --workers 32
dotnet run --project GB.Experiments.Windows -c Release -- --life --columns 40 --rows 25 --generations 3 --tick-delay-ms 0
```

`GB.Emulator.Windows` references the shared core, hosting, Windows audio and WinForms presentation
libraries. It has no reference to `GB.Experiments` or the experiments UI. Its menus and command-line
options cover ROM playback; the 3D desktop ROM player stays with it.

`GB.Experiments.Windows` references the headless experiments and shared WinForms presentation
library. The launcher offers DHCP cartridge, Parallel Mandelbrot and Living world. It has no
reference to the regular Windows player. The lab windows and node inspector live under `Labs/`;
the DHCP form belongs to the experiments executable. Each lab still uses the same native core and
`EmulationHost`. The small `GB.Emulator.Presentation.WinForms` library owns the reusable
`IVideoOutput` LCD control, keeping both front ends independent without duplicating rendering.

Headless execution remains in `GB.Experiments`. Native SDK samples, ROM builds, TCP protocols and
worker-process ownership are unchanged. Experimental Windows tests live in
`GB.Experiments.Windows.Tests`; player and 3D tests remain in `GB.Emulator.Windows.Tests`.

```powershell
dotnet test GB.Emulator.Windows.Tests -c Release
dotnet test GB.Experiments.Windows.Tests -c Release

# Publish to separate fresh directories so files from an older mixed package cannot linger.
dotnet publish GB.Emulator.Windows -c Release -r win-x64 --self-contained true -o artifacts/windows-player/win-x64
dotnet publish GB.Experiments.Windows -c Release -r win-x64 --self-contained true -o artifacts/experiments-windows/win-x64
```

CI builds and tests both applications and publishes separate `windows` and `experiments-windows`
artifacts/ZIPs for x64 and ARM64. The player package excludes experimental firmware, worker binaries
and lab configuration. The experiments package contains its worker executable and
`cluster-settings.json`, and excludes the player's bundled ROM and 3D assets.

See [DHCP](dhcp-cartridge-experiment.md), [Mandelbrot](visual-gameboy-cluster.md) and
[Living world](network-life-experiment.md) for each lab's protocol and settings.
