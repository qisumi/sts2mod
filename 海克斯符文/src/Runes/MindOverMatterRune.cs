using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class MindOverMatterRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(1)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromKeyword(CardKeyword.Ethereal)
	];

	public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, HextechCombatState combatState)
	{
		if (player != Owner || Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		List<CardModel> pool = Owner.Character.CardPool
			.GetUnlockedCards(Owner.UnlockState, Owner.RunState.CardMultiplayerConstraint)
			.Where(static card => card.Rarity is not CardRarity.Basic and not CardRarity.Ancient && card.CanBeGeneratedInCombat)
			.ToList();
		if (pool.Count == 0)
		{
			return;
		}

		CardModel canonicalCard = HextechStableRandom.Pick(
			pool,
			(RunState)Owner.RunState,
			HextechStableRandom.CardKey,
			"mind-over-matter",
			HextechStableRandom.PlayerKey(Owner),
			combatState.RoundNumber.ToString(),
			CountOwnedCardsDrawnFromHistory().ToString());
		CardModel card = combatState.CreateCard(canonicalCard, Owner);
		card.AddKeyword(CardKeyword.Ethereal);
		card.SetToFreeThisCombat();

		Flash();
		await HextechCardGeneration.AddGeneratedCardToCombat(card, PileType.Hand, addedByPlayer: true);
	}
}
