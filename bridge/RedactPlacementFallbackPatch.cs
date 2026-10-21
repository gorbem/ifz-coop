using System;
using System.Reflection;
using HarmonyLib;

namespace EconomyConfigBridge
{
    /// <summary>
    /// Last line of defence for the fog-on join failure:
    /// "MainHeadquarter id '...' is hidden and no repoint target is available
    /// (recipientHqBuildingId=null) - aborting (fail closed)".
    ///
    /// SaveDataRedactor.Redact already supports a placement mode that clears
    /// MainHeadquarter instead of repointing it. Whatever reason the recipient HQ lookup
    /// came back empty (fresh join, sidecar restore race, stale HQ id after a reload),
    /// serving a placement snapshot is better than refusing the join. This prefix turns
    /// placement mode on exactly when there is no repoint target.
    /// </summary>
    public static class RedactPlacementFallbackPatch
    {
        private static bool _applied;
        private static HarmonyLib.Harmony _harmony;

        public static bool TryApply()
        {
            if (_applied) return false;
            var type = AccessTools.TypeByName("IFZ.Multiplayer.Persistence.SaveDataRedactor");
            if (type == null) return false;
            var method = AccessTools.Method(type, "Redact");
            if (method == null) return false;
            try
            {
                _harmony = _harmony ?? new HarmonyLib.Harmony("local.ifz.redactplacement");
                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(RedactPlacementFallbackPatch), nameof(Prefix)));
                _applied = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Harmony binds by name: recipientHqBuildingId, placementMode.
        private static void Prefix(ref string recipientHqBuildingId, ref bool placementMode)
        {
            if (string.IsNullOrEmpty(recipientHqBuildingId))
                placementMode = true;
        }
    }
}
