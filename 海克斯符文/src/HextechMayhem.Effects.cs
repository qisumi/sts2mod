using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
    private async Task RunGroupedPlayerDebuffBurst(Func<Task> action)
    {
        bool wasHandlingGroupedPlayerDebuffs = _combatTracking.HandlingGroupedPlayerDebuffs;
        if (!wasHandlingGroupedPlayerDebuffs)
        {
            _combatTracking.HandlingGroupedPlayerDebuffs = true;
            _combatTracking.GroupedPlayerDebuffProcKeys.Clear();
        }

        try
        {
            await action();
        }
        finally
        {
            if (!wasHandlingGroupedPlayerDebuffs)
            {
                _combatTracking.GroupedPlayerDebuffProcKeys.Clear();
                _combatTracking.HandlingGroupedPlayerDebuffs = false;
            }
        }
    }

    private static decimal GetMonsterProteinShakeSustainMultiplier(Creature creature)
    {
        decimal bonusPercent = Math.Min(100m, Math.Floor(creature.MaxHp / 5m));
        return 1m + bonusPercent / 100m;
    }

    private async Task NormalizeEnemyPainfulStabsPowers(HextechCombatState combatState)
    {
        if (!HasActiveMonsterHex(MonsterHexKind.GetExcited))
        {
            return;
        }

        foreach (Creature enemy in combatState.Enemies.ToList())
        {
            if (enemy.CombatState != combatState)
            {
                continue;
            }

            PainfulStabsPower? legacyPower = enemy.GetPower<PainfulStabsPower>();
            if (legacyPower != null && enemy.IsDead)
            {
                await PowerCmd.Remove(legacyPower);
            }

            RemoveRetainedDeadEnemyIfNeeded(combatState, enemy);
        }
    }

    private static void RemoveRetainedDeadEnemyIfNeeded(HextechCombatState combatState, Creature enemy)
    {
        if (enemy.Side != CombatSide.Enemy
            || enemy.IsAlive
            || !combatState.Enemies.Contains(enemy)
            || !Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, enemy))
        {
            return;
        }

        var node = NCombatRoom.Instance?.GetCreatureNode(enemy);
        if (node != null)
        {
            NCombatRoom.Instance?.RemoveCreatureNode(node);
        }

        CombatManager.Instance.RemoveCreature(enemy);
        combatState.RemoveCreature(enemy);
        Log.Info($"[{ModInfo.Id}][Mayhem] Removed retained dead enemy after unsafe PainfulStabs cleanup: id={enemy.CombatId?.ToString() ?? "none"} model={enemy.ModelId.Entry}");
    }

    private async Task TryApplyServantMasterIllusion(Creature creature, Creature? applier, CardModel? cardSource)
    {
        if (_combatTracking.HandlingServantMasterIllusion
            || !HasActiveMonsterHex(MonsterHexKind.ServantMaster)
            || creature.Side != CombatSide.Enemy
            || !creature.IsAlive
            || creature.CombatState?.RunState != RunState
            || !creature.HasPower<MinionPower>()
            || creature.HasPower<IllusionPower>())
        {
            return;
        }

        try
        {
            _combatTracking.HandlingServantMasterIllusion = true;
            await PowerCmd.Apply<IllusionPower>(creature, 1m, applier ?? creature, cardSource);
        }
        finally
        {
            _combatTracking.HandlingServantMasterIllusion = false;
        }
    }

    private static void DowngradePlayerCombatCards(HextechCombatState combatState)
    {
        foreach (CardModel card in combatState.Players
            .SelectMany(static player => player.PlayerCombatState?.AllCards ?? Array.Empty<CardModel>())
            .Where(static card => card.IsUpgraded)
            .ToList())
        {
            CardCmd.Downgrade(card);
        }
    }

    private static IReadOnlyList<Creature> GetAliveEnemies(HextechCombatState combatState)
    {
        return combatState.Enemies.Where(static creature => creature.IsAlive).ToList();
    }

    private static IReadOnlyList<Creature> GetAlivePlayerSideCreatures(HextechCombatState combatState)
    {
        return combatState.PlayerCreatures.Where(static creature => creature.IsAlive).ToList();
    }

    private static bool TryConsumeLimitedProc(Dictionary<uint, int> counts, Creature creature, int maxPerTurn)
    {
        if (creature.CombatId == null)
        {
            return false;
        }

        uint combatId = creature.CombatId.Value;
        int current = counts.GetValueOrDefault(combatId, 0);
        if (current >= maxPerTurn)
        {
            return false;
        }

        counts[combatId] = current + 1;
        return true;
    }

    private static bool TryGetMonsterDebuffTrigger(PowerModel power, decimal amount, Creature? applier, out Creature? target, out Creature? source)
    {
        target = power.Owner;
        source = applier;
        return amount > 0m
            && target?.Side == CombatSide.Player
            && source?.Side == CombatSide.Enemy
            && power.GetTypeForAmount(amount) == PowerType.Debuff
            && power is not ITemporaryPower;
    }

    private bool ShouldSuppressMonsterDebuffDuplicate(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        string powerTypeName = power.GetType().FullName ?? power.GetType().Name;
        if (_combatTracking.HandlingGroupedPlayerDebuffs)
        {
            string groupedKey = $"{applier?.CombatId?.ToString() ?? "none"}:{powerTypeName}:{amount}";
            return !_combatTracking.GroupedPlayerDebuffProcKeys.Add(groupedKey);
        }

        if (cardSource == null || applier?.CombatId == null)
        {
            return false;
        }

        string actionKey = $"{applier.CombatId.Value}:{HextechStableRandom.InstanceHash(cardSource)}:{powerTypeName}:{amount}";
        return !_combatTracking.MonsterDebuffActionProcKeysThisTurn.Add(actionKey);
    }

    private bool ShouldSuppressDuplicateEnemyThresholdTrigger(Creature target, DamageResult result, Creature? dealer, CardModel? cardSource)
    {
        string key = string.Join(":",
            target.CombatId?.ToString() ?? "none",
            target.CurrentHp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            result.UnblockedDamage.ToString(System.Globalization.CultureInfo.InvariantCulture),
            dealer?.CombatId?.ToString() ?? "none",
            HextechStableRandom.InstanceKey(cardSource));
        bool suppress = key == _combatTracking.LastEnemyThresholdTriggerKey;
        _combatTracking.LastEnemyThresholdTriggerKey = key;
        return suppress;
    }

    private static bool TryGetMonsterSelfBuffTrigger(PowerModel power, decimal amount, Creature? applier, out Creature? source)
    {
        source = null;
        Creature? owner = power.Owner;
        if (amount <= 0m
            || owner?.Side != CombatSide.Enemy
            || power.GetTypeForAmount(amount) != PowerType.Buff
            || HextechMonsterInteractionPolicy.ShouldIgnoreMonsterSelfBuff(power)
            || power is ITemporaryPower
            || power is PlatingPower
            || power is BufferPower
            || (applier != null && applier != owner))
        {
            return false;
        }

        source = owner;
        return true;
    }

	private static bool TryMarkPersistentHexApplied(HashSet<uint> appliedSet, Creature creature, bool forceReapply = false)
	{
		if (creature.CombatId == null)
		{
			return false;
		}

		bool firstApplication = appliedSet.Add(creature.CombatId.Value);
		return forceReapply || firstApplication;
	}

    private bool TrackPlayerAttackCardPlayedThisTurn(CardPlay cardPlay)
    {
        if (!cardPlay.IsFirstInSeries
            || cardPlay.IsAutoPlay
            || cardPlay.Card.Type != CardType.Attack
            || cardPlay.Card.Owner?.Creature.Side != CombatSide.Player)
        {
            return false;
        }

        ulong playerId = cardPlay.Card.Owner.NetId;
        _combatTracking.PlayerAttackCardsPlayedThisTurn[playerId] = _combatTracking.PlayerAttackCardsPlayedThisTurn.GetValueOrDefault(playerId, 0) + 1;
        return true;
    }

    private bool HasEnemyAttackCostDoublingHex()
    {
        return HasActiveMonsterHex(MonsterHexKind.LightEmUp)
            || HasActiveMonsterHex(MonsterHexKind.TwiceThrice);
    }

    private void RefreshPlayerAttackCostDoublingPreviews(IEnumerable<Creature> playerCreatures)
    {
        if (!HasEnemyAttackCostDoublingHex())
        {
            return;
        }

        foreach (Creature playerCreature in playerCreatures)
        {
            Player? player = playerCreature.Player;
            if (player == null
                || playerCreature.CombatState?.RunState != RunState)
            {
                continue;
            }

            foreach (CardModel card in PileType.Hand.GetPile(player).Cards)
            {
                if (card.Type == CardType.Attack && !card.EnergyCost.CostsX)
                {
                    card.InvokeEnergyCostChanged();
                }
            }
        }
    }

    private bool TryConsumeEnemyEightPennyGate(CardModel card, bool isAutoPlay)
    {
        Player? owner = card.Owner;
        if (!HasActiveMonsterHex(MonsterHexKind.EightPennyGate)
            || isAutoPlay
            || card.Type == CardType.Power
            || owner?.Creature.Side != CombatSide.Player
            || owner.Creature.CombatState?.RunState != RunState)
        {
            return false;
        }

        ulong playerId = owner.NetId;
        return _combatTracking.EightPennyGatePlayersTriggeredThisTurn.Add(playerId);
    }

    private int GetPlayerAttacksPlayedThisTurn(CardModel card)
    {
        if (card.Owner == null)
        {
            return 0;
        }

        return _combatTracking.PlayerAttackCardsPlayedThisTurn.GetValueOrDefault(card.Owner.NetId, 0);
    }

    public decimal ModifyEnemyHealAmount(Creature creature, decimal amount)
    {
        if (creature.Side != CombatSide.Enemy)
        {
            return amount;
        }

        if (HasActiveMonsterHex(MonsterHexKind.Goliath))
        {
            amount *= 1.2m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.FirstAidKit))
        {
            amount *= 1.25m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.ProteinShake))
        {
            amount *= GetMonsterProteinShakeSustainMultiplier(creature);
        }

        if (HasActiveMonsterHex(MonsterHexKind.GoldenSpatula))
        {
            amount *= 0.5m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.GlassCannon))
        {
            int healCap = (int)Math.Floor(creature.MaxHp * 0.7m);
            amount = Math.Min(amount, Math.Max(0, healCap - creature.CurrentHp));
        }

        return amount;
    }
}
