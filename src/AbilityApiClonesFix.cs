using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace TimeBomb
{
    // Undoes a third-party patch that takes two lives from a duplicated player instead of
    // one.
    //
    // THE BUG. Duplicate yourself, let one copy die, and the round ends as though you had
    // died outright: no win awarded in a solo game, and in a bigger one the last player
    // standing is not credited. Every win condition in GameSessionHandler reads exactly one
    // number -- Player.playersAndClonesStillAlive, through IsAlive and TeamsLeft -- and
    // Player.Kill only ends a player when that number is already 1.
    //
    // WHOSE IT IS. Not ours, and not the game's. A watcher on the counter's setter caught it
    // in the act:
    //
    //   [dupe] player 1: lives 2 -> 1
    //     via set_playersAndClonesStillAlive
    //      <- AbilityApi.Internal.Plugin+PlayerCollisionPatchOnKill.Prefix
    //      <- PlayerCollision.killPlayer
    //      <- DestroyIfOutsideSceneBounds.selfDestruct
    //
    // Ability API prefixes PlayerCollision.killPlayer and decrements the counter itself.
    // The original then runs and reaches Player.Kill, which decrements it again -- so one
    // death costs two lives and two copies die as one. (The second decrement leaves no line
    // in the log because `playersAndClonesStillAlive--` inside Player.Kill is inlined past
    // the patched setter; the value read at WinGame is what proves it happened.)
    //
    // WHY REMOVING THE PATCH IS THE RIGHT CALL HERE. Nothing in this profile needs Ability
    // API: none of these eight mods reference it -- the Freeze Ray's notes record why it was
    // rejected -- and none of the other installed mods declares it as a dependency. It also
    // throws a NullReferenceException out of its own Awake on this game version, so it is
    // already half-loaded. Only that one prefix is removed; the rest of Ability API is left
    // exactly as it is.
    //
    // Uninstalling Ability API altogether does the same job more permanently, and would be
    // the better answer if it stays unused. This lives here so the fix does not depend on
    // remembering that -- but note it therefore ALSO disappears if the Time Bomb is ever
    // uninstalled.
    public static class AbilityApiClonesFix
    {
        // Any prefix on killPlayer whose type sits in one of these namespaces goes.
        private const string Culprit = "AbilityApi";

        private static bool done;

        // Cheap enough to call every tick: after the first success it is a bool test.
        //
        // Called per tick rather than once at startup because the other mod applies its
        // patches from its own Awake, and plugin load order is not something to bet on.
        public static void Ensure()
        {
            if (done)
            {
                return;
            }

            MethodInfo original = AccessTools.Method(typeof(PlayerCollision), "killPlayer");
            if (original == null)
            {
                done = true;
                Plugin.Log.LogWarning("Time Bomb: could not find PlayerCollision.killPlayer, so the "
                                      + "Ability API duplication fix could not be applied. Duplicated "
                                      + "players may lose two lives per death.");
                return;
            }

            Patches info = Harmony.GetPatchInfo(original);
            if (info == null || info.Prefixes == null || info.Prefixes.Count == 0)
            {
                // Nobody has patched it yet. Try again next tick.
                return;
            }

            List<MethodInfo> guilty = new List<MethodInfo>();
            foreach (Patch prefix in info.Prefixes)
            {
                MethodInfo method = prefix.PatchMethod;
                if (method == null || method.DeclaringType == null)
                {
                    continue;
                }
                if (method.DeclaringType.FullName.Contains(Culprit))
                {
                    guilty.Add(method);
                }
            }

            if (guilty.Count == 0)
            {
                // Ability API is not installed, or does not patch this any more. Nothing to
                // do, and nothing has gone wrong -- stop looking.
                done = true;
                Plugin.Log.LogInfo("Time Bomb: no Ability API patch on killPlayer to undo; duplicated "
                                   + "players are the game's own business.");
                return;
            }

            Harmony harmony = new Harmony("timebomb.abilityapi.clonefix");
            foreach (MethodInfo method in guilty)
            {
                harmony.Unpatch(original, method);
                Plugin.Log.LogWarning($"Time Bomb: removed {method.DeclaringType.FullName}."
                                      + $"{method.Name} from PlayerCollision.killPlayer. It took a "
                                      + "second life from duplicated players on every death, which "
                                      + "ended rounds early and swallowed wins.");
            }
            done = true;
        }
    }
}
