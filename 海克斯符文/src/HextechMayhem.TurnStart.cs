using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	private async Task BeforePlayerSideTurnStart(HextechCombatState combatState, IReadOnlyList<Creature> players)
	{
		_combatTracking.PreparePlayerSideTurnStart();
		RefreshPlayerAttackCostDoublingPreviews(players);

		await ApplyToCurrentEnemiesIfNeeded();
		await ApplyDelayedEnemyHealingBlocks(combatState);
		HextechEnemyHexContext context = new(this);
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this))
		{
			await effect.BeforePlayerSideTurnStart(context, combatState, players);
		}
    }

	private async Task BeforeEnemySideTurnStart(HextechCombatState combatState, IReadOnlyList<Creature> players)
	{
	    _combatTracking.PrepareEnemySideTurnStart();
		RefreshPlayerAttackCostDoublingPreviews(players);

	    IReadOnlyList<Creature> enemies = GetAliveEnemies(combatState);
	    HextechEnemyHexContext context = new(this);
        foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this))
        {
            await effect.BeforeEnemySideTurnStart(context, combatState, players, enemies);
        }
    }
}
