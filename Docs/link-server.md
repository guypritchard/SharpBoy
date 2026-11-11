# SharpBoy headless link server

Run one service for a shared multiplayer lobby. Game rooms use cartridge header
titles; random Tetris opponents also require an identical ROM SHA-256. The
server has no desktop UI or audio device. It relays serial bytes over TCP/TLS,
not ROM contents. This is experimental byte-level linking, not cycle-perfect
remote cable timing.

## Download and run

Use a `link-server-<version>-<runtime>.zip` release asset, or the matching folder
in the Actions `link-server-<version>` artifact. Extract the complete folder.
The Windows x64/ARM64 and glibc Linux x64/ARM64 packages include .NET; no SDK is
needed. Linux packages require a compatible glibc distribution with the usual
.NET native dependencies; Alpine/musl is not included.

Windows PowerShell:

```powershell
.\GB.Link.Server.exe --bind 0.0.0.0 --port 8766
```

Linux (ZIP extraction may not preserve the executable bit):

```sh
chmod +x GB.Link.Server
./GB.Link.Server --bind 0.0.0.0 --port 8766
```

From a second terminal, use `GB.Link.Server.exe --health-check --port 8766`
or `./GB.Link.Server --health-check --port 8766`. A successful protocol probe
exits with code 0. Without `--bind`, the standalone executable listens only on
loopback. Clients on this PC use `127.0.0.1:8766`; LAN clients use the server's
hostname/IP and need TCP port 8766 reachable through the host firewall.

In the Windows player press **Ctrl+L**, choose the address/port and **Connect**.
An unavailable or dropped server is automatically retried. **Leave lobby**
cancels retries. The Link Boy lamp is amber when joined to the server, red when
paired and dark offline. A reconnect creates a fresh cable session: reload an
interrupted multiplayer ROM before starting another match.

## Optional headless Tetris players

Supply your own supported Tetris ROM; no commercial ROM is bundled, uploaded,
or copied into a container. Set the environment before starting the executable:

```powershell
$env:LINK_TETRIS_ROM = 'C:\Games\Tetris.gb'
$env:LINK_TETRIS_PLAYERS = '4'
.\GB.Link.Server.exe --bind 0.0.0.0 --port 8766
```

```sh
LINK_TETRIS_ROM=/games/tetris.gb LINK_TETRIS_PLAYERS=4 ./GB.Link.Server --bind 0.0.0.0 --port 8766
```

The pool supports 1–12 machines, cycles four playing presets and randomly
assigns ready opponents to matching clients. No second player window opens.
Connect first, then select **2PLAYER** in Tetris. See [tetris-ai.md](tetris-ai.md)
for the supported ROM hash, presets and manual verification probes.

## Container deployment

The `link-server-<version>-container.zip` release asset contains the minimal
source/build context, Compose files and docs. Extract it, then run:

```sh
docker compose up -d --build
docker compose ps
docker compose logs --follow
```

This builds a local image; the ZIP is not an OCI image and no registry image is
published. Docker downloads the .NET build/runtime base images. For the pool,
set `TETRIS_ROM_PATH` to an absolute path on the Docker host, then use
`docker compose -f compose.yaml -f compose.tetris.yaml up -d --build`.
The ROM is a read-only runtime mount.

The default relay is plain TCP for local/LAN use. For remote hosting, configure
a trusted TLS certificate and join token with `compose.tls.yaml` as described
in [link-lobby.md](link-lobby.md). It needs TCP hosting, not an HTTP-only proxy.
The non-root service supports read-only containers, a health check and graceful
shutdown. Lobby state lives in memory; run a single authoritative instance.

## Configuration

| Setting | Default / purpose |
| --- | --- |
| `LINK_BIND` | `127.0.0.1` standalone, `0.0.0.0` in the container |
| `LINK_PORT` / `PORT` | `8766`; `LINK_PORT` wins |
| `--bind`, `--port` | Override the environment |
| `LINK_TETRIS_ROM` | Optional local ROM path |
| `LINK_TETRIS_PLAYERS` | `4`, valid range 1–12 |
| `LINK_BOT_HOST` | Optional address/hostname used by the pool to join the relay |
| `LINK_TLS_PFX` | Optional readable server certificate/private-key PFX path |
| `LINK_TLS_PASSWORD`, `LINK_ACCESS_TOKEN` | Optional secrets; each also accepts a `_FILE` path instead |
| `LINK_PUBLIC_PORT` | Compose host port, default `8766` |

The TLS Compose override uses host paths `LINK_TLS_PFX_FILE`,
`LINK_TLS_PASSWORD_FILE` and `LINK_ACCESS_TOKEN_FILE` for read-only secret mounts.
These host-side variables differ from the executable's PFX path variable.

Stop with Ctrl+C, SIGTERM, or `docker compose down`. The pool is retired and
connections drained; an in-progress multiplayer game cannot resume across a
server restart.
