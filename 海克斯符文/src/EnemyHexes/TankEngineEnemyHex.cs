namespace HextechRunes;

internal sealed class TankEngineEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.TankEngine;

	internal override async Task BeforeEnemySideTurnStart(HextechEnemyHexContext context, HextechCombatState combatState, IReadOnlyList<Creature> players, IReadOnlyList<Creature> enemies)
	{
		foreach (Creature enemy in enemies)
		{
			if (enemy.CombatId is uint combatId)
			{
				int currentRound = combatState.RoundNumber;
				if (context.Tracking.TankEngineLastAppliedRound.GetValueOrDefault(combatId, 0) == currentRound)
				{
					continue;
				}

				context.Tracking.TankEngineLastAppliedRound[combatId] = currentRound;
			}

			int maxHpGain = context.TierValue(Kind, 5, 10, 15);
			int hpGain = Math.Min(maxHpGain, Math.Max(1, (int)Math.Floor(enemy.MaxHp * 0.05m)));
			await HextechMayhemModifier.GainMonsterMaxHpWithoutHeal(enemy, hpGain);
			if (enemy.CombatId is uint trackedCombatId)
			{
				context.Tracking.TankEngineStacks[trackedCombatId] = context.Tracking.TankEngineStacks.GetValueOrDefault(trackedCombatId, 0) + 1;
				context.UpdateEnemyScale(enemy);
			}
		}
	}
}
