# Link cable lobby and service

**Ctrl+L** opens the link lounge from the Windows player or desktop Game Boy.
In the 3D view, clicking the EXT connector, cable or monitor also opens it.
Click the monitor or press Ctrl+L again to hide the lounge while keeping the cable.

With the cable visible, **drag the Game Boy shell or the link monitor independently**.
Ctrl+drag rotates the Game Boy and its seated plug; the monitor stays in place.
The cable jacket retains a broad curved shape and supports its own weight,
so stiffness reduces its resting droop. It follows both strain reliefs, flexes under gravity, swings and settles
after movement, and straightens as the ends move apart. Its finite length limits
dragging at full reach. The wire and plugs have continuous geometry, including
a recessed socket on the hub. Scroll still changes the model scale; double-click
the LCD for close-up and return without losing the hardware positions.

The two objects share a transparent desktop workspace. A fixed 120 Hz particle
solver applies material-length constraints, curvature memory across short and
long spans, damping and conservative housing
collision bounds. Cable mesh updates are capped at 30 Hz except while attaching
a moving endpoint; settled cords sleep until an endpoint moves. This is a presentation simulation: it does not alter serial
timing or unplug a network match, and does not simulate cable self-collision,
twist, friction against the desktop, or dragging objects between monitors.
After contact/curvature solving, bounded length-only projection reduces residual
stretch without changing the jacket's bending stiffness.

The cable-end **LINK BOY** instrument replaces the old four-port hub face. Its
160×144 four-shade LCD shows READ and WRITE byte rates in separate graphs, plus
36 seconds of cable connection history. Samples advance every 250 ms, including
idle and offline periods. The red LINK lamp means a partner is paired; bright amber
means connected to the lobby and waiting. The lamp is dark when offline. The LCD also spells out the state.
Its rounded LCD and grey bezel, purple/blue stripes, green pixels and diagonal
speaker slots follow the desktop Game Boy's design. The taller cream shell has
gently rounded top corners and the DMG's larger curved lower-right corner.

Rates count acknowledged serial payload bytes in each direction, excluding
lobby messages, heartbeats, framing and cancelled re-arms. DMG serial transfers
are full duplex, so successful read/write totals normally match. Reconnecting
resets the counter baseline without inventing a spike or discarding earlier
connection history. The monitor remains independently draggable and opens/hides
the lounge when clicked.

After you choose **Connect**, an unavailable or disconnected server is retried
after 1, 2, 4, 8 and then 15 seconds between attempts, even with the lounge hidden.
Each attempt uses the selected server, TLS/token, player and cartridge settings.
**Leave lobby**, closing the emulator or loading a different ROM cancels retries.
Invalid connection settings and TLS authentication failures require correction
before connecting again. Rejoining creates a fresh cable session; it cannot
restore a multiplayer game interrupted by a lost connection. Reload the ROM
before starting a new match if its serial state is no longer in sync.

## Desktop rendering performance

The linked desktop keeps a transparent workspace for dragging, but renders 3D
only inside padded bounds around the actual objects. An off-centre camera frustum
preserves perspective and desktop positions when that rectangle changes. The LCD
continues to publish on the emulation timer; cable motion has a separate 30 Hz
presentation timer. Per-byte network wakeups only advance/attach the machine,
while the lounge's 120 ms timer samples the monitor and updates visible controls.
Both LCDs use zero-wait bitmap locks: if WPF is still consuming a texture, the
next presentation retries with the latest pixels instead of blocking emulation.
Switching between the model and LCD close-up animates a live screen quad on
display-rendering events inside fixed window bounds. The native window changes
size only at the transition boundaries; the linked scene keeps its cropped
camera throughout the fade. The emulation/audio timer does no animation work.

Soft shadows use cached textures instead of a full-viewport blur shader. The
cable reuses vertex/normal/curve buffers, precomputes its ring coordinates, and
stops invalidating geometry when asleep. Static monitor geometry is frozen.
These paths also apply when Windows falls back to software rendering.

For an explicit desktop benchmark (outside the solution and CI):

```powershell
dotnet run --project Tools/DesktopPerformanceProbe -c Release -- artifacts/render-performance/probe.json 6
```

The probe plays the bundled native ROM and records clock throughput, guest and
uploaded LCD FPS, deferred uploads, composition events, render gaps, CPU time, allocations and viewport size
with and without the cable, including rotation. It runs phases sequentially and
mutes audio; it does not verify audio-device underruns or other ROMs' CPU load.
Run it alone so other tests/windows do not compete for rendering time.
Add `--transitions --audio` to exercise repeated mode changes with the real
Windows audio queue enabled and report underruns/discarded blocks. Audio-device
availability is included in the report; the default run remains muted.

Load a ROM, host a lobby or enter a server address, then invite another player
in the same cartridge room. The other player accepts the cable before it
attaches. Rooms use the normalised ROM-header title; related games with
different titles currently occupy different rooms. A ROM hash identifies the
revision but is not uploaded with ROM contents. The service holds names,
header titles, hashes, invitations and serial bytes in memory. A connected
cable keeps the ordinary Windows player running in the background, allowing
you to interact with player two without the inactive-window option pausing player one.

