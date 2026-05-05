using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace HextechRunes;

internal static partial class HextechRuneSelectionCoordinator
{
	private static async Task<RuneSelectionResult> SelectRune(
		HextechMayhemModifier modifier,
		Player player,
		IReadOnlyList<RelicModel> options,
		RelicModel? monsterHexRelic,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = null)
	{
		RunManager runManager = RunManager.Instance;
		NetGameType gameType = runManager.NetService.Type;
		if (gameType is NetGameType.Singleplayer or NetGameType.None)
		{
			MarkRelicsSeen(options);
			modifier.RecordSeenPlayerRunes(player, options);
			HashSet<ModelId> seenOptionIds = CreateSeenOptionIds(options, monsterHexRelic, modifier.GetSeenPlayerRuneIds(player));
			HextechRuneSelectionScreen screen = await CreateRuneSelectionScreenAsync(
				options,
				monsterHexRelic,
				(relics, slotIndex, _) => RerollSingleOptionAndTrack(modifier, player, relics, slotIndex, seenOptionIds),
				enemyHexOptions);
			RelicModel? selectedRelic = (await screen.RelicsSelected()).FirstOrDefault();
			return new RuneSelectionResult(selectedRelic, screen.CurrentRelics.ToList(), screen.RerollHistory.Count, screen.CurrentMonsterHex);
		}

		PlayerChoiceSynchronizer? synchronizer = await WaitForPlayerChoiceSynchronizerAsync(runManager);
		if (synchronizer == null)
		{
			MarkRelicsSeen(options);
			modifier.RecordSeenPlayerRunes(player, options);
			RelicModel? selectedRelic = await RelicSelectCmd.FromChooseARelicScreen(player, options);
			return new RuneSelectionResult(selectedRelic, options.ToList(), 0, enemyHexOptions?.InitialHex);
		}

		uint choiceId = synchronizer.ReserveChoiceId(player);
		if (IsLocalPlayer(runManager, player))
		{
			MarkRelicsSeen(options);
			modifier.RecordSeenPlayerRunes(player, options);
			HashSet<ModelId> seenOptionIds = CreateSeenOptionIds(options, monsterHexRelic, modifier.GetSeenPlayerRuneIds(player));
			HextechRuneSelectionScreen screen = await CreateRuneSelectionScreenAsync(
				options,
				monsterHexRelic,
				(relics, slotIndex, rerollOrdinal) => RerollSingleOptionAndTrackMultiplayer(modifier, player, relics, slotIndex, rerollOrdinal, seenOptionIds),
				enemyHexOptions);
			RelicModel? selectedRelic = (await screen.RelicsSelected()).FirstOrDefault();
			synchronizer.SyncLocalChoice(player, choiceId, CreateRuneChoiceResult(screen, selectedRelic));
			Log.Info($"[{ModInfo.Id}][Mayhem] RuneChoice sync local: player={player.NetId} choiceId={choiceId}");
			return new RuneSelectionResult(selectedRelic, screen.CurrentRelics.ToList(), screen.RerollHistory.Count, screen.CurrentMonsterHex);
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] RuneChoice wait remote: player={player.NetId} choiceId={choiceId}");
		(PlayerChoiceResult remoteChoice, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
			synchronizer,
			player,
			choiceId,
			IsRuneSelectionChoice,
			"rune-choice");
		Log.Info($"[{ModInfo.Id}][Mayhem] RuneChoice remote received: player={player.NetId} choiceId={receivedChoiceId}");
		return ResolveRemoteRuneChoice(modifier, player, options, remoteChoice, monsterHexRelic);
	}

	private static async Task<RuneSelectionResult> SelectRuneMultiplayer(
		HextechMayhemModifier modifier,
		PendingRuneSelection selection,
		PlayerChoiceSynchronizer synchronizer,
		RelicModel? monsterHexRelic,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = null,
		Func<HextechRuneSelectionScreen, Task>? afterLocalSelection = null)
	{
		if (selection.IsLocal)
		{
			MarkRelicsSeen(selection.Options);
			modifier.RecordSeenPlayerRunes(selection.Player, selection.Options);
			HashSet<ModelId> seenOptionIds = CreateSeenOptionIds(selection.Options, monsterHexRelic, modifier.GetSeenPlayerRuneIds(selection.Player));
			HextechRuneSelectionScreen screen = await CreateRuneSelectionScreenAsync(
				selection.Options,
				monsterHexRelic,
				(relics, slotIndex, rerollOrdinal) => RerollSingleOptionAndTrackMultiplayer(modifier, selection.Player, relics, slotIndex, rerollOrdinal, seenOptionIds),
				enemyHexOptions);
			RelicModel? selectedRelic = (await screen.RelicsSelected(removeOverlay: false)).FirstOrDefault();
			synchronizer.SyncLocalChoice(selection.Player, selection.ChoiceId, CreateRuneChoiceResult(screen, selectedRelic));
			Log.Info($"[{ModInfo.Id}][Mayhem] RuneChoice sync local: player={selection.Player.NetId} choiceId={selection.ChoiceId}");
			if (afterLocalSelection != null)
			{
				await afterLocalSelection(screen);
			}

			return new RuneSelectionResult(selectedRelic, screen.CurrentRelics.ToList(), screen.RerollHistory.Count, screen.CurrentMonsterHex, screen);
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] RuneChoice wait remote: player={selection.Player.NetId} choiceId={selection.ChoiceId}");
		(PlayerChoiceResult remoteChoice, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
			synchronizer,
			selection.Player,
			selection.ChoiceId,
			IsRuneSelectionChoice,
			"rune-choice");
		Log.Info($"[{ModInfo.Id}][Mayhem] RuneChoice remote received: player={selection.Player.NetId} choiceId={receivedChoiceId}");
		return ResolveRemoteRuneChoice(modifier, selection.Player, selection.Options, remoteChoice, monsterHexRelic);
	}

