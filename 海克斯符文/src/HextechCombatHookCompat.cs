using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes;

#if STS2_106_OR_NEWER
public abstract class HextechPowerBase : PowerModel
{
	public virtual Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		return Task.CompletedTask;
	}

	public sealed override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, HextechCombatState combatState)
	{
		return BeforeSideTurnStart(choiceContext, side, combatState);
	}

	public virtual Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		return Task.CompletedTask;
	}

	public sealed override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, HextechCombatState combatState)
	{
		return AfterSideTurnStart(side, combatState);
	}

	public virtual Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		return Task.CompletedTask;
	}

	public sealed override Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		return BeforeTurnEnd(choiceContext, side);
	}

	public virtual Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		return Task.CompletedTask;
	}

	public sealed override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		return AfterTurnEnd(choiceContext, side);
	}
}

internal abstract class HextechModifierBase : ModifierModel
{
	public virtual Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		return Task.CompletedTask;
	}

	public sealed override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, HextechCombatState combatState)
	{
		return BeforeSideTurnStart(choiceContext, side, combatState);
	}

	public virtual Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		return Task.CompletedTask;
	}

	public sealed override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, HextechCombatState combatState)
	{
		return AfterSideTurnStart(side, combatState);
	}

	public virtual Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		return Task.CompletedTask;
	}

	public sealed override Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		return BeforeTurnEnd(choiceContext, side);
	}

	public virtual Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		return Task.CompletedTask;
	}

	public sealed override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
	{
		return AfterTurnEnd(choiceContext, side);
	}
}
#else
public abstract class HextechPowerBase : PowerModel
{
}

internal abstract class HextechModifierBase : ModifierModel
{
}
#endif
