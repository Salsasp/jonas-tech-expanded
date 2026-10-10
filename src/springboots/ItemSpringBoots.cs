using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace JonasTechExpanded
{
    /// <summary>
    /// Leg armor that increases jump height, move speed, and adds a charge jump. Runs on temporal gears.
    /// </summary>
    public class ItemSpringBoots : ItemFueledWearable
    {
        protected override string TunablesKey => "springboots";
        protected override string FuelItemAttribute => "springbootsFuelHours";
        protected override string PoweredAttribute => EntityBehaviorSpringBootsVisuals.PoweredAttribute;
        protected override string[] DefaultPoweredOnlyElements => new[] { "TemporalGearL", "TemporalGearR" };
        protected override bool BlankLineAfterNoFuel => true;

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

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            var attr = Attributes?[TunablesKey];
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

        protected override void AppendEffectInfo(StringBuilder dsc)
        {
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
