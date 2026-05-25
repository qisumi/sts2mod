namespace HextechRunes;

internal sealed class GhostFormEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.GhostForm;

	internal override Task ApplyCombatStartToEnemy(HextechEnemyHexContext context, Creature enemy, CombatRoom room)
	{
		if (enemy.GetPowerAmount<HardToKillPower>() > 0m)
		{
			return Task.CompletedTask;
		}

		int hardToKill = Math.Max(6, (int)Math.Floor(enemy.MaxHp * 0.10m));
		return PowerCmd.Apply<HardToKillPower>(enemy, hardToKill, enemy, null);
	}
}
