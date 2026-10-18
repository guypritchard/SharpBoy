# C#-to-Game Boy toolchain (DMG, ROM-only)

## SDK packages and standalone templates

Three distinct NuGet packages share the `0.1.0-alpha.1` prerelease version:
`GB.Sdk` is the reference contract for normal C# project restore/type-checking
and editor IntelliSense; `GB.Compiler.Cli` is the `gbc` .NET tool containing the
compiler, SDK reference and Roslyn runtime dependencies; `GB.Sdk.Templates` is
a content-only template pack. The template projects contain their own binary
assets; they are **not** compiled into `GB.Sdk.dll` or the compiler. Repository
`Samples/` remain integration fixtures. MBC1 is supported through the manual
CLI (see below), but no incomplete MBC1 starter is distributed.

With .NET 10 and RGBDS 0.9.0 installed (see below), create a local feed from a
checkout and install without publishing anything:

```powershell
$feed = (New-Item -ItemType Directory -Force artifacts/sdk-feed).FullName
dotnet pack GB.Sdk/GB.Sdk.csproj -c Release -o $feed
dotnet pack GB.Compiler.Cli/GB.Compiler.Cli.csproj -c Release -o $feed
dotnet pack GB.Sdk.Templates/GB.Sdk.Templates.csproj -c Release -o $feed
dotnet tool install GB.Compiler.Cli --version 0.1.0-alpha.1 --tool-path ./artifacts/sdk-tools --add-source $feed
dotnet new install "$feed/GB.Sdk.Templates.0.1.0-alpha.1.nupkg"
$env:PATH = "$(Resolve-Path ./artifacts/sdk-tools)$([IO.Path]::PathSeparator)$env:PATH"
dotnet new gb-dmg -n MyGame
dotnet new gb-dmg-background -n MyBackground
dotnet new gb-dmg-sprites -n MySprites
dotnet restore MyGame/MyGame.csproj --source $feed
dotnet build MyGame/MyGame.csproj --no-restore
./MyGame/build.ps1 -RgbdsBin $env:GB_RGBDS_BIN
# Remove the template installation when finished: dotnet new uninstall GB.Sdk.Templates
```

For published packages, replace the local feed with the package source; normal
`dotnet restore` then resolves the SDK. Each starter has a `net10.0` project
referencing `GB.Sdk`, source accepted by `gbc`, a README and `build.ps1`. The
`gbc` tool must be on PATH for the build script. Use `--sdkVersion` with
`dotnet new` if installing a coordinated non-default package version.
`dotnet build` type-checks the
host C# but does **not** build a ROM; `./build.ps1 -RgbdsBin <dir>` invokes the
installed tool with explicit assets (none for blank) and produces `out/*.gb`,
assembly, map, symbols and memory report. Pass `--romTitle` when creating a
project to set the 1-16 character ROM title. SDK intrinsic host bodies throw
guard exceptions and are never emitted as ROM code; only `gbc` implements
their exact-symbol native translation, not an alternate CLR implementation.
The original `dotnet run --project GB.Compiler.Cli -- --out ... source.cs`
explicit-source workflow remains supported.

To test all three packages as *outside-repository* consumers (isolated feed,
tool path, template hive and generated projects), run
`./Tools/verify-sdk-packages.ps1 -RgbdsBin <RGBDS-0.9.0-dir>`; it accepts
`-PackageVersion` for coordinated pack/version overrides and cleans its own
temporary files and installs on exit. No packages are published externally.

The [sprite demo](../Samples/Sprites/README.md) compiles with the same pinned
RGBDS 0.9.0 CLI. `GBR007` also validates anchored ROM0 sprite helper symbols and
bank-1 sprite tile symbols (`$4440` with background, `$4000` without); `GBR008` validates its 16 caller-supplied bytes.
`GBL010` rejects nonconstant/out-of-range indices and nonconstant/unsupported
attribute bits. `GameSprites` is an exact-SDK intrinsic; ROM0 helpers use polling
and direct OAM writes, with no OAM DMA/timing or bus-blocking claims.

Use .NET SDK 10, Roslyn `Microsoft.CodeAnalysis.CSharp` 4.14.0 (MIT) and **RGBDS 0.9.0** (`rgbasm`, `rgblink`, `rgbfix`; MIT). On a clean Windows x64 checkout, run this PowerShell from the repository root. The official release ZIP is verified against the independently obtained SHA-256 **`6EEE196CAD54DB55D392012684D11C3FE08DDCEE34284D21B792DD6644F3B51A` before extraction**. Never commit binaries or modify the system PATH:

