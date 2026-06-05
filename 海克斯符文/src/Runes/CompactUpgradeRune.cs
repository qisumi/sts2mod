using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace HextechRunes;

public sealed class CompactUpgradeRune : CardUpgradeRuneBase<Compact>
{
	protected override bool IsAvailableForCharacter(Player player)
	{
		return IsDefectPlayer(player);
	}

	internal static bool ShouldUseUpgradedPlay(CardModel card)
	{
		return card is Compact && card.Owner?.GetRelic<CompactUpgradeRune>() != null;
	}

	internal static async Task PlayUpgraded(PlayerChoiceContext choiceContext, Compact card, CardPlay cardPlay)
	{
		var owner = card.Owner!;
		var combatState = card.CombatState!;
		PlayerCombatState? playerCombatState = owner.PlayerCombatState;
		if (playerCombatState == null)
		{
			return;
		}

		await CreatureCmd.GainBlock(owner.Creature, card.DynamicVars.Block, cardPlay);

		List<CardTransformation> transformations = new();
		foreach (CardPile pile in playerCombatState.AllPiles)
		{
			foreach (CardModel statusCard in pile.Cards)
			{
				if (!statusCard.IsTransformable || statusCard.Type != CardType.Status)
				{
					continue;
				}

				CardModel fuel = combatState.CreateCard<Fuel>(owner);
				if (card.IsUpgraded)
				{
					CardCmd.Upgrade(fuel);
				}

				transformations.Add(new CardTransformation(statusCard, fuel));
			}
		}

		if (transformations.Count == 0)
		{
			return;
		}

		owner.GetRelic<CompactUpgradeRune>()?.Flash();
		await CardCmd.Transform(transformations, null, CardPreviewStyle.None);
	}
}
