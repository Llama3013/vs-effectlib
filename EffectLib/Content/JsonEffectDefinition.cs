using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace EffectLib
{
    public static class JsonEffectDefinition
    {
        // domain qualifies plain ids in resetEffectIds - the declaring item's mod, see EffectIds.
        public static void Apply(EffectContext ctx, JsonObject def, string domain = null)
        {
            if (ctx == null || def == null)
                return;

            ctx.ResetsEffects = def["resetsEffects"].AsBool();
            ctx.ReplaceIfActive = def["replaceIfActive"].AsBool();
            ctx.Duration = def["duration"].AsInt();
            if (ctx.Duration < 0)
                ctx.Duration = EffectContext.EndlessDuration;
            ctx.RepeatSec = def["repeatSec"].AsFloat();

            string[] resetDomains = def["resetDomains"].AsArray<string>(null);
            if (resetDomains != null)
                ctx.ResetDomains.AddRange(resetDomains.Where(d => !string.IsNullOrWhiteSpace(d)));

            string[] resetEffectIds = def["resetEffectIds"].AsArray<string>(null);
            if (resetEffectIds != null)
                ctx.ResetEffectIds.AddRange(
                    resetEffectIds.Select(id => EffectIds.Qualify(id, domain)).Where(id => id != null)
                );

            Dictionary<string, float> stats = def["stats"]
                .AsObject<Dictionary<string, float>>(null);
            if (stats != null)
            {
                foreach (KeyValuePair<string, float> stat in stats)
                    ctx.AddStat(stat.Key, stat.Value);
            }

            float health = def["health"].AsFloat();
            if (Math.Abs(health) > float.Epsilon)
            {
                ctx.SetHealth(health);
                ctx.TickSec = def["tickSec"].AsFloat();

                string damageType = def["damageType"].AsString();
                if (
                    !string.IsNullOrWhiteSpace(damageType)
                    && Enum.TryParse(damageType, true, out EnumDamageType parsed)
                )
                    ctx.DamageType = parsed;
            }

            ctx.GlowStrength = def["glowStrength"].AsInt();
            ctx.TemporalStabilityGain = def["temporalStabilityGain"].AsFloat();
            ctx.RetainedNutrition = def["retainedNutrition"].AsFloat();
            ctx.SizeChange = def["sizeChange"].AsFloat();
            ctx.SizeMinHeight = def["sizeMinHeight"].AsFloat();
            ctx.SizeMaxHeight = def["sizeMaxHeight"].AsFloat();
            ctx.FallDamageReduction = def["fallDamageReduction"].AsFloat();

            ctx.WaterBreathe = def["waterBreathe"].AsBool();
            ctx.ColdResist = def["coldResist"].AsBool();
            ctx.CanClimbAnywhere = def["canClimbAnywhere"].AsBool();
            ctx.CanFly = def["canFly"].AsBool();
            ctx.Respawn = def["respawn"].AsBool();
            ctx.Reshape = def["reshape"].AsBool();
            ctx.BlockRecallOnVessel = def["blockRecallOnVessel"].AsBool(true);
            ctx.BlockRecallOnMount = def["blockRecallOnMount"].AsBool();
            ctx.BlockReshapeReentry = def["blockReshapeReentry"].AsBool(true);

            ctx.KnockbackResistance = def["knockbackResistance"].AsFloat();
            ctx.NoFallDamage = def["noFallDamage"].AsBool();
            ctx.DisableClimbing = def["disableClimbing"].AsBool();
            ctx.ClimbTouchDistance = def["climbTouchDistance"].AsFloat();
            ctx.Weight = def["weight"].AsFloat();
            ctx.NoGravity = def["noGravity"].AsBool();
        }

        // Reads a behavior's own effect definition attribute and its id, warning when either is
        // missing.
        public static bool TryReadOwn(
            CollectibleObject collObj,
            string attributeKey,
            string idField,
            string behaviorName,
            ILogger logger,
            out JsonObject def,
            out string effectId
        )
        {
            effectId = null;
            def = collObj.Attributes?[attributeKey];
            if (def?.Exists != true)
            {
                logger.Warning(
                    "[EffectLib] {0} has the {1} behavior but no '{2}' attribute, so it "
                        + "will do nothing.",
                    collObj.Code,
                    behaviorName,
                    attributeKey
                );
                return false;
            }

            effectId = EffectIds.Qualify(def[idField].AsString(), collObj.Code.Domain);
            if (string.IsNullOrWhiteSpace(effectId))
            {
                logger.Warning(
                    "[EffectLib] {0}'s '{1}' attribute has no '{2}' - give it one, e.g. "
                        + "\"{1}\": {{ \"{2}\": \"{3}:youreffectid\", ... }}. This item will do nothing.",
                    collObj.Code,
                    attributeKey,
                    idField,
                    collObj.Code.Domain
                );
                effectId = null;
                return false;
            }

            return true;
        }

        public static void RegisterFrom(
            string effectId,
            string domain,
            JsonObject def,
            AssetLocation iconSource = null
        ) => RegisterFrom(effectId, domain, def, iconSource, null);

        // afterApply runs on every built context, after the JSON definition has been applied.
        public static void RegisterFrom(
            string effectId,
            string domain,
            JsonObject def,
            AssetLocation iconSource,
            EffectBuilder afterApply
        )
        {
            JsonObject definition = def;
            string[] channels = def["channels"].AsArray<string>(null);
            string exclusivityGroup = def["exclusivityGroup"].AsString();

            string hudIcon = def["hudIcon"].AsString();
            AssetLocation iconTexture = string.IsNullOrWhiteSpace(hudIcon)
                ? null
                : AssetLocation.Create(hudIcon, string.IsNullOrWhiteSpace(domain) ? EffectRegistry.DefaultDomain : domain);

            EffectRegistry.Register(
                effectId,
                ctx =>
                {
                    Apply(ctx, definition, domain);
                    afterApply?.Invoke(ctx);
                },
                domain,
                iconSource,
                channels,
                exclusivityGroup,
                iconTexture
            );
        }
    }
}
