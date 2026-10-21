using System;
using System.Reflection;
using HarmonyLib;

namespace EconomyConfigBridge
{
    /// <summary>
    /// A player joining a NEW campaign has no settlement yet, so the snapshot redactor
    /// must run in placement mode (clear MainHeadquarter for the joiner to found their own).
    /// RC7 passes placementMode=false on the serve path, so with human fog ON the host's
    /// hidden HQ has no repoint target and the join aborts with
    /// "MainHeadquarter ... is hidden and no repoint target is available (fail closed)".
    ///
    /// This prefix turns placement mode on only when the recipient really has no founded
    /// settlement record, so a rejoin into a campaign that already has their HQ keeps the
    /// normal repoint behaviour.
    /// </summary>
    public static class HqPlacementEligibilityPatch
    {
        private static bool _applied;
        private static HarmonyLib.Harmony _harmony;
        private static MethodInfo _tryGetRecord;
        private static FieldInfo _founded;

        public static bool TryApply()
        {
            if (_applied) return false;
            var type = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.RedactedSnapshotBuilder");
            if (type == null) return false;
            var method = AccessTools.Method(type, "Build");
            if (method == null) return false;
            try
            {
                _harmony = _harmony ?? new HarmonyLib.Harmony("local.ifz.hqplacement");
                _harmony.Patch(method, prefix: new HarmonyMethod(typeof(HqPlacementEligibilityPatch), nameof(Prefix)));
                _applied = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Harmony binds by name: recipient, placementMode.
        private static void Prefix(object recipient, ref bool placementMode)
        {
            if (placementMode) return;
            try
            {
                if (recipient != null && IsFounded(recipient)) return;
                placementMode = true;
            }
            catch (Exception) { }
        }

        private static bool IsFounded(object recipient)
        {
            try
            {
                if (_tryGetRecord == null)
                {
                    var service = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.HumanSettlementService");
                    _tryGetRecord = service == null ? null : AccessTools.Method(service, "TryGetRecord");
                }
                if (_tryGetRecord == null) return false;
                var args = new object[] { recipient, null };
                if (!(bool)_tryGetRecord.Invoke(null, args)) return false;
                var record = args[1];
                if (record == null) return false;
                _founded = _founded ?? record.GetType().GetField("Founded");
                return _founded != null && (bool)_founded.GetValue(record);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

