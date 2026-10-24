# Hosting an emulator

`GB.Emulator.Hosting.EmulationHost` owns playback for one `Gameboy`. Console,
Windows player and debugger compose this shared host. The core implements the
hardware; the host implements playback policy; each UI implements presentation,
physical input and scheduling. No UI toolkit or platform audio dependency enters
`GB.Emulator.Core` or `GB.Emulator.Hosting`.

## Responsibilities and dependencies

| Component | Owns |
| --- | --- |
| `Gameboy` in `GB.Emulator.Core` | Its CPU/registers/flags, memory, devices, cartridge mapping, startup and machine snapshots |
| `EmulationHost` in `GB.Emulator.Hosting` | Load/reset/resume/pause, bounded execution, shared pacing, frames, audio routing, statistics and output lifetime |
| `EmulationInspection` | Detached read-only CPU, cartridge, hardware and memory snapshots |
| `EmulationDebugger` | Stepping, breakpoints, disassembly and bounded machine rewind |
| Console / Windows / debugger UI | File selection, key mapping, display/palette/scaling, window layout and scheduling |
| `WindowsAudioOutput` in `GB.Emulator.Core.Win32` | Bounded native waveOut queue implementing `IAudioOutput` |

CPU registers, flags, arithmetic operations, memory references and executable
opcode handlers belong to each CPU instance. Creating or resetting a second
machine cannot alter the first. Immutable opcode names/timing tables can still
be shared. The old unused opcode groups have been removed. Instruction handlers
execute through their owning CPU, which advances its own program counter.

The experiment worker pool still uses processes for isolation and bounded worker
lifetime. Separate processes are no longer required to isolate CPU state.

## Creating another UI

Reference `GB.Emulator.Hosting`. Compose a host, supply an optional audio output,
and implement `IVideoOutput.Present(VideoFrame)` using the UI toolkit of choice.
Keep keyboard/gamepad mapping and the file picker in that UI. The following is
the integration shape; `display` and `scheduler` are supplied by the new UI:

```csharp
using var host = new EmulationHost(audio: platformAudioOutput);
await host.LoadAsync(romPath);
host.Resume();

// On the host's serialized execution context:
host.SetButtonState(GameBoyButton.A, pressed);
host.Advance(elapsedSinceLastTick);

// On the presentation thread, using an appropriately synchronized handoff:
if (host.LatestFrame is VideoFrame frame)
    display.Present(frame);
```

The current console loop and WinForms timer execute the host and present frames
on their own thread. They use the same pacer, frame creation and statistics.
`Advance` never sleeps or runs an unbounded loop: catch-up is capped at two frames
by default, and instruction/work-time budgets bound each call. An instruction or
synthetic startup frame can overshoot its cycle budget; that overshoot carries
into the next budget. Callers supply elapsed wall time and choose when to yield.

`Gameboy.RunForCycles` is also available for deterministic bounded machine work
without playback policy. Bare `new Gameboy()` remains boot-skipped; the default
host creates a machine with the synthetic DMG startup enabled.

## Frames and sound

The host captures video at `Video.FrameReady`, renders shade indices once with
the core renderer, and publishes a `VideoFrame` with owned pixels, frame number,
cycle count and startup status. It also publishes initial/reset frames, debugger
step updates and a blank frame when the LCD is disabled. Only hardware frame
events increment the frame number and FPS counter.
`Pixels` is a read-only span; retain the frame object when queueing work. Old
frames remain valid after advancing, resetting or disposing the host.

Use `FrameReady` for notifications or read `LatestFrame` on the host's context.
Prefer a bounded latest-frame handoff when presentation is slower than emulation;
do not enqueue an unbounded backlog of UI updates. Palette choice, pixel scaling
and letterboxing belong to the presenter. The renderer still reconstructs video
snapshots; this refactor does not add a cycle-accurate pixel pipeline.

