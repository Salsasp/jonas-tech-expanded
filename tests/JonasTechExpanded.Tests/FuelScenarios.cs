using Atlas.XUnit;
using Vintagestory.API.Common;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// <see cref="ItemFueledWearable"/> behavior shared by both wearables: storage, refueling with temporal gears,
    /// powered state, and draining over in-game time while worn.
    /// </summary>
    public class FuelScenarios : AtlasScenarioBase
    {
        public static IEnumerable<object[]> Wearables => new[]
        {
            new object[] { "jonastechexpanded:exoskeletonchest", EnumCharacterDressType.ArmorBody, "jonastechexpanded:exoPowered" },
            new object[] { "jonastechexpanded:springboots", EnumCharacterDressType.ArmorLegs, "jonastechexpanded:springBootsPowered" }
        };

        [AtlasTheory]
        [MemberData(nameof(Wearables))]
        public Task Fuel_is_clamped_to_capacity(string code, EnumCharacterDressType _, string __)
        {
            var stack = TestKit.Fueled(World, code, 1000);
            var item = (ItemFueledWearable)stack.Collectible;

            Assert.Equal(item.FuelHoursCapacity, item.GetFuelHours(stack), 3);

            item.AddFuelHours(stack, -5000);
            Assert.Equal(0, item.GetFuelHours(stack));
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [MemberData(nameof(Wearables))]
        public Task Powered_needs_fuel_and_durability(string code, EnumCharacterDressType _, string __)
        {
            var stack = TestKit.Fueled(World, code, 10);
            var item = (ItemFueledWearable)stack.Collectible;
            Assert.True(item.IsPowered(stack));

            item.SetFuelHours(stack, 0);
            Assert.False(item.IsPowered(stack));

            item.SetFuelHours(stack, 10);
            item.SetDurability(stack, 0);
            Assert.False(item.IsPowered(stack));
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [MemberData(nameof(Wearables))]
        public Task Temporal_gear_refuels_by_direct_merge(string code, EnumCharacterDressType _, string __)
        {
            var sink = new DummySlot(TestKit.Fueled(World, code, 0));
            var source = new DummySlot(new ItemStack(TestKit.Item(World, "game:gear-temporal"), 3));

            int moved = mergeGearInto(source, sink);

            Assert.Equal(1, moved);
            Assert.Equal(2, source.StackSize);
            Assert.Equal(24, ((ItemFueledWearable)sink.Itemstack.Collectible).GetFuelHours(sink.Itemstack), 3);
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [MemberData(nameof(Wearables))]
        public Task Refuel_is_refused_when_less_than_half_a_gear_fits(string code, EnumCharacterDressType _, string __)
        {
            var sink = new DummySlot(TestKit.Fueled(World, code, 40));
            var source = new DummySlot(new ItemStack(TestKit.Item(World, "game:gear-temporal"), 1));

            mergeGearInto(source, sink);

            Assert.Equal(1, source.StackSize);
            Assert.Equal(40, ((ItemFueledWearable)sink.Itemstack.Collectible).GetFuelHours(sink.Itemstack), 3);
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [MemberData(nameof(Wearables))]
        public async Task Worn_fuel_drains_with_in_game_time(string code, EnumCharacterDressType slot, string _)
        {
            var player = await World.JoinPlayer("Drain" + slot);
            var worn = TestKit.Equip(player, slot, TestKit.Fueled(World, code, 10));
            var item = (ItemFueledWearable)worn.Itemstack.Collectible;

            // Let one drain pass seed its timestamp, then skip an in-game hour.
            await World.Ticks(60);
            double before = item.GetFuelHours(worn.Itemstack);
            World.Calendar.Add(1);

            await TestKit.UntilTicked(World, () => item.GetFuelHours(worn.Itemstack) <= before - 0.9);
            Assert.InRange(item.GetFuelHours(worn.Itemstack), before - 1.2, before - 0.9);
        }

        [AtlasTheory]
        [MemberData(nameof(Wearables))]
        public async Task Server_publishes_the_powered_flag(string code, EnumCharacterDressType slot, string poweredAttribute)
        {
            var player = await World.JoinPlayer("Flag" + slot);
            var attrs = player.Entity.WatchedAttributes;

            var worn = TestKit.Equip(player, slot, TestKit.Fueled(World, code, 10));
            await TestKit.UntilTicked(World, () => attrs.GetBool(poweredAttribute));

            ((ItemFueledWearable)worn.Itemstack.Collectible).SetFuelHours(worn.Itemstack, 0);
            worn.MarkDirty();
            await TestKit.UntilTicked(World, () => !attrs.GetBool(poweredAttribute));

            Assert.False(attrs.GetBool(poweredAttribute));
        }

        /// <summary>
        /// Left-clicks a held gear onto the wearable's slot, the inventory path players use. TryPutInto can't be
        /// used here: it checks mergeability at AutoMerge priority, and refueling only accepts DirectMerge.
        /// </summary>
        private int mergeGearInto(ItemSlot source, ItemSlot sink)
        {
            var op = new ItemStackMoveOperation(World.Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, source.StackSize);
            sink.ActivateSlot(source, ref op);
            return op.MovedQuantity;
        }
    }
}
