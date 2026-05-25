using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace KeystoneRunes;

internal static class Sts2Compat
{
	public static Task<T?> ApplyPower<T>(
		Creature target,
		decimal amount,
		Creature? applier,
		CardModel? cardSource,
		bool silent = false)
		where T : PowerModel
	{
#if STS2_104_OR_NEWER
		return PowerCmd.Apply<T>(new BlockingPlayerChoiceContext(), target, amount, applier, cardSource, silent);
#else
		return PowerCmd.Apply<T>(target, amount, applier, cardSource, silent);
#endif
	}

	public static Task SetPowerAmount<T>(
		Creature target,
		decimal amount,
		Creature? applier,
		CardModel? cardSource)
		where T : PowerModel
	{
#if STS2_104_OR_NEWER
		decimal offset = amount - target.GetPowerAmount<T>();
		return offset == 0m
			? Task.CompletedTask
			: ApplyPower<T>(target, offset, applier, cardSource);
#else
		return PowerCmd.SetAmount<T>(target, amount, applier, cardSource);
#endif
	}

	public static Task<CardPileAddResult> AddGeneratedCardToCombat(
		CardModel card,
		PileType newPileType,
		Player creator,
		CardPilePosition position)
	{
#if STS2_104_OR_NEWER
		return CardPileCmd.AddGeneratedCardToCombat(card, newPileType, creator, position);
#else
		return CardPileCmd.AddGeneratedCardToCombat(card, newPileType, addedByPlayer: true, position: position);
#endif
	}
}
