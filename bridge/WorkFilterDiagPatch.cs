using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace EconomyConfigBridge
{
    /// <summary>
    /// Diagnostic for "the client's workplace groups show 0/0". The mod filters the
    /// employment panel with WorkersPanelOwnershipFilterPatch.Prefix(WorkBase): it reads
    /// EconomyPartition.GetOwner(structure) and WorkAssignmentAuthority.CanUnassign(local,
    /// owner). This postfix logs what the filter sees for every work and, when the owner is
    /// unknown/invalid, lets the work through instead of hiding it.
    /// </summary>
    public static class WorkFilterDiagPatch
    {
        private static bool _applied;
        private static HarmonyLib.Harmony _harmony;
        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static MethodInfo _getRelatedStructure, _getOwner, _getLocal, _canUnassign;
        private static PropertyInfo _runtimeInstance, _structureId;
        private static FieldInfo _playerValue;

        public static bool TryApply()
        {
            if (_applied) return false;
            var type = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.Patches.WorkersPanelOwnershipFilterPatch");
            if (type == null) return false;
            var method = AccessTools.Method(type, "Prefix");
            if (method == null) return false;
            try
            {
                _harmony = _harmony ?? new HarmonyLib.Harmony("local.ifz.workfilterdiag");
                _harmony.Patch(method, postfix: new HarmonyMethod(typeof(WorkFilterDiagPatch), nameof(Postfix)));
                _applied = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Postfix(object work, ref bool __result)
        {
            try
            {
                if (work == null) return;
                var info = Inspect(work);
                var key = info;
                if (Logged.Count < 200 && Logged.Add(key))
                    Plugin.Log?.LogInfo("MP-DIAG-WORK " + info + " filterResult=" + __result);

                // Unknown owner: do not hide the work from its own player.
                if (!__result && info.Contains("ownerInvalid=True"))
                    __result = true;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("MP-DIAG-WORK failed: " + e.Message);
            }
        }

        private static string Inspect(object work)
        {
            Init();
            string structId = "?";
            object owner = null;
            try
            {
                if (_getRelatedStructure == null)
                {
                    _getRelatedStructure = work.GetType().GetMethod("GetRelatedStructure");
                    var rt = _getRelatedStructure?.ReturnType;
                    if (rt != null) _structureId = rt.GetProperty("Id");
                }
                var structure = _getRelatedStructure?.Invoke(work, null);
                if (structure != null)
                {
                    structId = _structureId?.GetValue(structure, null)?.ToString() ?? "?";
                    owner = _getOwner?.Invoke(null, new[] { structure });
                }
            }
            catch (Exception) { }

            object runtime = null;
            object local = null;
            try
            {
                runtime = _runtimeInstance?.GetValue(null, null);
                local = runtime == null ? null : _getLocal?.Invoke(runtime, null);
            }
            catch (Exception) { }

            var ownerValue = ValueOf(owner);
            var localValue = ValueOf(local);
            bool ownerInvalid = ownerValue == 0UL;
            bool can = false;
            try
            {
                if (_canUnassign != null && local != null && owner != null)
                    can = (bool)_canUnassign.Invoke(null, new[] { local, owner });
            }
            catch (Exception) { }

            return "work=" + work.GetType().Name + " struct=" + structId +
                   " owner=" + ownerValue + " local=" + localValue +
                   " ownerInvalid=" + ownerInvalid + " canUnassign=" + can;
        }

        private static ulong ValueOf(object playerId)
        {
            try
            {
                if (playerId == null) return 0UL;
                _playerValue = _playerValue ?? playerId.GetType().GetField("Value");
                return _playerValue == null ? 0UL : Convert.ToUInt64(_playerValue.GetValue(playerId));
            }
            catch (Exception) { return 0UL; }
        }

        private static void Init()
        {
            if (_getRelatedStructure != null) return;
            try
            {
                var ep = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.Patches.EconomyPartition");
                var authority = AccessTools.TypeByName("IFZ.Multiplayer.Core.WorkAssignmentAuthority");
                var runtime = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.MpRuntime");
                _getOwner = ep == null ? null : AccessTools.Method(ep, "GetOwner");
                _canUnassign = authority == null ? null : AccessTools.Method(authority, "CanUnassign");
                _runtimeInstance = runtime?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _getLocal = runtime == null ? null : AccessTools.Method(runtime, "get_LocalPlayerId");
            }
            catch (Exception) { }
        }

        public static void PrepareForWork(object work)
        {
            try
            {
                _getRelatedStructure = _getRelatedStructure ?? work?.GetType().GetMethod("GetRelatedStructure");
                if (_getRelatedStructure != null)
                {
                    var st = _getRelatedStructure.ReturnType;
                    _structureId = _structureId ?? st.GetProperty("Id");
                }
            }
            catch (Exception) { }
        }
    }
}
