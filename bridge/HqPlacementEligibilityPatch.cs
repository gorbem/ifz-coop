using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using HarmonyLib;

namespace EconomyConfigBridge
{
    /// <summary>
    /// When the host loads a campaign, the joiner's settlement record lives in the save's
    /// multiplayer.json (sidecar), but the per-recipient snapshot is sometimes built before
    /// CampaignPersistence.ApplyPendingToSession restores those records. The redactor then
    /// sees recipientHqBuildingId=null, cannot repoint the host's fog-hidden HQ, and fails
    /// closed ("MainHeadquarter ... is hidden and no repoint target is available").
    ///
    /// This prefix restores the recipient's HumanSettlementRecord from the sidecar before
    /// the snapshot is built. If the save really has no founded record for them (a fresh
    /// joiner into a new campaign), it falls back to placement mode so they can found.
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

        // Harmony binds by name: recipient, placementMode, canonicalSaveFolder.
        private static void Prefix(object recipient, ref bool placementMode, string canonicalSaveFolder)
        {
            if (placementMode) return;
            try
            {
                if (recipient != null && IsFounded(recipient)) return;
                if (recipient != null && TryRestore(recipient, canonicalSaveFolder) && IsFounded(recipient))
                    return; // normal repoint with the restored HQ
                placementMode = true; // genuinely a fresh joiner
            }
            catch (Exception) { }
        }

        private static bool IsFounded(object recipient)
        {
            try
            {
                var record = GetRecord(recipient);
                if (record == null) return false;
                _founded = _founded ?? record.GetType().GetField("Founded");
                return _founded != null && (bool)_founded.GetValue(record);
            }
            catch (Exception) { return false; }
        }

        private static object GetRecord(object recipient)
        {
            if (_tryGetRecord == null)
            {
                var service = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.HumanSettlementService");
                _tryGetRecord = service == null ? null : AccessTools.Method(service, "TryGetRecord");
            }
            if (_tryGetRecord == null) return null;
            var args = new object[] { recipient, null };
            return (bool)_tryGetRecord.Invoke(null, args) ? args[1] : null;
        }

        private static bool TryRestore(object recipient, string saveFolder)
        {
            try
            {
                if (string.IsNullOrEmpty(saveFolder)) return false;
                var jsonPath = Path.Combine(saveFolder, "multiplayer.json");
                if (!File.Exists(jsonPath)) return false;

                var data = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                    .DeserializeObject(File.ReadAllText(jsonPath)) as Dictionary<string, object>;
                if (data == null || !data.ContainsKey("Players")) return false;
                var players = data["Players"] as object[];
                if (players == null) return false;

                var valueField = recipient.GetType().GetField("Value");
                if (valueField == null) return false;
                var wanted = Convert.ToUInt64(valueField.GetValue(recipient));

                Dictionary<string, object> match = null;
                foreach (var item in players)
                {
                    var p = item as Dictionary<string, object>;
                    if (p == null || !p.ContainsKey("PlayerId")) continue;
                    if (Convert.ToUInt64(p["PlayerId"]) != wanted) continue;
                    match = p;
                    break;
                }
                if (match == null) return false;
                if (!match.ContainsKey("Founded") || !Convert.ToBoolean(match["Founded"])) return false;
                var hq = match.ContainsKey("HqBuildingId") ? match["HqBuildingId"] as string : null;
                if (string.IsNullOrEmpty(hq)) return false;

                var recordType = AccessTools.TypeByName("IFZ.Multiplayer.Core.HumanSettlementRecord");
                if (recordType == null) return false;
                var ctor = recordType.GetConstructor(new[]
                {
                    recipient.GetType(), typeof(string), typeof(bool), typeof(string),
                    typeof(float), typeof(float), typeof(double)
                });
                if (ctor == null) return false;

                var name = match.ContainsKey("Name") ? match["Name"] as string : "";
                var spawnX = match.ContainsKey("SpawnX") ? (float)Convert.ToDouble(match["SpawnX"]) : 0f;
                var spawnZ = match.ContainsKey("SpawnZ") ? (float)Convert.ToDouble(match["SpawnZ"]) : 0f;
                var foundedAt = match.ContainsKey("FoundedAtGameplaySeconds")
                    ? Convert.ToDouble(match["FoundedAtGameplaySeconds"]) : 0.0;
                var record = ctor.Invoke(new object[] { recipient, name, true, hq, spawnX, spawnZ, foundedAt });

                var serviceType = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.HumanSettlementService");
                var registryField = serviceType?.GetField("_registry", BindingFlags.NonPublic | BindingFlags.Static);
                var registry = registryField?.GetValue(null);
                var upsert = registry?.GetType().GetMethod("Upsert", BindingFlags.Public | BindingFlags.Instance);
                if (registry == null || upsert == null) return false;
                upsert.Invoke(registry, new[] { record });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
