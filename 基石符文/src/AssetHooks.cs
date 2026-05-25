using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace KeystoneRunes;

internal static class AssetHooks
{
	private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly Dictionary<string, Texture2D> ManualTextureCache = new();

	private static readonly FieldInfo? NRelicModelField = typeof(NRelic).GetField("_model", InstanceMembers);

	public static void Install(Harmony harmony)
	{
		if (TryGetGetter(typeof(RelicModel), nameof(RelicModel.Icon)) is { } getRelicIcon)
		{
			harmony.Patch(getRelicIcon, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(RelicIconPrefix)));
		}
		else
		{
			Log.Warn($"[{ModInfo.Id}] RelicModel.Icon getter not found; keystone small icons will use the runtime fallback.");
		}

		if (TryGetGetter(typeof(RelicModel), nameof(RelicModel.IconOutline)) is { } getRelicIconOutline)
		{
			harmony.Patch(getRelicIconOutline, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(RelicIconOutlinePrefix)));
		}
		else
		{
			Log.Warn($"[{ModInfo.Id}] RelicModel.IconOutline getter not found; keystone icon outlines will use the runtime fallback.");
		}

		if (TryGetGetter(typeof(RelicModel), nameof(RelicModel.BigIcon)) is { } getRelicBigIcon)
		{
			harmony.Patch(getRelicBigIcon, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(RelicBigIconPrefix)));
		}
		else
		{
			Log.Warn($"[{ModInfo.Id}] RelicModel.BigIcon getter not found; keystone big icons will use the runtime fallback.");
		}

		if (NRelicModelField == null)
		{
			Log.Warn($"[{ModInfo.Id}] NRelic._model field not found; NRelic.Reload icon override skipped.");
			return;
		}

		if (TryGetMethod(typeof(NRelic), "Reload") is { } relicReload)
		{
			harmony.Patch(relicReload, prefix: new HarmonyMethod(typeof(AssetHooks), nameof(NRelicReloadPrefix)));
		}
		else
		{
			Log.Warn($"[{ModInfo.Id}] NRelic.Reload method not found; NRelic reload icon override skipped.");
		}
	}

	private static bool RelicIconPrefix(RelicModel __instance, ref Texture2D __result)
	{
		if (!TryGetKeystoneRelicTexture(__instance, out Texture2D? texture))
		{
			return true;
		}

		__result = texture!;
		return false;
	}

	private static bool RelicIconOutlinePrefix(RelicModel __instance, ref Texture2D __result)
	{
		if (!TryGetKeystoneRelicTexture(__instance, out Texture2D? texture))
		{
			return true;
		}

		__result = texture!;
		return false;
	}

	private static bool RelicBigIconPrefix(RelicModel __instance, ref Texture2D __result)
	{
		if (!TryGetKeystoneRelicTexture(__instance, out Texture2D? texture))
		{
			return true;
		}

		__result = texture!;
		return false;
	}

	private static bool NRelicReloadPrefix(NRelic __instance)
	{
		if (NRelicModelField?.GetValue(__instance) is not RelicModel model)
		{
			return true;
		}

		string? path = ModInfo.TryGetRelicIconPath(model);
		if (path == null)
		{
			return true;
		}

		if (!__instance.IsNodeReady())
		{
			return false;
		}

		if (!TryGetKeystoneRelicTexture(model, out Texture2D? texture))
		{
			TryHideRelicOutline(__instance, path);
			return false;
		}

		TryApplyRelicNodeTexture(__instance, texture!, path);
		return false;
	}

	private static bool TryGetKeystoneRelicTexture(RelicModel self, out Texture2D? texture)
	{
		texture = LoadRelicTexture(self);
		return texture != null;
	}

	internal static Texture2D? LoadRelicTexture(RelicModel relic)
	{
		string? path = ModInfo.TryGetRelicIconPath(relic);
		return path == null ? null : LoadPortableTexture(path);
	}

	private static Texture2D? LoadPortableTexture(string path)
	{
		if (ManualTextureCache.TryGetValue(path, out Texture2D? cachedTexture))
		{
			if (IsTextureUsable(cachedTexture))
			{
				return cachedTexture;
			}

			ManualTextureCache.Remove(path);
		}

		byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
		if (bytes.Length == 0 && !TryGetEmbeddedBytes(path, out bytes))
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
		ManualTextureCache[path] = texture;
		return texture;
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

	private static void TryApplyRelicNodeTexture(NRelic relicNode, Texture2D texture, string path)
	{
		try
		{
			if (relicNode.Icon is { } icon)
			{
				icon.Texture = texture;
			}

			if (relicNode.Outline is { } outline)
			{
				outline.Visible = false;
			}
		}
		catch (Exception ex) when (IsExpectedGodotLifecycleException(ex))
		{
			ManualTextureCache.Remove(path);
			Log.Warn($"[{ModInfo.Id}] Skipped relic node texture update because the node was no longer valid: {ex.GetType().Name}");
		}
	}

	private static void TryHideRelicOutline(NRelic relicNode, string path)
	{
		try
		{
			if (relicNode.Outline is { } outline)
			{
				outline.Visible = false;
			}
		}
		catch (Exception ex) when (IsExpectedGodotLifecycleException(ex))
		{
			ManualTextureCache.Remove(path);
		}
	}

	private static bool IsExpectedGodotLifecycleException(Exception ex)
	{
		return ex is InvalidOperationException or ObjectDisposedException or NullReferenceException;
	}

	private static bool TryGetEmbeddedBytes(string path, out byte[] bytes)
	{
		bytes = [];
		const string prefix = "res://KeystoneRunes/images/relics/";
		if (!path.StartsWith(prefix, StringComparison.Ordinal))
		{
			return false;
		}

		string resourceName = "KeystoneRunes.images.relics." + path[prefix.Length..];
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

	private static MethodInfo? TryGetMethod(Type type, string name, params Type[] parameterTypes)
	{
		return type.GetMethod(name, InstanceMembers, binder: null, parameterTypes, modifiers: null);
	}

	private static MethodInfo? TryGetGetter(Type type, string propertyName)
	{
		return type.GetProperty(propertyName, InstanceMembers)?.GetMethod
			?? AccessTools.PropertyGetter(type, propertyName)
			?? AccessTools.Method(type, "get_" + propertyName);
	}
}
