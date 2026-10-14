using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace JonasTechExpanded
{
    /// <summary>
    /// Registers the chest piece and its behaviors, and applies the survival reach bonus. Fuel drain and the
    /// powered flag come from <see cref="FueledWearableModSystem{TItem}"/>.
    /// </summary>
    public class ExoskeletonModSystem : FueledWearableModSystem<ItemExoskeletonChest>
    {
        protected override EnumCharacterDressType Slot => EnumCharacterDressType.ArmorBody;
        protected override string PoweredAttribute => EntityBehaviorExoVisuals.PoweredAttribute;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass("ItemExoskeletonChest", typeof(ItemExoskeletonChest));
            api.RegisterEntityBehaviorClass("exoStabilized", typeof(EntityBehaviorExoStabilized));
            api.RegisterEntityBehaviorClass("exoVisuals", typeof(EntityBehaviorExoVisuals));
            api.RegisterEntityBehaviorClass("exoDamageDrain", typeof(EntityBehaviorExoDamageDrain));
            api.RegisterCollectibleBehaviorClass("exoMiningSpeed", typeof(CollectibleBehaviorExoMiningSpeed));
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            foreach (var collectible in api.World.Collectibles)
            {
                if (collectible.HasBehavior<CollectibleBehaviorExoMiningSpeed>()) continue;

                collectible.CollectibleBehaviors = collectible.CollectibleBehaviors.Append(new CollectibleBehaviorExoMiningSpeed(collectible));
            }
        }

        protected override void SetBonus(IServerPlayer player, ItemExoskeletonChest exo)
        {
            // Write absolute values rather than adding an offset, so a crash or an unclean shutdown
            // can't leave the bonus stacked onto itself.
            float range = exo == null ? GlobalConstants.DefaultPickingRange : GlobalConstants.DefaultPickingRange + exo.ReachBonus;
            if (Math.Abs(player.WorldData.PickingRange - range) < 0.0001f) return;

            player.WorldData.PickingRange = range;
            player.BroadcastPlayerData();
        }
    }
}
