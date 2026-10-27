# Local TCP/UDP telemetry and a native Game Boy cluster

The next step after DHCP is a working socket-offload experiment. Guest firmware is constrained C#
compiled by the SharpBoy SDK/compiler to real SM83 instructions. DHCP parsing, telemetry record
construction, job decoding and range-sum arithmetic execute on the Game Boy. The cartridge backend
uses operating-system TCP/UDP sockets. There is no CLR inside the emulated machine.

## Run telemetry

From the repository root, either command starts a receiver, runs the firmware, verifies the received
record against guest RAM and exits. No Npcap or administrator access is needed:

```powershell
dotnet run --project GB.Experiments -c Release -- --network-cartridge --telemetry --udp
dotnet run --project GB.Experiments -c Release -- --network-cartridge --telemetry --tcp
```

The ROM acquires a virtual DHCP lease, draws its existing native status screen, closes port 68,
opens the selected application transport and emits its address, server, mask, router, DNS and lease
duration. This is one telemetry record per run, rather than a periodic stream. The packet contains
the acquired values, not values synthesized by the receiver.

The DHCP address (by default `192.0.2.42`) is **data in the telemetry payload**. The actual socket uses
`127.0.0.1` and an ephemeral source port. This local experiment does not claim that address on your
home network. The virtual DHCP backend never binds real ports 67/68. Its application path opens a
real loopback UDP socket after the guest closes its DHCP port. TCP uses a host-configured receiver.

### Use a separate receiver

Start this in one PowerShell terminal; it prints one record and exits:

```powershell
dotnet run --project GB.Experiments -c Release -- --network-cartridge --receive --udp --port 9000
```

Send from another terminal:

```powershell
dotnet run --project GB.Experiments -c Release -- --network-cartridge --telemetry --udp --receiver-port 9000
```

Use `--tcp` in **both** terminals to try the stream transport. Ctrl+C stops either command. When
using an external receiver, send completion does not prove application-level receipt. The default
single-command experiment verifies receipt itself. A receiver can also be your own application;
use the binary layout below. Receivers bind only to loopback and process one record per invocation.

## Run a cluster

```powershell
dotnet run --project GB.Experiments -c Release -- --network-cartridge --cluster --tcp --workers 4 --count 1000
dotnet run --project GB.Experiments -c Release -- --network-cartridge --cluster --udp --workers 4 --jobs-per-worker 8 --count 1000

# Deliberately split coordinator TCP writes into 3-byte chunks.
dotnet run --project GB.Experiments -c Release -- --network-cartridge --cluster --tcp --tcp-fragment-size 3
```

Each node owns an independent `Gameboy`, `EmulationHost`, device, memory and socket. Tasks run
independently on the thread pool, with serialized access inside each node. The coordinator partitions
`0 .. count-1` into contiguous jobs and sends each job's starting value and count. The SM83 sums the
range in a 16-bit accumulator and sends the result back. The coordinator validates the echoed node,
job and operands, checks the result against an independent arithmetic formula, and reduces all
shard results modulo 65,536. It does not calculate results on behalf of the workers.

Defaults: four workers, four jobs per worker, 1,000 inputs, TCP. The expected total is **40,748**
(`499,500 mod 65,536`). Empty shards are valid. Supported limits are 1–16 workers, 1–32 jobs per
worker and 0–65,535 inputs. Each worker has a unique node ID and a separate loopback endpoint;
the workers do not need DHCP to communicate on this local socket network.

TCP is a byte stream, not a message transport: firmware collects partial reads until its fixed-size
record is complete. The stop-and-wait protocol allows one outstanding job per worker. UDP preserves
record boundaries and checks the coordinator's source endpoint. The coordinator can retransmit a
job up to five times with a two-second response window. Jobs are pure range sums, so re-execution
after a lost reply is harmless; stale/duplicate reply IDs cannot complete a different job. This is
bounded experimental recovery, not a general exactly-once messaging guarantee. Initial HELLO loss,
node restart, persistent deduplication and job reassignment remain future work.

This range-sum runner is a **headless cluster** intended for protocol/parallel-processing experiments. It makes no
speed-up claim: emulation and messaging overhead can greatly exceed the cost of this small job.
For a visual workload, the [nine-Game-Boy Mandelbrot lab](visual-gameboy-cluster.md) displays native
LCDs, assembles returned pixel values and exports data. LAN discovery and attachment of arbitrary
game ROMs to a cluster remain separate work.

## Cartridge extension

