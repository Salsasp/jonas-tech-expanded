using System.Linq;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// The Jonas workbench end to end on the server: blueprint placement, staged installs from the hotbar,
    /// hold-to-assemble, and getting parts back. Right-clicks are driven through the block entity's interaction
    /// methods, the same calls <see cref="BlockJonasWorkbench"/> forwards a player's clicks to.
    /// </summary>
    public class WorkbenchScenarios : AtlasScenarioBase
    {
        private const string Blueprint = "jonastechexpanded:jonasblueprint-springboots";
        private const string Output = "jonastechexpanded:springboots";

        /// <summary>The springboots blueprint's stages, in order: one ingredient each.</summary>
        private static readonly (string code, int quantity)[] SpringbootsStages =
        {
            ("jonastechexpanded:shockabsorptionapparatus", 2),
            ("jonastechexpanded:springbootsbody", 2),
            ("game:jonasframes-gears02", 1),
            ("game:jonasparts-cylinder01", 1),
            ("game:metalnailsandstrips-steel", 8),
            ("game:flaxtwine", 8)
        };

        [AtlasScenario]
        public async Task Blueprint_recipes_resolve_and_fit_the_bench()
        {
            await Task.CompletedTask;
            var bench = TestKit.Block(World, "jonastechexpanded:jonasworkbench-north");
            int sockets = bench.Attributes["workbench"]["sockets"].AsArray().Length;

            var blueprints = World.Api.World.Collectibles
                .Where(c => c.Attributes?[BlockEntityJonasWorkbench.RecipeAttribute].Exists == true)
                .ToList();
            Assert.NotEmpty(blueprints);

            foreach (var blueprint in blueprints)
            {
                var recipe = blueprint.Attributes[BlockEntityJonasWorkbench.RecipeAttribute];
                string domain = blueprint.Code.Domain;

                var output = recipe["output"].AsObject<JsonItemStack>(null, domain);
                Assert.True(output != null && output.Resolve(World.Api.World, "test"), $"{blueprint.Code}: output does not resolve");

                var stages = recipe["stages"].AsObject<ConstructionStage[]>(null, domain);
                Assert.True(stages != null && stages.Length > 0, $"{blueprint.Code}: no stages");

                int ingredients = 0;
                foreach (var ingredient in stages.SelectMany(s => s.RequireStacks ?? Array.Empty<ConstructionIngredient>()))
                {
                    ingredients++;
                    Assert.True(ingredient.Resolve(World.Api.World, "test"), $"{blueprint.Code}: ingredient {ingredient.Code} does not resolve");
                }

                Assert.True(ingredients <= sockets, $"{blueprint.Code}: {ingredients} ingredients but the bench has {sockets} sockets");
            }
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Building_springboots_takes_every_stage_and_returns_the_boots()
        {
            var (player, be, _) = await setUp("Builder");

            Assert.True(be.OnInteractStart(player.Player));
            Assert.True(be.HasBlueprint && be.HasRecipe);
            Assert.True(TestKit.HotbarSlot(player, 0).Empty, "Blueprint should have left the hand");

            stockHotbar(player);
            for (int stage = 0; stage < SpringbootsStages.Length; stage++)
            {
                Assert.False(be.IsComplete, $"Complete before stage {stage + 1}");
                be.OnInteractStart(player.Player);

                var (code, quantity) = SpringbootsStages[stage];
                Assert.Equal(0, TestKit.CountCarried(player, code));

                var socket = be.Inventory[1 + stage];
                Assert.Equal(new AssetLocation(code), socket.Itemstack?.Collectible.Code);
                Assert.Equal(quantity, socket.StackSize);
            }
            Assert.True(be.IsComplete);

            // Releasing early does nothing.
            Assert.True(be.OnInteractStart(player.Player));
            be.OnInteractStop(1f, player.Player);
            Assert.True(be.IsComplete);
            Assert.Equal(0, TestKit.CountCarried(player, Output));

            Assert.True(be.OnInteractStart(player.Player));
            Assert.True(be.OnInteractStep(1f, player.Player), "Still assembling after 1 second");
            Assert.False(be.OnInteractStep(4f, player.Player), "Assembly should be done at 4 seconds");
            be.OnInteractStop(4f, player.Player);

            Assert.Equal(1, TestKit.CountCarried(player, Output));
            Assert.False(be.IsComplete);
            Assert.True(be.HasBlueprint, "Blueprint stays on the bench for the next build");
            Assert.All(Enumerable.Range(1, be.Inventory.Count - 1), i => Assert.True(be.Inventory[i].Empty, $"Socket {i} not cleared"));
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Stage_without_its_ingredients_is_not_installed()
        {
            var (player, be, _) = await setUp("Missing");
            be.OnInteractStart(player.Player);

            TestKit.PutInHotbar(player, 1, new ItemStack(TestKit.Item(World, "jonastechexpanded:shockabsorptionapparatus"), 1));
            be.OnInteractStart(player.Player);

            Assert.True(be.Inventory[1].Empty, "Stage installed with 1 of 2 apparatus");
            Assert.Equal(1, TestKit.CountCarried(player, "jonastechexpanded:shockabsorptionapparatus"));
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Blueprint_can_be_taken_back_before_any_stage()
        {
            var (player, be, _) = await setUp("TakeBack");
            be.OnInteractStart(player.Player);
            Assert.True(be.HasBlueprint);

            player.Entity.Controls.ShiftKey = true;
            try
            {
                be.OnInteractStart(player.Player);
            }
            finally
            {
                player.Entity.Controls.ShiftKey = false;
            }

            Assert.False(be.HasBlueprint);
            Assert.Equal(1, TestKit.CountCarried(player, Blueprint));
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Non_blueprint_on_an_empty_bench_is_not_handled()
        {
            var (player, be, _) = await setUp("NotBlueprint");
            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, "game:flaxtwine"), 4));

            Assert.False(be.OnInteractStart(player.Player));
            Assert.False(be.HasBlueprint);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Breaking_the_bench_mid_build_drops_blueprint_and_installed_parts()
        {
            var (player, be, pos) = await setUp("Breaker");
            be.OnInteractStart(player.Player);
            stockHotbar(player);
            be.OnInteractStart(player.Player);
            be.OnInteractStart(player.Player);

            TestKit.ClearHotbar(player);
            World.Api.World.BlockAccessor.BreakBlock(pos, player.Player);
            await World.Ticks(5);

            var dropped = World.EntitiesIn(pos.Area(3))
                .OfType<EntityItem>()
                .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));

            Assert.Equal(1, dropped.GetValueOrDefault(Blueprint));
            Assert.Equal(2, dropped.GetValueOrDefault("jonastechexpanded:shockabsorptionapparatus"));
            Assert.Equal(2, dropped.GetValueOrDefault("jonastechexpanded:springbootsbody"));
        }

        /// <summary>Survival player holding the blueprint, standing next to a fresh bench.</summary>
        private async Task<(ITestPlayer player, BlockEntityJonasWorkbench be, BlockPos pos)> setUp(string playerName)
        {
            var player = await World.JoinPlayer(playerName);
            TestKit.SetGameMode(player, EnumGameMode.Survival);
            TestKit.ClearHotbar(player);
            player.Player.InventoryManager.ActiveHotbarSlotNumber = 0;

            BlockPos pos = World.Spawn.Offset(2, 1, 2);
            World.SetBlock("jonastechexpanded:jonasworkbench-north", pos);
            await World.Ticks(2);

            var be = World.BlockEntityAt<BlockEntityJonasWorkbench>(pos);
            Assert.NotNull(be);

            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, Blueprint)));
            return (player, be, pos);
        }

        /// <summary>Every stage's ingredients in hotbar slots 1+, leaving the active slot 0 free.</summary>
        private void stockHotbar(ITestPlayer player)
        {
            for (int i = 0; i < SpringbootsStages.Length; i++)
            {
                var (code, quantity) = SpringbootsStages[i];
                var collectible = (CollectibleObject)World.Api.World.GetItem(new AssetLocation(code)) ?? World.Api.World.GetBlock(new AssetLocation(code));
                TestKit.PutInHotbar(player, 1 + i, new ItemStack(collectible, quantity));
            }
        }
    }
}
