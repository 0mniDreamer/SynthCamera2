using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SynthCamera2.SynthCamera2Mod), "SynthCamera2", "0.6.0", "OmniDreamer")]
[assembly: MelonGame(null, null)]

namespace SynthCamera2
{
    // SynthCamera2 v0.6.0 (17-08-2026)
    //
    // v0.6.0 changes:
    //   - Input system update switched to future proof the Unity 6 builds to Input
    //     System package ONLY; every legacy UnityEngine.Input read throws.
    //     The hotkey block swallowed the exception silently -> F8/F9/F10
    //     dead on the some builds plus per-frame exception cost. All keyboard
    //     reads now go through KeyInput.cs, the probe-once backend ported
    //     from SynthRidersTwitchChat v1.2.1 mod: legacy where it works (2021
    //     branch), Input System Keyboard via reflection where it doesn't,
    //     clean disable with one warning if neither resolves.
    //   - GrabManager grip read aligned to the suite-proven pattern from the
    //     chat mod's XR rewrite: analog CommonUsages.grip with hysteresis
    //     (>0.75 press, <0.35 release), gripButton bool retained as
    //     fallback. InputDevices itself was never affected by the input
    //     backend switch (validated in chat mod production use).
    //  (optimization pass, zero behavior change):
    //   - Camera type resolved to an enum at construction; the per-frame
    //     string comparisons in follow/grab paths are gone.
    //   - Transform cached per camera; effective-enabled tracked as a managed
    //     bool instead of reading Cam.enabled via interop each frame.
    //   - FirstPerson head offset precomputed at construction.
    //   - GrabManager: XR device reads (4 interop calls per hand per frame)
    //     skipped entirely when grabbing is disallowed or no grabbable
    //     cameras exist; gizmo active-state applied only on allow transitions
    //     and rebuilds (NotifyCamerasRebuilt); tint updates gated on a valid
    //     hand.
    //   - OnLateUpdate early-outs when no cameras are built.
    //
    // v0.5.2 changes:
    //   - Default camera config now matches OmniDreamer's live layout:
    //     StaticThirdPerson enabled (Fov 90, VisibleIn Always), first-person
    //     and all MR cameras present but disabled, MRForeground on the right
    //     half-viewport. Defaults only affect fresh installs; existing
    //     cameras.json files are never touched.
    //   - GrabRadius default raised to 6.25m: grip anywhere grabs the
    //     nearest camera (menu-only by default). Existing MelonPreferences
    //     keep their saved value.
    //
    // v0.5.1 changes:
    //   - Overlapping-viewport notice (once per config load). The F8 dump
    //     (15-07-2026, Unity 6) proved the visibility toggles and the layer
    //     mapping were CORRECT all along -- Notes(22)/WallObstacles(30)/
    //     HitParticles(23)/StageUI(16) hold the real renderers, and the
    //     edited camera's mask showed the toggles applied. The "not working"
    //     report was two stacked fullscreen cameras: the higher-depth one
    //     covered the camera being edited. The mod now says so.
    //   - Dump-informed HideLayers tips: in-song hit counters live on
    //     "Controller Indicator"(25), tutorial/alert text on "Stage"(11);
    //     neither is part of ShowUI by design -- use HideLayers for those.
    //
    // v0.5.0 changes:
    //   - F8 layer-usage dump: renderer counts + sample objects per layer,
    //     plus Rail Manager(Clone) subtree layers mid-song. Added because the
    //     Show* visibility toggles were reported ineffective (15-07-2026);
    //     the toggle->layer-name mapping came from the layer TABLE, which
    //     proves layers exist, not that renderers sit on them. The dump gives
    //     the real mapping so the toggles can be corrected against evidence.
    //   - Per-camera ShowLayers/HideLayers string arrays: explicit layer
    //     control by name, applied after the toggles (HideLayers wins).
    //     Unresolvable names warn in debug builds.
    //   - Default calibrated MR pair now ships side-by-side viewport rects.
    //     All cameras render into the ONE game window -- MR layers are
    //     viewports within it, never separate OS windows; capture the window
    //     twice in OBS and crop each half.
    //
    // v0.4.1 changes:
    //   - FIX: gizmos were invisible. CreatePrimitive's built-in Standard
    //     material is stripped from URP IL2CPP builds (observed 14-07-2026,
    //     Unity 6 branch; same failure class as the emote-rain quad meshes).
    //     Gizmo materials now use a runtime-resolved shader from candidates
    //     (URP/Unlit first).
    //   - XR input diagnostics: debug log on first controller detection per
    //     hand; one-time warning if no valid hand devices after 10s (the
    //     previous silent-failure hole); debug log on grip press showing
    //     distance to nearest camera vs grab radius.
    //   - Build fixes baked in: MelonLoader.Utils usings (MelonEnvironment),
    //     UnityEngine.PhysicsModule reference (Collider).
    //
    // v0.4.0 changes:
    //   - VR grab-and-place: Static cameras show a camera-shaped gizmo on the
    //     HMDViewOnly layer (visible in headset, never on stream; our cameras
    //     now always strip HMDViewOnly from their masks). Squeeze grip within
    //     GrabRadius to grab; release to place. New pose is written back to
    //     cameras.json automatically. Menus only by default (AllowGrabInGame
    //     preference enables mid-song grabbing). External cameras excluded
    //     (calibration-locked); FirstPerson excluded (follows head).
    //   - Controller input via UnityEngine.XR InputDevices (XRModule interop)
    //     -- the one path not probe-validated; guarded so a failure disables
    //     grab for the session with a single warning. Report if seen.
    //
    // v0.3.0 changes:
    //   - "External" camera type: pose/fov/clip loaded from an
    //     externalcamera.cfg calibration file (LIV / SteamVR MRC format).
    //     Search order: absolute path -> UserData/SynthCamera2/ -> game root
    //     (the traditional location next to the game exe). Calibration is in
    //     play-space coordinates, so External cameras anchor to the XR rig
    //     root (found by name candidates walking up from the headset) and
    //     re-anchor every frame. Def Fov/NearClip/FarClip override cfg values.
    //     Missing cfg falls back to the def's Position/Rotation with a
    //     warning. Default config gains a disabled calibrated MR pair
    //     (MRBackgroundCalibrated / MRForegroundCalibrated) sharing one cfg.
    //
    // v0.2.0 changes:
    //   - FIX: cameras vanished across scene loads when the game's display
    //     camera was set to OFF. Cause: "[OFF Camera]" is an enabled,
    //     non-stereo, backbuffer camera with a dead culling mask and was
    //     being picked as the clone template. Template scan now rejects OFF
    //     cameras by name and any camera whose mask has fewer than 2 bits
    //     set. Rebuild is transactional: existing cameras are only destroyed
    //     after a usable template is secured; otherwise retry every 60
    //     frames while the old cameras keep rendering.
    //   - Mixed reality support: per-camera ClearMode "Chroma" (+ChromaColor,
    //     0-255 RGB) clears to a solid key color with post-processing
    //     disabled so bloom/tonemapping cannot contaminate the key.
    //     NearClip/FarClip overrides (meters, 0 = inherit) enable classic
    //     MR foreground layers: FarClip at player distance renders only
    //     objects between the camera and the player.
    //
    // SynthCamera2 v0.1.0 (14-07-2026)
    //
    // Camera2-style multi-camera desktop viewing for Synth Riders PCVR.
    // Single cross-branch DLL (Unity 2021.3.45f2 / 6000.3.13). Built entirely
    // on SynthCameraProbe v0.1-v0.3 findings, all dated 14-07-2026:
    //   - Clone template: the game's active desktop camera
    //     ("[Main Camera]/[ThirdPerson Custom Camera]/..."), never the
    //     Headset Camera. Fallback to Camera.main scrubs HMD-only layers.
    //   - Desktop-only rendering: stereoTargetEye=None + URP
    //     allowXRRendering=false. Confirmed zero HMD impact on both branches.
    //   - One extra camera measured at no visible frame cost (vsync-pinned
    //     8.34ms with and without, spikes symmetric) on both branches.
    //   - Camera GameObjects use the "SynthCamera2_" prefix so SynthPerfFix
    //     can learn to ignore them (it currently stands down when an unknown
    //     camera appears -- observed 14-07-2026, Unity 6 branch).
    //
    // Config: UserData/SynthCamera2/cameras.json (auto-created with a smoothed
    // first-person camera enabled and a static third-person example disabled).
    //
    // Hotkeys: F9 = reload config + rebuild cameras, F10 = master toggle.
    //
    // v0.1 scope notes:
    //   - Cameras render direct-to-backbuffer over the HMD mirror. Per-camera
    //     FPS caps are deliberately absent: skipping frames on a fullscreen
    //     backbuffer camera would flicker against the mirror underneath. Caps
    //     return with a RenderTexture compositor in a later version.
    //   - Scene classification (menu vs game) uses the "Rail Manager(Clone)"
    //     marker heuristic after scene settle.
    //
    // No lambdas / async / LINQ -> CompilerShims.cs not required.

