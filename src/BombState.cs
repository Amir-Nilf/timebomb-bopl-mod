using System.Collections.Generic;
using BoplFixedMath;
using UnityEngine;

namespace TimeBomb
{
    // Who is carrying the bomb, how long they have left, and the tag that passes it on.
    //
    // The countdown is a whole-tick integer, never accumulated float seconds: Bopl Battle
    // runs a deterministic lockstep simulation with replays and network checksums, so a
    // float here would drift a replay apart even when it looked right locally.
    public static class BombState
    {
        private static int carrierId = -1;
        private static int ticksLeft;
        private static int passImmunityTicks;

        // Which (player, ability slot) pairs have already spent their one use this round.
        private static readonly HashSet<long> spent = new HashSet<long>();

        // The blast used when the fuse runs out, borrowed from the native grenade so the
        // visuals, camera shake and kill logic are all the game's own.
        private static Explosion explosionPrefab;

        public static bool HasCarrier => carrierId >= 0;
        public static int CarrierId => carrierId;

        // Raw ticks remaining. The visuals read this to decide how hard the bomb thumps
        // and how fast the fuse burns; whole seconds are too coarse for either.
        public static int TicksLeft => ticksLeft;

        // Whole seconds remaining, which is what the counter above the carrier shows.
        public static int SecondsLeft
        {
            get
            {
                if (ticksLeft <= 0)
                {
                    return 0;
                }
                // Rounded up, so "1" means "less than a second to go" rather than flashing
                // 0 for a whole second before it blows.
                return Mathf.Clamp(
                    (ticksLeft + TimeBombAbility.TicksPerSecond - 1) / TimeBombAbility.TicksPerSecond,
                    0, 8);
            }
        }

        public static void SetExplosionPrefab(Explosion prefab)
        {
            explosionPrefab = prefab;
        }

        public static long SlotKey(int playerId, int abilityIndex)
        {
            return (long)playerId * 16L + abilityIndex;
        }

        public static bool IsSpent(long key)
        {
            return spent.Contains(key);
        }

        public static void Arm(int playerId, long slotKey)
        {
            carrierId = playerId;
            ticksLeft = TimeBombAbility.FuseTicks;
            passImmunityTicks = TimeBombAbility.PassImmunityTicks;
            spent.Add(slotKey);
            Plugin.Log.LogInfo($"Time Bomb: player {playerId} armed the bomb "
                               + $"({TimeBombAbility.FuseTicks} ticks / 8s).");
        }

        // Driven once per simulation tick from a patch on Updater.TickSimulation. Not a
        // MonoUpdatable of our own: Updater.PreLevelLoad calls updatables.Clear(), so
        // anything registered before a level loads silently stops ticking.
        // True once the fuse is into its final seconds -- the point the countdown turns red,
        // the bomb thumps twice a second, and the carrier gets a turn of speed.
        public static bool IsCritical =>
            carrierId >= 0 && ticksLeft > 0 && ticksLeft <= TimeBombAbility.CriticalTicks;

        public static void Tick()
        {
            if (carrierId < 0)
            {
                BombSpeed.Clear();
                return;
            }
            BombSpeed.Apply(carrierId, IsCritical);

            PlayerHandler handler = PlayerHandler.Get();
            if (handler == null)
            {
                return;
            }

            Player carrier = handler.GetPlayer(carrierId);
            if (carrier == null || !carrier.IsAlive)
            {
                // Carrier died some other way; the bomb dies with them.
                Plugin.Log.LogInfo($"Time Bomb: carrier {carrierId} is gone, so the bomb was defused.");
                Clear();
                return;
            }

            if (passImmunityTicks > 0)
            {
                passImmunityTicks--;
            }
            else
            {
                TryPassBomb(handler, carrier);
            }

            ticksLeft--;
            if (ticksLeft <= 0)
            {
                Detonate(carrier);
            }
        }

