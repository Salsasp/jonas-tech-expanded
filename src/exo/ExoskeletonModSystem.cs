using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace JonasTechExpanded
{
    /// <summary>
    /// Registers the chest piece and its behaviors, drains worn fuel, publishes the powered flag,
    /// and applies the survival reach bonus.
    /// </summary>
    public class ExoskeletonModSystem : ModSystem
    {
        private ICoreServerAPI sapi;
        private readonly HashSet<string> reachApplied = new HashSet<string>();
        private double lastFuelCheckTotalHours = -1;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass("ItemExoskeletonChest", typeof(ItemExoskeletonChest));
            api.RegisterEntityBehaviorClass("exoStabilized", typeof(EntityBehaviorExoStabilized));
            api.RegisterEntityBehaviorClass("exoVisuals", typeof(EntityBehaviorExoVisuals));
            api.RegisterCollectibleBehaviorClass("exoMiningSpeed", typeof(CollectibleBehaviorExoMiningSpeed));
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            foreach (var collectible in api.World.Collectibles)
            {
                if (collectible.HasBehavior<CollectibleBehaviorExoMiningSpeed>()) continue;

                collectible.CollectibleBehaviors = collectible.CollectibleBehaviors.Append(new CollectibleBehaviorExoMiningSpeed(collectible));
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.RegisterGameTickListener(onServerTick1s, 1000, 200);
            api.Event.PlayerDisconnect += onPlayerDisconnect;
        }

        /// <summary>
        /// Returns the exoskeleton the player is wearing if it is intact and has fuel left, otherwise null.
        /// Safe to call on either side for the owning player.
        /// </summary>
        public ItemExoskeletonChest GetPoweredExo(IPlayer player)
        {
            return GetPoweredExoSlot(player)?.Itemstack.Collectible as ItemExoskeletonChest;
        }

        public ItemSlot GetPoweredExoSlot(IPlayer player)
        {
            var slot = GetExoSlot(player);
            if (slot == null) return null;

            var exo = (ItemExoskeletonChest)slot.Itemstack.Collectible;
            return exo.IsPowered(slot.Itemstack) ? slot : null;
        }

        /// <summary>
        /// Returns the armor slot containing exoskeleton if available, otherwise returns null.
        /// </summary>
        public ItemSlot GetExoSlot(IPlayer player)
        {
            var inv = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inv == null) return null;

            var slot = inv[(int)EnumCharacterDressType.ArmorBody];
            return slot?.Itemstack?.Collectible is ItemExoskeletonChest ? slot : null;
        }

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
                if (drainFuel) drainExoFuel(player, hoursPassed);

                if (player is IServerPlayer serverPlayer)
                {
                    updateReach(serverPlayer);
                }

                updateExoPoweredFlag(player);
            }
        }

        /// <summary>
        /// If the player has an exoskeleton equipped, update the powered attribute
        /// </summary>
        private void updateExoPoweredFlag(IPlayer player)
        {
            var attrs = player.Entity?.WatchedAttributes;
            if (attrs == null) return;

            bool powered = GetPoweredExoSlot(player) != null;
            if (attrs.GetBool(EntityBehaviorExoVisuals.PoweredAttribute) != powered)
            {
                attrs.SetBool(EntityBehaviorExoVisuals.PoweredAttribute, powered);
            }
        }

        // Mostly replicates the functionality already in the game from the night vision mask
        private void drainExoFuel(IPlayer player, double hoursPassed)
        {
            var slot = GetExoSlot(player);
            if (slot == null) return;

            var exo = (ItemExoskeletonChest)slot.Itemstack.Collectible;
            if (exo.GetFuelHours(slot.Itemstack) <= 0) return;

            exo.AddFuelHours(slot.Itemstack, -hoursPassed);
            slot.MarkDirty();
        }

        private void updateReach(IServerPlayer player)
        {
            if (player.WorldData.CurrentGameMode != EnumGameMode.Survival)
            {
                // Creative and spectator set their own reach, so drop the claim without writing a value.
                reachApplied.Remove(player.PlayerUID);
                return;
            }

            var exo = GetPoweredExo(player);
            bool applied = reachApplied.Contains(player.PlayerUID);

            if ((exo != null) == applied) return;

            // Write absolute values rather than adding an offset, so a crash or an unclean shutdown
            // can't leave the bonus stacked onto itself.
            setPickingRange(player, exo == null ? GlobalConstants.DefaultPickingRange : GlobalConstants.DefaultPickingRange + exo.ReachBonus);

            if (exo != null) reachApplied.Add(player.PlayerUID);
            else reachApplied.Remove(player.PlayerUID);
        }

        private void onPlayerDisconnect(IServerPlayer player)
        {
            if (!reachApplied.Remove(player.PlayerUID)) return;

            setPickingRange(player, GlobalConstants.DefaultPickingRange);
        }

        private void setPickingRange(IServerPlayer player, float range)
        {
            if (Math.Abs(player.WorldData.PickingRange - range) < 0.0001f) return;

            player.WorldData.PickingRange = range;
            player.BroadcastPlayerData();
        }

        public override void Dispose()
        {
            base.Dispose();

            reachApplied.Clear();
            sapi = null;
        }
    }
}
