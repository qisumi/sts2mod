using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes;

public sealed class ViolenceRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(1)
	];

	public override async Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, HextechCombatState combatState)
	{
		if (player != Owner || Owner == null || Owner.Creature.IsDead)
		{
			return;
		}

		List<CardModel> attacks = PileType.Draw.GetPile(Owner).Cards
			.Where(card => card.Owner == Owner && IllusoryWeaponRune.IsAttackForEffects(card, Owner))
			.ToList();
		if (attacks.Count == 0)
		{
			return;
		}

		CardModel card = HextechStableRandom.Pick(
			attacks,
			(RunState)Owner.RunState,
			HextechStableRandom.CardKey,
			"violence-draw-attack",
			HextechStableRandom.PlayerKey(Owner),
			combatState.RoundNumber.ToString(),
			HextechStableRandom.CardPileKey(attacks));
		card.SetToFreeThisTurn();

		Flash();
		await CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Top, this);
	}
}