        // Tag: whoever the carrier touches becomes "it", and the fuse restarts.
        //
        // Measured between BODIES rather than between Players. `Player.Position` is a single
        // point, and it is the wrong point whenever someone is inside an ability: the slime
        // sitting there is switched off and the ability object is where they really are. A
        // player riding the missile is the clearest case -- they are across the map from the
        // position the tag test was reading, so the bomb could not be handed to them at all.
        // Clones are the same problem from the other side: a duplicated player has several
        // bodies and only one of them can be at Player.Position.
        private static void TryPassBomb(PlayerHandler handler, Player carrier)
        {
            Fix radiusSquared = TimeBombAbility.TagRadius * TimeBombAbility.TagRadius;
            List<Vec2> carrierBodies = PlayerBodies.PositionsOf(carrierId);
            if (carrierBodies.Count == 0)
            {
                // Nothing of the carrier's is in the world to touch anyone with. Fall back to
                // the player's own position rather than making the bomb untransferable.
                carrierBodies.Add(carrier.Position);
            }

            foreach (Player other in handler.PlayerList())
            {
                if (other == null || !other.IsAlive || other.Id == carrierId)
                {
                    continue;
                }
                List<Vec2> otherBodies = PlayerBodies.PositionsOf(other.Id);
                if (otherBodies.Count == 0)
                {
                    otherBodies.Add(other.Position);
                }
                if (!AnyWithin(carrierBodies, otherBodies, radiusSquared))
                {
                    continue;
                }

                int previous = carrierId;
                carrierId = other.Id;
                ticksLeft = TimeBombAbility.FuseTicks;
                passImmunityTicks = TimeBombAbility.PassImmunityTicks;
                Plugin.Log.LogInfo($"Time Bomb: player {previous} passed the bomb to player {other.Id}; "
                                   + "fuse reset to 8s.");
                return;
            }
        }

        // The fuse ran out. The carrier dies and the blast goes off around them.
        //
        // The carrier is killed OUTRIGHT here rather than being left to the explosion to
        // catch. Leaving it to the blast is how it used to work and it had a hole in it:
        // inside the platform-control ability the bomb went off and the carrier walked away.
        // Their slime is deactivated while they are in an ability and the ability object
        // stands in for it, and whatever the native Explosion tests for did not find it.
        //
        // Guessing at which layer or collider the blast missed would be guessing. This
        // ability's rule does not need a physics query to express: when your fuse runs out
        // you die, wherever you are and whatever you are doing. The explosion stays for the
        // spectacle and for catching everyone standing nearby.
        // True when any body of one player is within reach of any body of the other.
        private static bool AnyWithin(List<Vec2> mine, List<Vec2> theirs, Fix radiusSquared)
        {
            foreach (Vec2 here in mine)
            {
                foreach (Vec2 there in theirs)
                {
                    if (Vec2.SqrMagnitude(there - here) <= radiusSquared)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void Detonate(Player carrier)
        {
            Vec2 position = carrier.Position;
            int victim = carrierId;
            Clear();

            // Every body of theirs, not one: a duplicated carrier is several objects sharing
            // an id, and the fuse was attached to the player rather than to any one of them.
            int bodiesSeen;
            // respectProtection: false. The block ability protects its user by removing
            // their hurtbox, which stops anything being thrown or fired AT them -- but the
            // bomb is not aimed at anybody, it is strapped to them. Hiding inside a block
            // with a lit fuse should not defuse it, or the block is a free answer to the
            // whole ability.
            int killed = PlayerBodies.KillAll(victim, victim, CauseOfDeath.Other, out bodiesSeen,
                                              respectProtection: false);
            if (killed == 0)
            {
                PlayerBodies.WarnIfBlind(bodiesSeen, "Time Bomb");
                Plugin.Log.LogWarning($"Time Bomb: the fuse ran out on player {victim} but none of "
                                      + $"their {bodiesSeen} bodies could be killed, so they are "
                                      + "relying on the blast to catch them.");
            }

            if (explosionPrefab == null)
            {
                Plugin.Log.LogWarning($"Time Bomb: no explosion prefab available, so player {victim} "
                                      + "was not caught in a blast.");
                return;
            }

            Explosion blast = FixTransform.InstantiateFixed(explosionPrefab, position);
            if (blast != null)
            {
                blast.PlayerOwnerId = victim;
            }
            Plugin.Log.LogInfo($"Time Bomb: the fuse ran out on player {victim} "
                               + $"({killed} of {bodiesSeen} bodies killed outright).");
        }

        public static void Clear()
        {
            BombSpeed.Clear();
            carrierId = -1;
            ticksLeft = 0;
            passImmunityTicks = 0;
        }

        // New round: the bomb is gone and everyone's single use is refreshed.
        public static void ResetForNextStage()
        {
            Clear();
            spent.Clear();
        }
    }
}
