# DMG native ABI contract (v0, milestone 6 cartridge)

## Direct-OAM sprite extension ABI (milestone 10 slice)

When sprite initialization is used, bank-1 `gb_sprite_tile` contains exactly the
16 caller-supplied bytes. It begins at `$4440` (ends `$4450`) when background
initialization is also used, otherwise `$4000` (ends `$4010`). Sample artwork and
its preservation hashes belong to [Samples/Assets](../Samples/Assets/README.md), not the ABI.
`gb_sprite_init` and `gb_wait_oam_safe` share anchored ROM0 with generated code.
`gb_sprite_init` saves previous LCDC with `PUSH AF`, waits for LY 144-152 only when
LCD was on, disables LCD, copies tile 4 to `$8040`, zeros `$FE00-$FE9F`, writes
OBP0/OBP1 `$E4`, and restores LCDC with bit 1 set. It clobbers AF/BC/DE/HL and
uses 4 bytes of hardware stack (CALL plus PUSH). Set/Hide call the leaf
`gb_wait_oam_safe` (2 bytes CALL stack, clobbers AF) which returns immediately
with LCD off, accepts LY 144-152, and defers LY 153 to the next VBlank. Set then directly writes Y, X,
tile `$04`, attributes at `$FE00 + 4*index`; Hide directly writes Y=0. Coordinates
are raw OAM bytes, not screen positions. The compiler accounts for each CALL/PUSH
path plus enclosing game calls/startup. MBC1 bank 1 is restored after bank-2+
copies before tile copy; bank-copy code must remain ROM0. Interrupts stay disabled;
these operations do not implement DMA, its timing/bus blocking, reentrancy or
cycle-exact OAM access across a VBlank boundary.

`Sm83Backend.Emit(IrModule, Sm83Limits?)` accepts verified typed IR (and verifies it again). It returns an all-or-nothing `BackendResult`: on success, RGBDS SM83 function/storage source, a `MemoryLayout` and its deterministic `MemoryReport`; on error, located `BackendDiagnostic` entries and **no** successful output. `RomCompiler.CompileAsync` adds native startup, RGBDS 0.9.0 linking, ROM validation and artifact publication. The binary ABI is compiler-internal, not externally stable. Milestone 8 independent comparison is complete using pinned PyBoy 2.6.0 with the boot ROM unmapped alongside bounded SharpBoy execution; authentic boot-ROM acceptance and physical hardware fidelity remain unproven (see [independent validation](csharp-gameboy-independent-validation.md)).

## Values and fixed storage slots

| C# type | WRAM bytes | Representation |
| --- | --- | --- |
| `bool` | 1 | canonical 0 or 1 |
| `byte`, `sbyte` | 1 | low 8 bits, signed two's complement for `sbyte` |
| `ushort`, `short` | 2 | low byte at lower address; signed two's complement for `short` |
| `int` | 4 | least significant byte first; signed two's complement |

All storage is byte-aligned with no padding except when an allocation would straddle the physical `$CFFF/$D000` WRAM bank boundary. The planner allocates every mutable global (in ordinal qualified-name order), then every **entry-reachable** function in module order: parameters in declaration order, local/short-circuit temporary slots in IR order, every IR result value in block/instruction order, then one return slot for a nonvoid function. Slots are distinct for the entire run; no liveness-based reuse and no shared activation. Constants are immediate bits, not mutable storage. Unreachable helper functions receive neither assembly nor frame storage. The report includes each global's initial bits; startup explicitly writes every global byte (including zero-valued fields) in little-endian order before `gb_entry`. Locals with no initializer are unspecified until written; the C# frontend enforces definite assignment. Layout addresses and generated names are compiler-version internal, not a stable cross-compiler ABI.

