using MegaCrit.Sts2.Core.Logging;

namespace CustomDifficulty;

internal static class CustomDifficultySettings
{
	public const int MinTicks = 1;
	public const int MaxTicks = 50;
	public const int DefaultTicks = 10;

	private static int _monsterHpTicks = DefaultTicks;
	private static int _monsterAttackTicks = DefaultTicks;

	public static event Action? Changed;

	public static int MonsterHpTicks => _monsterHpTicks;

	public static int MonsterAttackTicks => _monsterAttackTicks;

	public static decimal MonsterHpMultiplier => TicksToMultiplier(_monsterHpTicks);

	public static decimal MonsterAttackMultiplier => TicksToMultiplier(_monsterAttackTicks);

	public static double MonsterHpSliderValue => TicksToSliderValue(_monsterHpTicks);

	public static double MonsterAttackSliderValue => TicksToSliderValue(_monsterAttackTicks);

	public static void SetLocal(int hpTicks, int attackTicks, bool broadcast)
	{
		SetInternal(hpTicks, attackTicks, broadcast, "local");
	}

	public static void SetRemote(int hpTicks, int attackTicks)
	{
		SetInternal(hpTicks, attackTicks, broadcast: false, "remote");
	}

	public static int SliderValueToTicks(double value)
	{
		return ClampTicks((int)Math.Round(value * 10.0, MidpointRounding.AwayFromZero));
	}

	public static double TicksToSliderValue(int ticks)
	{
		return ClampTicks(ticks) / 10.0;
	}

	public static string FormatMultiplier(int ticks)
	{
		return $"x{TicksToSliderValue(ticks):0.0}";
	}

	private static decimal TicksToMultiplier(int ticks)
	{
		return ClampTicks(ticks) / 10m;
	}

	private static void SetInternal(int hpTicks, int attackTicks, bool broadcast, string source)
	{
		int clampedHpTicks = ClampTicks(hpTicks);
		int clampedAttackTicks = ClampTicks(attackTicks);
		if (_monsterHpTicks == clampedHpTicks && _monsterAttackTicks == clampedAttackTicks)
		{
			return;
		}

		_monsterHpTicks = clampedHpTicks;
		_monsterAttackTicks = clampedAttackTicks;
		Log.Info($"[{ModInfo.Id}] Difficulty changed by {source}: hp={FormatMultiplier(_monsterHpTicks)} attack={FormatMultiplier(_monsterAttackTicks)}.");
		Changed?.Invoke();

		if (broadcast)
		{
			CustomDifficultySync.BroadcastSettings();
		}
	}

	private static int ClampTicks(int ticks)
	{
		return Math.Clamp(ticks, MinTicks, MaxTicks);
	}
}
