using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JonasTechExpanded
{
    /// <summary>
    /// Speeds up item transitions (perishing, drying, curing, ripening, ...) in containers inside a field by adding
    /// a handler to their inventory's <see cref="InventoryBase.OnAcquireTransitionSpeed"/>, whose results the engine
    /// multiplies into the transition rate.
    ///
    /// Transitions are worked out lazily from the time since the stack was last updated, using whatever rate
    /// applies at that moment. So the handler returns a cached multiplier, and before that changes the stacks are
    /// brought up to date at the old rate. Otherwise entering or leaving a field would re-rate time already spent.
    /// Runs on both sides, so client-side perish readouts agree with the server.
    /// </summary>
    public class AccelFieldTransitions
    {
        private class Tracked
        {
            public BlockPos Pos;
            public float Applied = 1;
            public CustomGetTransitionSpeedMulDelegate Handler;
        }

        private readonly Dictionary<InventoryBase, Tracked> tracked = new Dictionary<InventoryBase, Tracked>();

        public int Count => tracked.Count;

        /// <summary>Starts tracking the block entity's inventory, if it has one. Takes effect on the next <see cref="Update"/>.</summary>
        public void Track(BlockEntity be)
        {
            if (be is not IBlockEntityContainer container || container.Inventory is not InventoryBase inv) return;
            if (tracked.ContainsKey(inv)) return;

            var entry = new Tracked { Pos = be.Pos.Copy() };
            entry.Handler = (type, stack, mul) => entry.Applied;
            inv.OnAcquireTransitionSpeed += entry.Handler;
            tracked[inv] = entry;
        }

        /// <summary>Re-reads every tracked inventory's multiplier, and stops tracking those no longer accelerated.</summary>
        public void Update(IWorldAccessor world, System.Func<BlockPos, float> getMul)
        {
            foreach (var (inv, entry) in tracked.ToList())
            {
                var be = world.BlockAccessor.GetBlockEntity(entry.Pos);
                bool alive = (be as IBlockEntityContainer)?.Inventory == inv;
                float mul = alive ? getMul(entry.Pos) : 1;

                if (mul != entry.Applied)
                {
                    if (alive) flush(world, be, inv);
                    entry.Applied = mul;
                }

                if (mul == 1)
                {
                    inv.OnAcquireTransitionSpeed -= entry.Handler;
                    tracked.Remove(inv);
                }
            }
        }

        public void Clear()
        {
            foreach (var (inv, entry) in tracked) inv.OnAcquireTransitionSpeed -= entry.Handler;
            tracked.Clear();
        }

        /// <summary>Brings every stack's transitions up to now at the multiplier still in effect.</summary>
        private static void flush(IWorldAccessor world, BlockEntity be, InventoryBase inv)
        {
            bool changed = false;
            foreach (var slot in inv)
            {
                if (slot.Itemstack == null) continue;

                var codeBefore = slot.Itemstack.Collectible.Code;
                slot.Itemstack.Collectible.UpdateAndGetTransitionStates(world, slot);
                if (slot.Itemstack?.Collectible.Code != codeBefore) changed = true;
            }

            if (changed && world.Side == EnumAppSide.Server) be.MarkDirty(true);
        }
    }
}
