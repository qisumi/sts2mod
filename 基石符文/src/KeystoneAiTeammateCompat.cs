using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace KeystoneRunes;

internal static class KeystoneAiTeammateCompat
{
	private const string AiDummyControllerTypeName = "AITeammate.Scripts.AiTeammateDummyController";
	private const string AiLoopbackHostGameServiceTypeName = "AITeammate.Scripts.AiTeammateLoopbackHostGameService";
	private const string AiSessionRegistryTypeName = "AITeammate.Scripts.AiTeammateSessionRegistry";

	private static readonly Lazy<Type?> AiDummyControllerType = new(ResolveAiDummyControllerType);
	private static readonly Lazy<Type?> AiSessionRegistryType = new(ResolveAiSessionRegistryType);
	private static readonly Lazy<MethodInfo?> CanUseDirectSelectionAutomationMethod = new(() => GetRegistryMethod("CanUseDirectSelectionAutomation"));
	private static readonly Lazy<MethodInfo?> IsAiPlayerMethod = new(() => GetDummyControllerMethod("IsAiPlayer"));
	private static readonly Lazy<MethodInfo?> ShouldAutomateAiPlayerMethod = new(() => GetRegistryMethod("ShouldAutomateAiPlayer"));
	private static readonly Lazy<MethodInfo?> TryGetDisplayNameMethod = new(() => GetRegistryMethod("TryGetDisplayName", typeof(ulong), typeof(string).MakeByRefType()));
	private static readonly Lazy<PropertyInfo?> CurrentSessionProperty = new(() => AiSessionRegistryType.Value?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static));
	private static bool _warnedReflectionFailure;
	private static bool _warnedIsAiFailure;
	private static bool _warnedDisplayNameFailure;

	public static bool IsLoopbackHostSession()
	{
		return RunManager.Instance?.NetService?.GetType().FullName == AiLoopbackHostGameServiceTypeName;
	}

	public static bool IsAiPlayer(Player? player)
	{
		if (player == null || !IsLoopbackHostSession())
		{
			return false;
		}

		try
		{
			if (TrySessionContainsAiController(player.NetId, out bool isAiPlayer))
			{
				return isAiPlayer;
			}

			return InvokeRegistryBool(IsAiPlayerMethod.Value, player);
		}
		catch (Exception ex)
		{
			if (!_warnedIsAiFailure)
			{
				_warnedIsAiFailure = true;
				Log.Warn($"[{ModInfo.Id}][AITeammateCompat] Failed to query AI player identity: {ex.Message}");
			}

			return false;
		}
	}

	public static bool TryGetHostPlayerId(out ulong hostPlayerId)
	{
		hostPlayerId = 0UL;
		if (!IsLoopbackHostSession())
		{
			return false;
		}

		object? session = CurrentSessionProperty.Value?.GetValue(null);
		PropertyInfo? hostPlayerIdProperty = session?.GetType().GetProperty("HostPlayerId", BindingFlags.Public | BindingFlags.Instance);
		if (hostPlayerIdProperty?.GetValue(session) is ulong value && value != 0UL)
		{
			hostPlayerId = value;
			return true;
		}

		return false;
	}

	public static string GetDisplayName(Player player)
	{
		try
		{
			MethodInfo? method = TryGetDisplayNameMethod.Value;
			if (method != null)
			{
				object?[] args = [ player.NetId, null ];
				if (method.Invoke(null, args) is true && args[1] is string displayName && !string.IsNullOrWhiteSpace(displayName))
				{
					return displayName;
				}
			}
		}
		catch (Exception ex)
		{
			if (!_warnedDisplayNameFailure)
			{
				_warnedDisplayNameFailure = true;
				Log.Warn($"[{ModInfo.Id}][AITeammateCompat] Failed to query AI player display name: {ex.Message}");
			}
		}

		return $"玩家{player.NetId}";
	}

	public static bool IsAiTeammateLoopbackRun(RunState? runState)
	{
		if (!IsLoopbackHostSession() || runState == null)
		{
			return false;
		}

		foreach (Player player in runState.Players)
		{
			if (IsAiPlayer(player))
			{
				return true;
			}
		}

		return false;
	}

	public static bool ShouldAutoSelectRune(Player? player)
	{
		if (player == null || !IsLoopbackHostSession())
		{
			return false;
		}

		try
		{
			return InvokeRegistryBool(CanUseDirectSelectionAutomationMethod.Value, player)
				|| InvokeRegistryBool(ShouldAutomateAiPlayerMethod.Value, player);
		}
		catch (Exception ex)
		{
			if (!_warnedReflectionFailure)
			{
				_warnedReflectionFailure = true;
				Log.Warn($"[{ModInfo.Id}][AITeammateCompat] Failed to query AI teammate session registry: {ex.Message}");
			}

			return false;
		}
	}

	public static int PickRandomRuneIndex(Player player, IReadOnlyList<RelicModel> options)
	{
		if (options.Count == 0)
		{
			Log.Warn($"[{ModInfo.Id}][AITeammateCompat] No keystone options for AI player={player.NetId}");
			return -1;
		}

		int selectedIndex = Random.Shared.Next(options.Count);
		RelicModel selectedRelic = options[selectedIndex];
		Log.Info($"[{ModInfo.Id}][AITeammateCompat] Auto-selected keystone for AI player={player.NetId} index={selectedIndex} relic={(selectedRelic.CanonicalInstance?.Id ?? selectedRelic.Id).Entry}");
		return selectedIndex;
	}

	private static MethodInfo? GetRegistryMethod(string methodName)
	{
		return GetRegistryMethod(methodName, typeof(Player));
	}

	private static MethodInfo? GetRegistryMethod(string methodName, params Type[] parameterTypes)
	{
		return AiSessionRegistryType.Value?.GetMethod(
			methodName,
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
			null,
			parameterTypes,
			null);
	}

	private static MethodInfo? GetDummyControllerMethod(string methodName)
	{
		return AiDummyControllerType.Value?.GetMethod(
			methodName,
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
			null,
			[ typeof(Player) ],
			null);
	}

	private static bool InvokeRegistryBool(MethodInfo? method, Player player)
	{
		return method?.Invoke(null, [ player ]) is true;
	}

	private static bool TrySessionContainsAiController(ulong playerId, out bool contains)
	{
		contains = false;
		object? session = CurrentSessionProperty.Value?.GetValue(null);
		object? aiControllers = ResolveSessionStateProperty(session, "AiControllers")?.GetValue(session);
		if (aiControllers == null)
		{
			return false;
		}

		MethodInfo? containsKeyMethod = aiControllers.GetType().GetMethod(
			"ContainsKey",
			BindingFlags.Public | BindingFlags.Instance,
			null,
			[ typeof(ulong) ],
			null);
		if (containsKeyMethod == null)
		{
			return false;
		}

		contains = containsKeyMethod.Invoke(aiControllers, [ playerId ]) is true;
		return true;
	}

	private static PropertyInfo? ResolveSessionStateProperty(object? session, string propertyName)
	{
		return session?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
	}

	private static Type? ResolveAiSessionRegistryType()
	{
		return ResolveAiTeammateType(AiSessionRegistryTypeName);
	}

	private static Type? ResolveAiDummyControllerType()
	{
		return ResolveAiTeammateType(AiDummyControllerTypeName);
	}

	private static Type? ResolveAiTeammateType(string typeName)
	{
		Type? type = Type.GetType($"{typeName}, sts2AITeammate", throwOnError: false);
		if (type != null)
		{
			return type;
		}

		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			type = assembly.GetType(typeName, throwOnError: false);
			if (type != null)
			{
				return type;
			}
		}

		return null;
	}
}
