# SharpBoy
[![.NET](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml/badge.svg?branch=master)](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml)

A basic Gameboy emulator.

## Windows builds and versioning

The [Windows build](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml)
runs on pushes and pull requests to `master`, on version tags, and on manual
dispatch. It builds the console emulator and WinForms debugger, runs the tests,
and uploads self-contained `win-x64` and `win-arm64` builds of each app as
separate artifacts. Download an artifact and run `GB.Emulator.Console.exe` or
`GB.Debugger.exe` from the folder for your processor architecture.

GitVersion uses GitHub Flow. The `v0.5.1` tag marks the initial version;
later versions are calculated from Git history. Tag a release commit with
`v<major>.<minor>.<patch>` to fix its release version. Commit messages can
request a larger increment with `+semver: minor` or `+semver: major`.
The calculated version is applied to the .NET assemblies and artifact names.

After a merge or push to `master`, a successful build and test run automatically
creates a GitHub release tagged `v<calculated version>` at the built commit.
The release notes contain that commit's full message (the merge commit message
for a merge, or the squash commit message for a squash merge). Each release
includes separate ZIP downloads for the console emulator and debugger on
`win-x64` and `win-arm64`. Pull requests, tag pushes, and manual builds upload
build artifacts without creating a release.

## Architecture

`GB.Emulator.Core` owns the hardware: CPU execution, the memory bus, LCD timing,
VRAM/OAM state, the joypad, and the APU. `Gameboy` coordinates those
devices and exposes immutable video and sound snapshots. Its `Rendering` folder
turns a video snapshot into shade pixels. `GB.Emulator.Console` owns the Unicode
terminal renderer. The console app and `GB.Debugger` control the hardware and present it
through their own user interfaces. The core does not depend on drawing or
terminal APIs. The APU generates stereo PCM; a cycle-accurate pixel pipeline is
still future hardware work. Both front ends compile the Windows audio adapter
from `Shared/WindowsAudioOutput.cs`; platform audio stays outside the core
without requiring another assembly.
The memory bus uses separate silent and recording access observers for continuous
play and debugger stepping. Both modes execute instructions through the same CPU path.

Within the core, `Gameboy` creates and resets the devices, wires their interrupts,
and coordinates save and restore. `RomDisassembler` builds debugger instruction
windows without executing code. The CPU handles interrupt entry, operand fetch,
instruction execution, and delayed interrupt enablement in separate methods;
its registers own their snapshot and restore logic. `MemoryMap` routes bus
accesses, selects cartridge devices, and copies OAM DMA through normal bus reads
and writes so debugger traces include the transfer. These boundaries keep
hardware behavior separate from debugger views. CPU registers and the CPU memory
reference are still static, so multiple emulators require process isolation.

The CPU executes all 245 documented unprefixed opcodes and all 256 `CB`
bit/rotate opcodes. The 11 undefined opcodes raise an error. Instruction cycle
counts drive LCD timing, and interrupt entry, delayed `EI`, `RETI`, and the HALT
bug are handled. The wider emulator is still in progress; STOP wake-up and
complete audio/video hardware accuracy is still in progress.

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

`GB.Emulator.Console` redraws the current video state when the LCD reaches VBlank.
When `GB.Roms.Private/Tetris (World).gb` is present, the console runs it by
default. The private ROM is copied into the local build output but excluded from
publish output. Without it, the bundled sprite ROM is used; a command-line ROM
path always takes precedence.
The bottom row shows rendered frames per second, averaged over the last second.
Press **R** to repaint the terminal, or **Ctrl+C** to stop. If the terminal is
resized, the next frame clears and redraws it. The debugger uses the same
core renderer for its screen and tile views, and repaints its frame when the
view is resized.

## Input

Both the console and every debugger tab use **arrow keys** for the D-pad,
**Z** for A, **X** for B, **Enter** for Start, and **Space** for Select. The
console uses the physical key state on Windows, so buttons stay pressed until
release; quick taps last at least 30 ms. On other terminals, key repeat extends
a short timed hold. The debugger uses key-down and key-up events and releases
all buttons when it loses focus.
Other input sources can call `gameboy.Input.SetButtonState(button, pressed)`.
The joypad device implements the active-low `FF00` button matrix and requests
the joypad interrupt when a selected input line becomes low.

## Link port web experiment

`GB.Experiments` runs a small ROM in the emulator and exposes its serial reply
as a local HTTP page. Start it with:

```powershell
dotnet run --project GB.Experiments -- --port 8765
curl http://127.0.0.1:8765/
```

