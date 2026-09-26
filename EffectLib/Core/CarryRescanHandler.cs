using Vintagestory.API.Common;

namespace EffectLib
{
    // Any removal - expiry, purge, death, group eviction, commands - can free up a carry effect
    // that was blocked or knocked off, so every one queues a carry rescan.
    internal sealed class CarryRescanHandler : IEffectHandler
    {
        public static readonly CarryRescanHandler Instance = new();

        private CarryRescanHandler() { }

        public void OnApplied(EntityPlayer entity, string effectId, EffectContext ctx) { }

        public void OnRemoved(EntityPlayer entity, string effectId, EffectContext ctx) =>
            MarkDirty(entity);

        public void OnCleared(EntityPlayer entity, EffectPurge scope) => MarkDirty(entity);

        public void OnRestored(EntityPlayer entity) { }

        private static void MarkDirty(EntityPlayer entity) =>
            entity?.GetBehavior<EntityBehaviorPlayerEffects>()?.MarkCarryDirty();
    }
}
