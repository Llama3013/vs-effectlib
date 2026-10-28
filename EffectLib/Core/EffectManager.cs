using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace EffectLib
{
    public static class EffectAttr
    {
        public const string WaterBreathe = "effectlib:waterBreathe";
        public const string ColdResist = "effectlib:coldResist";
        public const string CanFly = "effectlib:canFly";
        public const string CanClimb = "effectlib:canClimb";
        public const string NoClimb = "effectlib:noClimb";
        public const string ClimbReach = "effectlib:climbReach";
        public const string GlowStrength = "effectlib:glowStrength";
        public const string NoGravity = "effectlib:noGravity";
    }

    public sealed class EffectManager(EntityPlayer entity)
    {
        public const string PersistKey = "effectlib:effects";

        private readonly EntityPlayer entity = entity;
        private readonly ICoreAPI api = entity.Api;
        private readonly Dictionary<string, ActiveEffect> active = new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> pendingInstant = new(StringComparer.OrdinalIgnoreCase);

        private float? baselineFallDamageMultiplier;
        private bool? baselineFallDamage;
        private bool? baselineCanClimbAnywhere;
        private bool? baselineCanClimb;
        private bool? baselineFreeMove;
        private EnumHabitat? baselineHabitat;
        private float? baselineKnockbackResistance;
        private float? baselineClimbTouchDistance;
        private float? baselineWeight;

        public bool IsActive(string id) => active.ContainsKey(id);

        public bool IsGroupBlocked(string group, string exceptId) =>
            group != null
            && active.Any(pair => pair.Key != exceptId && pair.Value.Effect.Context.ExclusivityGroup == group);

        public AssetLocation SourceOf(string id) =>
            active.TryGetValue(id, out ActiveEffect activeEffect) ? activeEffect.Effect.Context.Source : null;

        public bool HasAnyActive => active.Count > 0;

        public IReadOnlyCollection<string> ActiveIds => active.Keys;

        public List<ActiveEffectInfo> GetActiveEffects()
        {
            long nowMs = entity.World.ElapsedMilliseconds;

            return
            [
                .. active.Select(pair => new ActiveEffectInfo(
                    pair.Key,
                    pair.Value.DisplayName,
                    pair.Value.Effect.Context.IsEndless ? 0 : RemainingSec(pair.Value, nowMs),
                    pair.Value.Effect.Context.PotencyMul,
                    pair.Value.Effect.Context.IsEndless,
                    pair.Value.Effect.Context.Stacks
                )),
            ];
        }

        // Whether TryApply would accept this id right now - false while it's active and can't refresh.
        public bool CanApply(string id) => !(IsActive(id) || pendingInstant.Contains(id)) || CanRefresh(id);

        public bool CanRefresh(string id)
        {
            if (!active.TryGetValue(id, out ActiveEffect activeEffect))
                return false;

            if (activeEffect.Effect.Context.ReplaceIfActive || StacksOn(activeEffect.Effect.Context))
                return true;

            return EffectPolicy.IsAllowed(EffectCapability.Refresh)
                && !TicksHealth(activeEffect.Effect.Context);
        }

        public bool TryApply(string id, EffectContext ctx, string name, bool resume = false)
        {
            try
            {
                if (IsActive(id) || pendingInstant.Contains(id))
                {
                    if (!CanRefresh(id))
                    {
                        api.Logger.Debug("[EffectLib] Cannot apply effect {0}, it is already active.", id);
                        return false;
                    }

                    if (active.TryGetValue(id, out ActiveEffect previous) && StacksOn(ctx))
                        StackOnto(previous, ctx);

                    RemoveEffect(id, false);
                }

                if (ctx.ExclusivityGroup != null)
                {
                    foreach (
                        string otherId in active.Keys
                            .Where(otherId => active[otherId].Effect.Context.ExclusivityGroup == ctx.ExclusivityGroup)
                            .ToList()
                    )
                        RemoveEffect(otherId);
                }

                if (ctx.Duration == 0 && ctx.RepeatSec > 0f)
                    ctx.Duration = EffectContext.EndlessDuration;

                AppliedEffect effect = new(id, ctx);
                effect.Apply(entity, resume);

                if (!resume)
                    EffectHandlers.Applied(entity, id, ctx, api.Logger);

                if (ctx.Duration == 0)
                {
                    pendingInstant.Add(id);
                    entity.World.RegisterCallback(_ => pendingInstant.Remove(id), 500);
                    return true;
                }

                (long tickListenerId, long expiryCallbackId) = RegisterTimers(id, effect);

                active[id] = new ActiveEffect(effect, tickListenerId, expiryCallbackId, name)
                {
                    ApplyMs = entity.World.ElapsedMilliseconds,
                };
                RefreshEffectState();
                SaveEffectRecord(id, ctx, name);

                return true;
            }
            catch (Exception err)
            {
                api.Logger.Error("[EffectLib] Effect {0} could not be applied. An error occurred", id);
                api.Logger.Error(err);
                return false;
            }
        }

        private (long TickListenerId, long ExpiryCallbackId) RegisterTimers(string id, AppliedEffect effect)
        {
            EffectContext ctx = effect.Context;

            long tickListenerId = 0;
            if (ctx.IsEndless && TicksHealth(ctx))
                tickListenerId = entity.World.RegisterGameTickListener(
                    _ => effect.ApplyHealthTick(entity),
                    (int)Math.Max(1, ctx.TickSec * 1000)
                );
            else if (ctx.RepeatSec > 0)
                tickListenerId = entity.World.RegisterGameTickListener(
                    _ => RepeatTick(id),
                    (int)Math.Max(1, ctx.RepeatSec * 1000)
                );

            long expiryCallbackId = ctx.IsEndless
                ? 0
                : entity.World.RegisterCallback(
                    _ => OnExpired(id),
                    (int)Math.Min(int.MaxValue, ctx.Duration * 1000L)
                );

            return (tickListenerId, expiryCallbackId);
        }

        internal static bool StacksOn(EffectContext ctx) =>
            ctx.Stacking != EnumEffectStacking.None && EffectPolicy.IsAllowed(EffectCapability.Stack);

        // Carries the running effect's stacks or time left over into the incoming context.
        private void StackOnto(ActiveEffect previous, EffectContext ctx)
        {
            EffectContext prevCtx = previous.Effect.Context;
            switch (ctx.Stacking)
            {
                case EnumEffectStacking.Intensity:
                    ctx.SetStacks(Math.Min(prevCtx.Stacks + 1, ctx.MaxStacks));
                    break;

                case EnumEffectStacking.Duration:
                    if (ctx.Duration <= 0 || prevCtx.IsEndless)
                        break;
                    int total = RemainingSec(previous, entity.World.ElapsedMilliseconds) + ctx.Duration;
                    ctx.Duration = ctx.MaxDurationSec > 0 ? Math.Min(total, ctx.MaxDurationSec) : total;
                    break;
            }
        }

        private static int RemainingSec(ActiveEffect activeEffect, long nowMs) =>
            Math.Max(
                0,
                activeEffect.Effect.Context.Duration - (int)((nowMs - activeEffect.ApplyMs) / 1000)
            );

        private void OnExpired(string id)
        {
            if (
                active.TryGetValue(id, out ActiveEffect activeEffect)
                && activeEffect.Effect.Context is { Stacking: EnumEffectStacking.Intensity, StackDecay: EnumStackDecay.One, Stacks: > 1 }
                && EffectPolicy.IsAllowed(EffectCapability.Stack)
            )
                DecayStack(id, activeEffect);
            else
                RemoveEffect(id, notify: true, expired: true);
        }

        // Drops one stack in place and restarts the timer - no handlers, and no instant health re-fire.
        private void DecayStack(string id, ActiveEffect activeEffect)
        {
            AppliedEffect effect = activeEffect.Effect;
            EffectContext ctx = effect.Context;

            UnregisterTimers(activeEffect);
            effect.Remove(entity, stopDamageOverTime: true);
            ctx.SetStacks(ctx.Stacks - 1);
            effect.Apply(entity, instantHealth: false);

            (long tickListenerId, long expiryCallbackId) = RegisterTimers(id, effect);
            active[id] = activeEffect with
            {
                TickListenerId = tickListenerId,
                ExpiryCallbackId = expiryCallbackId,
                ApplyMs = entity.World.ElapsedMilliseconds,
            };
            RefreshEffectState();
            SaveEffectRecord(id, ctx, activeEffect.DisplayName);

            if (ctx.Notify)
                EffectLang.SendFor(
                    entity.Player as IServerPlayer,
                    EffectRegistry.DomainOf(id),
                    "effect-stack",
                    activeEffect.DisplayName,
                    ctx.Stacks
                );
        }

        public void RemoveEffect(string id, bool notify = true) => RemoveEffect(id, notify, expired: false);

        private void RemoveEffect(string id, bool notify, bool expired)
        {
            if (!active.TryGetValue(id, out ActiveEffect activeEffect))
                return;

            string key = activeEffect.Effect.EffectId;
            EffectContext ctx = activeEffect.Effect.Context;

            if (notify && ctx.Notify)
                EffectLang.SendFor(
                    entity.Player as IServerPlayer,
                    EffectRegistry.DomainOf(key),
                    "effect-lose",
                    activeEffect.DisplayName
                );

            UnregisterTimers(activeEffect);
            activeEffect.Effect.Remove(entity, stopDamageOverTime: !expired);

            active.Remove(key);
            pendingInstant.Remove(key);

            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(PersistKey);
            if (tree != null)
            {
                tree.RemoveAttribute(key);
                entity.WatchedAttributes.MarkPathDirty(PersistKey);
            }

            EffectHandlers.Removed(entity, key, ctx, api.Logger);

            RefreshEffectState();
        }

        private void RepeatTick(string id)
        {
            if (active.TryGetValue(id, out ActiveEffect activeEffect))
                EffectHandlers.Applied(entity, id, activeEffect.Effect.Context, api.Logger);
        }

        private void UnregisterTimers(ActiveEffect activeEffect)
        {
            if (activeEffect.TickListenerId != 0)
                entity.World.UnregisterGameTickListener(activeEffect.TickListenerId);
            if (activeEffect.ExpiryCallbackId != 0)
                entity.World.UnregisterCallback(activeEffect.ExpiryCallbackId);
        }

        private static bool TicksHealth(EffectContext ctx) =>
            ctx.TickSec > 0 && Math.Abs(ctx.Health) > float.Epsilon;

        public void Purge(EffectPurge scope) => Purge(scope, null);

        // exceptId survives the purge even when the scope covers it.
        public void Purge(EffectPurge scope, string exceptId)
        {
            scope ??= EffectPurge.Everything;

            foreach (
                string id in active.Keys
                    .Where(id => scope.Covers(id) && !string.Equals(id, exceptId, StringComparison.OrdinalIgnoreCase))
                    .ToList()
            )
            {
                RemoveEffect(id);
            }

            if (scope.IsEverything && exceptId == null)
            {
                RemoveIfPresent(PersistKey);
                pendingInstant.Clear();
            }

            RefreshEffectState();
            EffectHandlers.Cleared(entity, scope, api.Logger);
        }

        public void ResetAll() => Purge(EffectPurge.Everything);

        public void PurgeFor(string effectId, EffectContext ctx)
        {
            IEnumerable<string> domains =
                ctx.ResetDomains.Count > 0
                    ? ctx.ResetDomains
                    : [EffectRegistry.DomainOf(effectId)];

            // A stacking effect's reset spares itself, so it doesn't wipe the stack it adds to.
            string except = StacksOn(ctx) && IsActive(effectId) ? effectId : null;
            Purge(new EffectPurge(domains, ctx.ResetEffectIds), except);
        }

        public void Suspend()
        {
            if (active.Count == 0)
                return;

            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(PersistKey);
            long nowMs = entity.World.ElapsedMilliseconds;

            foreach (KeyValuePair<string, ActiveEffect> pair in active)
            {
                ActiveEffect activeEffect = pair.Value;
                UnregisterTimers(activeEffect);

                int remainingSec = activeEffect.Effect.Context.IsEndless
                    ? EffectContext.EndlessDuration
                    : Math.Max(RemainingSec(activeEffect, nowMs), 1);
                tree?.GetTreeAttribute(pair.Key)?.SetInt("remainingSec", remainingSec);
            }

            if (tree != null)
                entity.WatchedAttributes.MarkPathDirty(PersistKey);

            active.Clear();

            RefreshEffectState();
        }

        public void RestoreEffects()
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute(PersistKey);
            if (tree != null)
            {
                foreach (string id in tree.Select(pair => pair.Key).ToList())
                {
                    // Carry effects are re-derived from the inventory by the next scan instead.
                    if (CarryEffectIds.Is(id))
                    {
                        tree.RemoveAttribute(id);
                        continue;
                    }

                    ITreeAttribute record = tree.GetTreeAttribute(id);
                    int remainingSec = record?.GetInt("remainingSec") ?? 0;
                    bool endless = remainingSec == EffectContext.EndlessDuration;
                    string source = record?.GetString("source");
                    EffectContext ctx =
                        remainingSec > 0 || endless
                            ? EffectRegistry.Build(
                                id,
                                record.GetFloat("strengthMul", 1f),
                                string.IsNullOrEmpty(source) ? null : new AssetLocation(source),
                                record.GetFloat("durationMul", 1f)
                            )
                            : null;

                    if (ctx == null)
                    {
                        tree.RemoveAttribute(id);
                        continue;
                    }

                    bool primitive = EffectPrimitives.IsPrimitiveId(id);

                    if (endless || ctx.IsEndless)
                    {
                        ctx.Duration = EffectContext.EndlessDuration;
                    }
                    else if (primitive)
                    {
                        ctx.Duration = remainingSec;
                    }
                    else if (ctx.Duration <= 0)
                    {
                        tree.RemoveAttribute(id);
                        continue;
                    }
                    else if (ctx.Stacking == EnumEffectStacking.Duration)
                    {
                        // Stacked time can run past the definition's own duration.
                        ctx.Duration = ctx.MaxDurationSec > 0 ? Math.Min(remainingSec, ctx.MaxDurationSec) : remainingSec;
                    }
                    else
                    {
                        ctx.Duration = Math.Min(remainingSec, ctx.Duration);
                    }

                    int stacks = record.GetInt("stacks", 1);
                    if (stacks > 1)
                        ctx.SetStacks(Math.Min(stacks, ctx.MaxStacks));

                    if (primitive)
                        RestoreRepeatShape(ctx, record);

                    if (ctx.CanFly)
                        baselineFreeMove ??= record.GetBool("origFreeMove");

                    if (!TryApply(id, ctx, record.GetString("name", ""), resume: true))
                        tree.RemoveAttribute(id);
                }

                entity.WatchedAttributes.MarkPathDirty(PersistKey);
            }

            RefreshEffectState();
            EffectHandlers.Restored(entity, api.Logger);
        }

        private void RefreshEffectState()
        {
            bool allowFly = EffectPolicy.IsAllowed(EffectCapability.Fly);
            bool allowClimb = EffectPolicy.IsAllowed(EffectCapability.Climb);
            bool allowFall = EffectPolicy.IsAllowed(EffectCapability.Fall);

            bool waterBreathe = false;
            bool coldResist = false;
            bool canFly = false;
            bool canClimb = false;
            int glowStrength = 0;
            float fallDamageReduction = 0f;
            bool noFallDamage = false;
            bool disableClimbing = false;
            bool noGravity = false;
            float knockbackResistance = 0f;
            float climbTouchDistance = 0f;
            float weight = 0f;
            float sizeOffset = 0f;
            bool allowResize = EffectPolicy.IsAllowed(EffectCapability.Resize);

            foreach (ActiveEffect activeEffect in active.Values)
            {
                EffectContext ctx = activeEffect.Effect.Context;
                waterBreathe |= ctx.WaterBreathe;
                coldResist |= ctx.ColdResist;
                canFly |= ctx.CanFly && allowFly;
                canClimb |= ctx.CanClimbAnywhere && allowClimb;
                noFallDamage |= ctx.NoFallDamage && allowFall;
                disableClimbing |= ctx.DisableClimbing && allowClimb;
                noGravity |= ctx.NoGravity && allowFly;
                if (ctx.GlowStrength > glowStrength)
                    glowStrength = ctx.GlowStrength;
                if (allowFall && ctx.FallDamageReduction > fallDamageReduction)
                    fallDamageReduction = ctx.FallDamageReduction;
                knockbackResistance += ctx.KnockbackResistance;
                if (allowClimb)
                    climbTouchDistance += ctx.ClimbTouchDistance;
                weight += ctx.Weight;
                if (allowResize && !ctx.PermanentSize)
                    sizeOffset += ctx.SizeChange;
            }

            SetEffectBool(EffectAttr.WaterBreathe, waterBreathe);
            SetEffectBool(EffectAttr.ColdResist, coldResist);
            SetEffectBool(EffectAttr.CanFly, canFly);
            SetEffectInt(EffectAttr.GlowStrength, glowStrength);

            // Player movement is simulated client-side, so these are mirrored there too - see
            // PlayerPhysicsSync.
            SetEffectBool(EffectAttr.CanClimb, canClimb);
            SetEffectBool(EffectAttr.NoClimb, disableClimbing);
            SetEffectFloat(EffectAttr.ClimbReach, climbTouchDistance);
            SetEffectBool(EffectAttr.NoGravity, noGravity);

            SyncFallDamage(fallDamageReduction);
            SyncClimbing(canClimb);
            SyncFreeMove(canFly);
            SyncNoFallDamage(noFallDamage);
            SyncDisableClimbing(disableClimbing);
            SyncNoGravity(noGravity);
            SyncOffset(
                ref baselineKnockbackResistance,
                knockbackResistance,
                () => entity.Properties.KnockbackResistance,
                value => entity.Properties.KnockbackResistance = value
            );
            SyncOffset(
                ref baselineClimbTouchDistance,
                climbTouchDistance,
                () => entity.Properties.ClimbTouchDistance,
                value => entity.Properties.ClimbTouchDistance = value
            );
            UtilityEffects.SetSizeOffset(entity, sizeOffset);
            SyncOffset(
                ref baselineWeight,
                weight,
                () => entity.Properties.Weight,
                value => entity.Properties.Weight = value
            );
        }

        private static void SyncOffset(
            ref float? baseline,
            float total,
            Func<float> read,
            Action<float> write
        )
        {
            if (Math.Abs(total) > float.Epsilon)
            {
                baseline ??= read();
                write(baseline.Value + total);
            }
            else if (baseline.HasValue)
            {
                write(baseline.Value);
                baseline = null;
            }
        }

        private void SyncFallDamage(float reduction)
        {
            if (reduction > 0f)
            {
                baselineFallDamageMultiplier ??= entity.Properties.FallDamageMultiplier;
                entity.Properties.FallDamageMultiplier = 1f - Math.Min(reduction, 1f);
            }
            else if (baselineFallDamageMultiplier.HasValue)
            {
                entity.Properties.FallDamageMultiplier = baselineFallDamageMultiplier.Value;
                baselineFallDamageMultiplier = null;
            }
        }

        private void SyncNoFallDamage(bool noFallDamage)
        {
            if (noFallDamage)
            {
                baselineFallDamage ??= entity.Properties.FallDamage;
                entity.Properties.FallDamage = false;
            }
            else if (baselineFallDamage.HasValue)
            {
                entity.Properties.FallDamage = baselineFallDamage.Value;
                baselineFallDamage = null;
            }
        }

        private void SyncClimbing(bool canClimb)
        {
            if (canClimb)
            {
                baselineCanClimbAnywhere ??= entity.Properties.CanClimbAnywhere;
                entity.Properties.CanClimbAnywhere = true;
            }
            else if (baselineCanClimbAnywhere.HasValue)
            {
                entity.Properties.CanClimbAnywhere = baselineCanClimbAnywhere.Value;
                baselineCanClimbAnywhere = null;
            }
        }

        private void SyncDisableClimbing(bool disableClimbing)
        {
            if (disableClimbing)
            {
                baselineCanClimb ??= entity.Properties.CanClimb;
                entity.Properties.CanClimb = false;
            }
            else if (baselineCanClimb.HasValue)
            {
                entity.Properties.CanClimb = baselineCanClimb.Value;
                baselineCanClimb = null;
            }
        }

        private void SyncNoGravity(bool noGravity)
        {
            if (noGravity)
            {
                baselineHabitat ??= entity.Properties.Habitat;
                entity.Properties.Habitat = EnumHabitat.Air;
            }
            else if (baselineHabitat.HasValue)
            {
                entity.Properties.Habitat = baselineHabitat.Value;
                baselineHabitat = null;
            }
        }

        private void SyncFreeMove(bool canFly)
        {
            if (entity?.Player is not IServerPlayer flyPlayer)
                return;

            bool target;

            if (canFly)
            {
                baselineFreeMove ??= flyPlayer.WorldData.FreeMove;
                target = true;
            }
            else
            {
                if (!baselineFreeMove.HasValue)
                    return;

                target = baselineFreeMove.Value;
                baselineFreeMove = null;
            }

            if (flyPlayer.WorldData.FreeMove == target)
                return;

            flyPlayer.WorldData.FreeMove = target;
            flyPlayer.BroadcastPlayerData();
        }

        private void SetEffectBool(string key, bool value)
        {
            if (!value && !entity.WatchedAttributes.HasAttribute(key))
                return;
            if (entity.WatchedAttributes.GetBool(key) != value)
                entity.WatchedAttributes.SetBool(key, value);
        }

        private void SetEffectInt(string key, int value)
        {
            if (value == 0 && !entity.WatchedAttributes.HasAttribute(key))
                return;
            if (entity.WatchedAttributes.GetInt(key) != value)
                entity.WatchedAttributes.SetInt(key, value);
        }

        private void SetEffectFloat(string key, float value)
        {
            if (Math.Abs(value) <= float.Epsilon && !entity.WatchedAttributes.HasAttribute(key))
                return;
            if (Math.Abs(entity.WatchedAttributes.GetFloat(key) - value) > float.Epsilon)
                entity.WatchedAttributes.SetFloat(key, value);
        }

        private void RemoveIfPresent(string key)
        {
            if (entity.WatchedAttributes.HasAttribute(key))
                entity.WatchedAttributes.RemoveAttribute(key);
        }

        private void SaveEffectRecord(string id, EffectContext ctx, string name)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetOrAddTreeAttribute(PersistKey);
            ITreeAttribute record = tree.GetOrAddTreeAttribute(id);
            record.SetString("name", name ?? "");
            record.SetFloat("strengthMul", ctx.PotencyMul);
            record.SetInt("stacks", ctx.Stacks);
            record.SetFloat("durationMul", ctx.DurationMul);
            record.SetInt("remainingSec", ctx.Duration);
            record.SetLong("appliedAt", entity.World.ElapsedMilliseconds);
            if (ctx.Source != null)
                record.SetString("source", ctx.Source.ToString());
            else
                record.RemoveAttribute("source");
            if (ctx.CanFly)
                record.SetBool("origFreeMove", baselineFreeMove ?? false);

            if (EffectPrimitives.IsPrimitiveId(id))
            {
                if (ctx.RepeatSec > 0f)
                    record.SetFloat("repeatSec", ctx.RepeatSec);
                if (ctx.TickSec > 0f)
                    record.SetFloat("tickSec", ctx.TickSec);
                if (ctx.DamageType.HasValue)
                    record.SetString("damageType", ctx.DamageType.Value.ToString());
            }

            entity.WatchedAttributes.MarkPathDirty(PersistKey);
        }

        private static void RestoreRepeatShape(EffectContext ctx, ITreeAttribute record)
        {
            float repeatSec = record.GetFloat("repeatSec");
            if (repeatSec > 0f)
                ctx.RepeatSec = repeatSec;

            float tickSec = record.GetFloat("tickSec");
            if (tickSec > 0f)
                ctx.TickSec = tickSec;

            if (
                record.HasAttribute("damageType")
                && Enum.TryParse(record.GetString("damageType"), true, out EnumDamageType dt)
            )
                ctx.DamageType = dt;
        }
    }

    public sealed record ActiveEffectInfo(
        string Id,
        string DisplayName,
        int RemainingSec,
        float PotencyMul,
        bool Endless = false,
        int Stacks = 1
    );

    internal sealed record ActiveEffect(
        AppliedEffect Effect,
        long TickListenerId,
        long ExpiryCallbackId,
        string DisplayName
    )
    {
        public long ApplyMs;
    }
}