`IAudioOutput.Write` receives callback-scoped, signed 16-bit stereo PCM at
`Apu.SampleRate` (48 kHz). A queueing implementation must copy the samples before
returning. The host flushes queued audio on pause, load, reset, mute, rewind and
disposal. It owns and disposes the supplied audio output. Pass null for silent
execution. The Win32 adapter opens independently of the APU and no longer
subscribes to machine events itself.

## State, ownership and threading

`State` is `Empty`, `Paused`, `Running`, `Faulted` or `Disposed`. A loaded ROM is
initially paused; call `Resume` explicitly. `Pause` changes the user's playback
request. `SetSuspended(reason, true/false)` represents temporary causes such as a
dialog, inactive window, minimization or menu. Removing one cause cannot clear
another cause or undo a manual pause. Reset preserves the user's playback request
and active suspension reasons, clears cartridge RAM/history, and replays startup.

The host is single-owner and not thread-safe. Serialize playback commands,
inspection and stepping; marshal commands to the execution context if using a
worker. `FrameReady` handlers run synchronously on that context and must not
reenter playback or dispose the host. They can retain frames for later dispatch.
UI-specific focus behavior and dispatcher selection stay outside the host.

Continuous execution faults pause the machine, release buttons and expose
`Fault`; reset or load a cartridge before resuming. Disposal is idempotent,
detaches hardware callbacks, releases input and closes the audio output. UIs must
stop their scheduler and detach late focus/menu callbacks during shutdown.

## Loading and inspection

`Load(Cartridge)` copies the ROM bytes and derives its header from those bytes.
Mutating the caller's buffer cannot modify the loaded program. Unsupported
cartridges are rejected before replacing the active machine unless explicitly
allowed by host options. Partial hardware support remains visible in inspection.

`LoadAsync(string)` uses `FileCartridgeSource`. Implement `ICartridgeSource` for a
browser upload, embedded resource, remote store or test double. Async loading
suspends playback, rejects overlapping loads and checks cancellation before
replacing the machine. Cancellation/failure preserves the previous machine;
disposing during a pending load prevents its completion from resurrecting it.
File-source cancellation is cooperative around the existing cartridge loader.

`Inspection.CaptureCpu`, `CaptureHardware`, `CaptureCartridge` and `ReadMemory`
return detached data. Inspectors cannot mutate the CPU through those snapshots.
Video capture reads the live LCD registers and only the video address ranges,
without allocating a full address-space snapshot or copying cartridge banks for
every frame. Memory inspection does not record CPU bus accesses.

## Debugging and headless use

`Debugging.Step` requires paused playback and records up to `HistoryCapacity`
machine snapshots. `StepBack` restores registers, devices, cartridge RAM and host
frame numbering. Failed steps restore the pre-step machine without losing older
history. Load/reset and continuous playback clear rewind history. The WinForms
debugger retains its presentation history (highlighted addresses and selected
views), while the shared debugger service owns machine rewind.

`SetBreakpoint(address, true)` pauses continuous execution before that instruction.
Resuming from a hit executes that instruction before checking subsequent hits.
Synthetic startup does not hit cartridge instruction breakpoints. The existing
WinForms debugger trace remains a manually scheduled sequence of shared debugger
steps; its richer hardware visualizations still use the composed core devices.

For headless tests or another service, provide no audio output and optionally set
`CaptureFrames = false`. FPS and speed remain available from `Statistics`; speed
is measured against the Game Boy clock and excludes paused elapsed time. Tests
can supply an unloaded `Gameboy(enableDmgStartup: false)` to skip startup.

## Validation

```powershell
dotnet build GB.Emulator.sln -c Release
dotnet test GB.Emulator.Hosting.Tests -c Release
dotnet test GB.Emulator.Tests -c Release
dotnet test GB.Emulator.Windows.Tests -c Release
```

Hosting tests cover independent machines, matching frames, frame lifetime,
bounded catch-up, pause causes, audio lifetime, read-only inspection, ROM
ownership, rewind, breakpoints, faults, callback reentrancy and async load
cancellation/disposal. Windows tests exercise the actual message loop, startup,
input, resizing, fullscreen and inspector lifecycle.
