using System.Linq;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// The mod loads cleanly: no errors at boot, every item, block and class registered, every patch applied.
    /// These catch typos in asset JSON and missing Register* calls, which otherwise only show up in-game.
    /// </summary>
    public class BootScenarios : AtlasScenarioBase
    {
        [AtlasScenario]
        public Task Mod_is_loaded()
        {
            Assert.True(World.Api.ModLoader.IsModEnabled(TestKit.Domain));
            return Task.CompletedTask;
        }

        [AtlasScenario]
        public Task Boot_logs_no_errors()
        {
            var errors = World.BootDiagnostics
                .Where(e => e.Level is EnumLogType.Error or EnumLogType.Fatal)
                .Select(e => e.Level + ": " + e.Message)
                .ToList();

            Assert.True(errors.Count == 0, "Errors logged during boot:\n" + string.Join("\n", errors));
            return Task.CompletedTask;
        }

        [AtlasScenario]
        public Task Boot_logs_no_warnings_about_this_mod()
        {
            var warnings = World.BootDiagnostics
                .Where(e => (e.Message + " " + e.AssetPath + " " + e.Source).Contains(TestKit.Domain, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Level + ": " + e.Message)
                .ToList();

            Assert.True(warnings.Count == 0, "Warnings mentioning " + TestKit.Domain + " during boot:\n" + string.Join("\n", warnings));
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [InlineData("jonastechexpanded:exoskeletonchest", typeof(ItemExoskeletonChest))]
        [InlineData("jonastechexpanded:springboots", typeof(ItemSpringBoots))]
        [InlineData("jonastechexpanded:shockabsorptionapparatus", typeof(Item))]
        [InlineData("jonastechexpanded:springbootsbody", typeof(Item))]
        [InlineData("jonastechexpanded:jonasblueprint-springboots", typeof(Item))]
        public Task Item_is_registered_with_its_class(string code, Type expected)
        {
            Assert.IsType(expected, TestKit.Item(World, code));
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [InlineData("north")]
        [InlineData("east")]
        [InlineData("south")]
        [InlineData("west")]
        public Task Workbench_block_is_registered_with_its_classes(string side)
        {
            var block = TestKit.Block(World, "jonastechexpanded:jonasworkbench-" + side);

            Assert.IsType<BlockJonasWorkbench>(block);
            Assert.Equal("JonasWorkbench", block.EntityClass);
            Assert.Equal(typeof(BlockEntityJonasWorkbench), World.Api.ClassRegistry.GetBlockEntity(block.EntityClass));
            return Task.CompletedTask;
        }

        [AtlasScenario]
        public Task Every_item_and_block_of_this_mod_has_a_display_name()
        {
            var unnamed = World.Api.World.Collectibles
                .Where(c => c.Code?.Domain == TestKit.Domain)
                .Select(c => (code: c.Code.ToString(), name: new ItemStack(c).GetName()))
                // A missing translation comes back as its own lang key, e.g. "jonastechexpanded:item-foo".
                .Where(c => c.name.StartsWith(TestKit.Domain + ":"))
                .Select(c => c.code + " -> " + c.name)
                .ToList();

            Assert.True(unnamed.Count == 0, "Missing lang entries:\n" + string.Join("\n", unnamed));
            return Task.CompletedTask;
        }

        [AtlasScenario]
        public Task Every_collectible_has_the_exo_mining_behavior()
        {
            var missing = World.Api.World.Collectibles
                .Where(c => c.Code != null && !c.HasBehavior<CollectibleBehaviorExoMiningSpeed>())
                .Select(c => c.Code.ToString())
                .Take(10)
                .ToList();

            Assert.True(missing.Count == 0, "Collectibles without CollectibleBehaviorExoMiningSpeed: " + string.Join(", ", missing));
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [InlineData("exoskeletonFuelHours")]
        [InlineData("springbootsFuelHours")]
        public Task Temporal_gear_is_patched_as_fuel(string attribute)
        {
            var gear = TestKit.Item(World, "game:gear-temporal");
            Assert.Equal(24f, gear.Attributes?[attribute].AsFloat(0));
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [InlineData("exoactive")]
        [InlineData("springbootsactive")]
        public Task Seraph_shape_is_patched_with_the_empty_wearable_animation(string code)
        {
            var shape = Shape.TryGet(World.Api, "game:shapes/entity/humanoid/seraph-faceless.json");
            Assert.NotNull(shape);
            Assert.Contains(shape.Animations, anim => anim.Code == code);
            return Task.CompletedTask;
        }

        [AtlasScenario]
        public async Task Player_gets_the_server_behaviors_with_springboots_charge_before_health()
        {
            var player = await World.JoinPlayer("BootBehaviors");
            var behaviors = player.Entity.SidedProperties.Behaviors;

            int charge = behaviors.FindIndex(b => b is EntityBehaviorSpringBootsCharge);
            int health = behaviors.FindIndex(b => b is EntityBehaviorHealth);

            Assert.True(charge >= 0, "springbootsCharge missing from the player's server behaviors");
            Assert.True(health >= 0, "health missing from the player's server behaviors");
            // Fall damage is only absorbed if the boots see the hit before health subtracts it.
            Assert.True(charge < health, $"springbootsCharge (index {charge}) must run before health (index {health})");
        }

        [AtlasTheory]
        [InlineData("jonastechexpanded:springboots")]
        [InlineData("jonastechexpanded:springbootsbody")]
        [InlineData("jonastechexpanded:shockabsorptionapparatus")]
        public Task Grid_recipe_exists_for(string output)
        {
            var loc = new AssetLocation(output);
            Assert.Contains(World.Api.World.GridRecipes, r => r.Output?.ResolvedItemStack?.Collectible.Code.Equals(loc) == true);
            return Task.CompletedTask;
        }
    }
}
