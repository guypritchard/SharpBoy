# GB.Compiler.Cli

Install with `dotnet tool install GB.Compiler.Cli --version 0.1.0-alpha.1`
(or use `--tool-path <dir>` for an isolated install). The `gbc` command accepts
explicit `.cs` source files, `--out game.gb`, optional asset switches and
`--rgbds-bin <dir>` pointing at RGBDS 0.9.0. It compiles SharpBoy's restricted
C# subset into native DMG ROMs. See https://github.com/guypritchard/SharpBoy
for the language contract, toolchain instructions and sample commands.
