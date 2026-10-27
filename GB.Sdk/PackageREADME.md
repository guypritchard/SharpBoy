# GB.Sdk

Reference-only .NET 10 declarations for SharpBoy's restricted C#-to-Game-Boy
language. Install this package in a project for C# validation and IntelliSense.
Host method bodies deliberately throw guard exceptions; only `gbc` translates
their exact SDK symbols into native ROM instructions. GameFont.CreateTiles() creates the original uppercase/digit ASCII font on the host.
GameText.Write emits constant text as native tile writes. GameMemory.ReadByte/WriteByte
access the target bus. Compile Target/GameTextNumbers.cs alongside your game for
reusable decimal/IPv4 drawing. Target/GameNetwork.cs provides native-source cartridge command,
word encoding and fixed-record TCP/UDP receive helpers; include it explicitly when compiling a
network-cartridge program. The experimental cartridge offloads sockets to the host. No managed
runtime or heap is needed on the target. See https://github.com/guypritchard/SharpBoy for documentation.

For version-2 TCP cartridges, compile Target/GameNetworkStream.cs alongside GameNetwork.cs to send up to 1,024 bytes and read exact application lengths across split/coalesced TCP reads. This helper supports one TCP connection per guest run; see Docs/network-life-experiment.md in the source repository.
