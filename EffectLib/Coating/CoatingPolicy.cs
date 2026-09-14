using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

#pragma warning disable IDE0130
namespace EffectLib
#pragma warning restore IDE0130
{
    public sealed class CoatingConfig
    {
        public System.Func<bool> AllowCoating { get; init; }

        public System.Func<int> MaxCharges { get; init; }

        public System.Func<float> EffectMultiplier { get; init; }

        public System.Func<CollectibleObject, bool> IsCoatableWeapon { get; init; }

        public System.Func<CollectibleObject, bool> IsCoatableProjectile { get; init; }

        public System.Func<string, bool> IsEffectCoatable { get; init; }

        public System.Func<ItemStack, (string EffectId, float PotencyMul)?> ResolveLiquidEffect { get; init; }

        public Action<string, Entity, float> ApplySideEffects { get; init; }

        public System.Func<string, EntityPlayer, EffectContext, string> GetBlockReason { get; init; }

        public System.Func<bool> AllowBarrelCoating { get; init; }

        public System.Func<float> BarrelConsumeLitres { get; init; }

        public System.Func<float> BarrelCheckLitres { get; init; }
    }

    public static class CoatingPolicy
    {
        private static CoatingConfig config = new();
        private static ICoreAPI api;
        private static TagSet coatableWeaponTagSet;
        private static bool coatableWeaponTagSetBuilt;

        public static void Configure(CoatingConfig hooks) => config = hooks ?? new();

        public static void Init(ICoreAPI coreApi) => api = coreApi;

        public static void InvalidateCaches() => coatableWeaponTagSetBuilt = false;

        public static bool AllowCoating() => config.AllowCoating?.Invoke() ?? true;

        public static int MaxCharges() => config.MaxCharges?.Invoke() ?? 5;

        public static float EffectMultiplier() => config.EffectMultiplier?.Invoke() ?? 1f;

        public static bool IsCoatableWeapon(CollectibleObject col) =>
            config.IsCoatableWeapon?.Invoke(col) ?? DefaultIsCoatableWeapon(col);

        public static bool IsCoatableProjectile(CollectibleObject col) =>
            config.IsCoatableProjectile?.Invoke(col) ?? DefaultIsCoatableProjectile(col);

        public static bool IsEffectCoatable(string effectId) =>
            config.IsEffectCoatable?.Invoke(effectId) ?? true;

        public static bool AllowBarrelCoating() => config.AllowBarrelCoating?.Invoke() ?? true;

        public static float BarrelConsumeLitres() => config.BarrelConsumeLitres?.Invoke() ?? 0.25f;

        public static float BarrelCheckLitres() => config.BarrelCheckLitres?.Invoke() ?? 0.24f;

        public static (string EffectId, float PotencyMul)? ResolveLiquidEffect(ItemStack stack) =>
            (config.ResolveLiquidEffect ?? DefaultResolveLiquidEffect)(stack);

        public static void ApplySideEffects(string effectId, Entity target, float multiplier) =>
            config.ApplySideEffects?.Invoke(effectId, target, multiplier);

        public static string GetBlockReason(string effectId, EntityPlayer player, EffectContext ctx) =>
            config.GetBlockReason?.Invoke(effectId, player, ctx);

        // Weapons are matched by vanilla item/block tags
        // (default "weapon-melee"), since arrows have no reliable tag as of 
        // VS 1.22.7 they're matched by wildcard item code instead (default "*arrow*").
        private static bool DefaultIsCoatableWeapon(CollectibleObject col) =>
            col?.Tags != null
            && TryGetCoatableWeaponTagSet(out TagSet tagSet)
            && col.Tags.Overlaps(tagSet);

        private static bool TryGetCoatableWeaponTagSet(out TagSet tagSet)
        {
            if (!coatableWeaponTagSetBuilt && api != null)
            {
                List<string> tagList =
                [
                    .. EffectLibConfig.Loaded.CoatableWeaponTags
                        .Split(',')
                        .Select(t => t.Trim())
                        .Where(t => t.Length > 0),
                ];
                api.CollectibleTagRegistry.TryCreateTagSet(out coatableWeaponTagSet, tagList);
                coatableWeaponTagSetBuilt = true;
            }
            tagSet = coatableWeaponTagSet;
            return coatableWeaponTagSetBuilt;
        }

        private static bool DefaultIsCoatableProjectile(CollectibleObject col)
        {
            if (col?.Code == null)
                return false;

            string[] codes =
            [
                .. EffectLibConfig.Loaded.CoatableProjectilesCodes
                    .Split(',')
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0),
            ];
            return codes.Length > 0 && WildcardUtil.Match(codes, col.Code.ToString());
        }

        private static (string EffectId, float PotencyMul)? DefaultResolveLiquidEffect(ItemStack stack)
        {
            CollectibleObject content = stack?.Collectible;
            JsonObject def = content?.Attributes?["effectinfo"];
            if (def?.Exists != true)
                return null;

            string effectId = def["effectId"].AsString()?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(effectId))
                return null;

            if (!EffectRegistry.IsRegistered(effectId))
                JsonEffectDefinition.RegisterFrom(effectId, content.Code.Domain, def, content.Code);

            return (effectId, 1f);
        }
    }
}
