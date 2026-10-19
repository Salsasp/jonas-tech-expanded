using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace JonasTechExpanded
{
    /// <summary>
    /// Multistep crafting bench. A blueprint placed on the bench selects the recipe, whose stages are filled one
    /// right-click at a time through vanilla's <see cref="RightClickConstruction"/> (the system behind waterwheels and
    /// boats). Each installed ingredient is shown in a socket on the bench top; once every stage is in, holding
    /// right-click with a hammer assembles the output while the parts slide together (<see cref="WorkbenchAssemblyRenderer"/>).
    /// </summary>
    /// <remarks>
    /// The socket slots hold the real consumed stacks, so breaking the bench drops everything put in so far.
    /// RightClickConstruction.GetDrops is not used: it returns nothing until two stages are complete.
    /// The bench is two blocks wide. This entity lives on the main block only; the vanilla Multiblock behavior
    /// fills the second block with a placeholder that forwards clicks to <see cref="BlockJonasWorkbench"/> with
    /// the selection moved onto the main block.
    /// </remarks>
    public class BlockEntityJonasWorkbench : BlockEntityDisplay
    {
        /// <summary>Item attribute on a blueprint holding its workbench recipe.</summary>
        public const string RecipeAttribute = "jonasWorkbench";

        private const int BlueprintSlot = 0;
        private const int MaxSockets = 8;

        private static readonly AssetLocation PlaceSound = new AssetLocation("game:sounds/effect/latch");
        private static readonly AssetLocation AssembleTickSound = new AssetLocation("game:sounds/effect/gears");
        private static readonly AssetLocation AssembleDoneSound = new AssetLocation("game:sounds/effect/anvilhit1");

        private readonly InventoryDisplayed inventory;
        private readonly RightClickConstruction rcc = new RightClickConstruction();

        /// <summary>Recipe stages with an implicit, ingredient-free stage 0 standing for "blueprint placed".</summary>
        private ConstructionStage[] stages;
        /// <summary>Index into the sockets of each stage's first ingredient.</summary>
        private int[] firstSocketOfStage;
        private JsonItemStack output;
        private float assemblySeconds;

        private SocketPlacement blueprintPlacement;
        private SocketPlacement[] socketPlacements;

        /// <summary>
        /// Whether a hold-to-assemble is in progress. Each side tracks its own interaction; the server's value is also
        /// synced so other players' clients can show the parts converging.
        /// </summary>
        private bool assembling;
        private float nextTickSoundAt;

        /// <summary>Client: socket items are drawn by the renderer, sliding to the middle, instead of in the chunk mesh.</summary>
        private bool converging;
        private WorkbenchAssemblyRenderer assemblyRenderer;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "jonasworkbench";
        public override string AttributeTransformCode => "onJonasWorkbenchTransform";

        public bool HasBlueprint => !inventory[BlueprintSlot].Empty;
        public bool HasRecipe => stages != null;
        public bool IsComplete => HasRecipe && rcc.CurrentCompletedStage >= stages.Length - 1;

        /// <summary>While converging only the blueprint (slot 0) goes into the chunk mesh; the renderer draws the rest.</summary>
        public override int DisplayedItems => converging ? 1 : base.DisplayedItems;

        public BlockEntityJonasWorkbench()
        {
            inventory = new InventoryDisplayed(this, 1 + MaxSockets, "jonasworkbench-0", null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            var placements = WorkbenchSocketMarkers.Get(api, Block);
            blueprintPlacement = placements.Blueprint;
            socketPlacements = placements.Sockets.Take(MaxSockets).ToArray();

            if (api is ICoreClientAPI capi) assemblyRenderer = new WorkbenchAssemblyRenderer(capi, this);

            bindRecipe();
        }

        /// <summary>The bench at a position, following a multiblock placeholder back to the main block.</summary>
        public static BlockEntityJonasWorkbench At(IWorldAccessor world, BlockPos pos)
        {
            if (world.BlockAccessor.GetBlock(pos) is BlockMultiblock placeholder) pos = pos.AddCopy(placeholder.OffsetInv);
            return world.BlockAccessor.GetBlockEntity(pos) as BlockEntityJonasWorkbench;
        }

        public static bool IsHammer(ItemSlot slot) => slot?.Itemstack?.Collectible.Tool == EnumTool.Hammer;

        /// <summary>
        /// Reads the recipe from the blueprint on the bench, or clears it when there is none. Doesn't touch progress.
        /// </summary>
        private void bindRecipe()
        {
            stages = null;
            output = null;

            var blueprint = inventory[BlueprintSlot].Itemstack;
            var recipe = blueprint?.ItemAttributes?[RecipeAttribute];
            if (recipe == null || !recipe.Exists) return;

            string domain = blueprint.Collectible.Code.Domain;
            string source = "Jonas workbench recipe on " + blueprint.Collectible.Code;

            var recipeStages = recipe["stages"].AsObject<ConstructionStage[]>(null, domain);
            output = recipe["output"].AsObject<JsonItemStack>(null, domain);
            if (recipeStages == null || recipeStages.Length == 0 || output == null || !output.Resolve(Api.World, source))
            {
                Api.World.Logger.Error("{0} needs a non-empty stages list and a resolvable output.", source);
                output = null;
                return;
            }

            var allStages = new ConstructionStage[recipeStages.Length + 1];
            allStages[0] = new ConstructionStage();
            recipeStages.CopyTo(allStages, 1);

            var firstSockets = new int[allStages.Length];
            int sockets = 0;
            for (int i = 0; i < allStages.Length; i++)
            {
                firstSockets[i] = sockets;
                sockets += allStages[i].RequireStacks?.Length ?? 0;
            }

            if (sockets > socketPlacements.Length)
            {
                Api.World.Logger.Error("{0} needs {1} sockets but {2} only has {3}.", source, sockets, Block.Code, socketPlacements.Length);
                output = null;
                return;
            }

            stages = allStages;
            firstSocketOfStage = firstSockets;
            assemblySeconds = recipe["assemblySeconds"].AsFloat(4f);

            rcc.LateInit(stages, Api, () => Pos.ToVec3d().Add(0.5, 1, 0.5), source);
        }

        public bool OnInteractStart(IPlayer byPlayer)
        {
            var hand = byPlayer.InventoryManager.ActiveHotbarSlot;
            assembling = false;

            if (!HasBlueprint)
            {
                // Not a blueprint: let the held item do its normal thing, e.g. place a block against the bench.
                if (hand.Empty || hand.Itemstack.ItemAttributes?[RecipeAttribute].Exists != true) return false;

                if (Api.Side == EnumAppSide.Server)
                {
                    hand.TryPutInto(Api.World, inventory[BlueprintSlot], 1);
                    hand.MarkDirty();
                    resetProgress();
                    bindRecipe();
                    MarkDirty(true);
                }
                playSound(PlaceSound, byPlayer);
                return true;
            }

            // Shift, not ctrl: creative + ctrl is vanilla RightClickConstruction's free install.
            if (byPlayer.Entity.Controls.ShiftKey)
            {
                if (Api.Side == EnumAppSide.Server) takeLast(byPlayer);
                playSound(PlaceSound, byPlayer);
                return true;
            }

            if (!HasRecipe) return true;

            if (IsComplete)
            {
                if (!IsHammer(hand))
                {
                    (Api as ICoreClientAPI)?.TriggerIngameError(this, "needhammer", Lang.Get("jonastechexpanded:workbench-needhammer"));
                    return true;
                }

                assembling = true;
                nextTickSoundAt = 0;
                if (Api.Side == EnumAppSide.Server) MarkDirty();
                else startConverging();
                return true;
            }

            int stage = rcc.CurrentCompletedStage + 1;
            if (rcc.OnInteract(byPlayer.Entity, hand) && Api.Side == EnumAppSide.Server)
            {
                fillSockets(stage);
                MarkDirty(true);
            }

            return true;
        }

        public bool OnInteractStep(float secondsUsed, IPlayer byPlayer)
        {
            // Switching away from the hammer stops it, short of the time, so nothing is made.
            if (!assembling || !IsComplete || !IsHammer(byPlayer.InventoryManager.ActiveHotbarSlot)) return false;

            if (Api.Side == EnumAppSide.Client && secondsUsed >= nextTickSoundAt)
            {
                playSound(AssembleTickSound, byPlayer);
                nextTickSoundAt = secondsUsed + 1f;
            }

            return secondsUsed < assemblySeconds;
        }

        public void OnInteractStop(float secondsUsed, IPlayer byPlayer)
        {
            bool wasAssembling = assembling;
            assembling = false;
            bool done = wasAssembling && IsComplete && secondsUsed >= assemblySeconds - 0.1f;

            if (!done)
            {
                if (Api.Side == EnumAppSide.Client) stopConverging();
                else if (wasAssembling) MarkDirty();
                return;
            }

            // On the client the parts stay converged until the server's update empties the sockets.
            playSound(AssembleDoneSound, byPlayer);
            if (Api.Side == EnumAppSide.Server) completeAssembly(byPlayer);
        }

        public void OnInteractCancel()
        {
            bool wasAssembling = assembling;
            assembling = false;

            if (Api.Side == EnumAppSide.Client) stopConverging();
            else if (wasAssembling) MarkDirty();
        }

        /// <summary>Client: hands the socket items to the renderer and takes them out of the chunk mesh.</summary>
        private void startConverging()
        {
            if (converging || assemblyRenderer == null) return;

            var meshes = new MeshData[socketPlacements.Length];
            for (int i = 0; i < meshes.Length; i++)
            {
                var slot = inventory[1 + i];
                if (!slot.Empty) meshes[i] = getOrCreateMesh(slot, 1 + i);
            }

            assemblyRenderer.Start(meshes, socketPlacements, Block.Shape?.rotateY ?? 0, assemblySeconds);
            converging = true;
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }

        /// <summary>Client: puts the socket items back into the chunk mesh.</summary>
        private void stopConverging()
        {
            if (!converging) return;

            assemblyRenderer?.Stop();
            converging = false;
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }

        private void fillSockets(int stage)
        {
            var require = stages[stage].RequireStacks;
            if (require == null) return;

            for (int i = 0; i < require.Length; i++)
            {
                var stack = resolveInstalledStack(require[i]);
                if (stack == null) continue;

                int slot = 1 + firstSocketOfStage[stage] + i;
                inventory[slot].Itemstack = stack;
                inventory.MarkSlotDirty(slot);
            }
        }

        /// <summary>The stack an ingredient consumed, with any wildcard the construction stored filled in.</summary>
        private ItemStack resolveInstalledStack(ConstructionIngredient ingredient)
        {
            var ingred = ingredient.Clone();
            foreach (var wildcard in rcc.StoredWildCards) ingred.FillPlaceHolder(wildcard.Key, wildcard.Value);

            if (ingred.StoreWildCard != null && rcc.StoredWildCards.TryGetValue(ingred.StoreWildCard, out string value))
            {
                ingred.Code.Path = ingred.Code.Path.Replace("*", value);
            }

            if (!ingred.Resolve(Api.World, "Installed stack on " + Block.Code)) return null;

            var stack = ingred.ResolvedItemStack.Clone();
            stack.StackSize = ingred.Quantity;
            return stack;
        }

        private void completeAssembly(IPlayer byPlayer)
        {
            var stack = output.ResolvedItemstack.Clone();

            for (int slot = 1; slot < inventory.Count; slot++)
            {
                if (inventory[slot].Empty) continue;
                inventory[slot].Itemstack = null;
                inventory.MarkSlotDirty(slot);
            }
            resetProgress();
            giveOrDrop(byPlayer, stack);
            MarkDirty(true);
        }

        /// <summary>
        /// Undoes the last installed stage, giving back everything it put in the sockets. With no stage installed, takes
        /// the blueprint instead.
        /// </summary>
        private void takeLast(IPlayer byPlayer)
        {
            int stage = HasRecipe ? rcc.CurrentCompletedStage : 0;
            if (stage == 0)
            {
                takeBlueprint(byPlayer);
                return;
            }

            var require = stages[stage].RequireStacks ?? new ConstructionIngredient[0];
            for (int i = 0; i < require.Length; i++)
            {
                int slot = 1 + firstSocketOfStage[stage] + i;
                giveOrDrop(byPlayer, inventory[slot].TakeOutWhole());
                inventory.MarkSlotDirty(slot);

                // Lets the stage pick a different variant when it is installed again.
                if (require[i].StoreWildCard != null) rcc.StoredWildCards.Remove(require[i].StoreWildCard);
            }

            rcc.CurrentCompletedStage = stage - 1;
            MarkDirty(true);
        }

        private void takeBlueprint(IPlayer byPlayer)
        {
            giveOrDrop(byPlayer, inventory[BlueprintSlot].TakeOutWhole());
            bindRecipe();
            MarkDirty(true);
        }

        private void giveOrDrop(IPlayer byPlayer, ItemStack stack)
        {
            if (stack == null || byPlayer.InventoryManager.TryGiveItemstack(stack, true)) return;

            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
        }

        private void resetProgress()
        {
            rcc.CurrentCompletedStage = 0;
            rcc.StoredWildCards.Clear();
        }

        /// <summary>
        /// Plays a sound for everyone nearby. Called on both sides: the server skips the interacting player,
        /// whose client plays it locally without network delay.
        /// </summary>
        private void playSound(AssetLocation sound, IPlayer byPlayer)
        {
            Api.World.PlaySoundAt(sound, Pos, 0.5, byPlayer);
        }

        public WorldInteraction[] GetInteractionHelp(IPlayer forPlayer)
        {
            if (!HasBlueprint)
            {
                return new[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "jonastechexpanded:workbench-placeblueprint",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = getBlueprintStacks()
                    }
                };
            }

            var takeLast = new WorldInteraction
            {
                ActionLangCode = HasRecipe && rcc.CurrentCompletedStage > 0 ? "jonastechexpanded:workbench-takelast" : "jonastechexpanded:workbench-takeblueprint",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "shift"
            };

            if (!HasRecipe) return new[] { takeLast };

            if (IsComplete)
            {
                return new[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "jonastechexpanded:workbench-assemble",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = getHammerStacks()
                    },
                    takeLast
                };
            }

            var help = rcc.GetInteractionHelp(Api.World, forPlayer) ?? new WorldInteraction[0];
            return help.Append(takeLast);
        }

        private ItemStack[] getBlueprintStacks()
        {
            return ObjectCacheUtil.GetOrCreate(Api, "jonasWorkbenchBlueprintStacks", () =>
                Api.World.Collectibles
                    .Where(obj => obj.Attributes?[RecipeAttribute].Exists == true)
                    .Select(obj => new ItemStack(obj))
                    .ToArray());
        }

        private ItemStack[] getHammerStacks()
        {
            return ObjectCacheUtil.GetOrCreate(Api, "jonasWorkbenchHammerStacks", () =>
                Api.World.Collectibles
                    .Where(obj => obj.Tool == EnumTool.Hammer)
                    .Select(obj => new ItemStack(obj))
                    .ToArray());
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            if (!HasBlueprint)
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:workbench-noblueprint"));
                return;
            }

            dsc.AppendLine(Lang.Get("jonastechexpanded:workbench-blueprint", inventory[BlueprintSlot].Itemstack.GetName()));
            if (!HasRecipe) return;

            if (IsComplete)
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:workbench-ready", output.ResolvedItemstack.GetName()));
            }
            else
            {
                dsc.AppendLine(Lang.Get("jonastechexpanded:workbench-progress", rcc.CurrentCompletedStage, stages.Length - 1));
            }
        }

        protected override float[][] genTransformationMatrices()
        {
            var matrices = new float[inventory.Count][];
            float blockRotY = Block.Shape?.rotateY ?? 0;

            for (int i = 0; i < matrices.Length; i++)
            {
                var placement = i == BlueprintSlot ? blueprintPlacement : socketPlacements.ElementAtOrDefault(i - 1);
                matrices[i] = PlacementMatrix(blockRotY, placement ?? new SocketPlacement()).Values;
            }

            return matrices;
        }

        protected override MeshData getOrCreateMesh(ItemSlot slot, int index)
        {
            var cached = getMesh(slot);
            if (cached != null) return cached;

            // Vanilla already applied the item's own onJonasWorkbenchTransform, if it has one.
            var mesh = base.getOrCreateMesh(slot, index);
            var stack = slot.Itemstack;
            if (stack.Collectible.Attributes?[AttributeTransformCode].Exists == true) return mesh;

            var transform = FindItemTransform(Block.Attributes?["workbench"]["itemTransforms"], stack.Collectible.Code, Block.Code.Domain);
            if (transform == null) return mesh;

            // displayedShape meshes are shared through ObjectCacheUtil, so transform a copy.
            mesh = mesh.Clone();
            transform.EnsureDefaultValues();
            mesh.ModelTransform(transform);
            MeshCache[getMeshCacheKey(slot)] = mesh;
            return mesh;
        }

        /// <summary>
        /// The bench's own transform for an item, from block JSON <c>attributes.workbench.itemTransforms</c>: an object
        /// keyed by item code (wildcards allowed, unprefixed codes are in <paramref name="defaultDomain"/>, the bench's own
        /// domain, as in blueprint recipes). The first matching key in file
        /// order wins, so list specific codes before wildcards. Null when nothing matches.
        /// </summary>
        public static ModelTransform FindItemTransform(JsonObject itemTransforms, AssetLocation code, string defaultDomain)
        {
            if (itemTransforms?.Token is not JObject byCode) return null;

            foreach (var entry in byCode)
            {
                if (WildcardUtil.Match(AssetLocation.Create(entry.Key, defaultDomain), code))
                {
                    return new JsonObject(entry.Value).AsObject<ModelTransform>();
                }
            }
            return null;
        }

        /// <summary>
        /// Transform from a displayed stack's mesh to the main block's space. Rotates about the main block's
        /// center exactly like the block shape's rotateY, so a placement past x = 16 lands on the second block.
        /// Multiplies onto <paramref name="into"/> when given (the renderer passes its block-to-camera matrix).
        /// </summary>
        public static Matrixf PlacementMatrix(float blockRotY, SocketPlacement placement, Matrixf into = null)
        {
            return (into ?? new Matrixf())
                .Translate(0.5f, 0, 0.5f)
                .RotateYDeg(blockRotY)
                .Translate(placement.X / 16f - 0.5f, placement.Y / 16f, placement.Z / 16f - 0.5f)
                .RotateYDeg(placement.RotateY)
                .Scale(placement.Scale, placement.Scale, placement.Scale)
                .Translate(-0.5f, 0, -0.5f);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            assemblyRenderer?.Dispose();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            assemblyRenderer?.Dispose();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            rcc.ToTreeAttributes(tree);
            tree.SetBool("assembling", assembling);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            rcc.FromTreeAttributes(tree);

            // The blueprint may have changed. Before Initialize there is no Api; Initialize binds then.
            if (Api != null) bindRecipe();

            // Another player's assembly, as the server sees it. The local player's own assembly is driven by its
            // interaction instead, so a stale update can't cut it short.
            if (Api?.Side == EnumAppSide.Client)
            {
                bool serverAssembling = tree.GetBool("assembling");
                if (serverAssembling && IsComplete) startConverging();
                else if (!serverAssembling && !assembling) stopConverging();
            }

            RedrawAfterReceivingTreeAttributes(worldForResolving);
        }

        /// <summary>
        /// Where one displayed stack sits on the bench top, in voxels (1/16 block) of the unrotated (north) bench,
        /// measured from the main block. The bench is two blocks wide, so X runs 0..32.
        /// Read from marker cubes in the block's shape by <see cref="WorkbenchSocketMarkers"/>.
        /// </summary>
        public class SocketPlacement
        {
            public float X = 8;
            public float Y = 16;
            public float Z = 8;
            public float RotateY;
            public float Scale = 0.5f;
        }
    }
}
