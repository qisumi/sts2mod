using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace HextechRunes;

internal static partial class HextechCatalog
{
	public readonly record struct RuneSeriesGroup(string LocalizationKey, IReadOnlyList<RelicModel> Relics);

	private readonly record struct CharacterRunePool(string LocalizationKey, IReadOnlyList<Type> RuneTypes);

	private static readonly IReadOnlyList<Type> SilverRuneTypes = HextechContentRegistry.SilverRuneTypes;

	private static readonly IReadOnlyList<Type> GoldRuneTypes = HextechContentRegistry.GoldRuneTypes;

	private static readonly IReadOnlyList<Type> PrismaticRuneTypes = HextechContentRegistry.PrismaticRuneTypes;

	private static readonly IReadOnlyList<Type> SilverForgeTypes = HextechContentRegistry.SilverForgeTypes;

	private static readonly IReadOnlyList<Type> GoldForgeTypes = HextechContentRegistry.GoldForgeTypes;

	private static readonly IReadOnlyList<Type> PrismaticForgeTypes = HextechContentRegistry.PrismaticForgeTypes;

	private static readonly IReadOnlyList<Type> ShopOnlyRelicTypes = HextechContentRegistry.ShopOnlyRelicTypes;

	private static readonly IReadOnlySet<Type> DisabledPlayerRuneTypes = HextechContentRegistry.DisabledPlayerRuneTypes;

	private static readonly IReadOnlyList<CharacterRunePool> CharacterRunePools =
	[
		new("IRONCLAD", HextechContentRegistry.IroncladRuneTypes),
		new("SILENT", HextechContentRegistry.SilentRuneTypes),
		new("REGENT", HextechContentRegistry.RegentRuneTypes),
		new("DEFECT", HextechContentRegistry.DefectRuneTypes),
		new("NECROBINDER", HextechContentRegistry.NecrobinderRuneTypes)
	];

	private static readonly IReadOnlySet<Type> CharacterSpecificRuneTypes = CharacterRunePools
		.SelectMany(static pool => pool.RuneTypes)
		.ToHashSet();

	private static readonly IReadOnlyList<Type> AttributeConversionExclusiveRuneTypes = HextechContentRegistry.AttributeConversionExclusiveRuneTypes;

	private static readonly IReadOnlySet<Type> FirstActExcludedRuneTypes = HextechContentRegistry.FirstActExcludedRuneTypes;

	private static readonly IReadOnlySet<Type> ThirdActExcludedRuneTypes = HextechContentRegistry.ThirdActExcludedRuneTypes;

	private static readonly IReadOnlyList<Type> AllRuneTypes = HextechContentRegistry.AllRuneTypes;

	private static readonly IReadOnlyList<Type> AllForgeTypes = HextechContentRegistry.AllForgeTypes;

	private static readonly IReadOnlyList<Type> AllCustomRelicTypes = HextechContentRegistry.AllCustomRelicTypes;

	private static readonly IReadOnlyList<Type> CustomCardTypes = HextechContentRegistry.CustomCardTypes;

	public static IReadOnlyList<Type> GetAllRuneTypes() => AllRuneTypes;

	public static IReadOnlyList<Type> GetAllSelectableRuneTypes()
	{
		return Enum.GetValues<HextechRarityTier>()
			.SelectMany(GetPlayerRuneTypesForRarity)
			.ToArray();
	}

	public static bool IsPlayerRuneTypeSelectable(Type runeType)
	{
		return AllRuneTypes.Contains(runeType) && !DisabledPlayerRuneTypes.Contains(runeType);
	}

	public static IReadOnlyList<Type> GetGenericSelectableRuneTypes()
	{
		return GetAllSelectableRuneTypes()
			.Where(static type => !CharacterSpecificRuneTypes.Contains(type))
			.ToArray();
	}

	public static string GetPlayerRunePoolKey(RelicModel relic)
	{
		ModelId id = relic.CanonicalInstance?.Id ?? relic.Id;
		foreach (CharacterRunePool pool in CharacterRunePools)
		{
			foreach (Type runeType in pool.RuneTypes)
			{
				if (ModelDb.GetId(runeType) == id)
				{
					return pool.LocalizationKey;
				}
			}
		}

		return "GENERIC";
	}

	public static IReadOnlyList<Type> GetAllForgeTypes() => AllForgeTypes;

	public static IReadOnlyList<Type> GetAllCustomRelicTypes() => AllCustomRelicTypes;

	public static IReadOnlyList<Type> GetAllCustomCardTypes() => CustomCardTypes;

	public static IReadOnlyList<Type> GetPlayerRuneTypesForRarity(HextechRarityTier rarity)
	{
		IReadOnlyList<Type> runeTypes = rarity switch
		{
			HextechRarityTier.Silver => SilverRuneTypes,
			HextechRarityTier.Gold => GoldRuneTypes,
			HextechRarityTier.Prismatic => PrismaticRuneTypes,
			_ => Array.Empty<Type>()
		};
		return runeTypes.Where(static type => !DisabledPlayerRuneTypes.Contains(type)).ToArray();
	}

	public static IReadOnlyList<Type> GetForgeTypesForRarity(HextechRarityTier rarity)
	{
		return rarity switch
		{
			HextechRarityTier.Silver => SilverForgeTypes,
			HextechRarityTier.Gold => GoldForgeTypes,
			HextechRarityTier.Prismatic => PrismaticForgeTypes,
			_ => Array.Empty<Type>()
		};
	}

	public static bool IsAvailableForPlayer(RelicModel relic, Player player)
	{
		return relic is not HextechRelicBase hextechRelic || hextechRelic.IsAvailableForPlayer(player);
	}

	public static bool IsPlayerRuneAllowedInAct(Type runeType, int actIndex)
	{
		return actIndex switch
		{
			0 => !FirstActExcludedRuneTypes.Contains(runeType),
			2 => IsEndlessModeLoaded() || !ThirdActExcludedRuneTypes.Contains(runeType),
			_ => true
		};
	}

	private static bool IsEndlessModeLoaded()
	{
		foreach (object mod in EnumerateKnownMods())
		{
			if (IsLoadedEndlessModeMod(mod))
			{
				return true;
			}
		}

		return false;
	}

	private static IEnumerable<object> EnumerateKnownMods()
	{
		Type modManagerType = typeof(ModManager);
		object? mods =
			modManagerType.GetProperty("LoadedMods", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
			?? modManagerType.GetField("_loadedMods", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
			?? modManagerType.GetProperty("AllMods", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
			?? modManagerType.GetField("_mods", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

		if (mods is not IEnumerable enumerable)
		{
			yield break;
		}

		foreach (object? mod in enumerable)
		{
			if (mod != null)
			{
				yield return mod;
			}
		}
	}

	private static bool IsLoadedEndlessModeMod(object mod)
	{
		Type modType = mod.GetType();
		object? manifest = modType.GetField("manifest", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mod)
			?? modType.GetProperty("manifest", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mod);
		string? id = manifest?.GetType().GetField("id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manifest) as string
			?? manifest?.GetType().GetProperty("id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manifest) as string;
		if (!string.Equals(id, "EndlessMode", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		object? wasLoadedValue = modType.GetField("wasLoaded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mod)
			?? modType.GetProperty("wasLoaded", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mod);
		return wasLoadedValue is not bool wasLoaded || wasLoaded;
	}
}
