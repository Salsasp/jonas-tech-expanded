using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace JonasTechExpanded
{
    /// <summary>
    /// Chest armor that runs on temporal gears. While fueled it extends the wearer's reach, speeds up
    /// mining of every block material and prevents temporal stability loss. Uses same armor values as gambeson.
    /// </summary>
    public class ItemExoskeletonChest : Item, IWearableShapeSupplier
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

        /// <summary>Worn shape elements, with their children, that are only shown while powered.</summary>
        public string[] PoweredOnlyElements { get; private set; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            var attr = Attributes?["exoskeleton"];
            ReachBonus = attr?["reachBonus"].AsFloat(2.5f) ?? 2.5f;
            MiningSpeedMultiplier = attr?["miningSpeedMultiplier"].AsFloat(2f) ?? 2f;
            FuelHoursCapacity = attr?["fuelHoursCapacity"].AsFloat(48f) ?? 48f;
            PoweredOnlyElements = attr?["poweredOnlyElements"].AsArray<string>() ?? new[] { "TemporalGear" };
        }

        public bool IsPowered(ItemStack stack)
        {
            return GetFuelHours(stack) > 0 && GetRemainingDurability(stack) > 0;
        }

        public Shape GetShape(ItemStack stack, Entity forEntity, string texturePrefixCode)
        {
            AssetLocation shapeLoc = Shape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json");

            // Parsed fresh each call because the engine mutates the shape it is handed while attaching it.
            var shape = Vintagestory.API.Common.Shape.TryGet(api, shapeLoc);
            if (shape == null) return null;

            // Players use the server's synced flag, since other players' stack attributes can be stale on
            // this client. Anything else, like a mannequin, goes by the stack itself.
            bool powered = forEntity is EntityPlayer
                ? forEntity.WatchedAttributes.GetBool(EntityBehaviorExoVisuals.PoweredAttribute)
                : IsPowered(stack);

            if (!powered) removePoweredOnlyElements(shape);

            // Returning a shape skips the engine's own subclassing step, so redo it here including the
            // visibleDamageEffect wear.
            float damageEffect = 0;
            if (stack.ItemAttributes?["visibleDamageEffect"].AsBool() == true)
            {
                damageEffect = Math.Max(0, 1 - (float)GetRemainingDurability(stack) / GetMaxDurability(stack) * 1.1f);
            }

            shape.SubclassForStepParenting(texturePrefixCode, damageEffect);
            shape.ResolveReferences(api.World.Logger, shapeLoc.ToString());
            return shape;
        }

        private void removePoweredOnlyElements(Shape shape)
        {
            shape.RemoveElements(PoweredOnlyElements);
            if (shape.Animations == null) return;

            var remaining = new HashSet<string>();
            foreach (var element in shape.Elements) element.WalkRecursive(el => remaining.Add(el.Name));

            foreach (var keyFrame in shape.Animations.SelectMany(anim => anim.KeyFrames))
            {
                foreach (var name in keyFrame.Elements.Keys.Where(name => !remaining.Contains(name)).ToList())
                {
                    keyFrame.Elements.Remove(name);
                }
            }
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
