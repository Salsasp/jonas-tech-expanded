using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace JonasTechExpanded.Tests
{
    /// <summary>What powered gear does to a survival player, and that it all reverts when the power goes.</summary>
    public class WearableEffectScenarios : AtlasScenarioBase
    {
        [AtlasScenario]
        public async Task Powered_exoskeleton_extends_reach_and_unequipping_restores_it()
        {
            var player = await World.JoinPlayer("ExoReach");
            TestKit.SetGameMode(player, EnumGameMode.Survival);
            var exo = (ItemExoskeletonChest)TestKit.Item(World, "jonastechexpanded:exoskeletonchest");
            float powered = GlobalConstants.DefaultPickingRange + exo.ReachBonus;

            var worn = TestKit.Equip(player, EnumCharacterDressType.ArmorBody, TestKit.Fueled(World, exo.Code.ToString(), 10));
            await TestKit.UntilTicked(World, () => Math.Abs(player.Player.WorldData.PickingRange - powered) < 0.001f);

            worn.Itemstack = null;
            worn.MarkDirty();
            await TestKit.UntilTicked(World, () => Math.Abs(player.Player.WorldData.PickingRange - GlobalConstants.DefaultPickingRange) < 0.001f);
        }

        [AtlasScenario]
        public async Task Powered_exoskeleton_multiplies_mining_speed_on_any_material()
        {
            var player = await World.JoinPlayer("ExoMiner");
            var exo = (ItemExoskeletonChest)TestKit.Item(World, "jonastechexpanded:exoskeletonchest");
            var pickaxe = new ItemStack(TestKit.Item(World, "game:pickaxe-iron"));
            BlockPos pos = World.Spawn.Offset(2, 1, 0);

            foreach (var blockCode in new[] { "game:soil-medium-none", "game:planks-oak-ud", "game:rock-granite" })
            {
                var block = TestKit.Block(World, blockCode);
                var sel = new BlockSelection { Position = pos };

                TestKit.Equip(player, EnumCharacterDressType.ArmorBody, null);
                float bare = pickaxe.Collectible.GetMiningSpeed(pickaxe, sel, block, player.Player);

                TestKit.Equip(player, EnumCharacterDressType.ArmorBody, TestKit.Fueled(World, exo.Code.ToString(), 10));
                float boosted = pickaxe.Collectible.GetMiningSpeed(pickaxe, sel, block, player.Player);

                Assert.True(bare > 0, "No base mining speed on " + blockCode);
                Assert.Equal(exo.MiningSpeedMultiplier, boosted / bare, 3);
            }
        }

        [AtlasScenario]
        public async Task Unpowered_exoskeleton_leaves_mining_speed_alone()
        {
            var player = await World.JoinPlayer("ExoMinerOff");
            var pickaxe = new ItemStack(TestKit.Item(World, "game:pickaxe-iron"));
            var block = TestKit.Block(World, "game:soil-medium-none");
            var sel = new BlockSelection { Position = World.Spawn.Offset(2, 1, 0) };

            float bare = pickaxe.Collectible.GetMiningSpeed(pickaxe, sel, block, player.Player);
            TestKit.Equip(player, EnumCharacterDressType.ArmorBody, TestKit.Fueled(World, "jonastechexpanded:exoskeletonchest", 0));

            Assert.Equal(bare, pickaxe.Collectible.GetMiningSpeed(pickaxe, sel, block, player.Player), 3);
        }

        [AtlasScenario]
        public async Task Powered_springboots_set_move_speed_and_jump_height_and_unequipping_restores_them()
        {
            var player = await World.JoinPlayer("BootsSpeed");
            TestKit.SetGameMode(player, EnumGameMode.Survival);
            var boots = (ItemSpringBoots)TestKit.Item(World, "jonastechexpanded:springboots");

            var worn = TestKit.Equip(player, EnumCharacterDressType.ArmorLegs, TestKit.Fueled(World, boots.Code.ToString(), 10));
            await TestKit.UntilTicked(World, () => Math.Abs(player.Player.WorldData.MoveSpeedMultiplier - boots.SpeedMultiplier) < 0.001f);
            Assert.Equal(boots.JumpHeightMul, player.Entity.Stats.GetBlended("jumpHeightMul"), 3);

            worn.Itemstack = null;
            worn.MarkDirty();
            await TestKit.UntilTicked(World, () => Math.Abs(player.Player.WorldData.MoveSpeedMultiplier - 1f) < 0.001f);
            Assert.Equal(1f, player.Entity.Stats.GetBlended("jumpHeightMul"), 3);
        }

        [AtlasScenario]
        public async Task Powered_springboots_absorb_fall_damage_into_fuel()
        {
            var player = await World.JoinPlayer("BootsFall");
            TestKit.SetGameMode(player, EnumGameMode.Survival);
            var worn = TestKit.Equip(player, EnumCharacterDressType.ArmorLegs, TestKit.Fueled(World, "jonastechexpanded:springboots", 20));
            var boots = (ItemSpringBoots)worn.Itemstack.Collectible;
            await World.Ticks(5);

            float health = player.Stats.Health;
            player.Entity.ReceiveDamage(fall(), 6);
            await World.Ticks(2);

            Assert.Equal(health, player.Stats.Health, 3);
            Assert.Equal(20 - 6 * boots.FallDamageFuelHoursPerHp, boots.GetFuelHours(worn.Itemstack), 2);
        }

        [AtlasScenario]
        public async Task Unpowered_springboots_take_fall_damage_normally()
        {
            var player = await World.JoinPlayer("BootsFallOff");
            TestKit.SetGameMode(player, EnumGameMode.Survival);
            TestKit.Equip(player, EnumCharacterDressType.ArmorLegs, TestKit.Fueled(World, "jonastechexpanded:springboots", 0));
            await World.Ticks(5);

            float health = player.Stats.Health;
            player.Entity.ReceiveDamage(fall(), 6);
            await World.Ticks(2);

            Assert.True(player.Stats.Health < health, $"Health stayed at {player.Stats.Health}; fall damage should apply without fuel");
        }

        [AtlasTheory]
        [InlineData(0f, 0)]
        [InlineData(0.49f, 0)]
        [InlineData(0.5f, 1)]
        [InlineData(1.25f, 2)]
        [InlineData(2f, 3)]
        [InlineData(10f, 3)]
        public Task Charge_level_follows_the_hold_thresholds(float seconds, int level)
        {
            var boots = (ItemSpringBoots)TestKit.Item(World, "jonastechexpanded:springboots");
            Assert.Equal(level, boots.GetChargeLevel(seconds));
            return Task.CompletedTask;
        }

        [AtlasScenario]
        public Task Charge_jump_multipliers_match_each_level()
        {
            var boots = (ItemSpringBoots)TestKit.Item(World, "jonastechexpanded:springboots");

            Assert.Equal(boots.JumpHeightMul, boots.GetChargeJumpMul(0));
            for (int level = 1; level <= boots.ChargeJumpMuls.Length; level++)
            {
                Assert.Equal(boots.ChargeJumpMuls[level - 1], boots.GetChargeJumpMul(level));
            }
            Assert.Equal(boots.ChargeJumpMuls[^1], boots.GetChargeJumpMul(99));
            return Task.CompletedTask;
        }

        private static DamageSource fall() => new DamageSource { Source = EnumDamageSource.Fall, Type = EnumDamageType.Gravity };
    }
}
