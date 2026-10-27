# DHCP acquisition on the virtual network cartridge

The [constrained C# SDK program](../Samples/NetworkDhcp/NetworkDhcp.cs), compiled to native SM83 instructions, performs initial DHCP address acquisition over a virtual UDP interface. The ROM sends a DHCPDISCOVER, selects a matching DHCPOFFER, broadcasts a DHCPREQUEST containing the selected address/server, and enters BOUND only after validating DHCPACK. The host never writes a lease directly into guest RAM.

```powershell
dotnet run --project GB.Experiments -c Release -- --network-cartridge --dhcp

# Exercise retransmissions and restart after a negative acknowledgement.
dotnet run --project GB.Experiments -c Release -- --network-cartridge --dhcp --drop-first-offer --drop-first-ack --nak-first-request

# Export the native 32 KiB ROM.
dotnet run --project GB.Experiments -c Release -- --network-cartridge --dhcp --rom-output artifacts/netcart/dhcp.gb
```

Typical output:

```text
client Discover → server Offer → client Request → server Ack
ROM result: BOUND; virtual receive timeouts: 0
IP 192.0.2.42, mask 255.255.255.0, gateway 192.0.2.1, DNS 192.0.2.53, server 192.0.2.1, lease 3600s
```

Exit status is 0 when bound, 1 on failure/cancellation, or 2 for invalid arguments. Ctrl+C cancels the run.

## Watch it on the Game Boy screen

To send the acquired values to a real localhost receiver, use the new
[SDK telemetry experiment](network-telemetry-and-cluster.md). It reuses this guest's DHCP logic and
screen, then opens an application UDP/TCP socket. DHCP addresses remain virtual payload data in
that local mode; the Npcap transport is still DHCP-only.

```powershell
dotnet run --project GB.Experiments.Windows -c Release -- --dhcp
```

You can also launch `GB.Experiments.Windows` without arguments and choose **DHCP cartridge**. Its resizable window uses `EmulationHost` and the same `ScreenControl` as the player, supplied by the independent `GB.Emulator.Presentation.WinForms` library. The virtual backend delays each receive slightly so DISCOVER and REQUESTING remain visible. Restart acquisition recreates the machine and network device; closing cancels pending I/O and releases their resources.

The display is native Game Boy output built with `GameDisplay`, `GameText`, `GameMemory` and the shared `GameTextNumbers` source. The original hand-emitted DHCP/graphics opcodes have been removed. Guest instructions load a small font into VRAM, write the tile map during LCD-off periods, and draw STARTING, DISCOVER, OFFER RECEIVED, REQUESTING, ACK RECEIVED, IP ACQUIRED, RETRYING, NAK restart or failure. The accepted IP, gateway, DNS and server appear as ordinary dotted-decimal text. The Windows UI only scales the frame; it does not draw an IP/status overlay. The exported ROM includes the same screen code.

## Ethernet adapter backend

The optional Windows Npcap backend connects the same guest to Ethernet frames on an explicitly selected adapter:

```powershell
# List adapter names from an existing Npcap installation.
dotnet run --project GB.Experiments -c Release -- --network-cartridge --dhcp --list-adapters

# Paste an exact adapter name from that list.
dotnet run --project GB.Experiments -c Release -- --network-cartridge --dhcp --adapter "PASTE_ADAPTER_NAME"

# Watch the same Ethernet acquisition on the Game Boy screen.
dotnet run --project GB.Experiments.Windows -c Release -- --dhcp --adapter "PASTE_ADAPTER_NAME"
```

Npcap must be installed separately and accessible to the process. The application loads it from the Windows system Npcap directory; it does not install a driver, create a Windows network bridge or change adapter addresses. No adapter is selected automatically. Without Npcap, listing adapters reports the missing dependency and the virtual demo remains usable.

`EthernetDhcpBackend` wraps an `IEthernetLink`. It emits Ethernet II, IPv4 and UDP headers/checksums around the guest's DHCP payload, using the guest MAC and source `0.0.0.0:68`, and receives server replies on port 68. Capture checks MAC destination, IP/UDP lengths, IPv4/UDP checksums and fragmentation flags before delivering a payload to the cartridge. `NpcapEthernetLink` verifies Ethernet link type, installs a capture filter, uses nonblocking reads and copies packet data before the next native capture call. The framing follows [IPv4](https://www.rfc-editor.org/rfc/rfc791) and [UDP](https://www.rfc-editor.org/rfc/rfc768); the native integration follows the [Npcap API](https://npcap.com/guide/wpcap/pcap.html).

This is an Ethernet path for initial DHCP acquisition, not general bridged IP connectivity. ARP/address-conflict detection, routing/application traffic using the leased address, fragmentation/reassembly, renewal and release remain future work. The experimental client still uses a fixed MAC/transaction seed and is intended for one acquisition session at a time. Native bindings compile, and complete guest DORA is tested through encoded Ethernet frames, but live-router interoperability is unverified here because Npcap is not installed. Adapter/driver behavior, especially over Wi-Fi, must be checked on the target system.

## Execution boundaries

| Component | Responsibility |
| --- | --- |
| `DhcpCartridgeRom` | Loads the SDK-compiled native ROM. Guest instructions construct packets, validate replies, parse options and store the accepted configuration. |
| `NetworkCartridgeDevice` | Maps buffers/registers and queues asynchronous datagram operations; publishes completion between bounded CPU batches. |
| `IDatagramCartridgeBackend` | Exchanges complete UDP payloads with explicit IPv4/port metadata. |
| `VirtualDhcpNetwork` | An isolated UDP segment with one simulated DHCP server and a bounded receive queue; offers a configurable `VirtualDhcpLease` with deterministic defaults. |
| `DhcpSession` / `DhcpExperiment` | Own the machine and device, schedule through `EmulationHost`, handle cancellation and return the guest's result. |
| `DhcpExperimentForm` | Presents immutable frames in the Windows emulator with integer/fit scaling and restart/close lifecycle. |
| `EthernetDhcpBackend` / `NpcapEthernetLink` | Optional framed Ethernet DHCP transport on the selected Windows adapter. |

The DHCP payloads follow the BOOTP/DHCP layout and option encodings from [RFC 2131](https://www.rfc-editor.org/rfc/rfc2131) and [RFC 2132](https://www.rfc-editor.org/rfc/rfc2132). The default virtual mode uses no host sockets, driver or LAN broadcasts. The explicit Ethernet mode offloads IPv4/UDP framing to the cartridge backend and can broadcast to a real DHCP server. An accepted address is retained by the guest experiment; it is not installed on the computer or applied to the existing host TCP backend.

The ROM has a ROM-only header and needs an explicitly attached version-2 cartridge device. Loading the exported ROM through the normal file picker does not attach that device; use the DHCP menu or command. A physical cartridge is not implemented. The restricted C# compiler/SDK builds the native program; `EntryPoint.cs` wraps the reusable `NetworkDhcp.Run` guest routine.

## Datagram cartridge protocol, version 2

The existing TCP protocol remains version 1 with its original 256-byte buffer layout. Constructing `NetworkCartridgeDevice` with an `IDatagramCartridgeBackend` selects version 2:

| Address | Meaning |
| --- | --- |
| `$A000` | Device identifier: `N` |
| `$A001` | Version: 2 |
| `$A002–$A008` | Existing status, command, error and little-endian transmit/receive lengths |
| `$A009` | Capabilities: bit 0 indicates datagram mode; zero for TCP mode |
| `$A010–$A011` | Writable local UDP port, little-endian; captured by Connect/Open |
| `$A012–$A013` | Writable destination UDP port, little-endian |
| `$A014–$A017` | Writable destination IPv4 bytes, network order |
| `$A018–$A019` | Last received source UDP port, read-only, little-endian |
| `$A01A–$A01D` | Last received source IPv4 bytes, read-only, network order |
| `$A100–$A4FF` | 1024-byte transmit buffer |
| `$A500–$A8FF` | 1024-byte receive buffer |

Connect opens the virtual UDP port; the connected status bit means the port is open, not that a TCP connection exists. Send captures the destination and payload at command submission. Receive returns exactly one datagram without merging or splitting it. Oversized datagrams fail explicitly instead of being silently truncated. Zero-length datagrams are valid and set receive-ready until acknowledged. Reading or inspecting registers cannot consume a message. An unread datagram must be acknowledged before another receive.

Error 8 means timeout. A UDP receive timeout leaves the port open so the ROM can retransmit; other transport failures clear connected status. The backend must honor cancellation. The existing asynchronous command deadline is five seconds; the headless DHCP runner also enforces a thirty-second wall-clock limit and owns device/backend disposal. The deterministic virtual server signals an empty receive queue as an immediate simulated timeout. These are logical loss/retry tests, not a model of real DHCP retransmission timing.

## Guest validation and state

The ROM uses ports 68 → 67, a broadcast destination, the broadcast BOOTP flag, a locally administered fixed MAC and a deterministic transaction ID. It advertises a maximum DHCP payload of 1024 bytes. SELECTING and REQUESTING each allow three transmissions and up to eight unrelated/invalid packets per transmission. A NAK from the selected server permits one fresh acquisition attempt with a changed transaction ID; repeated NAKs terminate rather than loop forever.

Before accepting a reply, guest instructions check its datagram length, source port, BOOTREPLY operation, hardware type/length, transaction ID, client hardware address and DHCP cookie. The virtual segment has no relays: `giaddr` must be zero, and the server-identifier option must match the datagram's source address. ACK must identify the selected server and the exact offered address and contain a nonzero lease time.

The option parser supports padding, END, arbitrary ordering, unknown options, message type, server identifier, lease time, subnet mask, router list and DNS list. It takes the first router/DNS address. It rejects truncated options, wrong known-option lengths, duplicate known options, missing END and option-overloaded `sname`/`file` fields. HL/DE and 16-bit length accounting protect reads across the full 1 KiB buffer. RFC 3396 option concatenation and option overload are outside this subset. Optional settings are taken from ACK; omitted settings are zero, rather than copied from an earlier offer.

| Guest RAM | Meaning |
| --- | --- |
| `$C000` | Result: 0 in progress, 1 bound, `$FF` failed |
| `$C001` | State: 1 selecting, 2 requesting, 4 bound, 5 failed |
| `$C008–$C00B` | Transaction ID, network order |
| `$C010–$C013` | Candidate/accepted address |
| `$C014–$C017` | Selected server |
| `$C018–$C01B` | ACK subnet mask |
| `$C01C–$C01F` | ACK first router |
| `$C020–$C023` | ACK first DNS server |
| `$C024–$C027` | ACK lease seconds, big-endian 32-bit value |

Candidate address/server values become usable only when result is 1 and state is BOUND. `DhcpExperimentResult` returns zero configuration on failure so callers cannot accidentally use a rejected offer.

This is initial acquisition, not a complete live-network DHCP client. The ROM halts after BOUND with its screen visible. Lease expiry, T1/T2 renewal/rebinding, RELEASE/DECLINE, address conflict detection/ARP, persistent leases, random transaction/MAC selection and real-time exponential retry scheduling remain future work. General cartridge lifecycle, normal-player/debugger device selection and live-network rewind policy also remain separate work.

## Validation

`DhcpExperimentTests` executes the ROM through Core's CPU against the virtual network. Coverage includes full DORA, lost OFFER/ACK, NAK restart, repeated NAK, silence, foreign transactions, malformed/truncated packets, wrong identity/source fields, mismatched ACK address/server, invalid leases, arbitrary option ordering, padding, large unknown options beyond 576 bytes, optional ACK settings, zero-length UDP datagrams, oversize rejection, inspection and pending-receive cancellation. Existing TCP tests continue to exercise version 1.

`DhcpScreenAndEthernetTests` verifies native tile text and pixels, visible progress/failure states, variable-length decimal addresses, framing/checksums, zero-checksum IPv4 UDP, VLAN decoding and complete guest acquisition across Ethernet frames. `DhcpWindowTests` exercises the live Windows display and cancellation while closing; screenshots are generated under ignored `artifacts/windows-player/dhcp-*.png`. No live LAN packets are sent by these tests.

```powershell
dotnet test GB.Emulator.Tests -c Release --filter FullyQualifiedName~DhcpExperimentTests
```


## Rebuilding the SDK firmware

Run `./Samples/NetworkDhcp/Build.ps1 -RgbdsBin <RGBDS-0.9.0-directory>`, then rebuild the emulator.
The build uses the normal compiler CLI, a reusable SDK font, and explicit constrained C# sources.
`GB.Roms/NETWORK-DHCP.gb` is generated from that source and embedded by GB.Experiments. Runtime
launches and published Windows builds do not depend on RGBDS, Roslyn or an installed .NET SDK.
The configured compiler tests rebuild it and compare every byte to the embedded firmware input.
The result mailbox remains at `$C000..$C05F`; compiler storage starts at `$C100`.
The guest publishes completion only after the final screen has been drawn, then parks in the
compiler startup's idle loop. DHCP acquisition and status drawing both execute inside the guest.
