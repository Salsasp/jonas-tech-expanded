using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace JonasTechExpanded
{
    /// <summary>
    /// Crouch-charges a jump on powered springboots, and absorbs fall damage into fuel.
    /// Writes jumpHeightMul so vanilla PModuleOnGround launches with the charged value;
    /// no custom physics module. Patched first on the server so fall absorb runs before health.
    /// </summary>
    public class EntityBehaviorSpringBootsCharge : EntityBehavior
    {
        public const string JumpHeightStatKey = "springboots";

        private enum ChargeState
        {
            Idle,
            Charging,
            Armed
        }

        private SpringBootsModSystem modSys;
        private ICoreClientAPI capi;

        private ChargeState state = ChargeState.Idle;
        private int chargeLevel;
        private float chargeTime;
        private float armedRemaining;
        private float coyote;
        private float lastWrittenJumpMul = float.NaN;
        private bool pendingConsume;

        private ILoadedSound chargingLoop;
        private ILoadedSound armedLoop;

        public EntityBehaviorSpringBootsCharge(Entity entity) : base(entity) { }

        /// <summary>
        /// True while a charge mul is live, so the 1s boots tick must not overwrite jumpHeightMul.
        /// </summary>
        public bool IsChargeActive => pendingConsume || state == ChargeState.Armed || (state == ChargeState.Charging && chargeLevel >= 1);

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            capi = entity.Api as ICoreClientAPI;
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            modSys = entity.Api.ModLoader.GetModSystem<SpringBootsModSystem>();
        }

        public override void OnGameTick(float deltaTime)
        {
            if (modSys == null || entity is not EntityPlayer entityPlayer) return;
            if (!isAuthoritativeSide(entityPlayer)) return;

            var boots = modSys.GetPoweredSpringBoots(entityPlayer.Player);
            var controls = getControls(entityPlayer);
            bool onGround = entity.OnGround && !entity.Swimming;
            if (onGround) coyote = 0.15f;
            else coyote = Math.Max(0, coyote - deltaTime);
            bool canJump = coyote > 0 && !entity.Swimming && !controls.IsFlying;

            if (boots == null || controls.IsFlying)
            {
                pendingConsume = false;
                cancel(restorePassive: boots != null);
                return;
            }

            bool sneak = controls.Sneak;
            bool jump = controls.Jump;

            if (pendingConsume)
            {
                if (!onGround)
                {
                    applyChargedLaunch(boots.GetChargeJumpMul(chargeLevel));
                    consume(boots);
                    pendingConsume = false;
                    return;
                }

                if (!jump) pendingConsume = false;
            }

            if ((state == ChargeState.Charging || state == ChargeState.Armed) && chargeLevel >= 1)
            {
                float chargeMul = boots.GetChargeJumpMul(chargeLevel);
                writeJumpMul(chargeMul);

                if (jump && canJump)
                {
                    if (!onGround)
                    {
                        applyChargedLaunch(chargeMul);
                        consume(boots);
                        return;
                    }

                    pendingConsume = true;
                }
            }

            switch (state)
            {
                case ChargeState.Idle:
                    if (sneak && onGround) beginCharging(boots);
                    break;

                case ChargeState.Charging:
                    if (!onGround)
                    {
                        if (!pendingConsume) cancel(restorePassive: true);
                        break;
                    }

                    if (!sneak)
                    {
                        if (chargeLevel >= 1) beginArmed(boots);
                        else cancel(restorePassive: true);
                        break;
                    }

                    if (!pendingConsume) tickCharging(boots, deltaTime);
                    break;

                case ChargeState.Armed:
                    armedRemaining -= deltaTime;
                    if (armedRemaining <= 0 && !pendingConsume) cancel(restorePassive: true);
                    else writeJumpMul(boots.GetChargeJumpMul(chargeLevel));
                    break;
            }
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (damage <= 0) return;
            if (damageSource.Source != EnumDamageSource.Fall || damageSource.Type != EnumDamageType.Gravity) return;
            if (modSys == null || entity is not EntityPlayer entityPlayer) return;

            if (entity.World.Side == EnumAppSide.Server)
            {
                var slot = modSys.GetPoweredSpringBootsSlot(entityPlayer.Player);
                if (slot == null) return;

                var boots = (ItemSpringBoots)slot.Itemstack.Collectible;
                float hours = damage * boots.FallDamageFuelHoursPerHp;
                if (hours > 0)
                {
                    boots.AddFuelHours(slot.Itemstack, -hours);
                    slot.MarkDirty();
                }
            }
            else if (!entity.WatchedAttributes.GetBool(EntityBehaviorSpringBootsVisuals.PoweredAttribute))
            {
                return;
            }

            // Must run before vanilla health: that behavior subtracts HP in this same pass.
            damage = 0;
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            stopLoops();
            base.OnEntityDespawn(despawn);
        }

        public override string PropertyName() => "springbootsCharge";

        private bool isAuthoritativeSide(EntityPlayer entityPlayer)
        {
            if (entity.World.Side == EnumAppSide.Server) return true;
            return capi != null && capi.World.Player?.PlayerUID == entityPlayer.PlayerUID;
        }

        private static EntityControls getControls(EntityAgent agent)
        {
            return agent.World.Side == EnumAppSide.Server ? agent.ServerControls : agent.Controls;
        }

        private void beginCharging(ItemSpringBoots boots)
        {
            state = ChargeState.Charging;
            chargeTime = 0;
            chargeLevel = 0;
            startChargingLoop(boots);
        }

        private void tickCharging(ItemSpringBoots boots, float dt)
        {
            chargeTime += dt;
            int next = boots.GetChargeLevel(chargeTime);
            if (next > chargeLevel)
            {
                chargeLevel = next;
                playLevelUp(boots, chargeLevel);
            }

            if (chargeLevel >= 1) writeJumpMul(boots.GetChargeJumpMul(chargeLevel));
        }

        private void beginArmed(ItemSpringBoots boots)
        {
            state = ChargeState.Armed;
            armedRemaining = boots.ChargeHoldSeconds;
            stopChargingLoop();
            startArmedLoop(boots);
            writeJumpMul(boots.GetChargeJumpMul(chargeLevel));
        }

        private void consume(ItemSpringBoots boots)
        {
            goIdle();
            writeJumpMul(boots.JumpHeightMul);
        }

        private void cancel(bool restorePassive)
        {
            bool wasActive = IsChargeActive || state != ChargeState.Idle;
            goIdle();
            if (wasActive && restorePassive)
            {
                var boots = modSys?.GetPoweredSpringBoots((entity as EntityPlayer)?.Player);
                writeJumpMul(boots != null ? boots.JumpHeightMul : 1f);
            }
            else if (wasActive && !restorePassive)
            {
                writeJumpMul(1f);
            }
        }

        private void goIdle()
        {
            state = ChargeState.Idle;
            chargeLevel = 0;
            chargeTime = 0;
            armedRemaining = 0;
            pendingConsume = false;
            stopLoops();
        }

        private void writeJumpMul(float multiplier)
        {
            if (!float.IsNaN(lastWrittenJumpMul) && Math.Abs(lastWrittenJumpMul - multiplier) < 0.0001f) return;

            var stats = entity.Stats;
            if (stats == null) return;

            if (multiplier <= 1f) stats.Remove("jumpHeightMul", JumpHeightStatKey);
            else stats.Set("jumpHeightMul", JumpHeightStatKey, multiplier - 1f, true);

            lastWrittenJumpMul = multiplier;
        }

        private void applyChargedLaunch(float chargeMul)
        {
            if (entity.World.Side != EnumAppSide.Client) return;

            entity.Pos.Motion.Y = GlobalConstants.BaseJumpForce / 60f * MathF.Sqrt(MathF.Max(1f, chargeMul));
        }

        private bool isLocalOwner()
        {
            return capi != null && capi.World.Player?.Entity == entity;
        }

        private void startChargingLoop(ItemSpringBoots boots)
        {
            if (!isLocalOwner()) return;
            stopChargingLoop();
            chargingLoop = loadLoop(boots.ChargeSound);
            chargingLoop?.Start();
        }

        private void startArmedLoop(ItemSpringBoots boots)
        {
            if (!isLocalOwner()) return;
            stopArmedLoop();
            armedLoop = loadLoop(boots.ChargeArmedSound);
            armedLoop?.Start();
        }

        private ILoadedSound loadLoop(AssetLocation loc)
        {
            if (capi == null || loc == null) return null;

            return capi.World.LoadSound(new SoundParams
            {
                Location = loc.Clone(),
                ShouldLoop = true,
                DisposeOnFinish = false,
                Position = new Vec3f(),
                RelativePosition = true,
                Range = 16,
                Volume = 0.6f,
                SoundType = EnumSoundType.Sound
            });
        }

        private void playLevelUp(ItemSpringBoots boots, int level)
        {
            if (boots.ChargeLevelSound == null) return;

            var player = (entity as EntityPlayer)?.Player;
            float pitch = 0.85f + 0.15f * level;
            entity.World.PlaySoundAt(boots.ChargeLevelSound, entity, player, pitch, 16, 0.8f);
        }

        private void stopLoops()
        {
            stopChargingLoop();
            stopArmedLoop();
        }

        private void stopChargingLoop()
        {
            chargingLoop?.Stop();
            chargingLoop?.Dispose();
            chargingLoop = null;
        }

        private void stopArmedLoop()
        {
            armedLoop?.Stop();
            armedLoop?.Dispose();
            armedLoop = null;
        }
    }
}
