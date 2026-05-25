using System;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	internal bool QueueEnemyHealingBlock(Creature creature, decimal amount)
	{
		if (creature.Side != CombatSide.Enemy || creature.CombatId == null || amount <= 0m)
		{
			return false;
		}

		int block = (int)Math.Floor(amount);
		if (block <= 0)
		{
			return false;
		}

		uint combatId = creature.CombatId.Value;
		_combatTracking.DelayedEnemyHealingBlock[combatId] =
			_combatTracking.DelayedEnemyHealingBlock.GetValueOrDefault(combatId, 0) + block;
		return true;
	}

	private async Task ApplyDelayedEnemyHealingBlocks(HextechCombatState combatState)
	{
		foreach ((uint combatId, int block) in _combatTracking.DelayedEnemyHealingBlock.ToList())
		{
			_combatTracking.DelayedEnemyHealingBlock.Remove(combatId);
			Creature? creature = combatState.GetCreature(combatId);
			if (creature == null || !creature.IsAlive || block <= 0)
			{
				continue;
			}

			await CreatureCmd.GainBlock(creature, block, ValueProp.Unpowered, null);
		}
	}
}
