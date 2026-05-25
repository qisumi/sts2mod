using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
	private const string HarmonyId = "Natsuki.HextechRunes";

	private static readonly object InitializeLock = new();
	private static Harmony? _harmony;
	private static bool _initialized;

	public static void Initialize()
	{
		lock (InitializeLock)
		{
			if (_initialized)
			{
				Log.Info($"[{ModInfo.Id}] Initialization already completed; skipping duplicate call.");
				return;
			}

			HextechModelBootstrap.Install();
			HextechTelemetry.Initialize();
			Harmony harmony = _harmony ??= new Harmony(HarmonyId);
			ThoughtOverwriteKeywordPersistenceHooks.Install(harmony);
			HextechCustomRunModifierHooks.Install(harmony);
			HextechRunLifecycleHooks.Install(harmony);
			HextechCombatHooks.Install(harmony);
			HextechEnemyPowerScalingHooks.Install(harmony);
			HextechUpdateChecker.Install(harmony);
			TryInstallOptionalHookGroup("inspect relic screen", () => HextechInspectHooks.Install(harmony));
			AssetHooks.Install(harmony);
			TryInstallOptionalHookGroup("relic collection", () => CollectionHooks.Install(harmony));
			TryInstallOptionalHookGroup("shop random forge", () => HextechShopForgeHooks.Install(harmony));
			TryInstallOptionalHookGroup("forge stacking", () => HextechForgeStackingHooks.Install(harmony));
			TryInstallOptionalHookGroup("relic UI safety", () => HextechUiSafetyHooks.Install(harmony));
			TryInstallOptionalHookGroup("reward serialization safety", () => HextechRewardSafetyHooks.Install(harmony));
			TryInstallOptionalHookGroup("relic visibility toggle", () => HextechRelicVisibilityHooks.Install(harmony));
			TryInstallOptionalHookGroup("game over score line compatibility", () => HextechGameOverCompatibilityHooks.Install(harmony));
			_initialized = true;
			Log.Info($"[{ModInfo.Id}] Loaded for Slay the Spire 2 {ModInfo.TargetGameVersion}.");
		}
	}

	internal static HextechMayhemModifier EnsureMayhemModifier(RunState runState)
	{
		return HextechRunLifecycleHooks.EnsureMayhemModifier(runState);
	}

	internal static Task HandleHextechActStarted(HextechMayhemModifier modifier)
	{
		return HextechRunLifecycleHooks.HandleHextechActStarted(modifier);
	}

	private static void TryInstallOptionalHookGroup(string label, Action install)
	{
		try
		{
			install();
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] Optional hook group skipped: {label}: {ex.GetType().Name}: {ex.Message}");
		}
	}
}
