# Changelog

## 0.7.1
- Removed the `ShowAvatar` option. Existing configs containing the field
  still load (the field is ignored). To hide avatar layers on a camera, use
  `"HideLayers": ["PlayerAvatar"]`.

## 0.7.0
- Fixed `ShowNotes` / `ShowUI` no longer hiding notes and score UI after the
  game update: the game moved the visible note/rail meshes and part of the
  score HUD onto the Default layer, which culling cannot selectively hide.
  The mod now moves those objects back onto the `Notes` / `StageUI` layers
  when (and only when) an enabled camera hides notes or UI, with a periodic
  in-game re-sweep. The headset view is unaffected.

## 0.6.3
- Fixed `PostProcessing: "Off"` not being honored on the Unity 6 branch:
  bloom rendered even with post-processing disabled on the camera. The
  camera's post-processing volume mask is now cleared as well when
  post-processing is off, which removes bloom regardless. Debug logs now
  read the URP settings back after applying them.

## 0.6.2 
- `PostProcessing` now defaults to `"Off"`. Set `"Auto"` or `"On"` per
  camera to enable bloom/tonemapping.

## 0.6.1 
- Restored post-processing (bloom, tonemapping) on mod cameras: the URP
  volume layer mask and antialiasing settings are now copied from the clone
  template, so the game's post-processing volumes actually affect mod
  cameras. Previously a fresh camera's default volume mask could miss them
  entirely.
- New per-camera `PostProcessing` option: `"Auto"` (on for normal cameras,
  off for Chroma — the previous behavior), `"On"`, or `"Off"`.

## 0.6.0 
- Update to the Input System package only, which silently killed the F8/F9/F10
  hotkeys and added per-frame exception cost. All keyboard reads now go
  through a probe-once backend (`KeyInput`): legacy Input where it still
  works (Unity 2021 branch), the Input System package via reflection where
  it doesn't, and a clean disable with one warning if neither is available.
- Grab input aligned to the proven analog pattern: `CommonUsages.grip` with
  hysteresis (>0.75 press, <0.35 release), `gripButton` retained as
  fallback. VR controller input (`InputDevices`) itself was never affected
  by the backend switch. 
- Fixed applied where Lindsay Sterling Experiance wasn't displayed in the desktop window
- Optimization pass, no behavior changes: camera type and head offset are
  resolved once per rebuild instead of per frame; per-camera Transform and
  enabled-state are cached to cut IL2CPP interop calls in the follow loop;
  XR controller reads are skipped entirely when grabbing is disallowed or no
  grabbable cameras exist; gizmo visibility and tints update on transitions
  rather than every frame.

## 0.5.2 
- New-install defaults updated: StaticThirdPerson enabled (FOV 90, visible
  everywhere); first-person and MR cameras included but disabled;
  MRForeground on the right half-viewport. Existing configs are untouched.
- Default `GrabRadius` raised to 6.25 m (grip anywhere grabs the nearest
  camera; grabbing remains menu-only by default). Existing preferences keep
  their saved value.

## 0.5.1
- Added a once-per-config-load notice when enabled cameras have overlapping
  viewports (the higher-depth camera draws on top). Investigation of the
  "visibility toggles not working" report showed the toggles and layer
  mapping were correct; the edited camera was fully covered by a second
  fullscreen camera.

## 0.5.0 
- Added F8 layer-usage diagnostic: renderer counts and sample objects per
  layer, plus the Rail Manager(Clone) subtree layers when pressed mid-song.
  Added while investigating ineffective visibility toggles.
- Added per-camera `ShowLayers` / `HideLayers` arrays for explicit layer
  control by name, applied after the boolean toggles (`HideLayers` wins).
- The default calibrated MR pair now ships with side-by-side viewport rects.
  Note: all cameras render into the single game window; MR layers are
  viewports within it, not separate OS windows.

## 0.4.1 
- Fixed invisible grab gizmos: `CreatePrimitive`'s built-in Standard material
  is stripped from URP IL2CPP builds; gizmo materials now use a
  runtime-resolved shader (URP/Unlit first, with fallbacks).
- XR input diagnostics: first-detection log per controller, one-time warning
  if no valid hand devices appear within 10 seconds, and grip-press logging
  with distance to the nearest camera vs grab radius (debug logging only).
- Build fixes: `MelonLoader.Utils` usings, `UnityEngine.PhysicsModule`
  reference.

## 0.4.0 
- VR grab-and-place for Static cameras: in-headset gizmos on the HMDViewOnly
  layer (never visible on stream), grip to grab, release to place, pose saved
  back to `cameras.json` automatically. Menu-only by default
  (`AllowGrabInGame` preference). Controller input via `UnityEngine.XR`
  InputDevices.

## 0.3.0 
- `External` camera type: pose/FOV/clip loaded from `externalcamera.cfg`
  (LIV / SteamVR MRC format), searched in `UserData/SynthCamera2/` and the
  game root. Poses are play-space coordinates anchored to the XR rig root and
  re-anchored every frame. Camera-def values override the file. Default
  config gains a disabled calibrated MR camera pair.

## 0.2.0 
- Fixed cameras vanishing across scene loads when the game's display camera
  is set to off: the game's "[OFF Camera]" is no longer accepted as a clone
  template (rejected by name and by near-empty culling mask), and rebuilds
  are transactional — existing cameras keep rendering until a usable template
  is secured, retrying every 60 frames.
- Mixed reality support: per-camera `ClearMode: "Chroma"` with `ChromaColor`
  (post-processing disabled on chroma cameras) and `NearClip`/`FarClip`
  overrides for foreground occlusion layers.

## 0.1.0 
- Initial release: config-driven multi-camera desktop viewing. Smoothed
  first-person and static third-person cameras, per-camera FOV / viewport
  rect / visibility toggles, scene gating (menu/game), F9 config reload,
  F10 master toggle. Desktop-only rendering with zero HMD impact, verified
  on both Unity branches.
