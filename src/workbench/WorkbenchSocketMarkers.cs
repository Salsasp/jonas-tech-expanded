using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace JonasTechExpanded
{
    /// <summary>
    /// Reads the workbench's socket placements from marker cubes in its shape, so sockets are positioned by
    /// dragging cubes in VS Model Creator instead of typing coordinates. Markers are top-level elements named
    /// <see cref="BlueprintMarker"/> and Socket1, Socket2, ... (filled in that order, stopping at the first gap).
    /// The block JSON lists them in its shape's ignoreElements so they never render.
    /// </summary>
    /// <remarks>
    /// A marker's socket point is the center of its bottom face: a socket's item is centered on it and sits on it.
    /// The marker's own Y rotation turns the item; X/Z rotations are ignored. Markers must be top-level because
    /// ignoreElements is only known to match top-level names.
    /// </remarks>
    public static class WorkbenchSocketMarkers
    {
        public const string BlueprintMarker = "SocketBlueprint";
        public const string SocketMarkerPrefix = "Socket";

        /// <summary>Raises socket items off the surface their marker sits on, so they don't z-fight with it.</summary>
        private const float SurfaceLift = 0.02f;

        public class Placements
        {
            public BlockEntityJonasWorkbench.SocketPlacement Blueprint;
            public BlockEntityJonasWorkbench.SocketPlacement[] Sockets;
        }

        /// <summary>The block's placements, read from its shape once per block and cached.</summary>
        public static Placements Get(ICoreAPI api, Block block)
        {
            return ObjectCacheUtil.GetOrCreate(api, "jonasWorkbenchSockets-" + block.Code, () =>
            {
                var attr = block.Attributes?["workbench"];
                float blueprintScale = attr?["blueprintScale"].AsFloat(0.8f) ?? 0.8f;
                float socketScale = attr?["socketScale"].AsFloat(0.4f) ?? 0.4f;

                var shapeLoc = block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                var shape = Shape.TryGet(api, shapeLoc);
                if (shape == null)
                {
                    api.World.Logger.Error("{0}: could not load shape {1} to read its socket markers.", block.Code, shapeLoc);
                    return new Placements { Blueprint = new BlockEntityJonasWorkbench.SocketPlacement(), Sockets = new BlockEntityJonasWorkbench.SocketPlacement[0] };
                }

                var placements = Read(shape, blueprintScale, socketScale);
                if (placements.Blueprint == null)
                {
                    api.World.Logger.Error("{0}: shape {1} has no {2} marker.", block.Code, shapeLoc, BlueprintMarker);
                    placements.Blueprint = new BlockEntityJonasWorkbench.SocketPlacement();
                }
                return placements;
            });
        }

        /// <summary>Reads every marker of a shape. Blueprint is null if the shape has no blueprint marker.</summary>
        public static Placements Read(Shape shape, float blueprintScale, float socketScale)
        {
            var byName = new Dictionary<string, ShapeElement>();
            foreach (var element in shape.Elements ?? new ShapeElement[0])
            {
                if (element.Name != null) byName[element.Name] = element;
            }

            var sockets = new List<BlockEntityJonasWorkbench.SocketPlacement>();
            while (byName.TryGetValue(SocketMarkerPrefix + (sockets.Count + 1), out var marker))
            {
                sockets.Add(FromMarker(marker, socketScale, SurfaceLift));
            }

            return new Placements
            {
                Blueprint = byName.TryGetValue(BlueprintMarker, out var blueprint) ? FromMarker(blueprint, blueprintScale, 0) : null,
                Sockets = sockets.ToArray()
            };
        }

        /// <summary>The placement for a top-level marker: its bottom face's center and its yaw, in model voxels.</summary>
        public static BlockEntityJonasWorkbench.SocketPlacement FromMarker(ShapeElement marker, float scale, float lift)
        {
            // In block units, from the marker's local space (origin at its "from" corner) to the model's.
            float[] local = marker.GetLocalTransformMatrix(0);

            float halfX = (float)(marker.To[0] - marker.From[0]) / 32f;
            float halfZ = (float)(marker.To[2] - marker.From[2]) / 32f;
            float[] point = Mat4f.MulWithVec4(local, new float[] { halfX, 0, halfZ, 1 });
            float[] xAxis = Mat4f.MulWithVec4(local, new float[] { 1, 0, 0, 0 });

            return new BlockEntityJonasWorkbench.SocketPlacement
            {
                X = point[0] * 16f,
                Y = point[1] * 16f + lift,
                Z = point[2] * 16f,
                // Mat4f.RotateY turns +X towards -Z, so this is the yaw that RotateYDeg reproduces.
                RotateY = (float)Math.Round(Math.Atan2(-xAxis[2], xAxis[0]) * GameMath.RAD2DEG, 3),
                Scale = scale
            };
        }
    }
}
