using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves.Runs;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static class HextechRewardSafetyHooks
{
	public static void Install(Harmony harmony)
	{
		harmony.Patch(
			RequireMethod(typeof(Reward), nameof(Reward.FromSerializable), BindingFlags.Public | BindingFlags.Static, typeof(SerializableReward), typeof(Player)),
			postfix: new HarmonyMethod(typeof(HextechRewardSafetyHooks), nameof(RewardFromSerializablePostfix)));
	}

	private static void RewardFromSerializablePostfix(SerializableReward save, Player player, ref Reward __result)
	{
		if (save.RewardType != RewardType.Gold || save.GoldAmount >= 0 || __result is not GoldReward)
		{
			return;
		}

		__result = new GoldReward(0, player, save.WasGoldStolenBack);
		Log.Warn($"[{ModInfo.Id}][Rewards] Repaired serialized gold reward with negative amount {save.GoldAmount}; defaulting to 0 gold.");
	}
}
