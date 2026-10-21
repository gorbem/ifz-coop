using System.Reflection;
using HarmonyLib;
using Steamworks;

namespace EconomyConfigBridge
{
    /// <summary>
    /// IFZ Multiplayer's server browser asks Steam for a worldwide lobby list with no
    /// server-side filter, then drops rows that lack its metadata. Under Spacewar
    /// (AppID 480) there are far more than 50 public lobbies, so Steam's 50-row cap can
    /// hide every IFZ lobby. Adding a string filter on the mod's own protocol key makes
    /// Steam return only compatible IFZ lobbies.
    /// </summary>
    public static class LobbyFilterPatch
    {
        private static bool _applied;
        private static HarmonyLib.Harmony _harmony;

        public static bool TryApply()
        {
            if (_applied) return false;
            var type = AccessTools.TypeByName("IFZ.Multiplayer.GameIntegration.Steam.LobbyDirectory");
            if (type == null) return false;
            var method = AccessTools.Method(type, "RequestRefresh");
            if (method == null) return false;
            _harmony = _harmony ?? new HarmonyLib.Harmony("local.ifz.lobbyfilter");
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(LobbyFilterPatch), nameof(Prefix)));
            _applied = true;
            return true;
        }

        private static void Prefix()
        {
            // Protocol 31 is the value RC7 writes into ifzmp_proto when it advertises.
            SteamMatchmaking.AddRequestLobbyListStringFilter(
                "ifzmp_proto", "31", ELobbyComparison.k_ELobbyComparisonEqual);
        }
    }
}
