# Build, release and diagnostic artifacts

The [build workflow](../.github/workflows/dotnet.yml) runs on pull requests and
pushes to `master`, version tags, and manual dispatches. GitVersion supplies the
version used by assemblies and package names. Only successful `master` pushes
create a GitHub release; PRs, tags and manual runs upload Actions artifacts.

## Applications

Extract an entire runtime folder from the Actions artifact, or the complete
runtime-specific release ZIP. These are self-contained applications.

| Actions artifact | Runtime folders | Release ZIP stem |
| --- | --- | --- |
| `windows-<version>-windows` | `win-x64`, `win-arm64` | `windows-<version>-<runtime>` |
| `experiments-windows-<version>-windows` | `win-x64`, `win-arm64` | `experiments-windows-<version>-<runtime>` |
| `console-<version>-windows` | `win-x64`, `win-arm64` | `console-<version>-<runtime>` |
| `debugger-<version>-windows` | `win-x64`, `win-arm64` | `debugger-<version>-<runtime>` |
| `link-server-<version>` | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64` | `link-server-<version>-<runtime>` |
| `link-server-container-<version>` | One source/build-context ZIP | `link-server-<version>-container` |

The player includes its ordinary window and `--desktop` 3D mode, the public
`LINK-LOUNGE.gb` demo, and the link/Tetris guides. Model attribution remains in
`Desktop/Assets/ATTRIBUTION.md`; the bundled 3D model is **CC BY-NC 4.0**.
The [link server packages](link-server.md) include a README and both multiplayer
guides. Linux ZIP users must apply `chmod +x GB.Link.Server`. No private ROMs,
local artwork, certificates, tokens or development probe output are packaged.

The container ZIP is built from explicitly selected committed backend source,
the Dockerfile and Compose files. It is a portable Docker build context, not a
prebuilt image or a published registry tag. Extract it and run
`docker compose up -d --build`.

## Validation and diagnostics

The Windows job builds the solution, tests compiler/PyBoy, emulator, hosting,
player and experiments, and requires nonempty TRX reports with zero skipped or
inconclusive tests. It also smoke-builds a C# sample with pinned RGBDS. A Linux
job builds the relay container and runs its protocol health check as a non-root,
read-only container. Releases require both jobs to succeed.

`test-diagnostics-<run-id>-<attempt>` uploads available TRX reports and synthetic
or public-demo player/link-monitor screenshots even when the Windows tests fail.
`container-diagnostics-<run-id>-<attempt>` retains relay logs and container
inspection output. Both diagnostic artifacts are retained for 14 days and are
not release assets. Build failures before a report exists may leave only a
partial artifact; use the workflow log to diagnose those failures.

Full cluster simulations/SSH tests are excluded from normal runs; enable
`run_cluster_integration` on manual dispatch to run them. Private Tetris ROM
probes and `Tools/DesktopPerformanceProbe` remain manual, outside the solution
and CI. Audio/GPU performance depends on an interactive local machine; the
pipeline does not run a large multiplayer or performance simulation.

Linux dependency-submission jobs can restore Windows reference packs through
`Directory.Build.props`; GUI builds and releases still run on Windows. SDK
template project files use a `.csproj.template` suffix in the source package so
repository dependency scans do not treat them as consumer projects. `dotnet new`
renames them to ordinary `.csproj` files when generating a game.
