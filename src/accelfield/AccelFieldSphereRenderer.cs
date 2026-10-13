using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace JonasTechExpanded
{
    /// <summary>
    /// Client: the cosmetic "Show field" effect. A translucent shimmering sphere around each running field whose
    /// visibility toggle is on, sized so its shell passes through the centers of the field's corner blocks so
    /// inside it. Purely visual; the ctrl + right-click range preview is the accurate one.
    ///
    /// Drawn at <see cref="EnumRenderStage.AfterBlit"/> like vanilla's rift, so the shader hides itself behind
    /// terrain by sampling the scene depth texture (shaders/accelfield.fsh).
    /// </summary>
    public class AccelFieldSphereRenderer : IRenderer
    {
        private const int Slices = 48;
        private const int Stacks = 32;

        /// <summary>Seconds a sphere takes to fade fully in or out.</summary>
        private const float FadeSeconds = 0.6f;

        private readonly ICoreClientAPI capi;
        private readonly AccelFieldModSystem system;
        private readonly MeshRef sphere;
        private readonly Matrixf modelView = new Matrixf();
        private readonly Dictionary<BlockEntityTemporalAccelField, float> fade = new Dictionary<BlockEntityTemporalAccelField, float>();
        private IShaderProgram prog;
        private float time;

        public double RenderOrder => 0.06;
        public int RenderRange => 64;

        public AccelFieldSphereRenderer(ICoreClientAPI capi, AccelFieldModSystem system)
        {
            this.capi = capi;
            this.system = system;
            sphere = capi.Render.UploadMesh(buildUnitSphere());

            capi.Event.ReloadShader += loadShader;
            loadShader();
            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "jonastechexpanded-accelfield");
        }

        private bool loadShader()
        {
            prog = capi.Shader.NewShaderProgram();
            prog.AssetDomain = "jonastechexpanded";
            prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
            capi.Shader.RegisterFileShaderProgram("accelfield", prog);
            return prog.Compile();
        }

        /// <summary>
        /// Radius in blocks: distance from the machine's center to the center of a corner block of the field, plus
        /// <see cref="AccelFieldTunables.SphereMargin"/> (default 0, so the shell cuts the corner blocks in half).
        /// </summary>
        public static float SphereRadius(BlockEntityTemporalAccelField field)
        {
            float cornerBlockCenter = (field.Size - 1) / 2f * MathF.Sqrt(3);
            return cornerBlockCenter + field.Tunables.SphereMargin;
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            time = (time + deltaTime) % 3600f;
            if (!updateFades(deltaTime) || prog == null) return;

            var camPos = capi.World.Player.Entity.CameraPos;

            // Everything later in the frame (GUI, the animated block renderer) inherits whatever GL state this leaves
            // behind, so put back exactly what was there rather than assuming a default. Forcing face culling on
            // here once made most of the GUI and the running machine itself invisible.
            bool cullWasOn = GL.IsEnabled(EnableCap.CullFace);
            GL.GetBoolean(GetPName.DepthWritemask, out bool depthWriteWasOn);

            // Blending is left as the stage set it, like vanilla's rift renderer does.
            capi.Render.GLDepthMask(false);
            // Both faces: from outside the far side shows through, from inside the field you still see it.
            capi.Render.GlDisableCullFace();

            prog.Use();
            prog.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
            // Texture unit 1, as the rift binds its depth texture; unit 0 is what most later draws assume is theirs.
            prog.BindTexture2D("depthTex", capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId, 1);
            prog.Uniform("invFrameSize", 1f / capi.Render.FrameWidth, 1f / capi.Render.FrameHeight);
            prog.Uniform("time", time);

            foreach (var (field, level) in fade)
            {
                if (level <= 0) continue;

                float radius = SphereRadius(field);
                modelView.Set(capi.Render.CameraMatrixOriginf)
                    .Translate((float)(field.Pos.X + 0.5 - camPos.X), (float)(field.Pos.InternalY + 0.5 - camPos.Y), (float)(field.Pos.Z + 0.5 - camPos.Z))
                    .Scale(radius, radius, radius);

                prog.UniformMatrix("modelViewMatrix", modelView.Values);
                prog.Uniform("intensity", level);
                prog.Uniform("radius", radius);
                prog.Uniform("fieldColor", field.Tunables.SphereColor);
                capi.Render.RenderMesh(sphere);
            }

            prog.Stop();

            if (cullWasOn) capi.Render.GlEnableCullFace();
            capi.Render.GLDepthMask(depthWriteWasOn);
        }

        /// <summary>Moves each field's fade towards shown/hidden. Returns whether anything needs drawing.</summary>
        private bool updateFades(float dt)
        {
            float step = dt / FadeSeconds;
            bool any = false;

            foreach (var field in system.Fields)
            {
                if (!fade.ContainsKey(field) && shouldShow(field)) fade[field] = 0;
            }

            foreach (var field in new List<BlockEntityTemporalAccelField>(fade.Keys))
            {
                bool loaded = system.IsRegistered(field);
                float target = loaded && shouldShow(field) ? 1 : 0;
                float level = GameMath.Clamp(fade[field] + (target > fade[field] ? step : -step), 0, 1);

                // Unloaded or removed fields vanish at once; there's nothing left to fade around.
                if (!loaded || (level <= 0 && target <= 0)) { fade.Remove(field); continue; }

                fade[field] = level;
                any = true;
            }

            return any;
        }

        private static bool shouldShow(BlockEntityTemporalAccelField field) => field.FieldVisible && field.IsActive;

        /// <summary>Unit sphere, position doubling as the normal; uv is longitude/latitude.</summary>
        private static MeshData buildUnitSphere()
        {
            var mesh = new MeshData((Slices + 1) * (Stacks + 1), Slices * Stacks * 6, false, true, false, false);

            for (int stack = 0; stack <= Stacks; stack++)
            {
                float v = (float)stack / Stacks;
                float phi = v * MathF.PI;
                for (int slice = 0; slice <= Slices; slice++)
                {
                    float u = (float)slice / Slices;
                    float theta = u * 2 * MathF.PI;
                    mesh.AddVertex(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta), u, v);
                }
            }

            for (int stack = 0; stack < Stacks; stack++)
            {
                for (int slice = 0; slice < Slices; slice++)
                {
                    int a = stack * (Slices + 1) + slice;
                    int b = a + Slices + 1;
                    mesh.AddIndex(a); mesh.AddIndex(b); mesh.AddIndex(a + 1);
                    mesh.AddIndex(a + 1); mesh.AddIndex(b); mesh.AddIndex(b + 1);
                }
            }

            return mesh;
        }

        public void Dispose()
        {
            capi.Event.ReloadShader -= loadShader;
            sphere?.Dispose();
            prog?.Dispose();
        }
    }
}
