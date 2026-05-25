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

public sealed class EarthAwakensRune : HextechRelicBase
{
	private bool _initialPowerAppliedThisCombat;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public bool SavedInitialPowerAppliedThisCombat
	{
		get => _initialPowerAppliedThisCombat;
		set => _initialPowerAppliedThisCombat = value;
	}

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<RollingBoulderPower>(5m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<RollingBoulderPower>()
	];

	public override Task BeforeCombatStart()
	{
		_initialPowerAppliedThisCombat = false;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_initialPowerAppliedThisCombat = false;
		return Task.CompletedTask;
	}

#if STS2_104_OR_NEWER
	public override Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)
#else
	public override Task BeforePlayPhaseStart(PlayerChoiceContext choiceContext, Player player)
#endif
	{
		if (player != Owner || Owner == null || Owner.Creature.IsDead || _initialPowerAppliedThisCombat)
		{
			return Task.CompletedTask;
		}

		_initialPowerAppliedThisCombat = true;
		return ApplyRollingBoulderPower();
	}

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != Owner || Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		_initialPowerAppliedThisCombat = true;
		await ApplyRollingBoulderPower();
	}

	private async Task ApplyRollingBoulderPower()
	{
		if (Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<RollingBoulderPower>(Owner.Creature, DynamicVars["RollingBoulderPower"].BaseValue, Owner.Creature, null);
	}
}
