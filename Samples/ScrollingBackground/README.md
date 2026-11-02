# Scrolling background (DMG)

From the repository root with .NET 10 and the verified RGBDS **0.9.0** (`$bin` set by the [clean-checkout Windows installation and SHA-256 procedure](../../Docs/csharp-gameboy-toolchain.md)):

```powershell
dotnet run --project GB.Compiler.Cli -c Release -- --rgbds-bin $bin --out artifacts/ci/scrolling.gb --title SCROLLING --background-tiles Samples/Assets/background.2bpp --background-map Samples/Assets/background.tilemap Samples/ScrollingBackground/ScrollingBackground.cs
foreach ($extension in '.gb', '.asm', '.map', '.sym', '.memory.txt') {
    if (-not (Test-Path "artifacts/ci/scrolling$extension" -PathType Leaf)) { throw "Missing $extension" }
}
if ((Get-Item artifacts/ci/scrolling.gb).Length -ne 32768) { throw 'ROM size mismatch' }
dotnet run --project GB.Debugger -c Release
```

Click **Load ROM…** and open `artifacts/ci/scrolling.gb` in GB.Debugger. The ROM starts with interrupts disabled, waits for a safe LCD disable point if needed, writes known VRAM/palette/scroll state while LCD is off, enables the background, then polls for the **next** VBlank before each scroll update. Output `.asm` (code), `.map` (section/address layout), `.sym` (labels) and `.memory.txt` (ROM/WRAM/stack plan) are published next to `.gb`. `.sym`/`.map` provide manual source-to-label metadata; GB.Debugger does not yet consume them for source-level debugging. `artifacts/` is gitignored, and no generated ROM or tool binaries belong in source control.

The sample owns four inspectable 8×8 2bpp tiles in [`../Assets/background.2bpp`](../Assets/background.2bpp): tile 0 blank, tile 1 horizontal bars, tile 2 checker, tile 3 diagonal. Each tile occupies 16 bytes, two bytes per row (low bitplane then high bitplane), with bit 7 the leftmost pixel. The sample's [`../Assets/background.tilemap`](../Assets/background.tilemap) is a 32×32 map (1024 bytes, row-major) selecting `1 + ((x / 4 + y / 4) % 3)`; it wraps over the 256×256-pixel background. Both explicit inputs are emitted unchanged as RGBDS `db` in ROM bank 1, at `$4000` and `$4040` respectively. SHA-256 of tiles followed by map remains `50682b8a4bbdcb4a6cd262755ac5626cee38ca8f5347c88025031982eeae0ab4`, checked by fixture tests; the build report hashes each input separately. The compiler contains no artwork or fallback. Native code copies to `$8000` and `$9800` while LCD is off. `LCDC=$91` enables LCD, BG, unsigned `$8000` tile data and `$9800` map; `BGP=$E4` maps palette indices 0–3 monotonically. SharpBoy can inspect register and VRAM state; its timing/rendering behavior is not independent DMG hardware proof (milestone 8).
