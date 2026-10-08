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
    /// Leg armor that increases jump height, move speed, and adds a charge jump. Runs on temporal gears.
    /// </summary>
    public class ItemSpringBoots : Item, IWearableShapeSupplier
    {
        public const string FuelHoursAttribute = "fuelHours";
        public const string FuelItemAttribute = "springbootsFuelHours";
        public float SpeedMultiplier { get; private set; }

        /// <summary>jumpHeightMul written while powered. Replaces the vanilla base of 1.</summary>
        public float JumpHeightMul { get; private set; }

        /// <summary>How long a released charge stays armed, in seconds.</summary>
        public float ChargeHoldSeconds { get; private set; }

        /// <summary>Hold times in seconds that unlock charge levels 1..N.</summary>
        public float[] ChargeThresholdSeconds { get; private set; }

        /// <summary>jumpHeightMul used for a charged jump at each level, replacing the powered value.</summary>
        public float[] ChargeJumpMuls { get; private set; }

        public AssetLocation ChargeSound { get; private set; }
        public AssetLocation ChargeLevelSound { get; private set; }
        public AssetLocation ChargeArmedSound { get; private set; }

        /// <summary>In-game hours of fuel drained per 1 HP of fall damage absorbed.</summary>
        public float FallDamageFuelHoursPerHp { get; private set; }

        /// <summary>In-game hours of fuel the frame can hold.</summary>
        public float FuelHoursCapacity { get; private set; }

        /// <summary>Worn shape elements, with their children, that are only shown while powered.</summary>
        public string[] PoweredOnlyElements { get; private set; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            var attr = Attributes?["springboots"];
            SpeedMultiplier = attr?["speedBonus"].AsFloat(2.0f) ?? 2.0f;
            JumpHeightMul = attr?["jumpHeightMul"].AsFloat(2.0f) ?? 2.0f;
            ChargeHoldSeconds = attr?["chargeHoldSeconds"].AsFloat(3f) ?? 3f;
            ChargeThresholdSeconds = attr?["chargeThresholdSeconds"]?.AsArray<float>(new[] { 0.5f, 1.25f, 2.0f })
                ?? new[] { 0.5f, 1.25f, 2.0f };
            ChargeJumpMuls = attr?["chargeJumpMuls"]?.AsArray<float>(new[] { 2.0f, 3.0f, 4.0f })
                ?? new[] { 2.0f, 3.0f, 4.0f };
            ChargeSound = AssetLocation.Create(attr?["chargeSound"]?.AsString("game:sounds/effect/woodgrind") ?? "game:sounds/effect/woodgrind");
            ChargeLevelSound = AssetLocation.Create(attr?["chargeLevelSound"]?.AsString("game:sounds/effect/woodswitch") ?? "game:sounds/effect/woodswitch");
            ChargeArmedSound = AssetLocation.Create(attr?["chargeArmedSound"]?.AsString("game:sounds/effect/tempstab-drain") ?? "game:sounds/effect/tempstab-drain");
            FallDamageFuelHoursPerHp = attr?["fallDamageFuelHoursPerHp"]?.AsFloat(0.5f) ?? 0.5f;
            FuelHoursCapacity = attr?["fuelHoursCapacity"].AsFloat(48f) ?? 48f;
            PoweredOnlyElements = attr?["poweredOnlyElements"].AsArray<string>() ?? new[] { "TemporalGearL", "TemporalGearR" };
        }

        public int GetChargeLevel(float chargeTime)
        {
            int level = 0;
            for (int i = 0; i < ChargeThresholdSeconds.Length; i++)
            {
                if (chargeTime >= ChargeThresholdSeconds[i]) level = i + 1;
            }
            return Math.Min(level, ChargeJumpMuls.Length);
        }

        public float GetChargeJumpMul(int level)
        {
            if (level < 1 || ChargeJumpMuls.Length == 0) return JumpHeightMul;
            int i = Math.Min(level, ChargeJumpMuls.Length) - 1;
            return ChargeJumpMuls[i];
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
                ? forEntity.WatchedAttributes.GetBool(EntityBehaviorSpringBootsVisuals.PoweredAttribute)
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

            // TODO: generalize this error to just fuelable item full (for exo as well)
            (api as ICoreClientAPI)?.TriggerIngameError(this, "springbootsfull", Lang.Get("ingameerror-fuelable-full"));
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
            }

            if (JumpHeightMul > 1f)
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:springboots-effect-jump", JumpHeightMul));
            }
            if (SpeedMultiplier > 1f)
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:springboots-effect-speed", SpeedMultiplier));
            }
            if (ChargeJumpMuls != null && ChargeJumpMuls.Length > 0)
            {
                dsc.AppendLine(Lang.Get(
                    "jonastechexpanded:springboots-effect-charge",
                    string.Join(" / ", ChargeJumpMuls),
                    ChargeHoldSeconds));
            }
            dsc.AppendLine(Lang.Get("jonastechexpanded:springboots-effect-fall", FallDamageFuelHoursPerHp));
        }
    }
}