    public class SynthCamera2Mod : MelonMod
    {
        private MelonPreferences_Category _cfg;
        private MelonPreferences_Entry<bool> _debugLogging;
        private MelonPreferences_Entry<int> _rebuildDelayFrames;
        private MelonPreferences_Entry<bool> _enableGrab;
        private MelonPreferences_Entry<bool> _allowGrabInGame;
        private MelonPreferences_Entry<float> _grabRadius;

        private CameraConfigFile _cameraConfig;
        private readonly List<ManagedCamera> _cameras = new List<ManagedCamera>();
        private readonly GrabManager _grab = new GrabManager();
        private Transform _rigCached;

        private int _framesUntilRebuild = -1;
        private bool _masterEnabled = true;
        private bool _isGameScene;

        public override void OnInitializeMelon()
        {
            _cfg = MelonPreferences.CreateCategory("SynthCamera2");
            _debugLogging = _cfg.CreateEntry<bool>("DebugLogging", false);
            _rebuildDelayFrames = _cfg.CreateEntry<int>("RebuildDelayFrames", 150,
                null, "Frames after scene load before cameras are (re)built.");
            _enableGrab = _cfg.CreateEntry<bool>("EnableGrab", true,
                null, "Grab Static cameras with the controller grip to move them.");
            _allowGrabInGame = _cfg.CreateEntry<bool>("AllowGrabInGame", false,
                null, "Allow grabbing during songs (off = menus only).");
            _grabRadius = _cfg.CreateEntry<float>("GrabRadius", 6.25f,
                null, "Controller-to-camera distance (meters) required to grab. "
                + "Large values let you grab the nearest camera from anywhere.");

            _cameraConfig = ConfigLoader.LoadOrCreate();

            MelonLogger.Msg("SynthCamera2 0.6.0 loaded - " + CountEnabled()
                + " camera(s) enabled. F9 reload config, F10 master toggle, "
                + "F8 layer dump.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            // Old cameras keep rendering through the transition; rebuild after
            // the scene settles so the template and layers are current.
            _framesUntilRebuild = _rebuildDelayFrames.Value;
            if (_debugLogging.Value)
                MelonLogger.Msg("Scene loaded: \"" + sceneName
                    + "\"; rebuild in " + _framesUntilRebuild + " frames.");
        }

        public override void OnUpdate()
        {
            if (_framesUntilRebuild > 0)
            {
                _framesUntilRebuild--;
                if (_framesUntilRebuild == 0)
                {
                    _framesUntilRebuild = -1;
                    RebuildCameras();
                }
            }

            try
            {
                if (KeyInput.GetKeyDown(KeyCode.F9))
                {
                    _cameraConfig = ConfigLoader.LoadOrCreate();
                    _overlapWarned = false;
                    MelonLogger.Msg("Config reloaded (" + CountEnabled()
                        + " camera(s) enabled); rebuilding.");
                    RebuildCameras();
                }
                if (KeyInput.GetKeyDown(KeyCode.F10))
                {
                    _masterEnabled = !_masterEnabled;
                    for (int i = 0; i < _cameras.Count; i++)
                        _cameras[i].SetMasterVisible(_masterEnabled);
                    MelonLogger.Msg("Master toggle: cameras "
                        + (_masterEnabled ? "ON" : "OFF"));
                }
                if (KeyInput.GetKeyDown(KeyCode.F8))
                    DumpLayerUsage();
            }
            catch (Exception ex)
            {
                if (_debugLogging.Value)
                    MelonLogger.Warning("Hotkey handling failed: " + ex.Message);
            }
        }

        public override void OnLateUpdate()
        {
            if (_cameras.Count == 0)
                return;

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _cameras.Count; i++)
            {
                try
                {
                    _cameras[i].LateUpdateFollow(dt);
                }
                catch (Exception ex)
                {
                    if (_debugLogging.Value)
                        MelonLogger.Warning("Follow update failed for \""
                            + _cameras[i].Def.Name + "\": " + ex.Message);
                }
            }

            // v0.4: grab-and-place, after follow so grabbed poses win.
            try
            {
                bool allowGrab = _enableGrab.Value && _masterEnabled
                    && (!_isGameScene || _allowGrabInGame.Value);
                bool dirty = _grab.Update(_cameras, _rigCached, allowGrab,
                    _grabRadius.Value, _debugLogging.Value);
                if (dirty)
                {
                    ConfigLoader.Save(_cameraConfig);
                    MelonLogger.Msg("Camera placement saved to cameras.json.");
                }
            }
            catch (Exception ex)
            {
                if (_debugLogging.Value)
                    MelonLogger.Warning("Grab update failed: " + ex.Message);
            }
        }

