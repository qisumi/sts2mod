using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;

namespace HextechRunes;

public sealed class NeutralizeUpgradeRune : CardUpgradeRuneBase<Neutralize>
{
	private bool _isAutoPlayingDiscardedCard;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("Repeats", 2m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Neutralize>(),
		HoverTipFactory.FromCard<Suppress>()
	];

	protected override bool DeckContainsRequiredCard(Player player)
	{
		return DeckContains<Neutralize>(player) || DeckContains<Suppress>(player);
	}

	protected override bool IsAvailableForCharacter(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
	{
		if (_isAutoPlayingDiscardedCard
			|| Owner == null
			|| !IsOwnedCard(card)
			|| Owner.Creature.IsDead
			|| !IsSupportedCard(card)
			|| Owner.Creature.CombatState == null
			|| !CombatManager.Instance.IsInProgress
			|| CombatManager.Instance.IsOverOrEnding)
		{
			return;
		}

		List<Creature> enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
		if (enemies.Count == 0)
		{
			return;
		}

		_isAutoPlayingDiscardedCard = true;
		try
		{
			Flash(enemies);
			for (int repeat = 0; repeat < DynamicVars["Repeats"].IntValue; repeat++)
			{
				foreach (Creature enemy in enemies.Where(static enemy => !enemy.IsDead).ToList())
				{
					CardModel copy = card.CreateClone();
					copy.SetToFreeThisTurn();
					copy.ExhaustOnNextPlay = true;
					await HextechCardGeneration.AddGeneratedCardToCombat(
						copy,
						PileType.Hand,
						addedByPlayer: true,
						CardPilePosition.Top,
						previewNonHandAdds: false);
					await HextechAutoPlayHelper.AutoPlayOrMoveToResultPile(
						choiceContext,
						copy,
						enemy,
						skipCardPileVisuals: true);
				}
			}
		}
		finally
		{
			_isAutoPlayingDiscardedCard = false;
		}
	}

	private static bool IsSupportedCard(CardModel card)
	{
		return card is Neutralize or Suppress;
	}
}
