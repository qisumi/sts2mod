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

public sealed class GoldenSpatulaRune : HextechRelicBase, IHextechSharedCombatVictoryRune
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
			new DynamicVar("StackBonusPercent", 1m),
			new DynamicVar("StackOverloadThreshold", 10m)
		];

	public decimal SustainMultiplier => StackMultiplier;

	public override Task AfterCombatVictory(CombatRoom room)
	{
		if (IsNetworkMultiplayer())
		{
			return Task.CompletedTask;
		}

		return ApplySharedCombatVictory(room);
	}

	public async Task ApplySharedCombatVictory(CombatRoom room)
	{
		if (Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		int previousStacks = _stacks;
		SavedStacks = previousStacks + 1;
		Flash(Array.Empty<Creature>());
		decimal hpGainPercent = TotalBonusPercentFor(_stacks) - TotalBonusPercentFor(previousStacks);
		int hpGain = Math.Max(1, FloorToInt(Owner.Creature.MaxHp * hpGainPercent / 100m));
		await CreatureCmd.GainMaxHp(Owner.Creature, hpGain);
	}

	public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return IsDamageFromOwner(dealer, cardSource) ? StackMultiplier : 1m;
	}

	public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return target == Owner?.Creature ? StackMultiplier : 1m;
	}

	private decimal StackMultiplier
	{
		get
		{
			if (_stacks <= 0)
			{
				return 1m;
			}

			return 1m + TotalBonusPercentFor(_stacks) / 100m;
		}
	}

	private decimal TotalBonusPercentFor(int stacks)
	{
		if (stacks <= 0)
		{
			return 0m;
		}

		decimal multiplier = stacks > DynamicVars["StackOverloadThreshold"].IntValue ? 2m : 1m;
		return stacks * DynamicVars["StackBonusPercent"].BaseValue * multiplier;
	}
}
