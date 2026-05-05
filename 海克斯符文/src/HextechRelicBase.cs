using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public abstract class HextechRelicBase : RelicModel
{
	private static readonly string PlaceholderIconPath = ImageHelper.GetImagePath("powers/missing_power.png");
	private HextechCombatState? _turnScopedCombatState;
	private int _turnScopedRoundNumber = -1;

	public sealed override RelicRarity Rarity => RelicRarity.Starter;

	public override string PackedIconPath => GetResolvedIconPath();

	protected override string PackedIconOutlinePath => GetResolvedIconPath();

	protected override string BigIconPath => GetResolvedIconPath();

	public virtual bool IsAvailableForPlayer(Player player) => true;

	protected static int FloorToInt(decimal value)
	{
		return (int)decimal.Floor(value);
	}

	protected void EnsureTurnScopedStateCurrent(Action resetState)
	{
		HextechCombatState? combatState = Owner?.Creature.CombatState;
		if (combatState == null)
		{
			resetState();
			_turnScopedCombatState = null;
			_turnScopedRoundNumber = -1;
			return;
		}

		if (!ReferenceEquals(_turnScopedCombatState, combatState)
			|| _turnScopedRoundNumber != combatState.RoundNumber)
		{
			resetState();
			UpdateTurnScopedStateIdentity(combatState);
		}
	}

	protected void UpdateTurnScopedStateIdentity(HextechCombatState? combatState = null)
	{
		combatState ??= Owner?.Creature.CombatState;
		_turnScopedCombatState = combatState;
		_turnScopedRoundNumber = combatState?.RoundNumber ?? -1;
	}

	protected bool IsOwnedCard(CardModel? card)
	{
		return card?.Owner == Owner;
	}

	protected bool IsOwnedAttack(CardModel? card)
	{
		return card != null && card.Owner == Owner && card.Type == CardType.Attack;
	}

	protected bool IsOwnedSkill(CardModel? card)
	{
		return card != null && card.Owner == Owner && card.Type == CardType.Skill;
	}

	protected static bool IsNetworkMultiplayer()
	{
		try
		{
			return RunManager.Instance?.NetService?.Type is NetGameType.Host or NetGameType.Client;
		}
		catch
		{
			return false;
		}
	}

	protected bool ShouldUseNetworkCombatHistory()
	{
		return IsNetworkMultiplayer()
			&& CombatManager.Instance?.IsInProgress == true
			&& Owner != null;
	}

	protected int CountOwnedAttackCardsPlayedFromHistory(bool firstInSeriesOnly = true, bool includeAutoPlay = false)
	{
		if (Owner == null)
		{
			return 0;
		}

		ulong ownerId = Owner.NetId;
		return CombatManager.Instance.History.Entries
			.OfType<CardPlayFinishedEntry>()
			.Count(entry =>
				(!firstInSeriesOnly || entry.CardPlay.IsFirstInSeries)
				&& (includeAutoPlay || !entry.CardPlay.IsAutoPlay)
				&& entry.CardPlay.Card.Type == CardType.Attack
				&& entry.CardPlay.Card.Owner?.NetId == ownerId);
	}

	protected int CountOwnedCardsDrawnFromHistory()
	{
		if (Owner == null)
		{
			return 0;
		}

		ulong ownerId = Owner.NetId;
		return CombatManager.Instance.History.Entries
			.OfType<CardDrawnEntry>()
			.Count(entry => entry.Card.Owner?.NetId == ownerId);
	}

	protected bool IsOwnedNonXCardWithCostAtLeast(CardModel? card, decimal minimumCost)
	{
		return card != null
			&& card.Owner == Owner
			&& !card.EnergyCost.CostsX
			&& card.EnergyCost.GetAmountToSpend() >= minimumCost;
	}

	protected bool IsOwnerOrPet(Creature? dealer)
	{
		return dealer == Owner?.Creature || dealer?.PetOwner == Owner;
	}

	protected bool IsDamageFromOwner(Creature? dealer, CardModel? cardSource)
	{
		if (Owner == null)
		{
			return false;
		}

		if (IsOwnerOrPet(dealer))
		{
			return true;
		}

		return cardSource?.Owner == Owner;
	}

	protected bool IsDefectPlayer(Player player)
	{
		return player.Character.Id == ModelDb.GetId<Defect>();
	}

	protected bool IsDefectOwner => Owner != null && IsDefectPlayer(Owner);

	protected bool IsIroncladPlayer(Player player)
	{
		return player.Character.Id == ModelDb.GetId<Ironclad>();
	}

	protected bool IsSilentPlayer(Player player)
	{
		return player.Character.Id == ModelDb.GetId<Silent>();
	}

	protected bool IsRegentPlayer(Player player)
	{
		return player.Character.Id == ModelDb.GetId<Regent>();
	}

	protected bool IsRegentOwner => Owner != null && IsRegentPlayer(Owner);

	protected bool IsNecrobinderPlayer(Player player)
	{
		return player.Character.Id == ModelDb.GetId<Necrobinder>();
	}

	protected void FlashDeferred(IEnumerable<Creature>? targets = null)
	{
		Creature[] targetArray = targets?.ToArray() ?? Array.Empty<Creature>();
		Callable.From(() => Flash(targetArray)).CallDeferred();
	}

	protected async Task AddCardCopiesToDeckOrHand<TCard>(int count)
		where TCard : CardModel
	{
		if (Owner == null || count <= 0)
		{
			return;
		}

		HextechCombatState? combatState = Owner.Creature.CombatState;
		if (Owner.PlayerCombatState != null
			&& combatState != null
			&& CombatManager.Instance.IsInProgress
			&& !CombatManager.Instance.IsOverOrEnding)
		{
			List<CardModel> cards = new(count);
			for (int i = 0; i < count; i++)
			{
				cards.Add(combatState.CreateCard<TCard>(Owner));
			}

			await HextechCardGeneration.AddGeneratedCardsToCombat(cards, PileType.Hand, addedByPlayer: true);

			return;
		}

		List<CardPileAddResult> results = new(count);
		for (int i = 0; i < count; i++)
		{
			CardModel card = Owner.RunState.CreateCard<TCard>(Owner);
			results.Add(await CardPileCmd.Add(card, PileType.Deck));
			SaveManager.Instance.MarkCardAsSeen(card);
		}

		CardCmd.PreviewCardPileAdd(results, 2f);
	}

	protected async Task AddCardCopiesToCombatHand<TCard>(int count)
		where TCard : CardModel
	{
		if (Owner == null
			|| count <= 0
			|| Owner.PlayerCombatState == null
			|| Owner.Creature.CombatState is not HextechCombatState combatState
			|| !CombatManager.Instance.IsInProgress
			|| CombatManager.Instance.IsOverOrEnding)
		{
			return;
		}

		List<CardModel> cards = new(count);
		for (int i = 0; i < count; i++)
		{
			cards.Add(combatState.CreateCard<TCard>(Owner));
		}

		await HextechCardGeneration.AddGeneratedCardsToCombat(cards, PileType.Hand, addedByPlayer: true);
	}

	private string GetResolvedIconPath()
	{
		string? customPath = HextechAssets.TryGetCustomRelicIconPath(this);
		if (!string.IsNullOrEmpty(customPath) && ResourceLoader.Exists(customPath))
		{
			return customPath;
		}

		return PlaceholderIconPath;
	}

	protected bool TryGetOwnedEnemyDebuffTarget(PowerModel power, decimal amount, Creature? applier, out Creature? target)
	{
		target = power.Owner;
		return amount > 0m
			&& target?.Side == CombatSide.Enemy
			&& applier == Owner?.Creature
			&& power.GetTypeForAmount(amount) == PowerType.Debuff
			&& power is not ITemporaryPower;
	}
}

