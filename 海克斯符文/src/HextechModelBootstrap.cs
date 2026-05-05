using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Saves.Runs;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static class HextechModelBootstrap
{
	private static readonly MethodInfo AddModelToPoolMethod = typeof(ModHelper).GetMethods(BindingFlags.Public | BindingFlags.Static)
		.Single(method => method.Name == "AddModelToPool" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 2);
	private static readonly object InstallLock = new();
	private static bool _installed;

	public static void Install()
	{
		lock (InstallLock)
		{
			if (_installed)
			{
				Log.Info($"[{ModInfo.Id}] Model bootstrap already installed; skipping duplicate registration.");
				return;
			}

			PreloadDependencyAssemblies();
			InjectSavedPropertyCaches();
			RegisterModels();
			_installed = true;
		}
	}

	private static void InjectSavedPropertyCaches()
	{
		foreach (Type type in HextechCatalog.GetAllCustomRelicTypes())
		{
			SavedPropertiesTypeCache.InjectTypeIntoCache(type);
		}

		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechMayhemModifier));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechBurnPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporaryStrengthPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporaryDexterityPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporaryStrengthLossPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporaryDexterityLossPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechLethalTempoTemporaryStrengthPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechBloodPactTemporaryStrengthPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechAttackReplayPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechTemporarySlowPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechOceanDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechInfernalDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechMountainDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechChemtechDragonSoulPower));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(HextechCloudDragonSoulPower));
		EnsureSavedPropertyNetIdBitSize();
	}

	private static void EnsureSavedPropertyNetIdBitSize()
	{
		const int minimumBitSize = 16;
		const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

		FieldInfo? mapField = TryGetField(typeof(SavedPropertiesTypeCache), "_netIdToPropertyNameMap", flags);
		int propertyNameCount = (mapField?.GetValue(null) as System.Collections.ICollection)?.Count ?? 0;
		int requiredBitSize = GetRequiredBitSize(propertyNameCount);
		int targetBitSize = Math.Max(minimumBitSize, requiredBitSize);
		int currentBitSize = SavedPropertiesTypeCache.NetIdBitSize;
		if (currentBitSize >= targetBitSize)
		{
			Log.Info($"[{ModInfo.Id}][Mayhem] SavedPropertiesTypeCache NetIdBitSize unchanged: bitSize={currentBitSize} propertyNames={propertyNameCount}");
			return;
		}

		FieldInfo? backingField = TryGetField(typeof(SavedPropertiesTypeCache), "<NetIdBitSize>k__BackingField", flags);
		if (backingField == null)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] SavedPropertiesTypeCache NetIdBitSize backing field not found; custom saved properties may desync in multiplayer.");
			return;
		}

		backingField.SetValue(null, targetBitSize);
		Log.Info($"[{ModInfo.Id}][Mayhem] SavedPropertiesTypeCache NetIdBitSize updated: old={currentBitSize} new={targetBitSize} propertyNames={propertyNameCount}");
	}

	private static int GetRequiredBitSize(int valueCount)
	{
		int maxValue = Math.Max(1, valueCount - 1);
		int bits = 0;
		while (maxValue > 0)
		{
			bits++;
			maxValue >>= 1;
		}

		return bits;
	}

	private static void RegisterModels()
	{
		foreach (Type runeType in HextechCatalog.GetAllCustomRelicTypes())
		{
			AddModelToPoolMethod.MakeGenericMethod(typeof(SharedRelicPool), runeType).Invoke(null, null);
		}

		foreach (Type cardType in HextechCatalog.GetAllCustomCardTypes())
		{
			AddModelToPoolMethod.MakeGenericMethod(typeof(TokenCardPool), cardType).Invoke(null, null);
		}
	}

	private static void PreloadDependencyAssemblies()
	{
		Assembly assembly = Assembly.GetExecutingAssembly();
		string? modDirectory = Path.GetDirectoryName(assembly.Location);
		if (string.IsNullOrEmpty(modDirectory) || !Directory.Exists(modDirectory))
		{
			return;
		}

		string selfPath = assembly.Location;
		AssemblyLoadContext loadContext = AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default;
		foreach (string dllPath in Directory.GetFiles(modDirectory, "*.dll"))
		{
			if (string.Equals(dllPath, selfPath, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			loadContext.LoadFromAssemblyPath(dllPath);
		}
	}
}