Existing TCP-only version-1 and UDP-only version-2 instances retain their buffer layouts. A new
dual-transport constructor exposes version 2 with capability bits `A009 = 3`:

| Address | Access | Meaning |
| --- | --- | --- |
| `A00A` | Read/write | 0 = UDP, 1 = TCP; change only while closed, with no pending operation or unread data |
| `A030` | Read-only | Node ID |
| `A031` | Read-only | Initial application transport |
| `A032..A035` | Read-only | Coordinator/receiver IPv4 bytes, network order |
| `A036..A037` | Read-only | Coordinator/receiver port, little-endian |
| `A038..A03F` | Read-only | Firmware-specific bootstrap settings; fractal firmware uses `A038` for its iteration limit |

Both transports use the version-2 1 KiB buffers (`A100..A4FF` transmit, `A500..A8FF` receive).
Commands remain connect/open, send, receive, close and acknowledge. The transport cannot change
while an operation is pending. TCP EOF is a zero-length read; an empty UDP datagram remains a valid
received message until acknowledged. TCP destinations remain supplied by the host. Local UDP
accepts only `127/8` destinations with nonzero ports; it cannot accidentally broadcast on the LAN.

The shared `GB.Sdk/Target/GameNetwork.cs` supplies native-source command, buffer, word encoding,
configured-open and fixed-record receive helpers. Include it explicitly when compiling firmware.
The helpers implement application framing, not a guest IPv4/TCP stack. Framing follows the
distinction between UDP datagrams ([RFC 768](https://www.rfc-editor.org/rfc/rfc768)) and TCP's reliable
byte stream ([RFC 9293](https://www.rfc-editor.org/rfc/rfc9293)).

### Telemetry record: 32 bytes

| Offset | Value |
| --- | --- |
| 0..3 | ASCII `GBT1` |
| 4 | Node ID |
| 5 | Transport: 0 UDP, 1 TCP |
| 6..7 | Payload length: 24, little-endian |
| 8..11 | Acquired IPv4 |
| 12..15 | DHCP server IPv4 |
| 16..19 | Subnet mask |
| 20..23 | Router IPv4 |
| 24..27 | DNS IPv4 |
| 28..31 | Lease duration, unsigned 32-bit network order |

IPv4/mask fields are four raw network-order bytes. The entire record is one UDP datagram or one
32-byte application record on TCP. The guest's result is at `C060`, and its outgoing record is at
`C070..C08F`. Result 1 means send and close completed; 255 means failure.

### Worker record: 16 bytes

| Offset | Value |
| --- | --- |
| 0..3 | ASCII `GBW1` |
| 4 | Node ID |
| 5 | Kind: 0 HELLO, 1 JOB, 2 RESULT, 3 STOP |
| 6..7 | Job ID, little-endian |
| 8..9 | Range start, little-endian |
| 10..11 | Range count, little-endian |
| 12..13 | Result modulo 65,536, little-endian |
| 14..15 | Reserved, zero |

RESULT echoes the JOB identity and operands. STOP closes the socket and publishes success at
`C060`. The current packet occupies `C080..C08F`; compiler-managed WRAM begins at `C100`.

## Build, test and next steps

```powershell
./Samples/NetworkTelemetry/Build.ps1
dotnet test GB.Emulator.Tests -c Release --filter FullyQualifiedName~LocalNetworkStackTests
```

The build script uses RGBDS 0.9.0 and regenerates all three native ROMs: DHCP, telemetry and worker.
Published experiment binaries embed the ROMs and do not compile C# at runtime. Compiler tests
rebuild each ROM and compare its bytes with the checked-in image. Transport tests exercise real
localhost UDP/TCP, fragmented streams, 16-bit overflow, empty jobs, duplicate work after a lost UDP
result, wrong worker identities, cancellation and compatible mode switching. No tests send LAN traffic.

For home-network packet-level operation the next stage is ARP/neighbour resolution, applying the
leased IP/mask/gateway to the Ethernet path, address-conflict detection and lease renewal. The
existing Npcap backend still supports DHCP acquisition only. A true SM83 TCP stack would additionally
need its own connection state machine, sequence numbers, retransmissions, windows and congestion
control. Neither is silently substituted by localhost socket offload here.

For cluster work, the next useful changes are more job operations, node discovery, task deadlines,
worker failure/reassignment and an inspection UI. Reliable commands over TCP with optional UDP
telemetry are already possible using the two transports, but this first runner selects one transport
for its worker protocol for each invocation.
