using System.Linq;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// The acceleration field is two blocks tall: the main block holding the entity plus a vanilla multiblock
    /// placeholder directly above it.
    /// </summary>
    public class AccelFieldMultiblockScenarios : AtlasScenarioBase
    {
        private const string FieldCode = "jonastechexpanded:temporalaccelfield";

        [AtlasScenario(RollbackWorld = true)]
        public async Task Placeholder_sits_directly_above()
        {
            var pos = await place();

            Assert.IsType<BlockMultiblock>(World.BlockAt(pos.UpCopy()));
            Assert.NotNull(World.BlockEntityAt<BlockEntityTemporalAccelField>(pos));
            foreach (var face in BlockFacing.HORIZONTALS.Append(BlockFacing.DOWN))
            {
                Assert.IsNotType<BlockMultiblock>(World.BlockAt(pos.AddCopy(face)));
            }
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Placeholder_hitbox_is_the_machine_box_in_the_top_block()
        {
            var pos = await place();
            var top = pos.UpCopy();

            var boxes = World.BlockAt(top).GetSelectionBoxes(World.Api.World.BlockAccessor, top);
            var expected = World.BlockAt(pos).GetSelectionBoxes(World.Api.World.BlockAccessor, pos);
            Assert.Equal(expected.Length, boxes.Length);
            Assert.Equal(expected[0].Y2, boxes[0].Y2);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Breaking_the_placeholder_removes_the_field_and_drops_it_once()
        {
            var pos = await place();
            var player = await World.JoinPlayer("TopBreaker");
            TestKit.SetGameMode(player, EnumGameMode.Survival);
            var top = pos.UpCopy();

            World.Api.World.BlockAccessor.BreakBlock(top, player.Player);
            await World.Ticks(5);

            Assert.Equal(0, World.BlockAt(pos).Id);
            Assert.Equal(0, World.BlockAt(top).Id);
            int fields = World.EntitiesIn(pos.Area(3)).OfType<EntityItem>()
                .Where(e => e.Itemstack.Collectible.Code.Path == "temporalaccelfield")
                .Sum(e => e.Itemstack.StackSize);
            Assert.Equal(1, fields);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Breaking_the_field_removes_the_placeholder()
        {
            var pos = await place();

            World.Api.World.BlockAccessor.BreakBlock(pos, null);
            await World.Ticks(2);

            Assert.Equal(0, World.BlockAt(pos.UpCopy()).Id);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Field_cannot_be_placed_under_a_block()
        {
            var player = await World.JoinPlayer("LowCeiling");
            var block = TestKit.Block(World, FieldCode);
            BlockPos pos = World.Spawn.Offset(4, 1, 4);
            World.SetBlock("game:soil-medium-none", pos.UpCopy());

            string failure = null;
            bool allowed = block.CanPlaceBlock(World.Api.World, player.Player, new BlockSelection { Position = pos, Face = BlockFacing.UP }, ref failure);

            Assert.False(allowed);
            Assert.Equal("notenoughspace", failure);
        }

        private async Task<BlockPos> place()
        {
            BlockPos pos = World.Spawn.Offset(4, 1, 4);
            World.SetBlock(FieldCode, pos);
            await World.Ticks(2);
            return pos;
        }
    }
}
