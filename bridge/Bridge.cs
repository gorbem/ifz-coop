using System;
using System.Reflection;
using BepInEx;
using Gameplay;
using Zenject;

namespace EconomyConfigBridge
{
    [BepInPlugin("local.ifz.economyconfigbridge", "IFZ EconomyConfig bridge", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private float _nextRefresh;

        private void Awake()
        {
            Logger.LogInfo("EconomyConfigBridge ready (v1.0.7)");
            try
            {
                new HarmonyLib.Harmony("local.ifz.squadfog").PatchAll(typeof(SquadFogPatch));
                Logger.LogInfo("SquadFogPatch applied");
            }
            catch (System.Exception e)
            {
                Logger.LogError("SquadFogPatch failed: " + e);
            }

            if (LobbyFilterPatch.TryApply())
                Logger.LogInfo("LobbyFilterPatch applied (ifzmp_proto=31)");
            if (SnapshotLockPatch.TryApply())
                Logger.LogInfo("SnapshotLockPatch applied (save-lock retry)");
            if (HqPlacementEligibilityPatch.TryApply())
                Logger.LogInfo("HqPlacementEligibilityPatch applied (placement for new joiners)");
            if (RedactPlacementFallbackPatch.TryApply())
                Logger.LogInfo("RedactPlacementFallbackPatch applied (null HQ -> placement)");
        }

        private void Update()
        {
            if (UnityEngine.Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = UnityEngine.Time.unscaledTime + 1f;
            SquadFogPatch.RefreshAll();
            if (LobbyFilterPatch.TryApply())
                Logger.LogInfo("LobbyFilterPatch applied (ifzmp_proto=31)");
            if (SnapshotLockPatch.TryApply())
                Logger.LogInfo("SnapshotLockPatch applied (save-lock retry)");
            if (HqPlacementEligibilityPatch.TryApply())
                Logger.LogInfo("HqPlacementEligibilityPatch applied (placement for new joiners)");
            if (RedactPlacementFallbackPatch.TryApply())
                Logger.LogInfo("RedactPlacementFallbackPatch applied (null HQ -> placement)");
        }
    }

    /// <summary>
    /// Game 0.26.10.8 added an EconomyConfig parameter to
    /// Gameplay.GameResources.MainResourcesContainer..ctor. The IFZ Multiplayer RC7 build
    /// still calls the old 4-argument constructor, so client settlement founding fails with
    /// MissingMethodException. This bridge hands the patch a real EconomyConfig instance
    /// resolved from the game's Zenject container.
    /// </summary>
    public static class Bridge
    {
        private static EconomyConfig _cached;

        public static EconomyConfig Get()
        {
            if (_cached != null) return _cached;

            var cfg = TryResolve();
            if (cfg != null)
            {
                _cached = cfg;
                return cfg;
            }

            // Last resort: a default instance keeps the constructor call valid.
            _cached = new EconomyConfig();
            return _cached;
        }

        private static EconomyConfig TryResolve()
        {
            try
            {
                var pc = ProjectContext.Instance;
                if (pc != null && pc.Container != null)
                {
                    var cfg = pc.Container.Resolve<EconomyConfig>();
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception) { }

            try
            {
                // Some scenes only have a scene container; ask every context we can find.
                foreach (var ctx in UnityEngine.Object.FindObjectsOfType<SceneContext>())
                {
                    try
                    {
                        var cfg = ctx.Container.Resolve<EconomyConfig>();
                        if (cfg != null) return cfg;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }

            return null;
        }
    }
}





