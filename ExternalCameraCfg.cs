using System;
using System.Globalization;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace SynthCamera2
{
    // Parser for the classic externalcamera.cfg calibration format written by
    // LIV, the SteamVR MRC tools, and most third-party calibrators:
    //
    //   x=0.0        (position in play space, meters)
    //   y=1.2
    //   z=-1.5
    //   rx=5.0       (rotation, euler degrees)
    //   ry=0.0
    //   rz=0.0
    //   fov=60.0     (vertical FOV, degrees)
    //   near=0.05    (optional)
    //   far=100      (optional)
    //
    // Unknown keys (sceneResolutionScale, frameSkip, ...) are ignored.
    // Lines starting with ';' or '#' are comments.
    public class ExternalCameraCfg
    {
        public float X;
        public float Y;
        public float Z;
        public float Rx;
        public float Ry;
        public float Rz;
        public float Fov = 60f;
        public float Near;
        public float Far;
        public string SourcePath = "";

        // Search order: absolute path as given -> UserData/SynthCamera2/ ->
        // game root (the traditional externalcamera.cfg location, next to
        // the game executable, where existing calibration tools write it).
        public static ExternalCameraCfg Resolve(string fileName, bool debugLog,
            string camName)
        {
            if (string.IsNullOrEmpty(fileName))
                fileName = "externalcamera.cfg";

            string[] candidates;
            if (Path.IsPathRooted(fileName))
            {
                candidates = new string[] { fileName };
            }
            else
            {
                candidates = new string[]
                {
                    Path.Combine(ConfigLoader.ConfigDir, fileName),
                    Path.Combine(MelonEnvironment.GameRootDirectory, fileName),
                };
            }

            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    if (!File.Exists(candidates[i]))
                        continue;
                    ExternalCameraCfg cfg = Parse(candidates[i]);
                    if (cfg != null)
                    {
                        if (debugLog)
                            MelonLogger.Msg("[" + camName + "] calibration loaded "
                                + "from " + candidates[i] + ": pos=(" + cfg.X + ", "
                                + cfg.Y + ", " + cfg.Z + ") rot=(" + cfg.Rx + ", "
                                + cfg.Ry + ", " + cfg.Rz + ") fov=" + cfg.Fov);
                        return cfg;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[" + camName + "] calibration file \""
                        + candidates[i] + "\" failed to load: " + ex.Message);
                }
            }
            return null;
        }

        private static ExternalCameraCfg Parse(string path)
        {
            var cfg = new ExternalCameraCfg();
            cfg.SourcePath = path;

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#"))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();

                float f;
                if (!float.TryParse(val, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out f))
                    continue;

                if (key == "x") cfg.X = f;
                else if (key == "y") cfg.Y = f;
                else if (key == "z") cfg.Z = f;
                else if (key == "rx") cfg.Rx = f;
                else if (key == "ry") cfg.Ry = f;
                else if (key == "rz") cfg.Rz = f;
                else if (key == "fov") cfg.Fov = f;
                else if (key == "near") cfg.Near = f;
                else if (key == "far") cfg.Far = f;
                // Unknown keys ignored by design.
            }
            return cfg;
        }
    }
}
