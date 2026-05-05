using System.Diagnostics.CodeAnalysis;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes;

internal static class HextechMonsterInteractionPolicy
{
	public static bool IsTrueCombatDeath(Creature creature)
	{
		return IsTrueCombatDeath(creature, out _);
	}

	public static bool IsTrueCombatDeath(Creature creature, [NotNullWhen(true)] out HextechCombatState? combatState)
	{
		combatState = creature.CombatState;
		return combatState != null
			&& !IsBossPhaseTransitionDeath(creature)
			&& Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, creature);
	}

	public static bool IsBossPhaseTransitionDeath(Creature creature)
	{
		HextechCombatState? combatState = creature.CombatState;
		return combatState != null
			&& creature.Monster is TestSubject
			&& !Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, creature);
	}

	public static bool ShouldIgnoreMonsterSelfBuff(PowerModel power)
	{
		return power is SandpitPower;
	}
}
