using System;
using System.IO;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace EconomyConfigBridge
{
    /// <summary>
    /// The host serves a redacted snapshot by opening the canonical save's
    /// CompressedData.zip. While the game is autosaving, that file is locked and the mod
    /// fails closed ("Sharing violation ... refusing to serve an unredacted snapshot").
    /// This prefix waits for the lock to clear and hands the builder a private copy of the
    /// save folder, so the join does not race the autosave.
    /// </summary>
    public static class SnapshotLockPatch
    {
        [ThreadStatic] private static string _tempRoot;

        private static bool _applied;
        private static HarmonyLib.Harmony _harmony;

        public static bool TryApply()
        {
            if (_applied) return false;
            var type = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.RedactedSnapshotBuilder");
            if (type == null) return false;
            var method = AccessTools.Method(type, "Build");
            if (method == null) return false;
            _harmony = _harmony ?? new HarmonyLib.Harmony("local.ifz.snapshotlock");
            _harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(SnapshotLockPatch), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(SnapshotLockPatch), nameof(Postfix)));
            _applied = true;
            return true;
        }

        // Harmony binds by parameter name; the mod's parameter is "canonicalSaveFolder".
        private static void Prefix(ref string canonicalSaveFolder)
        {
            _tempRoot = null;
            try
            {
                if (string.IsNullOrEmpty(canonicalSaveFolder) || !Directory.Exists(canonicalSaveFolder))
                    return;
                var zip = Path.Combine(canonicalSaveFolder, "CompressedData.zip");
                if (!File.Exists(zip)) return;

                var leaf = new DirectoryInfo(canonicalSaveFolder).Name;
                var tempRoot = Path.Combine(Path.GetTempPath(), "ifz-snap-" + Guid.NewGuid().ToString("N"));
                var tempSave = Path.Combine(tempRoot, leaf);

                for (int attempt = 0; attempt < 150; attempt++) // up to ~15 s
                {
                    try
                    {
                        CopyDirectory(canonicalSaveFolder, tempSave);
                        _tempRoot = tempRoot;
                        canonicalSaveFolder = tempSave;
                        return;
                    }
                    catch (IOException)
                    {
                        TryDelete(tempRoot);
                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception)
            {
                // Fall through: the mod's own error handling stays in charge.
            }
        }

        private static void Postfix()
        {
            try { if (_tempRoot != null) TryDelete(_tempRoot); }
            finally { _tempRoot = null; }
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }

        private static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch (Exception) { }
        }
    }
}
