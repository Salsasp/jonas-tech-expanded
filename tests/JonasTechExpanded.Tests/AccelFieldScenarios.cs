using System.Linq;
using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JonasTechExpanded.Tests
{
    /// <summary>
    /// The temporal acceleration field: fuel and its cost per setting, which blocks it covers, the GUI's setting
    /// packets, and each kind of vanilla process it speeds up. Acceleration passes are driven by calling
    /// <see cref="AccelFieldModSystem.Tick"/> with a chosen number of in-game hours. The system's own 1s tick may
    /// add a sliver more in between, hence the tolerances.
    /// </summary>
    public class AccelFieldScenarios : AtlasScenarioBase
    {
        private const string FieldCode = "jonastechexpanded:temporalaccelfield";
        private const double Tolerance = 0.1;

        private AccelFieldModSystem System => World.Api.ModLoader.GetModSystem<AccelFieldModSystem>();

        [AtlasScenario]
        public Task Block_and_gear_fuel_are_registered()
        {
            var block = TestKit.Block(World, FieldCode);
            Assert.IsType<BlockTemporalAccelField>(block);
            Assert.Equal("TemporalAccelField", block.EntityClass);

            var gear = TestKit.Item(World, "game:gear-temporal");
            Assert.Equal(24, gear.Attributes[BlockEntityTemporalAccelField.FuelItemAttribute].AsFloat(0));
            return Task.CompletedTask;
        }

        [AtlasTheory]
        [InlineData(typeof(BlockEntityFirepit), typeof(FirepitAccelPatch))]
        [InlineData(typeof(BlockEntityOven), typeof(ClayOvenAccelPatch))]
        public Task Real_second_ticks_are_patched(Type blockEntity, Type patch)
        {
            var method = AccessTools.Method(blockEntity, "OnBurnTick");
            var patches = Harmony.GetPatchInfo(method);
            Assert.True(patches != null && patches.Prefixes.Any(p => p.PatchMethod.DeclaringType == patch),
                $"{blockEntity.Name}.OnBurnTick has no {patch.Name} prefix");
            return Task.CompletedTask;
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Switched_off_field_accelerates_nothing_and_keeps_its_fuel()
        {
            var player = await World.JoinPlayer("FieldPower");
            BlockPos center = World.Spawn.Offset(0, 2, 0);
            var field = await placeField(center, gears: 1, activate: true);
            field.FuelBuffer = 10;

            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetEnabled, BitConverter.GetBytes(0));
            Assert.False(field.Enabled);
            Assert.False(field.IsActive);
            Assert.Equal(1, System.GetMul(center.AddCopy(1, 0, 0)));

            System.Tick(5);
            Assert.Equal(10, field.FuelBuffer);
            Assert.Equal(1, field.FuelSlot.StackSize);

            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetEnabled, BitConverter.GetBytes(1));
            Assert.True(field.IsActive);
            Assert.Equal(2, System.GetMul(center.AddCopy(1, 0, 0)));
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Running_changed_fires_for_switch_and_fuel_running_out()
        {
            var player = await World.JoinPlayer("FieldHook");
            var field = await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: false);
            var events = new List<bool>();
            field.RunningChanged += (_, running) => events.Add(running);

            field.FuelBuffer = 2;
            field.ConsumeFuel(0);
            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetEnabled, BitConverter.GetBytes(0));
            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetEnabled, BitConverter.GetBytes(1));
            // Setting the same value again is not a change.
            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetEnabled, BitConverter.GetBytes(1));
            field.ConsumeFuel(5);

            Assert.Equal(new[] { true, false, true, false }, events);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Clay_oven_burns_twice_as_fast_inside_a_field()
        {
            var ovenBlock = World.Api.World.Blocks.FirstOrDefault(b => b.EntityClass == "Oven");
            Assert.True(ovenBlock != null, "No clay oven block found");

            BlockPos ovenPos = World.Spawn.Offset(1, 1, 0);
            World.SetBlock(ovenBlock.Code.ToString(), ovenPos);
            await World.Ticks(2);
            var oven = World.BlockEntityAt<BlockEntityOven>(ovenPos);
            Assert.NotNull(oven);

            var onBurnTick = AccessTools.Method(typeof(BlockEntityOven), "OnBurnTick");
            var fuelBurnTime = Traverse.Create(oven).Field<float>("fuelBurnTime");
            float burnOneSecond()
            {
                fuelBurnTime.Value = 100;
                onBurnTick.Invoke(oven, new object[] { 1f });
                return 100 - fuelBurnTime.Value;
            }

            // Vanilla already runs the oven at 1.25x real time.
            Assert.Equal(1.25f, burnOneSecond(), 3);

            await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: true);
            Assert.Equal(2.5f, burnOneSecond(), 3);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Burning_bloomery_finishes_sooner()
        {
            var bloomeryBlock = World.Api.World.Blocks.FirstOrDefault(b => b.EntityClass == "Bloomery");
            Assert.True(bloomeryBlock != null, "No bloomery block found");

            BlockPos pos = World.Spawn.Offset(1, 1, 0);
            World.SetBlock(bloomeryBlock.Code.ToString(), pos);
            await World.Ticks(2);
            var bloomery = World.BlockEntityAt<BlockEntityBloomery>(pos);
            Assert.NotNull(bloomery);

            await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: true);

            // Far in the future, so the bloomery's own tick doesn't try to smelt an empty ore slot.
            double until = World.Api.World.Calendar.TotalDays + 10;
            Traverse.Create(bloomery).Field<bool>("burning").Value = true;
            var untilDays = Traverse.Create(bloomery).Field<double>("burningUntilTotalDays");
            untilDays.Value = until;

            System.Tick(12);
            // 12 hours at 2x: 12 extra hours, half a day.
            Assert.InRange(untilDays.Value, until - 0.5 - Tolerance / 24, until - 0.5 + Tolerance / 24);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Lit_charcoal_pit_warms_up_and_burns_sooner()
        {
            var pitBlock = World.Api.World.Blocks.FirstOrDefault(b => b.EntityClass == "CharcoalPit");
            Assert.True(pitBlock != null, "No charcoal pit block found");

            BlockPos pos = World.Spawn.Offset(1, 1, 0);
            World.SetBlock(pitBlock.Code.ToString(), pos);
            await World.Ticks(2);
            var pit = World.BlockEntityAt<BlockEntityCharcoalPit>(pos);
            Assert.NotNull(pit);

            await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: true);

            double now = World.Api.World.Calendar.TotalHours;
            var starting = Traverse.Create(pit).Field<double>("startingAfterTotalHours");
            var finished = Traverse.Create(pit).Field<double>("finishedAfterTotalHours");
            starting.Value = now + 10;
            finished.Value = now + 30;

            // Unlit pits are left alone.
            System.Tick(3);
            Assert.Equal(now + 10, starting.Value);

            Traverse.Create(pit).Property<bool>("Lit").Value = true;
            System.Tick(3);
            Assert.InRange(starting.Value, now + 7 - Tolerance, now + 7 + Tolerance);
            Assert.InRange(finished.Value, now + 27 - Tolerance, now + 27 + Tolerance);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Gear_is_burned_into_the_buffer_and_drains_per_hour()
        {
            var field = await placeField(World.Spawn.Offset(0, 1, 0), gears: 2, activate: false);
            Assert.False(field.IsActive);

            System.Tick(0);
            Assert.True(field.IsActive);
            Assert.Equal(1, field.FuelSlot.StackSize);
            Assert.InRange(field.FuelBuffer, 24 - Tolerance, 24);

            System.Tick(1);
            Assert.InRange(field.FuelBuffer, 23 - Tolerance, 23);
            Assert.InRange(field.HoursRemaining, 47 - Tolerance, 47);

            // Draining past the buffer lights the next gear.
            System.Tick(30);
            Assert.True(field.FuelSlot.Empty);
            Assert.InRange(field.FuelBuffer, 17 - Tolerance, 17);

            System.Tick(20);
            Assert.False(field.IsActive);
            Assert.Equal(0, field.FuelBuffer);
        }

        [AtlasTheory]
        // Design doc: 24h per gear at the defaults, speed cost 1/4/8x, size cost 1/2/3x, multiplied.
        [InlineData(0, 0, 24)]
        [InlineData(0, 1, 6)]
        [InlineData(0, 2, 3)]
        [InlineData(1, 0, 12)]
        [InlineData(2, 0, 8)]
        [InlineData(2, 2, 1)]
        public async Task Hours_per_gear_follow_size_and_speed(int sizeIndex, int speedIndex, double hoursPerGear)
        {
            var field = await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: false);
            setIndices(field, sizeIndex, speedIndex);

            Assert.Equal(hoursPerGear, field.HoursPerGear, 3);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Multiplier_covers_the_cube_but_not_the_machine()
        {
            BlockPos center = World.Spawn.Offset(0, 2, 0);
            var field = await placeField(center, gears: 0, activate: true);

            Assert.Equal(1, System.GetMul(center));
            Assert.Equal(2, System.GetMul(center.AddCopy(1, 1, -1)));
            Assert.Equal(1, System.GetMul(center.AddCopy(2, 0, 0)));

            setIndices(field, 1, 2);
            Assert.Equal(8, System.GetMul(center.AddCopy(2, -2, 2)));
            Assert.Equal(1, System.GetMul(center.AddCopy(3, 0, 0)));

            field.FuelBuffer = 0;
            Assert.Equal(1, System.GetMul(center.AddCopy(1, 0, 0)));
        }

        [AtlasTheory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Show_field_sphere_reaches_the_corner_blocks(int sizeIndex)
        {
            var field = await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: false);
            setIndices(field, sizeIndex, 0);

            // Design: the shell passes through the centers of the field's corner blocks, (size - 1) / 2 blocks
            // out on each axis. It must not shrink below that, or corner blocks would look outside the field.
            float cornerBlockCenter = (field.Size - 1) / 2f * MathF.Sqrt(3);
            Assert.True(AccelFieldSphereRenderer.SphereRadius(field) >= cornerBlockCenter - 0.001f,
                $"{field.Size}^3 field: sphere radius {AccelFieldSphereRenderer.SphereRadius(field)} is inside the corner blocks' centers at {cornerBlockCenter}");
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Overlapping_fields_use_the_fastest_not_the_sum()
        {
            BlockPos a = World.Spawn.Offset(0, 2, 0);
            var slow = await placeField(a, gears: 0, activate: true);
            var fast = await placeField(a.AddCopy(2, 0, 0), gears: 0, activate: true);
            setIndices(fast, 0, 1);

            Assert.Equal(4, System.GetMul(a.AddCopy(1, 0, 0)));
            Assert.Equal(2, System.GetMul(a.AddCopy(-1, 0, 0)));
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Settings_packets_change_settings_and_ignore_bad_values()
        {
            var player = await World.JoinPlayer("FieldGui");
            var field = await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: false);

            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetSpeed, BitConverter.GetBytes(2));
            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetSize, BitConverter.GetBytes(1));
            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetVisible, BitConverter.GetBytes(1));
            Assert.Equal(8, field.Speed);
            Assert.Equal(5, field.Size);
            Assert.True(field.FieldVisible);

            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetSpeed, BitConverter.GetBytes(7));
            field.OnReceivedClientPacket(player.Player, BlockEntityTemporalAccelField.PacketSetSize, BitConverter.GetBytes(-1));
            Assert.Equal(8, field.Speed);
            Assert.Equal(5, field.Size);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Fuel_slot_only_takes_field_fuel()
        {
            var field = await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: false);

            Assert.True(field.FuelSlot.CanHold(new DummySlot(new ItemStack(TestKit.Item(World, "game:gear-temporal")))));
            Assert.False(field.FuelSlot.CanHold(new DummySlot(new ItemStack(TestKit.Item(World, "game:flaxtwine")))));
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Firepit_burns_twice_as_fast_inside_a_field()
        {
            BlockPos firePos = World.Spawn.Offset(1, 1, 0);
            World.SetBlock("game:firepit-lit", firePos);
            await World.Ticks(2);
            var firepit = World.BlockEntityAt<BlockEntityFirepit>(firePos);
            Assert.NotNull(firepit);

            var onBurnTick = AccessTools.Method(typeof(BlockEntityFirepit), "OnBurnTick");
            float burnOneSecond()
            {
                // Far from max temperature with an empty input, so the idle 4x fuel saving doesn't kick in.
                firepit.fuelBurnTime = firepit.maxFuelBurnTime = 100;
                firepit.maxTemperature = 1000;
                firepit.furnaceTemperature = 20;
                onBurnTick.Invoke(firepit, new object[] { 1f });
                return 100 - firepit.fuelBurnTime;
            }

            Assert.Equal(1, burnOneSecond(), 3);

            await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: true);
            Assert.Equal(2, burnOneSecond(), 3);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Lit_pit_kiln_finishes_sooner()
        {
            BlockPos kilnPos = World.Spawn.Offset(1, 1, 0);
            World.SetBlock("game:pitkiln", kilnPos);
            await World.Ticks(2);
            var kiln = World.BlockEntityAt<BlockEntityPitKiln>(kilnPos);
            Assert.NotNull(kiln);

            var field = await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: true);
            setIndices(field, 0, 1);

            double until = World.Api.World.Calendar.TotalHours + 20;
            kiln.Lit = true;
            kiln.BurningUntilTotalHours = until;

            System.Tick(2);
            // 2 hours at 4x: 6 hours of extra progress.
            Assert.InRange(kiln.BurningUntilTotalHours, until - 6 - Tolerance, until - 6 + Tolerance);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Sealed_barrel_counts_extra_hours_and_unsealed_does_not()
        {
            BlockPos barrelPos = World.Spawn.Offset(1, 1, 0);
            World.SetBlock("game:barrel", barrelPos);
            await World.Ticks(2);
            var barrel = World.BlockEntityAt<BlockEntityBarrel>(barrelPos);
            Assert.NotNull(barrel);

            await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: true);

            barrel.Sealed = false;
            barrel.SealedSinceTotalHours = 100;
            System.Tick(3);
            Assert.Equal(100, barrel.SealedSinceTotalHours);

            barrel.Sealed = true;
            System.Tick(3);
            Assert.InRange(barrel.SealedSinceTotalHours, 97 - Tolerance, 97 + Tolerance);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Skep_timers_run_faster()
        {
            var skepBlock = World.Api.World.Blocks.FirstOrDefault(b => b is BlockSkep && b.Variant["type"] == "populated");
            Assert.True(skepBlock != null, "No populated skep block found");

            BlockPos hivePos = World.Spawn.Offset(1, 1, 0);
            World.SetBlock(skepBlock.Code.ToString(), hivePos);
            await World.Ticks(2);
            var hive = World.BlockEntityAt<BlockEntityBeehive>(hivePos);
            Assert.NotNull(hive);

            await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: true);

            // Set right before the pass: the hive's own 3s tick can move this timer in cold weather.
            var harvestableAt = AccessTools.FieldRefAccess<BlockEntityBeehive, double>("harvestableAtTotalHours");
            harvestableAt(hive) = 500;
            System.Tick(4);

            Assert.InRange(harvestableAt(hive), 496 - Tolerance, 496 + Tolerance);
        }

        [AtlasScenario(RollbackWorld = true)]
        public async Task Container_transitions_speed_up_inside_and_reset_outside()
        {
            BlockPos barrelPos = World.Spawn.Offset(1, 1, 0);
            World.SetBlock("game:barrel", barrelPos);
            await World.Ticks(2);
            var inv = World.BlockEntityAt<BlockEntityBarrel>(barrelPos).Inventory;
            var stack = new ItemStack(TestKit.Item(World, "game:flaxtwine"));

            float baseline = inv.GetTransitionSpeedMul(EnumTransitionType.Dry, stack);
            Assert.True(baseline > 0, "Barrel drying rate should be nonzero to compare against");

            var field = await placeField(World.Spawn.Offset(0, 1, 0), gears: 0, activate: true);
            setIndices(field, 0, 2);
            System.Tick(0);
            Assert.Equal(baseline * 8, inv.GetTransitionSpeedMul(EnumTransitionType.Dry, stack), 3);

            field.FuelBuffer = 0;
            System.Tick(0);
            Assert.Equal(baseline, inv.GetTransitionSpeedMul(EnumTransitionType.Dry, stack), 3);
        }

        /// <summary>Places a field. <paramref name="activate"/> fills its buffer directly instead of burning a gear.</summary>
        private async Task<BlockEntityTemporalAccelField> placeField(BlockPos pos, int gears, bool activate)
        {
            World.SetBlock(FieldCode, pos);
            await World.Ticks(2);

            var field = World.BlockEntityAt<BlockEntityTemporalAccelField>(pos);
            Assert.NotNull(field);

            if (gears > 0)
            {
                field.FuelSlot.Itemstack = new ItemStack(TestKit.Item(World, "game:gear-temporal"), gears);
                field.FuelSlot.MarkDirty();
            }

            if (activate)
            {
                field.FuelBuffer = 1000;
                field.ConsumeFuel(0);
            }
            return field;
        }

        private static void setIndices(BlockEntityTemporalAccelField field, int sizeIndex, int speedIndex)
        {
            typeof(BlockEntityTemporalAccelField).GetProperty(nameof(BlockEntityTemporalAccelField.SizeIndex))!.SetValue(field, sizeIndex);
            typeof(BlockEntityTemporalAccelField).GetProperty(nameof(BlockEntityTemporalAccelField.SpeedIndex))!.SetValue(field, speedIndex);
        }
    }
}
