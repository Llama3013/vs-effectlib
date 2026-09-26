using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Config;

namespace EffectLib
{
    /// <summary>
    /// Describes what an effect does, from its built context - for item tooltips, and as the
    /// effect's name when no lang entry names its id (see <see cref="EffectLang.NameFor"/>).
    /// </summary>
    public static class EffectDescription
    {
        // Everything the effect does, in display order: a label, plus its amount where it has one.
        private static IEnumerable<(string Label, string Amount)> Parts(string effectId, EffectContext ctx)
        {
            string Label(string key) => EffectLang.GetIfExists(effectId, key) ?? key;

            foreach (KeyValuePair<string, float> stat in ctx.StatModifiers)
                yield return (Label(stat.Key), $"{stat.Value * 100:+0.#;-0.#}%");

            if (Math.Abs(ctx.Health) > float.Epsilon)
                yield return (Label("health"), ctx.Health.ToString("+0.#;-0.#"));
            if (ctx.GlowStrength > 0)
                yield return (Label("glow"), null);
            if (ctx.WaterBreathe)
                yield return (Label("waterbreathe"), null);
            if (ctx.ColdResist)
                yield return (Label("coldresist"), null);
            if (ctx.CanFly)
                yield return (Label("flight"), null);
            if (ctx.NoGravity)
                yield return (Label("nogravity"), null);
            if (ctx.CanClimbAnywhere)
                yield return (Label("climb"), null);
            if (ctx.DisableClimbing)
                yield return (Label("noclimb"), null);
            if (ctx.NoFallDamage)
                yield return (Label("nofalldamage"), null);
            if (ctx.FallDamageReduction > 0)
                yield return (Label("fall"), null);
            if (Math.Abs(ctx.KnockbackResistance) > float.Epsilon)
                yield return (Label("knockbackresist"), null);
            if (Math.Abs(ctx.ClimbTouchDistance) > float.Epsilon)
                yield return (Label("climbreach"), null);
            if (Math.Abs(ctx.Weight) > float.Epsilon)
                yield return (Label("weight"), null);
            if (ctx.Respawn)
                yield return (Label("respawn"), null);
            if (ctx.Reshape)
                yield return (Label("reshape"), null);
            if (Math.Abs(ctx.RetainedNutrition) > float.Epsilon)
                yield return (Label("nutrition"), null);
            if (Math.Abs(ctx.TemporalStabilityGain) > float.Epsilon)
                yield return (Label("temporalstability"), null);

            float size = ctx.SizeChange + ctx.SizeOffset;
            if (size > float.Epsilon)
                yield return (Label("grow"), null);
            else if (size < -float.Epsilon)
                yield return (Label("shrink"), null);
        }

        // Tooltip lines: each effect with its amount, then purge and duration.
        public static IEnumerable<string> Lines(string effectId, EffectContext ctx)
        {
            foreach ((string label, string amount) in Parts(effectId, ctx))
                yield return amount == null ? label : $"{label}: {amount}";

            if (ctx.ResetsEffects)
                yield return Lang.Get("effectlib:purge");

            if (ctx.IsEndless)
                yield return Lang.Get("effectlib:duration-endless");
            else if (ctx.Duration > 0)
                yield return Lang.Get("effectlib:duration", ctx.Duration);
        }

        // A plain list of what the effect does, e.g. "Cold resistance, Glowing aura" - null if it
        // does nothing describable.
        public static string Summary(string effectId, EffectContext ctx)
        {
            if (ctx == null)
                return null;

            string summary = string.Join(", ", Parts(effectId, ctx).Select(part => part.Label));
            return summary.Length > 0 ? summary : null;
        }
    }
}
