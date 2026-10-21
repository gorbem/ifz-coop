using System;
using System.Collections.Generic;
using System.Reflection;
using Gameplay.Units;
using HarmonyLib;
using UI;
using UnityEngine;

namespace EconomyConfigBridge
{
    /// <summary>
    /// IFZ Multiplayer RC7 hides undiscovered players' squads in the world, but the
    /// vanilla squad list (SquadCanvas) still creates a row for them. This patch hides
    /// those rows and brings them back when the mod's own state says they are visible.
    ///
    /// Role matters:
    ///  - the host uses HostViewerFogPresentationService.IsSuppressed(id);
    ///  - a client must never hide its own squads, and hides other players' rows only
    ///    until MpRuntime.Discovery.HasDiscovered(local, owner).
    /// </summary>
    [HarmonyPatch]
    public static class SquadFogPatch
    {
        private static readonly Dictionary<string, SquadLabel> Labels = new Dictionary<string, SquadLabel>();
        private static MethodInfo _isSuppressed;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(SquadCanvas), "SpawnNewSquadLabel");
        }

        private static void Postfix(SquadCanvas __instance, Group squad)
        {
            try
            {
                if (squad == null) return;
                var mgr = AccessTools.Field(typeof(SquadCanvas), "_labelsManager").GetValue(__instance) as SquadLabelsManager;
                if (mgr == null) return;
                var label = mgr.GetLabel(squad);
                if (label == null) return;
                Labels[squad.Id] = label;

                if (ShouldHide(squad.Id))
                    label.gameObject.SetActive(false);
            }
            catch (Exception) { }
        }

        /// <summary>Called once a second by the plugin: keeps rows in sync with fog.</summary>
        public static void RefreshAll()
        {
            if (Labels.Count == 0) return;
            try
            {
                List<string> dead = null;
                foreach (var kv in Labels)
                {
                    var label = kv.Value;
                    if (label == null)
                    {
                        (dead ??= new List<string>()).Add(kv.Key);
                        continue;
                    }
                    var shouldShow = !ShouldHide(kv.Key);
                    if (label.gameObject.activeSelf != shouldShow)
                        label.gameObject.SetActive(shouldShow);
                }
                if (dead != null)
                    foreach (var id in dead) Labels.Remove(id);
            }
            catch (Exception) { }
        }

        private static bool ShouldHide(string id)
        {
            try
            {
                var runtime = ModState.Runtime;
                if (runtime == null)
                    return IsSuppressed(id);

                var local = ModState.LocalPlayerId(runtime);
                object owner;
                bool hasOwner = ModState.TryGetOwner(id, out owner);

                // Never hide the player's own squads (this was the client-side bug).
                if (hasOwner && Equals(owner, local))
                    return false;

                if (ModState.IsClient(runtime))
                {
                    if (!hasOwner) return false;
                    return !ModState.HasDiscovered(runtime, local, owner);
                }

                return IsSuppressed(id);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsSuppressed(string id)
        {
            try
            {
                if (_isSuppressed == null)
                {
                    var t = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.HostViewerFogPresentationService");
                    _isSuppressed = t == null ? null : AccessTools.Method(t, "IsSuppressed");
                }
                if (_isSuppressed == null) return false;
                return (bool)_isSuppressed.Invoke(null, new object[] { id });
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Reflection access to the mod's runtime state, so this plugin loads even if
    /// the mod is missing or its assembly changes.</summary>
    internal static class ModState
    {
        private static bool _init;
        private static PropertyInfo _instance, _local, _isClient, _discovery;
        private static MethodInfo _tryOwner, _hasDiscovered;

        private static void Init()
        {
            if (_init) return;
            _init = true;
            try
            {
                var runtime = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.MpRuntime");
                var ownership = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.OwnershipRegistry");
                var discovery = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.DiscoveryService");
                if (runtime != null)
                {
                    _instance = runtime.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    _local = runtime.GetProperty("LocalPlayerId");
                    _isClient = runtime.GetProperty("IsClient");
                    _discovery = runtime.GetProperty("Discovery");
                }
                if (ownership != null)
                    _tryOwner = ownership.GetMethod("TryGetExplicitOwner", BindingFlags.Public | BindingFlags.Static);
                if (discovery != null)
                    _hasDiscovered = discovery.GetMethod("HasDiscovered", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception) { }
        }

        public static object Runtime
        {
            get
            {
                Init();
                try { return _instance?.GetValue(null); }
                catch { return null; }
            }
        }

        public static object LocalPlayerId(object runtime)
        {
            try { return _local?.GetValue(runtime, null); }
            catch { return null; }
        }

        public static bool IsClient(object runtime)
        {
            try { return _isClient != null && (bool)_isClient.GetValue(runtime, null); }
            catch { return false; }
        }

        public static bool TryGetOwner(string id, out object owner)
        {
            owner = null;
            try
            {
                if (_tryOwner == null) return false;
                var args = new object[] { id, null };
                var ok = (bool)_tryOwner.Invoke(null, args);
                owner = args[1];
                return ok;
            }
            catch { return false; }
        }

        public static bool HasDiscovered(object runtime, object local, object other)
        {
            try
            {
                if (_hasDiscovered == null || _discovery == null) return false;
                var discovery = _discovery.GetValue(runtime, null);
                if (discovery == null) return false;
                return (bool)_hasDiscovered.Invoke(discovery, new[] { local, other });
            }
            catch { return false; }
        }
    }
}
