using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace HextechRunes;

public sealed class KeystoneHunterRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<ToolsOfTheTradePower>(1m),
		new PowerVar<MasterPlannerPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<ToolsOfTheTradePower>(),
		HoverTipFactory.FromPower<MasterPlannerPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task BeforeCombatStart()
	{
		if (Owner == null || Owner.Creature.IsDead || !IsSilentPlayer(Owner))
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<ToolsOfTheTradePower>(Owner.Creature, DynamicVars["ToolsOfTheTradePower"].BaseValue, Owner.Creature, null);
		await PowerCmd.Apply<MasterPlannerPower>(Owner.Creature, DynamicVars["MasterPlannerPower"].BaseValue, Owner.Creature, null);
	}
}

public sealed class KillerHunterRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("TemporaryStatLoss", 1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<StrengthPower>(),
		HoverTipFactory.FromPower<DexterityPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (!IsOwnedCard(cardPlay.Card) || Owner == null || Owner.Creature.IsDead || Owner.Creature.CombatState == null)
		{
			return;
		}

		IReadOnlyList<Creature> enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
		if (enemies.Count == 0)
		{
			return;
		}

		Flash(enemies);
		await PowerCmd.Apply<HextechTemporaryStrengthLossPower>(enemies, DynamicVars["TemporaryStatLoss"].BaseValue, Owner.Creature, cardPlay.Card);
		await PowerCmd.Apply<HextechTemporaryDexterityLossPower>(enemies, DynamicVars["TemporaryStatLoss"].BaseValue, Owner.Creature, cardPlay.Card);
	}
}

public sealed class LethalTempoRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<StrengthPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<StrengthPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (!IsOwnedCard(cardPlay.Card) || !cardPlay.Card.Tags.Contains(CardTag.Shiv) || Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<HextechLethalTempoTemporaryStrengthPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, cardPlay.Card);
	}
}

public sealed class LifeFlowRune : HextechRelicBase
{
	private int _procsThisTurn;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedProcsThisTurn
	{
		get
		{
			EnsureTurnScopedStateCurrent(ResetProcs);
			return _procsThisTurn;
		}
		set
		{
			_procsThisTurn = Math.Max(0, value);
			InvokeDisplayAmountChanged();
			UpdateTurnScopedStateIdentity();
		}
	}

	public override bool ShowCounter => CombatManager.Instance?.IsInProgress == true && !IsCanonical;

	public override int DisplayAmount => !IsCanonical ? Math.Max(0, DynamicVars["MaxProcsPerTurn"].IntValue - _procsThisTurn) : 0;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("HealPercent", 0.05m),
		new DynamicVar("MaxProcsPerTurn", 3m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsIroncladPlayer(player);
	}

	public override Task BeforeCombatStart()
	{
		ResetProcs(null);
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ResetProcs(null);
		return Task.CompletedTask;
	}

	public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		if (Owner != null && side == Owner.Creature.Side)
		{
			ResetProcs(combatState);
		}

		return Task.CompletedTask;
	}

	public override Task AfterCardExhausted(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
	{
		EnsureTurnScopedStateCurrent(ResetProcs);
		if (!IsOwnedCard(card)
			|| Owner == null
			|| Owner.Creature.IsDead
			|| _procsThisTurn >= DynamicVars["MaxProcsPerTurn"].IntValue)
		{
			return Task.CompletedTask;
		}

		_procsThisTurn++;
		InvokeDisplayAmountChanged();
		UpdateTurnScopedStateIdentity();
		int healAmount = Math.Max(1, FloorToInt(Owner.Creature.MaxHp * DynamicVars["HealPercent"].BaseValue));
		Flash();
		return CreatureCmd.Heal(Owner.Creature, healAmount);
	}

	private void ResetProcs()
	{
		ResetProcs(null);
	}

	private void ResetProcs(HextechCombatState? combatState)
	{
		_procsThisTurn = 0;
		InvokeDisplayAmountChanged();
		UpdateTurnScopedStateIdentity(combatState);
	}
}

public sealed class LightEmUpRune : HextechRelicBase
{
	private const int AttacksPerReplay = 4;

	private int _attacksPlayedThisCombat;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedAttacksPlayedThisCombat
	{
		get => IsNetworkMultiplayer() ? 0 : GetAttacksPlayedThisCombat();
		set
		{
			_attacksPlayedThisCombat = Math.Max(0, value) % AttacksPerReplay;
			InvokeDisplayAmountChanged();
		}
	}

