namespace HextechRunes;

internal sealed class ForbiddenGrimoireEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.ForbiddenGrimoire;

	internal override bool TryModifyCardRewardOptionsLate(HextechEnemyHexContext context, Player player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
	{
		if (player.RunState != context.RunState
			|| creationOptions.Source != CardCreationSource.Encounter
			|| cardRewardOptions.Count <= 1)
		{
			return false;
		}

		cardRewardOptions.RemoveAt(cardRewardOptions.Count - 1);
		return true;
	}
}
