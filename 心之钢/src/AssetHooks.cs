using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace Heartsteel;

internal static class AssetHooks
{
	private static readonly Dictionary<string, Texture2D> TextureCache = new();

	private static bool _hooksInstalled;

	private static readonly FieldInfo? NRelicModelField = TryGetField(typeof(NRelic), "_model");

	private static readonly FieldInfo? CombatPowerModelField = TryGetField(typeof(NPower), "_model");

	private static readonly FieldInfo? CombatPowerIconField = TryGetField(typeof(NPower), "_icon");

	private static readonly FieldInfo? CombatPowerFlashField = TryGetField(typeof(NPower), "_powerFlash");

	public static void Install(Harmony harmony)
	{
		if (_hooksInstalled)
		{
			return;
		}

		if (TryGetGetter(typeof(RelicModel), nameof(RelicModel.Icon)) is { } getRelicIcon)
		{
			harmony.Patch(getRelicIcon, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(RelicIconPrefix)));
		}

		if (TryGetGetter(typeof(RelicModel), nameof(RelicModel.BigIcon)) is { } getRelicBigIcon)
		{
			harmony.Patch(getRelicBigIcon, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(RelicBigIconPrefix)));
		}

		if (NRelicModelField != null && TryGetMethod(typeof(NRelic), "Reload", BindingFlags.Instance | BindingFlags.NonPublic) is { } relicReload)
		{
			harmony.Patch(relicReload, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(NRelicReloadPrefix)));
		}
		else
		{
			Log.Warn("[Heartsteel] NRelic.Reload or NRelic._model not found; relic node icon reload hook disabled.");
		}

		if (TryGetGetter(typeof(PowerModel), nameof(PowerModel.Icon)) is { } getPowerIcon)
		{
			harmony.Patch(getPowerIcon, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(PowerIconPrefix)));
		}

		if (TryGetGetter(typeof(PowerModel), nameof(PowerModel.BigIcon)) is { } getPowerBigIcon)
		{
			harmony.Patch(getPowerBigIcon, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(PowerBigIconPrefix)));
		}

		if (CanPatchCombatPowerReload() && TryGetMethod(typeof(NPower), "Reload", BindingFlags.Instance | BindingFlags.NonPublic) is { } combatPowerReload)
		{
			harmony.Patch(combatPowerReload, postfix: new HarmonyMethod(typeof(AssetHooks), nameof(CombatPowerReloadPostfix)));
		}
		else
		{
			Log.Warn("[Heartsteel] NPower.Reload or power icon fields not found; combat power node refresh hook disabled.");
		}

		if (TryGetMethod(typeof(EventModel), nameof(EventModel.CreateInitialPortrait), BindingFlags.Instance | BindingFlags.Public) is { } createInitialPortrait)
		{
			harmony.Patch(createInitialPortrait, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(CreateInitialPortraitPrefix)));
		}
		else
		{
			Log.Warn("[Heartsteel] EventModel.CreateInitialPortrait not found; Ornn's Forge portrait fallback disabled.");
		}
		_hooksInstalled = true;
	}

	private static bool CanPatchCombatPowerReload()
	{
		return CombatPowerModelField != null
			&& CombatPowerIconField != null
			&& CombatPowerFlashField != null;
	}

	private static bool CreateInitialPortraitPrefix(EventModel __instance, ref Texture2D __result)
	{
		if (__instance.Id == ModelDb.GetId<OrnnsForge>())
		{
			Texture2D? texture = LoadTexture(ModInfo.OrnnsForgePortraitPath);
			if (texture != null)
			{
				__result = texture;
				return false;
			}
		}

		return true;
	}

	private static bool RelicIconPrefix(RelicModel __instance, ref Texture2D __result)
	{
		if (TryGetHeartsteelRelicTexture(__instance, out Texture2D? texture))
		{
			__result = texture!;
			return false;
		}

		return true;
	}

	private static bool RelicBigIconPrefix(RelicModel __instance, ref Texture2D __result)
	{
		if (TryGetHeartsteelRelicTexture(__instance, out Texture2D? texture))
		{
			__result = texture!;
			return false;
		}

		return true;
	}

	private static bool NRelicReloadPrefix(NRelic __instance)
	{
		if (!__instance.IsNodeReady()
			|| NRelicModelField?.GetValue(__instance) is not RelicModel model
			|| !TryGetHeartsteelRelicTexture(model, out Texture2D? texture))
		{
			return true;
		}

		try
		{
			model.UpdateTexture(__instance.Icon);
			__instance.Icon.Texture = texture;
			__instance.Outline.Visible = false;
		}
		catch (ObjectDisposedException)
		{
			TextureCache.Remove(ModInfo.RelicIconPath);
			return true;
		}

		return false;
	}

	private static bool PowerIconPrefix(PowerModel __instance, ref Texture2D __result)
	{
		Texture2D? texture = TryGetHeartsteelPowerTexture(__instance);
		if (texture != null)
		{
			__result = texture;
			return false;
		}

		return true;
	}

	private static bool PowerBigIconPrefix(PowerModel __instance, ref Texture2D __result)
	{
		Texture2D? texture = TryGetHeartsteelPowerTexture(__instance);
		if (texture != null)
		{
			__result = texture;
			return false;
		}

		return true;
	}

	private static void CombatPowerReloadPostfix(NPower __instance)
	{
		if (!__instance.IsNodeReady())
		{
			return;
		}

		if (CombatPowerModelField?.GetValue(__instance) is not PowerModel model)
		{
			return;
		}

		Texture2D? texture = TryGetHeartsteelPowerTexture(model);
		if (texture == null)
		{
			return;
		}

		if (CombatPowerIconField?.GetValue(__instance) is TextureRect icon)
		{
			icon.Texture = texture;
		}

		if (CombatPowerFlashField?.GetValue(__instance) is CpuParticles2D flash)
		{
			flash.Texture = texture;
		}
	}

	private static Texture2D? TryGetHeartsteelPowerTexture(PowerModel self)
	{
		if (self.Id != ModelDb.GetId<HeartsteelDevourPower>())
		{
			return null;
		}

		return LoadTexture(ModInfo.PowerIconPath);
	}

	private static bool TryGetHeartsteelRelicTexture(RelicModel self, out Texture2D? texture)
	{
		texture = null;
		if (self.Id != ModelDb.GetId<HeartsteelRelic>())
		{
			return false;
		}

		texture = LoadTexture(ModInfo.RelicIconPath);
		return texture != null;
	}

	private static Texture2D? LoadTexture(string path)
	{
		if (TextureCache.TryGetValue(path, out Texture2D? cachedTexture))
		{
			if (IsTextureUsable(cachedTexture))
			{
				return cachedTexture;
			}

			TextureCache.Remove(path);
		}

		if (TryLoadPortableTexture(path, out Texture2D? portableTexture))
		{
			TextureCache[path] = portableTexture!;
			return portableTexture;
		}

		return null;
	}

	private static bool TryLoadPortableTexture(string path, out Texture2D? texture)
	{
		texture = null;
		byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
		if (bytes.Length == 0 && !TryGetEmbeddedBytes(path, out bytes))
		{
			return false;
		}

		Image image = new();
		Error err = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
			? image.LoadPngFromBuffer(bytes)
			: image.LoadJpgFromBuffer(bytes);
		if (err != Error.Ok)
		{
			return false;
		}

		PortableCompressedTexture2D portableTexture = new();
		portableTexture.CreateFromImage(image, PortableCompressedTexture2D.CompressionMode.Lossless);
		texture = portableTexture;
		return true;
	}

	private static bool IsTextureUsable(Texture2D texture)
	{
		try
		{
			return GodotObject.IsInstanceValid(texture) && texture.GetWidth() > 0;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	private static bool TryGetEmbeddedBytes(string path, out byte[] bytes)
	{
		bytes = [];
		const string prefix = "res://Heartsteel/images/";
		if (!path.StartsWith(prefix, StringComparison.Ordinal))
		{
			return false;
		}

		string resourceName = "Heartsteel.images." + path[prefix.Length..].Replace('/', '.');
		using Stream? stream = typeof(AssetHooks).Assembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			return false;
		}

		using MemoryStream memory = new();
		stream.CopyTo(memory);
		bytes = memory.ToArray();
		return bytes.Length > 0;
	}

	private static MethodInfo? TryGetMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
	{
		return type.GetMethod(name, flags, binder: null, parameterTypes, modifiers: null);
	}

	private static MethodInfo? TryGetGetter(Type type, string propertyName)
	{
		return type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetMethod
			?? AccessTools.PropertyGetter(type, propertyName)
			?? AccessTools.Method(type, "get_" + propertyName);
	}

	private static FieldInfo? TryGetField(Type type, string name)
	{
		return type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
	}

	private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, params Type[] parameterTypes)
	{
		return TryGetMethod(type, name, flags, parameterTypes)
			?? throw new InvalidOperationException($"Could not find method {type.FullName}.{name}.");
	}

	private static MethodInfo RequireGetter(Type type, string propertyName)
	{
		return TryGetGetter(type, propertyName)
			?? throw new InvalidOperationException($"Could not find property getter {type.FullName}.{propertyName}.");
	}
}