public abstract class LimitedDebuffProcRelicBase : HextechRelicBase
{
	private int _procsThisTurn;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int SavedProcsThisTurn
	{
		get
		{
			EnsureTurnScopedStateCurrent(ResetProcs);
			return _procsThisTurn;
		}
		set
		{
			_procsThisTurn = Math.Max(0, value);
			UpdateDisplay();
			UpdateTurnScopedStateIdentity();
		}
	}

	protected virtual int MaxProcsPerTurn => 3;

	public override bool ShowCounter => CombatManager.Instance?.IsInProgress == true && !IsCanonical;

	public override int DisplayAmount => !IsCanonical ? Math.Max(0, MaxProcsPerTurn - _procsThisTurn) : 0;

	public override Task BeforeCombatStart()
	{
		ResetProcs(null);
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ResetProcs(null);
		return Task.CompletedTask;
	}

	public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		if (Owner != null && side == Owner.Creature.Side)
		{
			ResetProcs(combatState);
		}

		return Task.CompletedTask;
	}

#if STS2_104_OR_NEWER
	public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
#else
	public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
#endif
	{
		EnsureTurnScopedStateCurrent(ResetProcs);
		if (!TryGetOwnedEnemyDebuffTarget(power, amount, applier, out Creature? target) || _procsThisTurn >= MaxProcsPerTurn)
		{
			return;
		}

		_procsThisTurn++;
		UpdateDisplay();
		Flash(target == null ? Array.Empty<Creature>() : [target]);
		await OnEnemyDebuffApplied(target!);
	}

	protected abstract Task OnEnemyDebuffApplied(Creature target);

	private void ResetProcs()
	{
		ResetProcs(null);
	}

	private void ResetProcs(HextechCombatState? combatState)
	{
		_procsThisTurn = 0;
		UpdateDisplay();
		UpdateTurnScopedStateIdentity(combatState);
	}

	private void UpdateDisplay()
	{
		Status = _procsThisTurn == MaxProcsPerTurn - 1 ? RelicStatus.Active : RelicStatus.Normal;
		InvokeDisplayAmountChanged();
	}
}

