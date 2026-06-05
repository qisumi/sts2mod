namespace HextechRunes;

internal sealed class LoopEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.Loop;

	internal override decimal ModifyHandDraw(HextechEnemyHexContext context, Player player, decimal count)
	{
		int drawReduction = context.TierValue(Kind, 0, 1, 1);
		return player.Creature.CombatState?.RunState == context.RunState ? Math.Max(0m, count - drawReduction) : count;
	}
}
