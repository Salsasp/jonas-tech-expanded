using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace JonasTechExpanded
{
    /// <summary>
    /// Shared server loop for a <see cref="ItemFueledWearable"/> worn in one armor slot: drains worn fuel in
    /// in-game hours, publishes the powered flag the visuals behavior listens to, and applies the wearable's
    /// survival-only player bonus while powered. Subclasses register their classes in Start and say how the
    /// bonus is written.
    /// </summary>
    public abstract class FueledWearableModSystem<TItem> : ModSystem where TItem : ItemFueledWearable
    {
        private ICoreServerAPI sapi;
        private readonly HashSet<string> bonusApplied = new HashSet<string>();
        private double lastFuelCheckTotalHours = -1;

        /// <summary>Vanilla character inventory slot the wearable goes in; checked first, before searching the rest.</summary>
        protected abstract EnumCharacterDressType Slot { get; }

        /// <summary>Watched attribute published on the wearer while the wearable is powered.</summary>
        protected abstract string PoweredAttribute { get; }

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.RegisterGameTickListener(onServerTick1s, 1000, 200);
            api.Event.PlayerDisconnect += onPlayerDisconnect;
        }

        /// <summary>
        /// Returns the wearable the player is wearing if it is intact and has fuel left, otherwise null.
        /// Safe to call on either side for the owning player.
        /// </summary>
        public TItem GetPowered(IPlayer player)
        {
            return GetPoweredSlot(player)?.Itemstack.Collectible as TItem;
        }

        public ItemSlot GetPoweredSlot(IPlayer player)
        {
            var slot = GetWornSlot(player);
            if (slot == null) return null;

            var item = (TItem)slot.Itemstack.Collectible;
            return item.IsPowered(slot.Itemstack) ? slot : null;
        }

        /// <summary>The armor slot holding the wearable, powered or not, otherwise null.</summary>
        public ItemSlot GetWornSlot(IPlayer player)
        {
            var inv = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inv == null) return null;

            var slot = inv[(int)Slot];
            if (slot?.Itemstack?.Collectible is TItem) return slot;

            // Combat Overhaul swaps in its own character inventory with the vanilla armor slots disabled;
            // armor sits in extra layer/zone slots further along, so search the rest.
            foreach (var other in inv)
            {
                if (other?.Itemstack?.Collectible is TItem) return other;
            }
            return null;
        }

        /// <summary>
        /// Writes the player's bonus for <paramref name="powered"/>, or the vanilla values when it is null.
        /// Only called when that changes, and only for survival players (plus on disconnect).
        /// </summary>
        protected abstract void SetBonus(IServerPlayer player, TItem powered);

        private void onServerTick1s(float dt)
        {
            double totalHours = sapi.World.Calendar.TotalHours;

            // Don't count the world's entire age as elapsed time on the first tick after startup.
            if (lastFuelCheckTotalHours < 0) lastFuelCheckTotalHours = totalHours;

            double hoursPassed = totalHours - lastFuelCheckTotalHours;
            bool drainFuel = hoursPassed > 0.05;
            if (drainFuel) lastFuelCheckTotalHours = totalHours;

            foreach (var player in sapi.World.AllOnlinePlayers)
            {
                if (drainFuel) drainFuelOf(player, hoursPassed);

                if (player is IServerPlayer serverPlayer)
                {
                    updateBonus(serverPlayer);
                }

                updatePoweredFlag(player);
            }
        }

        private void updatePoweredFlag(IPlayer player)
        {
            var attrs = player.Entity?.WatchedAttributes;
            if (attrs == null) return;

            bool powered = GetPoweredSlot(player) != null;
            if (attrs.GetBool(PoweredAttribute) != powered)
            {
                attrs.SetBool(PoweredAttribute, powered);
            }
        }

        // Mostly replicates the functionality already in the game from the night vision mask
        private void drainFuelOf(IPlayer player, double hoursPassed)
        {
            var slot = GetWornSlot(player);
            if (slot == null) return;

            var item = (TItem)slot.Itemstack.Collectible;
            if (item.GetFuelHours(slot.Itemstack) <= 0) return;

            item.AddFuelHours(slot.Itemstack, -hoursPassed);
            slot.MarkDirty();
        }

        private void updateBonus(IServerPlayer player)
        {
            if (player.WorldData.CurrentGameMode != EnumGameMode.Survival)
            {
                // Creative and spectator set their own reach and speed, so drop the claim without writing a value.
                bonusApplied.Remove(player.PlayerUID);
                return;
            }

            var item = GetPowered(player);
            bool applied = bonusApplied.Contains(player.PlayerUID);

            if ((item != null) == applied) return;

            SetBonus(player, item);

            if (item != null) bonusApplied.Add(player.PlayerUID);
            else bonusApplied.Remove(player.PlayerUID);
        }

        private void onPlayerDisconnect(IServerPlayer player)
        {
            if (!bonusApplied.Remove(player.PlayerUID)) return;

            SetBonus(player, null);
        }

        public override void Dispose()
        {
            base.Dispose();

            bonusApplied.Clear();
            sapi = null;
        }
    }
}
