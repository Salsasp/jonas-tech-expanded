using Vintagestory.API.Common;

namespace JonasTechExpanded
{
    /// <summary>
    /// Registers the Jonas workbench block and its block entity.
    /// </summary>
    public class WorkbenchModSystem : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            api.RegisterBlockClass("BlockJonasWorkbench", typeof(BlockJonasWorkbench));
            api.RegisterBlockEntityClass("JonasWorkbench", typeof(BlockEntityJonasWorkbench));
        }
    }
}
