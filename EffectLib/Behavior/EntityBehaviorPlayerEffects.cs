using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public class EntityBehaviorPlayerEffects : EntityBehavior
    {
        private readonly EntityPlayer player;
        private readonly HashSet<string> carryActiveIds = [];

        private bool carryDirty = true;

        private IInventory hookedHotbar;
        private IInventory hookedBackpack;

        private readonly long dirtyCheckListenerId;
        private readonly long safetyNetListenerId;
        private readonly long durabilityListenerId;

        public EntityBehaviorPlayerEffects(Entity entity)
            : base(entity)
        {
            if (entity is EntityPlayer ep && entity.World.Side == EnumAppSide.Server)
            {
                player = ep;
                Manager = new EffectManager(ep);

                dirtyCheckListenerId = entity.World.RegisterGameTickListener(
                    OnDirtyCheckTick,
                    (int)(EffectLibConfig.Loaded.CarryDirtyCheckIntervalSec * 1000)
                );
                safetyNetListenerId = entity.World.RegisterGameTickListener(
                    OnSafetyNetTick,
                    (int)(EffectLibConfig.Loaded.CarrySafetyNetIntervalSec * 1000)
                );
                durabilityListenerId = entity.World.RegisterGameTickListener(
                    OnDurabilityTick,
                    (int)(EffectLibConfig.Loaded.CarryDurabilityCheckIntervalSec * 1000)
                );
            }
        }

        public EffectManager Manager { get; private set; }

        public override string PropertyName() => "effectlibPlayerEffects";

        public override void OnEntityDespawn(EntityDespawnData despawnData)
        {
            base.OnEntityDespawn(despawnData);

            if (Manager == null)
                return;

            entity.World.UnregisterGameTickListener(dirtyCheckListenerId);
            entity.World.UnregisterGameTickListener(safetyNetListenerId);
            entity.World.UnregisterGameTickListener(durabilityListenerId);
        }

        private void OnDirtyCheckTick(float dt)
        {
            if (!carryDirty)
                return;

            carryDirty = false;
            CarryEffectScanner.Scan(player, Manager, carryActiveIds);
        }

        // Unconditional - runs regardless of the dirty flag, see CarrySafetyNetIntervalSec.
        private void OnSafetyNetTick(float dt)
        {
            carryDirty = false;
            CarryEffectScanner.Scan(player, Manager, carryActiveIds);
        }

        private void OnDurabilityTick(float dt) =>
            CarryEffectScanner.DrainDurability(
                player,
                Manager,
                EffectLibConfig.Loaded.CarryDurabilityCheckIntervalSec
            );

        public void SuspendCarryEffects()
        {
            if (Manager == null)
                return;

            foreach (string id in carryActiveIds)
                Manager.RemoveEffect(id, notify: false);

            carryActiveIds.Clear();
        }

        public void MarkCarryDirty() => carryDirty = true;

        public void HookCarryInventoryEvents()
        {
            IPlayerInventoryManager inventoryManager = player?.Player?.InventoryManager;
            if (inventoryManager == null)
                return;

            UnhookCarryInventoryEvents();

            hookedHotbar = inventoryManager.GetHotbarInventory();
            hookedBackpack = inventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);

            if (hookedHotbar != null)
                hookedHotbar.SlotModified += OnCarrySlotModified;
            if (hookedBackpack != null)
                hookedBackpack.SlotModified += OnCarrySlotModified;
        }

        public void UnhookCarryInventoryEvents()
        {
            if (hookedHotbar != null)
                hookedHotbar.SlotModified -= OnCarrySlotModified;
            if (hookedBackpack != null)
                hookedBackpack.SlotModified -= OnCarrySlotModified;

            hookedHotbar = null;
            hookedBackpack = null;
        }

        private void OnCarrySlotModified(int slotId) => MarkCarryDirty();

        public static EffectManager ManagerFor(EntityPlayer entity)
        {
            if (entity?.Properties == null)
                return null;

            if (!entity.HasBehavior<EntityBehaviorPlayerEffects>())
                entity.AddBehavior(new EntityBehaviorPlayerEffects(entity));

            return entity.GetBehavior<EntityBehaviorPlayerEffects>()?.Manager;
        }
    }
}
