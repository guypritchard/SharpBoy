# Sample-owned DMG artwork

These binary source inputs preserve the original demos exactly. The compiler has
no artwork generators, sample hashes or fallback assets. Pass only inputs required
by the compiled initialization intrinsics (see the scrolling and sprite samples).

* `background.2bpp`: 64 bytes, four 8×8 tiles. Each row is low plane then high
  plane; bit 7 is the left pixel. Tile 0 is blank. Tile 1 uses shade 1 for
  `row % 4 < 2`, otherwise shade 2. Tile 2 uses shade 3 for even `row + col`,
  otherwise 0. Tile 3 uses shade 3 where `col == row` or `col == (row + 1) % 8`,
  otherwise shade 1.
* `background.tilemap`: 1024 bytes, row-major 32×32, ID
  `1 + ((x / 4 + y / 4) % 3)` using integer division.
* `sprite.2bpp`: 16 bytes, black diamond on transparent pixels:
  `18 18 3C 3C 7E 7E FF FF FF FF 7E 7E 3C 3C 18 18`.

Preservation SHA-256 (also checked by compiler tests):

* Background tiles concatenated with map:
  `50682b8a4bbdcb4a6cd262755ac5626cee38ca8f5347c88025031982eeae0ab4`
* Sprite:
  `ecad92431cabb0bb47965254ea8b0039b30794a032828c492d872ca89cc57d7d`

The build's `.memory.txt` reports each input's bank, address, size and individual
SHA-256. Different caller artwork is accepted under the same size/ID contracts.
