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

        /// <summary>Each blueprint's output and stages, in order: one ingredient each.</summary>
        private static readonly Dictionary<string, (string output, (string code, int quantity)[] stages)> Builds = new()
        {
            [Blueprint] = ("jonastechexpanded:springboots", new[]
            {
                ("jonastechexpanded:shockabsorptionapparatus", 1),
                ("jonastechexpanded:shockabsorptionapparatus", 1),
                ("jonastechexpanded:springbootsbody", 1),
                ("jonastechexpanded:springbootsbody", 1),
                ("game:jonasframes-gears02", 1),
                ("game:jonasparts-cylinder01", 1),
                ("game:metalnailsandstrips-steel", 8),
                ("game:flaxtwine", 8)
            }),
            ["jonastechexpanded:jonasblueprint-exoskeleton"] = ("jonastechexpanded:exoskeletonchest", new[]
            {
                ("game:armor-body-jerkin-leather", 1),
                ("jonastechexpanded:exoskeletontorsoframe", 1),
                ("jonastechexpanded:exoskeletonarmframe", 1),
                ("jonastechexpanded:exoskeletonarmframe", 1),
                ("game:metalnailsandstrips-steel", 8)
            }),
            ["jonastechexpanded:jonasblueprint-tank02"] = ("game:jonasparts-tank02", new[]
            {
                ("game:ingot-steel", 1),
                ("game:ingot-cupronickel", 1),
                ("game:glass-plain", 1),
                ("game:clearquartz", 1),
                ("game:metalnailsandstrips-steel", 2)
            }),
            ["jonastechexpanded:jonasblueprint-gears02"] = ("game:jonasframes-gears02", new[]
            {
                ("game:ingot-steel", 1),
                ("game:ingot-cupronickel", 1),
                ("game:gear-rusty", 3),
                ("game:metalnailsandstrips-steel", 2)
            })
        };

        [AtlasScenario]
        public async Task Blueprint_recipes_resolve_and_fit_the_bench()
        {
            await Task.CompletedTask;
            var bench = TestKit.Block(World, "jonastechexpanded:jonasworkbench-north");
            int sockets = WorkbenchSocketMarkers.Get(World.Api, bench).Sockets.Length;

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

                // Shared in the item's `attributes`; the engine has to merge it into each variant's attributesByType.
                Assert.True(blueprint.Attributes["onJonasWorkbenchTransform"].Exists, $"{blueprint.Code}: no onJonasWorkbenchTransform");
            }
        }

        [AtlasScenario]
        public async Task Socket_markers_put_every_item_on_the_work_surface()
        {
            await Task.CompletedTask;
            var bench = TestKit.Block(World, "jonastechexpanded:jonasworkbench-north");
            var placements = WorkbenchSocketMarkers.Get(World.Api, bench);

            Assert.Equal(8, placements.Sockets.Length);
            Assert.Equal(0.8f, placements.Blueprint.Scale);

            // The work surface is the top at y = 14, x 2..30, z 1..16 (backboard at z 0..1).
            foreach (var (socket, i) in placements.Sockets.Select((s, i) => (s, i + 1)))
            {
                Assert.True(socket.Y > 14 && socket.Y < 14.1, $"Socket{i} y is {socket.Y}, not on the work surface");
                Assert.True(socket.X >= 2 && socket.X <= 30 && socket.Z >= 1 && socket.Z <= 16, $"Socket{i} at ({socket.X}, {socket.Z}) is off the work surface");
                Assert.Equal(0.4f, socket.Scale);
            }

        }

        [AtlasTheory(RollbackWorld = true)]
        [InlineData("jonastechexpanded:jonasblueprint-springboots", "BuildSpringboots")]
        [InlineData("jonastechexpanded:jonasblueprint-exoskeleton", "BuildExo")]
        [InlineData("jonastechexpanded:jonasblueprint-tank02", "BuildPart")]
        [InlineData("jonastechexpanded:jonasblueprint-gears02", "BuildFrame")]
        public async Task Building_takes_every_stage_and_returns_the_output(string blueprint, string playerName)
        {
            var (output, stages) = Builds[blueprint];
            var (player, be, _) = await setUp(playerName, blueprint);

            Assert.True(be.OnInteractStart(player.Player));
            Assert.True(be.HasBlueprint && be.HasRecipe);
            Assert.True(TestKit.HotbarSlot(player, 0).Empty, "Blueprint should have left the hand");

            stockHotbar(player, stages);
            for (int stage = 0; stage < stages.Length; stage++)
            {
                Assert.False(be.IsComplete, $"Complete before stage {stage + 1}");
                be.OnInteractStart(player.Player);

                var (code, quantity) = stages[stage];
                // Later stages may take the same item again, so count what the remaining stages still need.
                int stillNeeded = stages.Skip(stage + 1).Where(s => s.code == code).Sum(s => s.quantity);
                Assert.Equal(stillNeeded, TestKit.CountCarried(player, code));

                var socket = be.Inventory[1 + stage];
                Assert.Equal(new AssetLocation(code), socket.Itemstack?.Collectible.Code);
                Assert.Equal(quantity, socket.StackSize);
            }
            Assert.True(be.IsComplete);
            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, "game:hammer-iron")));

            // Releasing early does nothing.
            Assert.True(be.OnInteractStart(player.Player));
            be.OnInteractStop(1f, player.Player);
            Assert.True(be.IsComplete);
            Assert.Equal(0, TestKit.CountCarried(player, output));

            Assert.True(be.OnInteractStart(player.Player));
            Assert.True(be.OnInteractStep(1f, player.Player), "Still assembling after 1 second");
            Assert.False(be.OnInteractStep(4f, player.Player), "Assembly should be done at 4 seconds");
            be.OnInteractStop(4f, player.Player);

            Assert.Equal(1, TestKit.CountCarried(player, output));
            Assert.False(be.IsComplete);
            Assert.True(be.HasBlueprint, "Blueprint stays on the bench for the next build");
            Assert.All(Enumerable.Range(1, be.Inventory.Count - 1), i => Assert.True(be.Inventory[i].Empty, $"Socket {i} not cleared"));
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Assembly_needs_a_hammer_in_hand_throughout()
        {
            var (player, be, _) = await completeBench("NoHammer");

            // Empty hand: the click is taken but nothing starts.
            Assert.True(be.OnInteractStart(player.Player));
            Assert.False(be.OnInteractStep(1f, player.Player));
            be.OnInteractStop(4f, player.Player);
            Assert.True(be.IsComplete);

            // Not a hammer either.
            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, "game:chisel-iron")));
            Assert.True(be.OnInteractStart(player.Player));
            Assert.False(be.OnInteractStep(1f, player.Player));
            be.OnInteractStop(4f, player.Player);
            Assert.True(be.IsComplete);

            // Starting with a hammer, then switching away, stops it short.
            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, "game:hammer-iron")));
            Assert.True(be.OnInteractStart(player.Player));
            Assert.True(be.OnInteractStep(1f, player.Player));
            player.Player.InventoryManager.ActiveHotbarSlotNumber = 9;
            Assert.False(be.OnInteractStep(2f, player.Player), "Assembly kept going without the hammer");
            be.OnInteractStop(2f, player.Player);

            Assert.True(be.IsComplete);
            Assert.Equal(0, TestKit.CountCarried(player, "jonastechexpanded:springboots"));
        }

        [AtlasScenario]
        public Task Every_hammer_gets_the_assembly_swing()
        {
            var hammers = World.Api.World.Collectibles.Where(c => c.Tool == EnumTool.Hammer).ToList();

            Assert.NotEmpty(hammers);
            Assert.All(hammers, h => Assert.True(h.HasBehavior<CollectibleBehaviorWorkbenchHammer>(), $"{h.Code} has no workbench swing"));
            Assert.False(TestKit.Item(World, "game:chisel-iron").HasBehavior<CollectibleBehaviorWorkbenchHammer>());
            return Task.CompletedTask;
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Shift_right_click_takes_back_stages_newest_first_then_the_blueprint()
        {
            var (player, be, _) = await completeBench("TakeLast");
            var (_, stages) = Builds[Blueprint];
            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, "game:hammer-iron")));

            player.Entity.Controls.ShiftKey = true;
            try
            {
                // Works on a complete bench too, instead of assembling.
                for (int stage = stages.Length - 1; stage >= 0; stage--)
                {
                    Assert.True(be.OnInteractStart(player.Player));
                    Assert.False(be.IsComplete);
                    Assert.True(be.Inventory[1 + stage].Empty, $"Socket {1 + stage} still filled");
                    Assert.All(Enumerable.Range(1, stage), i => Assert.False(be.Inventory[i].Empty, $"Socket {i} emptied too early"));
                }

                Assert.Equal(stages.Sum(s => s.quantity), stages.Select(s => s.code).Distinct().Sum(code => TestKit.CountCarried(player, code)));
                Assert.True(be.HasBlueprint, "Blueprint should go last");

                Assert.True(be.OnInteractStart(player.Player));
                Assert.False(be.HasBlueprint);
                Assert.Equal(1, TestKit.CountCarried(player, Blueprint));
            }
            finally
            {
                player.Entity.Controls.ShiftKey = false;
            }

            // Taken-back parts install again from scratch.
            TestKit.ClearHotbar(player);
            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, Blueprint)));
            be.OnInteractStart(player.Player);
            stockHotbar(player, stages);
            be.OnInteractStart(player.Player);
            Assert.False(be.Inventory[1].Empty, "First stage didn't install after taking everything back");
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Stage_without_its_ingredients_is_not_installed()
        {
            var (player, be, _) = await setUp("Missing");
            be.OnInteractStart(player.Player);

            TestKit.PutInHotbar(player, 1, new ItemStack(TestKit.Item(World, "jonastechexpanded:springbootsbody"), 1));
            be.OnInteractStart(player.Player);

            Assert.True(be.Inventory[1].Empty, "Stage installed with the wrong item");
            Assert.Equal(1, TestKit.CountCarried(player, "jonastechexpanded:springbootsbody"));
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
            stockHotbar(player, Builds[Blueprint].stages);
            for (int i = 0; i < 4; i++) be.OnInteractStart(player.Player);

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

        [AtlasTheory(RollbackWorld = true)]
        [InlineData("game:solderbar-tin", "CraftBenchLead")]
        [InlineData("game:solderbar-silver", "CraftBenchSilver")]
        public async Task Grid_recipe_crafts_the_bench_and_uses_up_the_tools(string solder, string playerName)
        {
            var player = await World.JoinPlayer(playerName);
            // BI_, WHC, PPP; mixed plank woods are fine.
            var codes = new[]
            {
                solder, "game:solderingiron", null,
                "game:wrench-iron", "game:hammer-iron", "game:chisel-iron",
                "game:plank-oak", "game:plank-pine", "game:plank-oak"
            };
            var grid = codes.Select(code =>
            {
                var slot = (ItemSlot)new DummySlot();
                if (code != null) slot.Itemstack = new ItemStack(TestKit.Item(World, code), code == solder || code.Contains("plank") ? 2 : 1);
                return slot;
            }).ToArray();

            var recipe = Assert.Single(World.Api.World.GridRecipes.Where(r => r.Matches(player.Player, World.Api.World, grid, 3)));
            Assert.Equal(new AssetLocation("jonastechexpanded:jonasworkbench-north"), recipe.Output.ResolvedItemStack.Collectible.Code);

            Assert.True(recipe.ConsumeInput(player.Player, grid, 3));
            for (int i = 0; i < grid.Length; i++) Assert.True(grid[i].Empty, $"{codes[i]} should be used up");
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Grid_recipe_needs_two_solder_bars()
        {
            var player = await World.JoinPlayer("CraftBenchShort");
            var codes = new[]
            {
                "game:solderbar-tin", "game:solderingiron", null,
                "game:wrench-iron", "game:hammer-iron", "game:chisel-iron",
                "game:plank-oak", "game:plank-oak", "game:plank-oak"
            };
            var grid = codes.Select(code =>
            {
                var slot = (ItemSlot)new DummySlot();
                if (code != null) slot.Itemstack = new ItemStack(TestKit.Item(World, code), code.Contains("plank") ? 2 : 1);
                return slot;
            }).ToArray();

            Assert.DoesNotContain(World.Api.World.GridRecipes, r => r.Matches(player.Player, World.Api.World, grid, 3));
        }

        /// <summary>Survival player holding the blueprint, standing next to a fresh bench.</summary>
        private async Task<(ITestPlayer player, BlockEntityJonasWorkbench be, BlockPos pos)> setUp(string playerName, string blueprint = Blueprint)
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

            TestKit.PutInHotbar(player, 0, new ItemStack(TestKit.Item(World, blueprint)));
            return (player, be, pos);
        }

        /// <summary>Every stage's ingredients in hotbar slots 1+, leaving the active slot 0 free.</summary>
        /// <summary>A bench with every springboots stage installed, the hand empty.</summary>
        private async Task<(ITestPlayer player, BlockEntityJonasWorkbench be, BlockPos pos)> completeBench(string playerName)
        {
            var (player, be, pos) = await setUp(playerName);
            be.OnInteractStart(player.Player);

            var (_, stages) = Builds[Blueprint];
            stockHotbar(player, stages);
            for (int stage = 0; stage < stages.Length; stage++) be.OnInteractStart(player.Player);

            Assert.True(be.IsComplete);
            Assert.True(TestKit.HotbarSlot(player, 0).Empty);
            return (player, be, pos);
        }

        private void stockHotbar(ITestPlayer player, (string code, int quantity)[] stages)
        {
            for (int i = 0; i < stages.Length; i++)
            {
                var (code, quantity) = stages[i];
                var collectible = (CollectibleObject)World.Api.World.GetItem(new AssetLocation(code)) ?? World.Api.World.GetBlock(new AssetLocation(code));
                TestKit.PutInHotbar(player, 1 + i, new ItemStack(collectible, quantity));
            }
        }
    }
}
