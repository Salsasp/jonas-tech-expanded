using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace JonasTechExpanded
{
    /// <summary>
    /// Client renderer for a Jonas workbench's hold-to-assemble: draws the socket items sliding from their sockets
    /// to the middle of all the sockets over the assembly time. While it runs, the block entity leaves those items
    /// out of the chunk mesh.
    /// </summary>
    public class WorkbenchAssemblyRenderer : IRenderer, IDisposable
    {
        private readonly ICoreClientAPI capi;
        private readonly BlockEntityJonasWorkbench bench;
        private readonly Matrixf modelMat = new Matrixf();
        private readonly BlockEntityJonasWorkbench.SocketPlacement current = new BlockEntityJonasWorkbench.SocketPlacement();

        private MultiTextureMeshRef[] meshRefs;
        private BlockEntityJonasWorkbench.SocketPlacement[] placements;
        private Vec3f center;
        private float blockRotY;
        private float seconds;
        private long startMs;

        public double RenderOrder => 0.5;
        public int RenderRange => 24;

        public WorkbenchAssemblyRenderer(ICoreClientAPI capi, BlockEntityJonasWorkbench bench)
        {
            this.capi = capi;
            this.bench = bench;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "jonasworkbenchassembly");
        }

        /// <summary>
        /// Starts drawing. <paramref name="meshes"/> lines up with <paramref name="placements"/>; null entries are
        /// empty sockets. The meeting point is the middle of every socket, filled or not, so it doesn't depend on the recipe.
        /// </summary>
        public void Start(MeshData[] meshes, BlockEntityJonasWorkbench.SocketPlacement[] placements, float blockRotY, float seconds)
        {
            Stop();

            meshRefs = new MultiTextureMeshRef[meshes.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] != null) meshRefs[i] = capi.Render.UploadMultiTextureMesh(meshes[i]);
            }

            this.placements = placements;
            this.blockRotY = blockRotY;
            this.seconds = Math.Max(seconds, 0.1f);
            startMs = capi.InWorldEllapsedMilliseconds;

            center = new Vec3f();
            foreach (var p in placements) center.Add(p.X, p.Y, p.Z);
            center.Mul(1f / Math.Max(placements.Length, 1));
        }

        public void Stop()
        {
            if (meshRefs == null) return;

            foreach (var meshRef in meshRefs) meshRef?.Dispose();
            meshRefs = null;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (meshRefs == null) return;

            float t = GameMath.Clamp((capi.InWorldEllapsedMilliseconds - startMs) / 1000f / seconds, 0, 1);
            t = t * t * (3 - 2 * t);

            var rpi = capi.Render;
            var pos = bench.Pos;
            var camPos = capi.World.Player.Entity.CameraPos;

            rpi.GlDisableCullFace();
            rpi.GlToggleBlend(true);

            var prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.ViewMatrix = rpi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;

            for (int i = 0; i < meshRefs.Length; i++)
            {
                if (meshRefs[i] == null) continue;

                var from = placements[i];
                current.X = GameMath.Lerp(from.X, center.X, t);
                current.Y = GameMath.Lerp(from.Y, center.Y, t);
                current.Z = GameMath.Lerp(from.Z, center.Z, t);
                current.RotateY = from.RotateY;
                current.Scale = from.Scale;

                modelMat.Identity().Translate(pos.X - camPos.X, pos.Y - camPos.Y, pos.Z - camPos.Z);
                BlockEntityJonasWorkbench.PlacementMatrix(blockRotY, current, modelMat);
                prog.ModelMatrix = modelMat.Values;

                rpi.RenderMultiTextureMesh(meshRefs[i], "tex");
            }

            prog.Stop();
            rpi.GlEnableCullFace();
        }

        public void Dispose()
        {
            Stop();
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }
    }
}
