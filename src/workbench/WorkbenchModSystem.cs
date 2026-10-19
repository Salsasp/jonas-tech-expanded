using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace JonasTechExpanded
{
    /// <summary>
    /// Registers the Jonas workbench block, its block entity and the blueprint item, and gives every hammer the
    /// assembly swing.
    /// </summary>
    public class WorkbenchModSystem : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass("ItemJonasBlueprint", typeof(ItemJonasBlueprint));
            api.RegisterBlockClass("BlockJonasWorkbench", typeof(BlockJonasWorkbench));
            api.RegisterBlockEntityClass("JonasWorkbench", typeof(BlockEntityJonasWorkbench));
            api.RegisterCollectibleBehaviorClass("workbenchHammer", typeof(CollectibleBehaviorWorkbenchHammer));
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            foreach (var collectible in api.World.Collectibles)
            {
                if (collectible.Tool != EnumTool.Hammer || collectible.HasBehavior<CollectibleBehaviorWorkbenchHammer>()) continue;

                collectible.CollectibleBehaviors = collectible.CollectibleBehaviors.Append(new CollectibleBehaviorWorkbenchHammer(collectible));
            }
        }
    }
}
