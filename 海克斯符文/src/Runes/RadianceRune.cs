using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class RadianceRune : HextechRelicBase
{
	private bool _triggeredThisTurn;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("MaxHpDamagePercent", 0.05m),
		new HealVar(2m)
	];

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

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		EnsureTurnScopedStateCurrent(ResetTurnState);
		if (_triggeredThisTurn
			|| Owner == null
			|| target.Side != CombatSide.Enemy
			|| result.TotalDamage <= 0m
			|| !IsDamageFromOwner(dealer, cardSource))
		{
			return;
		}

		_triggeredThisTurn = true;
		UpdateTurnScopedStateIdentity();
		if (target.IsAlive)
		{
			int damage = Math.Max(1, FloorToInt(target.MaxHp * DynamicVars["MaxHpDamagePercent"].BaseValue));
			Flash([target]);
			await CreatureCmd.Damage(choiceContext, target, damage, ValueProp.Unpowered, Owner.Creature, cardSource);
		}
		else
		{
			Flash();
		}

		if (Owner.Creature.IsAlive)
		{
			await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
		}
	}

	private void ResetTurnState()
	{
		ResetTurnState(null);
	}

	private void ResetTurnState(HextechCombatState? combatState)
	{
		_triggeredThisTurn = false;
		UpdateTurnScopedStateIdentity(combatState);
	}
}
