using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes;

public sealed class ColorDiscoveryRune : HextechRelicBase
{
	private ModelId _pendingRewardCardId = ModelId.none;
	private bool _offeredThisCombat;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(3),
		new DynamicVar("Selection", 1m)
	];

	public override Task BeforeCombatStart()
	{
		_offeredThisCombat = false;
		_pendingRewardCardId = ModelId.none;
		return Task.CompletedTask;
	}

	public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, HextechCombatState combatState)
	{
		if (_offeredThisCombat
			|| player != Owner
			|| Owner == null
			|| Owner.Creature.IsDead
			|| combatState.RoundNumber != 1
			|| PickOptions(combatState).ToList() is not { Count: > 0 } options)
		{
			return;
		}

		_offeredThisCombat = true;
		IEnumerable<CardModel> selected = await CardSelectCmd.FromSimpleGrid(
			choiceContext,
			options,
			Owner,
			new CardSelectorPrefs(new LocString("cards", "colorDiscoveryRune.selectionScreenPrompt"), 1));
		CardModel? card = selected.FirstOrDefault();
		if (card == null)
		{
			return;
		}

		_pendingRewardCardId = card.CanonicalInstance?.Id ?? card.Id;
		card.SetToFreeThisCombat();

		Flash();
		await HextechCardGeneration.AddGeneratedCardToCombat(card, PileType.Hand, addedByPlayer: true);
	}

	public override bool TryModifyCardRewardOptionsLate(
		Player player,
		List<CardCreationResult> cardRewardOptions,
		CardCreationOptions creationOptions)
	{
		if (player != Owner || _pendingRewardCardId.Equals(ModelId.none))
		{
			return false;
		}

		CardModel canonicalCard = ModelDb.GetById<CardModel>(_pendingRewardCardId);
		CardCreationOptions options = new CardCreationOptions(
				[canonicalCard],
				creationOptions.Source,
				CardRarityOddsType.Uniform)
			.WithFlags(creationOptions.Flags | CardCreationFlags.NoModifyHooks);
		CardCreationResult? result = CardFactory.CreateForReward(player, 1, options).FirstOrDefault();
		if (result == null)
		{
			return false;
		}

		cardRewardOptions.Add(result);
		_pendingRewardCardId = ModelId.none;
		Flash();
		return true;
	}

	private IEnumerable<CardModel> PickOptions(HextechCombatState combatState)
	{
		if (Owner == null)
		{
			return [];
		}

		List<CardModel> candidates = GetOtherCharacterCards(Owner).ToList();
		List<CardModel> options = [];
		for (int i = 0; i < DynamicVars.Cards.IntValue && candidates.Count > 0; i++)
		{
			CardModel canonicalCard = HextechStableRandom.Pick(
				candidates,
				(RunState)Owner.RunState,
				HextechStableRandom.CardKey,
				"color-discovery-option",
				HextechStableRandom.PlayerKey(Owner),
				combatState.RoundNumber.ToString(),
				i.ToString(),
				HextechStableRandom.CardPileKey(candidates));
			options.Add(combatState.CreateCard(canonicalCard, Owner));
			candidates.RemoveAll(card => card.Id == canonicalCard.Id);
		}

		return options;
	}

	private static IEnumerable<CardModel> GetOtherCharacterCards(Player player)
	{
		ModelId ownerPoolId = player.Character.CardPool.Id;
		return GetCharacterPools()
			.Where(pool => !pool.Id.Equals(ownerPoolId))
			.SelectMany(pool => pool.GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint))
			.Where(static card => card.Rarity is not CardRarity.Basic and not CardRarity.Ancient
				&& card.CanBeGeneratedInCombat
				&& card.CanBeGeneratedByModifiers)
			.GroupBy(static card => card.Id)
			.Select(static group => group.First());
	}

	private static IEnumerable<CardPoolModel> GetCharacterPools()
	{
		yield return ModelDb.CardPool<IroncladCardPool>();
		yield return ModelDb.CardPool<SilentCardPool>();
		yield return ModelDb.CardPool<RegentCardPool>();
		yield return ModelDb.CardPool<NecrobinderCardPool>();
		yield return ModelDb.CardPool<DefectCardPool>();
	}
}
