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
    /// Shared base for wearables that run on temporal gears: per-stack fuel storage, refueling by dropping
    /// fuel onto the item, and a worn shape that drops its powered-only parts while unpowered.
    /// </summary>
    public abstract class ItemFueledWearable : Item, IWearableShapeSupplier
    {
        public const string FuelHoursAttribute = "fuelHours";

        /// <summary>Key of this item's tunables block under the item JSON's attributes.</summary>
        protected abstract string TunablesKey { get; }

        /// <summary>Item attribute on fuel items saying how many hours they are worth to this wearable.</summary>
        protected abstract string FuelItemAttribute { get; }

        /// <summary>Watched attribute the server publishes on the wearer while this wearable is powered.</summary>
        protected abstract string PoweredAttribute { get; }

        protected abstract string[] DefaultPoweredOnlyElements { get; }

        /// <summary>In-game hours of fuel the item can hold.</summary>
        public float FuelHoursCapacity { get; private set; }

        /// <summary>Worn shape elements, with their children, that are only shown while powered.</summary>
        public string[] PoweredOnlyElements { get; private set; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            var attr = Attributes?[TunablesKey];
            FuelHoursCapacity = attr?["fuelHoursCapacity"].AsFloat(48f) ?? 48f;
            PoweredOnlyElements = attr?["poweredOnlyElements"].AsArray<string>() ?? DefaultPoweredOnlyElements;
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
                ? forEntity.WatchedAttributes.GetBool(PoweredAttribute)
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
        /// How many hours of runtime the given stack is worth as fuel for this wearable, 0 if it is not fuel.
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

            // Only accept fuel if at least half of it would actually be stored, so gears don't get
            // wasted topping off an almost full item.
            if (GetFuelHours(op.SinkSlot.Itemstack) + fuel / 2 < FuelHoursCapacity)
            {
                AddFuelHours(op.SinkSlot.Itemstack, fuel);
                op.MovedQuantity = 1;
                op.SourceSlot.TakeOut(1);
                op.SinkSlot.MarkDirty();
                return;
            }

            (api as ICoreClientAPI)?.TriggerIngameError(this, "fuelablefull", Lang.Get("jonastechexpanded:ingameerror-fuelable-full"));
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            dsc.AppendLine();

            double fuelLeft = GetFuelHours(inSlot.Itemstack);
            if (fuelLeft > 0)
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:fuelable-fuel", fuelLeft, FuelHoursCapacity));
            }
            else
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:fuelable-nofuel"));
                if (BlankLineAfterNoFuel) dsc.AppendLine();
            }

            AppendEffectInfo(dsc);
        }

        /// <summary>Whether the tooltip leaves a blank line between the out-of-fuel hint and the effect lines.</summary>
        protected virtual bool BlankLineAfterNoFuel => false;

        /// <summary>Adds this wearable's "while powered" lines to the tooltip, after the fuel line.</summary>
        protected abstract void AppendEffectInfo(StringBuilder dsc);
    }
}
