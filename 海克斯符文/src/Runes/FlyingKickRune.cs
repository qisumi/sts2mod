using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Exceptions;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class FlyingKickRune : HextechRelicBase
{
	private const string BaseExecutePercentVar = "BaseExecutePercent";
	private const string OwnerMaxHpToExecutePercentVar = "OwnerMaxHpToExecutePercent";
	private const string ExecutePercentVar = "ExecutePercent";

	private bool _executing;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar(BaseExecutePercentVar, 10m),
		new DynamicVar(OwnerMaxHpToExecutePercentVar, 8m),
		new DynamicVar(ExecutePercentVar, 10m),
		new HealVar(10m)
	];

	public void RefreshExecutePercentFromOwner()
	{
		Player? owner;
		try
		{
			owner = Owner;
		}
		catch (CanonicalModelException)
		{
			return;
		}

		if (owner == null)
		{
			return;
		}

		RefreshExecutePercent(owner.Creature.MaxHp);
	}

	public decimal RefreshExecutePercent(decimal ownerMaxHp)
	{
		decimal executePercent = DynamicVars[BaseExecutePercentVar].BaseValue
			+ ownerMaxHp * DynamicVars[OwnerMaxHpToExecutePercentVar].BaseValue / 100m;
		DynamicVars[ExecutePercentVar].BaseValue = executePercent;
		return executePercent;
	}

	public override async Task AfterDamageGiven(
		PlayerChoiceContext choiceContext,
		Creature? dealer,
		DamageResult result,
		ValueProp props,
		Creature target,
		CardModel? cardSource)
	{
		if (_executing
			|| Owner == null
			|| Owner.Creature.IsDead
			|| target.Side != CombatSide.Enemy
			|| result.UnblockedDamage <= 0m
			|| !IsDamageFromOwner(dealer, cardSource))
		{
			return;
		}

		if (result.WasTargetKilled)
		{
			await TriggerFlyingKick(choiceContext, target, killTarget: false);
			return;
		}

		if (!target.IsAlive)
		{
			return;
		}

		decimal executePercent = RefreshExecutePercent(Owner.Creature.MaxHp);
		decimal threshold = target.MaxHp * executePercent / 100m;
		if (target.CurrentHp >= threshold)
		{
			return;
		}

		await TriggerFlyingKick(choiceContext, target, killTarget: true);
	}

	private async Task TriggerFlyingKick(PlayerChoiceContext choiceContext, Creature target, bool killTarget)
	{
		if (_executing || Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		_executing = true;
		try
		{
			Flash([target]);
			if (killTarget)
			{
				FlyingKickCorpseLaunchDriver.MarkPending(target);
				await CreatureCmd.Kill(target);
			}
			else if (HextechMonsterInteractionPolicy.IsTrueCombatDeath(target))
			{
				FlyingKickCorpseLaunchDriver.MarkPendingUntilConsumed(target);
			}
		}
		finally
		{
			if (killTarget)
			{
				FlyingKickCorpseLaunchDriver.ClearPending(target);
			}

			_executing = false;
		}

		if (Owner.Creature.IsAlive)
		{
			decimal heal = Math.Max(1m, decimal.Floor(Owner.Creature.MaxHp * DynamicVars.Heal.BaseValue / 100m));
			await CreatureCmd.Heal(Owner.Creature, heal);
		}
	}
}