Use `--workers 8` to run eight independent Game Boy emulators. The default is
four workers (or fewer when fewer processors are available). The listener
accepts requests concurrently and assigns them round robin. Each worker runs
in its own process because the current CPU registers and memory bus are static;
that process boundary keeps their hardware state separate. One worker handles
one request at a time. Run `dotnet run --project GB.Experiments -- --benchmark`
to measure worker counts on your computer.

The listener binds to `127.0.0.1`. `GET /` sends the byte `G` to the ROM using
an externally clocked transfer at `FF01` (SB) and `FF02` (SC). The ROM responds
with ASCII bytes followed by a zero byte; the adapter clocks each byte and wraps
the result in HTTP. The built-in ROM serves `Hello from the Game Boy serial
port!`. Supply `--rom path/to/program.gb` to try another ROM implementing the
same protocol.

The same server exposes `GET /api/primes/{n}`. For example,
`curl http://127.0.0.1:8765/api/primes/10` returns
`{"n":10,"prime":29}` as JSON. The adapter sends `P`, a 16-bit little-endian
index, and a 16-bit search ceiling. For each request, the emulated ROM marks
odd composites in work RAM, counts primes, and sends the result as two
little-endian bytes. No prime results are stored in the cartridge. The ceiling
is at most `min(65535, 16 * n)`, so small requests do less work. The supported
range is **1–6542**, ending at 65521, the largest prime that fits in 16 bits.
Invalid indices return HTTP 400. A custom ROM must implement the `P` command
and this request and response format to support the endpoint.

The core also exposes `gameboy.Serial` and `SerialLinkCable` for byte exchange
between an internally clocked serial port and an externally clocked one. Serial completion
clears SC bit 7 and requests interrupt bit 3. The current byte exchange is a
functional model; individual bits are not yet shifted during the transfer.

## Debugger interaction map

Open `GB.Debugger`, load a ROM, and select the **Interaction map** tab. Press
**Step** or F10 to follow a single instruction through the CPU and mapped
hardware on an exploded Game Boy poster. The cartridge lifts above the console,
video components sit in the screen, input sits with the controls, and the APU
sits with the exploded speaker. **Fit poster** shows the whole assembly;
**100% / read labels** provides a scrollable view at full size.
The diagram shows cartridge ROM and RAM, work
RAM banks, video RAM, sprite OAM, LCD/PPU, APU, joypad, serial link, timer/I/O,
interrupt registers, high RAM, and OAM DMA. Cyan traces mark reads and fetches,
amber marks writes, and violet marks both. **Trace** accumulates activity from
every instruction in a batch; brighter traces indicate more accesses. The
transparent glow fades within about a second after activity stops.
Scroll the board to inspect the lower components; the separate bus log lists
the latest instruction's exact addresses. **Step Back** or Shift+F10 restores
the previous instruction and its diagram.
The **Address map** tab lays out every address from `0000` through `FFFF` as
coloured hardware blocks. Cyan and amber bars show where reads and writes land
inside each range; the cream marker shows the latest instruction fetch. Activity
accumulates during Trace and fades after it stops. Hover over a block for the
exact addresses touched by the latest instruction. Cartridge blocks show the
selected ROM and RAM banks when the cartridge has a mapper.
The **Cartridge** tab reads the ROM header and shows its mapper, declared ROM and
RAM sizes, display modes, active banks, and hardware requirements. Its support
status distinguishes implemented features from missing ones; Pokémon Red is
marked partial because battery saves and Super Game Boy enhancements are not
yet implemented.
Sound register access lights the APU on the map. The DMG APU generates two pulse
channels, programmable wave audio, and noise, with stereo routing and volume.
On Windows the console and debugger play the resulting PCM through the system
audio device; the console paces emulation to the Game Boy clock when it runs ahead.

The debugger's **Video** tab shows a 160 × 144 screen preview alongside a
scrollable atlas of all 384 VRAM tiles. The preview combines the current
background, window, and sprites using LCD control, scroll, and monochrome
palette registers. Writes to `FF46` copy sprite entries into OAM. The preview
refreshes after relevant writes while stepping or tracing;
Step Back and Reset restore the view. It is reconstructed from the current
memory snapshot, so it cannot show mid-frame effects until the emulator has a
full pixel pipeline.

MBC3 cartridges, including Pokémon Red, can switch ROM and cartridge RAM banks.
The CPU fetches banked instructions through the memory bus, so Pokémon Red can
reach its title screen and introduction. Battery RAM currently lasts only for
the running emulator session; persistent save files are not yet implemented.

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
