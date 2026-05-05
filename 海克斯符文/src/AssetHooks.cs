using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Relics;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static class AssetHooks
{
	private static readonly Dictionary<string, Texture2D> TextureCache = new();

	private static readonly FieldInfo? NRelicModelField = TryGetField(typeof(NRelic), "_model");

	public static void Install(Harmony harmony)
	{
		MethodInfo getRelicIcon = RequireGetter(typeof(RelicModel), nameof(RelicModel.Icon));
		MethodInfo getRelicIconOutline = RequireGetter(typeof(RelicModel), nameof(RelicModel.IconOutline));
		MethodInfo getRelicBigIcon = RequireGetter(typeof(RelicModel), nameof(RelicModel.BigIcon));
		MethodInfo? relicReload = TryGetMethod(typeof(NRelic), "Reload", BindingFlags.Instance | BindingFlags.NonPublic);
		MethodInfo getPowerIcon = RequireGetter(typeof(PowerModel), nameof(PowerModel.Icon));
		MethodInfo getPowerBigIcon = RequireGetter(typeof(PowerModel), nameof(PowerModel.BigIcon));
		MethodInfo getCardPortrait = RequireGetter(typeof(CardModel), nameof(CardModel.Portrait));

		harmony.Patch(getRelicIcon, postfix: new HarmonyMethod(typeof(AssetHooks), nameof(RelicIconPostfix)));
		harmony.Patch(getRelicIconOutline, postfix: new HarmonyMethod(typeof(AssetHooks), nameof(RelicIconOutlinePostfix)));
		harmony.Patch(getRelicBigIcon, postfix: new HarmonyMethod(typeof(AssetHooks), nameof(RelicBigIconPostfix)));
		if (relicReload != null && NRelicModelField != null)
		{
			harmony.Patch(relicReload, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(NRelicReloadPrefix)));
		}
		else
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] NRelic.Reload asset hook skipped: missing {(relicReload == null ? "NRelic.Reload" : "NRelic._model")}.");
		}
		harmony.Patch(getPowerIcon, postfix: new HarmonyMethod(typeof(AssetHooks), nameof(PowerIconPostfix)));
		harmony.Patch(getPowerBigIcon, postfix: new HarmonyMethod(typeof(AssetHooks), nameof(PowerBigIconPostfix)));
		harmony.Patch(getCardPortrait, postfix: new HarmonyMethod(typeof(AssetHooks), nameof(CardPortraitPostfix)));
	}

	private static void CardPortraitPostfix(CardModel __instance, ref Texture2D __result)
	{
		if (TryGetHextechCardTexture(__instance, out Texture2D? texture))
		{
			__result = texture!;
		}
	}

	private static void RelicIconPostfix(RelicModel __instance, ref Texture2D __result)
	{
		if (TryGetHextechRelicTexture(__instance, out Texture2D? texture))
		{
			__result = texture!;
		}
	}

	private static void RelicIconOutlinePostfix(RelicModel __instance, ref Texture2D __result)
	{
		if (TryGetHextechRelicTexture(__instance, out Texture2D? texture))
		{
			__result = texture!;
		}
	}

	private static void RelicBigIconPostfix(RelicModel __instance, ref Texture2D __result)
	{
		if (TryGetHextechRelicTexture(__instance, out Texture2D? texture))
		{
			__result = texture!;
		}
	}

	private static bool NRelicReloadPrefix(NRelic __instance)
	{
		if (!__instance.IsNodeReady()
			|| NRelicModelField == null
			|| NRelicModelField.GetValue(__instance) is not RelicModel model
			|| !TryGetHextechRelicTexture(model, out Texture2D? texture))
		{
			return true;
		}

		model.UpdateTexture(__instance.Icon);
		__instance.Icon.Texture = texture;
		__instance.Outline.Visible = false;
		return false;
	}

	private static void PowerIconPostfix(PowerModel __instance, ref Texture2D __result)
	{
		if (TryGetHextechPowerTexture(__instance, out Texture2D? texture))
		{
			__result = texture!;
		}
	}

	private static void PowerBigIconPostfix(PowerModel __instance, ref Texture2D __result)
	{
		if (TryGetHextechPowerTexture(__instance, out Texture2D? texture))
		{
			__result = texture!;
		}
	}

	private static bool TryGetHextechRelicTexture(RelicModel self, out Texture2D? texture)
	{
		texture = null;
		string? path = HextechAssets.TryGetCustomRelicIconPath(self);
		if (path == null)
		{
			return false;
		}

		texture = LoadPortableTexture(path);
		return texture != null;
	}

	private static bool TryGetHextechPowerTexture(PowerModel self, out Texture2D? texture)
	{
		texture = null;
		string? path = self switch
		{
			HextechBurnPower => $"res://{ModInfo.Id}/images/powers/hextechBurnPower.png",
			HextechAttackReplayPower => $"res://{ModInfo.Id}/images/powers/hextechAttackReplayPower.png",
			HextechOceanDragonSoulPower => HextechAssets.OceanDragonSoulPowerIconPath,
			HextechInfernalDragonSoulPower => HextechAssets.InfernalDragonSoulPowerIconPath,
			HextechDragonSoulPower => HextechAssets.HextechDragonSoulPowerIconPath,
			HextechMountainDragonSoulPower => HextechAssets.MountainDragonSoulPowerIconPath,
			HextechChemtechDragonSoulPower => HextechAssets.ChemtechDragonSoulPowerIconPath,
			HextechCloudDragonSoulPower => HextechAssets.CloudDragonSoulPowerIconPath,
			_ => null
		};
		if (path == null)
		{
			return false;
		}

		texture = LoadPortableTexture(path);
		return texture != null;
	}

	private static bool TryGetHextechCardTexture(CardModel self, out Texture2D? texture)
	{
		texture = null;
		string? path = self switch
		{
			ElicitCard => HextechAssets.ElicitCardPortraitPath,
			TrickMagicCard => HextechAssets.TrickMagicCardPortraitPath,
			BladeWaltzCard => HextechAssets.BladeWaltzCardPortraitPath,
			OceanDragonSoulCard => HextechAssets.OceanDragonSoulCardPortraitPath,
			InfernalDragonSoulCard => HextechAssets.InfernalDragonSoulCardPortraitPath,
			HextechDragonSoulCard => HextechAssets.HextechDragonSoulCardPortraitPath,
			MountainDragonSoulCard => HextechAssets.MountainDragonSoulCardPortraitPath,
			ChemtechDragonSoulCard => HextechAssets.ChemtechDragonSoulCardPortraitPath,
			CloudDragonSoulCard => HextechAssets.CloudDragonSoulCardPortraitPath,
			_ => null
		};
		if (path == null)
		{
			return false;
		}

		texture = LoadPortableTexture(path);
		return texture != null;
	}

	internal static Texture2D? LoadUiTexture(string path)
	{
		return LoadPortableTexture(path);
	}

	private static Texture2D? LoadPortableTexture(string path)
	{
		if (ResourceLoader.Load<Texture2D>(path) is Texture2D loadedTexture)
		{
			TextureCache[path] = loadedTexture;
			return loadedTexture;
		}

		if (TextureCache.TryGetValue(path, out Texture2D? cachedTexture))
		{
			return cachedTexture;
		}

		byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
		if (bytes.Length == 0)
		{
			return null;
		}

		Image image = new();
		Error err = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
			? image.LoadPngFromBuffer(bytes)
			: image.LoadJpgFromBuffer(bytes);
		if (err != Error.Ok)
		{
			return null;
		}

		PortableCompressedTexture2D texture = new();
		texture.CreateFromImage(image, PortableCompressedTexture2D.CompressionMode.Lossless);
		texture.ResourcePath = path;
		TextureCache[path] = texture;
		return texture;
	}

}
