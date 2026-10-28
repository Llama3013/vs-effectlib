using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Config;

namespace EffectLib
{
    /// <summary>
    /// Describes what an effect does, from its built context - for item tooltips, the effect HUD,
    /// and as the effect's name when no lang entry names its id (see <see cref="EffectLang.NameFor"/>).
    /// </summary>
    public static class EffectDescription
    {
        // Everything the effect does, in display order: a label, plus its amount where it has one.
        private static IEnumerable<(string Label, string Amount)> Parts(EffectContext ctx, bool withStats)
        {
            static string Label(string key) => Lang.GetIfExists("effectlib:" + key) ?? key;

            if (withStats)
                foreach (KeyValuePair<string, float> stat in ctx.StatModifiers)
                    yield return (Label(stat.Key), Percent(stat.Value));

            if (Math.Abs(ctx.Health) > float.Epsilon)
                yield return ctx.TickSec > 0
                    ? (Label(ctx.Health < 0 ? "poison" : "regen"), $"{ctx.Health:+0.#;-0.#} HP / {ctx.TickSec:0.##}s")
                    : (Label("health"), ctx.Health.ToString("+0.#;-0.#"));
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
                yield return (Label("fall"), Percent(Math.Min(ctx.FallDamageReduction, 1f)));
            if (Math.Abs(ctx.KnockbackResistance) > float.Epsilon)
                yield return (Label("knockbackresist"), Percent(ctx.KnockbackResistance));
            if (Math.Abs(ctx.ClimbTouchDistance) > float.Epsilon)
                yield return (Label("climbreach"), ctx.ClimbTouchDistance.ToString("+0.##;-0.##"));
            if (Math.Abs(ctx.Weight) > float.Epsilon)
                yield return (Label("weight"), $"{ctx.Weight:+0.#;-0.#}kg");
            if (ctx.Respawn)
                yield return (Label("respawn"), null);
            if (ctx.Reshape)
                yield return (Label("reshape"), null);
            if (Math.Abs(ctx.RetainedNutrition) > float.Epsilon)
                yield return (Label("nutrition"), $"{ctx.RetainedNutrition * 100:0.#}%");
            if (Math.Abs(ctx.TemporalStabilityGain) > float.Epsilon)
                yield return (Label("temporalstability"), Percent(ctx.TemporalStabilityGain));

            if (Math.Abs(ctx.SizeChange) > float.Epsilon)
                yield return (
                    Label(ctx.SizeChange > 0 ? "grow" : "shrink"),
                    Lang.Get("effectlib:size-blocks", ctx.SizeChange.ToString("+0.##;-0.##"))
                );
        }

        private static string Percent(float fraction) => $"{fraction * 100:+0.#;-0.#}%";

        // One line per thing the effect does, e.g. "Walk speed: +10%".
        public static IEnumerable<string> EffectLines(EffectContext ctx, bool withStats = true) =>
            Parts(ctx, withStats).Select(part => part.Amount == null ? part.Label : $"{part.Label}: {part.Amount}");

        // Tooltip lines: each effect with its amount, then purge and duration.
        public static IEnumerable<string> Lines(EffectContext ctx)
        {
            foreach (string line in EffectLines(ctx))
                yield return line;

            if (ctx.ResetsEffects)
                yield return Lang.Get("effectlib:purge");

            if (ctx.IsEndless)
                yield return Lang.Get("effectlib:duration-endless");
            else if (ctx.Duration > 0)
                yield return Lang.Get("effectlib:duration", ctx.Duration);

            if (ctx.Chance < 1f)
                yield return Lang.Get("effectlib:chance", $"{ctx.Chance * 100:0.#}");

            if (ctx.Stacking == EnumEffectStacking.Intensity && ctx.MaxStacks > 1)
                yield return Lang.Get("effectlib:stacks-max", ctx.MaxStacks);
            else if (ctx.Stacking == EnumEffectStacking.Duration && ctx.Duration > 0)
                yield return ctx.MaxDurationSec > 0
                    ? Lang.Get("effectlib:stacks-duration-max", ctx.MaxDurationSec)
                    : Lang.Get("effectlib:stacks-duration");
        }

        // A plain list of what the effect does, e.g. "Cold resistance, Glowing aura" - null if it
        // does nothing describable.
        public static string Summary(EffectContext ctx)
        {
            if (ctx == null)
                return null;

            string summary = string.Join(", ", Parts(ctx, withStats: true).Select(part => part.Label));
            return summary.Length > 0 ? summary : null;
        }
    }
}
