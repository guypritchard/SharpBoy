; DMG: no interrupt handlers are enabled. Unexpected vectors park safely.
SECTION "GB reset", ROM0[$0000]
    jp gb_start
SECTION "GB interrupt vectors", ROM0[$0040]
    jp gb_idle ; VBlank
SECTION "GB LCD vector", ROM0[$0048]
    jp gb_idle
SECTION "GB timer vector", ROM0[$0050]
    jp gb_idle
SECTION "GB serial vector", ROM0[$0058]
    jp gb_idle
SECTION "GB joypad vector", ROM0[$0060]
    jp gb_idle
SECTION "GB cartridge header", ROM0[$0100]
    nop
    jp gb_start
    ds $4C ; logo and metadata reserved for rgbfix; entry ends at $0103
SECTION "GB startup", ROM0[$0150]
gb_start::
    di
    ld sp, gb_stack_top
    xor a
    ld [$FFFF], a ; disable interrupt sources independently of reset state
    ld [$FF0F], a ; discard pending interrupts
; GB_MBC1_INITIALIZERS
; GB_GLOBAL_INITIALIZERS
    call gb_entry
gb_idle::
    jp gb_idle
