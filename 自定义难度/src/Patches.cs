using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;

namespace CustomDifficulty;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeSingleplayer))]
internal static class CharacterSelectSingleplayerPatch
{
	private static void Postfix(NCharacterSelectScreen __instance)
	{
		CustomDifficultySync.Register(__instance.Lobby.NetService);
		CustomDifficultyPanel.Inject(__instance);
	}
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeMultiplayerAsHost), typeof(INetGameService), typeof(int))]
internal static class CharacterSelectHostPatch
{
	private static void Postfix(NCharacterSelectScreen __instance, INetGameService gameService)
	{
		CustomDifficultySync.Register(gameService);
		CustomDifficultyPanel.Inject(__instance);
		CustomDifficultySync.BroadcastSettings();
	}
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeMultiplayerAsClient), typeof(INetGameService), typeof(ClientLobbyJoinResponseMessage))]
internal static class CharacterSelectClientPatch
{
	private static void Postfix(NCharacterSelectScreen __instance, INetGameService gameService)
	{
		CustomDifficultySync.Register(gameService);
		CustomDifficultyPanel.Inject(__instance);
		CustomDifficultySync.RequestHostSettings();
	}
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Ready))]
internal static class CharacterSelectReadyPatch
{
	private static void Postfix(NCharacterSelectScreen __instance)
	{
		try
		{
			if (__instance.Lobby?.NetService != null)
			{
				CustomDifficultySync.Register(__instance.Lobby.NetService);
				CustomDifficultyPanel.Inject(__instance);
			}
		}
		catch
		{
		}
	}
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.PlayerConnected))]
internal static class CharacterSelectPlayerConnectedPatch
{
	private static void Postfix(LobbyPlayer player)
	{
		Log.Debug($"[{ModInfo.Id}] Player connected: {player.id}; resending host difficulty.");
		CustomDifficultySync.BroadcastSettings();
		CustomDifficultyPanel.Refresh();
	}
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
internal static class RunLaunchPatch
{
	private static void Postfix()
	{
		if (RunManager.Instance?.NetService == null)
		{
			return;
		}

		CustomDifficultySync.Register(RunManager.Instance.NetService);
		CustomDifficultySync.BroadcastSettings();
		CustomDifficultySync.RequestHostSettings();
	}
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class RunCleanUpPatch
{
	private static void Postfix()
	{
		CustomDifficultySync.Unregister();
	}
}

[HarmonyPatch(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer))]
internal static class MonsterScalingPatch
{
	private static void Postfix(Creature __instance)
	{
		if (!__instance.IsMonster)
		{
			return;
		}

		ApplyHpMultiplier(__instance);
		ApplyAttackMultiplierPower(__instance);
	}

	private static void ApplyHpMultiplier(Creature creature)
	{
		decimal multiplier = CustomDifficultySettings.MonsterHpMultiplier;
		if (multiplier == 1m)
		{
			return;
		}

		int scaledHp = Math.Max(1, (int)Math.Round(creature.MaxHp * multiplier, MidpointRounding.AwayFromZero));
		creature.SetMaxHpInternal(scaledHp);
		creature.SetCurrentHpInternal(scaledHp);
		Log.Debug($"[{ModInfo.Id}] {creature.Name} HP scaled to {scaledHp} ({CustomDifficultySettings.FormatMultiplier(CustomDifficultySettings.MonsterHpTicks)}).");
	}

	private static void ApplyAttackMultiplierPower(Creature creature)
	{
		if (CustomDifficultySettings.MonsterAttackMultiplier == 1m || creature.HasPower<MonsterAttackScalePower>())
		{
			return;
		}

		try
		{
			ModelDb.Power<MonsterAttackScalePower>().ToMutable().ApplyInternal(creature, 1m, silent: true);
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}] Failed to apply MonsterAttackScalePower: {ex.Message}");
		}
	}
}
