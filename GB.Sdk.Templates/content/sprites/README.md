# TemplateProject

Moving sprite over a scrolling-background asset set. All art is included here.
Install `GB.Compiler.Cli` as `gbc` and RGBDS 0.9.0. `dotnet build` type-checks
against `GB.Sdk` but does not produce a ROM.

```powershell
./build.ps1 -RgbdsBin C:/path/to/rgbds-0.9.0
```

Or set `GB_RGBDS_BIN` and run `./build.ps1`. The script passes
`--background-tiles Assets/background.2bpp --background-map Assets/background.tilemap`
and `--sprite-tiles Assets/sprite.2bpp` to `gbc`. Outputs are in `out/`.
The SDK host methods are guard stubs; only `gbc` generates native ROM code.
