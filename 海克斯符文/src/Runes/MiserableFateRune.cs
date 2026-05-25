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

	public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		if (Owner == null || side != Owner.Creature.Side || Owner.Creature.IsDead || Owner.Creature.CombatState == null)
		{
			return;
		}

		Creature[] enemies = Owner.Creature.CombatState.HittableEnemies.ToArray();
		bool flashed = false;
		foreach (Creature enemy in enemies)
		{
			decimal doom = Math.Max(0m, enemy.GetPowerAmount<DoomPower>());
			if (doom <= 0m)
			{
				continue;
			}

			if (!flashed)
			{
				Flash();
				flashed = true;
			}

			await CreatureCmd.Damage(choiceContext, enemy, doom, ValueProp.Unblockable | ValueProp.Unpowered, Owner.Creature, null);
			if (enemy.GetPower<DoomPower>() is DoomPower remainingDoom)
			{
				await HextechPowerCmdCompat.Remove(remainingDoom);
			}
		}
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
