using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace JonasTechExpanded
{
    /// <summary>
    /// Chest armor that runs on temporal gears. While fueled it extends the wearer's reach, speeds up
    /// mining of every block material and prevents temporal stability loss. Uses same armor values as gambeson.
    /// </summary>
    public class ItemExoskeletonChest : ItemFueledWearable
    {
        protected override string TunablesKey => "exoskeleton";
        protected override string FuelItemAttribute => "exoskeletonFuelHours";
        protected override string PoweredAttribute => EntityBehaviorExoVisuals.PoweredAttribute;
        protected override string[] DefaultPoweredOnlyElements => new[] { "TemporalGear" };

        /// <summary>
        /// Blocks of reach added on top of GlobalConstants.DefaultPickingRange. The engine uses a single
        /// picking range for breaking, placing and interacting, so this raises all three at once.
        /// </summary>
        public float ReachBonus { get; private set; }

        /// <summary>Mining speed multiplier applied to every block material.</summary>
        public float MiningSpeedMultiplier { get; private set; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            var attr = Attributes?[TunablesKey];
            ReachBonus = attr?["reachBonus"].AsFloat(2.5f) ?? 2.5f;
            MiningSpeedMultiplier = attr?["miningSpeedMultiplier"].AsFloat(2f) ?? 2f;
        }

        protected override void AppendEffectInfo(StringBuilder dsc)
        {
            dsc.AppendLine(Lang.Get("jonastechexpanded:exoskeleton-effect-reach", ReachBonus));
            dsc.AppendLine(Lang.Get("jonastechexpanded:exoskeleton-effect-mining", (int)((MiningSpeedMultiplier - 1) * 100)));
            dsc.AppendLine(Lang.Get("jonastechexpanded:exoskeleton-effect-stability"));
        }
    }
}
