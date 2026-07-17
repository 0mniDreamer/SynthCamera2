using System;
using MelonLoader;
using UnityEngine;

namespace SynthCamera2
{
    // One runtime camera owned by the mod. The spawn recipe is exactly the
    // probe-validated sequence (SynthCameraProbe v0.2.0/v0.3.0, confirmed on
    // Unity 2021.3.45f2 and 6000.3.13, 14-07-2026):
    //   clone from the game's active desktop camera -> CopyFrom ->
    //   targetTexture null, stereoTargetEye None, depth offset ->
    //   ensure UniversalAdditionalCameraData -> allowXRRendering = false.
    public class ManagedCamera
    {
        // Name prefix lets other mods (SynthPerfFix) recognize our cameras.
        public const string GoNamePrefix = "SynthCamera2_";

        public CameraDef Def;
        public GameObject Go;
        public Camera Cam;

        private Transform _head;
        private Transform _rig;
        private Vector3 _cfgPos;
        private Quaternion _cfgRot = Quaternion.identity;
        private bool _cfgValid;
        private Vector3 _smoothedPos;
        private Quaternion _smoothedRot;
        private bool _poseInitialized;
        private bool _masterVisible = true;
        private bool _sceneVisible = true;

        // Visibility toggle -> layer names it controls (resolved at runtime).
        private static readonly string[] NotesLayers = new string[] { "Notes" };
        private static readonly string[] WallsLayers = new string[] { "WallObstacles" };
        private static readonly string[] TrailsLayers = new string[] { "ControllersTrail" };
        private static readonly string[] AvatarLayers = new string[]
        {
            "PlayerAvatar", "OculusAvatar2"
        };
        private static readonly string[] HitParticlesLayers = new string[] { "HitParticles" };
        private static readonly string[] UiLayers = new string[]
        {
            "UI", "ScoreUI", "StageUI", "ScoreUI MS", "StatusScoreData"
        };

        public ManagedCamera(CameraDef def)
        {
            Def = def;
        }

