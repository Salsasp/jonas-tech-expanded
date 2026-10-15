using Vintagestory.API.Common;

namespace JonasTechExpanded
{
    /// <summary>
    /// Registers the Jonas workbench block, its block entity and the blueprint item.
    /// </summary>
    public class WorkbenchModSystem : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass("ItemJonasBlueprint", typeof(ItemJonasBlueprint));
            api.RegisterBlockClass("BlockJonasWorkbench", typeof(BlockJonasWorkbench));
            api.RegisterBlockEntityClass("JonasWorkbench", typeof(BlockEntityJonasWorkbench));
        }
    }
}
