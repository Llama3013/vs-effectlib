using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public class CollectibleBehaviorEffectItem(CollectibleObject collObj)
        : CollectibleBehavior(collObj)
    {
        protected string attributeKey;
        protected string idField;
        private bool consumeOnUse;
        private int durabilityCost;

        protected string animation;
        protected string sound;
        private float consumeTime;

        private string ownEffectId;
        private IProgressBar progressBarRender;

        protected ICoreAPI Api { get; private set; }

        private static readonly HashSet<long> resolvedEntities = [];

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            attributeKey = properties["attributeKey"].AsString("effectinfo");
            idField = properties["idField"].AsString("effectId");
            consumeOnUse = properties["consumeOnUse"].AsBool(true);
            durabilityCost = properties["durabilityCost"].AsInt();
            animation = properties["animation"].AsString("eat");
            sound = properties["sound"].AsString("game:sounds/player/eat");
            consumeTime = properties["consumeTime"].AsFloat(1.6f);
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            Api = api;
            RegisterOwnEffect();
        }

        protected virtual void RegisterOwnEffect()
        {
            if (
                JsonEffectDefinition.TryReadOwn(
                    collObj,
                    attributeKey,
                    idField,
                    "EffectItem",
                    Api.Logger,
                    out JsonObject def,
                    out ownEffectId
                )
            )
                JsonEffectDefinition.RegisterFrom(ownEffectId, collObj.Code.Domain, def, collObj.Code);
        }

        protected virtual bool ShouldIntercept(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel
        ) => true;

        protected virtual bool TryResolveEffect(
            ItemSlot slot,
            EntityAgent byEntity,
            out string effectId,
            out float potencyMul,
            out float durationMul
        )
        {
            effectId = ownEffectId;
            potencyMul = 1f;
            durationMul = 1f;
            return effectId != null;
        }

        protected virtual float GetConsumeTime(EntityAgent byEntity) => consumeTime;

        protected virtual AssetLocation GetEffectSource(ItemSlot slot) => collObj.Code;

        protected virtual bool HasEnoughSource(ItemSlot slot) => (slot.Itemstack?.StackSize ?? 0) > 0;

        protected virtual string GetBlockReason(
            ItemSlot slot,
            EntityAgent byEntity,
            string effectId,
            EffectContext ctx
        )
        {
            if (!HasEnoughSource(slot))
                return "effectlib:not-enough-source";

            if (
                byEntity is EntityPlayer player
                && EntityBehaviorPlayerEffects.ManagerFor(player) is { } manager
                && manager.IsActive(effectId)
                && !manager.CanRefresh(effectId)
            )
                return "effectlib:effect-already-active";

            return null;
        }

        protected virtual bool ApplyEffect(
            ItemSlot slot,
            EntityAgent byEntity,
            string effectId,
            EffectContext ctx
        )
        {
            if (byEntity is not EntityPlayer player)
                return false;

            EffectManager manager = EntityBehaviorPlayerEffects.ManagerFor(player);
            if (manager == null)
                return false;

            string name = EffectLang.NameFor(effectId, ctx);
            if (!manager.TryApply(effectId, ctx, name))
                return false;

            EffectLang.SendGained(player, effectId, name);

            return true;
        }

        protected virtual void OnConsumed(ItemSlot slot, EntityAgent byEntity)
        {
            if (durabilityCost > 0 && collObj.GetMaxDurability(slot.Itemstack) > 1)
            {
                collObj.DamageItem(byEntity.World, byEntity, slot, durabilityCost);
                return;
            }

            if (!consumeOnUse)
                return;
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        protected virtual void AppendExtraTooltip(
            StringBuilder dsc,
            string effectId,
            EffectContext ctx
        ) { }

        protected virtual void DenyUse(EntityAgent byEntity, string reasonLangKey)
        {
            byEntity.PlayEntitySound("smallhurt", (byEntity as EntityPlayer)?.Player);
            if (reasonLangKey != null && byEntity is EntityPlayer { Player: IServerPlayer serverPlayer })
                serverPlayer.SendMessage(
                    GlobalConstants.InfoLogChatGroup,
                    Lang.Get(reasonLangKey),
                    EnumChatType.Notification
                );
        }

        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling,
            ref EnumHandling bhHandling
        )
        {
            if (!ShouldIntercept(slot, byEntity, blockSel))
                return;
            if (!TryResolveEffect(slot, byEntity, out _, out _, out _))
                return;

            if (byEntity.World.Side == EnumAppSide.Server)
                resolvedEntities.Remove(byEntity.EntityId);

            float time = GetConsumeTime(byEntity);
            if (time <= 0f)
            {
                if (firstEvent && byEntity.World.Side == EnumAppSide.Server)
                    TryConsumeAndApply(slot, byEntity);

                handling = EnumHandHandling.PreventDefault;
                bhHandling = EnumHandling.PreventDefault;
                return;
            }

            byEntity.World.RegisterCallback(
                dt =>
                {
                    if (byEntity.Controls.HandUse == EnumHandInteract.HeldItemInteract)
                        byEntity.PlayEntitySound(sound, (byEntity as EntityPlayer)?.Player);
                },
                200
            );
            byEntity.AnimManager?.StartAnimation(animation);

            if (Api?.Side == EnumAppSide.Client)
            {
                ModSystemProgressBar progressBarSystem =
                    Api.ModLoader.GetModSystem<ModSystemProgressBar>();
                progressBarSystem.RemoveProgressbar(progressBarRender);
                progressBarRender = progressBarSystem.AddProgressbar();
            }

            handling = EnumHandHandling.PreventDefault;
            bhHandling = EnumHandling.PreventDefault;
        }

        public override bool OnHeldInteractStep(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            ref EnumHandling handling
        )
        {
            if (!TryResolveEffect(slot, byEntity, out _, out _, out _))
                return base.OnHeldInteractStep(
                    secondsUsed,
                    slot,
                    byEntity,
                    blockSel,
                    entitySel,
                    ref handling
                );

            handling = EnumHandling.PreventDefault;

            if (secondsUsed > 0.5f && (int)(30 * secondsUsed) % 7 == 1)
            {
                Vec3d pos = byEntity.Pos.AheadCopy(0.4f).XYZ.Add(byEntity.LocalEyePos);
                pos.Y -= 0.4f;
                byEntity.World.SpawnCubeParticles(
                    pos,
                    slot.Itemstack,
                    0.3f,
                    4,
                    0.5f,
                    (byEntity as EntityPlayer)?.Player
                );
            }

            float currentConsumeTime = GetConsumeTime(byEntity);
            if (progressBarRender != null)
                progressBarRender.Progress =
                    currentConsumeTime > 0 ? secondsUsed / currentConsumeTime : 1f;

            return secondsUsed <= currentConsumeTime;
        }

        public override bool OnHeldInteractCancel(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            EnumItemUseCancelReason cancelReason,
            ref EnumHandling handling
        )
        {
            ClearProgressBar();
            return base.OnHeldInteractCancel(
                secondsUsed,
                slot,
                byEntity,
                blockSel,
                entitySel,
                cancelReason,
                ref handling
            );
        }

        public override void OnHeldInteractStop(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            ref EnumHandling handling
        )
        {
            ClearProgressBar();

            if (!TryResolveEffect(slot, byEntity, out _, out _, out _))
                return;

            handling = EnumHandling.PreventDefault;

            float consumeTimeNow = GetConsumeTime(byEntity);
            if (consumeTimeNow <= 0f)
                return;

            if (byEntity.World.Side != EnumAppSide.Server)
                return;
            if (secondsUsed < consumeTimeNow - 0.05f)
                return;
            if (!resolvedEntities.Add(byEntity.EntityId))
                return;

            TryConsumeAndApply(slot, byEntity);
        }

        private void ClearProgressBar()
        {
            Api?.ModLoader.GetModSystem<ModSystemProgressBar>()?.RemoveProgressbar(progressBarRender);
            progressBarRender = null;
        }

        private void TryConsumeAndApply(ItemSlot slot, EntityAgent byEntity)
        {
            if (
                !TryResolveEffect(
                    slot,
                    byEntity,
                    out string effectId,
                    out float potencyMul,
                    out float durationMul
                )
            )
                return;

            EffectContext ctx = EffectRegistry.Build(
                effectId,
                potencyMul,
                GetEffectSource(slot),
                durationMul
            );
            if (ctx == null)
                return;

            // Runs before the virtual GetBlockReason so overrides can't skip it.
            string blockReason =
                UtilityEffects.GetBlockReason(byEntity, ctx) ?? GetBlockReason(slot, byEntity, effectId, ctx);
            if (blockReason != null)
            {
                DenyUse(byEntity, blockReason);
                return;
            }

            if (ctx.ResetsEffects && byEntity is EntityPlayer purgePlayer)
                EntityBehaviorPlayerEffects.ManagerFor(purgePlayer)?.PurgeFor(effectId, ctx);

            if (ApplyEffect(slot, byEntity, effectId, ctx))
                OnConsumed(slot, byEntity);
            else
                DenyUse(byEntity, null);
        }

        public override void GetHeldItemInfo(
            ItemSlot slot,
            StringBuilder dsc,
            IWorldAccessor world,
            bool withDebugInfo
        )
        {
            if (
                !TryResolveEffect(
                    slot,
                    null,
                    out string effectId,
                    out float potencyMul,
                    out float durationMul
                )
            )
                return;

            EffectContext ctx = EffectRegistry.Build(
                effectId,
                potencyMul,
                GetEffectSource(slot),
                durationMul
            );
            if (ctx == null)
                return;

            AppendDescription(dsc, effectId, ctx);
            AppendExtraTooltip(dsc, effectId, ctx);
        }

        private static void AppendDescription(StringBuilder dsc, string effectId, EffectContext ctx)
        {
            foreach (string line in EffectDescription.Lines(effectId, ctx))
                dsc.AppendLine(line);
        }
    }
}
