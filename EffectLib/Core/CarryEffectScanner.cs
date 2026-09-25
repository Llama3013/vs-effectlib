using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace EffectLib
{
    /// <summary>
    /// Periodically re-evaluates every <see cref="CollectibleBehaviorEffectCarry"/>-bearing item a
    /// player is carrying and flips its effect on/off via <see cref="EffectManager.TryApply"/> /
    /// <see cref="EffectManager.RemoveEffect"/> as the item enters/leaves its declared activation
    /// tier (and, for toggle items, its on/off state), plus drains durability over time from
    /// whichever items are actually granting an effect. Called from
    /// <see cref="EntityBehaviorPlayerEffects"/>'s throttled game tick.
    /// </summary>
    public static class CarryEffectScanner
    {
        public static void Scan(EntityPlayer player, EffectManager manager, HashSet<string> carryActiveIds)
        {
            if (player?.Player == null || manager == null)
                return;

            carryActiveIds.RemoveWhere(id => !manager.IsActive(id));

            HashSet<string> qualifying = [.. EnumerateQualifying(player).Select(q => q.Carry.EffectId)];

            foreach (string id in qualifying)
            {
                if (carryActiveIds.Contains(id))
                    continue;

                EffectContext ctx = EffectRegistry.Build(id, 1f);
                if (ctx == null)
                    continue;

                ctx.Duration = EffectContext.EndlessDuration;

                if (UtilityEffects.GetBlockReason(player, ctx) != null)
                    continue;

                if (manager.IsGroupBlocked(ctx.ExclusivityGroup, id))
                    continue;

                if (ctx.ResetsEffects)
                    manager.PurgeFor(id, ctx);

                string name = EffectLang.Name(id);
                if (manager.TryApply(id, ctx, name))
                {
                    carryActiveIds.Add(id);
                    EffectLang.SendGained(player, id, name);
                }
            }

            foreach (string id in carryActiveIds.Where(id => !qualifying.Contains(id)).ToList())
            {
                manager.RemoveEffect(id);
                carryActiveIds.Remove(id);
            }
        }

        public static void DrainDurability(EntityPlayer player, EffectManager manager, float elapsedSec)
        {
            if (player?.Player == null || manager == null || elapsedSec <= 0f)
                return;

            foreach ((CollectibleBehaviorEffectCarry carry, ItemSlot slot) in EnumerateQualifying(player))
            {
                if (carry.DurabilityDrainIntervalSec <= 0f || carry.DurabilityDrainAmount <= 0)
                    continue;

                if (!manager.IsActive(carry.EffectId))
                    continue;

                float expected =
                    elapsedSec / carry.DurabilityDrainIntervalSec * carry.DurabilityDrainAmount;
                int amount = GameMath.RoundRandom(player.World.Rand, expected);
                if (amount <= 0)
                    continue;

                slot.Itemstack.Collectible.DamageItem(
                    player.World,
                    player,
                    slot,
                    amount,
                    destroyOnZeroDurability: false
                );
            }
        }

        private static IEnumerable<(CollectibleBehaviorEffectCarry Carry, ItemSlot Slot)> EnumerateQualifying(
            EntityPlayer player
        )
        {
            IPlayerInventoryManager inventoryManager = player.Player.InventoryManager;
            IInventory hotbar = inventoryManager.GetHotbarInventory();
            IInventory backpack = inventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);

            ItemSlot rightHand = player.RightHandItemSlot;
            ItemSlot leftHand = player.LeftHandItemSlot;

            if (hotbar != null)
                foreach (ItemSlot slot in hotbar)
                    foreach (var q in QualifiersIn(slot, isHotbar: true, rightHand, leftHand))
                        yield return q;

            if (backpack != null)
                foreach (ItemSlot slot in backpack)
                    foreach (var q in QualifiersIn(slot, isHotbar: false, rightHand, leftHand))
                        yield return q;
        }

        private static IEnumerable<(CollectibleBehaviorEffectCarry, ItemSlot)> QualifiersIn(
            ItemSlot slot,
            bool isHotbar,
            ItemSlot rightHand,
            ItemSlot leftHand
        )
        {
            CollectibleObject collectible = slot?.Itemstack?.Collectible;
            if (collectible == null)
                yield break;

            foreach (
                CollectibleBehaviorEffectCarry carry in collectible.CollectibleBehaviors.OfType<CollectibleBehaviorEffectCarry>()
            )
            {
                if (carry.EffectId == null)
                    continue;

                if (!carry.AllowWhenBroken && IsBroken(collectible, slot.Itemstack))
                    continue;

                if (!carry.IsToggledOn(slot))
                    continue;

                bool qualifies = carry.Activation switch
                {
                    EnumCarryActivation.Inventory => true,
                    EnumCarryActivation.Hotbar => isHotbar,
                    EnumCarryActivation.Held => IsHeld(collectible, carry.Hand, rightHand, leftHand),
                    _ => false,
                };

                if (qualifies)
                    yield return (carry, slot);
            }
        }

        // Items with no durability at all (maxDurability <= 1) are never "broken".
        private static bool IsBroken(CollectibleObject collectible, ItemStack stack) =>
            collectible.GetMaxDurability(stack) > 1 && collectible.GetRemainingDurability(stack) <= 0;

        private static bool IsHeld(
            CollectibleObject collectible,
            EnumCarryHand hand,
            ItemSlot rightHand,
            ItemSlot leftHand
        )
        {
            bool isRight = rightHand?.Itemstack?.Collectible == collectible;
            bool isLeft = leftHand?.Itemstack?.Collectible == collectible;

            return hand switch
            {
                EnumCarryHand.Main => isRight,
                EnumCarryHand.Off => isLeft,
                EnumCarryHand.Both => isRight && isLeft,
                _ => isRight || isLeft, // Either
            };
        }
    }
}