        public override void OnApplicationQuit()
        {
            DestroyAllCameras();
        }

        // ------------------------------------------------------------------
        // Camera lifecycle
        // ------------------------------------------------------------------

        private void RebuildCameras()
        {
            // v0.2: transactional rebuild. Secure a template BEFORE touching
            // the existing cameras; if none is available (e.g. game display
            // set to OFF during a transition), keep the old cameras rendering
            // and retry shortly. Fixes cameras vanishing across scene loads
            // when the game's own display camera is off (reported 14-07-2026).
            bool templateIsStereo;
            Camera template = PickCloneTemplate(out templateIsStereo);
            if (template == null)
            {
                _framesUntilRebuild = TemplateRetryFrames;
                if (_debugLogging.Value)
                    MelonLogger.Msg("No usable clone template yet; keeping "
                        + "existing cameras, retrying in " + TemplateRetryFrames
                        + " frames.");
                return;
            }

            DestroyAllCameras();

            _isGameScene = DetectGameScene();
            if (_debugLogging.Value)
                MelonLogger.Msg("Scene classified as "
                    + (_isGameScene ? "GAME" : "MENU") + ".");

            Transform head = FindHeadTransform();
            if (head == null && _debugLogging.Value)
                MelonLogger.Warning("Headset transform not found; first-person "
                    + "cameras will not follow until it appears.");

            Transform rig = FindRigRoot(head);
            _rigCached = rig;
            if (_debugLogging.Value)
                MelonLogger.Msg("Rig root: " + (rig == null
                    ? "<none, calibration treated as world space>" : rig.name));

            int built = 0;
            for (int i = 0; i < _cameraConfig.Cameras.Count; i++)
            {
                CameraDef def = _cameraConfig.Cameras[i];
                if (def == null || !def.Enabled)
                    continue;

                var mc = new ManagedCamera(def);
                if (mc.Spawn(template, templateIsStereo, head, rig, built,
                    _debugLogging.Value))
                {
                    mc.SetMasterVisible(_masterEnabled);
                    mc.SetSceneVisible(_isGameScene);
                    _cameras.Add(mc);
                    built++;
                }
            }

            if (_debugLogging.Value)
                MelonLogger.Msg("Built " + built + " camera(s) from template \""
                    + template.name + "\" (stereo template: " + templateIsStereo + ").");

            WarnOverlappingViewports();
            _grab.NotifyCamerasRebuilt();
        }

