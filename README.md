# SynthCamera2

Camera2-style multi-camera desktop viewing for **Synth Riders PCVR**, built for
streamers and mixed-reality capture. A MelonLoader mod by OmniDreamer, inspired
by kinsi55's [Camera2](https://github.com/kinsi55/CS_BeatSaber_Camera2) for
Beat Saber.

One DLL supports **both** game branches (Unity 2021.3.45f2 and Unity
6000.3.13), including the mid-2026 beta builds that removed legacy input.
Every camera renders to the desktop window only — the headset view is never
touched.

## Features

- **Multiple simultaneous cameras**, fully config-driven (JSON), hot-reloaded
  with F9
- **Smoothed first-person** camera with adjustable position/rotation smoothing
  and Force Upright (horizon stays level)
- **Static third-person** cameras with per-camera FOV and viewport rects
  (picture-in-picture and side-by-side layouts)
- **Grab-and-place in VR**: squeeze grip to pick up a camera by its in-headset
  gizmo, move it, release — the new position saves to the config
  automatically. Gizmos are visible only in the headset, never on stream
- **Per-camera visibility**: toggles for notes, walls, trails, avatar, hit
  particles, and UI, plus explicit show/hide lists for any layer by name
- **Per-camera post-processing** control (bloom/tonemapping off by default)
- **Mixed reality**: chroma-key foreground layers with clip-plane control, and
  `externalcamera.cfg` calibration support (LIV / SteamVR MRC format)
- **Scene gating**: cameras can be menu-only, game-only, or always on (WIP)
- Works with the game's own display camera set to **off** (recommended — the
  mod's cameras replace it and you save the redundant render)

## Requirements

