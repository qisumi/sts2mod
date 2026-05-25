using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class GoodLuckRune : HextechRelicBase
{
	public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
	{
		if (player != Owner
			|| creationOptions.Source != CardCreationSource.Encounter
			|| cardRewardOptions.Count == 0)
		{
			return false;
		}

		HashSet<ModelId> existingIds = cardRewardOptions
			.Select(static result => result.Card.CanonicalInstance.Id)
			.ToHashSet();
		List<CardModel> rarePool = creationOptions
			.GetPossibleCards(player)
			.Where(card => card.Rarity == CardRarity.Rare && !existingIds.Contains(card.Id))
			.ToList();
		if (rarePool.Count == 0)
		{
			return false;
		}

		CardCreationOptions rareOptions = new CardCreationOptions(
				rarePool,
				creationOptions.Source,
				CardRarityOddsType.Uniform)
			.WithFlags(CardCreationFlags.NoModifyHooks);
		CardCreationResult? rareResult = CardFactory.CreateForReward(player, 1, rareOptions).FirstOrDefault();
		if (rareResult == null)
		{
			return false;
		}

		CardCmd.Upgrade(rareResult.Card, CardPreviewStyle.None);
		cardRewardOptions.Add(rareResult);
		Flash();
		return true;
	}
}
