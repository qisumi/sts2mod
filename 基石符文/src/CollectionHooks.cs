using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Unlocks;

namespace KeystoneRunes;

internal static class CollectionHooks
{
	private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private const string StarterHeaderZh = "初始：";

	private const string StarterHeaderZhBody = "角色们开始游戏时自身携带的遗物。";

	private const string KeystoneHeaderZh = "基石：";

	private const string KeystoneHeaderZhBody = "来自英雄联盟里的符文遗物。";

	private const string StarterHeaderEn = "Starter:";

	private const string StarterHeaderEnBody = "Relics that characters start the game with.";

	private const string KeystoneHeaderEn = "Keystone:";

	private const string KeystoneHeaderEnBody = "Rune relics from League of Legends.";

	private static readonly FieldInfo? HeaderLabelField = TryGetField(typeof(NRelicCollectionCategory), "_headerLabel");

	private static readonly FieldInfo? SubCategoriesField = TryGetField(typeof(NRelicCollectionCategory), "_subCategories");

	private static readonly MethodInfo? CreateForSubcategoryMethod = TryGetMethod(typeof(NRelicCollectionCategory), "CreateForSubcategory");

	private static readonly MethodInfo? LoadSubcategoryMethod = TryGetMethod(
		typeof(NRelicCollectionCategory),
		"LoadSubcategory",
		typeof(NRelicCollection),
		typeof(LocString),
		typeof(IEnumerable<RelicModel>),
		typeof(HashSet<RelicModel>),
		typeof(HashSet<RelicModel>));

	private static string? _starterHeaderTemplate;

	public static void Install(Harmony harmony)
	{
		MethodInfo? loadRelicsMethod = TryGetMethod(
			typeof(NRelicCollectionCategory),
			"LoadRelics",
			typeof(RelicRarity),
			typeof(NRelicCollection),
			typeof(LocString),
			typeof(HashSet<RelicModel>),
			typeof(UnlockState),
			typeof(HashSet<RelicModel>));
		if (loadRelicsMethod == null
			|| HeaderLabelField == null
			|| SubCategoriesField == null
			|| CreateForSubcategoryMethod == null
			|| LoadSubcategoryMethod == null)
		{
			Log.Warn($"[{ModInfo.Id}] Relic collection subcategory hook skipped: missing {DescribeMissingCollectionMember(loadRelicsMethod)}.");
			return;
		}

		harmony.Patch(loadRelicsMethod, postfix: new HarmonyMethod(typeof(CollectionHooks), nameof(LoadRelicsPostfix)));
	}

	private static void LoadRelicsPostfix(
		NRelicCollectionCategory __instance,
		RelicRarity relicRarity,
		NRelicCollection collection,
		LocString header,
		HashSet<RelicModel> seenRelics,
		UnlockState unlockState,
		HashSet<RelicModel> allUnlockedRelics)
	{
		if (relicRarity != RelicRarity.Starter)
		{
			return;
		}

		_starterHeaderTemplate ??= header.GetRawText();

		AddKeystoneSubcategory(__instance, collection, seenRelics, allUnlockedRelics);
	}

	private static void AddKeystoneSubcategory(
		NRelicCollectionCategory self,
		NRelicCollection collection,
		HashSet<RelicModel> seenRelics,
		HashSet<RelicModel> allUnlockedRelics)
	{
		List<NRelicCollectionCategory> subCategories = GetSubCategories(self);
		if (collection.Relics.Any(ModInfo.IsKeystoneRelic))
		{
			return;
		}

		NRelicCollectionCategory subCategory = (NRelicCollectionCategory)CreateForSubcategoryMethod!.Invoke(self, null)!;
		int insertIndex = ((Control)HeaderLabelField!.GetValue(self)!).GetIndex() + subCategories.Count + 1;
		subCategories.Add(subCategory);
		self.AddChild(subCategory);
		self.MoveChild(subCategory, insertIndex);

		LoadSubcategoryMethod!.Invoke(
			subCategory,
			[
				collection,
				new LocString("relic_collection", ModInfo.KeystoneSubcategoryKey),
				ModInfo.GetCanonicalRunes(),
				seenRelics,
				allUnlockedRelics
			]);

		ApplyCustomHeaderText(subCategory);
	}

	private static void ApplyCustomHeaderText(NRelicCollectionCategory subCategory)
	{
		if (HeaderLabelField!.GetValue(subCategory) is not MegaRichTextLabel headerLabel)
		{
			return;
		}

		string fallback = new LocString("relic_collection", ModInfo.KeystoneSubcategoryKey).GetRawText();
		headerLabel.SetTextAutoSize(FormatLikeStarterHeader(_starterHeaderTemplate, fallback));
	}

	private static string FormatLikeStarterHeader(string? starterTemplate, string fallback)
	{
		if (string.IsNullOrWhiteSpace(starterTemplate))
		{
			return fallback;
		}

		string formatted = starterTemplate
			.Replace(StarterHeaderZh, KeystoneHeaderZh)
			.Replace(StarterHeaderZhBody, KeystoneHeaderZhBody)
			.Replace(StarterHeaderEn, KeystoneHeaderEn)
			.Replace(StarterHeaderEnBody, KeystoneHeaderEnBody);

		return formatted == starterTemplate ? fallback : formatted;
	}

	private static List<NRelicCollectionCategory> GetSubCategories(NRelicCollectionCategory category)
	{
		return (List<NRelicCollectionCategory>)SubCategoriesField!.GetValue(category)!;
	}

	private static string DescribeMissingCollectionMember(MethodInfo? loadRelicsMethod)
	{
		List<string> missing = [];
		if (loadRelicsMethod == null)
		{
			missing.Add("NRelicCollectionCategory.LoadRelics");
		}

		if (HeaderLabelField == null)
		{
			missing.Add("NRelicCollectionCategory._headerLabel");
		}

		if (SubCategoriesField == null)
		{
			missing.Add("NRelicCollectionCategory._subCategories");
		}

		if (CreateForSubcategoryMethod == null)
		{
			missing.Add("NRelicCollectionCategory.CreateForSubcategory");
		}

		if (LoadSubcategoryMethod == null)
		{
			missing.Add("NRelicCollectionCategory.LoadSubcategory");
		}

		return string.Join(", ", missing);
	}

	private static FieldInfo? TryGetField(Type type, string name)
	{
		return type.GetField(name, InstanceMembers);
	}

	private static MethodInfo? TryGetMethod(Type type, string name, params Type[] parameters)
	{
		return type.GetMethod(name, InstanceMembers, binder: null, parameters, modifiers: null);
	}
}
