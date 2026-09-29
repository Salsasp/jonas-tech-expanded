using Vintagestory.API.Common;

namespace JonasTechExpanded
{
    /// <summary>
    /// Attached to every collectible at asset finalize so that a powered exoskeleton speeds up mining of
    /// all block materials. The player stat "miningSpeedMul" cannot be used for this because vanilla only
    /// applies it to ore and stone.
    /// </summary>
    public class CollectibleBehaviorExoMiningSpeed : CollectibleBehavior
    {
        private readonly JonasTechExpandedModSystem modSys;

        public CollectibleBehaviorExoMiningSpeed(CollectibleObject collObj, JonasTechExpandedModSystem modSys) : base(collObj)
        {
            this.modSys = modSys;
        }

        public override float GetMiningSpeed(ItemStack itemstack, BlockSelection blockSel, Block block, IPlayer forPlayer, ref EnumHandling bhHandling)
        {
            var exo = modSys.GetPoweredExo(forPlayer);
            if (exo == null) return 1f;

            // Handled rather than PreventDefault, so this multiplies on top of the tool's own
            // per-material mining speed instead of replacing it.
            bhHandling = EnumHandling.Handled;
            return exo.MiningSpeedMultiplier;
        }
    }
}
