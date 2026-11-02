# TemplateProject

Scrolling four-tile DMG background; the bundled 2bpp tiles and 32x32 tilemap
belong to this project. Install `GB.Compiler.Cli` as `gbc` and RGBDS 0.9.0.
`dotnet build` type-checks against `GB.Sdk` but does not create a ROM.

```powershell
./build.ps1 -RgbdsBin C:/path/to/rgbds-0.9.0
```

Or set `GB_RGBDS_BIN` and run `./build.ps1`. The script invokes `gbc` with
`--background-tiles Assets/background.2bpp --background-map Assets/background.tilemap`
and writes `out/TemplateProject.gb` plus assembly, map, symbols and memory report.
The SDK host methods are guard stubs; only `gbc` generates native ROM code.