        // The layer toggles "not working" report (15-07-2026) turned out to be
        // two fullscreen cameras stacked: the higher-depth one fully covered
        // the camera being edited. Surface that once per config load.
        private bool _overlapWarned;

        private void WarnOverlappingViewports()
        {
            if (_overlapWarned)
                return;

            int warnings = 0;
            for (int i = 0; i < _cameras.Count && warnings < 3; i++)
            {
                for (int j = i + 1; j < _cameras.Count && warnings < 3; j++)
                {
                    Rect a = RectOf(_cameras[i].Def);
                    Rect b = RectOf(_cameras[j].Def);
                    bool overlap = a.xMin < b.xMax && b.xMin < a.xMax
                        && a.yMin < b.yMax && b.yMin < a.yMax;
                    if (!overlap)
                        continue;
                    _overlapWarned = true;
                    warnings++;
                    MelonLogger.Msg("Note: viewports of \"" + _cameras[i].Def.Name
                        + "\" and \"" + _cameras[j].Def.Name + "\" overlap; \""
                        + _cameras[j].Def.Name + "\" (higher depth) draws on top "
                        + "wherever they intersect. Give them separate Rects to "
                        + "see both.");
                }
            }
        }

        private static Rect RectOf(CameraDef def)
        {
            float x = 0f, y = 0f, w = 1f, h = 1f;
            if (def.Rect != null)
            {
                if (def.Rect.Length > 0) x = def.Rect[0];
                if (def.Rect.Length > 1) y = def.Rect[1];
                if (def.Rect.Length > 2) w = def.Rect[2];
                if (def.Rect.Length > 3) h = def.Rect[3];
            }
            return new Rect(x, y, w, h);
        }

