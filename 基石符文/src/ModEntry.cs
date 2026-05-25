using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace KeystoneRunes;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
	private const string HarmonyId = "Natsuki.KeystoneRunes";

	private readonly record struct PendingRuneSelection(Player Player, List<RelicModel> Options, uint ChoiceId, bool IsLocal);

	private static Harmony? _harmony;

	public static void Initialize()
	{
		InjectSavedPropertyCaches();
		RegisterModels();
		Harmony harmony = _harmony ??= new Harmony(HarmonyId);
		InstallHooks(harmony);
		TryInstallOptionalHookGroup("asset hooks", () => AssetHooks.Install(harmony));
		TryInstallOptionalHookGroup("collection hooks", () => CollectionHooks.Install(harmony));
		Log.Info($"[{ModInfo.Id}] Loaded for Slay the Spire 2 {ModInfo.TargetGameVersion}.");
	}

	private static void TryInstallOptionalHookGroup(string label, Action install)
	{
		try
		{
			install();
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}] Optional hook group skipped: {label}: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static void InjectSavedPropertyCaches()
	{
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_ElectrocuteRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_FirstStrikeRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_UndyingGraspRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_ConquerorRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_SummonAeryRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_LethalTempoRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_PhaseRushRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_UnsealedSpellbookRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_HailOfBladesRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_FleetFootworkRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_ArcaneCometRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_DarkHarvestRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_GlacialAugmentRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_AftershockRune));
		SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(Keystone_GuardianRune));
		EnsureSavedPropertyNetIdBitSize();
	}

	private static void EnsureSavedPropertyNetIdBitSize()
	{
		const int minimumBitSize = 16;
		const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;

		FieldInfo? mapField = typeof(SavedPropertiesTypeCache).GetField("_netIdToPropertyNameMap", flags);
		int propertyNameCount = (mapField?.GetValue(null) as System.Collections.ICollection)?.Count ?? 0;
		int requiredBitSize = GetRequiredBitSize(propertyNameCount);
		int targetBitSize = Math.Max(minimumBitSize, requiredBitSize);
		int currentBitSize = SavedPropertiesTypeCache.NetIdBitSize;
		if (currentBitSize >= targetBitSize)
		{
			Log.Info($"[{ModInfo.Id}] SavedPropertiesTypeCache NetIdBitSize unchanged: bitSize={currentBitSize} propertyNames={propertyNameCount}");
			return;
		}

		FieldInfo? backingField = typeof(SavedPropertiesTypeCache).GetField("<NetIdBitSize>k__BackingField", flags);
		if (backingField == null)
		{
			Log.Warn($"[{ModInfo.Id}] SavedPropertiesTypeCache NetIdBitSize backing field not found; custom saved properties may desync in multiplayer.");
			return;
		}

		backingField.SetValue(null, targetBitSize);
		Log.Info($"[{ModInfo.Id}] SavedPropertiesTypeCache NetIdBitSize updated: old={currentBitSize} new={targetBitSize} propertyNames={propertyNameCount}");
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
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_ElectrocuteRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_FirstStrikeRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_UndyingGraspRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_ConquerorRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_SummonAeryRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_LethalTempoRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_PhaseRushRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_UnsealedSpellbookRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_HailOfBladesRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_FleetFootworkRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_ArcaneCometRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_DarkHarvestRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_GlacialAugmentRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_AftershockRune>();
		ModHelper.AddModelToPool<SharedRelicPool, Keystone_GuardianRune>();
	}

	private static void InstallHooks(Harmony harmony)
	{
		harmony.Patch(
			RequireMethod(typeof(RunManager), nameof(RunManager.FinalizeStartingRelics), BindingFlags.Instance | BindingFlags.Public),
			postfix: new HarmonyMethod(typeof(ModEntry), nameof(FinalizeStartingRelicsPostfix)));
		harmony.Patch(
			RequireMethod(typeof(NGame), "StartRun", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, typeof(RunState)),
			postfix: new HarmonyMethod(typeof(ModEntry), nameof(StartRunPostfix)));
	}

	private static void FinalizeStartingRelicsPostfix(RunManager __instance, ref Task __result)
	{
		__result = FinalizeStartingRelicsAfterOriginal(__result, __instance);
	}

	private static async Task FinalizeStartingRelicsAfterOriginal(Task original, RunManager self)
	{
		await original;

		RunState? runState = self.DebugOnlyGetState();
		if (runState == null)
		{
			return;
		}

		foreach (Player player in runState.Players)
		{
			RemoveRunesFromGrabBags(player);
		}
	}

	private static void StartRunPostfix(RunState runState, ref Task __result)
	{
		__result = StartRunAfterOriginal(__result, runState);
	}

	private static async Task StartRunAfterOriginal(Task original, RunState runState)
	{
		await original;

		NetGameType gameType = RunManager.Instance.NetService.Type;
		if (gameType is NetGameType.Singleplayer or NetGameType.None)
		{
			foreach (Player player in runState.Players)
			{
				await EnsureKeystoneRuneSelected(player);
			}

			return;
		}

		await EnsureKeystoneRunesSelectedMultiplayer(runState.Players.ToList());
	}

	private static async Task EnsureKeystoneRunesSelectedMultiplayer(IReadOnlyList<Player> players)
	{
		RunManager runManager = RunManager.Instance;
		RunState? runState = runManager.DebugOnlyGetState();
		if (KeystoneAiTeammateCompat.IsAiTeammateLoopbackRun(runState))
		{
			await EnsureKeystoneRunesSelectedAiTeammateHostControlled(players);
			return;
		}

		IReadOnlyList<Player> orderedPlayers = players
			.OrderBy(static player => player.NetId)
			.ToList();

		PlayerChoiceSynchronizer? synchronizer = await WaitForPlayerChoiceSynchronizerAsync(runManager);
		if (synchronizer == null)
		{
			foreach (Player player in orderedPlayers)
			{
				await EnsureKeystoneRuneSelected(player);
			}

			return;
		}

		List<PendingRuneSelection> pendingSelections = new();
		foreach (Player player in orderedPlayers)
		{
			RemoveRunesFromGrabBags(player);
			if (player.Relics.Any(ModInfo.IsKeystoneRelic))
			{
				continue;
			}

			List<RelicModel> options = ModInfo.GetCanonicalRunes()
				.Select(static relic => relic.ToMutable())
				.ToList();
			foreach (RelicModel relic in options)
			{
				SaveManager.Instance.MarkRelicAsSeen(relic);
			}

			uint choiceId = synchronizer.ReserveChoiceId(player);
			pendingSelections.Add(new PendingRuneSelection(player, options, choiceId, IsLocalPlayer(runManager, player)));
		}

		List<KeystoneRuneSelectionScreen> localScreens = new();
		try
		{
			Task<RelicModel?>[] selectionTasks = pendingSelections
				.Select(selection => SelectRuneMultiplayer(selection, synchronizer, localScreens))
				.ToArray();

			RelicModel?[] selectedRelics = await Task.WhenAll(selectionTasks);
			for (int i = 0; i < pendingSelections.Count; i++)
			{
				PendingRuneSelection selection = pendingSelections[i];
				RelicModel selectedRelic = selectedRelics[i] ?? selection.Options[0];
				await RelicCmd.Obtain(selectedRelic, selection.Player);
			}
		}
		finally
		{
			foreach (KeystoneRuneSelectionScreen screen in localScreens)
			{
				screen.CloseSelectionScreen();
			}
		}
	}

	private static async Task EnsureKeystoneRunesSelectedAiTeammateHostControlled(IReadOnlyList<Player> players)
	{
		Log.Info($"[{ModInfo.Id}][AITeammateCompat] Host-controlled keystone selection started.");
		KeystoneAiTeammateCompat.TryGetHostPlayerId(out ulong hostPlayerId);
		IReadOnlyList<Player> orderedPlayers = players
			.OrderBy(player => hostPlayerId != 0UL
				? (player.NetId == hostPlayerId ? 0 : 1)
				: (KeystoneAiTeammateCompat.IsAiPlayer(player) ? 1 : 0))
			.ThenBy(static player => player.NetId)
			.ToList();

		foreach (Player player in orderedPlayers)
		{
			RemoveRunesFromGrabBags(player);
			if (player.Relics.Any(ModInfo.IsKeystoneRelic))
			{
				continue;
			}

			List<RelicModel> options = ModInfo.GetCanonicalRunes()
				.Select(static relic => relic.ToMutable())
				.ToList();
			foreach (RelicModel relic in options)
			{
				SaveManager.Instance.MarkRelicAsSeen(relic);
			}

			bool isAiPlayer = KeystoneAiTeammateCompat.IsAiPlayer(player);
			string? titleOverride = isAiPlayer ? FormatAiTeammateSelectionTitle(player) : null;
			RelicModel? selected = await SelectRuneWithLocalScreen(options, titleOverride);
			selected ??= options[0];
			await RelicCmd.Obtain(selected, player);
			Log.Info($"[{ModInfo.Id}][AITeammateCompat] Host-controlled obtained: player={player.NetId} ai={isAiPlayer} relic={(selected.CanonicalInstance?.Id ?? selected.Id).Entry}");
		}

		Log.Info($"[{ModInfo.Id}][AITeammateCompat] Host-controlled keystone selection complete.");
	}

	private static async Task EnsureKeystoneRuneSelected(Player player)
	{
		RemoveRunesFromGrabBags(player);

		if (player.Relics.Any(ModInfo.IsKeystoneRelic))
		{
			return;
		}

		List<RelicModel> options = ModInfo.GetCanonicalRunes()
			.Select(static relic => relic.ToMutable())
			.ToList();

		RelicModel? selected = await SelectRune(player, options);
		selected ??= options[0];
		await RelicCmd.Obtain(selected, player);
	}

	private static async Task<RelicModel?> SelectRune(Player player, IReadOnlyList<RelicModel> options)
	{
		foreach (RelicModel relic in options)
		{
			SaveManager.Instance.MarkRelicAsSeen(relic);
		}

		RunManager runManager = RunManager.Instance;
		NetGameType gameType = runManager.NetService.Type;
		if (gameType is NetGameType.Singleplayer or NetGameType.None)
		{
			return await SelectRuneWithLocalScreen(options);
		}

		PlayerChoiceSynchronizer? synchronizer = await WaitForPlayerChoiceSynchronizerAsync(runManager);
		if (synchronizer == null)
		{
			return await RelicSelectCmd.FromChooseARelicScreen(player, options);
		}

		uint choiceId = synchronizer.ReserveChoiceId(player);
		if (IsLocalPlayer(runManager, player))
		{
			RelicModel? selectedRelic = await SelectRuneWithLocalScreen(options);
			int selectedIndex = selectedRelic == null ? -1 : options.IndexOf(selectedRelic);
			synchronizer.SyncLocalChoice(player, choiceId, PlayerChoiceResult.FromIndex(selectedIndex));
			return selectedRelic;
		}

		if (KeystoneAiTeammateCompat.ShouldAutoSelectRune(player))
		{
			Log.Info($"[{ModInfo.Id}][AITeammateCompat] Keystone choice AI auto-select: player={player.NetId} choiceId={choiceId}");
			int selectedIndex = KeystoneAiTeammateCompat.PickRandomRuneIndex(player, options);
			return selectedIndex >= 0 && selectedIndex < options.Count ? options[selectedIndex] : null;
		}

		PlayerChoiceResult remoteChoice = await synchronizer.WaitForRemoteChoice(player, choiceId);
		int index = remoteChoice.AsIndex();
		return index >= 0 && index < options.Count ? options[index] : null;
	}

	private static async Task<RelicModel?> SelectRuneMultiplayer(
		PendingRuneSelection selection,
		PlayerChoiceSynchronizer synchronizer,
		ICollection<KeystoneRuneSelectionScreen> localScreens)
	{
		if (selection.IsLocal)
		{
			KeystoneRuneSelectionScreen screen = await CreateRuneSelectionScreenAsync(selection.Options);
			localScreens.Add(screen);
			RelicModel? selectedRelic = (await screen.RelicsSelected(closeOnSelection: false)).FirstOrDefault();
			int selectedIndex = selectedRelic == null ? -1 : selection.Options.IndexOf(selectedRelic);
			synchronizer.SyncLocalChoice(selection.Player, selection.ChoiceId, PlayerChoiceResult.FromIndex(selectedIndex));
			return selectedRelic;
		}

		if (KeystoneAiTeammateCompat.ShouldAutoSelectRune(selection.Player))
		{
			Log.Info($"[{ModInfo.Id}][AITeammateCompat] Keystone choice AI auto-select: player={selection.Player.NetId} choiceId={selection.ChoiceId}");
			int selectedIndex = KeystoneAiTeammateCompat.PickRandomRuneIndex(selection.Player, selection.Options);
			return selectedIndex >= 0 && selectedIndex < selection.Options.Count ? selection.Options[selectedIndex] : null;
		}

		PlayerChoiceResult remoteChoice = await synchronizer.WaitForRemoteChoice(selection.Player, selection.ChoiceId);
		int index = remoteChoice.AsIndex();
		return index >= 0 && index < selection.Options.Count ? selection.Options[index] : null;
	}

	private static async Task<RelicModel?> SelectRuneWithLocalScreen(IReadOnlyList<RelicModel> options, string? titleOverride = null)
	{
		KeystoneRuneSelectionScreen screen = await CreateRuneSelectionScreenAsync(options, titleOverride);
		return (await screen.RelicsSelected()).FirstOrDefault();
	}

	private static async Task<PlayerChoiceSynchronizer?> WaitForPlayerChoiceSynchronizerAsync(RunManager runManager)
	{
		for (int i = 0; i < 60; i++)
		{
			if (runManager.PlayerChoiceSynchronizer != null)
			{
				return runManager.PlayerChoiceSynchronizer;
			}

			await Task.Yield();
		}

		return runManager.PlayerChoiceSynchronizer;
	}

	private static bool IsLocalPlayer(RunManager runManager, Player player)
	{
		return player.NetId != 0UL && player.NetId == runManager.NetService.NetId;
	}

	private static async Task<KeystoneRuneSelectionScreen> CreateRuneSelectionScreenAsync(IReadOnlyList<RelicModel> relics, string? titleOverride = null)
	{
		for (int i = 0; i < 60; i++)
		{
			if (NOverlayStack.Instance != null)
			{
				break;
			}

			await Task.Yield();
		}

		KeystoneRuneSelectionScreen selectionScreen = KeystoneRuneSelectionScreen.Create(relics, titleOverride);

		if (NOverlayStack.Instance == null)
		{
			throw new InvalidOperationException("NOverlayStack is not available for rune selection.");
		}

		NOverlayStack.Instance.Push(selectionScreen);
		return selectionScreen;
	}

	private static string FormatAiTeammateSelectionTitle(Player player)
	{
		string template = new LocString("relic_collection", "KEYSTONE_SELECTION_TITLE_FOR_PLAYER").GetRawText();
		if (string.IsNullOrWhiteSpace(template) || template == "KEYSTONE_SELECTION_TITLE_FOR_PLAYER")
		{
			template = "为{PlayerName}选择一枚基石符文";
		}

		return template.Replace("{PlayerName}", KeystoneAiTeammateCompat.GetDisplayName(player), StringComparison.Ordinal);
	}

	private static void RemoveRunesFromGrabBags(Player player)
	{
		foreach (RelicModel relic in ModInfo.GetCanonicalRunes())
		{
			player.RelicGrabBag.Remove(relic);
			player.RunState.SharedRelicGrabBag.Remove(relic);
		}
	}

	private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, params Type[] parameters)
	{
		return type.GetMethod(name, flags, binder: null, parameters, modifiers: null)
			?? throw new InvalidOperationException($"Could not find required method {type.FullName}.{name}.");
	}
}