Each IR instruction reads its operands from WRAM, computes with `A` and optionally `B`, and stores its result in its own WRAM value slot before the next IR instruction. No IR value survives solely in registers across an instruction or call. Before `call`, the caller copies already-evaluated argument values from its slots into the callee's parameter slots, in declared parameter order. After `ret`, a nonvoid caller copies the callee's return slot into its own result slot. The callee writes its return slot before `ret`; void returns write nothing. Nested/sibling calls therefore cannot overwrite live caller spills. Recursion, reentrancy, interrupts and overlapping activations are unsupported; the call graph is checked again directly from IR calls before planning.

## Regions, stack and clobbers

The default target memory limit is `$C000-$DFFF` (8192 bytes DMG WRAM), split into fixed `WRAM0` (`$C000-$CFFF`) and DMG bank 1 `WRAMX` (`$D000-$DFFF`). The uppermost 128 bytes are reserved as a **separate downward-growing CPU stack** (`$DF80-$DFFF`, initial `SP=$E000`); static slots grow upward from `$C000` and must end strictly before the stack reservation. Bank-crossing primitives advance to `$D000` with a small unused gap. `Sm83Limits` can override WRAM start/size, stack reserve and generated ROM0 code budget. The default generated-code budget is `$3F00` bytes; RGBDS linker placement enforces actual ROM0 section capacity and overlaps with fixed header/startup. Assembly reserves each slot as a fixed-address RGBDS WRAM section and the stack as one or two fixed-address sections. `gb_entry`, `gb_fn_N`, `gb_stack_bottom`, `gb_stack_top` and `gb_ram_XXXX` are emitted symbols; startup sets `SP` to `gb_stack_top` explicitly and initializes statics. The generated code relies on **no** emulator/reset general-register or flag values.

Generated functions clobber **all** SM83 general registers/flags (`A`, `F`, `B`, `C`, `D`, `E`, `H`, `L`); they promise preservation of none. All compiler scratch is in these registers: **zero bytes of runtime scratch WRAM** and **zero generated CPU `push`/`pop`** (`PUSHS`/`POPS` in assembly are RGBDS section directives, not CPU instructions). A call consumes exactly two hardware-stack bytes for its return address; branches, bytewise arithmetic, comparisons and the inline ordered `GameMemory.WriteByte` (16-bit address loaded into `HL`, value into `A`, then `ld [hl], a`) require no CPU stack. LCD initialization and next-VBlank waiting use two native leaf helpers; each helper call adds two stack bytes to that function's path, with no nested calls, saves or WRAM scratch. `SetScroll` is inline, ordered `LDH` writes SCX then SCY. The callee balances `SP`: its `ret` consumes precisely its incoming return address. With interrupts disabled by startup, required stack bytes are two for startup's `CALL gb_entry` plus the maximum nested entry-reachable game-call path (two bytes each) **or** native-helper leaf call (two bytes). The reserved range must contain this maximum; headroom is reported. If entry returns, startup loops at `gb_idle` rather than falling through. Reset jumps to `gb_start`; unexpected interrupt vectors jump to idle and are never enabled. The `$0100-$014F` entry/header is reserved for `rgbfix`; executable startup begins at `$0150`.

The startup, optional LCD helpers and generated code share the **same explicitly anchored `ROM0[$0150]` section**. RGBDS `PUSHS`/`POPS` preserves that section while fixed-address WRAM sections are declared; code cannot first-fit into free vector/header gaps. The linker rejects total ROM0 overflow. Background initialization requires explicit 64-byte tile and 1024-byte 32×32 map inputs (IDs 0..3), emitted unchanged into fixed bank-1 `ROMX[$4000]` (map immediately after the tile bytes); linker symbols and exact ROM bytes are validated against the supplied bundle. `SetScroll` or `WaitForNextVBlank` alone emits no assets or background initialization helper. There is no compiler-owned artwork or fallback. The copy loop reads ROM bank 1 (`HL`) and writes `$8000`/`$9800` (`DE`) while LCD is off. Initialization checks LCDC bit 7, waits for LY ≥ 144 before disabling if LCD was enabled, establishes SCX/SCY/WX/WY = 0, BGP = `$E4`, OBP0/1 = 0, then enables LCD **last** with LCDC = `$91` (LCD on, window/sprites off, BG on, unsigned `$8000` tile addressing, `$9800` BG map). No boot-ROM LCD register value is assumed. `WaitForNextVBlank` polls LY until below 144 **before** waiting until LY ≥ 144; both LCD and interrupts must remain in the polling configuration. The sample-specific map and encoding/checksum are documented with the [sample](../Samples/ScrollingBackground/README.md).

