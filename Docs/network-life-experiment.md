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
asynchronous simulation are future extensions. Remote worker hosts are supported through SSH placement below.

## Several hosts

The coordinator can place worker groups on Windows or Linux hosts through OpenSSH. Native machines
still run the embedded Life ROM and return all simulation cells through their cartridge TCP interface.
SSH runs the worker command and forwards each remote loopback cartridge socket to its coordinator
collector. Control/inspection uses the SSH exec channel; simulation bytes use separate TCP forwarding
channels. Both travel inside the encrypted SSH connection. Collectors remain loopback-only, with no
LAN collector ports, Npcap, DHCP lease, or extra service specific to SharpBoy.

1. Build/deploy the **same revision** of `GB.Experiments` to each worker machine. Use the published
   Windows experiment package's worker files, or publish the headless executable for the remote OS:

   ```powershell
   dotnet publish GB.Experiments -c Release -r win-x64 --self-contained true -o artifacts/life-worker/win-x64
   dotnet publish GB.Experiments -c Release -r linux-x64 --self-contained true -o artifacts/life-worker/linux-x64
   ```

   Copy the complete output directory. On Linux, ensure the `GB.Experiments` apphost is executable.
   Place it at a path without spaces or shell syntax, such as `C:/SharpBoy/GB.Experiments.exe` or
   `/opt/sharpboy/GB.Experiments`. Remote command launching supports POSIX shells and Windows OpenSSH's
   default `cmd.exe` shell. A custom PowerShell SSH shell needs a compatible exec wrapper/command setup.
   No .NET installation is needed for a self-contained deployment. Firmware checksums are checked
   before native machines start; deploy every host together when the worker protocol changes too.

2. Configure working SSH key authentication and known host keys from the coordinator. Use SSH aliases
   for usernames, nonstandard SSH ports, identity files and jump hosts. Remote SSH servers must allow
   remote TCP forwarding and exec sessions. Confirm with `ssh gameboy-a C:/SharpBoy/GB.Experiments.exe --life-worker-group`;
   the worker waits for configuration, so Ctrl+C exits this check. The coordinator uses batch mode,
   strict host-key checking, forwarding-failure detection and keepalives. It never prompts for passwords
   or accepts unknown host keys automatically. Only the SSH server port needs to be reachable.

3. Copy [hosts.example.json](../Samples/LifeCluster/hosts.example.json), then edit its destinations,
   executable paths and node counts. The example assigns **4 local + 6 Windows + 6 Linux nodes** to a
   4x4 world. Omit `Destination` for a local child-process group. `Nodes` must total the grid size.
   `Processes` specifies groups on that host; `--processes` is overridden when `--hosts` is present.
   There may be at most 32 groups total, with at most 128 nodes per SSH group. `FirstPort` reserves
   `Nodes` remote loopback ports starting at that number; choose free ranges, and distinct ranges
   for concurrent runs or repeated entries on one physical host. Aliases for the same physical host
   must also use distinct ranges. A binding failure aborts the run instead of connecting to another job.

   ```powershell
   # Visual cluster, with the same native LCDs, paging and inspectors.
   dotnet run --project GB.Experiments.Windows -c Release -- --life --columns 4 --rows 4 --hosts hosts.json

   # Headless coordinator and a collected result/report.
   dotnet run --project GB.Experiments -c Release -- --life --columns 4 --rows 4 --generations 20 --hosts hosts.json --report artifacts/life-cluster/distributed.json
   ```

   The visual lab also has a **Hosts JSON** field under world configuration. Apply retains the chosen
   host file. Optional top-level `SshConfigFile` selects a dedicated OpenSSH config; relative paths are
   resolved beside the host JSON. Without it, OpenSSH uses its ordinary user configuration.

Inspectors identify the actual machine hostname and worker PID. Reports include `Metrics.Locations`
for local/remote host and PID pairs, while `WorkerProcessIds` retains local native worker PIDs only;
PIDs on different hosts are separate namespaces. Remote CPU and memory come from worker-side samples,
rather than the coordinator's SSH client processes. Samples arrive roughly once per second and at
shutdown. Peak RAM remains a sum of per-worker peaks, not simultaneous cluster usage.

The coordinator is the single barrier owner. A host failure aborts the generation and shuts down the
remaining groups; there is no partial-generation commit, automatic retry, host discovery, deployment
or recovery/checkpoint scheduler. Graceful stop drains worker snapshots before closing the forwarded
collectors, then waits for SSH exit. On connection loss, worker stdin EOF cancels the remote group;
the existing ten-minute worker deadline is the final limit if a broken SSH server never delivers EOF.

### SSH transport smoke test

```powershell
dotnet build GB.Experiments -c Release
python -m venv artifacts/toolchains/life-ssh
artifacts/toolchains/life-ssh/Scripts/python.exe -m pip install -r Tools/LifeCluster/requirements.txt
artifacts/toolchains/life-ssh/Scripts/python.exe Tools/LifeCluster/ssh_smoke.py
```

The test starts a temporary key-authenticated SSH server on loopback and uses the real OpenSSH client.
It compares six native nodes across two SSH aliases plus local placement with a local reference run,
checks remote process identities/cleanup, then drops a connection and verifies barrier failure and
worker shutdown. Keys/config/reports remain in ignored `artifacts/life-cluster/ssh-smoke`; user SSH
files, services and firewall rules are untouched. On Linux the venv interpreter is `bin/python`.
This verifies encrypted exec/forwarding and process lifetimes on one machine; it does not claim a
physical multi-host LAN run. The normal .NET suite also covers placement validation, local grouped
equivalence, firmware mismatch and cancellation.

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
