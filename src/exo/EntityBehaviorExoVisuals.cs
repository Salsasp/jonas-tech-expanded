using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace JonasTechExpanded
{
    /// <summary>
    /// Client side half of the exoskeleton's look. The server publishes whether the player's exoskeleton
    /// is powered as a watched attribute; this rebuilds the worn shape when that flips, so
    /// ItemExoskeletonChest can add or drop the powered-only parts, and keeps the exoactive animation
    /// running only while powered. While the exoskeleton is worn it also moves the hands' item anchors
    /// onto the exoskeleton's grippers, so held items sit at the end of the frame's arms.
    /// </summary>
    public class EntityBehaviorExoVisuals : EntityBehavior
    {
        public const string PoweredAttribute = "jonastechexpanded:exoPowered";
        public const string ActiveAnimation = "exoactive";

        /// <summary>Vanilla seraph item anchor element, mapped to the exoskeleton marker it moves onto.</summary>
        private static readonly (string anchor, string marker)[] ItemAnchors =
        {
            ("ItemAnchor", "HeldItemAnchorR"),
            ("ItemAnchorL", "HeldItemAnchorL")
        };

        private bool powered;

        public EntityBehaviorExoVisuals(Entity entity) : base(entity) { }

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

            foreach (var (anchorName, markerName) in ItemAnchors)
            {
                moveAnchorToMarker(entityShape, anchorName, markerName);
            }
        }

        /// <summary>
        /// Moves the anchor, keeping its size and pivot, so that its origin corner lands on the marker's.
        /// The anchor keeps its own rotation, and the vanilla animations that swing it still apply.
        /// </summary>
        private static void moveAnchorToMarker(Shape shape, string anchorName, string markerName)
        {
            var anchor = shape.GetElementByName(anchorName);
            var bone = anchor?.ParentElement;
            if (bone == null) return;

            // Worn shape elements get the gear's texture prefix added to their names.
            var marker = findElementByNameSuffix(shape.Elements, markerName);
            if (marker == null) return;

            float[] toBone = Mat4f.Create();
            var el = marker;
            for (; el != null && el != bone; el = el.ParentElement)
            {
                toBone = Mat4f.Mul(Mat4f.Create(), el.GetLocalTransformMatrix(0), toBone);
            }
            if (el != bone) return;

            float[] pos = Mat4f.MulWithVec4(toBone, 0, 0, 0, 1);
            double dx = pos[0] * 16 - anchor.From[0];
            double dy = pos[1] * 16 - anchor.From[1];
            double dz = pos[2] * 16 - anchor.From[2];

            shift(anchor.From, dx, dy, dz);
            shift(anchor.To, dx, dy, dz);
            if (anchor.RotationOrigin != null) shift(anchor.RotationOrigin, dx, dy, dz);
        }

        private static void shift(double[] vec, double dx, double dy, double dz)
        {
            vec[0] += dx;
            vec[1] += dy;
            vec[2] += dz;
        }

        private static ShapeElement findElementByNameSuffix(ShapeElement[] elements, string suffix)
        {
            if (elements == null) return null;

            foreach (var element in elements)
            {
                if (element.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return element;

                var found = findElementByNameSuffix(element.Children, suffix);
                if (found != null) return found;
            }

            return null;
        }

        public override string PropertyName() => "exoVisuals";
    }
}
