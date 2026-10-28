using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace EffectLib
{
    public enum EnumEffectStacking
    {
        None,
        Intensity,
        Duration,
    }

    public enum EnumStackDecay
    {
        All,
        One,
    }

    public sealed class EffectContext
    {
        public const int EndlessDuration = -1;
        public const int DefaultMaxStacks = 5;

        public Dictionary<string, float> StatModifiers { get; } = [];

        public float PotencyMul { get; set; } = 1f;

        public float DurationMul { get; set; } = 1f;

        public int Duration { get; set; }

        public bool IsEndless => Duration == EndlessDuration;

        public bool ResetsEffects { get; set; }

        public bool ReplaceIfActive { get; set; }

        public string ExclusivityGroup { get; set; }

        public bool Notify { get; set; } = true;

        // The item whose definition built this context, if any - see EffectRegistry.Build.
        public AssetLocation Source { get; set; }

        public List<string> ResetDomains { get; } = [];

        public List<string> ResetEffectIds { get; } = [];

        public float Health { get; set; }

        public float TickSec { get; set; }

        public float RepeatSec { get; set; }
        public float Chance { get; set; } = 1f;
        public EnumEffectStacking Stacking { get; set; }
        public int MaxStacks { get; set; } = DefaultMaxStacks;
        public EnumStackDecay StackDecay { get; set; }
        public int MaxDurationSec { get; set; }

        public int Stacks { get; private set; } = 1;

        private StackBase stackBase;

        private sealed record StackBase(
            Dictionary<string, float> Stats,
            float Health,
            float KnockbackResistance,
            float FallDamageReduction,
            float Weight,
            float SizeChange
        );

        // Scales what the effect holds while active to n stacks of its unscaled values. One-time
        // actions (instant health, temporal stability, nutrition, a permanent size change) fire at
        // the same strength on every use, and flags, glow, climb distance and duration are left alone.
        public void SetStacks(int n)
        {
            n = Math.Max(1, n);
            stackBase ??= new StackBase(
                new Dictionary<string, float>(StatModifiers),
                Health,
                KnockbackResistance,
                FallDamageReduction,
                Weight,
                SizeChange
            );

            Stacks = n;
            foreach (KeyValuePair<string, float> stat in stackBase.Stats)
                StatModifiers[stat.Key] = stat.Value * n;
            Health = TickSec > 0 ? stackBase.Health * n : stackBase.Health;
            KnockbackResistance = stackBase.KnockbackResistance * n;
            FallDamageReduction = stackBase.FallDamageReduction * n;
            Weight = stackBase.Weight * n;
            SizeChange = PermanentSize ? stackBase.SizeChange : stackBase.SizeChange * n;
        }

        public EnumDamageType? DamageType { get; set; }

        public EnumDamageType ResolveDamageType() =>
            DamageType ?? (Health > 0 ? EnumDamageType.Heal : EnumDamageType.Poison);

        public float RetainedNutrition { get; set; }
        public float TemporalStabilityGain { get; set; }
        public bool Respawn { get; set; }
        public bool Reshape { get; set; }
        public bool BlockRecallOnVessel { get; set; } = true;
        public bool BlockRecallOnMount { get; set; }
        public bool BlockReshapeReentry { get; set; } = true;
        public float SizeChange { get; set; }

        // Keeps the size change until death or a reset instead of wearing off with the effect.
        public bool SizePermanent { get; set; }

        // An instant effect has no duration to time its size change by, so it always lasts.
        public bool PermanentSize => SizePermanent || Duration == 0;

        public float SizeMinHeight { get; set; }
        public float SizeMaxHeight { get; set; }

        private int glowStrength;
        public int GlowStrength
        {
            get => glowStrength;
            set => glowStrength = Math.Clamp(value, 0, GlowPatch.MaxGlowStrength);
        }
        public bool WaterBreathe { get; set; }
        public bool ColdResist { get; set; }
        public float FallDamageReduction { get; set; }
        public bool CanClimbAnywhere { get; set; }
        public bool CanFly { get; set; }
        public float KnockbackResistance { get; set; }
        public bool NoFallDamage { get; set; }
        public bool DisableClimbing { get; set; }
        public float ClimbTouchDistance { get; set; }
        public float Weight { get; set; }
        public bool NoGravity { get; set; }

        public void AddStat(string key, float baseValue)
        {
            if (Math.Abs(baseValue) <= float.Epsilon)
                return;
            StatModifiers[key] = StatModifiers.GetValueOrDefault(key) + baseValue * PotencyMul;
        }

        public void SetHealth(float healthWithoutMul)
        {
            Health = healthWithoutMul * PotencyMul;
        }
    }
}
