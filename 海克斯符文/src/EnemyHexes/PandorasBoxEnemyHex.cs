namespace HextechRunes;

internal sealed class PandorasBoxEnemyHex : HextechEnemyHexEffect
{
	internal override MonsterHexKind Kind => MonsterHexKind.PandorasBox;

	internal override CardCreationOptions ModifyCardRewardCreationOptions(HextechEnemyHexContext context, Player player, CardCreationOptions options)
	{
		if (player.RunState != context.RunState
			|| options.Source != CardCreationSource.Encounter
			|| options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications)
			|| options.CustomCardPool != null
			|| options.CardPools.All(static pool => pool.IsColorless))
		{
			return options;
		}

		IEnumerable<CardPoolModel> pools = player.UnlockState.CharacterCardPools.Union(options.CardPools);
		return options.WithCardPools(pools, options.CardPoolFilter);
	}
}
