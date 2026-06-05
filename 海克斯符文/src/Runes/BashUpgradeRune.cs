using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes;

public sealed class BashUpgradeRune : CardUpgradeRuneBase<Bash>
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new PowerVar<StrengthPower>(2m),
		new DynamicVar("BreakStrength", 3m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<Bash>(),
		HoverTipFactory.FromCard<Break>(),
		HoverTipFactory.FromPower<StrengthPower>()
	];

	protected override bool DeckContainsRequiredCard(Player player)
	{
		return DeckContains<Bash>(player) || DeckContains<Break>(player);
	}

	protected override bool IsAvailableForCharacter(Player player)
	{
		return IsIroncladPlayer(player);
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (Owner == null || cardPlay.Card.Owner != Owner || Owner.Creature.IsDead)
		{
			return;
		}

		decimal amount = cardPlay.Card switch
		{
			Bash => DynamicVars["StrengthPower"].BaseValue,
			Break => DynamicVars["BreakStrength"].BaseValue,
			_ => 0m
		};
		if (amount <= 0m)
		{
			return;
		}

		Flash();
		await PowerCmd.Apply<StrengthPower>(Owner.Creature, amount, Owner.Creature, cardPlay.Card);
	}
}