	public override bool ShowCounter => CombatManager.Instance?.IsInProgress == true && !IsCanonical;

	public override int DisplayAmount
	{
		get
		{
			if (IsCanonical)
			{
				return 0;
			}

			return GetAttacksPlayedThisCombat();
		}
	}

	public override Task BeforeCombatStart()
	{
		ResetAttacksPlayedThisCombat();
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ResetAttacksPlayedThisCombat();
		return Task.CompletedTask;
	}

	public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
	{
		if (!IsOwnedAttack(card))
		{
			return playCount;
		}

		int nextAttacksPlayed = GetAttacksPlayedBeforeCurrentAttack() + 1;
		_attacksPlayedThisCombat = nextAttacksPlayed % AttacksPerReplay;
		if (nextAttacksPlayed % AttacksPerReplay == 0)
		{
			InvokeDisplayAmountChanged();
			return playCount + 1;
		}

		InvokeDisplayAmountChanged();
		return playCount;
	}

	public override Task AfterModifyingCardPlayCount(CardModel card)
	{
		if (IsOwnedAttack(card))
		{
			Flash();
		}

		return Task.CompletedTask;
	}

	private void ResetAttacksPlayedThisCombat()
	{
		_attacksPlayedThisCombat = 0;
		InvokeDisplayAmountChanged();
	}

	private int GetAttacksPlayedThisCombat()
	{
		return ShouldUseNetworkCombatHistory()
			? CountOwnedAttackCardsPlayedFromHistory() % AttacksPerReplay
			: _attacksPlayedThisCombat;
	}

	private int GetAttacksPlayedBeforeCurrentAttack()
	{
		return ShouldUseNetworkCombatHistory()
			? CountOwnedAttackCardsPlayedFromHistory()
			: _attacksPlayedThisCombat;
	}
}

public sealed class LoopRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new EnergyVar(1)
	];

	public override decimal ModifyMaxEnergy(Player player, decimal amount)
	{
		return player == Owner ? amount + DynamicVars.Energy.BaseValue : amount;
	}
}

public sealed class LubricantRune : HextechRelicBase
{
	private bool _usedThisTurn;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedUsedThisTurn
	{
		get
		{
			EnsureTurnScopedStateCurrent(ResetTurnState);
			return _usedThisTurn;
		}
		set
		{
			_usedThisTurn = value;
			InvokeDisplayAmountChanged();
			UpdateTurnScopedStateIdentity();
		}
	}

	public override bool ShowCounter => CombatManager.Instance?.IsInProgress == true && !IsCanonical;

	public override int DisplayAmount => !IsCanonical && !_usedThisTurn ? 1 : 0;

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsDefectPlayer(player);
	}

	public override Task BeforeCombatStart()
	{
		ResetTurnState(null);
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ResetTurnState(null);
		return Task.CompletedTask;
	}

	public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		if (Owner != null && side == Owner.Creature.Side)
		{
			ResetTurnState(combatState);
		}

		return Task.CompletedTask;
	}

	public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (!ShouldPowerCardBeFree(card))
		{
			return false;
		}

		modifiedCost = 0m;
		return true;
	}

	public override bool TryModifyStarCost(CardModel card, decimal originalCost, out decimal modifiedCost)
	{
		modifiedCost = originalCost;
		if (!ShouldPowerCardBeFree(card))
		{
			return false;
		}

		modifiedCost = 0m;
		return true;
	}

	public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		EnsureTurnScopedStateCurrent(ResetTurnState);
		if (_usedThisTurn
			|| cardPlay.IsAutoPlay
			|| !cardPlay.IsFirstInSeries
			|| cardPlay.Card.Owner != Owner
			|| cardPlay.Card.Type != CardType.Power)
		{
			return Task.CompletedTask;
		}

		_usedThisTurn = true;
		InvokeDisplayAmountChanged();
		UpdateTurnScopedStateIdentity();
		Flash();
		return Task.CompletedTask;
	}

	private bool ShouldPowerCardBeFree(CardModel card)
	{
		EnsureTurnScopedStateCurrent(ResetTurnState);
		return !_usedThisTurn
			&& Owner != null
			&& card.Owner == Owner
			&& card.Type == CardType.Power
			&& card.Pile?.Type is PileType.Hand or PileType.Play;
	}

	private void ResetTurnState()
	{
		ResetTurnState(null);
	}

	private void ResetTurnState(HextechCombatState? combatState)
	{
		_usedThisTurn = false;
		InvokeDisplayAmountChanged();
		UpdateTurnScopedStateIdentity(combatState);
	}
}

