using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace JonasTechExpanded
{
    /// <summary>
    /// Forwards right-click interactions to <see cref="BlockEntityJonasWorkbench"/>, which holds all the logic.
    /// </summary>
    public class BlockJonasWorkbench : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (getBench(world, blockSel) is { } be) return be.OnInteractStart(byPlayer);

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (getBench(world, blockSel) is { } be) return be.OnInteractStep(secondsUsed, byPlayer);

            return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel);
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (getBench(world, blockSel) is { } be)
            {
                be.OnInteractStop(secondsUsed, byPlayer);
                return;
            }

            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            getBench(world, blockSel)?.OnInteractCancel();
            return true;
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            var help = getBench(world, selection)?.GetInteractionHelp(forPlayer);
            return (help ?? new WorldInteraction[0]).Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }

        private static BlockEntityJonasWorkbench getBench(IWorldAccessor world, BlockSelection blockSel)
        {
            return blockSel == null ? null : world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityJonasWorkbench;
        }
    }
}
