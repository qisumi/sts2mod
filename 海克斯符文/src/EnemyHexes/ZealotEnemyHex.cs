namespace HextechRunes;

internal sealed class ZealotEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.Zealot;

	internal override Task ApplyCombatStartToEnemy(HextechEnemyHexContext context, Creature enemy, CombatRoom room)
	{
		return PowerCmd.Apply<VigorPower>(enemy, context.TierValue(Kind, 1, 2, 3), enemy, null);
	}
}
