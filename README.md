# SharpBoy

[![Windows build](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml/badge.svg?branch=master)](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml)

A Game Boy emulator written in C#, with a playable terminal front end and a Windows debugger that shows what happens inside the machine.

Play through a Unicode console display, or follow instructions across an exploded Game Boy: cartridge banks, CPU, memory, display, sound and input. SharpBoy is a work in progress, with an emphasis on making the hardware visible and understandable.

[Download Windows builds](https://github.com/guypritchard/SharpBoy/releases/latest) · [Run from source](#run-from-source) · [Explore the debugger](#explore-the-debugger) · [Hardware support](#hardware-support) · [Architecture](#architecture)

## See it running

### The console emulator

![SharpBoy console running the bundled sprite demo with a small terminal font](Docs/screenshots/console.png)

*The bundled `GB.Roms/SPRITE.GB` demo running in Windows Terminal with a 6-point Cascadia Mono font. The capture includes the complete Game Boy frame and the FPS/control line; unused terminal space is cropped.*

The display is real terminal text: two Game Boy pixels per character, using foreground and background colours. It needs **160 columns × 73 rows**, so a small font is essential.

### The debugger

![SharpBoy debugger loaded with the bundled sprite demo, showing registers, disassembly and the Video tab](Docs/screenshots/debugger-interaction.jpg)

*The debugger loaded with `GB.Roms/SPRITE.GB`: registers on the left, disassembly in the centre, and the Video tab on the right. The hardware visualisation is shown in detail below.*

![Readable detail of the Game Boy hardware atlas with the bundled ROM-only sprite demo](Docs/screenshots/debugger-hardware.png)

*Hardware atlas detail, rendered by the debugger's diagram control from the executing `GB.Roms/SPRITE.GB` demo. This ROM-only cartridge shows fixed wiring; the controller and capability panel change with the loaded ROM.*

All screenshots use demo ROMs from [GB.Roms](GB.Roms). ROMs from `GB.Roms.Private` are excluded from documentation captures.

## Get started on Windows

1. Download the console or debugger ZIP from the [latest release](https://github.com/guypritchard/SharpBoy/releases/latest): `win-x64` for Intel/AMD PCs, or `win-arm64` for Windows on ARM.
2. Extract the whole ZIP. These builds include the .NET runtime.
3. For the debugger, launch `GB.Debugger.exe`, click **Load ROM…**, and choose a `.gb` file.
4. For console play, configure your terminal as described below, then run the executable with a ROM path:

```powershell
.\GB.Emulator.Console.exe "C:\Games\your-game.gb"
```

Without a path, the console uses `Tetris (World).gb` if present alongside the executable, otherwise the bundled `SPRITE.GB` demo. Private ROMs are excluded from published builds.

### Make the console fit

A Game Boy screen is 160 × 144 pixels. SharpBoy packs vertically adjacent pixels into `▀`/`█` characters, producing a **160-column, 72-row picture**, plus one status row.

Use a terminal with UTF-8, ANSI true colour and a monospaced font with block characters. In Windows Terminal:

- Set a small font for the emulator profile. **6 points** worked for the capture above; your display scaling may need a different size.
- Make the window at least **160 columns wide and 73 rows high** before launching. A wide window alone is not enough: all 73 rows must fit vertically.
- Use a font such as Cascadia Mono and reduce terminal padding if space is tight.
- Press **R** to repaint after changing the window size. A subsequent frame also clears and redraws after resizing.

If you see `Console video requires at least 160 columns and 73 rows`, reduce the font size or enlarge the terminal, then launch again. Redirected output is unsupported because the renderer needs an interactive terminal.

### Controls

| Game Boy button | Keyboard |
| --- | --- |
| D-pad | Arrow keys |
| A | **Z** |
| B | **X** |
| Start | **Enter** |
| Select | **Space** |
| Repaint console | **R** |
| Stop console | **Ctrl+C** |

The debugger accepts game buttons from every tab. On Windows, console input tracks physical key state; quick taps last at least 30 ms. Other terminal platforms use short holds extended by key repeat. The debugger releases held buttons when it loses focus.

## Explore the debugger

Load a ROM, choose a tab, then use **Step**, **Trace**, **Stop**, **Step Back** and **Reset** to follow execution. **F10** steps one instruction; **Shift+F10** steps back.

| View | What it shows |
| --- | --- |
| **Video** | Current 160 × 144 screen, the 384-tile VRAM atlas, scanline and VBlank status. |
| **Interaction map** | An exploded Game Boy with live reads, writes, instruction fetches and a bus log. |
| **Address map** | The complete `0000–FFFF` address space, access activity and active cartridge banks. |
| **Cartridge** | ROM header, mapper, declared sizes, display modes, active banks and support limitations. |
| **Memory** | Memory inspection alongside disassembly, registers, interrupts and stack state. |

### Follow the hardware

In **Interaction map**, choose **Fit poster** to see the whole machine, or **100% / read labels** for a scrollable view at full size.

- **Cyan:** reads and instruction fetches.
- **Amber:** writes.
- **Violet:** both reads and writes.

Trace accumulates activity across instructions: more accesses produce brighter light, which fades when activity stops. The bus log lists the latest instruction's addresses; the glow shows recent activity rather than a permanent history.

ROM reads light the cartridge ROM. Writes to mapper-control addresses light the bank controller instead. Cartridge appearance reflects its hardware:

| Cartridge | Visualisation |
| --- | --- |
| ROM only | Fixed wiring with no bank-controller chip. |
| MBC1 | Violet controller showing simple or advanced banking mode. |
| MBC3 | Cyan controller with independent ROM/RAM selection. |
| Unimplemented mapper | Dashed controller outline with an unavailable label. |

The capability panel shows both ROM windows, selected RAM bank, RAM enable state and optional hardware limitations. **Step Back** restores mapper registers and every cartridge RAM bank as part of the emulator snapshot. **Reset** reloads the cartridge and resets its state.

The Video tab reconstructs the screen from current VRAM, OAM and LCD registers, combining background, window and sprites. It refreshes while stepping/tracing and after relevant writes, including OAM DMA at `FF46`. Mid-frame effects require a more accurate pixel pipeline.

## Hardware support

| Area | Current implementation |
| --- | --- |
| CPU | All 245 documented unprefixed and 256 `CB` opcodes; undefined opcodes raise an error. Interrupt entry, delayed `EI`, `RETI` and the HALT bug are handled. |
| ROM only | Fixed cartridge ROM. |
| MBC1 | Types `01–03`: ROM banking, RAM enable/disable, both banking modes and banked RAM. Standard wiring supports up to 2 MiB ROM with 8 KiB RAM, or 512 KiB ROM with 32 KiB RAM. |
| MBC3 | Types `11–13`: ROM and cartridge RAM banking. RTC hardware is not implemented. |
| Display | DMG monochrome screen reconstruction, LCD timing, background/window/sprites and OAM DMA. Full pixel-pipeline accuracy remains in progress. |
| Sound | Two pulse channels, wave audio and noise, with stereo routing/volume. Windows front ends play PCM through the shared audio adapter. |
| Input/timer | Joypad button matrix and interrupts; timer hardware. |
| Serial | Functional byte transfers, completion interrupt and link-cable abstraction; individual bits are not yet shifted during transfers. |
| Other cartridge hardware | MBC1M multicarts, other mappers, RTC, rumble and motion sensors are unavailable. |
| Enhanced systems | Game Boy Color mode and Super Game Boy enhancements are unavailable. DMG-compatible CGB cartridges can use their DMG mode. |

Mapper support does not guarantee that every game runs correctly. The Cartridge tab reports requirements and limitations for the ROM you load; battery-backed or enhanced-system cartridges may be marked partial even when banking works.

### Battery saves and Step Back

**There are currently no persistent battery save files.** Cartridge RAM lives in memory for the current emulator session. SharpBoy does not read or write `.sav` files and has no save-directory setting.

Closing the app, loading another cartridge or resetting loses that RAM. Debugger Step Back snapshots preserve cartridge RAM while the session is running; they are not disk saves. Persistent battery storage is still to be implemented.

## Run from source

Use the **.NET 10 SDK**. The WinForms debugger requires Windows. Windows audio is provided by a separate platform library; the console can continue silently on other platforms with a suitable terminal.

```powershell
git clone https://github.com/guypritchard/SharpBoy.git
cd SharpBoy
dotnet restore GB.Emulator.sln
```

Start the console in a terminal large enough for its display:

```powershell
dotnet run --project GB.Emulator.Console -c Release -- "C:\Games\your-game.gb"
```

Start the debugger:

```powershell
dotnet run --project GB.Debugger -c Release
```

Use the bundled demo explicitly:

```powershell
dotnet run --project GB.Emulator.Console -c Release -- "GB.Roms\SPRITE.GB"
```

When `GB.Roms.Private/Tetris (World).gb` exists locally, the console project copies it into the build output and uses it by default. It is excluded from publish output.

Build and test:

```powershell
dotnet build GB.Emulator.Console/GB.Emulator.Console.csproj -c Release
dotnet build GB.Debugger/GB.Debugger.csproj -c Release
dotnet test GB.Emulator.Tests/GB.Emulator.Tests.csproj -c Release
```

## Architecture

![SharpBoy logical architecture](Docs/architecture.svg)

[Open the architecture diagram](Docs/architecture.svg) · [Source-backed architecture model](Docs/architecture.json)

| Project | Responsibility |
| --- | --- |
| `GB.Emulator.Core` | CPU and hardware devices, memory routing, cartridge mappers, snapshots and platform-independent DMG rendering. |
| `GB.Emulator.Console` | Unicode terminal display, keyboard input and paced playback. |
| `GB.Debugger` | WinForms stepping, disassembly, state inspection, hardware atlas and screen/tile views. |
| `GB.Emulator.Windows` | Shared Windows audio output adapter. |
| `GB.Experiments` | HTTP-to-emulated-serial experiments and isolated emulator workers. |
| `GB.Emulator.Tests` | Hardware, execution, mapper, rendering, input and experiment regression tests. |

`Gameboy` composes and resets the hardware. `MemoryMap` routes reads/writes and DMA; the cartridge factory selects a mapper from the header. The shared `CartridgeMapper` owns ROM/RAM storage and mapped windows, while MBC1/MBC3 own their banking rules and snapshots. Silent and recording memory observers allow continuous play and debugger tracing through the same CPU execution path.

The core does not depend on terminal or drawing APIs. `DmgScreenRenderer` converts video snapshots into shade pixels; each front end presents them. CPU registers and the CPU memory reference are currently static, so independent emulator instances require separate processes.

## Link-port web experiment

Run a small ROM as a local HTTP service through the emulated serial port:

```powershell
dotnet run --project GB.Experiments -- --port 8765
curl.exe http://127.0.0.1:8765/
curl.exe http://127.0.0.1:8765/api/primes/10
```

The built-in ROM replies `Hello from the Game Boy serial port!`; the prime endpoint returns `{"n":10,"prime":29}`. The prime search executes in the ROM using work RAM. Indices `1–6542` are supported, ending at 65521; invalid indices return HTTP 400.

The listener binds to `127.0.0.1`. By default it uses up to four isolated emulator worker processes and distributes requests round robin. Use `--workers 8` for eight workers, `--rom path/to/program.gb` for a ROM implementing the serial protocol, or `--benchmark` to measure worker counts.

The protocol sends `G` for the greeting, or `P` followed by a 16-bit little-endian index and search ceiling for primes. Replies are zero-terminated ASCII or a two-byte little-endian prime respectively. The search ceiling is at most `min(65535, 16 * n)`.

## Builds and releases

[GitHub Actions](https://github.com/guypritchard/SharpBoy/actions/workflows/dotnet.yml) builds both front ends, runs tests and publishes self-contained `win-x64`/`win-arm64` artifacts. Successful builds on `master` also create a GitHub release with separate console/debugger ZIPs. Pull requests, tag pushes and manual builds upload artifacts without creating a release.

GitVersion calculates assembly and artifact versions from Git history using GitHub Flow. Tags of the form `v<major>.<minor>.<patch>` set a release version; commit messages can request a larger increment with `+semver: minor` or `+semver: major`. Automatic release notes use the built commit's full message.

## Implementation references

- [SharpBoy opcode manual](https://guypritchard.github.io/SharpBoy-Opcode-Manual/)
- [Pan Docs](https://gbdev.io/pandocs/)
- [Game Boy opcode tables](https://www.pastraiser.com/cpu/gameboy/gameboy_opcodes.html)
- [Game Boy CPU manual](http://marc.rawer.de/Gameboy/Docs/GBCPUman.pdf)
- [Opcode-generation notes](https://meganesulli.com/generate-gb-opcodes/)
- [Cinoop emulator walkthrough](https://cturt.github.io/cinoop.html)
