using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes;

internal static class HextechGoldRewardHelper
{
	public static void AddFixedExtraGoldReward(CombatRoom room, Player player, int amount)
	{
		if (amount <= 0)
		{
			return;
		}

		room.AddExtraReward(player, new GoldReward(amount, player));
	}

	public static void AddStableRangedExtraGoldReward(
		CombatRoom room,
		Player player,
		int minInclusive,
		int maxInclusive,
		params string?[] saltParts)
	{
		AddFixedExtraGoldReward(room, player, StableGoldAmount(player, minInclusive, maxInclusive, saltParts));
	}

	private static int StableGoldAmount(Player player, int minInclusive, int maxInclusive, params string?[] saltParts)
	{
		if (maxInclusive < minInclusive)
		{
			throw new ArgumentOutOfRangeException(nameof(maxInclusive), maxInclusive, "Maximum gold must be at least the minimum gold.");
		}

		string?[] fullSalt = new string?[saltParts.Length + 2];
		fullSalt[0] = "gold-reward";
		fullSalt[1] = HextechStableRandom.PlayerKey(player);
		Array.Copy(saltParts, 0, fullSalt, 2, saltParts.Length);
		int range = maxInclusive - minInclusive + 1;
		return minInclusive + HextechStableRandom.Index((RunState)player.RunState, range, fullSalt);
	}
}
