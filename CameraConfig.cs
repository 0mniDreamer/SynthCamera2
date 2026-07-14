using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MelonLoader;
using MelonLoader.Utils;

namespace SynthCamera2
{
    // One entry per camera in UserData/SynthCamera2/cameras.json.
    // All layer-affecting options resolve layer NAMES at runtime, never indices.
    public class CameraDef
    {
        public string Name { get; set; } = "Camera";
        public bool Enabled { get; set; } = true;

        // "FirstPerson" = smoothed follow of the headset.
        // "Static"      = fixed world position/rotation.
        // "External"    = pose/fov/clip loaded from an externalcamera.cfg
        //                 calibration file (LIV / SteamVR MRC format),
        //                 anchored to the play-space origin (XR rig root).
        public string Type { get; set; } = "FirstPerson";

        // External type: calibration file name or absolute path. Relative
        // names are searched in UserData/SynthCamera2/ then the game root.
        public string CalibrationFile { get; set; } = "externalcamera.cfg";

        // "Always" | "MenuOnly" | "GameOnly"
        public string VisibleIn { get; set; } = "Always";

        // 0 = inherit from the game's desktop camera template.
        public float Fov { get; set; }

        // Normalized viewport rect [x, y, w, h]. Full screen = 0,0,1,1.
        // Smaller rects give picture-in-picture over whatever renders beneath.
        public float[] Rect { get; set; } = new float[] { 0f, 0f, 1f, 1f };

        // Static type: world pose. Rotation is euler degrees.
        public float[] Position { get; set; } = new float[] { 0f, 2f, -3f };
        public float[] Rotation { get; set; } = new float[] { 15f, 0f, 0f };

        // FirstPerson type: exponential smoothing speeds (higher = snappier;
        // 0 = locked to head, no smoothing). Typical: position 6, rotation 4.
        public float SmoothPosition { get; set; } = 6f;
        public float SmoothRotation { get; set; } = 4f;

        // FirstPerson type: keep the horizon level (kills head roll).
        public bool ForceUpright { get; set; } = true;

        // FirstPerson type: local offset from the head, in head space.
        public float[] Offset { get; set; } = new float[] { 0f, 0f, 0f };

        // Per-camera visibility. Each maps to layer names at runtime.
        public bool ShowNotes { get; set; } = true;
        public bool ShowWalls { get; set; } = true;
        public bool ShowTrails { get; set; } = true;
        public bool ShowAvatar { get; set; } = true;
        public bool ShowHitParticles { get; set; } = true;
        public bool ShowUI { get; set; } = true;

        // ---- v0.2: mixed reality support ----

        // "Inherit" = keep the template's clear flags (normal rendering).
        // "Chroma"  = clear to solid ChromaColor; use for MR foreground
        //             layers that get chroma-keyed in OBS. Post-processing is
        //             disabled on chroma cameras to keep the key color clean.
        public string ClearMode { get; set; } = "Inherit";

        // 0-255 RGB. Default pure green.
        public int[] ChromaColor { get; set; } = new int[] { 0, 255, 0 };

        // Clip plane overrides in meters; 0 = inherit from template.
        // MR foreground: set FarClip to your distance from the camera so only
        // objects between the camera and you are rendered.
        public float NearClip { get; set; }
        public float FarClip { get; set; }
    }

    public class CameraConfigFile
    {
        public int ConfigVersion { get; set; } = 1;
        public List<CameraDef> Cameras { get; set; } = new List<CameraDef>();
    }

    public static class ConfigLoader
    {
        public static string ConfigDir
        {
            get { return Path.Combine(MelonEnvironment.UserDataDirectory, "SynthCamera2"); }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDir, "cameras.json"); }
        }

        private static JsonSerializerOptions BuildOptions()
        {
            var opts = new JsonSerializerOptions();
            opts.WriteIndented = true;
            opts.ReadCommentHandling = JsonCommentHandling.Skip;
            opts.AllowTrailingCommas = true;
            opts.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
            return opts;
        }

        public static CameraConfigFile LoadOrCreate()
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                    Directory.CreateDirectory(ConfigDir);

                if (!File.Exists(ConfigPath))
                {
                    CameraConfigFile fresh = BuildDefault();
                    Save(fresh);
                    MelonLogger.Msg("Created default camera config at " + ConfigPath);
                    return fresh;
                }

                string json = File.ReadAllText(ConfigPath);
                CameraConfigFile loaded =
                    JsonSerializer.Deserialize<CameraConfigFile>(json, BuildOptions());
                if (loaded == null || loaded.Cameras == null)
                {
                    MelonLogger.Warning("Camera config parsed to null; using defaults "
                        + "(file left untouched).");
                    return BuildDefault();
                }
                return loaded;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Failed to load camera config (" + ex.Message
                    + "); using defaults (file left untouched).");
                return BuildDefault();
            }
        }

        public static void Save(CameraConfigFile cfg)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                    Directory.CreateDirectory(ConfigDir);
                File.WriteAllText(ConfigPath,
                    JsonSerializer.Serialize(cfg, BuildOptions()));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Failed to save camera config: " + ex.Message);
            }
        }

        private static CameraConfigFile BuildDefault()
        {
            var cfg = new CameraConfigFile();

            var fp = new CameraDef();
            fp.Name = "SmoothFirstPerson";
            fp.Type = "FirstPerson";
            fp.Enabled = true;
            fp.VisibleIn = "Always";
            fp.SmoothPosition = 6f;
            fp.SmoothRotation = 4f;
            fp.ForceUpright = true;
            cfg.Cameras.Add(fp);

            var tp = new CameraDef();
            tp.Name = "StaticThirdPerson";
            tp.Type = "Static";
            tp.Enabled = false;
            tp.VisibleIn = "GameOnly";
            tp.Position = new float[] { 1.2f, 2.2f, -3.0f };
            tp.Rotation = new float[] { 18f, -20f, 0f };
            cfg.Cameras.Add(tp);

            // MR foreground example: pair with a normal Static camera at the
            // SAME Position/Rotation/Fov. FarClip = your distance from the
            // camera. Chroma-key this feed in OBS above your keyed video.
            var mr = new CameraDef();
            mr.Name = "MRForeground";
            mr.Type = "Static";
            mr.Enabled = false;
            mr.VisibleIn = "GameOnly";
            mr.Position = new float[] { 0f, 1.6f, -2.5f };
            mr.Rotation = new float[] { 5f, 0f, 0f };
            mr.ClearMode = "Chroma";
            mr.FarClip = 2.2f;
            mr.ShowUI = false;
            mr.ShowAvatar = false;
            cfg.Cameras.Add(mr);

            // Calibrated MR pair: both read the same externalcamera.cfg, so
            // one calibration drives both layers. Background renders the full
            // scene; foreground renders only what is between the camera and
            // the player (FarClip) on chroma green.
            var mrBg = new CameraDef();
            mrBg.Name = "MRBackgroundCalibrated";
            mrBg.Type = "External";
            mrBg.Enabled = false;
            mrBg.VisibleIn = "Always";
            cfg.Cameras.Add(mrBg);

            var mrFg = new CameraDef();
            mrFg.Name = "MRForegroundCalibrated";
            mrFg.Type = "External";
            mrFg.Enabled = false;
            mrFg.VisibleIn = "Always";
            mrFg.ClearMode = "Chroma";
            mrFg.FarClip = 2.2f;
            mrFg.ShowUI = false;
            mrFg.ShowAvatar = false;
            cfg.Cameras.Add(mrFg);

            return cfg;
        }
    }
}
