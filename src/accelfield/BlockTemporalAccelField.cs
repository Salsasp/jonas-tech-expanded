using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace JonasTechExpanded
{
    /// <summary>
    /// Right-click opens the field's dialog; ctrl + right-click shows the field outline for a few seconds instead.
    /// </summary>
    public class BlockTemporalAccelField : Block
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityTemporalAccelField be)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            if (byPlayer.Entity.Controls.CtrlKey)
            {
                if (world.Side == EnumAppSide.Client) world.Api.ModLoader.GetModSystem<AccelFieldModSystem>()?.ShowPreview(be);
                return true;
            }

            return be.OnPlayerRightClick(byPlayer, blockSel);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction { ActionLangCode = "jonastechexpanded:accelfield-open", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "jonastechexpanded:accelfield-preview", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" }
            }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }
    }
}