```powershell
$zip = Join-Path $env:TEMP 'rgbds-0.9.0-win64.zip'
$root = Join-Path $env:TEMP 'sharpboy-rgbds-0.9.0'
Invoke-WebRequest 'https://github.com/gbdev/rgbds/releases/download/v0.9.0/rgbds-0.9.0-win64.zip' -OutFile $zip
if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne '6EEE196CAD54DB55D392012684D11C3FE08DDCEE34284D21B792DD6644F3B51A') { throw 'RGBDS SHA-256 mismatch; do not extract' }
Expand-Archive $zip -DestinationPath $root -Force
$bins = @(Get-ChildItem $root -Recurse -File -Filter rgbasm.exe | Where-Object {
    (Test-Path (Join-Path $_.DirectoryName 'rgblink.exe') -PathType Leaf) -and
    (Test-Path (Join-Path $_.DirectoryName 'rgbfix.exe') -PathType Leaf)
} | ForEach-Object DirectoryName)
if ($bins.Count -ne 1) { throw "Expected one complete RGBDS directory; found $($bins.Count)" }
$bin = $bins[0]
foreach ($tool in 'rgbasm', 'rgblink', 'rgbfix') {
    $version = & (Join-Path $bin "$tool.exe") --version
    if ($LASTEXITCODE -ne 0 -or $version -cne "$tool v0.9.0") { throw "Unexpected $tool version: $version" }
}
$env:GB_RGBDS_BIN = $bin
dotnet restore GB.Emulator.sln
dotnet build GB.Emulator.sln -c Release --no-restore
dotnet run --project GB.Compiler.Cli -c Release --no-build -- --rgbds-bin $bin --check-rgbds
dotnet run --project GB.Compiler.Cli -c Release --no-build -- --rgbds-bin $bin --out artifacts/ci/scrolling.gb --title SCROLLING --background-tiles Samples/Assets/background.2bpp --background-map Samples/Assets/background.tilemap Samples/ScrollingBackground/ScrollingBackground.cs
foreach ($extension in '.gb', '.asm', '.map', '.sym', '.memory.txt') {
    if (-not (Test-Path "artifacts/ci/scrolling$extension" -PathType Leaf)) { throw "Missing output $extension" }
}
if ((Get-Item artifacts/ci/scrolling.gb).Length -ne 32768) { throw 'Expected 32768-byte ROM' }
dotnet run --project GB.Debugger -c Release
```

In GB.Debugger click **Load ROM…** and select `artifacts/ci/scrolling.gb`. The debugger can step/inspect the running ROM; it does **not** integrate `.sym`/`.map` with source-level debugging. The `.sym` labels and `.map` section/address layout remain available alongside the generated assembly for manual source-to-label inspection. The memory report (`.memory.txt`) records planned ROM/WRAM/stack usage; inspect it before expanding the game. The local `$bin` is not a repository dependency. Pass `--rgbds-bin <dir>` or put the tools on PATH. Paths and titles are passed as separate arguments (no shell command execution); the title must be 1–16 printable ASCII characters. Supply distinct explicit `.cs` files, not an MSBuild project. `--out` must name a `.gb` file; `--keep-intermediates` preserves an isolated `.build-*` directory next to the ROM for troubleshooting (normally removed). The library equivalent is `new RomCompiler().CompileAsync(sourcePaths, new RomBuildOptions(outputPath, title, rgbdsBinDirectory, keepIntermediates))`; inject `IToolRunner` to test the process boundary or `IRomArtifactFileSystem` to test publication/cleanup. A cancellation token can abort the build.

