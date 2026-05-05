using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (!TryGetDamagedEnemy(target, result, out uint combatId))
		{
			return;
		}

		TrackEnemyDamageReceived(target, combatId);
		await ApplyEnemyDamageReceivedReactiveHexes(target, dealer, cardSource);

		if (!ShouldSuppressDuplicateEnemyThresholdTrigger(target, result, dealer, cardSource))
		{
			await ApplyEnemyThresholdHexes(target, combatId);
		}
	}

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (dealer?.Side != CombatSide.Enemy || dealer.CombatState?.RunState != RunState || !target.IsAlive)
		{
			return;
		}

		await ApplyEnemyDamageGivenImmediateHexes(dealer, result, target, cardSource);
		if (result.UnblockedDamage <= 0 || target.Side != CombatSide.Player)
		{
			return;
		}

		await ApplyEnemyDamageGivenPlayerHitHexes(dealer, target);
	}

	private static bool TryGetDamagedEnemy(Creature target, DamageResult result, out uint combatId)
	{
		combatId = 0;
		if (target.Side != CombatSide.Enemy || result.UnblockedDamage <= 0 || target.CombatId == null)
		{
			return false;
		}

		combatId = target.CombatId.Value;
		return true;
	}

	private void TrackEnemyDamageReceived(Creature target, uint combatId)
	{
		if (HasActiveMonsterHex(MonsterHexKind.MountainSoul))
		{
			_combatTracking.MountainSoulDamagedSinceLastTurn.Add(combatId);
		}
	}

	private async Task ApplyEnemyDamageReceivedReactiveHexes(Creature target, Creature? dealer, CardModel? cardSource)
	{
		if (HasActiveMonsterHex(MonsterHexKind.BloodPact)
			&& target.IsAlive
			&& TryConsumeLimitedProc(_combatTracking.BloodPactProcsThisTurn, target, 2))
		{
			await PowerCmd.Apply<HextechBloodPactTemporaryStrengthPower>(target, BloodPactTemporaryStrengthStacks, target, null);
		}

		if (HasActiveMonsterHex(MonsterHexKind.ClownCollege)
			&& target.IsAlive
			&& TryConsumeLimitedProc(_combatTracking.ClownCollegeProcsThisTurn, target, 1))
		{
			await HextechEnemyPowerScalingHooks.Apply<SlipperyPower>(target, ClownCollegeSlipperyStacks, target, null);
		}

		if (HasActiveMonsterHex(MonsterHexKind.MadScientist)
			&& target.IsAlive
			&& target.CombatState is HextechCombatState combatState)
		{
			Player? player = ResolveMadScientistDazedTarget(combatState, dealer, cardSource);
			if (player != null)
			{
				CardModel dazed = combatState.CreateCard<Dazed>(player);
				await HextechCardGeneration.AddGeneratedCardToCombat(
					dazed,
					PileType.Discard,
					addedByPlayer: false,
					position: CardPilePosition.Top);
			}
		}
	}

	private static Player? ResolveMadScientistDazedTarget(HextechCombatState combatState, Creature? dealer, CardModel? cardSource)
	{
		Player? cardOwner = cardSource?.Owner;
		if (cardOwner?.Creature.IsAlive == true && cardOwner.Creature.CombatState == combatState)
		{
			return cardOwner;
		}

		Player? dealerPlayer = dealer?.Player;
		if (dealerPlayer?.Creature.IsAlive == true && dealerPlayer.Creature.CombatState == combatState)
		{
			return dealerPlayer;
		}

		List<Player> alivePlayers = GetAlivePlayerSideCreatures(combatState)
			.Select(static creature => creature.Player)
			.OfType<Player>()
			.ToList();
		return alivePlayers.Count == 1 ? alivePlayers[0] : null;
	}

	private async Task ApplyEnemyThresholdHexes(Creature target, uint combatId)
	{
		if (!IsBelowEnemyHealthThreshold(target))
		{
			return;
		}

		if (HasActiveMonsterHex(MonsterHexKind.EscapePlan))
		{
			TryQueueEnemyThresholdEffect(_combatTracking.EscapePlanTriggered, _combatTracking.EscapePlanPending, combatId);
		}

		if (HasActiveMonsterHex(MonsterHexKind.Repulsor))
		{
			TryQueueEnemyThresholdEffect(_combatTracking.RepulsorTriggered, _combatTracking.RepulsorPending, combatId);
		}

		if (HasActiveMonsterHex(MonsterHexKind.DawnbringersResolve)
			&& _combatTracking.DawnTriggered.Add(combatId))
		{
			int regen = Math.Max(1, (int)Math.Floor(target.MaxHp * DawnbringersResolveRegenPercent));
			await HextechEnemyPowerScalingHooks.Apply<RegenPower>(target, regen, target, null);
		}

		if (HasActiveMonsterHex(MonsterHexKind.FeelTheBurn)
			&& _combatTracking.FeelTheBurnTriggered.Add(combatId))
		{
			_combatTracking.FeelTheBurnPending.Add(combatId);
		}

		if (HasActiveMonsterHex(MonsterHexKind.MikaelsBlessing)
			&& _combatTracking.MikaelsBlessingTriggers.GetValueOrDefault(combatId, 0) < MikaelsBlessingMaxTriggers)
		{
			_combatTracking.MikaelsBlessingTriggers[combatId] = _combatTracking.MikaelsBlessingTriggers.GetValueOrDefault(combatId, 0) + 1;
			int heal = Math.Max(1, (int)Math.Floor(target.MaxHp * MikaelsBlessingHealPercent));
			await CreatureCmd.Heal(target, heal);

			List<PowerModel> negativePowers = target.Powers
				.Where(static power => power.GetTypeForAmount(power.Amount) == PowerType.Debuff)
				.ToList();
			foreach (PowerModel power in negativePowers)
			{
				await PowerCmd.Remove(power);
			}
		}
	}

	private static bool IsBelowEnemyHealthThreshold(Creature target)
	{
		return target.CurrentHp < target.MaxHp * EscapePlanHealthThresholdPercent;
	}

	private static bool TryQueueEnemyThresholdEffect(HashSet<uint> triggered, HashSet<uint> pending, uint combatId)
	{
		if (!triggered.Add(combatId))
		{
			return false;
		}

		pending.Add(combatId);
		return true;
	}

	private async Task ApplyEnemyDamageGivenImmediateHexes(Creature dealer, DamageResult result, Creature target, CardModel? cardSource)
	{
		if (HasActiveMonsterHex(MonsterHexKind.ShrinkRay) && result.UnblockedDamage > 0 && target.Side == CombatSide.Player)
		{
			await PowerCmd.Apply<ShrinkPower>(target, ShrinkRayStacks, dealer, cardSource);
		}

		if (HasActiveMonsterHex(MonsterHexKind.Firebrand)
			&& result.UnblockedDamage > 0
			&& target.Side == CombatSide.Player
			&& !HextechBurnPower.IsResolvingDamage)
		{
			await PowerCmd.Apply<HextechBurnPower>(target, FirebrandBurnStacks, dealer, cardSource);
		}

		if (HasActiveMonsterHex(MonsterHexKind.Goldrend)
			&& result.UnblockedDamage > 0
			&& target.Player != null)
		{
			await HextechGoldrendSync.HandleEnemyGoldrendHit(target.Player);
		}
	}

	private async Task ApplyEnemyDamageGivenPlayerHitHexes(Creature dealer, Creature target)
	{
		if (HasActiveMonsterHex(MonsterHexKind.DevilsDance)
			&& dealer.IsAlive
			&& dealer.CombatId != null
			&& _combatTracking.DevilsDanceTriggeredThisTurn.Add(dealer.CombatId.Value))
		{
			int heal = Math.Max(1, (int)Math.Floor(dealer.MaxHp * DevilsDanceHealPercent));
			await CreatureCmd.Heal(dealer, heal);
		}

		if (HasActiveMonsterHex(MonsterHexKind.SpeedDemon)
			&& dealer.IsAlive
			&& dealer.CombatId != null)
		{
			_combatTracking.SpeedDemonPending.Add(dealer.CombatId.Value);
		}

		if (HasActiveMonsterHex(MonsterHexKind.CantTouchThis) && dealer.IsAlive)
		{
			await HextechEnemyPowerScalingHooks.Apply<SlipperyPower>(dealer, CantTouchThisSlipperyStacks, dealer, null);
		}

		if (HasActiveMonsterHex(MonsterHexKind.FeyMagic)
			&& target.CombatId != null
			&& dealer.CombatId != null
			&& !_combatTracking.FeyMagicPendingNoDrawPlayers.ContainsKey(target.CombatId.Value))
		{
			_combatTracking.FeyMagicPendingNoDrawPlayers[target.CombatId.Value] = dealer.CombatId.Value;
		}

		if (HasActiveMonsterHex(MonsterHexKind.FinalForm) && dealer.IsAlive)
		{
			int block = Math.Max(1, (int)Math.Floor(dealer.MaxHp * FinalFormBlockPercent));
			await CreatureCmd.GainBlock(dealer, block, ValueProp.Unpowered, null);
		}
	}
}
