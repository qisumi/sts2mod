using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes;

public sealed class OminousPactRune : HextechRelicBase
{
	private int _summoningFromDoomDepth;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<DoomPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsNecrobinderPlayer(player);
	}

#if STS2_104_OR_NEWER
	public override Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		return HandleDoomApplied(choiceContext, power, amount, applier);
	}
#else
	public override Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		return HandleDoomApplied(new BlockingPlayerChoiceContext(), power, amount, applier);
	}
#endif

	private async Task HandleDoomApplied(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier)
	{
		if (Owner == null
			|| Owner.Creature.IsDead
			|| power is not DoomPower
			|| power.Owner?.Side != CombatSide.Enemy
			|| applier != Owner.Creature
			|| amount <= 0m
			|| _summoningFromDoomDepth > 0)
		{
			return;
		}

		Flash(power.Owner == null ? Array.Empty<Creature>() : [power.Owner]);
		_summoningFromDoomDepth++;
		try
		{
			await OstyCmd.Summon(choiceContext, Owner, amount, this);
		}
		finally
		{
			_summoningFromDoomDepth--;
		}
	}
}
