using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SynthCamera2.SynthCamera2Mod), "SynthCamera2", "0.3.0", "OmniDreamer")]
[assembly: MelonGame(null, null)]

namespace SynthCamera2
{
    // SynthCamera2 v0.3.0 (2026-07-14)
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
    // SynthCamera2 v0.1.0 (2026-07-14)
    //
    // Camera2-style multi-camera desktop viewing for Synth Riders PCVR.
    // Single cross-branch DLL (Unity 2021.3.45f2 / 6000.3.13). Built entirely
    // on SynthCameraProbe v0.1-v0.3 findings, all dated 2026-07-14:
    //   - Clone template: the game's active desktop camera
    //     ("[Main Camera]/[ThirdPerson Custom Camera]/..."), never the
    //     Headset Camera. Fallback to Camera.main scrubs HMD-only layers.
    //   - Desktop-only rendering: stereoTargetEye=None + URP
    //     allowXRRendering=false. Confirmed zero HMD impact on both branches.
    //   - One extra camera measured at no visible frame cost (vsync-pinned
    //     8.34ms with and without, spikes symmetric) on both branches.
    //   - Camera GameObjects use the "SynthCamera2_" prefix so SynthPerfFix
    //     can learn to ignore them (it currently stands down when an unknown
    //     camera appears -- observed 2026-07-14, Unity 6 branch).
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

        private CameraConfigFile _cameraConfig;
        private readonly List<ManagedCamera> _cameras = new List<ManagedCamera>();

        private int _framesUntilRebuild = -1;
        private bool _masterEnabled = true;
        private bool _isGameScene;

        public override void OnInitializeMelon()
        {
            _cfg = MelonPreferences.CreateCategory("SynthCamera2");
            _debugLogging = _cfg.CreateEntry<bool>("DebugLogging", false);
            _rebuildDelayFrames = _cfg.CreateEntry<int>("RebuildDelayFrames", 150,
                null, "Frames after scene load before cameras are (re)built.");

            _cameraConfig = ConfigLoader.LoadOrCreate();

            MelonLogger.Msg("SynthCamera2 0.3.0 loaded - " + CountEnabled()
                + " camera(s) enabled. F9 reload config, F10 master toggle.");
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
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    _cameraConfig = ConfigLoader.LoadOrCreate();
                    MelonLogger.Msg("Config reloaded (" + CountEnabled()
                        + " camera(s) enabled); rebuilding.");
                    RebuildCameras();
                }
                if (Input.GetKeyDown(KeyCode.F10))
                {
                    _masterEnabled = !_masterEnabled;
                    for (int i = 0; i < _cameras.Count; i++)
                        _cameras[i].SetMasterVisible(_masterEnabled);
                    MelonLogger.Msg("Master toggle: cameras "
                        + (_masterEnabled ? "ON" : "OFF"));
                }
            }
            catch (Exception ex)
            {
                if (_debugLogging.Value)
                    MelonLogger.Warning("Hotkey handling failed: " + ex.Message);
            }
        }

        public override void OnLateUpdate()
        {
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
            // when the game's own display camera is off (reported 2026-07-14).
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
        }

        private void DestroyAllCameras()
        {
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
        // confirmed on both branches (probe logs, 2026-07-14):
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
