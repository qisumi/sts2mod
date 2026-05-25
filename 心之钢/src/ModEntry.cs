using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Heartsteel;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
	private const string HarmonyId = "Natsuki.Heartsteel";

	private static Harmony? HarmonyInstance;

	public static void Initialize()
	{
		Harmony harmony = HarmonyInstance ??= new Harmony(HarmonyId);
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HeartsteelRelic));
		ModHelper.AddModelToPool<SharedRelicPool, HeartsteelRelic>();
		AssetHooks.Install(harmony);
		OrnnsForgeRegistration.Install(harmony);
		Log.Info($"[{ModInfo.Id}] Loaded for Slay the Spire 2 {ModInfo.TargetGameVersion}.");
	}
}
