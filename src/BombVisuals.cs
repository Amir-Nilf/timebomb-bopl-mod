using UnityEngine;

namespace TimeBomb
{
    // Draws the countdown digit above whoever is carrying the bomb.
    //
    // Synced once per simulation tick from the same patch that runs the countdown, rather
    // than from a MonoBehaviour Update loop -- a plain MonoBehaviour created by a plugin was
    // found not to tick at all in this game. Presentation only: it reads state and never
    // writes it, so the floats here cannot reach the lockstep simulation.
    public static class BombVisuals
    {
        private static Sprite[] digits;
        private static bool loadAttempted;
        private static bool loadFailed;

        private static GameObject counter;
        private static SpriteRenderer counterRenderer;
        private static int shownDigit = -1;

        private static Sprite[] bombFrames;
        private static GameObject heldBomb;
        private static SpriteRenderer heldBombRenderer;
        // How far through the fuse's burn loop we are. Accumulated rather than divided out
        // of the tick count because the loop speeds up as the countdown runs down, and a
        // rate that changes underneath a division makes the frame number jump backwards.
        //
        // A float, and safely so: it decides which picture is showing and nothing else.
        private static float fusePhase;

        public static void Sync()
        {
            if (loadFailed)
            {
                return;
            }
            if (!loadAttempted && !LoadDigits())
            {
                return;
            }

            if (!BombState.HasCarrier)
            {
                Hide();
                return;
            }

            SpriteRenderer target = FindPlayerSprite(BombState.CarrierId);
            if (target == null)
            {
                Hide();
                return;
            }

            if (counter == null)
            {
                counter = new GameObject("TimeBombCounter");
                counterRenderer = counter.AddComponent<SpriteRenderer>();
                shownDigit = -1;
            }

            if (heldBomb == null && bombFrames != null)
            {
                heldBomb = new GameObject("TimeBombHeld");
                heldBombRenderer = heldBomb.AddComponent<SpriteRenderer>();
            }

            counter.SetActive(true);

            // Everything below is placed in the CARRIER's frame rather than in world space.
            //
            // That is the whole reason the bomb used to look stuck on: it was offset by a
            // fixed world x and y, and players in this game rotate to stand on the side and
            // the underside of platforms. Run along a wall and the bomb stayed politely to
            // the east of you, floating off your shoulder at whatever angle you happened to
            // be at. Rotating the offset with the player -- and the bomb with it -- is what
            // makes it look carried rather than stuck on with tape.
            Quaternion carrierFrame = target.transform.rotation;
            float scale = PlayerScale();

            // The number stays upright however the carrier is standing, because it is
            // something to READ, but it is offset along their own up so it is still over
            // their head when they are sideways on a wall.
            counter.transform.position = target.transform.position
                + carrierFrame * (Vector3.up * TimeBombAbility.DigitHeightAbovePlayer * scale);
            counter.transform.localScale = new Vector3(scale, scale, 1f);
            counterRenderer.sortingLayerID = target.sortingLayerID;
            counterRenderer.sortingOrder = target.sortingOrder + 2;

            // The bomb sits low and off to the side the carrier is facing, so it looks
            // tucked under an arm rather than pasted over the middle of them.
            if (heldBomb != null)
            {
                heldBomb.SetActive(true);
                float facing = FacingSign(BombState.CarrierId);

                heldBomb.transform.position = target.transform.position + carrierFrame
                    * new Vector3(TimeBombAbility.HeldBombSideOffset * facing * scale,
                                  TimeBombAbility.HeldBombHeightAbovePlayer * scale, 0f);
                heldBomb.transform.rotation = carrierFrame;

                // A heartbeat, once a second and twice a second in the last three, hitting
                // harder as the fuse runs down. Mirrored on the x so the fuse always trails
                // away from the player.
                float beat = Pulse();
                heldBomb.transform.localScale =
                    new Vector3(facing * scale * beat, scale * beat, 1f);

                heldBombRenderer.sortingLayerID = target.sortingLayerID;
                heldBombRenderer.sortingOrder = target.sortingOrder + 1;
                heldBombRenderer.sprite = bombFrames[NextFuseFrame()];
            }

            int seconds = BombState.SecondsLeft;
            if (seconds != shownDigit)
            {
                shownDigit = seconds;
                counterRenderer.sprite = digits[Mathf.Clamp(seconds, 0, digits.Length - 1)];
            }
        }

        public static void Hide()
        {
            if (counter != null)
            {
                counter.SetActive(false);
            }
            if (heldBomb != null)
            {
                heldBomb.SetActive(false);
            }
            shownDigit = -1;
        }

        public static void Destroy()
        {
            if (counter != null)
            {
                Object.Destroy(counter);
            }
            if (heldBomb != null)
            {
                Object.Destroy(heldBomb);
            }
            counter = null;
            counterRenderer = null;
            heldBomb = null;
            heldBombRenderer = null;
            shownDigit = -1;
        }

