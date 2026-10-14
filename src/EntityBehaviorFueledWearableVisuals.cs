using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace JonasTechExpanded
{
    /// <summary>
    /// Client side half of a fueled wearable's look. The server publishes whether the wearable is powered as a
    /// watched attribute; this rebuilds the worn shape when that flips, so the item can add or drop its
    /// powered-only parts, and keeps the wearable's active animation running only while powered.
    /// </summary>
    public abstract class EntityBehaviorFueledWearableVisuals : EntityBehavior
    {
        private readonly string poweredAttribute;
        private readonly string activeAnimation;
        private bool powered;

        protected EntityBehaviorFueledWearableVisuals(Entity entity, string poweredAttribute, string activeAnimation) : base(entity)
        {
            this.poweredAttribute = poweredAttribute;
            this.activeAnimation = activeAnimation;
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            powered = entity.WatchedAttributes.GetBool(poweredAttribute);
            entity.WatchedAttributes.RegisterModifiedListener(poweredAttribute, onPoweredChanged);
        }

        private void onPoweredChanged()
        {
            bool nowPowered = entity.WatchedAttributes.GetBool(poweredAttribute);
            if (nowPowered == powered) return;

            powered = nowPowered;
            entity.MarkShapeModified();
        }

        public override void OnGameTick(float deltaTime)
        {
            // Polled rather than set on change, because the animator doesn't exist yet during Initialize.
            var animManager = entity.AnimManager;
            if (animManager == null) return;

            bool running = animManager.IsAnimationActive(activeAnimation);
            if (powered && !running) animManager.StartAnimation(activeAnimation);
            else if (!powered && running) animManager.StopAnimation(activeAnimation);
        }
    }
}
