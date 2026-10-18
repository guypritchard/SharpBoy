; ROM0 only. Inputs: A=physical bank, HL=ROMX source, DE=staging destination,
; BC=nonzero byte count. Clobbers A,F,B,C,D,E,H,L. Saves previous tracked ROMX
; bank on CPU stack (CALL 2 bytes + PUSH AF 2 bytes), restores it before RET.
; DI at startup remains in force: no callbacks, nested calls, or reentrancy.
PUSHS
SECTION "GB MBC1 bank state", WRAM0[$C000]
gb_current_bank:: ds 1
SECTION "GB MBC1 target bank", WRAM0[$C001]
gb_bank_copy_target:: ds 1
POPS
gb_bank_copy::
    ld [gb_bank_copy_target], a
    ld a, [gb_current_bank]
    push af
    ld a, [gb_bank_copy_target]
    ld [$2000], a
    ld [gb_current_bank], a
.loop
    ld a, [hli]
    ld [de], a
    inc de
    dec bc
    ld a, b
    or c
    jr nz, .loop
    pop af
    ld [$2000], a
    ld [gb_current_bank], a
    ret
