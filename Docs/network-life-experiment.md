# Networked living-world experiment

Conway's Game of Life (B3/S23) runs inside independent native SM83 Game Boys. Each ROM calculates
and draws a 32x24 territory. The coordinator routes neighbouring cells over TCP, validates the
returned cells against an independent whole-world reference, and assembles only received results.
No .NET runtime executes inside the guest.

## Run

```powershell
# Default: 16 machines, 80 ticks, 50 ms deliberate pacing, in-process.
dotnet run --project GB.Experiments.Windows -c Release -- --life

# 1,000 native machines, 16 automatically selected worker processes, 768,000 cells.
dotnet run --project GB.Experiments.Windows -c Release -- --life --columns 40 --rows 25 --generations 3 --tick-delay-ms 0

# Headless execution and a JSON scaling report, with explicit process grouping.
dotnet run --project GB.Experiments -c Release -- --life --columns 40 --rows 25 --generations 3 --tick-delay-ms 0 --processes 16 --report artifacts/life-cluster/scaling-1000.json
```

Launch `GB.Experiments.Windows` without arguments to choose **Living world** from its launcher.
The separate experiments package includes the worker executable and runtime files. No external ROM, artwork or network driver is required.

Use **Previous/Next** to page through groups of 16 native LCDs. Click an LCD, enter an ID beside
**Inspect GB**, or click a territory in the combined world to open that machine's enlarged screen,
registers, measured speed, memory information and actual localhost TCP endpoints/counters. Page
selection and open inspectors control which running workers publish LCD updates. Final native LCDs
are retained for every node, so off-page machines remain inspectable after their processes exit.
The UI allocates WPF bitmaps only for the visible page and open inspectors.

Use the editors and **Apply & run** to create a new configuration. **Run again** repeats the same
seeded world. **Stop** or Ctrl+C cancels all machines/sockets; worker stdin EOF also cancels its group.
The coordinator drains final inspection events, waits for child exit, and uses an eight-second
termination fallback for an unresponsive child. The UI and IPC run on separate execution contexts.

**Collect values** writes final 0/1 cells, settings, final node inspection, process/CPU/memory metrics
and population/timing history to JSON, a generation CSV, and a combined-world PNG. CLI `--report`
writes settings, per-node completion/cycles, process metrics and a final-world SHA-256 checksum.
Windows capture automation accepts `--capture path.png` and optional `--capture-and-close`.

| Setting | Range / default |
| --- | --- |
| Columns x rows | Positive dimensions, up to 1,000 machines total; default 4x4 |
| Generations | 1-1,000; default 80 |
| Tick delay | 0-1,000 ms; default 50, added after each committed generation |
| Seed | Signed 32-bit integer; default 314159, 22% initial population |
| Processes | -1 automatic, 0 in-process, or 1-32 child processes |
| Automatic grouping | Up to 64 nodes stays in-process; larger runs use roughly 64 nodes per child, at most 16 processes |
| World boundary | Toroidal: left/right and top/bottom wrap |
| Transport | One host-offloaded localhost TCP connection per native machine |

The experiment has a ten-minute deadline. At large counts, start with a few generations before
choosing a longer run. This is a synchronized correctness experiment, not a claim that 1,000 Game
Boys run at physical hardware speed or outperform one host processor. Live painting, fault injection,
remote worker hosts and asynchronous simulation are future extensions.

## State, transport and scheduling

The first tick initializes each guest with its full 34x26 tile/halo. The ROM then retains its own
32x24 interior in guest WRAM. Subsequent requests carry only **116 boundary cells**, including
corners: 34 top cells, 24 pairs of left/right cells, and 34 bottom cells. That reduces recurring
request payload from 884 to 116 bytes. Responses still carry all 768 new cells, for collection and
independent validation. A generation advances only after every territory passes validation.
A failed/disconnected/cancelled worker prevents partial-world advancement.

`LifeCluster` owns the committed global world and TCP collectors. `LifeProcessPool` launches bounded
`LifeWorkerGroup` processes; each runs private `NativeNetworkNode` machines. Bootstrap assignments,
display selections and detached inspections/native LCDs use redirected process pipes. **Simulation
cells always travel through the cartridge's TCP interface**, including when a worker shares the
coordinator process. The pipes do not carry simulation cell results. Wider node IDs also apply to
inspection callbacks, avoiding wrapping at node 256.

Every node task serializes CPU stepping, cartridge pumping and inspection. WPF consumes detached
frames and snapshots. Life disables automatic per-VBlank frame creation and captures selected LCDs
on demand at most four times per second, plus one final native LCD per node. Snapshot capture occurs
on the machine's owner task, including when ordinary host frame capture is disabled.

