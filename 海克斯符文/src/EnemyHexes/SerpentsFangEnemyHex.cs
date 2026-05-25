namespace HextechRunes;

internal sealed class SerpentsFangEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.SerpentsFang;

	internal override Task AfterEnemyDamageGivenImmediate(HextechEnemyHexContext context, Creature dealer, DamageResult result, Creature target, CardModel? cardSource)
	{
		return result.UnblockedDamage > 0 && target.Side == CombatSide.Player
			? PowerCmd.Apply<PoisonPower>(target, context.TierValue(Kind, 2, 3, 4), dealer, cardSource)
			: Task.CompletedTask;
	}
}
