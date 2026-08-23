using System.IO;
using System.Reflection;
using BoplFixedMath;
using UnityEngine;

namespace TimeBomb
{
    // Names, tunables and sprite loading for the Time Bomb.
    public static class TimeBombAbility
    {
        // Instances the game spawns from the template pick up Unity's "(Clone)" suffix, so
        // identity is always tested with Contains rather than equality.
        public const string AbilityName = "timebomb";

        // Cloned from a native *instant* ability, because this one fires and is done -- no
        // aiming, no projectile, no held state.
        public const string TemplateNameFragment = "invis";

        // 8 seconds at the game's fixed 1/60 tick. Held as whole ticks, never float
        // seconds, so the countdown is exact under lockstep and replays.
        public const int FuseTicks = 480;
        public const int TicksPerSecond = 60;

        // Short cooldown; the real limit is that it's one use per round.
        public const int CooldownSeconds = 1;

        // Drawn slightly smaller than the ability it sits beside.
        public const float IconScale = 0.85f;

        // How close another player must be to be tagged, in world units.
        public static readonly Fix TagRadius = (Fix)1.6f;

        // After a pass, neither the giver nor the receiver can pass for this long, so the
        // bomb doesn't ping-pong between two players standing next to each other.
        public const int PassImmunityTicks = 36;

        // Digits are drawn this wide in world units, above the carrier's head. Both this
        // and the bomb's offsets below are multiplied by the carrier's own scale, so a
        // shrunk or grown player carries a shrunk or grown bomb.
        public const float DigitWorldSize = 1.6f;
        public const float DigitHeightAbovePlayer = 2.1f;

        // The bomb the carrier visibly holds, so onlookers can see who is "it" without
        // reading the number. Offset to the side they are facing rather than sitting on
        // their centre, so it reads as being carried rather than swallowed.
        //
        // This is the width of the whole CANVAS, of which the bomb itself is about 0.71 --
        // the rest is the fuse and the room the flame needs. tools/make_bomb_art.py prints
        // the exact fraction and the multiplier to use, so the number here works out to a
        // bomb roughly 1.2 units across against a player who reads as about 2.2.
        public const float HeldBombWorldSize = 1.7f;
        public const float HeldBombHeightAbovePlayer = 0.30f;
        public const float HeldBombSideOffset = 0.80f;

        // Frames of the burning fuse, and how long each is held. The fuse burns faster as
        // the countdown runs down, which is the same trick the wail's charge-up uses:
        // urgency is easier to read as a change of speed than as a change of size.
        public const int BombFrames = 3;
        public const float FuseFrameTicksSlow = 10f;
        public const float FuseFrameTicksFast = 3f;

        // The last stretch of the fuse: the countdown turns red, the bomb beats twice a
        // second instead of once, and the carrier gets a small turn of speed so they can
        // still catch somebody. 180 ticks = the final 3 seconds.
        public const int CriticalTicks = 180;

        // What the carrier's top speed becomes while the fuse is critical, against a normal
        // 19. Deliberately small: enough to run someone down who is not paying attention,
        // not enough to make being "it" an advantage.
        public static readonly Fix CriticalSpeed = (Fix)23L;

        // The colour the countdown turns for those last seconds. A multiply tint, so this
        // can only darken the white digits -- which is all a red needs to do.
        public static readonly UnityEngine.Color CriticalTint =
            new UnityEngine.Color(1f, 0.25f, 0.2f, 1f);

        // The bomb thumps once a second, and twice a second in the last three, growing
        // harder as it goes. This is what stops it looking like a sticker: a still image
        // pinned to a moving player reads as pasted on no matter how well it is drawn.
        public const float PulseSmall = 0.07f;
        public const float PulseLarge = 0.24f;
        public const int PulseDoubleTimeUnder = 180;

        public static bool IsTimeBomb(GameObject go)
        {
            return go != null && go.name.ToLower().Contains(AbilityName);
        }

        public static Sprite LoadIcon(Sprite template)
        {
            Texture2D texture = LoadTexture("TimeBomb.AbilityIcon.png");
            if (texture == null)
            {
                return null;
            }
            // Matches the world size of the ability it sits beside. Sprite.Create defaults
            // to 100 pixels per unit, which would render custom art at the wrong size.
            float pixelsPerUnit = 100f;
            if (template != null && template.rect.width > 0f)
            {
                // Dividing by IconScale raises pixels-per-unit, which shrinks the sprite:
                // world size is width / pixelsPerUnit.
                pixelsPerUnit = template.pixelsPerUnit
                                * (texture.width / template.rect.width) / IconScale;
            }
            return Build(texture, pixelsPerUnit);
        }

        // One frame of the carried bomb. HeldBomb.png itself is not shipped -- it is the
        // hand-drawn source that tools/make_bomb_art.py builds these from.
        public static Sprite LoadBombFrame(int frame)
        {
            Texture2D texture = LoadTexture($"TimeBomb.Bomb{frame}.png");
            if (texture == null)
            {
                return null;
            }
            return Build(texture, texture.width / HeldBombWorldSize);
        }

        public static Sprite LoadDigit(int digit)
        {
            Texture2D texture = LoadTexture($"TimeBomb.Digit{digit}.png");
            if (texture == null)
            {
                return null;
            }
            return Build(texture, texture.width / DigitWorldSize);
        }

        private static Sprite Build(Texture2D texture, float pixelsPerUnit)
        {
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit);
        }

        private static Texture2D LoadTexture(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    Plugin.Log.LogError($"Time Bomb: embedded resource '{resourceName}' not found.");
                    return null;
                }
                byte[] data;
                using (MemoryStream buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    data = buffer.ToArray();
                }
                Texture2D texture = new Texture2D(1, 1);
                if (!texture.LoadImage(data))
                {
                    Plugin.Log.LogError($"Time Bomb: '{resourceName}' failed to decode as a PNG.");
                    return null;
                }
                return texture;
            }
        }
    }
}
