using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SynthCamera2.SynthCamera2Mod), "SynthCamera2", "0.7.3", "OmniDreamer")]
[assembly: MelonGame(null, null)]

namespace SynthCamera2
{

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

        // v0.7.0: layer normalization (ShowNotes/ShowUI on the updated game
        // build). Flags computed at rebuild from the built cameras' defs;
        // periodic re-sweep catches pool growth mid-song.
        private bool _needNoteNormalize;
        private bool _needUiNormalize;
        private float _normalizeTimer;
        private const float NormalizeInterval = 10f;

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

            MelonLogger.Msg("SynthCamera2 0.7.3 loaded - " + CountEnabled()
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

            // Note/rail pools can grow mid-song, spawning fresh Default-layer
            // meshes; re-sweep periodically while a camera needs it.
            if ((_needNoteNormalize || _needUiNormalize) && _cameras.Count > 0)
            {
                _normalizeTimer += Time.unscaledDeltaTime;
                if (_normalizeTimer >= NormalizeInterval)
                {
                    _normalizeTimer = 0f;
                    RunLayerNormalization("periodic");
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

            _needNoteNormalize = false;
            _needUiNormalize = false;
            for (int i = 0; i < _cameras.Count; i++)
            {
                CameraDef d = _cameras[i].Def;
                if (!d.ShowNotes)
                    _needNoteNormalize = true;
                if (!d.ShowUI)
                    _needUiNormalize = true;
            }
            _normalizeTimer = 0f;
            RunLayerNormalization("rebuild");
        }

        private void RunLayerNormalization(string reason)
        {
            try
            {
                int moved = 0;
                if (_needNoteNormalize)
                    moved += LayerNormalizer.NormalizeNotes();
                if (_needUiNormalize)
                    moved += LayerNormalizer.NormalizeUi();
                if (moved > 0 && _debugLogging.Value)
                    MelonLogger.Msg("Layer normalization (" + reason + "): "
                        + moved + " object(s) moved off Default.");
            }
            catch (Exception ex)
            {
                if (_debugLogging.Value)
                    MelonLogger.Warning("Layer normalization failed: " + ex.Message);
            }
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