	private static async Task<HextechRuneSelectionScreen> CreateRuneSelectionScreenAsync(
		IReadOnlyList<RelicModel> relics,
		RelicModel? monsterHexRelic,
		Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>>? rerollFunc = null,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = null)
	{
		for (int i = 0; i < 60; i++)
		{
			if (NOverlayStack.Instance != null)
			{
				break;
			}

			await Task.Yield();
		}

		HextechRuneSelectionScreen selectionScreen = HextechRuneSelectionScreen.Create(relics, monsterHexRelic, rerollFunc, enemyHexOptions);
		if (NOverlayStack.Instance == null)
		{
			throw new InvalidOperationException("NOverlayStack is not available for rune selection.");
		}

		NOverlayStack.Instance.Push(selectionScreen);
		enemyHexOptions?.ScreenCreated?.Invoke(selectionScreen);
		return selectionScreen;
	}

	private static PlayerChoiceResult CreateRuneChoiceResult(HextechRuneSelectionScreen screen, RelicModel? selectedRelic)
	{
		int selectedIndex = selectedRelic == null ? -1 : IndexOfRelic(screen.CurrentRelics, selectedRelic);
		List<int> payload = [ HextechChoiceMagic, ChoiceKindRuneSelection, selectedIndex, screen.RerollHistory.Count ];
		payload.AddRange(screen.RerollHistory);
		Log.Info($"[{ModInfo.Id}][Mayhem] CreateRuneChoiceResult: selectedIndex={selectedIndex} rerolls={string.Join(",", screen.RerollHistory)}");
		return PlayerChoiceResult.FromIndexes(payload);
	}

	private static int IndexOfRelic(IReadOnlyList<RelicModel> relics, RelicModel relic)
	{
		for (int i = 0; i < relics.Count; i++)
		{
			if (ReferenceEquals(relics[i], relic))
			{
				return i;
			}
		}

		return -1;
	}

	private static RuneSelectionResult ResolveRemoteRuneChoice(HextechMayhemModifier modifier, Player player, IReadOnlyList<RelicModel> options, PlayerChoiceResult remoteChoice, RelicModel? monsterHexRelic)
	{
		if (!TryDecodeRuneChoiceResult(remoteChoice, out int selectedIndex, out List<int> rerollHistory))
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] ResolveRemoteRuneChoice: malformed hextech rune payload player={player.NetId} result={remoteChoice}");
			return new RuneSelectionResult(null, options.ToList(), 0, null);
		}

		MarkRelicsSeen(options);
		modifier.RecordSeenPlayerRunes(player, options);
		HashSet<ModelId> seenOptionIds = CreateSeenOptionIds(options, monsterHexRelic, modifier.GetSeenPlayerRuneIds(player));
		IReadOnlyList<RelicModel> currentOptions = options;
		for (int i = 0; i < rerollHistory.Count; i++)
		{
			int slotIndex = rerollHistory[i];
			currentOptions = RerollSingleOptionAndTrackMultiplayer(modifier, player, currentOptions, slotIndex, i, seenOptionIds);
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] ResolveRemoteRuneChoice: player={player.NetId} selectedIndex={selectedIndex} rerolls={string.Join(",", rerollHistory)}");
		RelicModel? selectedRelic = selectedIndex >= 0 && selectedIndex < currentOptions.Count ? currentOptions[selectedIndex] : null;
		return new RuneSelectionResult(selectedRelic, currentOptions.ToList(), rerollHistory.Count, null);
	}

	private static bool IsRuneSelectionChoice(PlayerChoiceResult result)
	{
		return TryDecodeRuneChoiceResult(result, out _, out _);
	}

	private static bool TryDecodeRuneChoiceResult(PlayerChoiceResult result, out int selectedIndex, out List<int> rerollHistory)
	{
		selectedIndex = -1;
		rerollHistory = [];
		if (!TryGetIndexPayload(result, out List<int>? payload)
			|| payload.Count < 4
			|| payload[0] != HextechChoiceMagic
			|| payload[1] != ChoiceKindRuneSelection)
		{
			return false;
		}

		selectedIndex = payload[2];
		int rerollCount = Math.Max(0, payload[3]);
		if (payload.Count < rerollCount + 4)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] DecodeRuneChoiceResult: malformed payload={string.Join(",", payload)}");
			return false;
		}

		rerollHistory = payload.Skip(4).Take(rerollCount).ToList();
		return true;
	}
}
