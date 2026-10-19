using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace JonasTechExpanded
{
    /// <summary>
    /// Attached to every hammer at asset finalize: while the player holds right-click on a Jonas workbench that is
    /// ready to assemble, the held-use animation is vanilla's anvil swing instead of the static interact pose.
    /// Vanilla's player animation code picks this up on the server (synced to others) and on the owning client.
    /// </summary>
    public class CollectibleBehaviorWorkbenchHammer : CollectibleBehavior
    {
        /// <summary>The player animation the hammer plays on an anvil; it has first-person variants.</summary>
        public const string AssembleAnimation = "smithingwide";

        public CollectibleBehaviorWorkbenchHammer(CollectibleObject collObj) : base(collObj)
        {
        }

        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity forEntity, ref EnumHandling bhHandling)
        {
            var pos = (forEntity as EntityPlayer)?.Player?.CurrentBlockSelection?.Position;
            if (pos == null || BlockEntityJonasWorkbench.At(forEntity.World, pos)?.IsComplete != true) return null;

            bhHandling = EnumHandling.PreventDefault;
            return AssembleAnimation;
        }
    }
}
