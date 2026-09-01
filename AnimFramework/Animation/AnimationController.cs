using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Shared state-machine + draw logic used by both attachment paths (the AnimatedNPC base
    /// class and the AnimationRegistry/AnimatedGlobalNPC registry path), so the two don't
    /// duplicate this logic against each other.
    /// </summary>
    internal static class AnimationController
    {
        public static bool PreDraw(NPC npc, AnimationRuntime runtime, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            // No registered set (or nothing built for this NPC yet) — fall back to vanilla
            // drawing rather than drawing nothing. See spec's Fallback/Validation Needs section.
            if (runtime == null || runtime.Set == null)
                return true;

            string state = DetermineState(npc, runtime.Set);
            AnimationClip clip;
            if (state == null || !runtime.Set.States.TryGetValue(state, out clip))
                return true; // current state has no registered clip — fall back to vanilla

            if (runtime.CurrentState != state)
            {
                runtime.CurrentState = state;
                runtime.StatePlayer.Play(clip);
            }

            DrawClip(npc, clip, runtime.StatePlayer, spriteBatch, screenPos, drawColor);
            DrawOverlays(npc, runtime, spriteBatch, screenPos, drawColor);

            return false; // we drew the NPC ourselves; suppress vanilla's own draw
        }

        /// <summary>
        /// Default state-transition logic. Baseline set per ANIMFRAMEWORK_SPEC.md is Idle,
        /// SpecialIdle, Walking, Attacking, Sitting — only Idle/Walking are wired so far
        /// (rollout step 1: prove real-time playback/rendering before adding the rest).
        /// </summary>
        private static string DetermineState(NPC npc, AnimationSet set)
        {
            // TODO (rollout step 2): Attacking, bound to the confirmed vanilla shoot/throw/magic
            // hooks (see "Vanilla Compatibility" in ANIMFRAMEWORK_SPEC.md) — not wired yet.
            // TODO (rollout step 2): Sitting, bound to vanilla's NPC.sitting struct once its
            // exact field name is confirmed against a live API reference.
            // TODO (rollout step 2): SpecialIdle, framework-original random-chance-after-N-ticks.

            if (npc.velocity != Vector2.Zero && set.States.ContainsKey("Walking"))
                return "Walking";

            return set.States.ContainsKey("Idle") ? "Idle" : null;
        }

        private static void DrawOverlays(NPC npc, AnimationRuntime runtime, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            foreach (AnimationClip overlay in runtime.Set.Overlays)
            {
                bool shouldDraw = ShouldDrawOverlay(npc, overlay);
                bool wasActive = runtime.ActiveOverlays.Contains(overlay);

                if (!shouldDraw)
                {
                    runtime.ActiveOverlays.Remove(overlay);
                    continue;
                }

                AnimationPlayer player;
                if (!runtime.OverlayPlayers.TryGetValue(overlay, out player))
                {
                    player = new AnimationPlayer();
                    runtime.OverlayPlayers[overlay] = player;
                }

                if (!wasActive)
                {
                    runtime.ActiveOverlays.Add(overlay);
                    player.Play(overlay); // fresh start each time the overlay becomes active
                }

                DrawClip(npc, overlay, player, spriteBatch, screenPos, drawColor);
            }
        }

        /// <summary>
        /// Whether a given overlay should currently be drawn. Layered on top of the current
        /// state's frame as a second draw call, not a replacement of it — matches the "layered"
        /// resolution of the Talk overlay's replace-vs-layer open question in the spec.
        /// </summary>
        private static bool ShouldDrawOverlay(NPC npc, AnimationClip overlay)
        {
            switch (overlay.State)
            {
                case "Talk":
                    return Main.LocalPlayer.talkNPC == npc.whoAmI;
                default:
                    return false; // unrecognized overlay name — no framework-known trigger yet
            }
        }

        private static void DrawClip(NPC npc, AnimationClip clip, AnimationPlayer player, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            var texture = clip.GetTexture();
            if (!texture.IsLoaded)
                return;

            Rectangle source = clip.GetSourceRect(player.GetCurrentFrame());
            Vector2 origin = clip.GetOrigin();
            SpriteEffects effects = npc.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 drawPosition = npc.Center - screenPos + new Vector2(0f, npc.gfxOffY);

            spriteBatch.Draw(texture.Value, drawPosition, source, drawColor, npc.rotation, origin, npc.scale, effects, 0f);
        }
    }
}
