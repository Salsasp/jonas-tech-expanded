using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JonasTechExpanded
{
    /// <summary>
    /// The blueprint item. Shows <c>attributes.rolledShape</c> (a tied-up roll) while it sits in a vanilla scroll
    /// rack, and the normal flat sheet everywhere else, including on the workbench. Vanilla's <c>ItemRollable</c>
    /// does the same but rolls the item up in every display.
    /// </summary>
    public class ItemJonasBlueprint : Item, IContainedMeshSource
    {
        public const string RolledShapeAttribute = "rolledShape";

        public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            // Null makes every display fall back to the item's normal mesh. (Tool racks don't, but blueprints
            // aren't toolrackable.)
            if (api is not ICoreClientAPI capi || atBlockPos == null) return null;
            if (capi.World.BlockAccessor.GetBlockEntity(atBlockPos) is not BlockEntityScrollRack) return null;

            string rolledShape = Attributes?[RolledShapeAttribute].AsString();
            if (rolledShape == null) return null;

            var location = AssetLocation.Create(rolledShape, Code.Domain).WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            var shape = Vintagestory.API.Common.Shape.TryGet(capi, location);
            if (shape == null)
            {
                capi.Logger.Warning("Blueprint {0}: rolled shape {1} not found, showing the flat sheet", Code, location);
                return null;
            }

            // The shape's own textures (domain-prefixed, since a hand-loaded shape doesn't resolve them to ours),
            // with this variant's sheet in place of the shape's placeholder.
            var textures = new Dictionary<string, AssetLocation>(shape.Textures);
            foreach (var (code, texture) in Textures)
            {
                textures[code] = texture.Baked?.BakedName ?? texture.Base;
            }

            var texSource = new ContainedTextureSource(capi, targetAtlas, textures, "For rolled blueprint " + Code);
            capi.Tesselator.TesselateShape(new TesselationMetaData { TexSource = texSource }, shape, out MeshData mesh);
            return mesh;
        }

        // Display mesh caches are per display type, so the scroll rack's rolled mesh never meets another
        // display's flat one under this key.
        public string GetMeshCacheKey(ItemSlot slot) => slot.Itemstack.Collectible.Code.ToString();
    }
}
