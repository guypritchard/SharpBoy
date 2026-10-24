; Polling only, IME and IE remain disabled. No calls, pushes or WRAM scratch.
; Continue the anchored GB startup ROM0[$0150] section. Never let rgblink
; first-fit this code into gaps in the interrupt vectors or cartridge header.
; The generated WRAM sections use PUSHS/POPS to resume this same ROM0 section.
gb_wait_next_vblank::
    ; Always observe a non-VBlank scanline before accepting the NEXT VBlank.
.leave:
    ldh a, [$FF44]
    cp 144
    jr nc, .leave
.enter:
    ldh a, [$FF44]
    cp 144
    jr c, .enter
    ret

IF DEF(GB_HAS_BACKGROUND)
gb_display_init::
    ; If enabled, disable only during VBlank; if already off do not wait for LY.
    ldh a, [$FF40]
    bit 7, a
    jr z, .disable
.safe:
    ldh a, [$FF44]
    cp 144
    jr c, .safe
.disable:
    xor a
    ldh [$FF40], a
    ldh [$FF42], a ; SCY = 0
    ldh [$FF43], a ; SCX = 0
    ldh [$FF4A], a ; WY = 0
    ldh [$FF4B], a ; WX = 0
    ld a, $E4 ; BGP maps color 0..3 to white, light, dark, black
    ldh [$FF47], a
    xor a
    ldh [$FF48], a ; OBP0 = 0
    ldh [$FF49], a ; OBP1 = 0
    ; LCD off: transfer source bytes from fixed ROM bank 1 to VRAM.
    ld hl, gb_bg_tiles
    ld de, $8000
    ld bc, 64
.tiles:
    ld a, [hl+]
    ld [de], a
    inc de
    dec bc
    ld a, b
    or c
    jr nz, .tiles
    ld hl, gb_bg_map
    ld de, $9800
    ld bc, 1024
.map:
    ld a, [hl+]
    ld [de], a
    inc de
    dec bc
    ld a, b
    or c
    jr nz, .map
    ld a, $91 ; LCD on, window off, BG on, $9800 map, unsigned $8000 tiles
    ldh [$FF40], a ; enable LAST
    ret
ENDC
