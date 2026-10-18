# Sprites (direct OAM, milestone 10 extension)

Build from the repository root (RGBDS 0.9.0):

```powershell
dotnet run --project GB.Compiler.Cli -c Release -- --rgbds-bin $bin --out artifacts/ci/sprites.gb --background-tiles Samples/Assets/background.2bpp --background-map Samples/Assets/background.tilemap --sprite-tiles Samples/Assets/sprite.2bpp Samples/Sprites/Sprites.cs
```

Set `GB_RGBDS_BIN` to the RGBDS directory if it is not on PATH. Open the resulting
`sprites.gb` in the SharpBoy debugger. Watch VRAM `$8040-$804F`, OAM `$FE00-$FE9F`,
LCDC `$FF40`, OBP0/OBP1 `$FF48/$FF49`, and WRAM sentinel `$C200 = $A5` after eight
frames. Sprite 0 moves once per *next* VBlank; its raw OAM Y is 56 (visible Y=40),
and raw X starts at 32 (visible X=24). All other sprites are hidden. The background
is initialized first. `Set`/`Hide` themselves wait for VBlank if the LCD is on.
Interrupts are disabled. No OAM DMA, DMA timing, bus blocking or hardware fidelity
is implied; DMA is deferred until the bus/timing model supports it.

Artwork is explicitly sample-owned in [../Assets](../Assets/README.md), not generated
by the compiler. The original 16-byte diamond's SHA-256 remains
`ecad92431cabb0bb47965254ea8b0039b30794a032828c492d872ca89cc57d7d`.
This sample places background tiles at bank 1 `$4000`, map at `$4040`, and sprite
at `$4440`. A sprite-only program supplies only `--sprite-tiles`, placing its source
at `$4000`; its destination is still VRAM `$8040` (tile 4).
