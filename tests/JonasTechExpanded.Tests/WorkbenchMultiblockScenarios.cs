using System.Linq;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// The workbench is two blocks wide: a main block holding the entity plus a vanilla multiblock placeholder on
    /// the model's far half, which must be on the correct side for every facing.
    /// </summary>
    public class WorkbenchMultiblockScenarios : AtlasScenarioBase
    {
        private static readonly BlockFacing[] Sides = BlockFacing.HORIZONTALS;

        [AtlasTheory(RollbackWorld = true)]
        [InlineData("north")]
        [InlineData("east")]
        [InlineData("south")]
        [InlineData("west")]
        public async Task Placeholder_sits_under_the_far_half_of_the_model(string side)
        {
            var (block, pos) = await place(side);

            var placeholders = Sides.Select(f => pos.AddCopy(f)).Where(p => World.BlockAt(p) is BlockMultiblock).ToList();
            Assert.True(placeholders.Count == 1, $"{side}: expected one placeholder next to the bench, found {placeholders.Count}");
            var placeholder = placeholders[0];

            // Every corner of the model's top and every socket must render over the bench's two blocks, and the
            // far half's center over the placeholder: same rotation the renderer applies to the shape.
            float rotY = block.Shape.rotateY;
            var expectedFar = new Vec3i(placeholder.X - pos.X, 0, placeholder.Z - pos.Z);
            Assert.Equal(expectedFar, blockUnder(rotY, 24, 8));

            var footprint = new[] { new Vec3i(0, 0, 0), expectedFar };
            foreach (var (x, z) in new[] { (0.5f, 0.5f), (31.5f, 0.5f), (0.5f, 15.5f), (31.5f, 15.5f) })
            {
                Assert.Contains(blockUnder(rotY, x, z), footprint);
            }

            foreach (var socket in block.Attributes["workbench"]["sockets"].AsObject<BlockEntityJonasWorkbench.SocketPlacement[]>())
            {
                var at = BlockEntityJonasWorkbench.PlacementMatrix(rotY, socket).TransformVector(new Vec4f(0.5f, 0, 0.5f, 1));
                Assert.Contains(new Vec3i((int)Math.Floor(at.X), 0, (int)Math.Floor(at.Z)), footprint);
            }
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Clicking_the_placeholder_uses_the_bench()
        {
            var (_, pos) = await place("north");
            var player = await World.JoinPlayer("FarClick");
            TestKit.ClearHotbar(player);
            player.Player.InventoryManager.ActiveHotbarSlotNumber = 0;
            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, "jonastechexpanded:jonasblueprint-springboots")));

            var far = farHalf(pos);
            Assert.True(World.BlockAt(far).OnBlockInteractStart(World.Api.World, player.Player, new BlockSelection { Position = far }));

            Assert.True(World.BlockEntityAt<BlockEntityJonasWorkbench>(pos).HasBlueprint);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Breaking_the_placeholder_removes_the_whole_bench_and_drops_it_once()
        {
            var (_, pos) = await place("north");
            var player = await World.JoinPlayer("FarBreaker");
            TestKit.SetGameMode(player, EnumGameMode.Survival);
            var far = farHalf(pos);

            World.Api.World.BlockAccessor.BreakBlock(far, player.Player);
            await World.Ticks(5);

            Assert.Equal(0, World.BlockAt(pos).Id);
            Assert.Equal(0, World.BlockAt(far).Id);
            int benches = World.EntitiesIn(pos.Area(3)).OfType<EntityItem>()
                .Where(e => e.Itemstack.Collectible.Code.Path.StartsWith("jonasworkbench"))
                .Sum(e => e.Itemstack.StackSize);
            Assert.Equal(1, benches);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Breaking_the_bench_removes_the_placeholder()
        {
            var (_, pos) = await place("north");
            var far = farHalf(pos);

            World.Api.World.BlockAccessor.BreakBlock(pos, null);
            await World.Ticks(2);

            Assert.Equal(0, World.BlockAt(far).Id);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Bench_cannot_be_placed_when_its_second_block_is_taken()
        {
            var player = await World.JoinPlayer("Blocked");
            var block = TestKit.Block(World, "jonastechexpanded:jonasworkbench-north");
            BlockPos pos = World.Spawn.Offset(4, 1, 4);
            World.SetBlock("game:soil-medium-none", pos.AddCopy(BlockFacing.EAST));

            string failure = null;
            bool allowed = block.CanPlaceBlock(World.Api.World, player.Player, new BlockSelection { Position = pos, Face = BlockFacing.UP }, ref failure);

            Assert.False(allowed);
            Assert.Equal("notenoughspace", failure);
        }

        /// <summary>Which block (relative to the main block) a point of the north-facing model at (x, z) voxels renders over.</summary>
        private static Vec3i blockUnder(float rotY, float x, float z)
        {
            var at = BlockEntityJonasWorkbench.PlacementMatrix(rotY, new BlockEntityJonasWorkbench.SocketPlacement { X = x, Z = z, Scale = 1 })
                .TransformVector(new Vec4f(0.5f, 0, 0.5f, 1));
            return new Vec3i((int)Math.Floor(at.X), 0, (int)Math.Floor(at.Z));
        }

        private async Task<(Block block, BlockPos pos)> place(string side)
        {
            var block = TestKit.Block(World, "jonastechexpanded:jonasworkbench-" + side);
            BlockPos pos = World.Spawn.Offset(4, 1, 4);
            World.SetBlock(block.Code.ToString(), pos);
            await World.Ticks(2);
            return (block, pos);
        }

        private BlockPos farHalf(BlockPos pos)
        {
            return Sides.Select(f => pos.AddCopy(f)).Single(p => World.BlockAt(p) is BlockMultiblock);
        }
    }
}
