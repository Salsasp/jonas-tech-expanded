using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace JonasTechExpanded
{
    /// <summary>
    /// Drains a powered exoskeleton's fuel whenever the wearer takes damage, in proportion to the HP lost.
    /// Patched onto the player's server behaviors after vanilla health: health runs its onDamaged delegates
    /// (armor, shields) on the same damage value, so this sees what actually got through. A fall that
    /// powered springboots absorbed arrives here as 0 and costs nothing.
    /// </summary>
    public class EntityBehaviorExoDamageDrain : EntityBehavior
    {
        private ExoskeletonModSystem modSys;

        public EntityBehaviorExoDamageDrain(Entity entity) : base(entity)
        {
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            modSys = entity.Api.ModLoader.GetModSystem<ExoskeletonModSystem>();
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            if (damage <= 0 || damageSource.Type == EnumDamageType.Heal) return;
            // Health turns a hit with a duration into a damage-over-time effect without dealing it now;
            // its ticks arrive later as ordinary damage, so only those count.
            if (damageSource.Duration > System.TimeSpan.Zero) return;
            if (entity.World.Side != EnumAppSide.Server || modSys == null) return;

            var slot = modSys.GetPoweredExoSlot((entity as EntityPlayer)?.Player);
            if (slot == null) return;

            var exo = (ItemExoskeletonChest)slot.Itemstack.Collectible;
            float hours = damage * exo.DamageFuelHoursPerHp;
            if (hours <= 0) return;

            exo.AddFuelHours(slot.Itemstack, -hours);
            slot.MarkDirty();
        }

        public override string PropertyName() => "exoDamageDrain";
    }
}
