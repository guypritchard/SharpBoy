# Independent ROM execution (milestone 8)

## Direct-OAM sprite validation (milestone 10 slice)

With pinned RGBDS 0.9.0 and PyBoy 2.6.0 configured as below, compile the
[sprite sample](../Samples/Sprites/README.md) and run:

```powershell
& $env:GB_PYBOY_PYTHON Tools/IndependentEmulator/validate.py --rom artifacts/ci/sprites.gb --sprites --frames 18
```

The bounded headless check verifies `$8040-$804F` tile 4, 40 OAM entries (sprite
0 raw Y=56, X moving once per frame, tile 4/attributes 0; other entries hidden),
LCDC `$93`, OBJ palettes `$E4`, multiple frame-to-frame X changes and `$C200=$A5`
after eight frames. PyBoy renders a completed frame with OBJ enabled and another
with OBJ disabled, then requires a black tile-4 pixel in the sprite box to differ
from the background-only frame. The configured C# test also checks LCD-on/off initialization,
OAM clear, ordered Set/Hide, and bank-1 restoration after MBC1 bank-2 asset copies.
Boot ROM remains bypassed. Neither emulator proves cycle-exact sprite rendering,
OAM DMA timing/bus blocking or physical hardware fidelity; DMA is not implemented.

The semantic fixture compiles the **same** `GB.Compiler.Tests/Fixtures/SemanticPure.cs` file as test-host C# and target C#. `SemanticGame.cs` writes 25 results at `$C200–$C218`, then the completion byte `$A5` at `$C219`. Its short-circuit method takes a runtime boolean parameter and is called with false and true: both `&&` and `||` exercise a skipped and an executed side-effect path, with explicit host assertions on results and the counter. Host calls supply expected bytes; explicit host assertions also pin byte/word and signed 32-bit overflow and truncation. The test dirties all three linked globals and the result buffer before SharpBoy starts, checks all 26 WRAM bytes and the post-execution values of the globals within **100,000 instructions**, and separately tests header entry, ROM-only fields, Nintendo logo (via `RomValidator`), and both recomputed RGBDS checksums. It checks linked symbols/layout too. The compiler conservatively avoids allocating spill storage at WRAM addresses appearing as literal values in reachable code; without this, the larger semantic program's spill slots overlapped its `$C200` result buffer.

Independent execution uses **PyBoy 2.6.0**, licensed **LGPL-3.0-only** (including its redistribution/source obligations if distributing PyBoy). The only direct Python requirement is `Tools/IndependentEmulator/requirements.txt` (`pyboy==2.6.0`); pip resolves PyBoy's dependencies. Use **Python 3.11** in an untracked venv (PyBoy also supports Python 3.9+). First obtain and hash-check the official **RGBDS 0.9.0** ZIP (MIT), set `$bin` and `$env:GB_RGBDS_BIN` as in the [clean-checkout Windows toolchain procedure](csharp-gameboy-toolchain.md). Example PowerShell from the repository root:

```powershell
py -3.11 -m venv .venv
& .\.venv\Scripts\python.exe -m pip install -r Tools\IndependentEmulator\requirements.txt
$env:GB_PYBOY_PYTHON = (Resolve-Path .\.venv\Scripts\python.exe).Path
& $env:GB_PYBOY_PYTHON -c 'import importlib.metadata as m; import pyboy; assert m.version("pyboy") == "2.6.0"'
dotnet build GB.Emulator.sln -c Release --no-restore
dotnet test GB.Compiler.Tests/GB.Compiler.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=compiler.trx' --results-directory artifacts/ci/test-results
& Tools/ci/Assert-TrxNoSkipped.ps1 artifacts/ci/test-results/compiler.trx
dotnet test GB.Emulator.Tests/GB.Emulator.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=emulator.trx' --results-directory artifacts/ci/test-results
& Tools/ci/Assert-TrxNoSkipped.ps1 artifacts/ci/test-results/emulator.trx
```

`GB_RGBDS_BIN` is the existing opt-in RGBDS executable directory; `GB_PYBOY_PYTHON` is the opt-in path to the Python executable containing the exact pinned PyBoy version. Without either, relevant ROM acceptance tests are inconclusive, not passing. CI installs both in `RUNNER_TEMP`, rejects RGBDS archives with the wrong SHA-256 **before extraction**, verifies executable versions and the imported PyBoy version, and checks both TRX result sets: nonempty, all results passed, counters total equals passed; skipped/inconclusive tests fail the job even if `dotnet test` succeeds. Local equivalent commands above check TRX the same way; a successful local run does not prove a hosted GitHub Actions run. The C# integration invokes Python with `ProcessStartInfo.ArgumentList`, no shell, captured output, a bounded wall-clock limit (60 seconds for semantic/scroll, 90 seconds for banked) and clean process-tree termination on timeout. The Python validator stops PyBoy in `finally` and fails on mismatch, early stop or exhausted frame budget.

