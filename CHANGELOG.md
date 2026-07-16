# Changelog

## 0.4.1 (14-07-2026)
- Fixed invisible grab gizmos: `CreatePrimitive`'s built-in Standard material
  is stripped from URP IL2CPP builds; gizmo materials now use a
  runtime-resolved shader (URP/Unlit first, with fallbacks).
- XR input diagnostics: first-detection log per controller, one-time warning
  if no valid hand devices appear within 10 seconds, and grip-press logging
  with distance to the nearest camera vs grab radius (debug logging only).
- Build fixes: `MelonLoader.Utils` usings, `UnityEngine.PhysicsModule`
  reference.

## 0.4.0 (14-07-2026)
- VR grab-and-place for Static cameras: in-headset gizmos on the HMDViewOnly
  layer (never visible on stream), grip to grab, release to place, pose saved
  back to `cameras.json` automatically. Menu-only by default
  (`AllowGrabInGame` preference). Controller input via `UnityEngine.XR`
  InputDevices.

## 0.3.0 (14-07-2026)
- `External` camera type: pose/FOV/clip loaded from `externalcamera.cfg`
  (LIV / SteamVR MRC format), searched in `UserData/SynthCamera2/` and the
  game root. Poses are play-space coordinates anchored to the XR rig root and
  re-anchored every frame. Camera-def values override the file. Default
  config gains a disabled calibrated MR camera pair.

## 0.2.0 (14-07-2026)
- Fixed cameras vanishing across scene loads when the game's display camera
  is set to off: the game's "[OFF Camera]" is no longer accepted as a clone
  template (rejected by name and by near-empty culling mask), and rebuilds
  are transactional — existing cameras keep rendering until a usable template
  is secured, retrying every 60 frames.
- Mixed reality support: per-camera `ClearMode: "Chroma"` with `ChromaColor`
  (post-processing disabled on chroma cameras) and `NearClip`/`FarClip`
  overrides for foreground occlusion layers.

## 0.1.0 (14-07-2026)
- Initial release: config-driven multi-camera desktop viewing. Smoothed
  first-person and static third-person cameras, per-camera FOV / viewport
  rect / visibility toggles, scene gating (menu/game), F9 config reload,
  F10 master toggle. Desktop-only rendering with zero HMD impact, verified
  on both Unity branches.
