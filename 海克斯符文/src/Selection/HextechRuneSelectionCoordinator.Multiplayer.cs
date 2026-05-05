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
	private static async Task<MonsterHexKind?> SelectRunesForAllPlayersMultiplayer(
		RunState runState,
		HextechMayhemModifier modifier,
		int actIndex,
		HextechRarityTier rarity,
		MonsterHexKind? initialMonsterHex,
		RelicModel? monsterHexRelic)
	{
		RunManager runManager = RunManager.Instance;
		PlayerChoiceSynchronizer? synchronizer = await WaitForPlayerChoiceSynchronizerAsync(runManager);
		if (synchronizer == null)
		{
			MonsterHexKind? fallbackMonsterHex = initialMonsterHex;
			foreach (Player player in runState.Players)
			{
				HashSet<ModelId> excludedIds = CreateBaseExcludedIds(modifier, player, monsterHexRelic);
				List<RelicModel> options = BuildStableSelectableRunesForRarity(player, rarity, runState, excludedIds);
				HashSet<ModelId> enemyRerollExcludedIds = CreateEnemyHexRerollExcludedIds(options);
				RuneSelectionResult selection = await SelectRune(
					modifier,
					player,
					options,
					monsterHexRelic,
					new HextechEnemyHexAdjustmentOptions
					{
						InitialHex = fallbackMonsterHex,
						ControlsEnabled = runManager.NetService.Type == NetGameType.Host && IsLocalPlayer(runManager, player),
						RerollFunc = (currentHex, rerollOrdinal) => RerollEnemyHexForAct(modifier, rarity, runState, actIndex, currentHex, rerollOrdinal, enemyRerollExcludedIds)
					});
				fallbackMonsterHex = selection.FinalMonsterHex;
				monsterHexRelic = CreateMonsterHexRelic(fallbackMonsterHex);
				RelicModel selected = selection.SelectedRelic ?? options[0];
				HextechTelemetry.RecordRuneChoice(runState, actIndex, rarity, player, selection.FinalOptions, selected, selection.RerollCount);
				await RelicCmd.Obtain(selected, player);
			}

			return fallbackMonsterHex;
		}

		EnemyHexAdjustmentSyncContext? enemyHexSync = CreateEnemyHexAdjustmentSyncContext(runManager, runState, synchronizer, actIndex, initialMonsterHex);
		HashSet<ModelId> enemyRerollExcludedIdsForAllPlayers = new();
		List<PendingRuneSelection> pendingSelections = [];
		foreach (Player player in runState.Players)
		{
			HashSet<ModelId> excludedIds = CreateBaseExcludedIds(modifier, player, monsterHexRelic);
			List<RelicModel> options = BuildStableSelectableRunesForRarity(player, rarity, runState, excludedIds);
			enemyRerollExcludedIdsForAllPlayers.UnionWith(CreateEnemyHexRerollExcludedIds(options));
			MarkRelicsSeen(options);
			modifier.RecordSeenPlayerRunes(player, options);

			uint choiceId = synchronizer.ReserveChoiceId(player);
			pendingSelections.Add(new PendingRuneSelection(player, options, choiceId, IsLocalPlayer(runManager, player)));
			Log.Info($"[{ModInfo.Id}][Mayhem] RuneChoice pending: player={player.NetId} choiceId={choiceId} local={IsLocalPlayer(runManager, player)} options={string.Join(",", options.Select(o => (o.CanonicalInstance?.Id ?? o.Id).Entry))}");
		}

		RuneSelectionResult[] selectedRelics = [];
		try
		{
			selectedRelics = await Task.WhenAll(pendingSelections.Select(selection =>
				SelectRuneMultiplayer(
					modifier,
					selection,
					synchronizer,
					monsterHexRelic,
					CreateEnemyHexAdjustmentOptionsForSelection(
						modifier,
						runManager,
						runState,
						actIndex,
						rarity,
						initialMonsterHex,
						enemyRerollExcludedIdsForAllPlayers,
						enemyHexSync,
						selection),
					screen => CompleteLocalEnemyHexAdjustmentSync(runManager, enemyHexSync, screen))));
			for (int i = 0; i < pendingSelections.Count; i++)
			{
				PendingRuneSelection selection = pendingSelections[i];
				RuneSelectionResult selectedResult = selectedRelics[i];
				RelicModel selectedRelic = selectedResult.SelectedRelic ?? selection.Options[0];
				HextechTelemetry.RecordRuneChoice(runState, actIndex, rarity, selection.Player, selectedResult.FinalOptions, selectedRelic, selectedResult.RerollCount);
				await RelicCmd.Obtain(selectedRelic, selection.Player);
			}

			await SynchronizeActSelectionApplied(runState, synchronizer, actIndex);
			return enemyHexSync?.CurrentMonsterHex ?? initialMonsterHex;
		}
		finally
		{
			await DismissBlockingSelectionScreens(selectedRelics);
		}
	}

	private static async Task DismissBlockingSelectionScreens(IEnumerable<RuneSelectionResult> selections)
	{
		foreach (HextechRuneSelectionScreen screen in selections
			.Select(static selection => selection.BlockingScreen)
			.Where(static screen => screen != null)
			.Distinct()
			.Cast<HextechRuneSelectionScreen>())
		{
			await screen.DismissAfterSelectionComplete();
		}
	}

	private static EnemyHexAdjustmentSyncContext? CreateEnemyHexAdjustmentSyncContext(
		RunManager runManager,
		RunState runState,
		PlayerChoiceSynchronizer synchronizer,
		int actIndex,
		MonsterHexKind? initialMonsterHex)
	{
		Player? authorityPlayer = GetActRollAuthorityPlayer(runManager, runState);
		if (authorityPlayer == null)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] EnemyHexAdjustmentSync: no authority player act={actIndex}");
			return null;
		}

		uint choiceId = synchronizer.ReserveChoiceId(authorityPlayer);
		Log.Info($"[{ModInfo.Id}][Mayhem] EnemyHexAdjustmentSync: reserved act={actIndex} authority={authorityPlayer.NetId} choiceId={choiceId}");
		return new EnemyHexAdjustmentSyncContext(synchronizer, authorityPlayer, choiceId, actIndex, initialMonsterHex);
	}

	private static HextechEnemyHexAdjustmentOptions? CreateEnemyHexAdjustmentOptionsForSelection(
		HextechMayhemModifier modifier,
		RunManager runManager,
		RunState runState,
		int actIndex,
		HextechRarityTier rarity,
		MonsterHexKind? initialMonsterHex,
		IReadOnlySet<ModelId> enemyRerollExcludedIds,
		EnemyHexAdjustmentSyncContext? syncContext,
		PendingRuneSelection selection)
	{
		if (!selection.IsLocal)
		{
			return null;
		}

		bool isAuthorityLocal = syncContext != null && IsLocalPlayer(runManager, syncContext.AuthorityPlayer);
		return new HextechEnemyHexAdjustmentOptions
		{
			InitialHex = syncContext?.CurrentMonsterHex ?? initialMonsterHex,
			ControlsEnabled = isAuthorityLocal,
			RerollFunc = isAuthorityLocal
				? (currentHex, rerollOrdinal) => RerollEnemyHexForAct(modifier, rarity, runState, actIndex, currentHex, rerollOrdinal, enemyRerollExcludedIds)
				: null,
			Changed = isAuthorityLocal && syncContext != null
				? (monsterHex, removed, rerollCount) => SendEnemyHexAdjustment(syncContext, monsterHex, removed, rerollCount, isFinal: false)
				: null,
			ScreenCreated = !isAuthorityLocal && syncContext != null
				? screen => syncContext.RemoteReceiveTask = ReceiveEnemyHexAdjustments(syncContext, screen)
				: null
		};
	}

	private static async Task CompleteLocalEnemyHexAdjustmentSync(RunManager runManager, EnemyHexAdjustmentSyncContext? syncContext, HextechRuneSelectionScreen screen)
	{
		if (syncContext == null)
		{
			return;
		}

		if (IsLocalPlayer(runManager, syncContext.AuthorityPlayer))
		{
			SendEnemyHexAdjustment(syncContext, screen.CurrentMonsterHex, screen.EnemyHexRemoved, screen.EnemyHexRerollCount, isFinal: true);
			return;
		}

		if (syncContext.RemoteReceiveTask != null)
		{
			await syncContext.RemoteReceiveTask;
		}
	}

	private static void SendEnemyHexAdjustment(
		EnemyHexAdjustmentSyncContext syncContext,
		MonsterHexKind? monsterHex,
		bool removed,
		int rerollCount,
		bool isFinal)
	{
		if (syncContext.FinalSent)
		{
			return;
		}

		syncContext.CurrentMonsterHex = removed ? null : monsterHex;
		syncContext.Removed = removed;
		syncContext.RerollCount = rerollCount;
		EnemyHexAdjustmentPayload payload = new(
			syncContext.ActIndex,
			syncContext.Sequence,
			monsterHex,
			removed,
			rerollCount,
			isFinal);
		syncContext.Synchronizer.SyncLocalChoice(syncContext.AuthorityPlayer, syncContext.NextChoiceId, CreateEnemyHexAdjustmentChoiceResult(payload));
		Log.Info($"[{ModInfo.Id}][Mayhem] EnemyHexAdjustmentSync send: act={syncContext.ActIndex} choiceId={syncContext.NextChoiceId} seq={syncContext.Sequence} removed={removed} hex={monsterHex} rerolls={rerollCount} final={isFinal}");
		if (isFinal)
		{
			syncContext.FinalSent = true;
			return;
		}

		syncContext.Sequence++;
		syncContext.NextChoiceId = syncContext.Synchronizer.ReserveChoiceId(syncContext.AuthorityPlayer);
	}

	private static async Task ReceiveEnemyHexAdjustments(EnemyHexAdjustmentSyncContext syncContext, HextechRuneSelectionScreen screen)
	{
		while (screen.IsInsideTree())
		{
			(PlayerChoiceResult result, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
				syncContext.Synchronizer,
				syncContext.AuthorityPlayer,
				syncContext.NextChoiceId,
				choice => TryDecodeEnemyHexAdjustment(choice, syncContext.ActIndex, out _),
				$"enemy-hex-adjustment act={syncContext.ActIndex}");
			if (!TryDecodeEnemyHexAdjustment(result, syncContext.ActIndex, out EnemyHexAdjustmentPayload payload))
			{
				Log.Warn($"[{ModInfo.Id}][Mayhem] EnemyHexAdjustmentSync malformed: act={syncContext.ActIndex} choiceId={receivedChoiceId}");
				return;
			}

			syncContext.CurrentMonsterHex = payload.Removed ? null : payload.MonsterHex;
			syncContext.Removed = payload.Removed;
			syncContext.RerollCount = payload.RerollCount;
			syncContext.Sequence = payload.Sequence + 1;
			screen.ApplyEnemyHexAdjustment(payload.MonsterHex, payload.Removed, payload.RerollCount);
			Log.Info($"[{ModInfo.Id}][Mayhem] EnemyHexAdjustmentSync receive: act={syncContext.ActIndex} choiceId={receivedChoiceId} seq={payload.Sequence} removed={payload.Removed} hex={payload.MonsterHex} rerolls={payload.RerollCount} final={payload.IsFinal}");
			if (payload.IsFinal)
			{
				return;
			}

			syncContext.NextChoiceId = syncContext.Synchronizer.ReserveChoiceId(syncContext.AuthorityPlayer);
		}
	}

	private static async Task SynchronizeActSelectionApplied(RunState runState, PlayerChoiceSynchronizer synchronizer, int actIndex)
	{
		RunManager runManager = RunManager.Instance;
		List<Task> pendingAcks = [];
		foreach (Player player in runState.Players)
		{
			uint choiceId = synchronizer.ReserveChoiceId(player);
			if (IsLocalPlayer(runManager, player))
			{
				synchronizer.SyncLocalChoice(player, choiceId, CreateActSelectionAppliedChoiceResult(actIndex));
				Log.Info($"[{ModInfo.Id}][Mayhem] ActSelectionApplied sync local: act={actIndex} player={player.NetId} choiceId={choiceId}");
				continue;
			}

			pendingAcks.Add(WaitForRemoteActSelectionApplied(synchronizer, player, choiceId, actIndex));
		}

		if (pendingAcks.Count == 0)
		{
			return;
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] ActSelectionApplied waiting: act={actIndex} remoteCount={pendingAcks.Count}");
		Task allAcks = Task.WhenAll(pendingAcks);
		Task timeout = WaitForFramesOrRunChangeAsync(runState, ActSelectionAppliedAckTimeoutFrames);
		if (await Task.WhenAny(allAcks, timeout) == allAcks)
		{
			await allAcks;
			Log.Info($"[{ModInfo.Id}][Mayhem] ActSelectionApplied complete: act={actIndex}");
			return;
		}

		int completed = pendingAcks.Count(static task => task.IsCompletedSuccessfully);
		Log.Warn($"[{ModInfo.Id}][Mayhem] ActSelectionApplied timeout: act={actIndex} completed={completed}/{pendingAcks.Count}; continuing to avoid blocking map flow");
	}

	private static async Task WaitForRemoteActSelectionApplied(PlayerChoiceSynchronizer synchronizer, Player player, uint choiceId, int actIndex)
	{
		try
		{
			(PlayerChoiceResult remoteAck, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
				synchronizer,
				player,
				choiceId,
				result => TryDecodeActSelectionApplied(result, actIndex),
				$"act-selection-applied act={actIndex}");
			if (!TryDecodeActSelectionApplied(remoteAck, actIndex))
			{
				Log.Warn($"[{ModInfo.Id}][Mayhem] ActSelectionApplied malformed ack: act={actIndex} player={player.NetId} choiceId={choiceId}");
				return;
			}

			Log.Info($"[{ModInfo.Id}][Mayhem] ActSelectionApplied remote: act={actIndex} player={player.NetId} choiceId={receivedChoiceId}");
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] ActSelectionApplied wait failed: act={actIndex} player={player.NetId} choiceId={choiceId} error={ex}");
		}
	}

	private static async Task WaitForFramesOrRunChangeAsync(RunState runState, int frameCount)
	{
		for (int i = 0; i < frameCount && IsCurrentRun(runState); i++)
		{
			if (NGame.Instance?.IsInsideTree() == true)
			{
				await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			else
			{
				await Task.Yield();
			}
		}
	}

	private static PlayerChoiceResult CreateActSelectionAppliedChoiceResult(int actIndex)
	{
		return PlayerChoiceResult.FromIndexes([ HextechChoiceMagic, ChoiceKindActSelectionApplied, actIndex, 1 ]);
	}

	private static bool TryDecodeActSelectionApplied(PlayerChoiceResult result, int expectedActIndex)
	{
		return TryGetIndexPayload(result, out List<int>? payload)
			&& payload.Count >= 4
			&& payload[0] == HextechChoiceMagic
			&& payload[1] == ChoiceKindActSelectionApplied
			&& payload[2] == expectedActIndex
			&& payload[3] == 1;
	}

	private static PlayerChoiceResult CreateEnemyHexAdjustmentChoiceResult(EnemyHexAdjustmentPayload payload)
	{
		return PlayerChoiceResult.FromIndexes(
		[
			HextechChoiceMagic,
			ChoiceKindEnemyHexAdjustment,
			payload.ActIndex,
			payload.Sequence,
			payload.Removed ? 1 : 0,
			payload.MonsterHex.HasValue ? (int)payload.MonsterHex.Value : -1,
			payload.RerollCount,
			payload.IsFinal ? 1 : 0
		]);
	}

	private static bool TryDecodeEnemyHexAdjustment(PlayerChoiceResult result, int expectedActIndex, out EnemyHexAdjustmentPayload payload)
	{
		payload = default;
		if (!TryGetIndexPayload(result, out List<int>? indexes)
			|| indexes.Count < 8
			|| indexes[0] != HextechChoiceMagic
			|| indexes[1] != ChoiceKindEnemyHexAdjustment
			|| indexes[2] != expectedActIndex)
		{
			return false;
		}

		MonsterHexKind? monsterHex = null;
		if (indexes[5] >= 0)
		{
			if (!Enum.IsDefined(typeof(MonsterHexKind), indexes[5]))
			{
				return false;
			}

			monsterHex = (MonsterHexKind)indexes[5];
		}

		payload = new EnemyHexAdjustmentPayload(
			indexes[2],
			Math.Max(0, indexes[3]),
			monsterHex,
			indexes[4] != 0,
			Math.Max(0, indexes[6]),
			indexes[7] != 0);
		return true;
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

	private static async Task<(PlayerChoiceResult Result, uint ChoiceId)> WaitForRemoteHextechChoice(
		PlayerChoiceSynchronizer synchronizer,
		Player player,
		uint initialChoiceId,
		Func<PlayerChoiceResult, bool> isExpected,
		string context)
	{
		uint choiceId = initialChoiceId;
		int skipped = 0;
		while (true)
		{
			PlayerChoiceResult remoteChoice = await synchronizer.WaitForRemoteChoice(player, choiceId);
			if (isExpected(remoteChoice))
			{
				if (skipped > 0)
				{
					Log.Info($"[{ModInfo.Id}][Mayhem] WaitForRemoteHextechChoice: accepted after skipping foreign choices context={context} player={player.NetId} choiceId={choiceId} skipped={skipped}");
				}

				return (remoteChoice, choiceId);
			}

			skipped++;
			Log.Warn($"[{ModInfo.Id}][Mayhem] WaitForRemoteHextechChoice: skipped non-hextech choice context={context} player={player.NetId} choiceId={choiceId} skipped={skipped} type={remoteChoice.ChoiceType} result={remoteChoice}");
			choiceId = synchronizer.ReserveChoiceId(player);
		}
	}

	private static bool TryGetIndexPayload(PlayerChoiceResult result, out List<int> payload)
	{
		payload = [];
		try
		{
			List<int>? indexes = result.AsIndexes();
			if (indexes == null)
			{
				return false;
			}

			payload = indexes;
			return true;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}
}
