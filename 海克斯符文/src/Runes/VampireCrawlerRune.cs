using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace HextechRunes;

public sealed class VampireCrawlerRune : HextechRelicBase
{
	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (!ShouldCopyPlayedPowerToDiscard(cardPlay)
			|| Owner?.Creature.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		CardModel copy = combatState.CloneCard(cardPlay.Card);
		await HextechCardGeneration.AddGeneratedCardToCombat(
			copy,
			PileType.Discard,
			addedByPlayer: true,
			CardPilePosition.Bottom,
			previewNonHandAdds: false);
		Flash();
	}

	private bool ShouldCopyPlayedPowerToDiscard(CardPlay cardPlay)
	{
		CardModel card = cardPlay.Card;
		return Owner != null
			&& CombatManager.Instance.IsInProgress
			&& !CombatManager.Instance.IsOverOrEnding
			&& card.Owner == Owner
			&& !card.IsDupe
			&& card.Type == CardType.Power
			&& card.Pile?.Type == PileType.Play
			&& cardPlay.ResultPile == PileType.None
			&& cardPlay.PlayIndex + 1 >= Math.Max(1, cardPlay.PlayCount);
	}
}
