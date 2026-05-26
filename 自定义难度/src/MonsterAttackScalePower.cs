using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace CustomDifficulty;

public sealed class MonsterAttackScalePower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Single;

	protected override bool IsVisibleInternal => false;

	public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (dealer != Owner || target == null || !target.IsPlayer)
		{
			return 1m;
		}

		if (!props.IsPoweredAttack())
		{
			return 1m;
		}

		decimal multiplier = CustomDifficultySettings.MonsterAttackMultiplier;
		return multiplier > 0m ? multiplier : 1m;
	}
}
