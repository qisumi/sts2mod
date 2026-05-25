using MegaCrit.Sts2.Core.Entities.Creatures;

namespace HextechRunes;

public sealed class RegenerationSuppressionRune : HextechRelicBase
{
	internal void NotifyEnemyHealSuppressed(Creature target)
	{
		FlashDeferred([target]);
	}
}
