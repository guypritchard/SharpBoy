# Parallel Game Boys, one Mandelbrot image

```powershell
dotnet run --project GB.Experiments.Windows -c Release -- --cluster
dotnet run --project GB.Experiments.Windows -c Release -- --cluster --workers 16
dotnet run --project GB.Experiments.Windows -c Release -- --cluster --workers 32
dotnet run --project GB.Experiments.Windows -c Release -- --cluster --columns 5 --rows 4 --iterations 48 --row-delay-ms 0
dotnet run --project GB.Experiments.Windows -c Release -- --cluster --config Samples/NetworkFractal/lab.json
```

Or launch `GB.Experiments.Windows` without arguments and choose **Parallel Mandelbrot**. The lab starts fresh machines running its embedded
SDK firmware; it does not use the currently loaded game ROM. No Npcap or RGBDS installation is
needed to run it.

The native Windows dashboard shows enlarged Game Boy LCDs. Edit **Columns**, **Rows**, **Iterations**
and **Row delay ms** under **Configure the next run**, then choose **Apply & run**. This validates the
settings and starts a new lab, closing and cancelling the previous run. Each Game Boy owns
one tile of a Mandelbrot image. Watch its real 160×144 emulated LCD fill in, compare progress and iteration
counts, then inspect the assembled picture in the collector. **Run again** starts fresh machines;
**Stop** cancels the current run and closes its sockets. Runs do not overlap.

## Why this workload

Mandelbrot pixels are independent, so a picture is easy to split between workers. Areas outside
the set escape quickly; interior/boundary areas need more iterations. That makes unequal work
visible even though every node receives exactly 768 pixels. The final image is a useful correctness
check, and the per-pixel values support more detailed analysis.

Each node calculates 32×24 pixels. Example layouts are:

| Workers | LCD grid | Collected image | Pixels |
| --- | --- | --- | --- |
| 9 | 3×3 | 96×72 | 6,912 |
| 16 | 4×4 | 128×96 | 12,288 |
| 32 | 8×4 | 256×96 | 24,576 |

All sizes sample the same complex-plane region. The guest uses remainder accumulation to scale
coordinates to each grid without multiplication/division operators. Numeric precision remains Q5;
larger configurations add samples/work, so their elapsed times are not a fixed-work speed-up comparison.
The 8×4 output has a wider pixel aspect than the square grids. Coordinates use Q5
fixed-point arithmetic (scale 32), with **24 escape iterations by default** (configurable from 1–64). This intentionally coarse
integer approximation produces repeatable, recognisable four-shade imagery on a DMG. It is not a
floating-point reference rendering. The default nine-node image contains 6,912 pixels and 67,078 iterations.

### Configuration

| Setting | Range / behaviour |
| --- | --- |
| Columns / Rows | Positive whole numbers; their product is the Game Boy count, up to 64 |
| Iterations | 1–64; executed by the guest and used by the independent validator and LCD palette |
| Row delay ms | 0–1000; acknowledgement pacing, with 0 for no deliberate delay |
| `--workers N` | Any count 1–64; chooses an exact factor grid automatically |

Use **Load…** and **Save…** to reuse JSON configurations. Loading fills the editors; **Apply & run**
starts those settings. **Run again** repeats the current running configuration. Large grids scroll
instead of shrinking every screen beyond readability. Tile resolution, fixed-point precision and
transport remain 32×24, Q5 and localhost TCP.

```json
{
  "Columns": 5,
  "Rows": 4,
  "Iterations": 48,
  "RowDelayMs": 0
}
```

CLI overrides are applied after `--config`. Use either `--workers` or explicit `--columns` / `--rows`,
rather than combining them. Unknown JSON fields, invalid ranges and non-integer values are rejected.
The separate Windows experiments package includes `cluster-settings.json`; launch with
`--cluster --config cluster-settings.json` to use it. Source builds include the example at
`Samples/NetworkFractal/lab.json`.

The guest source uses a square table that it builds itself, additions/subtractions and an explicit
divide-by-32 scaler. No multiplication/division intrinsics, new compiler operators or precomputed
fractal artwork are introduced. All fractal arithmetic executes as SM83 instructions. The host
validates each returned pixel against a separately written multiply/divide reference and assembles
the returned values; it does not render tiles on behalf of the Game Boys.

## Inspect a Game Boy

Click any individual LCD (or focus it and press Enter/Space) to open that machine's
resizable inspector. Multiple machines can be inspected side by side. Clicking the same LCD
again brings its existing inspector forward. The enlarged native screen updates live.

The inspector shows tile/row progress, returned pixels, work totals, SM83 registers, LCD scanline,
clock cycles, cartridge identity and RAM capacities. Measured speed is emulated clock cycles per
wall-clock second, compared with the DMG's nominal 4.194304 MHz. It includes network waits and
host scheduling; it is not host CPU utilization. Telemetry is sampled around four times per second
by the owning emulator task and published as detached snapshots to the UI.

