using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public class EntityBehaviorPlayerEffects : EntityBehavior
    {
        private const int CarryPollMs = 250;

        private readonly EntityPlayer player;

        private bool carryPending;
        private long carryChangedMs;
        private long lastDrainMs;

        private IInventory hookedHotbar;
        private IInventory hookedBackpack;

        private long pollListenerId;
        private long safetyNetListenerId;
        private long durabilityListenerId;

        public EntityBehaviorPlayerEffects(Entity entity)
            : base(entity)
        {
            if (entity is EntityPlayer ep && entity.World.Side == EnumAppSide.Server)
            {
                player = ep;
                Manager = new EffectManager(ep);
            }
        }

        public EffectManager Manager { get; private set; }

        public override string PropertyName() => "effectlibPlayerEffects";

        public override void OnEntityDespawn(EntityDespawnData despawnData)
        {
            base.OnEntityDespawn(despawnData);
            StopCarryTracking();
        }

        // Call once the player is actually playing, so their inventories exist.
        public void StartCarryTracking()
        {
            IPlayerInventoryManager inventoryManager = player?.Player?.InventoryManager;
            if (Manager == null || inventoryManager == null || !CollectibleBehaviorEffectCarry.AnyLoaded)
                return;

            StopCarryTracking();

            hookedHotbar = inventoryManager.GetHotbarInventory();
            hookedBackpack = inventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);

            if (hookedHotbar != null)
                hookedHotbar.SlotModified += OnCarrySlotModified;
            if (hookedBackpack != null)
                hookedBackpack.SlotModified += OnCarrySlotModified;

            IWorldAccessor world = entity.World;
            pollListenerId = world.RegisterGameTickListener(OnCarryPollTick, CarryPollMs);
            safetyNetListenerId = world.RegisterGameTickListener(
                _ => RunCarryScan(),
                (int)(EffectLibConfig.Loaded.CarrySafetyNetIntervalSec * 1000)
            );
            durabilityListenerId = world.RegisterGameTickListener(
                OnDurabilityTick,
                (int)(EffectLibConfig.Loaded.CarryDurabilityCheckIntervalSec * 1000)
            );
            lastDrainMs = world.ElapsedMilliseconds;

            MarkCarryDirty();
        }

        public void StopCarryTracking()
        {
            if (hookedHotbar != null)
                hookedHotbar.SlotModified -= OnCarrySlotModified;
            if (hookedBackpack != null)
                hookedBackpack.SlotModified -= OnCarrySlotModified;

            hookedHotbar = null;
            hookedBackpack = null;

            foreach (long listenerId in new[] { pollListenerId, safetyNetListenerId, durabilityListenerId })
                if (listenerId != 0)
                    entity.World.UnregisterGameTickListener(listenerId);

            pollListenerId = safetyNetListenerId = durabilityListenerId = 0;
        }

        // Removes carry effects without saving them - the next scan after rejoining re-derives
        // them from whatever the player is carrying then.
        public void SuspendCarryEffects()
        {
            StopCarryTracking();

            if (Manager == null)
                return;

            foreach (string id in Manager.ActiveIds.Where(CarryEffectIds.Is).ToList())
                Manager.RemoveEffect(id, notify: false);
        }

        // Scans are deferred until nothing has changed for CarryScanDelaySec, so scrolling
        // through the hotbar only settles on the slot the player stops on.
        public void MarkCarryDirty()
        {
            carryPending = true;
            carryChangedMs = entity.World.ElapsedMilliseconds;
        }

        private void OnCarryPollTick(float dt)
        {
            if (
                carryPending
                && entity.World.ElapsedMilliseconds - carryChangedMs
                    >= EffectLibConfig.Loaded.CarryScanDelaySec * 1000
            )
                RunCarryScan();
        }

        private void RunCarryScan()
        {
            // Cleared after, not before - the scan's own apply/remove calls mark dirty again, and a
            // scan that couldn't run (e.g. player dead) stays pending until it can.
            if (CarryEffectScanner.Scan(player, Manager))
                carryPending = false;
        }

        private void OnDurabilityTick(float dt)
        {
            long now = entity.World.ElapsedMilliseconds;
            float intervalSec = EffectLibConfig.Loaded.CarryDurabilityCheckIntervalSec;

            // Real elapsed time so lag doesn't cause problems
            float elapsedSec = Math.Min((now - lastDrainMs) / 1000f, intervalSec * 2);
            lastDrainMs = now;

            if (CarryEffectScanner.DrainDurability(player, Manager, elapsedSec))
                RunCarryScan();
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
