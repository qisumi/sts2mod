using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace EndlessMode;

internal enum EndlessOptionalReward
{
	MimicInfestation,
	TimeMaze,
	Muzzle,
	HorribleTrophy
}

internal sealed class EndlessModeConfig
{
	private const string ConfigFileName = "config.json";
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	private static EndlessModeConfig _current = LoadOrCreate();

	[JsonPropertyName("grant_mimic_infestation")]
	public bool GrantMimicInfestation { get; set; } = true;

	[JsonPropertyName("grant_time_maze")]
	public bool GrantTimeMaze { get; set; } = true;

	[JsonPropertyName("grant_muzzle")]
	public bool GrantMuzzle { get; set; } = true;

	[JsonPropertyName("grant_horrible_trophy")]
	public bool GrantHorribleTrophy { get; set; } = true;

	internal static EndlessModeConfig Current => _current;

	internal static EndlessOptionalReward GetOptionalRewardForTier(int rewardTier)
	{
		return Math.Clamp(rewardTier, 1, 4) switch
		{
			1 => EndlessOptionalReward.MimicInfestation,
			2 => EndlessOptionalReward.TimeMaze,
			3 => EndlessOptionalReward.Muzzle,
			_ => EndlessOptionalReward.HorribleTrophy
		};
	}

	internal static bool IsRewardEnabled(EndlessOptionalReward reward)
	{
		return reward switch
		{
			EndlessOptionalReward.MimicInfestation => Current.GrantMimicInfestation,
			EndlessOptionalReward.TimeMaze => Current.GrantTimeMaze,
			EndlessOptionalReward.Muzzle => Current.GrantMuzzle,
			EndlessOptionalReward.HorribleTrophy => Current.GrantHorribleTrophy,
			_ => true
		};
	}

	internal static int GetEnabledRewardFlags()
	{
		int flags = 0;
		foreach (EndlessOptionalReward reward in Enum.GetValues<EndlessOptionalReward>())
		{
			if (IsRewardEnabled(reward))
			{
				flags |= GetRewardFlag(reward);
			}
		}

		return flags;
	}

	internal static bool IsRewardEnabled(int enabledRewardFlags, EndlessOptionalReward reward)
	{
		return (enabledRewardFlags & GetRewardFlag(reward)) != 0;
	}

	internal static void SetRewardEnabled(EndlessOptionalReward reward, bool enabled)
	{
		switch (reward)
		{
			case EndlessOptionalReward.MimicInfestation:
				Current.GrantMimicInfestation = enabled;
				break;
			case EndlessOptionalReward.TimeMaze:
				Current.GrantTimeMaze = enabled;
				break;
			case EndlessOptionalReward.Muzzle:
				Current.GrantMuzzle = enabled;
				break;
			case EndlessOptionalReward.HorribleTrophy:
				Current.GrantHorribleTrophy = enabled;
				break;
		}

		SaveCurrent();
	}

	private static int GetRewardFlag(EndlessOptionalReward reward)
	{
		return 1 << (int)reward;
	}

	internal static string GetConfigPath()
	{
		return Path.Combine(ProjectSettings.GlobalizePath("user://"), ModEntryConstants.ModId, ConfigFileName);
	}

	private static EndlessModeConfig LoadOrCreate()
	{
		string configPath = GetConfigPath();
		if (!File.Exists(configPath))
		{
			EndlessModeConfig config = new();
			WriteConfig(configPath, config);
			return config;
		}

		try
		{
			EndlessModeConfig? config = JsonSerializer.Deserialize<EndlessModeConfig>(File.ReadAllText(configPath), JsonOptions);
			if (config != null)
			{
				WriteConfig(configPath, config);
				return config;
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModEntryConstants.ModId}] Failed to load config; rewriting defaults: {ex.Message}");
		}

		EndlessModeConfig defaultConfig = new();
		WriteConfig(configPath, defaultConfig);
		return defaultConfig;
	}

	private static void SaveCurrent()
	{
		WriteConfig(GetConfigPath(), Current);
	}

	private static void WriteConfig(string configPath, EndlessModeConfig config)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
		File.WriteAllText(configPath, JsonSerializer.Serialize(config, JsonOptions));
	}
}
