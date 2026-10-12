using System.Linq;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// Jonas part crafting: every vanilla Jonas part and sub-assembly copies onto parchment as a blueprint (the part
    /// is kept), and that blueprint builds the part on the workbench from the shared base materials.
    /// </summary>
    public class PartCraftingScenarios : AtlasScenarioBase
    {
        /// <summary>Every vanilla Jonas part and sub-assembly; each has a blueprint named after its type state.</summary>
        public static readonly TheoryData<string> Parts = new()
        {
            "game:jonasparts-connector01", "game:jonasparts-cylinder01", "game:jonasparts-cylinder02",
            "game:jonasparts-tank01", "game:jonasparts-tank02", "game:jonasparts-valve01",
            "game:jonasframes-gearbox01", "game:jonasframes-gearbox02", "game:jonasframes-oscillator01",
            "game:jonasframes-spring01", "game:jonasframes-joint01", "game:jonasframes-gears01", "game:jonasframes-gears02"
        };

        private static string blueprintFor(string part) => TestKit.Domain + ":jonasblueprint-" + part.Split('-').Last();

        [AtlasTheory]
        [MemberData(nameof(Parts))]
        public Task Blueprint_builds_its_part_from_the_base_materials(string part)
        {
            var blueprint = TestKit.Item(World, blueprintFor(part));
            var recipe = blueprint.Attributes?[BlockEntityJonasWorkbench.RecipeAttribute];
            Assert.True(recipe?.Exists == true, $"{blueprint.Code} has no workbench recipe");

            var output = recipe["output"].AsObject<JsonItemStack>(null, blueprint.Code.Domain);
            Assert.True(output.Resolve(World.Api.World, "test"));
            Assert.Equal(new AssetLocation(part), output.ResolvedItemstack.Collectible.Code);

            var ingredients = recipe["stages"].AsObject<ConstructionStage[]>(null, blueprint.Code.Domain)
                .SelectMany(s => s.RequireStacks)
                .ToList();
            foreach (var ingredient in ingredients) Assert.True(ingredient.Resolve(World.Api.World, "test"));

            int quantityOf(string code) => ingredients
                .Where(i => i.ResolvedItemStack.Collectible.Code.Equals(new AssetLocation(code)))
                .Sum(i => i.Quantity);
            Assert.Equal(1, quantityOf("game:ingot-steel"));
            Assert.Equal(1, quantityOf("game:ingot-cupronickel"));
            Assert.Equal(2, quantityOf("game:metalnailsandstrips-steel"));
            return Task.CompletedTask;
        }

        [AtlasTheory(RollbackWorld = true)]
        [InlineData("game:jonasparts-tank02", "CopyPart")]
        [InlineData("game:jonasframes-gears02", "CopyFrame")]
        public async Task Part_and_parchment_make_its_blueprint_and_keep_the_part(string part, string playerName)
        {
            var player = await World.JoinPlayer(playerName);
            var grid = Enumerable.Range(0, 9).Select(_ => (ItemSlot)new DummySlot()).ToArray();
            grid[0].Itemstack = new ItemStack(TestKit.Item(World, part));
            grid[4].Itemstack = new ItemStack(TestKit.Item(World, "game:paper-parchment"));

            var matching = World.Api.World.GridRecipes.Where(r => r.Matches(player.Player, World.Api.World, grid, 3)).ToList();
            var recipe = Assert.Single(matching);
            Assert.Equal(new AssetLocation(blueprintFor(part)), recipe.Output.ResolvedItemStack.Collectible.Code);

            Assert.True(recipe.ConsumeInput(player.Player, grid, 3));
            Assert.Equal(1, grid[0].StackSize);
            Assert.Equal(new AssetLocation(part), grid[0].Itemstack.Collectible.Code);
            Assert.True(grid[4].Empty, "Parchment should be used up");
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Pump_head_has_no_blueprint_and_cannot_be_copied()
        {
            // A story-location item that only sits among the jonasparts by file layout.
            Assert.Null(World.Api.World.GetItem(new AssetLocation(TestKit.Domain, "jonasblueprint-pumphead")));

            var player = await World.JoinPlayer("CopyPumphead");
            var grid = Enumerable.Range(0, 9).Select(_ => (ItemSlot)new DummySlot()).ToArray();
            grid[0].Itemstack = new ItemStack(TestKit.Item(World, "game:jonasparts-pumphead"));
            grid[4].Itemstack = new ItemStack(TestKit.Item(World, "game:paper-parchment"));
            Assert.DoesNotContain(World.Api.World.GridRecipes, r => r.Matches(player.Player, World.Api.World, grid, 3));
        }

        [AtlasTheory]
        [MemberData(nameof(Parts))]
        public Task Grid_recipe_exists_for_every_part_blueprint(string part)
        {
            var loc = new AssetLocation(blueprintFor(part));
            Assert.Contains(World.Api.World.GridRecipes, r => r.Output?.ResolvedItemStack?.Collectible.Code.Equals(loc) == true);
            return Task.CompletedTask;
        }
    }
}
