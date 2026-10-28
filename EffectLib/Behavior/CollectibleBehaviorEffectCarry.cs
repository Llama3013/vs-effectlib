using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public enum EnumCarryActivation
    {
        Held,
        Hotbar,
        Inventory,
        Worn,
    }

    public enum EnumCarryHand
    {
        Main,
        Off,
        Either,
        Both,
    }

    /// <summary>
    /// Attach to an item/block that should grant its effect passively for as long as it is
    /// carried, rather than consumed. Where the item must be (held/hotbar/inventory/worn) and, for
    /// "held", which hand(s), is declared on the item's own effect definition attribute
    /// alongside effectId/duration/stats/etc. (see <see cref="JsonEffectDefinition"/>), not as a
    /// behavior property - it's per-item content, not parsing config. Activation/deactivation is
    /// evaluated periodically by <see cref="CarryEffectScanner"/>.
    /// </summary>
    public class CollectibleBehaviorEffectCarry(CollectibleObject collObj)
        : CollectibleBehavior(collObj)
    {
        protected string attributeKey;
        protected string idField;

        protected ICoreAPI Api { get; private set; }

        // Set once any item loads a working carry effect - with none, players inventories are never scanned.
        public static bool AnyLoaded { get; private set; }

        public string EffectId { get; private set; }

        public string DeclaredEffectId { get; private set; }

        public string DisplayName =>
            EffectLang.NameFor(
                DeclaredEffectId,
                EffectRegistry.Build(OneShot ? DeclaredEffectId : EffectId, 1f, Source)
            );

        public AssetLocation Source => collObj.Code;

        // When several carried items share an effect id, the highest order is the one applied.
        public int Order { get; private set; }

        public EnumCarryActivation Activation { get; private set; }
        public EnumCarryHand Hand { get; private set; } = EnumCarryHand.Either;

        public bool AllowWhenBroken { get; private set; }

        // Durability drain destroys the item at zero instead of leaving it broken.
        public bool DestroyWhenBroken { get; private set; }

        public float DurabilityDrainIntervalSec { get; private set; }
        public int DurabilityDrainAmount { get; private set; } = 1;

        public bool RequiresToggle { get; private set; }
        public bool OneShot { get; private set; }
        public float RollCooldownSec { get; private set; } = -1f;

        private const string ToggleAttrKey = "effectlibCarryOn";

        public bool IsToggledOn(ItemSlot slot) =>
            !RequiresToggle || (slot?.Itemstack?.Attributes?.GetBool(ToggleAttrKey) ?? false);

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            attributeKey = properties["attributeKey"].AsString("effectinfo");
            idField = properties["idField"].AsString("effectId");
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            Api = api;
            RegisterOwnEffect();
        }

        private void RegisterOwnEffect()
        {
            if (
                !JsonEffectDefinition.TryReadOwn(
                    collObj,
                    attributeKey,
                    idField,
                    "EffectCarry",
                    Api.Logger,
                    out JsonObject def,
                    out string declaredId
                )
            )
                return;

            string activation = def["activation"].AsString();
            if (
                string.IsNullOrWhiteSpace(activation)
                || !Enum.TryParse(activation, true, out EnumCarryActivation parsedActivation)
            )
            {
                Api.Logger.Warning(
                    "[EffectLib] {0}'s '{1}' attribute has no valid 'activation' - give it one of "
                        + "\"held\", \"hotbar\", \"inventory\" or \"worn\". This item will do nothing.",
                    collObj.Code,
                    attributeKey
                );
                return;
            }
            Activation = parsedActivation;

            string hand = def["hand"].AsString();
            if (!string.IsNullOrWhiteSpace(hand))
            {
                if (Enum.TryParse(hand, true, out EnumCarryHand parsedHand))
                    Hand = parsedHand;
                else
                    Api.Logger.Warning(
                        "[EffectLib] {0}'s '{1}' attribute has an invalid 'hand' \"{2}\" - give it one of "
                            + "\"main\", \"off\", \"either\" or \"both\". Using \"either\".",
                        collObj.Code,
                        attributeKey,
                        hand
                    );
            }

            AllowWhenBroken = def["allowWhenBroken"].AsBool();
            DestroyWhenBroken = def["destroyWhenBroken"].AsBool();
            DurabilityDrainIntervalSec = def["durabilityDrainIntervalSec"].AsFloat();
            DurabilityDrainAmount = def["durabilityDrainAmount"].AsInt(1);
            RequiresToggle = def["toggle"].AsBool();
            Order = def["order"].AsInt();
            OneShot = def["oneShot"].AsBool();
            RollCooldownSec = def["rollCooldownSec"].AsFloat(-1f);
            bool notify = def["notify"].AsBool();

            EffectContext probe = new() { PotencyMul = 1f };
            JsonEffectDefinition.Apply(probe, def, collObj.Code.Domain);
            List<string> ignored = AdaptForCarry(probe);
            if (ignored.Count > 0 && !OneShot)
                Api.Logger.Warning(
                    "[EffectLib] {0}'s '{1}' attribute sets {2}, which carried items ignore - they "
                        + "would re-fire every time the item is re-equipped.",
                    collObj.Code,
                    attributeKey,
                    string.Join(", ", ignored)
                );

            DeclaredEffectId = declaredId;
            EffectId = CarryEffectIds.For(declaredId);
            AnyLoaded = true;

            JsonEffectDefinition.RegisterFrom(
                EffectId,
                collObj.Code.Domain,
                def,
                collObj.Code,
                ctx =>
                {
                    AdaptForCarry(ctx);
                    ctx.Duration = EffectContext.EndlessDuration;
                    ctx.Notify = notify;
                }
            );

            if (OneShot)
                JsonEffectDefinition.RegisterFrom(declaredId, collObj.Code.Domain, def, collObj.Code);
        }

        // One-time effects (a permanent size change among them) would re-fire on every re-grant,
        // and a purge would fight other carried items, so carry effects drop them - returning the
        // names of any dropped. A plain size change lasts while the item is carried.
        private static List<string> AdaptForCarry(EffectContext ctx)
        {
            List<string> stripped = [];

            void Drop(bool isSet, string field, Action clear)
            {
                if (!isSet)
                    return;
                clear();
                stripped.Add(field);
            }

            Drop(
                ctx.TickSec <= 0f && Math.Abs(ctx.Health) > float.Epsilon,
                "health (without tickSec)",
                () => ctx.Health = 0f
            );
            Drop(
                Math.Abs(ctx.RetainedNutrition) > float.Epsilon,
                "retainedNutrition",
                () => ctx.RetainedNutrition = 0f
            );
            Drop(
                Math.Abs(ctx.TemporalStabilityGain) > float.Epsilon,
                "temporalStabilityGain",
                () => ctx.TemporalStabilityGain = 0f
            );
            Drop(ctx.Respawn, "respawn", () => ctx.Respawn = false);
            Drop(ctx.Reshape, "reshape", () => ctx.Reshape = false);
            Drop(ctx.SizePermanent, "sizePermanent", () => ctx.SizePermanent = false);
            Drop(ctx.ResetsEffects, "resetsEffects", () => ctx.ResetsEffects = false);
            Drop(ctx.Stacking != EnumEffectStacking.None, "stacking", () => ctx.Stacking = EnumEffectStacking.None);

            return stripped;
        }

        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handHandling,
            ref EnumHandling bhHandling
        )
        {
            if (!RequiresToggle || EffectId == null || !firstEvent)
                return;

            if (byEntity.World.Side == EnumAppSide.Server)
            {
                bool nowOn = !IsToggledOn(slot);
                slot.Itemstack.Attributes.SetBool(ToggleAttrKey, nowOn);
                slot.MarkDirty();

                if (byEntity is EntityPlayer { Player: IServerPlayer serverPlayer })
                    EffectLang.SendFor(
                        serverPlayer,
                        Source.Domain,
                        nowOn ? "carry-toggled-on" : "carry-toggled-off",
                        DisplayName
                    );
            }

            handHandling = EnumHandHandling.PreventDefault;
            bhHandling = EnumHandling.PreventDefault;
        }

        public override void GetHeldItemInfo(
            ItemSlot inSlot,
            StringBuilder dsc,
            IWorldAccessor world,
            bool withDebugInfo
        )
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            EffectContext ctx = EffectId == null
                ? null
                : EffectRegistry.Build(OneShot ? DeclaredEffectId : EffectId, 1f, Source);
            if (ctx != null)
            {
                dsc.AppendLine(Lang.Get(EffectLang.Key(Source.Domain, "carry-when-" + ActivationLangSuffix)));
                foreach (string line in EffectDescription.EffectLines(ctx))
                    dsc.AppendLine(line);
            }

            if (RequiresToggle)
                dsc.AppendLine(
                    Lang.Get(EffectLang.Key(Source.Domain, IsToggledOn(inSlot) ? "carry-toggle-state-on" : "carry-toggle-state-off"))
                );
        }

        private string ActivationLangSuffix =>
            Activation switch
            {
                EnumCarryActivation.Held => Hand switch
                {
                    EnumCarryHand.Main => "held-main",
                    EnumCarryHand.Off => "held-off",
                    EnumCarryHand.Both => "held-both",
                    _ => "held",
                },
                EnumCarryActivation.Hotbar => "hotbar",
                EnumCarryActivation.Worn => "worn",
                _ => "inventory",
            };
    }
}
