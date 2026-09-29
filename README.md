# SharpBoy
[![.NET](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml/badge.svg?branch=master)](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml)

A basic Gameboy emulator.

## Architecture

`GB.Emulator.Core` owns the hardware: CPU execution, the memory bus, LCD timing,
VRAM/OAM state, the joypad, and the sound register device. `Gameboy` coordinates those
devices and exposes immutable video and sound snapshots. `GB.Emulator.Display`
turns a video snapshot into shade pixels and provides the Unicode terminal
renderer. `GB.Emulator` and `GB.Debugger` control the hardware and present it
through their own user interfaces. The core does not depend on drawing or
terminal APIs. Audio synthesis and a cycle-accurate pixel pipeline are still
future hardware work.

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

`GB.Emulator` redraws the current video state when the LCD reaches VBlank.
Press **R** to repaint the terminal, or **Ctrl+C** to stop. If the terminal is
resized, the next frame clears and redraws it. The debugger uses the same
display layer for its screen and tile views, and repaints its frame when the
view is resized.

## Input

Both the console and the debugger's Video tab use **arrow keys** for the D-pad,
**Z** for A, **X** for B, **Enter** for Start, and **Space** for Select. The
console holds each key briefly and extends the hold on key repeat; the debugger
uses key-down and key-up events and releases all buttons when it loses focus.
Other input sources can call `gameboy.Input.SetButtonState(button, pressed)`.
The joypad device implements the active-low `FF00` button matrix and requests
the joypad interrupt when a selected input line becomes low.

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
