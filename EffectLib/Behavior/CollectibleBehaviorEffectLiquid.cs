using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public class CollectibleBehaviorEffectLiquid(CollectibleObject collObj)
        : CollectibleBehaviorEffectItem(collObj)
    {
        protected float consumeLitres;
        protected float checkLitres;

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            consumeLitres = properties["consumeLitres"].AsFloat(0.25f);
            checkLitres = properties["checkLitres"].AsFloat(0.25f);
        }

        protected override void RegisterOwnEffect() { }

        private CollectibleObject ContentOf(ItemSlot slot) =>
            (collObj as BlockLiquidContainerBase)?.GetContent(slot?.Itemstack)?.Collectible;

        protected override AssetLocation GetEffectSource(ItemSlot slot) => ContentOf(slot)?.Code;

        protected override bool TryResolveEffect(
            ItemSlot slot,
            EntityAgent byEntity,
            out string effectId,
            out float potencyMul,
            out float durationMul
        )
        {
            effectId = null;
            potencyMul = 1f;
            durationMul = 1f;

            CollectibleObject content = ContentOf(slot);
            if (content == null)
                return false;

            JsonObject def = content.Attributes?[attributeKey];
            if (def?.Exists != true)
                return false;

            effectId = EffectIds.Read(def, idField, content);
            if (string.IsNullOrWhiteSpace(effectId))
            {
                effectId = null;
                return false;
            }

            if (!EffectRegistry.IsRegistered(effectId, content.Code))
                JsonEffectDefinition.RegisterFrom(effectId, content.Code.Domain, def, content.Code);

            return true;
        }

        protected override bool HasEnoughSource(ItemSlot slot) =>
            collObj is BlockLiquidContainerBase container
            && container.GetCurrentLitres(slot.Itemstack) >= checkLitres;

        protected override void OnConsumed(ItemSlot slot, EntityAgent byEntity)
        {
            if (collObj is not BlockLiquidContainerBase container)
                return;

            EntityPlayer player = byEntity as EntityPlayer;
            container.SplitStackAndPerformAction(
                player,
                slot,
                stack => container.TryTakeLiquid(stack, consumeLitres)?.StackSize ?? 0
            );
            slot.MarkDirty();
            player?.Player?.InventoryManager?.BroadcastHotbarSlot();
        }
    }
}