public sealed class MadScientistRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("OrbSlots", 5m),
		new DynamicVar("OrbCount", 5m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<ElicitCard>()
	];

	public override async Task AfterObtained()
	{
		if (Owner == null)
		{
			return;
		}

		await AddCardCopiesToDeckOrHand<ElicitCard>(1);
	}

	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (Owner == null || side != Owner.Creature.Side || combatState.RoundNumber > 1)
		{
			return;
		}

		Flash();
		await OrbCmd.AddSlots(Owner, DynamicVars["OrbSlots"].IntValue);
		for (int i = 0; i < DynamicVars["OrbCount"].IntValue; i++)
		{
			OrbModel orb = HextechStableRandom.CreateOrb(
				(RunState)Owner.RunState,
				Owner,
				"mad-scientist-start-orb",
				i,
				combatState.RoundNumber);
			await OrbCmd.Channel(new BlockingPlayerChoiceContext(), orb, Owner);
		}
	}
}

public sealed class MakeItMineRune : HextechRelicBase
{
	private int _stacks;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedStacks
	{
		get => _stacks;
		set
		{
			_stacks = Math.Max(0, value);
			InvokeDisplayAmountChanged();
		}
	}

	public override bool ShowCounter => true;

	public override int DisplayAmount => !IsCanonical ? _stacks : 0;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new SummonVar(4m)
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsNecrobinderPlayer(player);
	}

	public override Task AfterCombatVictory(CombatRoom room)
	{
		if (Owner == null || Owner.Creature.IsDead)
		{
			return Task.CompletedTask;
		}

		SavedStacks++;
		Flash();
		return Task.CompletedTask;
	}

#if STS2_104_OR_NEWER
	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
#else
	public override async Task BeforePlayPhaseStart(PlayerChoiceContext choiceContext, Player player)
#endif
	{
		if (player != Owner
			|| Owner == null
			|| Owner.Creature.IsDead
			|| Owner.Creature.CombatState?.RoundNumber > 1
			|| _stacks <= 0
			|| !IsNecrobinderPlayer(player))
		{
			return;
		}

		Flash();
		await OstyCmd.Summon(choiceContext, player, _stacks * DynamicVars.Summon.BaseValue, this);
	}
}

public sealed class MasterOfDualityRune : HextechRelicBase
{
	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (Owner == null || cardPlay.Card.Owner != Owner)
		{
			return;
		}

		if (cardPlay.Card.Type == CardType.Skill)
		{
			Flash();
			await PowerCmd.Apply<HextechTemporaryStrengthPower>(Owner.Creature, 1m, Owner.Creature, null);
		}
		else if (cardPlay.Card.Type == CardType.Attack)
		{
			Flash();
			await PowerCmd.Apply<HextechTemporaryDexterityPower>(Owner.Creature, 1m, Owner.Creature, null);
		}
	}
}

public sealed class MikaelsBlessingRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("PotionCount", 2m),
		new DynamicVar("HealPercent", 20m)
	];

	public override async Task BeforeCombatStart()
	{
		if (Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		List<PotionModel> candidates = PotionFactory.GetPotionOptions(Owner, Array.Empty<PotionModel>()).ToList();
		if (candidates.Count == 0)
		{
			return;
		}

		Flash(Array.Empty<Creature>());
		for (int i = 0; i < DynamicVars["PotionCount"].IntValue; i++)
		{
			PotionModel potion = HextechStableRandom.Pick(
				candidates,
				(RunState)Owner.RunState,
				HextechStableRandom.PotionKey,
				"mikaels-blessing-potion",
				HextechStableRandom.PlayerKey(Owner),
				i.ToString()).ToMutable();
			await PotionCmd.TryToProcure(potion, Owner);
		}
	}

	public override async Task AfterPotionUsed(PotionModel potion, Creature? target)
	{
		if (Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		int healAmount = Math.Max(1, FloorToInt(Owner.Creature.MaxHp * DynamicVars["HealPercent"].BaseValue / 100m));
		await CreatureCmd.Heal(Owner.Creature, healAmount);

		List<PowerModel> negativePowers = Owner.Creature.Powers
			.Where(static power => power.GetTypeForAmount(power.Amount) == PowerType.Debuff)
			.ToList();
		foreach (PowerModel power in negativePowers)
		{
			await PowerCmd.Remove(power);
		}
	}
}

public sealed class MindPurificationRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("DamagePercent", 30m)
	];

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (Owner == null
			|| wasRemovalPrevented
			|| target.Side == Owner.Creature.Side
			|| !HextechMonsterInteractionPolicy.IsTrueCombatDeath(target, out HextechCombatState? combatState))
		{
			return;
		}

		List<(Creature creature, int damage)> toDamage = combatState.Enemies
			.Where(enemy => enemy != target && enemy.IsAlive)
			.Select(enemy => (enemy, FloorToInt(enemy.CurrentHp * 0.3m)))
			.Where(pair => pair.Item2 > 0)
			.ToList();
		if (toDamage.Count == 0)
		{
			return;
		}

		Flash(toDamage.Select(static pair => pair.creature));
		foreach ((Creature creature, int damage) in toDamage)
		{
			await CreatureCmd.Damage(choiceContext, creature, damage, ValueProp.Unpowered, Owner.Creature, null);
		}
	}
}

