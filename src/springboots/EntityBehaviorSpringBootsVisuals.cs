using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace JonasTechExpanded
{
    public class EntityBehaviorSpringBootsVisuals : EntityBehavior
    {
        public const string PoweredAttribute = "jonastechexpanded:springBootsPowered";
        public const string ActiveAnimation = "springBootsactive";

        private bool powered;

        public EntityBehaviorSpringBootsVisuals(Entity entity) : base(entity) { }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            powered = entity.WatchedAttributes.GetBool(PoweredAttribute);
            entity.WatchedAttributes.RegisterModifiedListener(PoweredAttribute, onPoweredChanged);
        }

        private void onPoweredChanged()
        {
            bool nowPowered = entity.WatchedAttributes.GetBool(PoweredAttribute);
            if (nowPowered == powered) return;

            powered = nowPowered;
            entity.MarkShapeModified();
        }

        public override void OnGameTick(float deltaTime)
        {
            // Polled rather than set on change, because the animator doesn't exist yet during Initialize.
            var animManager = entity.AnimManager;
            if (animManager == null) return;

            bool running = animManager.IsAnimationActive(ActiveAnimation);
            if (powered && !running) animManager.StartAnimation(ActiveAnimation);
            else if (!powered && running) animManager.StopAnimation(ActiveAnimation);
        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);

            // Runs after the player inventory behavior has attached worn gear. The markers only exist when
            // gear was attached, and attaching gear always clones the shape first, so the shared asset
            // shape is never edited here.
            if (entityShape == null || !shapeIsCloned) return;
        }

        public override string PropertyName() => "springBootsVisuals";
    }
}
