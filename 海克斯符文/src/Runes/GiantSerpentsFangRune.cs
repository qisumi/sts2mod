using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class GiantSerpentsFangRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("BlockReductionPercent", 50m)
	];

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (Owner == null
			|| Owner.Creature.IsDead
			|| target.Side != CombatSide.Enemy
			|| target.Block <= 0
			|| result.TotalDamage <= 0
			|| !IsDamageFromOwner(dealer, cardSource))
		{
			return;
		}

		int blockLoss = Math.Max(1, (int)Math.Ceiling(target.Block * DynamicVars["BlockReductionPercent"].BaseValue / 100m));
		Flash([target]);
		await CreatureCmd.LoseBlock(target, blockLoss);
	}
}