public sealed class MindToMatterRune : HextechRelicBase
{
	public override bool HasUponPickupEffect => true;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new MaxHpVar(1m)
	];

	public override Task AfterObtained()
	{
		if (Owner == null)
		{
			return Task.CompletedTask;
		}

		int maxHpGain = Owner.Deck.Cards.Count;
		if (maxHpGain <= 0)
		{
			return Task.CompletedTask;
		}

		Flash();
		return CreatureCmd.GainMaxHp(Owner.Creature, maxHpGain);
	}
}

public sealed class MirageRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new BlockVar(1m, ValueProp.Unpowered)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PoisonPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (Owner == null || side != Owner.Creature.Side || Owner.Creature.IsDead || Owner.Creature.CombatState == null)
		{
			return Task.CompletedTask;
		}

		decimal block = Owner.Creature.CombatState.HittableEnemies
			.Sum(static enemy => Math.Max(0m, enemy.GetPowerAmount<PoisonPower>()));
		if (block <= 0m)
		{
			return Task.CompletedTask;
		}

		Flash();
		return CreatureCmd.GainBlock(Owner.Creature, block * DynamicVars.Block.BaseValue, ValueProp.Unpowered, null);
	}
}

public sealed class MiserableFateRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new BlockVar(1m, ValueProp.Unpowered)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<DoomPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsNecrobinderPlayer(player);
	}

	public override Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (Owner == null || side != Owner.Creature.Side || Owner.Creature.IsDead || Owner.Creature.CombatState == null)
		{
			return Task.CompletedTask;
		}

		decimal block = Owner.Creature.CombatState.HittableEnemies
			.Sum(static enemy => Math.Max(0m, enemy.GetPowerAmount<DoomPower>()));
		if (block <= 0m)
		{
			return Task.CompletedTask;
		}

		Flash();
		return CreatureCmd.GainBlock(Owner.Creature, block * DynamicVars.Block.BaseValue, ValueProp.Unpowered, null);
	}
}

public sealed class MiseryRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<StrengthPower>(-1m),
		new PowerVar<DexterityPower>(-1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<StrengthPower>(),
		HoverTipFactory.FromPower<DexterityPower>()
	];

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner || Owner.Creature.IsDead || Owner.Creature.CombatState == null)
		{
			return;
		}

		IReadOnlyList<Creature> enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
		if (enemies.Count == 0)
		{
			return;
		}

		Creature target = enemies[HextechStableRandom.Index(
			(RunState)Owner.RunState,
			enemies.Count,
			"misery-target",
			HextechStableRandom.PlayerKey(Owner),
			Owner.Creature.CombatState.RoundNumber.ToString())];
		List<Creature> flashTargets = [target, Owner.Creature];
		FlashDeferred(flashTargets);
		await PowerCmd.Apply<StrengthPower>(target, DynamicVars.Strength.BaseValue, Owner.Creature, null);
		await PowerCmd.Apply<DexterityPower>(target, DynamicVars.Dexterity.BaseValue, Owner.Creature, null);
		await PowerCmd.Apply<StrengthPower>(Owner.Creature, -DynamicVars.Strength.BaseValue, Owner.Creature, null);
		await PowerCmd.Apply<DexterityPower>(Owner.Creature, -DynamicVars.Dexterity.BaseValue, Owner.Creature, null);
	}
}
