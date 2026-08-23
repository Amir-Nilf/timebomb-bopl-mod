using BoplFixedMath;
using UnityEngine;

namespace TimeBomb
{
    // The turn of speed the carrier gets in the last seconds of the fuse.
    //
    // Without it, being handed the bomb late is close to a death sentence: everyone else is
    // already running and there is not enough time to close a gap. A small boost turns the
    // final seconds into a chase instead of a formality.
    //
    // The value is deliberately modest. Enough to run down somebody who is not paying
    // attention; not enough that anyone would WANT to be holding it.
    internal static class BombSpeed
    {
        // Who is currently boosted, and what their speed was before we touched it, so it can
        // be put back exactly rather than assumed to be the default.
        private static int boostedId = -1;
        private static Fix originalSpeed;

        // Called every tick, whether or not anything should be boosted.
        //
        // Written as "state what should be true now" rather than "switch it on at the right
        // moment". A boost that is applied once and removed on an event leaks the moment any
        // of those events is missed -- a round ending mid-fuse, a death, an ability swapping
        // the body out -- and a player left permanently fast is a far worse bug than a slow
        // one. Re-stating it each tick means the worst a missed event can cost is one frame.
        public static void Apply(int carrierId, bool critical)
        {
            if (!critical || carrierId < 0)
            {
                Clear();
                return;
            }

            if (boostedId != carrierId)
            {
                // Someone else had it, or nobody did. Hand back the old one first.
                Clear();
            }

            PlayerPhysics physics = PhysicsOf(carrierId);
            if (physics == null)
            {
                return;
            }

            if (boostedId != carrierId)
            {
                boostedId = carrierId;
                originalSpeed = physics.Speed;
            }
            physics.Speed = TimeBombAbility.CriticalSpeed;
        }

        // Puts the boosted player back to the speed they had before, if there is one.
        public static void Clear()
        {
            if (boostedId < 0)
            {
                return;
            }

            PlayerPhysics physics = PhysicsOf(boostedId);
            if (physics != null)
            {
                physics.Speed = originalSpeed;
            }
            else
            {
                // Their body is gone -- they died, or the round ended. Nothing to restore to,
                // and the replacement body starts at the prefab's own speed anyway.
                Plugin.Log.LogInfo($"Time Bomb: player {boostedId} was boosted but their body is "
                                   + "gone, so there was nothing to restore.");
            }
            boostedId = -1;
        }

        // The physics of whichever body currently belongs to this player.
        //
        // Searched for rather than cached, and taken from the ACTIVE object: a player inside
        // an ability has had their slime switched off, and it is the ability object that is
        // moving. Boosting the switched-off one would do nothing at all.
        private static PlayerPhysics PhysicsOf(int playerId)
        {
            foreach (PlayerPhysics physics in Object.FindObjectsOfType<PlayerPhysics>())
            {
                if (physics == null || !physics.isActiveAndEnabled)
                {
                    continue;
                }
                IPlayerIdHolder holder = physics.GetComponent<IPlayerIdHolder>();
                if (holder != null && holder.GetPlayerId() == playerId)
                {
                    return physics;
                }
            }
            return null;
        }
    }
}
