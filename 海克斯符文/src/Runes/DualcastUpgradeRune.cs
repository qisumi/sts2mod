using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;

namespace HextechRunes;

public sealed class DualcastUpgradeRune : CardUpgradeRuneBase<Dualcast>
{
	private CardModel? _trackedCard;
	private Type? _lastEvokedOrbType;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Dualcast>(),
		HoverTipFactory.FromCard<Quadcast>()
	];

	protected override bool DeckContainsRequiredCard(Player player)
	{
		return DeckContains<Dualcast>(player) || DeckContains<Quadcast>(player);
	}

	protected override bool IsAvailableForCharacter(Player player)
	{
		return IsDefectPlayer(player);
	}

	public override Task BeforeCardPlayed(CardPlay cardPlay)
	{
		if (Owner != null && cardPlay.Card.Owner == Owner && IsSupportedCard(cardPlay.Card))
		{
			_trackedCard = cardPlay.Card;
			_lastEvokedOrbType = null;
		}
		else
		{
			_trackedCard = null;
			_lastEvokedOrbType = null;
		}

		return Task.CompletedTask;
	}

	public override async Task AfterOrbEvoked(PlayerChoiceContext choiceContext, OrbModel orb, IEnumerable<Creature> targets)
	{
		if (Owner == null || _trackedCard == null || orb.Owner != Owner)
		{
			return;
		}

		_lastEvokedOrbType = orb.GetType();
		await Task.CompletedTask;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		try
		{
			if (Owner == null || cardPlay.Card != _trackedCard || _lastEvokedOrbType == null)
			{
				return;
			}

			if (await TryChannelOrbOfType(context, Owner, _lastEvokedOrbType))
			{
				Flash();
			}
		}
		finally
		{
			if (cardPlay.Card == _trackedCard)
			{
				_trackedCard = null;
				_lastEvokedOrbType = null;
			}
		}
	}

	private static async Task<bool> TryChannelOrbOfType(PlayerChoiceContext context, Player player, Type orbType)
	{
		if (orbType == typeof(LightningOrb))
		{
			await OrbCmd.Channel<LightningOrb>(context, player);
			return true;
		}

		if (orbType == typeof(FrostOrb))
		{
			await OrbCmd.Channel<FrostOrb>(context, player);
			return true;
		}

		if (orbType == typeof(DarkOrb))
		{
			await OrbCmd.Channel<DarkOrb>(context, player);
			return true;
		}

		if (orbType == typeof(PlasmaOrb))
		{
			await OrbCmd.Channel<PlasmaOrb>(context, player);
			return true;
		}

		if (Activator.CreateInstance(orbType) is OrbModel orb)
		{
			await OrbCmd.Channel(context, orb, player);
			return true;
		}

		return false;
	}

	private static bool IsSupportedCard(CardModel card)
	{
		return card is Dualcast or Quadcast;
	}
}
