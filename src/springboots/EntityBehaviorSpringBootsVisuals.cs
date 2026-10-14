using Vintagestory.API.Common.Entities;

namespace JonasTechExpanded
{
    /// <summary>Powered-state visuals for the springboots: the shared <see cref="EntityBehaviorFueledWearableVisuals"/> as is.</summary>
    public class EntityBehaviorSpringBootsVisuals : EntityBehaviorFueledWearableVisuals
    {
        public const string PoweredAttribute = "jonastechexpanded:springBootsPowered";
        public const string ActiveAnimation = "springbootsactive";

        public EntityBehaviorSpringBootsVisuals(Entity entity) : base(entity, PoweredAttribute, ActiveAnimation) { }

        public override string PropertyName() => "springbootsVisuals";
    }
}
