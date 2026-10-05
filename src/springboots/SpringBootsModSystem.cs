using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace JonasTechExpanded
{
    /// <summary>
    /// Registers the boots and their visuals, drains worn fuel, publishes the powered flag,
    /// and applies the survival move-speed and jump-height multipliers.
    /// </summary>
    public class SpringBootsModSystem : ModSystem
    {
        private const float DefaultMoveSpeedMultiplier = 1f;
        private const string JumpHeightStatKey = "springboots";

        private ICoreServerAPI sapi;
        private readonly HashSet<string> speedApplied = new HashSet<string>();
        private double lastFuelCheckTotalHours = -1;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass("ItemSpringBoots", typeof(ItemSpringBoots));
            api.RegisterEntityBehaviorClass("springbootsVisuals", typeof(EntityBehaviorSpringBootsVisuals));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.RegisterGameTickListener(onServerTick1s, 1000, 200);
            api.Event.PlayerDisconnect += onPlayerDisconnect;
        }

        public ItemSpringBoots GetPoweredSpringBoots(IPlayer player)
        {
            return GetPoweredSpringBootsSlot(player)?.Itemstack.Collectible as ItemSpringBoots;
        }

        public ItemSlot GetPoweredSpringBootsSlot(IPlayer player)
        {
            var slot = GetSpringBootsSlot(player);
            if (slot == null) return null;

            var boots = (ItemSpringBoots)slot.Itemstack.Collectible;
            return boots.IsPowered(slot.Itemstack) ? slot : null;
        }

        public ItemSlot GetSpringBootsSlot(IPlayer player)
        {
            var inv = player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
            if (inv == null) return null;

            var slot = inv[(int)EnumCharacterDressType.ArmorLegs];
            return slot?.Itemstack?.Collectible is ItemSpringBoots ? slot : null;
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
                if (drainFuel) drainSpringBootsFuel(player, hoursPassed);

                if (player is IServerPlayer serverPlayer)
                {
                    updatePlayerSpeed(serverPlayer);
                }

                updateSpringBootsPoweredFlag(player);
            }
        }

        private void updateSpringBootsPoweredFlag(IPlayer player)
        {
            var attrs = player.Entity?.WatchedAttributes;
            if (attrs == null) return;

            bool powered = GetPoweredSpringBootsSlot(player) != null;
            if (attrs.GetBool(EntityBehaviorSpringBootsVisuals.PoweredAttribute) != powered)
            {
                attrs.SetBool(EntityBehaviorSpringBootsVisuals.PoweredAttribute, powered);
            }
        }

        private void drainSpringBootsFuel(IPlayer player, double hoursPassed)
        {
            var slot = GetSpringBootsSlot(player);
            if (slot == null) return;

            var boots = (ItemSpringBoots)slot.Itemstack.Collectible;
            if (boots.GetFuelHours(slot.Itemstack) <= 0) return;

            boots.AddFuelHours(slot.Itemstack, -hoursPassed);
            slot.MarkDirty();
        }

        private void updatePlayerSpeed(IServerPlayer player)
        {
            if (player.WorldData.CurrentGameMode != EnumGameMode.Survival)
            {
                // Creative and spectator set their own speed, so drop the claim without writing a value.
                speedApplied.Remove(player.PlayerUID);
                return;
            }

            var boots = GetPoweredSpringBoots(player);
            bool applied = speedApplied.Contains(player.PlayerUID);

            if ((boots != null) == applied) return;

            setPlayerSpeed(player, boots != null ? boots.SpeedMultiplier : DefaultMoveSpeedMultiplier);
            setJumpHeightMul(player, boots != null ? boots.JumpHeightMul : 1f);

            if (boots != null) speedApplied.Add(player.PlayerUID);
            else speedApplied.Remove(player.PlayerUID);
        }

        private void onPlayerDisconnect(IServerPlayer player)
        {
            if (!speedApplied.Remove(player.PlayerUID)) return;

            setPlayerSpeed(player, DefaultMoveSpeedMultiplier);
            setJumpHeightMul(player, 1f);
        }

        private void setPlayerSpeed(IServerPlayer player, float speedMultiplier)
        {
            if (Math.Abs(player.WorldData.MoveSpeedMultiplier - speedMultiplier) < 0.0001f) return;

            player.WorldData.MoveSpeedMultiplier = speedMultiplier;
            player.BroadcastPlayerData();
        }

        /// <summary>
        /// Writes the JSON multiplier as the jumpHeightMul offset so blended equals that value
        /// (base is 1). PModuleOnGround still takes sqrt of the blended stat.
        /// </summary>
        private void setJumpHeightMul(IServerPlayer player, float multiplier)
        {
            var stats = player.Entity?.Stats;
            if (stats == null) return;

            if (multiplier <= 1f)
            {
                stats.Remove("jumpHeightMul", JumpHeightStatKey);
                return;
            }

            stats.Set("jumpHeightMul", JumpHeightStatKey, multiplier - 1f, true);
        }

        public override void Dispose()
        {
            base.Dispose();

            speedApplied.Clear();
            sapi = null;
        }
    }
}
