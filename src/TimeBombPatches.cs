using BoplFixedMath;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace TimeBomb
{
    // Registers the Time Bomb by cloning a native *instant* ability, then stripping the
    // effect it came with.
    //
    // Cloning keeps the game's own cast flow -- entering, cooldown bookkeeping, the ability
    // indicator -- so only the effect is ours.
    [HarmonyPatch(typeof(AbilityGrid), "Awake")]
    public static class TimeBombInjectionPatch
    {
        private static bool hasInjected;

        public static void Prefix(AbilityGrid __instance)
        {
            if (hasInjected)
            {
                return;
            }

            NamedSpriteList icons = __instance.abilityIcons;
            if (icons == null || icons.sprites == null)
            {
                Plugin.Log.LogWarning("Time Bomb: AbilityGrid.abilityIcons was empty this Awake; "
                                      + "not injecting yet (will retry on the next grid build).");
                return;
            }

            // Grab the grenade's explosion prefab while the ability list is in front of us;
            // it is the blast used when the fuse runs out.
            CacheExplosionPrefab(icons);

            NamedSprite template = default(NamedSprite);
            bool found = false;
            foreach (NamedSprite sprite in icons.sprites)
            {
                if (sprite.name != null
                    && sprite.name.ToLower().Contains(TimeBombAbility.TemplateNameFragment))
                {
                    template = sprite;
                    found = true;
                    break;
                }
            }

            if (!found || template.associatedGameObject == null)
            {
                Plugin.Log.LogError("Time Bomb: could not find a native instant ability to clone, so the "
                                    + "bomb was not added. (Looked for an ability whose name contains "
                                    + $"'{TimeBombAbility.TemplateNameFragment}'.)");
                return;
            }

            GameObject clone = Object.Instantiate(template.associatedGameObject);
            Object.DontDestroyOnLoad(clone);
            clone.name = TimeBombAbility.AbilityName;

            StripTemplateEffect(clone);

            InstantAbility instant = clone.GetComponent<InstantAbility>();
            if (instant == null)
            {
                Plugin.Log.LogError("Time Bomb: the cloned ability has no InstantAbility component, so it "
                                    + "cannot be cast as an instant ability.");
                return;
            }
            instant.SetCoolDown((Fix)TimeBombAbility.CooldownSeconds);

            Sprite icon = TimeBombAbility.LoadIcon(template.sprite) ?? template.sprite;
            icons.sprites.Add(new NamedSprite(TimeBombAbility.AbilityName, icon, clone, true));
            hasInjected = true;
            Plugin.Log.LogInfo($"Time Bomb: ability injected into the select grid (cloned '{template.name}', "
                               + $"cooldown {TimeBombAbility.CooldownSeconds}s, fuse 8s).");
        }

        // The clone arrives carrying whatever the template did -- invisibility, in this
        // case. Without removing it, casting the bomb would also turn you invisible.
        private static void StripTemplateEffect(GameObject clone)
        {
            InstantAbility instant = clone.GetComponent<InstantAbility>();
            if (instant != null)
            {
                instant.funcOnEnter = new UnityEvent();
                instant.isInvisibilty = false;
                instant.EffectOnEnter = null;
            }

            Invisibility invisibility = clone.GetComponent<Invisibility>();
            if (invisibility != null)
            {
                Object.Destroy(invisibility);
            }
        }

        private static void CacheExplosionPrefab(NamedSpriteList icons)
        {
            foreach (NamedSprite sprite in icons.sprites)
            {
                if (sprite.name == null || !sprite.name.ToLower().Contains("grenade")
                    || sprite.associatedGameObject == null)
                {
                    continue;
                }
                ThrowItem2 thrower = sprite.associatedGameObject.GetComponent<ThrowItem2>();
                if (thrower == null || thrower.ItemPrefab == null)
                {
                    continue;
                }
                GrenadeExplode explode = thrower.ItemPrefab.GetComponent<GrenadeExplode>();
                if (explode == null || explode.explosion == null)
                {
                    continue;
                }
                BombState.SetExplosionPrefab(explode.explosion);
                Plugin.Log.LogInfo("Time Bomb: borrowed the grenade's explosion prefab for the fuse blast.");
                return;
            }
            Plugin.Log.LogWarning("Time Bomb: could not find the grenade's explosion prefab, so the fuse "
                                  + "running out will kill nobody.");
        }
    }

    // Casting straps the bomb to the caster.
    [HarmonyPatch(typeof(InstantAbility), "EnterAbility")]
    public static class TimeBombCastPatch
    {
        public static void Postfix(InstantAbility __instance)
        {
            if (!TimeBombAbility.IsTimeBomb(__instance.gameObject))
            {
                return;
            }
            int playerId = __instance.playerInfo.playerId;
            int slot = __instance.playerInfo.AbilityButtonUsedIndex012;
            BombState.Arm(playerId, BombState.SlotKey(playerId, slot));
            AudioManager.Get().Play("startEngine");
        }
    }

    // One use per round.
    //
    // Gated at isAbilityCastable rather than through Player.CanUseAbilities, because that
    // flag is global to the player and would disable their other ability too. This is per
    // ability slot, so the bomb greys out once spent while everything else stays live.
    [HarmonyPatch(typeof(SlimeController), "isAbilityCastable")]
    public static class TimeBombOneUsePatch
    {
        public static bool Prefix(SlimeController __instance, int abilityIndex, ref bool __result)
        {
            if (__instance.abilities == null
                || abilityIndex < 0
                || abilityIndex >= __instance.abilities.Count)
            {
                return true;
            }

            AbilityMonoBehaviour ability = __instance.abilities[abilityIndex];
            if (ability == null || !TimeBombAbility.IsTimeBomb(ability.gameObject))
            {
                return true;
            }

            long key = BombState.SlotKey(__instance.GetPlayerId(), abilityIndex);
            if (!BombState.IsSpent(key))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    // Advances the fuse and refreshes the counter, exactly once per simulation tick.
    [HarmonyPatch(typeof(Updater), nameof(Updater.TickSimulation))]
    public static class TimeBombTickPatch
    {
        public static void Postfix()
        {
            // Nothing to do with the bomb: it undoes another mod's patch that costs
            // duplicated players two lives per death. Here because plugin load order is not
            // worth betting on, and it costs one bool test once it has run.
            AbilityApiClonesFix.Ensure();

            BombState.Tick();
            BombVisuals.Sync();
        }
    }

    [HarmonyPatch(typeof(PlayerHandler), "ResetForNextStage")]
    public static class TimeBombResetPatch
    {
        public static void Prefix()
        {
            // Instance ids of bodies we have killed are held for a round; drop them.
            PlayerBodies.ForgetAll();
            BombState.ResetForNextStage();
            BombVisuals.Destroy();
        }
    }

    // Also cleared on every level load.
    //
    // ResetForNextStage alone was NOT enough: a round that ended while the fuse was still
    // running -- someone else died and finished the round early -- left the bomb armed on
    // its carrier, and the countdown simply carried on into the next round and blew them
    // up there. PreLevelLoad fires for every stage and is the dependable place to forget a
    // bomb that never got to go off. (The same gap bit Card Throw's banked volley.)
    [HarmonyPatch(typeof(Updater), nameof(Updater.PreLevelLoad))]
    public static class TimeBombLevelLoadPatch
    {
        public static void Prefix()
        {
            BombState.ResetForNextStage();
            BombVisuals.Destroy();
        }
    }

    // Death defuses the bomb the instant it happens.
    //
    // BombState.Tick already drops a bomb whose carrier is gone, but that only runs on the
    // next tick, and the tick after a round-ending death may never come. Doing it here
    // means the bomb cannot outlive its carrier even by a frame.
    [HarmonyPatch(typeof(Player), "Kill")]
    public static class TimeBombDefuseOnDeathPatch
    {
        public static void Prefix(Player __instance)
        {
            if (!BombState.HasCarrier || BombState.CarrierId != __instance.Id)
            {
                return;
            }
            Plugin.Log.LogInfo($"Time Bomb: carrier {__instance.Id} died, so the bomb was defused "
                               + "rather than left ticking.");
            BombState.Clear();
        }
    }
}
