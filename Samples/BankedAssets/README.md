# Banked assets (MBC1, 64 KiB)

Build from the repository root (RGBDS 0.9.0 required; set `$bin` to its
executable directory; `GB_RGBDS_BIN` is used by the tests):

```powershell
dotnet run --project GB.Compiler.Cli -c Release -- --rgbds-bin $bin --out banked.gb --title BANKED --mbc1-banks 4 --banked-asset 'Data:2:$4000:Samples/BankedAssets/data.bin' --banked-asset 'Tail:2:$7FFE:Samples/BankedAssets/tail.bin' Samples/BankedAssets/BankedAssets.cs
```

The declared physical bank 2 assets are copied by the ROM0 helper into WRAM
staging `$D000-$D7FF`; C# reads staged bytes using `ReadBufferByte` and writes
results at `$C100-$C102` and the `$5A` success sentinel at `$C110`.
The ROM0 helper maintains `$01` at `$C000` after copying, restoring bank 1
after every copy (so `$4000-$7FFF` again displays bank 1). This sample does
not invoke the display intrinsic, leaving the reserved bank-1 bytes zero.
`data.bin` is `A0 A1 A2 A3 A4 A5`; `tail.bin` is `EF F0` (last byte at `$7FFF`).
`CopyToBuffer` arguments are physical bank, ROMX CPU source address, D000-relative
buffer offset and byte length. Each copy must lie in one declared asset. This is
**assets-only** MBC1 type `$01` without RAM, with 4/8/16/32 supported banks
(64/128/256/512 KiB, ROM-size header `$01`/`$02`/`$03`/`$04`, RAM `$00`).
Code and helper stay in ROM0; bank 1 is reserved for display data, assets use
physical bank 2 and above. Arguments must be compile-time constants; managed
arrays, function banking, save RAM, more than 32 banks, DMA, VRAM transfer,
interrupts and reentrancy are unsupported. Interrupts remain disabled and the
mapper stays in mode 0 with RAM disabled. The build emits `banked.gb` plus
`banked.asm`, `banked.map`, `banked.sym` and `banked.memory.txt`. For this
four-bank manifest the report lists bank 0 and 1 as 0/16384 used/free, bank 2
as 8/16376, bank 3 as 1/16383 (capacity guard). It records Data at bank 2
`$4000`, size 6, SHA-256 `7924267638840da707f8353c26b12ba9d0774eae4660b9db13000addff29b5f5`
and Tail at bank 2 `$7FFE`, size 2, SHA-256
`3b41aeb6a4c56b9d52c95fcaadda44ed82774aeb853e88048df8d406d8115831`.
The report also includes generated ROM0 code (721 bytes excluding header and
startup), mapper state `$C000`, and stack `$DF80-$DFFF` (required 6 bytes).

Independently validate the produced ROM with the pinned PyBoy 2.6.0 Python
environment ([setup](../../Docs/csharp-gameboy-independent-validation.md)):

```powershell
& $env:GB_PYBOY_PYTHON Tools/IndependentEmulator/validate.py --rom banked.gb --banked --frames 10
```

On 2026-10-17 the configured compiler integration tests passed the sentinel
in **342 SharpBoy instructions** for each of 4/8/16/32 banks, and PyBoy
reported `PASS: PyBoy 2.6.0 banked frame 1/10 staging/results and bank 1 restoration`.
