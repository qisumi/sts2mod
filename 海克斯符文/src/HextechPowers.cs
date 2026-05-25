using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class HextechBurnPower : HextechPowerBase
{
	private const decimal StackDecayPercent = 0.1m;
	private static int _resolveDepth;

	internal static bool IsResolvingDamage => _resolveDepth > 0;

	public override PowerType Type => PowerType.Debuff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Side || Amount <= 0 || !Owner.IsAlive)
		{
			return;
		}

		int stacks = Amount;
		int percentHpLoss = Math.Max(1, (int)Math.Floor(Owner.CurrentHp * stacks / 100m));
		int hpLoss = Math.Max(stacks, percentHpLoss);
		int stackLoss = Math.Max(1, (int)Math.Ceiling(stacks * StackDecayPercent));
		Flash();
		try
		{
			_resolveDepth++;
			await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), Owner, hpLoss, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
		}
		finally
		{
			_resolveDepth--;
		}

		if (Owner.IsAlive)
		{
			await PowerCmd.Apply<HextechBurnPower>(Owner, -stackLoss, null, null);
		}
		else
		{
			await Cmd.CustomScaledWait(0.1f, 0.25f);
		}
	}
}

public sealed class HextechTemporaryStrengthPower : TemporaryStrengthPower
{
	public override AbstractModel OriginModel => ModelDb.Relic<MasterOfDualityRune>();

	protected override bool IsVisibleInternal => false;
}

public sealed class HextechTemporaryDexterityPower : TemporaryDexterityPower
{
	public override AbstractModel OriginModel => ModelDb.Relic<MasterOfDualityRune>();

	protected override bool IsVisibleInternal => false;
}

public sealed class HextechTemporaryStrengthLossPower : TemporaryStrengthPower
{
	public override AbstractModel OriginModel => ModelDb.Relic<MasterOfDualityRune>();

	protected override bool IsVisibleInternal => false;

	protected override bool IsPositive => false;
}

public sealed class HextechTemporaryDexterityLossPower : TemporaryDexterityPower
{
	public override AbstractModel OriginModel => ModelDb.Relic<MasterOfDualityRune>();

	protected override bool IsVisibleInternal => false;

	protected override bool IsPositive => false;
}

public sealed class HextechLethalTempoTemporaryStrengthPower : TemporaryStrengthPower
{
	public override AbstractModel OriginModel => ModelDb.Relic<LethalTempoRune>();

	protected override bool IsVisibleInternal => false;
}

public sealed class HextechBloodPactTemporaryStrengthPower : TemporaryStrengthPower
{
	public override AbstractModel OriginModel => ModelDb.Relic<BloodPactRune>();

	protected override bool IsVisibleInternal => false;
}

public sealed class HextechPowerShieldTemporaryStrengthPower : TemporaryStrengthPower
{
	public override AbstractModel OriginModel => ModelDb.Relic<PowerShieldRune>();

	protected override bool IsVisibleInternal => false;
}

public sealed class HextechAttackReplayPower : PowerModel
{
	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	public override int ModifyCardPlayCount(CardModel card, Creature? target, int playCount)
	{
		if (Amount <= 0m
			|| card.Owner?.Creature != Owner
			|| card.Type != CardType.Attack)
		{
			return playCount;
		}

		return playCount + Amount;
	}

	public override async Task AfterModifyingCardPlayCount(CardModel card)
	{
		if (Amount <= 0m
			|| card.Owner?.Creature != Owner
			|| card.Type != CardType.Attack)
		{
			return;
		}

		Flash();
		await PowerCmd.Remove(this);
	}
}

public sealed class HextechTemporarySlowPower : HextechPowerBase, ITemporaryPower
{
	private bool _shouldIgnoreNextInstance;

	public override PowerType Type => PowerType.Debuff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override bool IsVisibleInternal => false;

	public AbstractModel OriginModel => ModelDb.Relic<FrostWraithRune>();

	public PowerModel InternallyAppliedPower => ModelDb.Power<SlowPower>();

	public override LocString Title => ModelDb.Power<SlowPower>().Title;

	public override LocString Description => ModelDb.Power<SlowPower>().Description;

	public void IgnoreNextInstance()
	{
		_shouldIgnoreNextInstance = true;
	}

	public override async Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource)
	{
		if (_shouldIgnoreNextInstance)
		{
			_shouldIgnoreNextInstance = false;
			return;
		}

		await PowerCmd.Apply<SlowPower>(target, amount, applier, cardSource, silent: true);
	}

#if STS2_104_OR_NEWER
	public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
#else
	public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
#endif
	{
		if (power != this || amount == Amount)
		{
			return;
		}

		if (_shouldIgnoreNextInstance)
		{
			_shouldIgnoreNextInstance = false;
			return;
		}

		await PowerCmd.Apply<SlowPower>(Owner, amount, applier, cardSource, silent: true);
	}

	public override async Task AfterSideTurnStart(CombatSide side, HextechCombatState combatState)
	{
		if (side != Owner.Side)
		{
			return;
		}

		await PowerCmd.Remove(this);
		await PowerCmd.Apply<SlowPower>(Owner, -Amount, Owner, null, silent: true);
	}
}