- Synth Riders PCVR (Steam), either supported branch
- [MelonLoader](https://melonwiki.xyz/) 0.7.2 or newer (net6)

## Installation

1. Install MelonLoader into Synth Riders and run the game once.
2. Drop `SynthCamera2.dll` into the `Mods` folder.
3. Launch the game. A default config is created at
   `UserData/SynthCamera2/cameras.json` with a static third-person camera
   enabled; a smoothed first-person camera and three mixed-reality cameras
   are included but disabled.

## Hotkeys

| Key | Action |
| --- | ------ |
| F9  | Reload `cameras.json` and rebuild all cameras |
| F10 | Master toggle: all mod cameras on/off |
| F8  | Layer-usage diagnostic dump (press mid-song for note/rail layers) |

Hotkeys work on every supported game build: the mod detects at runtime whether
the build uses legacy input or the Input System package and reads the keyboard
accordingly.

## Configuration

Edit `UserData/SynthCamera2/cameras.json`, then press F9 in-game. Each entry in
`Cameras` supports:

| Field | Default | Description |
| ----- | ------- | ----------- |
| `Name` | `"Camera"` | Display name, used in logs |
| `Enabled` | `true` | Whether this camera is built |
| `Type` | `"FirstPerson"` | `FirstPerson` (follows headset), `Static` (fixed world pose, grabbable in VR), `External` (pose from calibration file) |
| `VisibleIn` | `"Always"` | `Always`, `MenuOnly`, or `GameOnly` |
| `Fov` | `0` | Field of view; `0` inherits (from the game camera, or the calibration file for `External`) |
| `Rect` | `[0,0,1,1]` | Normalized viewport `[x, y, w, h]`; smaller rects give picture-in-picture |
| `Position` | `[0,2,-3]` | Static: world position (also the fallback pose for `External` when no calibration file is found) |
| `Rotation` | `[15,0,0]` | Static: world rotation, euler degrees |
| `SmoothPosition` | `6` | FirstPerson: position smoothing speed (higher = snappier, `0` = locked) |
| `SmoothRotation` | `4` | FirstPerson: rotation smoothing speed |
| `ForceUpright` | `true` | FirstPerson: kill head roll, keep horizon level |
| `Offset` | `[0,0,0]` | FirstPerson: offset from the head, in head-local space |
| `ShowNotes` / `ShowWalls` / `ShowTrails` / `ShowAvatar` / `ShowHitParticles` / `ShowUI` | `true` | Per-camera visibility toggles |
| `ShowLayers` / `HideLayers` | `[]` | Explicit layer names to force-show/hide, applied after the toggles (`HideLayers` wins). Use the F8 dump to see layer names; useful ones: `"Controller Indicator"` (hit counters by your hands), `"Stage"` (tutorial/alert text) |
| `PostProcessing` | `"Off"` | Bloom/tonemapping: `Off` (default), `Auto` (on unless `Chroma`), or `On`  |
| `ClearMode` | `"Inherit"` | `Chroma` clears to a solid key color for MR foreground layers |
| `ChromaColor` | `[0,255,0]` | Key color, 0-255 RGB |
| `NearClip` / `FarClip` | `0` | Clip plane overrides in meters; `0` inherits |
| `CalibrationFile` | `"externalcamera.cfg"` | External: calibration file name or absolute path |

### !! WARNING IF YOU ENABLE POSTPROCESSING BE AWARE THAT IT MAY CAUSE PERFORMANCE ISSUE'S !!

Mod settings live in `UserData/MelonPreferences.cfg` under `[SynthCamera2]`:

| Setting | Default | Description |
| ------- | ------- | ----------- |
| `DebugLogging` | `false` | Verbose logs: camera builds, template choice, controller detection, grip presses |
| `RebuildDelayFrames` | `150` | Frames after a scene load before cameras are (re)built |
| `EnableGrab` | `true` | VR grab-and-place for Static cameras |
| `AllowGrabInGame` | `false` | Allow grabbing during songs (off = menus only) |
| `GrabRadius` | `6.25` | Controller-to-camera distance (meters) required to grab. The default effectively means "grip anywhere grabs the nearest camera"; lower it to ~0.3 for touch-only grabbing |

If two enabled cameras have overlapping viewports, the higher-depth one (later
in the list) draws on top wherever they intersect — the log notes this once
per config load. Give cameras separate `Rect`s if you expect to see both.

## Grab-and-place (VR)

Static cameras show a small camera-shaped gizmo in your headset (white = idle,
yellow = in reach, green = held). Squeeze **grip** within `GrabRadius` to
grab, move the camera, and release to place it. The pose is written back to
`cameras.json` automatically, so placements survive restarts.

Grabbing works in menus by default; set `AllowGrabInGame = true` to allow it
mid-song (and lower `GrabRadius` first, or every grip will grab a camera).
`GameOnly` cameras still show their gizmos in the menu, so you can place song
cameras before playing. `External` cameras are calibration-locked and can't be
grabbed; `FirstPerson` cameras follow your head.

## Mixed reality

**Basic (green screen):** film yourself against a green screen; add a `Static`
or `External` camera matching your physical camera's pose and FOV. In OBS:
game feed on the bottom, your chroma-keyed camera video on top.

**Foreground occlusion:** duplicate that camera, set `ClearMode: "Chroma"` and
`FarClip` to your distance from the camera, with `ShowUI` and `ShowAvatar`
off. Key this feed and stack it above your camera video — notes and rails
passing in front of you will occlude you.

**Calibrated:** put an `externalcamera.cfg` (LIV / SteamVR MRC format) next to
`SynthRiders.exe` or in `UserData/SynthCamera2/`, and use two `External`
cameras (see the disabled `MRBackgroundCalibrated` / `MRForegroundCalibrated`
entries in the default config). One calibration file drives both layers, so
the viewpoints can never drift apart. Calibration poses are play-space
coordinates, anchored to the XR rig — they survive stage offsets. `Fov`,
`NearClip`, and `FarClip` in the camera def override the file's values.

**All cameras render into the single game window** — MR layers are viewports
within it, never separate OS windows. The default calibrated pair ships
side-by-side (background left, foreground right): capture the game window
twice in OBS and crop each capture to its half.

## Troubleshooting

- Set `DebugLogging = true` in MelonPreferences and check
  `MelonLoader/Latest.log`. Camera builds, template selection, calibration
  loads, controller detection, and grip presses (with distance to the nearest
  camera) are all logged.
- **No bloom?** That's the default — set `PostProcessing` to `"Auto"` or
  `"On"` on the cameras that should glow.
- **A camera seems invisible or toggles seem to do nothing?** Check the log
  for the overlapping-viewports note; a higher-depth fullscreen camera may be
  covering the one you're editing.
- **Hotkeys dead?** Look for the `Input backend` line in the log. If it says
  keyboard shortcuts were disabled, the build's input configuration isn't one
  the mod recognizes — open an issue with the log.
- If the log shows `XR InputDevices reports no valid hand devices`, grab input
  needs a fallback on your setup — please open an issue with the log attached.
- An example config is in [`examples/cameras.json`](examples/cameras.json).

## Building from source

Building requires a local Synth Riders install with MelonLoader (the interop
assemblies it generates are referenced and are not redistributable):

```
dotnet build -c Release -p:GamePath="C:\path\to\SynthRiders"
```

No NuGet packages are used; all references come from the MelonLoader install.

## License

MIT — see [LICENSE](LICENSE).