WRAM non-zero bytes describe data contents, **not allocated memory**: zero-filled variables still
occupy memory. These native ROMs have static storage and no managed heap. The memory section
lists WRAM/VRAM/HRAM capacities, ROM size and declared cartridge RAM separately. Cartridge network
buffers are shown separately from guest RAM.

Network details include actual localhost TCP endpoints, successful application payload bytes
sent/received, cartridge connection status and errors. Counters exclude TCP/IP/Ethernet headers.
The last endpoints and final inspection remain visible after completion; the socket is then closed.
JSON exports include each node's final inspection. Run again resets the inspectors for the new
machines; closing the dashboard closes all its inspectors.

## Collect the values

After completion, click **Collect values** and select a JSON filename. Four files are written:

| File | Contents |
| --- | --- |
| `name.json` | Dimensions, precision/transport, wall time, numeric pixel array and all node statistics |
| `name.pixels.csv` | One row per pixel: node ID, global x/y and escape-iteration count |
| `name.nodes.csv` | Node/tile coordinates, pixel count, work total and emulated cycles |
| `name.image.png` | The assembled image at the selected layout's native resolution |

The CSV files open in Excel. Work totals are deterministic. Wall time and emulated cycle totals
depend on scheduling and include polling/network waits; they are not isolated CPU-time benchmarks
and do not demonstrate a speed-up. The visual run intentionally paces row acknowledgements by
90 ms so progress remains visible. Display scaling uses nearest-neighbour filtering.

## Execution and protocol

`FractalCluster` launches independent `Gameboy` / `EmulationHost` / cartridge instances on
thread-pool tasks. Each task serializes its own CPU stepping and I/O publication. Nodes use the
existing host-offloaded localhost TCP transport and shared `GameNetwork` target-source helpers.
`FractalClusterWindow` receives immutable guest frames and row messages through thread-safe queues;
only its dispatcher accesses WPF images and models.

The iteration limit is supplied through the cartridge's read-only bootstrap byte `A038`; firmware
rejects values outside 1–64. Each node sends the existing 16-byte `GBW1` HELLO. The coordinator sends a 16-byte `GBW1` job with
kind 4, job ID 1, tile column at bytes 8–9 and tile row at bytes 10–11, plus grid columns at bytes
12–13 and grid rows at bytes 14–15 (all little-endian). Coordinates must be within the supplied grid.
The firmware draws each completed row through native VRAM writes and emits a 48-byte `GBF1`
record:

| Bytes | Meaning |
| --- | --- |
| 0–3 | ASCII `GBF1` |
| 4 | Node ID, 1–9 |
| 5 | Local row, 0–23 |
| 6–7 | Tile column and tile row |
| 8–9 | Sum of that row's iteration counts, little-endian |
| 10–15 | Opaque job metadata, echoed in the acknowledgement |
| 16–47 | 32 raw iteration counts, each 0 up to the configured limit |

The coordinator validates identity, order, coordinates, every pixel and the work counter, then echoes
the first 16 bytes as an acknowledgement. Firmware validates it before proceeding. TCP records use
exact-length/partial-read handling, not assumptions about packet boundaries. After the last ACK,
the guest closes its socket and publishes completion. Cancellation closes all machines and
transports; a failed node cancels the run. There is a three-minute experiment deadline. This lab
supports configurable rectangular grids up to 64 nodes; work stealing and task reassignment are future extensions.

Guest RAM reserves `C080..C0AF` for records and `C400..C501` for its square table. Compiler storage
starts at `C800`. Pixels appear at LCD position (16,24), with each logical pixel drawn as a 4×4
block into 192 mutable background tiles; a separate blank tile fills the surrounding screen.
LCD-off writes are bracketed by VBlank synchronization through the SDK.

The lab displays native frames directly with nearest-neighbour scaling and does not load 3D assets.
The separate desktop player retains its 3D model presentation. No private game ROM or external artwork
is required for the lab.

## Rebuild and capture

```powershell
./Samples/NetworkFractal/Build.ps1
dotnet test GB.Emulator.Tests -c Release --filter FullyQualifiedName~FractalClusterTests
dotnet test GB.Experiments.Windows.Tests -c Release --filter FullyQualifiedName~FractalClusterWindowTests

# Save the dashboard plus collected data and close after completion.
dotnet run --project GB.Experiments.Windows -c Release -- --cluster --capture artifacts/fractal-cluster/dashboard.png --capture-and-close
```

Rebuilding firmware requires RGBDS 0.9.0 and the compiler. Runtime uses the embedded native ROM.
Compiler tests reproduce the ROM byte-for-byte. Execution tests compare every returned pixel and
the matching native LCD pixels at preset and custom sizes/iteration limits; Windows tests exercise live LCD views, cancellation,
restart, data collection and PNG output.
