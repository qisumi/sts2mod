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

public sealed class MadScientistRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("OrbSlots", 3m),
		new DynamicVar("OrbCount", 3m)
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
		if (Owner == null || side != Owner.Creature.Side)
		{
			return;
		}

		int orbSlots = Math.Max(0, DynamicVars["OrbSlots"].IntValue);
		int orbCount = Math.Max(0, DynamicVars["OrbCount"].IntValue);
		if (orbSlots <= 0 && orbCount <= 0)
		{
			return;
		}

		Flash();
		await OrbCmd.AddSlots(Owner, orbSlots);
		for (int i = 0; i < orbCount; i++)
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
