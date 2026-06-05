using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes;

public sealed class VoidFormUpgradeRune : CardUpgradeRuneBase<VoidForm>
{
	private static readonly HashSet<VoidForm> EndTurnAutoPlays = [];
	private bool _autoPlaying;

	protected override bool IsAvailableForCharacter(Player player)
	{
		return IsRegentPlayer(player);
	}

	public override async Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (_autoPlaying || Owner == null || Owner.Creature.IsDead || side != Owner.Creature.Side || !IsRegentPlayer(Owner))
		{
			return;
		}

		List<VoidForm> voidForms = PileType.Hand.GetPile(Owner).Cards
			.OfType<VoidForm>()
			.Where(card => card.Owner == Owner)
			.ToList();
		if (voidForms.Count == 0)
		{
			return;
		}

		_autoPlaying = true;
		try
		{
			Flash();
			foreach (VoidForm card in voidForms)
			{
				if (card.Pile?.Type != PileType.Hand)
				{
					continue;
				}

				EndTurnAutoPlays.Add(card);
				try
				{
					await HextechAutoPlayHelper.AutoPlayOrMoveToResultPile(choiceContext, card, target: null);
				}
				finally
				{
					EndTurnAutoPlays.Remove(card);
				}
			}
		}
		finally
		{
			EndTurnAutoPlays.Clear();
			_autoPlaying = false;
		}
	}

	public static bool ShouldUseUpgradedPlay(VoidForm card)
	{
		return EndTurnAutoPlays.Contains(card)
			&& card.Owner.GetRelic<VoidFormUpgradeRune>() != null;
	}

	public static async Task PlayUpgraded(PlayerChoiceContext choiceContext, VoidForm card, CardPlay cardPlay)
	{
		await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", card.Owner.Character.CastAnimDelay);
		await PowerCmd.Apply<VoidFormPower>(
			card.Owner.Creature,
			card.DynamicVars["VoidFormPower"].BaseValue,
			card.Owner.Creature,
			card);
		card.Owner.GetRelic<VoidFormUpgradeRune>()?.Flash();
	}
}