On success the CLI prints paths to `<base>.gb`, `<base>.asm` (combined startup and generated code), `<base>.map`, `<base>.sym` and `<base>.memory.txt`. For the default ROM-only profile, RGBDS assembles, links with a map and symbols, then `rgbfix -v -p 0 -m 0 -r 0 -t TITLE` fixes logo/checksums and pads to 32 KiB; the opt-in MBC1 profile instead uses `-m 1` and its requested ROM capacity. Startup reserves reset/interrupt vectors, the `$0100` entry/header through `$014F`, disables interrupts, sets SP to the planned stack top, initializes **every** mutable global byte in little-endian order (including zero fields), calls the generated entry, and loops at `gb_idle` if it returns. No reset general-register value is assumed. The compiler independently checks the expected ROM length, Nintendo logo, selected mapper/ROM-size/RAM-size fields, entry jump/bounds, both checksums and critical linked ROM0/WRAM/stack symbols against the planned layout. RGBDS linker errors enforce full section placement/overflow. Build/tool/validation failures leave the five output paths untouched. After validation, the compiler stages all five artifacts and backups of any prior generation before replacing sibling paths. A caught publication failure attempts to restore the entire previous set (and remove newly created siblings), returns no artifact paths, and retains the backup work directory if rollback cannot complete. **This is not crash-atomic:** an OS or process crash during the final sibling replacements may leave a mixed generation; rebuild into an isolated base before consuming all five files together. The output directory must be writable; use an isolated output base per concurrent build. A cleanup failure leaves successful artifacts valid and prints a `GBC007` warning to stderr; on an unsuccessful build the original error is retained and cleanup context is appended.

Failure diagnostics: `GBC001` source read, `GBC003` arguments/options, `GBC004` IR, `GBC008` invalid MBC1 manifest, `GBL` subset, `GBB` backend (including `GBB007` invalid bank-copy span or unsafe MBC1 raw write), `GBT001` missing tool, `GBT002` wrong version, `GBT003` tool/timeout/assembly/link/fix process (includes stderr and retained work path when requested), `GBR001`-`GBR008` invalid ROM/header/checksum/linker symbols/asset bytes, `GBC005` build I/O/missing tool artifact, `GBC006` publication, `GBC007` cleanup warning/context. Roslyn diagnostics include source/line/column. Framework references allow semantic binding only, not calls into host libraries. SDK `GameMemory.WriteByte(ushort, byte)` is a resolved intrinsic; calling its host body throws, whereas the compiler emits native SM83 writes.

The additional `GBR008` validator diagnostic detects background asset-content mismatch. `GBR007` checks critical global symbols (including display helpers and fixed bank-1 assets), ignoring RGBDS scoped local labels. `GameDisplay.InitializeBackground()`, `WaitForNextVBlank()` and `SetScroll(byte, byte)` are SDK symbol-verified native operations; host bodies throw. The LCD helpers and generated code continue the explicitly anchored ROM0 `$0150` section; no helper may first-fit into unused vector/header gaps. Bank-1 ROMX holds 64 bytes of tile data followed by the 1024-byte map; see the [sample and encoding](../Samples/ScrollingBackground/README.md). The [independent PyBoy 2.6.0 procedure](csharp-gameboy-independent-validation.md) verifies both the semantic fixture and scrolling sample; hardware remains unverified.

## Explicit background and sprite inputs

The compiler has no demo artwork or fallback. Use `--background-tiles FILE`
and `--background-map FILE` together for `GameDisplay.InitializeBackground()`;
the scoped formats are exactly 64 bytes (four DMG 2bpp tiles) and 1024 bytes
(32×32 map, IDs 0..3). `GameSprites.Initialize()` requires `--sprite-tiles FILE`
with exactly 16 bytes. `SetScroll`/`WaitForNextVBlank` and sprite `Set`/`Hide`
alone remain asset-free. Missing FILE arguments report `GBC003`, unreadable files
`GBC001`; missing required, half-paired, invalid or unused inputs report `GBC009`
before invoking RGBDS or publishing. The sample-owned binaries are in
[Samples/Assets](../Samples/Assets/README.md).

The library equivalent (for a program initializing both background and sprites):

```csharp
var assets = new GameAssetBundle(
    File.ReadAllBytes("Samples/Assets/background.2bpp"),
    File.ReadAllBytes("Samples/Assets/background.tilemap"),
    File.ReadAllBytes("Samples/Assets/sprite.2bpp"));
var result = await new RomCompiler().CompileAsync(sourcePaths,
    new RomBuildOptions(outputPath, RgbdsBinDirectory: rgbdsBinDirectory, Assets: assets));
```

