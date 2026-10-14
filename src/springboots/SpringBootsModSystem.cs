using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace JonasTechExpanded
{
    /// <summary>
    /// Registers the boots and their behaviors, and applies the survival move-speed and jump-height multipliers.
    /// Fuel drain and the powered flag come from <see cref="FueledWearableModSystem{TItem}"/>.
    /// </summary>
    public class SpringBootsModSystem : FueledWearableModSystem<ItemSpringBoots>
    {
        private const float DefaultMoveSpeedMultiplier = 1f;
        private const string JumpHeightStatKey = "springboots";

        protected override EnumCharacterDressType Slot => EnumCharacterDressType.ArmorLegs;
        protected override string PoweredAttribute => EntityBehaviorSpringBootsVisuals.PoweredAttribute;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass("ItemSpringBoots", typeof(ItemSpringBoots));
            api.RegisterEntityBehaviorClass("springbootsVisuals", typeof(EntityBehaviorSpringBootsVisuals));
            api.RegisterEntityBehaviorClass("springbootsCharge", typeof(EntityBehaviorSpringBootsCharge));
        }

        protected override void SetBonus(IServerPlayer player, ItemSpringBoots boots)
        {
            setPlayerSpeed(player, boots != null ? boots.SpeedMultiplier : DefaultMoveSpeedMultiplier);

            // A live charge owns jumpHeightMul until it is used or cancelled.
            var charge = player.Entity?.GetBehavior<EntityBehaviorSpringBootsCharge>();
            if (boots == null || charge?.IsChargeActive != true)
            {
                setJumpHeightMul(player, boots != null ? boots.JumpHeightMul : 1f);
            }
        }

        private void setPlayerSpeed(IServerPlayer player, float speedMultiplier)
        {
            if (Math.Abs(player.WorldData.MoveSpeedMultiplier - speedMultiplier) < 0.0001f) return;

            player.WorldData.MoveSpeedMultiplier = speedMultiplier;
            player.BroadcastPlayerData();
        }

        /// <summary>
        /// Writes the JSON multiplier as the jumpHeightMul offset so blended equals that value
        /// (base is 1). PModuleOnGround still takes sqrt of the blended stat.
        /// </summary>
        private void setJumpHeightMul(IServerPlayer player, float multiplier)
        {
            var stats = player.Entity?.Stats;
            if (stats == null) return;

            if (multiplier <= 1f)
            {
                stats.Remove("jumpHeightMul", JumpHeightStatKey);
                return;
            }

            stats.Set("jumpHeightMul", JumpHeightStatKey, multiplier - 1f, true);
        }
    }
}