        private void DestroyAllCameras()
        {
            // Commit any in-progress grab before its camera disappears.
            try { _grab.ReleaseAll(); }
            catch (Exception) { }

            for (int i = 0; i < _cameras.Count; i++)
                _cameras[i].Destroy();
            _cameras.Clear();
        }

        private int CountEnabled()
        {
            int n = 0;
            if (_cameraConfig != null && _cameraConfig.Cameras != null)
            {
                for (int i = 0; i < _cameraConfig.Cameras.Count; i++)
                {
                    if (_cameraConfig.Cameras[i] != null
                        && _cameraConfig.Cameras[i].Enabled)
                        n++;
                }
            }
            return n;
        }

        // ------------------------------------------------------------------
        // Probe-validated selection helpers
        // ------------------------------------------------------------------

        private const int TemplateRetryFrames = 60;

        // The game's "[OFF Camera]" (display set to off) is an enabled,
        // non-stereo, backbuffer camera that renders nothing -- it must never
        // be used as a template. Candidate name fragments, lowercase.
        private static readonly string[] OffCameraNameFragments = new string[]
        {
            "off camera", "[off"
        };

        // Prefer the game's own active desktop camera (enabled, active in
        // hierarchy, non-stereo, backbuffer, meaningful culling mask); fall
        // back to Camera.main with mask scrubbing. Skip our own cameras by
        // name prefix and the OFF camera by name/mask.
        private Camera PickCloneTemplate(out bool sourceIsStereo)
        {
            sourceIsStereo = false;
            Camera bestDesktop = null;

            try
            {
                var cams = Camera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    Camera c = cams[i];
                    if (c == null || !c.enabled)
                        continue;
                    if (c.gameObject.name.StartsWith(ManagedCamera.GoNamePrefix,
                        StringComparison.Ordinal))
                        continue;
                    if (IsOffCameraName(c.gameObject.name))
                        continue;

                    bool activeGo = false;
                    try { activeGo = c.gameObject.activeInHierarchy; }
                    catch (Exception) { }
                    if (!activeGo)
                        continue;

                    bool stereo = false;
                    try { stereo = c.stereoEnabled; }
                    catch (Exception) { }
                    if (stereo)
                        continue;

                    try
                    {
                        if (c.targetTexture != null)
                            continue;
                    }
                    catch (Exception) { }

                    // A template must actually render something: reject empty
                    // and near-empty culling masks.
                    if (PopCount(c.cullingMask) < 2)
                        continue;

                    if (bestDesktop == null || c.depth > bestDesktop.depth)
                        bestDesktop = c;
                }
            }
            catch (Exception ex)
            {
                if (_debugLogging.Value)
                    MelonLogger.Warning("Template scan failed: " + ex.Message);
            }

