using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace HextechRunes;

public abstract class CardUpgradeRuneBase<TCard> : HextechRelicBase
	where TCard : CardModel
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<TCard>()
	];

	public sealed override bool IsAvailableForPlayer(Player player)
	{
		return IsAvailableForCharacter(player) && DeckContainsRequiredCard(player);
	}

	protected virtual bool DeckContainsRequiredCard(Player player)
	{
		return DeckContains<TCard>(player);
	}

	protected abstract bool IsAvailableForCharacter(Player player);
}
