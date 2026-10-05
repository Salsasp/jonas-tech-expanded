using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace JonasTechExpanded
{
    /// <summary>
    /// Holds the wearer's temporal stability at a floor that only ever ratchets upwards while a powered
    /// exoskeleton is worn. This is patched onto the player after vanilla's temporalStabilityAffected so
    /// it undoes that behavior's drain within the same tick.
    /// </summary>
    /// <remarks>
    /// EntityBehaviorTemporalStabilityAffected.stabilityOffset would be the shorter route, but the rift
    /// systems overwrite that field every tick, so anything written to it is lost.
    /// </remarks>
    public class EntityBehaviorExoStabilized : EntityBehavior
    {
        private ExoskeletonModSystem modSys;
        private EntityBehaviorTemporalStabilityAffected stabilityBh;
        private double floor = -1;

        public EntityBehaviorExoStabilized(Entity entity) : base(entity)
        {
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);

            modSys = entity.Api.ModLoader.GetModSystem<ExoskeletonModSystem>();
            stabilityBh = entity.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
        }

        public override void OnGameTick(float deltaTime)
        {
            if (stabilityBh == null || modSys == null) return;

            if (modSys.GetPoweredExo((entity as EntityPlayer)?.Player) == null)
            {
                floor = -1;
                return;
            }

            if (floor < 0) floor = stabilityBh.OwnStability;

            if (stabilityBh.OwnStability < floor)
            {
                stabilityBh.OwnStability = floor;
            }
            else
            {
                floor = stabilityBh.OwnStability;
            }
        }

        public override string PropertyName()
        {
            return "exostabilized";
        }
    }
}
