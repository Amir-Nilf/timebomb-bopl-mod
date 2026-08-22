using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace TimeBomb
{
    // Depends on BepInEx only. The ability is built by cloning one the game already has,
    // so no ability framework is involved.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.maha.boplbattle.timebomb";
        public const string PluginName = "Time Bomb";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("Time Bomb: loading...");
            new Harmony(PluginGuid).PatchAll();
            Log.LogInfo($"Time Bomb: loaded (fuse {TimeBombAbility.FuseTicks} ticks / 8s, "
                        + $"cooldown {TimeBombAbility.CooldownSeconds}s, one use per round).");
        }
    }
}
