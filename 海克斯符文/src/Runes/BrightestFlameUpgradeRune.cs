using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Cards;

namespace HextechRunes;

public sealed class BrightestFlameUpgradeRune : CardUpgradeRuneBase<BrightestFlame>
{
	private decimal? _maxHpBeforePlay;
	private decimal? _currentHpBeforePlay;

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromCard<BrightestFlame>()
	];

	protected override bool IsAvailableForCharacter(Player player)
	{
		return true;
	}

	public override Task BeforeCardPlayed(CardPlay cardPlay)
	{
		if (Owner != null && cardPlay.Card.Owner == Owner && cardPlay.Card is BrightestFlame)
		{
			_maxHpBeforePlay = Owner.Creature.MaxHp;
			_currentHpBeforePlay = Owner.Creature.CurrentHp;
		}
		else
		{
			_maxHpBeforePlay = null;
			_currentHpBeforePlay = null;
		}

		return Task.CompletedTask;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		try
		{
			if (Owner == null
				|| cardPlay.Card.Owner != Owner
				|| cardPlay.Card is not BrightestFlame
				|| _maxHpBeforePlay is not decimal maxHpBefore)
			{
				return;
			}

			if (Owner.Creature.MaxHp >= maxHpBefore)
			{
				return;
			}

			Flash();
			await CreatureCmd.SetMaxHp(Owner.Creature, maxHpBefore);
			if (_currentHpBeforePlay is decimal currentHpBefore && Owner.Creature.CurrentHp < currentHpBefore)
			{
				await CreatureCmd.SetCurrentHp(Owner.Creature, Math.Min(currentHpBefore, maxHpBefore));
			}
		}
		finally
		{
			_maxHpBeforePlay = null;
			_currentHpBeforePlay = null;
		}
	}
}