**Asset-separation verification (2026-10-18):** display/sprite integration tests now
load the explicit binary fixtures from `Samples/Assets` into `GameAssetBundle`.
The Python oracle still checks the original sample visuals/bytes, not arbitrary
user artwork. Preservation hashes are tested independently of the compiler, which
has no sample art or hash constants. Additional real-RGBDS tests cover custom art,
exact ROM bytes/report hashes, sprite-only `$4000` placement, asset-free scroll/wait
and sprite Set/Hide, plus CLI file diagnostics and immutable inputs. Release build
passed with **0 warnings/errors**; emulator **159/159** and configured compiler
**136/136**, **zero skips**, both TRX gates passed. Historical run counts elsewhere
refer to earlier suites. No hosted CI or physical-hardware run is claimed.

For a manually built cartridge (temporary paths, never check ROMs into git):

```powershell
dotnet run --project GB.Compiler.Cli -c Release -- --rgbds-bin $bin --out "$env:TEMP\semantic.gb" --title SEMANTIC GB.Compiler.Tests/Fixtures/SemanticPure.cs GB.Compiler.Tests/Fixtures/SemanticGame.cs
& $env:GB_PYBOY_PYTHON Tools/IndependentEmulator/validate.py --rom "$env:TEMP\semantic.gb" --address C200 --expected 007801FF010001FF0080FF78FFFF010121310D0AFF81025634 --sentinel A5 --frames 10
dotnet run --project GB.Compiler.Cli -c Release -- --rgbds-bin $bin --out "$env:TEMP\scroll.gb" --title SCROLLING --background-tiles Samples/Assets/background.2bpp --background-map Samples/Assets/background.tilemap Samples/ScrollingBackground/ScrollingBackground.cs
& $env:GB_PYBOY_PYTHON Tools/IndependentEmulator/validate.py --rom "$env:TEMP\scroll.gb" --scroll --frames 10
```

The fixed hex above is a convenient CLI reproduction only: **MSTest calculates its oracle by calling the shared pure methods on the host**, rather than trusting this string. The validator expects a contiguous hex byte sequence followed immediately by the sentinel. `--scroll` instead checks at least three aligned SCX/SCY increments, `$91` LCDC, `$E4` BGP and representative tile/map bytes. Both checks are bounded to ten emulated frames. PyBoy is configured with `window="null"`, DMG mode, no input, sound emulation disabled, unlimited speed and `tick(..., render=False, sound=False)`. Its public `register_file.PC` and memory `$FF50` APIs start at cartridge `$0100` with boot ROM unmapped. This deliberately **skips the boot ROM**; neither authentic Nintendo boot validation, analog timing, peripherals, audio/video appearance, nor physical hardware fidelity is claimed. Header checks are independent static checks, not a claim that a real boot ROM accepted the cartridge.

Verified on 2026-10-17 with RGBDS 0.9.0 and PyBoy 2.6.0 on Windows x64: SharpBoy semantic completion **3,908/100,000 instructions**; PyBoy semantic bytes `$C200-$C219 = 007801FF010001FF0080FF78FFFF010121310D0AFF81025634A5` at **frame 1/10**; scrolling sample at **frame 4/10** (`SCX=SCY=03`, LCDC/BGP and tile/map assertions passed). Earlier milestone-8 regression snapshot: compiler MSTests **115/115**, emulator MSTests **150/150**, zero skipped. These are two emulator software paths, not an exhaustive instruction/timing certification.

### Asset-only MBC1 cross-check (2026-10-17)

With the same configured tools, build [BankedAssets](../Samples/BankedAssets/README.md) using `--mbc1-banks 4` and two `--banked-asset` options. Run the pinned headless validator directly:

```powershell
& $env:GB_PYBOY_PYTHON Tools/IndependentEmulator/validate.py --rom artifacts/ci/banked.gb --banked --frames 10
```

The configured compiler test builds **4, 8, 16, 32 banks** using real RGBDS 0.9.0, checks length (64/128/256/512 KiB), type `$01`, size codes `$01/$02/$03/$04`, RAM `$00`, bank-2 bytes and final-bank capacity guard or an asset ending at `$8000`, linked symbols and tamper rejection, then executes each ROM in SharpBoy to `$C110=$5A` within 20,000 instructions (**342 instructions for every size**). It checks `$D000-$D002=A1 A2 A3`, `$D004-$D007=A1 A2 A3 F0`, `$C100-$C102=A1 A1 F0`, bank state `$C000=01` and displayed bank-1 bytes at `$4000-$4001` after bank restoration. The four-bank image also runs in PyBoy 2.6.0 via `--banked --frames 10` (**frame 1/10**); no boot-ROM or physical-hardware acceptance is claimed. The sample is no-RAM MBC1 type `$01`, code/helper ROM0, bank 1 reserved for display data, explicitly hashed assets bank 2+, staging `$D000-$D7FF`, constant-only operations with interrupts disabled; no function banking, save RAM, >32 banks, VRAM/DMA transfers, reentrancy or managed arrays. The `.memory.txt` output reports per-bank usage/free space and asset SHA-256; [sample report](../Samples/BankedAssets/README.md) records exact values. Release build passed with zero warnings/errors; configured compiler suite **125/125**, emulator suite **150/150**, both zero skipped with passing TRX gates. CI's configured compiler suite automatically covers these runs; it does not need a separate banked smoke command.