Life's scheduler **suspends the guest clock while an offloaded network operation is pending**. It
awaits detached transport work, then calls `Pump` on the owner task to publish the completion. It
does not spend host CPU executing guest polling loops during wall-clock network waits. Other
experiments retain their existing scheduling policy. Guest cycle totals therefore exclude those
parked intervals; measured cycles per wall-clock second still includes waiting/scheduling time.
These values are not hardware CPU utilization or cycle-accurate networking latency measurements.

Worker memory in the UI is sampled at the last committed generation. Exported peak worker memory
is the **sum of per-process peaks**, not a simultaneous cluster peak. Coordinator and worker CPU
seconds are reported separately; CPU seconds sum across cores and may exceed wall time. In-process
runs have zero separate worker-process memory/CPU; their work is included in coordinator metrics.

LCDs may temporarily show different generations while workers finish. Final inspections and native
LCD pixels remain available after disposal. The collected world advances only at complete barriers.

## Protocol v2

Messages start with a 16-byte header. Multi-byte numbers are little-endian.

| Bytes | Meaning |
| --- | --- |
| 0-3 | `GBL2` |
| 4-5 | 16-bit node ID, 1-1,000 |
| 6-7 | Generation: HELLO 0, ticks 1..N, finish N+1 |
| 8-9 | Territory width 32 for tick requests/results, otherwise 0 |
| 10-11 | Territory height 24 for tick requests/results, otherwise 0 |
| 12-13 | Payload bytes: initial request 884, later request 116, result 768, otherwise 0 |
| 14 | Kind: 0 HELLO, 1 tick request, 2 tick result, 3 finish |
| 15 | Reserved, zero |

Cells are 0/1 bytes. Initial halos and result interiors are row-major. The initial interior begins
at halo offset 35. Subsequent boundaries contain the top row (including corners), 24 left/right
pairs, then the bottom row (including corners). The coordinator validates identity, generation,
geometry, framing and every returned cell. Finish closes each guest socket and waits for its mailbox.
Cartridge bootstrap A030 supplies the low node-ID byte and A03F the high byte; existing byte-ID
firmware remains compatible within its smaller limits.

`GB.Sdk/Target/GameNetworkStream.cs`, compiled alongside `GameNetwork.cs`, supports version-2 TCP
cartridge buffers up to 1,024 bytes. It preserves unread bytes when TCP coalesces records and collects
split reads. It supports one TCP connection per guest run; earlier UDP stop-and-wait helpers are
unchanged. TCP/IP is offloaded to host sockets; these are not home-LAN DHCP devices.

Guest WRAM: command header C080..C08F; retained tile/halo C100..C473; output C480..C78F;
boundary staging C800..C873; compiler-managed storage from C900; reserved CPU stack DF80..DFFF.
Display assets are 193 blank tiles and a background map; native SDK instructions fill the pixels.
LCD writes occur with the LCD disabled after VBlank synchronization.

## Verification and measurements

```powershell
./Samples/NetworkLife/Build.ps1
dotnet test GB.Emulator.Tests -c Release --filter FullyQualifiedName~LifeClusterTests
dotnet test GB.Experiments.Windows.Tests -c Release --filter FullyQualifiedName~LifeClusterWindowTests
$env:GB_RGBDS_BIN = (Resolve-Path artifacts/toolchains/rgbds).Path
dotnet test GB.Compiler.Tests -c Release --filter FullyQualifiedName~VisualNetworkFirmwareRebuilds
```

Tests reproduce the ROM byte for byte, check independent blinker/glider behaviour, cross territory
seams and toroidal corners, compare returned/native-LCD cells, fragment and coalesce TCP records,
exercise in-process/grouped execution, verify IDs through 272 and child cleanup, and cancel/restart/
page/inspect/export the Windows lab. A 1,000-node UI construction test verifies that only 16 LCD
bitmaps are allocated initially, without starting any emulators.

Local smoke runs on 2026-10-26 completed three independently verified generations on 256 and
1,000 nodes. The 1,000-node 40x25 world contained 768,000 cells. The initial polling run took 86.7 s;
the I/O-awaiting run took 30.9 s with an identical final-world checksum, 16 worker processes, about
1.36 GiB summed worker-process peak memory and 80 MiB coordinator peak memory. These are local
observations under the machine's current load, not controlled scaling/speed-up benchmarks.
Reports are generated under ignored `artifacts/life-cluster/` using `--report`.
