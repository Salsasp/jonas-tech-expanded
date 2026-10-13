using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace JonasTechExpanded
{
    /// <summary>
    /// Speeds up vanilla machines that time themselves against the calendar (a stored "done at" or "started at"
    /// game hour) by moving that timestamp. Server only. Machines that count real seconds per tick are handled by
    /// <see cref="FirepitAccelPatch"/> and <see cref="ClayOvenAccelPatch"/> instead, and item transitions by <see cref="AccelFieldTransitions"/>.
    /// </summary>
    public static class AccelFieldAdapters
    {
        private static readonly AccessTools.FieldRef<BlockEntityBeehive, double> hiveHarvestableAt =
            AccessTools.FieldRefAccess<BlockEntityBeehive, double>("harvestableAtTotalHours");
        private static readonly AccessTools.FieldRef<BlockEntityBeehive, double> hiveCooldownUntil =
            AccessTools.FieldRefAccess<BlockEntityBeehive, double>("cooldownUntilTotalHours");
        private static readonly AccessTools.FieldRef<BlockEntityBeehive, double> hivePopStart =
            AccessTools.FieldRefAccess<BlockEntityBeehive, double>("beginPopStartTotalHours");

        private static readonly AccessTools.FieldRef<BlockEntityCharcoalPit, double> pitStartingAfter =
            AccessTools.FieldRefAccess<BlockEntityCharcoalPit, double>("startingAfterTotalHours");
        private static readonly AccessTools.FieldRef<BlockEntityCharcoalPit, double> pitFinishedAfter =
            AccessTools.FieldRefAccess<BlockEntityCharcoalPit, double>("finishedAfterTotalHours");

        // The bloomery counts in days, not hours.
        private static readonly AccessTools.FieldRef<BlockEntityBloomery, double> bloomeryUntilDays =
            AccessTools.FieldRefAccess<BlockEntityBloomery, double>("burningUntilTotalDays");
        private static readonly AccessTools.FieldRef<BlockEntityBloomery, double> bloomeryStartDays =
            AccessTools.FieldRefAccess<BlockEntityBloomery, double>("burningStartTotalDays");

        /// <summary>
        /// Gives <paramref name="be"/> <paramref name="extraHours"/> of progress on top of the time that really
        /// passed. Vanilla only ever checks "has the calendar passed this timestamp", so moving it can't break state.
        /// </summary>
        public static void ApplyExtraHours(BlockEntity be, double extraHours)
        {
            if (extraHours <= 0) return;

            switch (be)
            {
                case BlockEntityPitKiln kiln when kiln.Lit:
                    kiln.BurningUntilTotalHours -= extraHours;
                    kiln.MarkDirty();
                    break;

                case BlockEntityBarrel barrel when barrel.Sealed:
                    barrel.SealedSinceTotalHours -= extraHours;
                    barrel.MarkDirty();
                    break;

                // Skeps and wild hives: honey, swarm cooldown, and the countdown to populating an empty skep.
                case BlockEntityBeehive hive:
                    hiveHarvestableAt(hive) -= extraHours;
                    hiveCooldownUntil(hive) -= extraHours;
                    hivePopStart(hive) -= extraHours;
                    hive.MarkDirty();
                    break;

                // Both the 30 minute warmup and the burn. While unsealed the pit resets its finish time every
                // tick anyway, so shifting it then does nothing. Only the pit's own (ignited) block entity counts.
                case BlockEntityCharcoalPit pit when pit.Lit:
                    pitStartingAfter(pit) -= extraHours;
                    pitFinishedAfter(pit) -= extraHours;
                    pit.MarkDirty();
                    break;

                // The start moves too, so the glow's ease-in/out (driven by both timestamps) stays consistent.
                case BlockEntityBloomery bloomery when bloomery.IsBurning:
                    bloomeryUntilDays(bloomery) -= extraHours / 24;
                    bloomeryStartDays(bloomery) -= extraHours / 24;
                    bloomery.MarkDirty();
                    break;
            }
        }
    }

    /// <summary>
    /// The clay oven works like the firepit: its 100ms tick adds real seconds to fuel burn, temperature and baking.
    /// Patching the base method covers it because the tick listener calls the virtual method.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityOven), "OnBurnTick")]
    public static class ClayOvenAccelPatch
    {
        public static void Prefix(BlockEntityOven __instance, ref float dt)
        {
            if (__instance.Api?.Side != EnumAppSide.Server) return;

            var system = __instance.Api.ModLoader.GetModSystem<AccelFieldModSystem>();
            if (system != null) dt *= system.GetMul(__instance.Pos);
        }
    }

    /// <summary>
    /// The firepit (and the cooking pot, crucible or spit in it, which have no timer of their own) adds real
    /// seconds to its cooking and fuel counters every 100ms tick. Scaling that tick's dt speeds up everything it
    /// does, fuel burn included.
    /// </summary>
    [HarmonyPatch(typeof(BlockEntityFirepit), "OnBurnTick")]
    public static class FirepitAccelPatch
    {
        public static void Prefix(BlockEntityFirepit __instance, ref float dt)
        {
            if (__instance.Api?.Side != EnumAppSide.Server) return;

            var system = __instance.Api.ModLoader.GetModSystem<AccelFieldModSystem>();
            if (system != null) dt *= system.GetMul(__instance.Pos);
        }
    }
}