Multi-byte arithmetic propagates carry/borrow from low to high byte, including all four bytes of `int`. Relational comparisons inspect high bytes first and flip bit 7 of the high byte of **both** operands for signed ordering; equality and inequality compare raw bits. Conditional results are canonical booleans. Conversions copy low bytes, discard excess high bytes, or sign/zero fill new high bytes. Overflow is unchecked modulo the result width. Hardware writes occur once in IR instruction order. Only actual SM83 instructions are selected; no implicit CLR runtime is used.

## Failure contract

Backend IDs: `GBB001` unverified IR/entry-reachable recursive graph; `GBB002` invalid configured DMG/ROM0 limits; `GBB003` WRAM static slots or stack reserve exceeds the available WRAM region; `GBB004` required hardware stack exceeds its explicit reserve; `GBB005` unsupported backend storage/instruction pattern; `GBB006` generated ROM0 instruction bytes exceed the configured budget. Capacity diagnostics include resource, required/available bytes and a contributor; source locations are supplied wherever an IR source exists. On any backend failure there is no assembly, layout or report. Later tool errors use `GBT003`, cartridge validation uses `GBR001`-`GBR008` (`GBR008` is asset-content mismatch); neither reports success or publishes artifacts on failure.

### Scoped MBC1 asset ABI extension (2026-10-17)

The original v0 ABI above is still the ROM-only default. An explicit `CartridgeProfile.CreateMbc1(4|8|16|32, assets)` produces no-RAM MBC1 type `$01`, ROM-size code `$01|$02|$03|$04` (64/128/256/512 KiB), RAM-size `$00`. Code/startup/bank-copy helper are in ROM0; fixed display tile/map data remain in bank 1; only declared binaries occupy bank 2+. The ROMX `$7FFF` capacity guard forces full capacity unless a last-bank asset already ends at `$8000`; that asset forces capacity itself and the guard is omitted. Asset metadata includes physical bank, CPU address, size and SHA-256; ROM validation checks linked symbols and content. `GBB007` rejects a copy not wholly inside one declared asset and the staging range; `GBR008` detects asset-content mismatch.

MBC1 uses mode 0 with RAM disabled. Startup tracks the currently selected ROMX bank in `gb_current_bank` (`$C000`, initially 1); the helper's target scratch is `gb_bank_copy_target` (`$C001`). `$D000-$D7FF` is reserved for bank-copy staging, so planned static slots and the hardware stack cannot overlap it. Each constant-argument copy passes physical bank in A, ROMX source in HL, staging destination in DE and nonzero length in BC to the ROM0 helper. It pushes the previous bank (two stack bytes), selects the requested bank via `$2000`, copies in source order, then restores the saved bank via `$2000` before returning; the extra helper CALL and PUSH AF add four stack bytes on top of startup's two for a leaf entry (six total). Reads use a constant staging offset. No asynchronous switching: interrupts remain disabled; no reentrancy, function banking, save RAM, >32 banks, VRAM/DMA transfers or managed arrays. See the [sample](../Samples/BankedAssets/README.md).


SDK text writes expand constant ASCII into ROM instructions and use no target string object or
additional helper stack. Text writes are range-checked against the whole `$9800..$9BFF` map.
Native `ReadByte` reads through HL and spills its byte result; it is an observable hardware
operation. Compile-time constants and width-only conversions may be loaded directly at each
use instead of materialized. Arithmetic may omit unobserved high bytes, preserving carry/borrow
through every demanded byte and preserving all observable C# values. Frame/VRAM timing remains
the caller's responsibility for raw memory and text writes.
