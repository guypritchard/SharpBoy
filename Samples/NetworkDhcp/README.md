# SDK DHCP cartridge

[NetworkDhcp.cs](NetworkDhcp.cs) is the actual guest program: packet construction, validation,
retries, NAK restart, lease selection and screen drawing are compiled from restricted C# to SM83
instructions. No .NET runtime runs on the Game Boy. The host cartridge supplies UDP datagrams.

`EntryPoint.cs` supplies the entry attribute and calls `NetworkDhcp.Run`. This lets the telemetry
sample reuse the complete acquisition routine before sending the acquired values to a receiver.

```powershell
./Samples/NetworkDhcp/Build.ps1 -RgbdsBin artifacts/toolchains/rgbds
dotnet run --project GB.Emulator.Windows -c Release -- --dhcp
# Or run the same embedded ROM headlessly:
dotnet run --project GB.Experiments -c Release -- --network-cartridge --dhcp
```

The build script invokes the regular compiler CLI with `--text-font` and compiles
[GameTextNumbers.cs](../../GB.Sdk/Target/GameTextNumbers.cs) alongside the sample. It refreshes
`GB.Roms/NETWORK-DHCP.gb`; rebuild the emulator afterward. The emulator embeds this firmware so
users need no compiler/toolchain to run it. Compiler tests rebuild and byte-compare the ROM.
Assembly, symbols, map and memory report go to `artifacts/netcart/`.

`GameText.Write(address, "CONSTANT TEXT")` emits ASCII tile writes. It consumes text at compile
time and creates no runtime strings. `GameFont.CreateTiles()` supplies an original uppercase and
digit font through the normal background assets; `--text-font` also supplies a blank map. The
reusable `GameTextNumbers` source draws decimal bytes and IPv4 addresses. The sample disables
the LCD during VBlank before changing the map. The Windows UI scales ordinary emulated pixels.

The `$C000..$C05F` mailbox holds progress, transaction and lease bytes. `--wram-start 49408`
reserves compiler storage from `$C100` onward, avoiding the mailbox. Native memory operations
also access cartridge registers at `$A000` and its 1024-byte transmit/receive buffers. The client
uses 16-bit cursors and remaining lengths, including options beyond byte 255.

Initial acquisition retains the earlier experiment's bounded retries and strict parsing. Renewal,
expiry, ARP conflict detection and application traffic are future work. The optional Ethernet
transport is unchanged; live LAN interoperability remains unverified here because Npcap is absent.
See [DHCP experiment documentation](../../Docs/dhcp-cartridge-experiment.md) for transport setup.
