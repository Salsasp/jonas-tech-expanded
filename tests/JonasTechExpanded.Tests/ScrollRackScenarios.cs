using System.Linq;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// Blueprints in vanilla scroll racks. The rolled-up look (<see cref="ItemJonasBlueprint.GenMesh"/>) is client
    /// only; this covers what the server decides: every variant is accepted, and a right-click puts it in and takes it out.
    /// </summary>
    public class ScrollRackScenarios : AtlasScenarioBase
    {
        private const string Blueprint = "jonastechexpanded:jonasblueprint-springboots";

        /// <summary>A middle-row slot: usable even without a neighbouring rack.</summary>
        private const int MidSlot = 3;

        [AtlasScenario]
        public async Task Every_blueprint_variant_is_scrollrackable_with_a_rolled_shape()
        {
            await Task.CompletedTask;
            var blueprints = World.Api.World.Items.Where(i => i.Code?.Path.StartsWith("jonasblueprint-") == true).ToList();
            Assert.NotEmpty(blueprints);

            foreach (var blueprint in blueprints)
            {
                Assert.IsType<ItemJonasBlueprint>(blueprint);
                Assert.True(blueprint.Attributes["scrollrackable"].AsBool(), $"{blueprint.Code} is not scrollrackable");

                string rolled = blueprint.Attributes[ItemJonasBlueprint.RolledShapeAttribute].AsString();
                Assert.False(string.IsNullOrEmpty(rolled), $"{blueprint.Code} has no rolledShape");
                var location = AssetLocation.Create(rolled, blueprint.Code.Domain).WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                Assert.True(World.Api.Assets.Exists(location), $"{blueprint.Code}: {location} not found");
            }
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Blueprint_goes_into_a_scroll_rack_and_back_out()
        {
            var player = await World.JoinPlayer("Archivist");
            TestKit.SetGameMode(player, EnumGameMode.Survival);
            TestKit.ClearHotbar(player);
            player.Player.InventoryManager.ActiveHotbarSlotNumber = 0;

            BlockPos pos = World.Spawn.Offset(2, 1, -2);
            World.SetBlock("game:scrollrack", pos);
            await World.Ticks(2);

            var rack = World.BlockEntityAt<BlockEntityScrollRack>(pos);
            Assert.NotNull(rack);
            var block = World.Api.World.BlockAccessor.GetBlock(pos);
            var selection = new BlockSelection { Position = pos, SelectionBoxIndex = MidSlot, Face = BlockFacing.NORTH, HitPosition = new Vec3d(0.5, 0.5, 0) };

            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, Blueprint)));
            Assert.True(block.OnBlockInteractStart(World.Api.World, player.Player, selection), "rack refused the blueprint");
            Assert.Equal(Blueprint, rack.Inventory[MidSlot].Itemstack?.Collectible.Code.ToString());
            Assert.True(TestKit.HotbarSlot(player, 0).Empty);

            Assert.True(block.OnBlockInteractStart(World.Api.World, player.Player, selection), "could not take the blueprint back");
            Assert.True(rack.Inventory[MidSlot].Empty);
            Assert.Equal(1, TestKit.CountCarried(player, Blueprint));
        }
    }
}
