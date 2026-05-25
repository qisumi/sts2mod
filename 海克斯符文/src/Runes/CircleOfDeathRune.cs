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

public sealed class CircleOfDeathRune : HextechRelicBase
{
	public Task HandleSustainGained(decimal amount)
	{
		if (Owner == null ||
			Owner.Creature.IsDead ||
			Owner.Creature.CombatState == null ||
			!CombatManager.Instance.IsInProgress ||
			amount <= 0m)
		{
			return Task.CompletedTask;
		}

		int damage = FloorToInt(amount);
		if (damage <= 0)
		{
			return Task.CompletedTask;
		}

		List<Creature> enemies = Owner.Creature.CombatState.HittableEnemies
			.Where(static enemy => enemy.IsAlive)
			.ToList();
		if (enemies.Count == 0)
		{
			return Task.CompletedTask;
		}

		Creature target = enemies[HextechStableRandom.Index(
			(RunState)Owner.RunState,
			enemies.Count,
			"circle-of-death-target",
			HextechStableRandom.PlayerKey(Owner),
			Owner.Creature.CombatState.RoundNumber.ToString(),
			damage.ToString(),
			CombatManager.Instance.History.Entries.Count().ToString())];
		Flash([target]);
		return CreatureCmd.Damage(new BlockingPlayerChoiceContext(), target, damage, ValueProp.Unpowered, Owner.Creature, null);
	}

	public override Task AfterBlockGained(Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
	{
		return creature == Owner?.Creature ? HandleSustainGained(amount) : Task.CompletedTask;
	}
}
