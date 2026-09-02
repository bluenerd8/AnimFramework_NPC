using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace AnimFramework.Animation
{
    /// <summary>
    /// Attaches AnimFramework playback to any NPC type registered via <see cref="AnimationRegistry"/>
    /// — vanilla or another mod's — without needing to own that NPC's ModNPC class. Opt-in only:
    /// <see cref="AppliesToEntity"/> gates on registry membership, so unregistered NPC types are
    /// never instanced here and cost nothing.
    /// </summary>
    public class AnimatedGlobalNPC : GlobalNPC
    {
        public override bool InstancePerEntity
        {
            get { return true; }
        }

        private AnimationRuntime _runtime;

        public override bool AppliesToEntity(NPC entity, bool lateInstantiation)
        {
            return AnimationRegistry.IsRegistered(entity.type);
        }

        private AnimationRuntime GetRuntime(NPC npc)
        {
            AnimationSet set;
            if (_runtime == null && AnimationRegistry.TryGet(npc.type, out set))
                _runtime = new AnimationRuntime(set);
            return _runtime;
        }

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            AnimationRuntime runtime = GetRuntime(npc);
            return AnimationController.PreDraw(npc, runtime, spriteBatch, screenPos, drawColor);
        }

        // Real vanilla attack-start signal for AttackType 0/1/2 (throwing/shooting/magic) — see
        // AnimationController.TriggerAttack and the spec's Vanilla Compatibility section.
        // Observation only: projType/attackDelay are never touched, so vanilla's own attack
        // behavior is completely unaffected — this hook is used purely to know an attack began.
        public override void TownNPCAttackProj(NPC npc, ref int projType, ref int attackDelay)
        {
            AnimationController.TriggerAttack(GetRuntime(npc));
        }

        // Same MemberwiseClone shared-state hazard as AnimatedNPC.Clone (see its comment) —
        // without this, every NPC instance of a registered type would share one Stopwatch.
        public override GlobalNPC Clone(NPC from, NPC to)
        {
            AnimatedGlobalNPC clone = (AnimatedGlobalNPC)base.Clone(from, to);
            clone._runtime = null;
            return clone;
        }
    }
}
