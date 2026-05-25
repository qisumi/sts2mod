using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	private static readonly AsyncLocal<int> OutbreakPowerPoisonResponseDepth = new();
	private static readonly AsyncLocal<int> SleightOfFleshPowerDebuffResponseDepth = new();

	internal static bool IsResolvingOutbreakPowerPoisonResponse => OutbreakPowerPoisonResponseDepth.Value > 0;
	internal static bool IsResolvingSleightOfFleshPowerDebuffResponse => SleightOfFleshPowerDebuffResponseDepth.Value > 0;

	private static void OutbreakPowerAfterPowerAmountChangedPrefix(OutbreakPower __instance, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource, out bool __state)
	{
		__state = amount > 0m
			&& applier == __instance.Owner
			&& power is PoisonPower;
		if (__state)
		{
			OutbreakPowerPoisonResponseDepth.Value++;
		}
	}

	private static void OutbreakPowerAfterPowerAmountChangedPostfix(bool __state, ref Task __result)
	{
		if (__state)
		{
			__result = CompleteWithOutbreakPowerPoisonResponseReset(__result);
		}
	}

	private static void SleightOfFleshPowerAfterPowerAmountChangedPrefix(SleightOfFleshPower __instance, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource, out bool __state)
	{
		__state = amount != 0m
			&& power.GetTypeForAmount(amount) == PowerType.Debuff
			&& power.Owner.IsEnemy
			&& applier == __instance.Owner
			&& power is not ITemporaryPower;
		if (__state)
		{
			SleightOfFleshPowerDebuffResponseDepth.Value++;
		}
	}

	private static void SleightOfFleshPowerAfterPowerAmountChangedPostfix(bool __state, ref Task __result)
	{
		if (__state)
		{
			__result = CompleteWithSleightOfFleshPowerDebuffResponseReset(__result);
		}
	}

	private static async Task CompleteWithOutbreakPowerPoisonResponseReset(Task task)
	{
		try
		{
			await task;
		}
		finally
		{
			OutbreakPowerPoisonResponseDepth.Value = Math.Max(0, OutbreakPowerPoisonResponseDepth.Value - 1);
		}
	}

	private static async Task CompleteWithSleightOfFleshPowerDebuffResponseReset(Task task)
	{
		try
		{
			await task;
		}
		finally
		{
			SleightOfFleshPowerDebuffResponseDepth.Value = Math.Max(0, SleightOfFleshPowerDebuffResponseDepth.Value - 1);
		}
	}
}