public abstract class AttributeConversionRelicBase : HextechRelicBase
{
	private bool _isConverting;
	private decimal? _pendingAmount;
	private Creature? _pendingApplier;
	private CardModel? _pendingCardSource;

	protected abstract bool ShouldConvert(PowerModel canonicalPower);

	protected abstract bool ShouldConvertAppliedPower(PowerModel power);

	protected abstract Task ApplyConvertedPower(decimal amount, Creature? applier, CardModel? cardSource);

	protected abstract Task RevertOriginalPower(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource);

	public override Task AfterCombatEnd(CombatRoom room)
	{
		_pendingAmount = null;
		_pendingApplier = null;
		_pendingCardSource = null;
		_isConverting = false;
		return Task.CompletedTask;
	}

	public override bool TryModifyPowerAmountReceived(PowerModel canonicalPower, Creature target, decimal amount, Creature? applier, out decimal modifiedAmount)
	{
		modifiedAmount = amount;
		if (_isConverting || Owner == null || target != Owner.Creature || amount == 0m || !ShouldConvert(canonicalPower))
		{
			return false;
		}

		// Replace the original stat change with the converted one after the hook pipeline finishes.
		_pendingAmount = amount;
		_pendingApplier = applier;
		_pendingCardSource = null;
		modifiedAmount = 0m;
		return true;
	}

	public override async Task AfterModifyingPowerAmountReceived(PowerModel power)
	{
		if (_pendingAmount is not decimal amount)
		{
			return;
		}

		Creature? applier = _pendingApplier;
		CardModel? cardSource = _pendingCardSource;
		_pendingAmount = null;
		_pendingApplier = null;
		_pendingCardSource = null;

		_isConverting = true;
		try
		{
			Flash();
			await ApplyConvertedPower(amount, applier, cardSource);
		}
		finally
		{
			_isConverting = false;
		}
	}

#if STS2_104_OR_NEWER
	public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
#else
	public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
#endif
	{
		if (_isConverting || Owner == null || amount == 0m || power.Owner != Owner.Creature || !ShouldConvertAppliedPower(power))
		{
			return;
		}

		_isConverting = true;
		try
		{
			Flash();
			await RevertOriginalPower(power, amount, applier, cardSource);
			await ApplyConvertedPower(amount, applier, cardSource);
		}
		finally
		{
			_isConverting = false;
		}
	}
}
