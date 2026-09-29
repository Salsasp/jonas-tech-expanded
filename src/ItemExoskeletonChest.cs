using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace JonasTechExpanded
{
    /// <summary>
    /// Chest armor that runs on temporal gears. While fuelled it extends the wearer's reach, speeds up
    /// mining of every block material and holds their temporal stability steady. Its protection values
    /// match a gambeson chest piece and apply whether or not the frame is powered.
    /// </summary>
    public class ItemExoskeletonChest : Item
    {
        public const string FuelHoursAttribute = "fuelHours";
        public const string FuelItemAttribute = "exoskeletonFuelHours";

        /// <summary>
        /// Blocks of reach added on top of GlobalConstants.DefaultPickingRange. The engine uses a single
        /// picking range for breaking, placing and interacting, so this raises all three at once.
        /// </summary>
        public float ReachBonus { get; private set; }

        /// <summary>Mining speed multiplier applied to every block material.</summary>
        public float MiningSpeedMultiplier { get; private set; }

        /// <summary>In-game hours of fuel the frame can hold.</summary>
        public float FuelHoursCapacity { get; private set; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Kept in the item json rather than a mod config file so that the values are authored on the
            // server and synced to every client, which matters because mining speed is calculated on
            // both sides and has to agree.
            var attr = Attributes?["exoskeleton"];
            ReachBonus = attr?["reachBonus"].AsFloat(2.5f) ?? 2.5f;
            MiningSpeedMultiplier = attr?["miningSpeedMultiplier"].AsFloat(2f) ?? 2f;
            FuelHoursCapacity = attr?["fuelHoursCapacity"].AsFloat(48f) ?? 48f;
        }

        public double GetFuelHours(ItemStack stack)
        {
            return Math.Max(0, stack.Attributes.GetDecimal(FuelHoursAttribute));
        }

        public void SetFuelHours(ItemStack stack, double fuelHours)
        {
            stack.Attributes.SetDouble(FuelHoursAttribute, Math.Clamp(fuelHours, 0, FuelHoursCapacity));
        }

        public void AddFuelHours(ItemStack stack, double fuelHours)
        {
            SetFuelHours(stack, GetFuelHours(stack) + fuelHours);
        }

        /// <summary>
        /// How many hours of runtime the given stack is worth as exoskeleton fuel, 0 if it is not fuel.
        /// </summary>
        public float GetStackFuel(ItemStack stack)
        {
            return stack?.ItemAttributes?[FuelItemAttribute].AsFloat(0) ?? 0;
        }

        public override int GetMergableQuantity(ItemStack sinkStack, ItemStack sourceStack, EnumMergePriority priority)
        {
            if (priority == EnumMergePriority.DirectMerge && GetStackFuel(sourceStack) > 0) return 1;

            return base.GetMergableQuantity(sinkStack, sourceStack, priority);
        }

        public override void TryMergeStacks(ItemStackMergeOperation op)
        {
            float fuel = op.CurrentPriority == EnumMergePriority.DirectMerge ? GetStackFuel(op.SourceSlot.Itemstack) : 0;
            if (fuel <= 0)
            {
                base.TryMergeStacks(op);
                return;
            }

            // Only accept a gear if at least half of it would actually be stored, so gears don't get
            // wasted topping off an almost full frame.
            if (GetFuelHours(op.SinkSlot.Itemstack) + fuel / 2 < FuelHoursCapacity)
            {
                AddFuelHours(op.SinkSlot.Itemstack, fuel);
                op.MovedQuantity = 1;
                op.SourceSlot.TakeOut(1);
                op.SinkSlot.MarkDirty();
                return;
            }

            (api as ICoreClientAPI)?.TriggerIngameError(this, "exoskeletonfull", Lang.Get("jonastechexpanded:ingameerror-exoskeleton-full"));
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            dsc.AppendLine();

            double fuelLeft = GetFuelHours(inSlot.Itemstack);
            if (fuelLeft > 0)
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:exoskeleton-fuel", fuelLeft, FuelHoursCapacity));
            }
            else
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:exoskeleton-nofuel"));
            }

            dsc.AppendLine(Lang.Get("jonastechexpanded:exoskeleton-effect-reach", ReachBonus));
            dsc.AppendLine(Lang.Get("jonastechexpanded:exoskeleton-effect-mining", (int)((MiningSpeedMultiplier - 1) * 100)));
            dsc.AppendLine(Lang.Get("jonastechexpanded:exoskeleton-effect-stability"));
        }
    }
}