The bundle defensively copies arrays to immutable properties; null means absent.
Omit unused inputs (and omit `Assets` entirely for asset-free programs).
Background tiles/map are at bank 1 `$4000`/`$4040`. Sprite source is `$4440` with
background, otherwise `$4000`; destination remains VRAM `$8040`, tile 4.
The report lists per-input sizes, addresses and SHA-256, and validation compares
exact linked bytes to the supplied bundle, not to a sample hash. MBC1 bank-2+
manifests and runtime banking are unchanged.

To verify locally with the actual toolchain (the ROM tests are opt-in, **inconclusive** without `GB_RGBDS_BIN`; a zero-skips acceptance run must set it):

```powershell
$env:GB_PYBOY_PYTHON = (Resolve-Path .venv/Scripts/python.exe).Path # install per independent-validation guide
dotnet test GB.Compiler.Tests/GB.Compiler.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=compiler.trx' --results-directory artifacts/ci/test-results
& Tools/ci/Assert-TrxNoSkipped.ps1 artifacts/ci/test-results/compiler.trx
dotnet test GB.Emulator.Tests/GB.Emulator.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=emulator.trx' --results-directory artifacts/ci/test-results
& Tools/ci/Assert-TrxNoSkipped.ps1 artifacts/ci/test-results/emulator.trx
```

The fixture calls an arithmetic helper (`35 + 7`), writes **42 to WRAM `$C100`** and **`$A5` to `$C101`** via `GameMemory.WriteByte`; the SharpBoy execution test loads the real generated cartridge and runs `Gameboy.RunStep` until both bytes match or **10,000 instructions** elapse. It also dirties mutable globals before running to verify explicit zero/constant startup initialization. On 2026-10-17, with Windows x64 RGBDS 0.9.0 extracted in the example `$bin` directory, this completed in **168 instructions**. Current full compiler and independent-emulator commands, exact version/license, output bytes, frame bounds and limitations are recorded in the [independent validation guide](csharp-gameboy-independent-validation.md). RGBDS binaries, virtual environments and generated artifacts remain outside source control.

### MBC1 asset-only build (2026-10-17)

From the repository root, with `$bin` pointing to RGBDS 0.9.0 (PowerShell quotes preserve literal `$` in hex addresses):

```powershell
dotnet run --project GB.Compiler.Cli -c Release -- --rgbds-bin $bin --out artifacts/ci/banked.gb --title BANKED --mbc1-banks 4 --banked-asset 'Data:2:$4000:Samples/BankedAssets/data.bin' --banked-asset 'Tail:2:$7FFE:Samples/BankedAssets/tail.bin' Samples/BankedAssets/BankedAssets.cs
```

`--mbc1-banks 4|8|16|32` opts into no-RAM MBC1 type `$01`, header ROM-size codes `$01|$02|$03|$04` (64/128/256/512 KiB), RAM-size `$00`; without it the default stays 32 KiB ROM-only type `$00`. Repeat `--banked-asset NAME:BANK:$ADDRESS:FILE` for each nonempty binary: decimal physical bank 2 through banks−1, hexadecimal (`$4000`) or decimal CPU ROMX address `$4000-$7FFF`, unique ASCII identifier and no overlap or bank crossing. `--banked-asset` without `--mbc1-banks` is invalid (`GBC008`); invalid manifests also yield `GBC008`. The CLI additionally accepts decimal addresses. All asset files are read and hashed at build time. The build publishes `banked.gb`, `banked.asm`, `banked.map`, `banked.sym`, `banked.memory.txt` together; the ROM's size matches the chosen bank count. The memory report identifies cartridge header/capacity, per-bank asset/guard usage and free space, and every asset's address, byte length and SHA-256. Linked asset symbols and bytes are validated, and mismatches return `GBR008` without publishing. Code and bank-copy helper stay in ROM0; display constants stay in bank 1; bank 2+ holds only explicitly declared assets. The constant-argument SDK operations copy only a span within a single declared asset to `$D000-$D7FF`, then restore the previous bank; `ReadBufferByte` reads staging. No function banking, save RAM, >32 banks, VRAM/DMA transfers, reentrancy or managed arrays; interrupts remain disabled. See the [sample](../Samples/BankedAssets/README.md) and [independent PyBoy reproduction](csharp-gameboy-independent-validation.md). Configured compiler integration tests exercise every size with real RGBDS and SharpBoy, plus a four-bank PyBoy run; CI's existing compiler test and zero-skip gate include these tests without a separate banked CLI smoke step.
