"""Bounded, headless independent WRAM oracle for generated DMG cartridges."""

import argparse
from importlib.metadata import version
import sys

from pyboy import PyBoy


def hex_byte(value):
    result = int(value, 16)
    if not 0 <= result <= 255:
        raise argparse.ArgumentTypeError("byte must be between 00 and FF")
    return result


def address(value):
    result = int(value, 16)
    if not 0xC000 <= result <= 0xDFFF:
        raise argparse.ArgumentTypeError("expected address must be in WRAM C000-DFFF")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rom", required=True)
    parser.add_argument("--address", type=address, help="first WRAM address (hex)")
    parser.add_argument("--expected", help="contiguous expected data bytes (hex, without sentinel)")
    parser.add_argument("--sentinel", type=hex_byte, help="completion byte at address + data length")
    parser.add_argument("--scroll", action="store_true", help="validate scrolling sample registers and tile/map")
    parser.add_argument("--banked", action="store_true", help="validate MBC1 staging, results and bank 1 restoration")
    parser.add_argument("--sprites", action="store_true", help="validate sprite tile/OAM, LCDC and bounded sentinel")
    parser.add_argument("--frames", required=True, type=int, help="maximum number of emulated frames")
    args = parser.parse_args()
    if args.frames < 1:
        parser.error("frames must be positive")
    if args.scroll or args.banked or args.sprites:
        if any(value is not None for value in (args.address, args.expected, args.sentinel)):
            parser.error("display/banked modes cannot be combined with semantic WRAM arguments")
    elif args.address is None or args.expected is None or args.sentinel is None:
        parser.error("--address, --expected and --sentinel are required for semantic validation")
    try:
        expected = bytes.fromhex(args.expected) if not (args.scroll or args.banked or args.sprites) else b""
    except ValueError as exc:
        parser.error(f"invalid expected hex: {exc}")
    if not (args.scroll or args.banked or args.sprites) and (not expected or args.address + len(expected) > 0xDFFF):
        parser.error("expected must be nonempty and fit in WRAM with sentinel")
    if version("pyboy") != "2.6.0":
        parser.error("PyBoy 2.6.0 is required")

    # Skip PyBoy's synthetic bootstrap via the public register and memory APIs.
    # This tests the cartridge from $0100, not authentic Nintendo boot behavior.
    pyboy = PyBoy(args.rom, window="null", bootrom=None, cgb=False,
                  sound_emulated=False, no_input=True)
    pyboy.set_emulation_speed(0)
    pyboy.memory[0xFF50] = 1  # Unmap boot ROM, as the DMG bootstrap would.
    pyboy.register_file.PC = 0x100
    sentinel_address = args.address + len(expected) if not (args.scroll or args.banked or args.sprites) else None
    sprite_tile = bytes.fromhex("18183C3C7E7EFFFFFFFF7E7E3C3C1818")
    last_x = None
    observed_moves = 0
    try:
        for frame in range(1, args.frames + 1):
            if not pyboy.tick(1, render=False, sound=False):
                print(f"FAIL: emulator stopped at frame {frame}", file=sys.stderr)
                return 1
            if args.sprites:
                x = pyboy.memory[0xFE01]
                if last_x is not None and x != last_x:
                    observed_moves += 1
                last_x = x
                if pyboy.memory[0xC200] != 0xA5:
                    continue
                tile = bytes(pyboy.memory[a] for a in range(0x8040, 0x8050))
                oam = bytes(pyboy.memory[a] for a in range(0xFE00, 0xFEA0))
                if (tile != sprite_tile or oam[0] != 56 or oam[1] < 40 or
                        oam[2:4] != bytes([4, 0]) or any(oam[4:]) or
                        pyboy.memory[0xFF40] != 0x93 or
                        pyboy.memory[0xFF48] != 0xE4 or pyboy.memory[0xFF49] != 0xE4 or
                        observed_moves < 3):
                    print(f"FAIL: sprites tile={tile.hex()} OAM={oam.hex()} LCDC={pyboy.memory[0xFF40]:02X} "
                          f"OBP={pyboy.memory[0xFF48]:02X}/{pyboy.memory[0xFF49]:02X} moves={observed_moves}",
                           file=sys.stderr)
                    return 1
                # Render one completed frame with OBJ enabled, then one with OBJ disabled.
                # Comparing copied RGBA buffers proves the independently rendered object is
                # visible rather than merely present in VRAM/OAM state.
                if not pyboy.tick(1, render=True, sound=False):
                    print("FAIL: emulator stopped before sprite render", file=sys.stderr)
                    return 1
                sprite_x = pyboy.memory[0xFE01] - 8
                sprite_y = pyboy.memory[0xFE00] - 16
                with_sprite = bytes(pyboy.screen.raw_buffer)
                lcdc = pyboy.memory[0xFF40]
                pyboy.memory[0xFF40] = lcdc & ~0x02
                if not pyboy.tick(1, render=True, sound=False):
                    print("FAIL: emulator stopped before background comparison", file=sys.stderr)
                    return 1
                without_sprite = bytes(pyboy.screen.raw_buffer)
                if pyboy.screen.raw_buffer_format != "RGBA":
                    print(f"FAIL: unsupported PyBoy screen format {pyboy.screen.raw_buffer_format}", file=sys.stderr)
                    return 1
                visible = False
                for y in range(max(0, sprite_y), min(144, sprite_y + 8)):
                    for x in range(max(0, sprite_x), min(160, sprite_x + 8)):
                        at = (y * 160 + x) * 4
                        if with_sprite[at:at + 3] == bytes([0, 0, 0]) and with_sprite[at:at + 4] != without_sprite[at:at + 4]:
                            visible = True
                if not visible:
                    print(f"FAIL: no independently rendered OBJ pixel in ({sprite_x},{sprite_y}) 8x8 box", file=sys.stderr)
                    return 1
                print(f"PASS: PyBoy 2.6.0 sprites frame {frame}/{args.frames} tile/OAM/LCDC/sentinel/rendered OBJ pixel")
                return 0
            if args.scroll:
                x, y = pyboy.memory[0xFF43], pyboy.memory[0xFF42]
                if (x >= 3 and x == y and pyboy.memory[0xFF40] == 0x91
                        and pyboy.memory[0xFF47] == 0xE4
                        and [pyboy.memory[a] for a in (0x8000, 0x8010, 0x8011, 0x9800, 0x9880)]
                        == [0, 0xFF, 0, 1, 2]):
                    print(f"PASS: PyBoy 2.6.0 scroll frame {frame}/{args.frames} "
                          f"SCX/SCY={x:02X}, LCDC=91 BGP=E4, tiles/map verified")
                    return 0
                continue
            if args.banked:
                results = bytes(pyboy.memory[a] for a in (0xC100, 0xC101, 0xC102, 0xC110))
                if results[3] != 0x5A:
                    continue
                staging = bytes(pyboy.memory[a] for a in (0xD000, 0xD001, 0xD002, 0xD004, 0xD005, 0xD006, 0xD007))
                state = pyboy.memory[0xC000]
                romx = bytes(pyboy.memory[a] for a in (0x4000, 0x4001))
                if results != bytes.fromhex("A1A1F05A") or staging != bytes.fromhex("A1A2A3A1A2A3F0") or state != 1 or romx != bytes.fromhex("0000"):
                    print(f"FAIL: banked results={results.hex()} staging={staging.hex()} state={state} ROMX={romx.hex()}", file=sys.stderr)
                    return 1
                print(f"PASS: PyBoy 2.6.0 banked frame {frame}/{args.frames} staging/results and bank 1 restoration")
                return 0
            if pyboy.memory[sentinel_address] != args.sentinel:
                continue
            actual = bytes(pyboy.memory[args.address:sentinel_address])
            if actual != expected:
                print(f"FAIL: frame {frame} WRAM ${args.address:04X}: expected {expected.hex().upper()}, "
                      f"actual {actual.hex().upper()}; sentinel ${sentinel_address:04X}="
                      f"{args.sentinel:02X}", file=sys.stderr)
                return 1
            print(f"PASS: PyBoy 2.6.0 frame {frame}/{args.frames} WRAM "
                  f"${args.address:04X}-${sentinel_address:04X}="
                  f"{(actual + bytes([args.sentinel])).hex().upper()}")
            return 0
        if args.sprites:
            print(f"TIMEOUT: sprites sentinel C200={pyboy.memory[0xC200]:02X} after {args.frames} frames", file=sys.stderr)
        elif args.banked:
            print(f"TIMEOUT: banked sentinel C110={pyboy.memory[0xC110]:02X} after {args.frames} frames", file=sys.stderr)
        elif args.scroll:
            print(f"TIMEOUT: scroll SCX/SCY={pyboy.memory[0xFF43]:02X}/"
                  f"{pyboy.memory[0xFF42]:02X} after {args.frames} frames", file=sys.stderr)
        else:
            print(f"TIMEOUT: sentinel ${sentinel_address:04X} expected {args.sentinel:02X}, "
                  f"actual {pyboy.memory[sentinel_address]:02X} after {args.frames} frames", file=sys.stderr)
        return 1
    finally:
        pyboy.stop(save=False)


if __name__ == "__main__":
    sys.exit(main())
