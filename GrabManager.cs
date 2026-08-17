using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace SynthCamera2
{
    // VR grab-and-place for grabbable (Static) cameras. v0.4.0.
    //
    // Controller poses and grip buttons are read through UnityEngine.XR
    // InputDevices (typed against the UnityEngine.XRModule interop). This is
    // the one path not yet probe-validated on either branch, so every read is
    // guarded: on failure the grab feature disables itself for the session
    // with a single warning instead of spamming or crashing.
    //
    // devicePosition/deviceRotation are play-space poses; they are transformed
    // through the rig root (same anchoring as External cameras).
    public class GrabManager
    {
        private class HandState
        {
            public string Name = "";
            public bool Valid;
            public bool EverValid;
            public bool DetectLogged;
            public bool GripHeld;
            public bool GripWasHeld;
            public Vector3 WorldPos;
            public Quaternion WorldRot = Quaternion.identity;
            public ManagedCamera Held;
            public Vector3 GrabOffsetPos;
            public Quaternion GrabOffsetRot = Quaternion.identity;
        }

        private readonly HandState _left = new HandState();
        private readonly HandState _right = new HandState();
        private bool _xrInputFailed;
        private float _noDeviceTimer;
        private bool _noDeviceWarned;

        // Perf (v0.5.3): gizmo active-state only changes on allow transitions
        // and camera rebuilds; don't touch it (or read activeSelf) per frame.
        private bool _lastAllow;
        private bool _gizmosApplied;

        public void NotifyCamerasRebuilt()
        {
            _gizmosApplied = false;
        }

        public GrabManager()
        {
            _left.Name = "Left";
            _right.Name = "Right";
        }

        // Returns true when a camera pose was committed (config needs saving).
        public bool Update(List<ManagedCamera> cams, Transform rig, bool allow,
            float radius, bool debugLog)
        {
            // Gizmo visibility tracks whether grabbing is currently allowed;
            // apply only on transitions and after rebuilds.
            if (allow != _lastAllow || !_gizmosApplied)
            {
                for (int i = 0; i < cams.Count; i++)
                    cams[i].SetGizmosActive(allow && cams[i].IsGrabbable());
                _lastAllow = allow;
                _gizmosApplied = true;
            }

            if (!allow || _xrInputFailed)
            {
                bool d1 = ForceRelease(_left);
                bool d2 = ForceRelease(_right);
                return d1 || d2;
            }

            // No grabbable cameras -> skip all XR interop reads this frame.
            bool anyGrabbable = false;
            for (int i = 0; i < cams.Count; i++)
            {
                if (cams[i].IsGrabbable() && cams[i].IsAlive())
                {
                    anyGrabbable = true;
                    break;
                }
            }
            if (!anyGrabbable)
            {
                bool d1 = ForceRelease(_left);
                bool d2 = ForceRelease(_right);
                return d1 || d2;
            }

            ReadHand(UnityEngine.XR.XRNode.LeftHand, _left, rig, debugLog);
            ReadHand(UnityEngine.XR.XRNode.RightHand, _right, rig, debugLog);

            // Diagnosis for the silent-failure case: OpenXR reporting no
            // valid devices at the hand nodes. One warning after 10 seconds,
            // then quiet.
            if (!_noDeviceWarned && !_left.EverValid && !_right.EverValid)
            {
                _noDeviceTimer += Time.unscaledDeltaTime;
                if (_noDeviceTimer >= 10f)
                {
                    _noDeviceWarned = true;
                    MelonLogger.Warning("XR InputDevices reports no valid hand "
                        + "devices after 10s; grab input needs a fallback path "
                        + "on this setup. Send this log.");
                }
            }

            bool dirty = false;
            dirty = ProcessHand(_left, cams, radius, debugLog) || dirty;
            dirty = ProcessHand(_right, cams, radius, debugLog) || dirty;

            if (_left.Valid || _right.Valid)
                UpdateTints(cams, radius);
            return dirty;
        }

        public void ReleaseAll()
        {
            ForceRelease(_left);
            ForceRelease(_right);
        }

        // ------------------------------------------------------------------

        private void ReadHand(UnityEngine.XR.XRNode node, HandState hs,
            Transform rig, bool debugLog)
        {
            hs.GripWasHeld = hs.GripHeld;
            try
            {
                var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
                if (!dev.isValid)
                {
                    hs.Valid = false;
                    hs.GripHeld = false;
                    return;
                }

                Vector3 pos;
                if (!dev.TryGetFeatureValue(
                    UnityEngine.XR.CommonUsages.devicePosition, out pos))
                {
                    hs.Valid = false;
                    hs.GripHeld = false;
                    return;
                }

                Quaternion rot;
                if (!dev.TryGetFeatureValue(
                    UnityEngine.XR.CommonUsages.deviceRotation, out rot))
                    rot = Quaternion.identity;

                // Suite-proven pattern (SynthRidersTwitchChat v1.2.1):
                // analog grip with hysteresis (>0.75 press, <0.35 release)
                // is the reliable read across runtimes; gripButton bool is
                // the fallback when the analog usage is unavailable.
                bool grip;
                float gripVal;
                if (dev.TryGetFeatureValue(
                    UnityEngine.XR.CommonUsages.grip, out gripVal))
                {
                    grip = hs.GripWasHeld ? (gripVal > 0.35f) : (gripVal > 0.75f);
                }
                else
                {
                    grip = false;
                    dev.TryGetFeatureValue(
                        UnityEngine.XR.CommonUsages.gripButton, out grip);
                }

                if (rig != null)
                {
                    hs.WorldPos = rig.TransformPoint(pos);
                    hs.WorldRot = rig.rotation * rot;
                }
                else
                {
                    hs.WorldPos = pos;
                    hs.WorldRot = rot;
                }
                hs.GripHeld = grip;
                hs.Valid = true;

                if (!hs.EverValid)
                {
                    hs.EverValid = true;
                    if (debugLog && !hs.DetectLogged)
                    {
                        hs.DetectLogged = true;
                        MelonLogger.Msg(hs.Name + " controller detected: \""
                            + dev.name + "\" (pose + grip tracking active).");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_xrInputFailed)
                {
                    _xrInputFailed = true;
                    MelonLogger.Warning("XR controller input read failed ("
                        + ex.Message + "); grab-and-move disabled this session. "
                        + "Send this log so a fallback input path can be added.");
                }
                hs.Valid = false;
                hs.GripHeld = false;
            }
        }

        private bool ProcessHand(HandState hs, List<ManagedCamera> cams,
            float radius, bool debugLog)
        {
            if (hs.Held != null)
            {
                if (!hs.Held.IsAlive())
                {
                    // Rebuild destroyed the camera mid-hold.
                    hs.Held = null;
                    return false;
                }
                if (hs.Valid && hs.GripHeld)
                {
                    hs.Held.SetWorldPose(
                        hs.WorldPos + hs.WorldRot * hs.GrabOffsetPos,
                        hs.WorldRot * hs.GrabOffsetRot);
                    return false;
                }
                // Grip released (or tracking lost): commit and let go.
                hs.Held.CommitPoseToDef();
                if (debugLog)
                    MelonLogger.Msg("[" + hs.Held.Def.Name + "] placed at "
                        + hs.Held.GetWorldPos().ToString("F2"));
                hs.Held = null;
                return true;
            }

            if (!hs.Valid || !hs.GripHeld || hs.GripWasHeld)
                return false;

            // Grip pressed this frame: grab the nearest grabbable in reach.
            ManagedCamera nearest = FindNearest(cams, hs.WorldPos, radius);
            if (nearest == null)
            {
                if (debugLog)
                {
                    ManagedCamera anyNearest = FindNearest(cams, hs.WorldPos,
                        float.MaxValue);
                    if (anyNearest != null)
                        MelonLogger.Msg(hs.Name + " grip pressed: nearest camera \""
                            + anyNearest.Def.Name + "\" at "
                            + Vector3.Distance(anyNearest.GetWorldPos(),
                                hs.WorldPos).ToString("F2")
                            + "m (grab radius " + radius.ToString("F2") + "m).");
                    else
                        MelonLogger.Msg(hs.Name + " grip pressed: no grabbable "
                            + "cameras exist.");
                }
                return false;
            }

            Quaternion invRot = Quaternion.Inverse(hs.WorldRot);
            hs.GrabOffsetPos = invRot * (nearest.GetWorldPos() - hs.WorldPos);
            hs.GrabOffsetRot = invRot * nearest.GetWorldRot();
            hs.Held = nearest;
            return false;
        }

        private bool ForceRelease(HandState hs)
        {
            if (hs.Held == null)
                return false;
            bool dirty = false;
            if (hs.Held.IsAlive())
            {
                hs.Held.CommitPoseToDef();
                dirty = true;
            }
            hs.Held = null;
            return dirty;
        }

        private ManagedCamera FindNearest(List<ManagedCamera> cams,
            Vector3 pos, float radius)
        {
            ManagedCamera best = null;
            float bestDist = radius;
            for (int i = 0; i < cams.Count; i++)
            {
                ManagedCamera mc = cams[i];
                if (!mc.IsGrabbable() || !mc.IsAlive())
                    continue;
                if (mc == _left.Held || mc == _right.Held)
                    continue;
                float d = Vector3.Distance(mc.GetWorldPos(), pos);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = mc;
                }
            }
            return best;
        }

        private void UpdateTints(List<ManagedCamera> cams, float radius)
        {
            for (int i = 0; i < cams.Count; i++)
            {
                ManagedCamera mc = cams[i];
                if (!mc.IsGrabbable() || !mc.IsAlive())
                    continue;

                int state = 0; // idle
                if (mc == _left.Held || mc == _right.Held)
                {
                    state = 2; // held
                }
                else
                {
                    Vector3 p = mc.GetWorldPos();
                    bool near =
                        (_left.Valid && Vector3.Distance(_left.WorldPos, p) <= radius)
                        || (_right.Valid && Vector3.Distance(_right.WorldPos, p) <= radius);
                    if (near)
                        state = 1; // in reach
                }
                mc.SetGizmoTint(state);
            }
        }
    }
}
