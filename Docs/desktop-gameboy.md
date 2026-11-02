# Desktop DMG player

A real, rotatable 3D DMG on a transparent Windows desktop surface. It uses the same emulator,
cartridge support, startup sequence, pacing and Windows audio as the normal player. It does
not run a second emulator implementation or stream video from a browser.

```powershell
dotnet run --project GB.Emulator.Windows -c Release -- --desktop
dotnet run --project GB.Emulator.Windows -c Release -- --desktop "C:\Games\MyGame.gb"
```

For the self-contained Windows package:

```powershell
.\GB.Emulator.Windows.exe --desktop "C:\Games\MyGame.gb"
```

The ordinary player also has **View → 3D desktop Game Boy…**. This opens the desktop player with
the same ROM and closes the original window and its inspectors; it starts the ROM from reset. If
the desktop process cannot be launched, the original window stays open and shows the error. No private ROM path
is assumed or bundled. Without a ROM the LCD shows an invitation to open one.

## Controls

For nine machines doing visible parallel computation, use the separate
[parallel lab](visual-gameboy-cluster.md): `dotnet run --project GB.Experiments.Windows -c Release -- --cluster`.

| Action | Control |
| --- | --- |
| Rotate | Hold Ctrl and drag the model |
| Move on the desktop | Drag the shell without Ctrl |
| Resize | Mouse wheel; Small/Medium/Large in the context menu |
| Screen close-up / return to 3D | Double-click the LCD; or use the context menu |
| Open a cartridge | Ctrl+O, drop a ROM on the model, or right-click → Open ROM |
| D-pad / A / B | Arrow keys / Z / X; the physical model buttons also respond to clicks |
| Start / Select | Enter / Space; or click their model buttons |
| Pause / reset | Ctrl+P / Ctrl+R |
| Reset orientation | Home, double-click the shell, or right-click → Reset orientation |
| Inspect the back | Ctrl-drag around, or right-click → Rear / cartridge view |
| Audio / always on top | Right-click menu |
| Close | Click the model's power switch, Alt+F4 or right-click → Exit |

The model has no window border or opaque background. The temporary instruction pill fades away.
Keyboard and held mouse input are released on deactivation; minimising suspends playback.
Playback otherwise continues while the desktop player is inactive. Menus and ROM dialogs suspend
the host, and mouse capture is released when a rotation ends.

## The screen and materials

The actual 160×144 emulator frame is uploaded to a reusable `WriteableBitmap` on the model's
LCD surface. Its four colours use a muted DMG palette. A separate glass layer adds a subtle
moving softbox highlight and specular reflection. Warm key light, cool fill, rear light and a
soft silhouette shadow light the textured shell. These are native WPF diffuse/specular effects,
not a physically based ray tracer or a reflection of the user's desktop.

The close-up shows only the live LCD at its original 160:144 aspect ratio, with crisp nearest-neighbour
scaling. Switching views preserves the running game and model orientation. The mouse wheel resizes
either view; right-click controls and keyboard input work in both views.
The live LCD unfolds from its position on the model into the close-up over 380 ms, while the shell
fades out and the window smoothly changes height. Returning reverses the transition to the saved
orientation.

The 18,182-triangle model is [Nintendo Game Boy (Original 1989) by Lokeig](https://sketchfab.com/3d-models/nintendo-game-boy-original-1989-ad2f6be906e948f793fe722bbae5d29c).
The source GLB is unchanged; the renderer hides the loose decorative cartridge, replaces the LCD,
and changes the inserted cartridge's appearance at runtime. Static geometry/materials are frozen,
textures are shared and the screen bitmap is reused. Real playback on the development machine
was measured at approximately 59 FPS / 1.00× speed; this is not a performance guarantee for every GPU.

**The model has a separate CC BY-NC 4.0 license, restricting it to noncommercial use.** Keep the
[asset attribution and license notice](../GB.Emulator.Windows/Desktop/Assets/ATTRIBUTION.md) with
copies. The code license does not replace the model license. No model download is needed at runtime.

## Matching the cartridge

ROM files contain game metadata and code, not scans of their printed labels. The inserted cartridge
uses the loaded ROM's filename and SHA-256 identity. It uses a grey DMG shell by default, recognises
the familiar Pokémon Red/Blue/Yellow/Gold/Silver colours, and uses a dark shell for other dual-mode
colour-capable games. These are cosmetic identification rules, not an exhaustive physical cartridge
database. Unsupported CGB-only ROMs remain unsupported by the underlying DMG emulator.

For an original label, put a matching image beside the ROM:

```text
MyGame.gb
MyGame.label.png
```

JPEG (`MyGame.label.jpg`) also works. Alternatively use `CartridgeLabels/<full-lowercase-ROM-SHA256>.png`
in the ROM's directory for an exact content match across renamed files. Label lookup is local;
the player does not scrape or download game artwork. Without a scan, it generates a clearly
identified title label, including the ROM size and hash. It replaces the model's baked Pokémon
label so another game is never shown with that unrelated label. The installed cartridge is seated
in the rear slot, so the case naturally obscures its lower portion.

## Design and validation

`DesktopWindow` owns native window/input/scheduling. `DesktopScene` owns the view, model transforms,
materials and frame texture. `DmgModel` reads the bundled static GLB with checked buffer ranges and
no external references. `CartridgeAppearance` resolves local ROM identity and optional artwork.
`EmulationHost` owns all emulation state and `WindowsAudioOutput` supplies audio. All host access
and presentation run on the window dispatcher; closing cancels pending ROM loading and disposes
the host/audio.

The Windows UI tests run the live WPF window, verify real frame advancement, capture transparent
front/rear/edge images, check button hit testing at multiple angles, distinguish Start from Select,
test local label lookup and verify host disposal. Developer captures are also available via
`--capture <image.png> --capture-and-close` after `--desktop`; optional `--yaw` / `--pitch` set the
initial view. The PNG has alpha and the adjacent JSON records host performance and frame identity.
