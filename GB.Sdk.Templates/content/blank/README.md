# TemplateProject

Minimal 32 KiB DMG ROM with no display assets. Install `GB.Compiler.Cli` as the
`gbc` tool and RGBDS 0.9.0 separately. `dotnet build` validates C# against the
`GB.Sdk` reference contract; it does not create a ROM. Set `GB_RGBDS_BIN` to the
directory containing `rgbasm`, `rgblink` and `rgbfix`, or pass `-RgbdsBin`:

```powershell
./build.ps1 -RgbdsBin C:/path/to/rgbds-0.9.0
```

The ROM and assembly/map/symbol/memory report are in `out/`. Alternatively run
`gbc --rgbds-bin <dir> --out out/TemplateProject.gb --title ROMTITLE Game.cs`.
The SDK host methods are guard stubs, not a CLR implementation of hardware.
