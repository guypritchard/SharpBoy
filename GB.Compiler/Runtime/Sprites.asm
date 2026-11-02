; Polling-only ROM0 native OBJ helpers. DI/IE remain disabled. No DMA or bus blocking.
; gb_wait_oam_safe: CALL 2 stack bytes, clobbers AF, returns immediately with LCD off.
; LY 153 is deliberately skipped so a fixed OAM write cannot straddle frame restart.
gb_wait_oam_safe::
    ldh a, [$FF40]
    bit 7, a
    ret z
.wait:
    ldh a, [$FF44]
    cp 144
    jr c, .wait
    cp 153
    jr z, .wait
    ret

; CALL 2 bytes, PUSH AF 2 bytes; no nested calls. Clobbers AF, BC, DE, HL.
; LCDC is saved even when LCD already off; only OBJ enable changes on restore.
IF DEF(GB_HAS_SPRITE_INIT)
gb_sprite_init::
    ldh a, [$FF40]
    push af
    bit 7, a
    jr z, .disable
.safe:
    ldh a, [$FF44]
    cp 144
    jr c, .safe
    cp 153
    jr z, .safe
.disable:
    xor a
    ldh [$FF40], a
    ld hl, gb_sprite_tile
    ld de, $8040
    ld bc, 16
.copy:
    ld a, [hl+]
    ld [de], a
    inc de
    dec bc
    ld a, b
    or c
    jr nz, .copy
    ld hl, $FE00
    ld b, 160
    xor a
.clear:
    ld [hl+], a
    dec b
    jr nz, .clear
    ld a, $E4
    ldh [$FF48], a
    ldh [$FF49], a
    pop af
    or $02
    ldh [$FF40], a
    ret
ENDC