        // 0 at the start of the fuse, 1 as it runs out.
        private static float Urgency()
        {
            return 1f - Mathf.Clamp01(BombState.TicksLeft / (float)TimeBombAbility.FuseTicks);
        }

        // A scale multiplier that thumps and settles, once per beat.
        //
        // Driven off the countdown itself rather than a free-running timer, so the thump
        // lands on the same tick the number above the carrier changes. Those two reading as
        // one event is most of what makes this feel like a mechanic rather than a decal.
        private static float Pulse()
        {
            int ticks = BombState.TicksLeft;
            if (ticks <= 0)
            {
                return 1f;
            }
            int period = ticks <= TimeBombAbility.PulseDoubleTimeUnder
                ? TimeBombAbility.TicksPerSecond / 2
                : TimeBombAbility.TicksPerSecond;

            // Sharp attack, quick decay: a beat, not a wobble.
            float since = (period - (ticks % period)) / (float)period;
            float strength = Mathf.Lerp(TimeBombAbility.PulseSmall, TimeBombAbility.PulseLarge,
                                        Urgency());
            return 1f + strength * Mathf.Pow(1f - since, 5f);
        }

        // Advances the fuse loop by one tick's worth and returns the frame to show.
        private static int NextFuseFrame()
        {
            float ticksPerFrame = Mathf.Lerp(TimeBombAbility.FuseFrameTicksSlow,
                                             TimeBombAbility.FuseFrameTicksFast, Urgency());
            fusePhase = (fusePhase + 1f / Mathf.Max(0.5f, ticksPerFrame))
                        % TimeBombAbility.BombFrames;
            return Mathf.Clamp((int)fusePhase, 0, TimeBombAbility.BombFrames - 1);
        }

        // The carrier's own scale, so a shrunk or grown player carries a shrunk or grown
        // bomb. Player.Scale stays correct whether they are a slime or inside an ability,
        // and unlike measured sprite bounds it cannot be fooled by them holding something
        // large at the moment it is read.
        private static float PlayerScale()
        {
            PlayerHandler handler = PlayerHandler.Get();
            Player player = handler == null ? null : handler.GetPlayer(BombState.CarrierId);
            return player == null ? 1f : Mathf.Max(0.01f, (float)player.Scale);
        }

        // +1 if the carrier faces right, -1 if left. SlimeController exposes this
        // directly; if they are mid-ability there is no slime to ask, so it holds at +1.
        private static float FacingSign(int playerId)
        {
            foreach (SlimeController slime in Object.FindObjectsOfType<SlimeController>())
            {
                if (slime.GetPlayerId() == playerId)
                {
                    return slime.isFacingRight() ? 1f : -1f;
                }
            }
            return 1f;
        }

        // A player's renderer lives on a child tagged "PlayerSprite", not on the
        // SlimeController itself, so GetComponent<SpriteRenderer>() finds nothing. When the
        // carrier is mid-ability their slime is inactive, so the ability object is checked
        // too.
        private static SpriteRenderer FindPlayerSprite(int playerId)
        {
            foreach (SlimeController slime in Object.FindObjectsOfType<SlimeController>())
            {
                if (slime.GetPlayerId() != playerId)
                {
                    continue;
                }
                SpriteRenderer sprite = slime.GetPlayerSprite();
                if (sprite != null && sprite.gameObject.activeInHierarchy)
                {
                    return sprite;
                }
            }
            foreach (Ability ability in Object.FindObjectsOfType<Ability>())
            {
                if (ability.GetPlayerId() != playerId)
                {
                    continue;
                }
                SpriteRenderer sprite = ability.GetComponent<SpriteRenderer>();
                if (sprite != null && sprite.gameObject.activeInHierarchy)
                {
                    return sprite;
                }
            }
            return null;
        }

        private static bool LoadDigits()
        {
            loadAttempted = true;
            Sprite[] loaded = new Sprite[9];
            for (int i = 0; i < loaded.Length; i++)
            {
                loaded[i] = TimeBombAbility.LoadDigit(i);
                if (loaded[i] == null)
                {
                    Plugin.Log.LogError($"Time Bomb: digit {i} failed to load, so no countdown will be "
                                        + "shown. The bomb itself still works.");
                    loadFailed = true;
                    return false;
                }
            }
            digits = loaded;

            Sprite[] bombs = new Sprite[TimeBombAbility.BombFrames];
            for (int i = 0; i < bombs.Length; i++)
            {
                bombs[i] = TimeBombAbility.LoadBombFrame(i);
                if (bombs[i] == null)
                {
                    Plugin.Log.LogWarning($"Time Bomb: bomb frame {i} failed to load, so the carrier "
                                          + "will show only the countdown.");
                    bombs = null;
                    break;
                }
            }
            bombFrames = bombs;
            Plugin.Log.LogInfo("Time Bomb: countdown digits ready.");
            return true;
        }
    }
}
