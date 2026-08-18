using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace SynthCamera2
{
    // Cross-branch keyboard reads. Ported from the SynthRidersTwitchChat
    // v1.2.1 KeyInput backend (pattern proven in production).
    //
    // The recent game update switched the Unity 6 branch's Player Settings
    // to "Input System package only": every UnityEngine.Input read THROWS
    // InvalidOperationException. The Unity 2021 branch still runs legacy
    // input. Probe once at first use:
    //   legacy works  -> use it (2021 branch, zero behaviour change)
    //   legacy throws -> resolve InputSystem Keyboard via reflection with
    //                    candidate type names (no compile-time reference to
    //                    the Input System interop assembly)
    //   neither       -> keyboard shortcuts disabled for the session with
    //                    one warning; VR input (InputDevices) is a separate
    //                    layer and unaffected either way.
    public static class KeyInput
    {
        private enum Backend { Unprobed, Legacy, InputSystem, None }

        private static Backend _backend = Backend.Unprobed;

        private static PropertyInfo _kbCurrent;
        private static PropertyInfo _wasPressedProp;
        private static readonly Dictionary<KeyCode, PropertyInfo> _keyProps =
            new Dictionary<KeyCode, PropertyInfo>();

        private static readonly string[] KeyboardTypeCandidates = new string[]
        {
            "UnityEngine.InputSystem.Keyboard",
            "Il2CppUnityEngine.InputSystem.Keyboard",
        };

        // Only the keys this mod actually uses. Extend here if new
        // shortcuts land.
        private static KeyCode[] UsedKeys = new KeyCode[]
        {
            KeyCode.F8, KeyCode.F9, KeyCode.F10
        };
        private static string[] UsedKeyPropNames = new string[]
        {
            "f8Key", "f9Key", "f10Key"
        };

        public static bool GetKeyDown(KeyCode key)
        {
            if (_backend == Backend.Unprobed)
                Probe();

            if (_backend == Backend.Legacy)
            {
                try
                {
                    return Input.GetKeyDown(key);
                }
                catch (Exception)
                {
                    // Backend flipped mid-session (shouldn't happen, but a
                    // game settings change mid-run would look like this).
                    MelonLogger.Warning("Legacy input started throwing "
                        + "mid-session; re-probing input backend.");
                    _backend = Backend.Unprobed;
                    Probe();
                    return false;
                }
            }

            if (_backend == Backend.InputSystem)
                return InputSystemKeyDown(key);

            return false;
        }

        private static void Probe()
        {
            // One legacy read decides. Space is arbitrary; only the
            // throw/no-throw outcome matters.
            try
            {
                bool unused = Input.GetKeyDown(KeyCode.Space);
                _backend = Backend.Legacy;
                return;
            }
            catch (Exception)
            {
                // Input System only build: fall through to reflection.
            }

            Type kbType = FindType(KeyboardTypeCandidates);
            if (kbType == null)
            {
                _backend = Backend.None;
                MelonLogger.Warning("Legacy input unavailable and the Input "
                    + "System Keyboard type could not be resolved; keyboard "
                    + "shortcuts (F8/F9/F10) disabled this session. VR input "
                    + "is unaffected.");
                return;
            }

            try
            {
                _kbCurrent = kbType.GetProperty("current",
                    BindingFlags.Public | BindingFlags.Static);
                if (_kbCurrent == null)
                {
                    _backend = Backend.None;
                    MelonLogger.Warning("InputSystem Keyboard.current not "
                        + "found; keyboard shortcuts disabled this session.");
                    return;
                }

                for (int i = 0; i < UsedKeys.Length; i++)
                {
                    PropertyInfo pi = kbType.GetProperty(UsedKeyPropNames[i],
                        BindingFlags.Public | BindingFlags.Instance);
                    _keyProps[UsedKeys[i]] = pi;
                }

                _backend = Backend.InputSystem;
                MelonLogger.Msg("Input backend: Input System package "
                    + "(legacy input disabled by this game build).");
            }
            catch (Exception ex)
            {
                _backend = Backend.None;
                MelonLogger.Warning("Input System reflection setup failed ("
                    + ex.Message + "); keyboard shortcuts disabled this session.");
            }
        }

        private static bool InputSystemKeyDown(KeyCode key)
        {
            try
            {
                PropertyInfo keyProp;
                if (!_keyProps.TryGetValue(key, out keyProp) || keyProp == null)
                    return false;

                object kb = _kbCurrent.GetValue(null);
                if (kb == null)
                    return false;

                object control = keyProp.GetValue(kb);
                if (control == null)
                    return false;

                if (_wasPressedProp == null)
                {
                    _wasPressedProp = control.GetType().GetProperty(
                        "wasPressedThisFrame",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (_wasPressedProp == null)
                        return false;
                }

                object val = _wasPressedProp.GetValue(control);
                return val is bool && (bool)val;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Exact-name resolution across loaded assemblies (never
        // AccessTools.TypeByName -- Harmony assembly scans throw warning
        // spam from malformed IL2CPP interop types).
        private static Type FindType(string[] candidates)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int c = 0; c < candidates.Length; c++)
            {
                for (int a = 0; a < assemblies.Length; a++)
                {
                    try
                    {
                        Type t = assemblies[a].GetType(candidates[c], false);
                        if (t != null)
                            return t;
                    }
                    catch (Exception) { }
                }
            }
            return null;
        }
    }
}
