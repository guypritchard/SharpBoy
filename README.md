# SharpBoy
[![.NET](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml/badge.svg?branch=master)](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml)

A basic Gameboy emulator.

The CPU executes all 245 documented unprefixed opcodes and all 256 `CB`
bit/rotate opcodes. The 11 undefined opcodes raise an error. Instruction cycle
counts drive LCD timing, and interrupt entry, delayed `EI`, `RETI`, and the HALT
bug are handled. The wider emulator is still in progress; STOP wake-up and
complete audio/video hardware are not implemented.

## Console video

`ConsoleVideoRenderer` converts a 160 × 144 frame of Game Boy shade indices
(0 = lightest, 3 = darkest) into ANSI true-color text. Each `▀` displays one
pixel in its foreground color and the pixel below it in its background color;
pixels with the same shade use `█`. A frame therefore occupies **160 columns
and 72 rows**. For interactive drawing, use a terminal at least **160 columns
by 73 rows** so the cursor has a spare row. The terminal also needs UTF-8,
ANSI true-color, and a font with block characters.

```csharp
byte[] frame = new byte[Video.Width * Video.Height];
// Fill frame with shade indices 0 through 3, in row-major order.
ConsoleVideoRenderer.Draw(frame);
```

The LCD and tile logic does not currently produce complete video frames, so
ROM execution does not yet call this renderer automatically.

## Debugger interaction map

Open `GB.Debugger`, load a ROM, and select the **Interaction map** tab. Press
**Step** or F10 to follow a single instruction through CPU,
memory, video address space, and sound registers. Colored arrows show the
addresses accessed by that instruction; the details pane lists them. **Step
Back** or Shift+F10 restores the previous diagram. **Trace** updates it while
running. Sound register access is visible, but audio synthesis is not yet
implemented.

The debugger's **Video** tab shows a 160 × 144 screen preview alongside a
scrollable atlas of all 384 VRAM tiles. The preview combines the current
background, window, and sprites using LCD control, scroll, and monochrome
palette registers. Writes to `FF46` copy sprite entries into OAM. The preview
refreshes after relevant writes while stepping or tracing;
Step Back and Reset restore the view. It is reconstructed from the current
memory snapshot, so it cannot show mid-frame effects until the emulator has a
full pixel pipeline.

> This is an early work in progress.  

## Implementation Reference Docs:

- I made this:  https://guypritchard.github.io/SharpBoy-Opcode-Manual/

- This is excellent:  https://meganesulli.com/generate-gb-opcodes/
- https://cturt.github.io/cinoop.html
- https://web.archive.org/web/20141105020940/http://problemkaputt.de/pandocs.htm#thecartridgeheader
- http://www.pastraiser.com/cpu/gameboy/gameboy_opcodes.html
- http://gameboy.mongenel.com/dmg/lesson1.html
- http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf
- http://www.codeslinger.co.uk/pages/projects/gameboy.html