            if (bestDesktop != null)
                return bestDesktop;

            try
            {
                Camera main = Camera.main;
                if (main != null)
                {
                    sourceIsStereo = true;
                    if (_debugLogging.Value)
                        MelonLogger.Msg("No desktop camera found; falling back "
                            + "to Camera.main with mask scrub.");
                    return main;
                }
            }
            catch (Exception) { }

            return null;
        }

        private static bool IsOffCameraName(string goName)
        {
            if (string.IsNullOrEmpty(goName))
                return false;
            string lower = goName.ToLowerInvariant();
            for (int i = 0; i < OffCameraNameFragments.Length; i++)
            {
                if (lower.Contains(OffCameraNameFragments[i]))
                    return true;
            }
            return false;
        }

        private static int PopCount(int value)
        {
            uint v = (uint)value;
            int count = 0;
            while (v != 0)
            {
                count += (int)(v & 1u);
                v >>= 1;
            }
            return count;
        }

        private Transform FindHeadTransform()
        {
            try
            {
                Camera main = Camera.main;
                if (main != null && main.stereoEnabled)
                    return main.transform;
            }
            catch (Exception) { }

            try
            {
                var cams = Camera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    Camera c = cams[i];
                    if (c == null)
                        continue;
                    bool stereo = false;
                    try { stereo = c.stereoEnabled; }
                    catch (Exception) { }
                    if (stereo)
                        return c.transform;
                }
            }
            catch (Exception) { }
            return null;
        }

        // Play-space origin for External (calibrated) cameras. Walk up from
        // the headset looking for the rig root by name candidates; hierarchy
        // confirmed on both branches (probe logs, 14-07-2026):
        // "XR Master/XR Origin/.../Headset Camera".
        private static readonly string[] RigRootNameCandidates = new string[]
        {
            "xr origin", "xr rig", "play space", "playspace"
        };

        private Transform FindRigRoot(Transform head)
        {
            if (head == null)
                return null;
            Transform t = head.parent;
            int guard = 0;
            while (t != null && guard < 64)
            {
                string lower = t.name.ToLowerInvariant();
                for (int i = 0; i < RigRootNameCandidates.Length; i++)
                {
                    if (lower.Contains(RigRootNameCandidates[i]))
                        return t;
                }
                t = t.parent;
                guard++;
            }
            return null;
        }

        // v0.5 diagnostic (F8): which layers do the game's renderers actually
        // occupy? The visibility toggles can only work if notes/walls/etc.
        // really sit on the semantically-named layers. Press in the menu and
        // again mid-song; the mid-song dump is the one that matters.
        private void DumpLayerUsage()
        {
            MelonLogger.Msg("==== Layer usage (scene renderers) ====");
            try
            {
                int[] counts = new int[32];
                int[] sampleCounts = new int[32];
                string[][] samples = new string[32][];
                for (int i = 0; i < 32; i++)
                    samples[i] = new string[4];

                Renderer[] rends = Resources.FindObjectsOfTypeAll<Renderer>();
                if (rends != null)
                {
                    for (int i = 0; i < rends.Length; i++)
                    {
                        Renderer r = rends[i];
                        if (r == null)
                            continue;
                        bool inScene = false;
                        try { inScene = r.gameObject.scene.IsValid(); }
                        catch (Exception) { }
                        if (!inScene)
                            continue;

                        int layer = r.gameObject.layer;
                        if (layer < 0 || layer > 31)
                            continue;
                        counts[layer]++;
                        if (sampleCounts[layer] < 4)
                        {
                            Transform t = r.transform;
                            string nm = t.parent != null
                                ? t.parent.name + "/" + t.name : t.name;
                            samples[layer][sampleCounts[layer]] = nm;
                            sampleCounts[layer]++;
                        }
                    }
                }

                for (int i = 0; i < 32; i++)
                {
                    if (counts[i] == 0)
                        continue;
                    string name = LayerMask.LayerToName(i);
                    if (string.IsNullOrEmpty(name))
                        name = "<unnamed>";
                    var sb = new StringBuilder();
                    sb.Append("  layer ").Append(i.ToString().PadLeft(2))
                      .Append(" ").Append(name).Append(": ")
                      .Append(counts[i]).Append(" renderer(s)  e.g. ");
                    for (int s = 0; s < sampleCounts[i]; s++)
                    {
                        if (s > 0)
                            sb.Append(" | ");
                        sb.Append(samples[i][s]);
                    }
                    MelonLogger.Msg(sb.ToString());
                }

                DumpRailSubtreeLayers();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Layer usage dump failed: " + ex);
            }
            MelonLogger.Msg("==== end layer usage ====");
        }

        private void DumpRailSubtreeLayers()
        {
            try
            {
                GameObject rm = GameObject.Find("Rail Manager(Clone)");
                if (rm == null)
                {
                    MelonLogger.Msg("  Rail Manager(Clone): not present "
                        + "(press F8 again mid-song for note/rail layers).");
                    return;
                }
                bool[] seen = new bool[32];
                CollectLayers(rm.transform, seen, 0);
                var sb = new StringBuilder("  Rail Manager(Clone) subtree layers: ");
                bool first = true;
                for (int i = 0; i < 32; i++)
                {
                    if (!seen[i])
                        continue;
                    if (!first)
                        sb.Append(", ");
                    first = false;
                    string nm = LayerMask.LayerToName(i);
                    sb.Append(i).Append(":")
                      .Append(string.IsNullOrEmpty(nm) ? "<unnamed>" : nm);
                }
                MelonLogger.Msg(sb.ToString());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("  Rail subtree layer dump failed: " + ex.Message);
            }
        }

        private void CollectLayers(Transform t, bool[] seen, int depth)
        {
            if (t == null || depth > 8)
                return;
            int layer = t.gameObject.layer;
            if (layer >= 0 && layer <= 31)
                seen[layer] = true;
            int n = t.childCount;
            for (int i = 0; i < n; i++)
                CollectLayers(t.GetChild(i), seen, depth + 1);
        }

        // Gameplay marker heuristic: the note pool root exists only in songs
        // ("Rail Manager(Clone)", verified in rail probe work). GameObject.Find
        // only sees active objects, which is what we want. Evaluated once per
        // rebuild, after scene settle.
        private bool DetectGameScene()
        {
            try
            {
                if (GameObject.Find("Rail Manager(Clone)") != null)
                    return true;
            }
            catch (Exception) { }
            return false;
        }
    }
}