        public bool Spawn(Camera template, bool templateIsStereo, Transform head,
            Transform rigRoot, int index, bool debugLog)
        {
            _head = head;
            _rig = rigRoot;
            _poseInitialized = false;
            _cfgValid = false;

            try
            {
                Go = new GameObject(GoNamePrefix + Def.Name);
                UnityEngine.Object.DontDestroyOnLoad(Go);
                Cam = Go.AddComponent<Camera>();

                try
                {
                    Cam.CopyFrom(template);
                }
                catch (Exception ex)
                {
                    if (debugLog)
                        MelonLogger.Warning("[" + Def.Name + "] CopyFrom failed ("
                            + ex.Message + "); manual settings.");
                    Cam.nearClipPlane = 0.05f;
                    Cam.farClipPlane = 1000f;
                    Cam.clearFlags = template != null
                        ? template.clearFlags : CameraClearFlags.Skybox;
                    Cam.cullingMask = template != null ? template.cullingMask : -1;
                }

                Cam.targetTexture = null;
                Cam.stereoTargetEye = StereoTargetEyeMask.None;
                Cam.targetDisplay = 0;
                Cam.depth = (template != null ? template.depth : 0f) + 50f + index;

                // v0.3: External type loads calibration BEFORE the def
                // overrides below, so explicit def values always win.
                if (IsExternal())
                {
                    ExternalCameraCfg cfg = ExternalCameraCfg.Resolve(
                        Def.CalibrationFile, debugLog, Def.Name);
                    if (cfg != null)
                    {
                        _cfgValid = true;
                        _cfgPos = new Vector3(cfg.X, cfg.Y, cfg.Z);
                        _cfgRot = Quaternion.Euler(cfg.Rx, cfg.Ry, cfg.Rz);
                        if (cfg.Fov > 1f)
                            Cam.fieldOfView = cfg.Fov;
                        if (cfg.Near > 0f)
                            Cam.nearClipPlane = cfg.Near;
                        if (cfg.Far > 0f)
                            Cam.farClipPlane = cfg.Far;
                    }
                    else
                    {
                        MelonLogger.Warning("[" + Def.Name + "] calibration file \""
                            + Def.CalibrationFile + "\" not found or unreadable; "
                            + "falling back to Position/Rotation from config.");
                    }
                }

                if (Def.Fov > 1f)
                    Cam.fieldOfView = Def.Fov;

                // v0.2: MR clip plane overrides.
                if (Def.NearClip > 0f)
                    Cam.nearClipPlane = Def.NearClip;
                if (Def.FarClip > 0f)
                    Cam.farClipPlane = Def.FarClip;

                // v0.2: chroma background for MR foreground layers.
                bool isChroma = string.Equals(Def.ClearMode, "Chroma",
                    StringComparison.OrdinalIgnoreCase);
                if (isChroma)
                {
                    Cam.clearFlags = CameraClearFlags.SolidColor;
                    Cam.backgroundColor = new Color(
                        Mathf.Clamp01(ArrInt(Def.ChromaColor, 0, 0) / 255f),
                        Mathf.Clamp01(ArrInt(Def.ChromaColor, 1, 255) / 255f),
                        Mathf.Clamp01(ArrInt(Def.ChromaColor, 2, 0) / 255f),
                        1f);
                }

                Cam.rect = new Rect(
                    ArrIdx(Def.Rect, 0, 0f), ArrIdx(Def.Rect, 1, 0f),
                    ArrIdx(Def.Rect, 2, 1f), ArrIdx(Def.Rect, 3, 1f));

                int mask = Cam.cullingMask;
                if (templateIsStereo)
                    mask = MaskUtil.ScrubStereoMask(mask);
                mask = ApplyVisibility(mask);
                Cam.cullingMask = mask;

                // Post-processing off on chroma cameras: bloom/tonemapping
                // would contaminate the key color.
                UrpUtil.EnsureDesktopCameraData(Go, debugLog, Def.Name, !isChroma);

                if (IsStatic() || (IsExternal() && !_cfgValid))
                {
                    Go.transform.position = new Vector3(
                        ArrIdx(Def.Position, 0, 0f),
                        ArrIdx(Def.Position, 1, 2f),
                        ArrIdx(Def.Position, 2, -3f));
                    Go.transform.rotation = Quaternion.Euler(
                        ArrIdx(Def.Rotation, 0, 0f),
                        ArrIdx(Def.Rotation, 1, 0f),
                        ArrIdx(Def.Rotation, 2, 0f));
                }
                else if (IsExternal())
                {
                    ApplyExternalPose();
                }

                ApplyEnabledState();

                if (IsGrabbable())
                    CreateGizmo(debugLog);

                if (debugLog)
                    WarnMissingCustomLayers();

                if (debugLog)
                    MelonLogger.Msg("[" + Def.Name + "] spawned: type=" + Def.Type
                        + " depth=" + Cam.depth
                        + " mask=0x" + Cam.cullingMask.ToString("X8"));
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[" + Def.Name + "] spawn failed: " + ex);
                Destroy();
                return false;
            }
        }

        public void Destroy()
        {
            if (Go != null)
            {
                try { UnityEngine.Object.Destroy(Go); }
                catch (Exception) { }
            }
            Go = null;
            Cam = null;
            _head = null;
            _gizmoRoot = null;
            _gizmoRenderers = null;
        }

        public bool IsAlive()
        {
            return Cam != null;
        }