The bundled **Load multiplayer link demo** needs no private ROM. Start/Enter
selects internal-clock player one; Select/Space selects external-clock player
two. Press A/Z to increase the local score; the other LCD receives it over TCP.
For Tetris, see the [headless AI pool guide](tetris-ai.md). With a pool enabled,
connecting assigns a random ready server opponent with the same title and ROM
hash. Human invitations still require consent.

## Host in a container

From the repository root on a Docker host with Compose:

```sh
docker compose up -d --build
docker compose ps
docker compose logs --follow
```

Clients connect to the host's name/IP, **TCP port 8766**. `LINK_PUBLIC_PORT`
changes the published port. This is raw TCP, so a host must support TCP port
publishing/forwarding; an HTTP-only hosting platform cannot route it directly.
The default service is a relay. The optional headless Tetris pool runs silent
emulator instances with a privately mounted ROM. Neither mode requires a display.

Without Compose:

```sh
docker build -f GB.Link.Server/Dockerfile -t sharpboy/link-hub:local .
docker run -d --name sharpboy-link-hub --restart unless-stopped --read-only --tmpfs /tmp:rw,noexec,nosuid,size=16m --cap-drop ALL --security-opt no-new-privileges:true -p 8766:8766 sharpboy/link-hub:local
docker exec sharpboy-link-hub dotnet GB.Link.Server.dll --health-check
```

The image uses .NET 10 runtime, non-root UID 1654, a bounded health probe and
SIGTERM shutdown. Compose adds an init process and a 15-second stop grace.
The build context includes Core, Hosting, AI, Link and Server source, excluding ROMs,
private directories, certificates, build artifacts and desktop/model assets.

Build for another Linux architecture using Buildx, for example
`docker buildx build --platform linux/arm64 -f GB.Link.Server/Dockerfile -t <your-registry>/sharpboy-link-hub:<tag> .`.
Publish/load that image using your own registry or `docker save`/`docker load`.
`sharpboy/link-hub:local` is a local tag, not a published registry image.

## TLS and private access

The base Compose file is plain TCP for local/LAN use. For a remote service,
configure a PFX with a certificate trusted by the clients, and a join token.
The client TLS checkbox uses normal hostname and certificate-chain validation;
it has no insecure-certificate bypass.

Set these environment variables to readable local secret-file paths:

```text
LINK_TLS_PFX_FILE=<server certificate with private key.pfx>
LINK_TLS_PASSWORD_FILE=<text file containing PFX password>
LINK_ACCESS_TOKEN_FILE=<text file containing join token>
```

Then run `docker compose -f compose.yaml -f compose.tls.yaml up -d --build`.
The override mounts those files read-only. UID 1654 must have permission to
read them. Select TLS and enter the join token in each client's lobby.
The health probe pins the server's own configured certificate without adding
it to the client trust store. No certificate, password or token is committed.

The executable also supports `LINK_TLS_PFX`, `LINK_TLS_PASSWORD` and
`LINK_ACCESS_TOKEN`; password/token values may instead come from the matching
`_FILE` variable, but cannot use both forms simultaneously.

## Run without a container

```sh
dotnet run --project GB.Link.Server -c Release -- --bind 0.0.0.0 --port 8766
```

Without `--bind`, the executable listens on loopback. `LINK_BIND`, `LINK_PORT`
and `PORT` are also supported; command-line arguments take precedence.
`dotnet GB.Link.Server.dll --health-check` probes the configured local service.

## Protocol and limits

`GB.Emulator.Link` provides a reusable client/deferred serial adapter and a
server. Messages are length-prefixed JSON over ordered TCP, optionally TLS.
The server permits 32 players with separate bounded handshake slots and
bounded queues. Each accepted pair gets a session ID, and transfer IDs reject
stale replies. Rooms and pairings disappear on restart. Run one authoritative
service for a lobby; replicas do not share state.

The core has no socket dependency. An internal-clock byte takes 4,096 guest
cycles, then parks guest execution until the remote byte arrives. External
clock arming leaves the CPU free to execute, as real games use it while
reading menus and waiting for a master. External clock waits may be indefinite;
unanswered internal-clock transfers unlink after 15 seconds. Owner-thread
polling applies received bytes and serial interrupts. A background receive
notification queues work on the owning UI dispatcher, rather than waiting for
another display timer tick. The host retains its unspent, bounded cycle budget
across each wait. Re-arming an unchanged external-clock request preserves its
pending reply. Header and payload use a single socket write.

On cable loss, both desktop hosts pause instead of continuing a half-completed
multiplayer handshake with an apparently frozen LCD. Reload the ROM to restart
both players. Windows audio primes roughly 43 ms of PCM before playback and
re-primes after starvation; its queue remains bounded at eight blocks.

This is experimental byte-level linking: UDP, bit-level cable timing,
simultaneous-master collision behaviour, CGB fast serial, network rewind and
rollback are not implemented. The public SDK demo and the private Tetris
multiplayer path have been exercised; other commercial games need their own
compatibility checks.
