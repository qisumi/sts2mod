namespace HextechRunes;

internal sealed class ScaredStiffEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.ScaredStiff;

	internal override Task ApplyCombatStartToEnemy(HextechEnemyHexContext context, Creature enemy, CombatRoom room)
	{
		int skittish = Math.Max(1, (int)Math.Floor(enemy.MaxHp * 0.10m));
		return HextechEnemyPowerScalingHooks.Apply<SkittishPower>(enemy, skittish, enemy, null);
	}
}