        private bool IsStatic()
        {
            return string.Equals(Def.Type, "Static", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsExternal()
        {
            return string.Equals(Def.Type, "External", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsFirstPerson()
        {
            return !IsStatic() && !IsExternal();
        }

        // ---- visibility ----------------------------------------------------

        public void SetMasterVisible(bool on)
        {
            _masterVisible = on;
            ApplyEnabledState();
        }

        // isGame: current scene classification from the mod.
        public void SetSceneVisible(bool isGame)
        {
            bool visible = true;
            if (string.Equals(Def.VisibleIn, "MenuOnly", StringComparison.OrdinalIgnoreCase))
                visible = !isGame;
            else if (string.Equals(Def.VisibleIn, "GameOnly", StringComparison.OrdinalIgnoreCase))
                visible = isGame;
            _sceneVisible = visible;
            ApplyEnabledState();
        }

        private void ApplyEnabledState()
        {
            if (Cam == null)
                return;
            Cam.enabled = Def.Enabled && _masterVisible && _sceneVisible;
        }

        private int ApplyVisibility(int mask)
        {
            mask = MaskUtil.SetLayers(mask, NotesLayers, Def.ShowNotes);
            mask = MaskUtil.SetLayers(mask, WallsLayers, Def.ShowWalls);
            mask = MaskUtil.SetLayers(mask, TrailsLayers, Def.ShowTrails);
            mask = MaskUtil.SetLayers(mask, AvatarLayers, Def.ShowAvatar);
            mask = MaskUtil.SetLayers(mask, HitParticlesLayers, Def.ShowHitParticles);
            mask = MaskUtil.SetLayers(mask, UiLayers, Def.ShowUI);
            // v0.5: explicit per-camera layer control, HideLayers wins last.
            if (Def.ShowLayers != null)
                mask = MaskUtil.SetLayers(mask, Def.ShowLayers, true);
            if (Def.HideLayers != null)
                mask = MaskUtil.SetLayers(mask, Def.HideLayers, false);
            // v0.4: grab gizmos live on HMDViewOnly; our cameras must never
            // render that layer regardless of template.
            mask = MaskUtil.SetLayers(mask, GizmoLayerNames, false);
            return mask;
        }

        // Debug aid: custom layer names that don't resolve are silently
        // skipped by SetLayers; surface them so typos are visible.
        public void WarnMissingCustomLayers()
        {
            WarnMissingIn(Def.ShowLayers, "ShowLayers");
            WarnMissingIn(Def.HideLayers, "HideLayers");
        }

        private void WarnMissingIn(string[] names, string fieldName)
        {
            if (names == null)
                return;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrEmpty(names[i]))
                    continue;
                if (LayerMask.NameToLayer(names[i]) < 0)
                    MelonLogger.Warning("[" + Def.Name + "] " + fieldName
                        + " entry \"" + names[i] + "\" is not a layer name "
                        + "in this scene (check the F8 layer dump).");
            }
        }

        // ---- v0.4: grab-and-place support -----------------------------------

        private static readonly string[] GizmoLayerNames = new string[]
        {
            "HMDViewOnly"
        };

        private GameObject _gizmoRoot;
        private Renderer[] _gizmoRenderers;
        private int _gizmoTint = -1;

        public bool IsGrabbable()
        {
            // Static only: External is calibration-locked, FirstPerson
            // follows the head.
            return IsStatic();
        }

        public Vector3 GetWorldPos()
        {
            return Go != null ? Go.transform.position : Vector3.zero;
        }

        public Quaternion GetWorldRot()
        {
            return Go != null ? Go.transform.rotation : Quaternion.identity;
        }

        public void SetWorldPose(Vector3 pos, Quaternion rot)
        {
            if (Go == null)
                return;
            Go.transform.position = pos;
            Go.transform.rotation = rot;
        }

        // Persist the current world pose into the def (Static defs are
        // world-space). Caller saves the config file.
        public void CommitPoseToDef()
        {
            if (Go == null)
                return;
            Vector3 p = Go.transform.position;
            Vector3 e = Go.transform.rotation.eulerAngles;
            Def.Position = new float[] { p.x, p.y, p.z };
            Def.Rotation = new float[] { e.x, e.y, e.z };
        }

        // Camera-shaped marker on the HMDViewOnly layer: visible in the
        // headset, never on stream. If the layer is missing on some stage,
        // skip the gizmo entirely rather than risk polluting the feed.
        private void CreateGizmo(bool debugLog)
        {
            int layer = LayerMask.NameToLayer(GizmoLayerNames[0]);
            if (layer < 0)
            {
                if (debugLog)
                    MelonLogger.Msg("[" + Def.Name + "] HMDViewOnly layer "
                        + "missing; gizmo skipped.");
                return;
            }

            try
            {
                _gizmoRoot = new GameObject("Gizmo");
                _gizmoRoot.transform.SetParent(Go.transform, false);
                _gizmoRoot.layer = layer;

                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                PrepareGizmoPart(body, layer, _gizmoRoot.transform,
                    new Vector3(0f, 0f, -0.02f),
                    new Vector3(0.14f, 0.10f, 0.16f), debugLog);

                GameObject lens = GameObject.CreatePrimitive(PrimitiveType.Cube);
                PrepareGizmoPart(lens, layer, _gizmoRoot.transform,
                    new Vector3(0f, 0f, 0.09f),
                    new Vector3(0.05f, 0.05f, 0.06f), debugLog);

                _gizmoRenderers = new Renderer[2];
                _gizmoRenderers[0] = body.GetComponent<Renderer>();
                _gizmoRenderers[1] = lens.GetComponent<Renderer>();
                _gizmoTint = -1;
                SetGizmoTint(0);
            }
            catch (Exception ex)
            {
                if (debugLog)
                    MelonLogger.Warning("[" + Def.Name + "] gizmo creation "
                        + "failed: " + ex.Message);
                _gizmoRoot = null;
                _gizmoRenderers = null;
            }
        }

        // CreatePrimitive assigns the built-in Standard shader's material,
        // which is stripped from URP IL2CPP builds -> invisible cubes
        // (observed 14-07-2026, Unity 6 branch). Resolve a shader that
        // actually exists in the build, candidates first per house rules.
        private static readonly string[] GizmoShaderCandidates = new string[]
        {
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Lit",
            "Sprites/Default",
            "UI/Default",
            "Unlit/Color",
        };

        private static Shader s_gizmoShader;
        private static bool s_gizmoShaderSearched;

        private static Shader ResolveGizmoShader(bool debugLog)
        {
            if (s_gizmoShaderSearched)
                return s_gizmoShader;
            s_gizmoShaderSearched = true;

            for (int i = 0; i < GizmoShaderCandidates.Length; i++)
            {
                try
                {
                    Shader s = Shader.Find(GizmoShaderCandidates[i]);
                    if (s != null)
                    {
                        s_gizmoShader = s;
                        if (debugLog)
                            MelonLogger.Msg("Gizmo shader resolved: \""
                                + GizmoShaderCandidates[i] + "\".");
                        return s;
                    }
                }
                catch (Exception) { }
            }
            MelonLogger.Warning("No gizmo shader found from candidates; "
                + "gizmos may be invisible. Send a debug log.");
            return null;
        }

        private static void PrepareGizmoPart(GameObject part, int layer,
            Transform parent, Vector3 localPos, Vector3 localScale, bool debugLog)
        {
            part.layer = layer;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = localScale;
            // CreatePrimitive attaches a collider; remove it so the gizmo
            // can never interact with game physics.
            Collider col = part.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.Destroy(col);

            // Replace the stripped default material with one built on a
            // shader that exists in this build.
            Shader shader = ResolveGizmoShader(debugLog);
            if (shader != null)
            {
                try
                {
                    Renderer r = part.GetComponent<Renderer>();
                    if (r != null)
                        r.material = new Material(shader);
                }
                catch (Exception ex)
                {
                    if (debugLog)
                        MelonLogger.Warning("Gizmo material assignment failed: "
                            + ex.Message);
                }
            }
        }

        public void SetGizmosActive(bool on)
        {
            if (_gizmoRoot != null)
            {
                if (_gizmoRoot.activeSelf != on)
                    _gizmoRoot.SetActive(on);
            }
        }

        // 0 = idle (white), 1 = in reach (yellow), 2 = held (green).
        public void SetGizmoTint(int state)
        {
            if (_gizmoRenderers == null || state == _gizmoTint)
                return;
            _gizmoTint = state;

            Color c = Color.white;
            if (state == 1)
                c = Color.yellow;
            else if (state == 2)
                c = Color.green;

            for (int i = 0; i < _gizmoRenderers.Length; i++)
            {
                try
                {
                    if (_gizmoRenderers[i] != null
                        && _gizmoRenderers[i].material != null)
                        _gizmoRenderers[i].material.color = c;
                }
                catch (Exception) { }
            }
        }

        // ---- per-frame follow (call from OnLateUpdate) ----------------------

        public void LateUpdateFollow(float dt)
        {
            if (Cam == null || !Cam.enabled)
                return;

            // External: re-anchor to the rig every frame so calibrated pose
            // stays correct if the play space moves or the stage offsets it.
            if (IsExternal())
            {
                if (_cfgValid)
                    ApplyExternalPose();
                return;
            }

            if (!IsFirstPerson() || _head == null)
                return;

            Vector3 targetPos = _head.position;
            Vector3 off = new Vector3(
                ArrIdx(Def.Offset, 0, 0f),
                ArrIdx(Def.Offset, 1, 0f),
                ArrIdx(Def.Offset, 2, 0f));
            if (off.sqrMagnitude > 0.000001f)
                targetPos = _head.TransformPoint(off);

            Quaternion targetRot = _head.rotation;
            if (Def.ForceUpright)
            {
                Vector3 fwd = targetRot * Vector3.forward;
                // Near-vertical forward would break LookRotation; keep last
                // smoothed rotation for those frames.
                if (Mathf.Abs(Vector3.Dot(fwd.normalized, Vector3.up)) < 0.98f)
                    targetRot = Quaternion.LookRotation(fwd, Vector3.up);
                else if (_poseInitialized)
                    targetRot = _smoothedRot;
            }

            if (!_poseInitialized)
            {
                _smoothedPos = targetPos;
                _smoothedRot = targetRot;
                _poseInitialized = true;
            }
            else
            {
                float pt = Def.SmoothPosition <= 0f
                    ? 1f : 1f - Mathf.Exp(-Def.SmoothPosition * dt);
                float rt = Def.SmoothRotation <= 0f
                    ? 1f : 1f - Mathf.Exp(-Def.SmoothRotation * dt);
                _smoothedPos = Vector3.Lerp(_smoothedPos, targetPos, pt);
                _smoothedRot = Quaternion.Slerp(_smoothedRot, targetRot, rt);
            }

            Go.transform.position = _smoothedPos;
            Go.transform.rotation = _smoothedRot;
        }

        // Transform the play-space calibration pose through the rig root.
        // No rig found -> treat calibration as world space.
        private void ApplyExternalPose()
        {
            if (Go == null)
                return;
            if (_rig != null)
            {
                Go.transform.position = _rig.TransformPoint(_cfgPos);
                Go.transform.rotation = _rig.rotation * _cfgRot;
            }
            else
            {
                Go.transform.position = _cfgPos;
                Go.transform.rotation = _cfgRot;
            }
        }

        public void ReacquireHead(Transform head)
        {
            _head = head;
            _poseInitialized = false;
        }

        private static float ArrIdx(float[] arr, int idx, float fallback)
        {
            if (arr == null || idx >= arr.Length)
                return fallback;
            return arr[idx];
        }

        private static int ArrInt(int[] arr, int idx, int fallback)
        {
            if (arr == null || idx >= arr.Length)
                return fallback;
            return arr[idx];
        }
    }

    public static class MaskUtil
    {
        // HMD-only layers removed / third-person layers added when the clone
        // template was a stereo camera. Names verified identical on both
        // branches (probe logs, 14-07-2026).
        private static readonly string[] StereoRemove = new string[]
        {
            "HMDViewOnly", "HideInMainScreen", "FIRSTPERSON_ONLY_LAYER"
        };
        private static readonly string[] StereoAdd = new string[]
        {
            "THIRDPERSON_ONLY_LAYER", "SpectatorViewOnly", "ControllersTrail",
            "MainDisplay"
        };

        public static int ScrubStereoMask(int mask)
        {
            mask = SetLayers(mask, StereoRemove, false);
            mask = SetLayers(mask, StereoAdd, true);
            return mask;
        }

        public static int SetLayers(int mask, string[] layerNames, bool on)
        {
            for (int i = 0; i < layerNames.Length; i++)
            {
                int layer = LayerMask.NameToLayer(layerNames[i]);
                if (layer < 0)
                    continue;
                if (on)
                    mask |= (1 << layer);
                else
                    mask &= ~(1 << layer);
            }
            return mask;
        }
    }

    public static class UrpUtil
    {
        // Ensure UniversalAdditionalCameraData exists and is configured for a
        // desktop-only camera. Typed access validated on BOTH branches
        // (probe logs, 14-07-2026). allowXRRendering=false is belt-and-braces:
        // the game gates HMD rendering via stereoTargetEye=None, but the URP
        // flag is the documented gate under XR Plugin Management.
        public static void EnsureDesktopCameraData(GameObject go, bool debugLog,
            string camName, bool postProcessing)
        {
            try
            {
                var data = go.GetComponent<
                    UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if (data == null)
                    data = go.AddComponent<
                        UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();

                data.allowXRRendering = false;
                data.renderPostProcessing = postProcessing;
                if (debugLog)
                    MelonLogger.Msg("[" + camName + "] URP data configured "
                        + "(allowXRRendering=false, postProcessing="
                        + postProcessing + ").");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[" + camName + "] URP data configuration failed: "
                    + ex.Message);
            }
        }
    }
}
