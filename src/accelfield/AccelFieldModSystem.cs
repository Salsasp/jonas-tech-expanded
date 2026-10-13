using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace JonasTechExpanded
{
    /// <summary>
    /// Registers the temporal acceleration field, keeps track of loaded fields, and on one clock: speeds up what's
    /// inside them, burns their fuel, and (client) draws field outlines.
    ///
    /// Where fields overlap, a block gets the fastest field's speed; speeds don't stack.
    /// </summary>
    public class AccelFieldModSystem : ModSystem
    {
        private const string HarmonyId = "jonastechexpanded.accelfield";

        /// <summary>Real-time milliseconds between acceleration passes.</summary>
        public const int TickIntervalMs = 1000;

        /// <summary>How long ctrl + right-click shows a field's outline.</summary>
        public const int PreviewMs = 5000;

        /// <summary>First highlight slot id used for field outlines; one slot per shown field.</summary>
        private const int HighlightSlotBase = 4810;

        private ICoreAPI api;
        private ICoreClientAPI capi;
        private Harmony harmony;

        private readonly List<BlockEntityTemporalAccelField> fields = new List<BlockEntityTemporalAccelField>();
        private readonly AccelFieldTransitions transitions = new AccelFieldTransitions();
        private double lastTickTotalHours = -1;

        // Client: outlines currently drawn, by field position, and ctrl-click previews by expiry time.
        private readonly Dictionary<BlockPos, (int slot, int size)> shownOutlines = new Dictionary<BlockPos, (int, int)>();
        private readonly Dictionary<BlockPos, long> previewUntilMs = new Dictionary<BlockPos, long>();

        public IReadOnlyList<BlockEntityTemporalAccelField> Fields => fields;
        public AccelFieldTransitions Transitions => transitions;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            api.RegisterBlockClass("BlockTemporalAccelField", typeof(BlockTemporalAccelField));
            api.RegisterBlockEntityClass("TemporalAccelField", typeof(BlockEntityTemporalAccelField));

            // Patches are process-wide; in single player the client and server share one process, so only the
            // first side to start applies (and later removes) them.
            if (!Harmony.HasAnyPatches(HarmonyId))
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(typeof(AccelFieldModSystem).Assembly);
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            api.Event.RegisterGameTickListener(onTick, TickIntervalMs, 300);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;
            api.Event.RegisterGameTickListener(onTick, TickIntervalMs, 300);
            api.Event.RegisterGameTickListener(_ => updateOutlines(), 250);
        }

        public void Register(BlockEntityTemporalAccelField field)
        {
            if (!fields.Contains(field)) fields.Add(field);
        }

        public void Unregister(BlockEntityTemporalAccelField field)
        {
            fields.Remove(field);
        }

        /// <summary>The time multiplier at a position: the fastest active field covering it, 1 if none.</summary>
        public float GetMul(BlockPos pos)
        {
            float mul = 1;
            foreach (var field in fields)
            {
                if (field.IsActive && field.Contains(pos)) mul = Math.Max(mul, field.Speed);
            }
            return mul;
        }

        private void onTick(float dt)
        {
            double now = api.World.Calendar.TotalHours;
            // Don't count the world's entire age as elapsed time on the first tick after startup.
            if (lastTickTotalHours < 0) lastTickTotalHours = now;
            double hours = Math.Max(0, now - lastTickTotalHours);
            lastTickTotalHours = now;

            Tick(hours);
        }

        /// <summary>
        /// One acceleration pass covering <paramref name="hours"/> in-game hours: extra progress for calendar-timed
        /// machines (server), transition multipliers (both sides), then fuel burn (server). Public for tests.
        /// </summary>
        public void Tick(double hours)
        {
            bool server = api.Side == EnumAppSide.Server;
            var visited = new HashSet<BlockPos>();

            foreach (var field in fields.Where(f => f.IsActive).ToList())
            {
                foreach (var pos in field.Positions())
                {
                    if (!visited.Add(pos)) continue;

                    var be = api.World.BlockAccessor.GetBlockEntity(pos);
                    if (be == null || be is BlockEntityTemporalAccelField) continue;

                    if (server) AccelFieldAdapters.ApplyExtraHours(be, hours * (GetMul(pos) - 1));
                    transitions.Track(be);
                }
            }

            transitions.Update(api.World, GetMul);

            if (!server) return;
            foreach (var field in fields.ToList()) field.ConsumeFuel(hours);
        }

        #region Client outlines

        /// <summary>Client: shows the field's outline for <see cref="PreviewMs"/>.</summary>
        public void ShowPreview(BlockEntityTemporalAccelField field)
        {
            if (capi == null) return;
            previewUntilMs[field.Pos.Copy()] = capi.ElapsedMilliseconds + PreviewMs;
            updateOutlines();
        }

        private void updateOutlines()
        {
            long now = capi.ElapsedMilliseconds;
            foreach (var expired in previewUntilMs.Where(p => p.Value <= now).Select(p => p.Key).ToList()) previewUntilMs.Remove(expired);

            var wanted = fields
                .Where(f => f.FieldVisible || previewUntilMs.ContainsKey(f.Pos))
                .ToDictionary(f => f.Pos, f => f);

            foreach (var pos in shownOutlines.Keys.Where(p => !wanted.ContainsKey(p)).ToList())
            {
                capi.World.HighlightBlocks(capi.World.Player, shownOutlines[pos].slot, new List<BlockPos>(), new List<int>());
                shownOutlines.Remove(pos);
            }

            foreach (var (pos, field) in wanted)
            {
                if (shownOutlines.TryGetValue(pos, out var shown) && shown.size == field.Size) continue;

                int slot = shown.slot != 0 ? shown.slot : freeSlot();
                var shell = shellPositions(field).ToList();
                // Translucent temporal teal. Highlight colors are true RGBA (see MultiblockStructure).
                int color = ColorUtil.ColorFromRgba(70, 200, 220, 40);
                capi.World.HighlightBlocks(capi.World.Player, slot, shell, Enumerable.Repeat(color, shell.Count).ToList());
                shownOutlines[pos] = (slot, field.Size);
            }
        }

        private int freeSlot()
        {
            var used = shownOutlines.Values.Select(v => v.slot).ToHashSet();
            int slot = HighlightSlotBase;
            while (used.Contains(slot)) slot++;
            return slot;
        }

        /// <summary>The outer layer of the field cube: enough to show its bounds without stacking translucent layers.</summary>
        private static IEnumerable<BlockPos> shellPositions(BlockEntityTemporalAccelField field)
        {
            int half = field.Size / 2;
            return field.Positions().Where(p =>
                Math.Abs(p.X - field.Pos.X) == half || Math.Abs(p.Y - field.Pos.Y) == half || Math.Abs(p.Z - field.Pos.Z) == half);
        }

        #endregion

        public override void Dispose()
        {
            transitions.Clear();
            fields.Clear();

            if (capi?.World?.Player != null)
            {
                foreach (var (slot, _) in shownOutlines.Values) capi.World.HighlightBlocks(capi.World.Player, slot, new List<BlockPos>(), new List<int>());
            }
            shownOutlines.Clear();

            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
        }
    }
}
