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
	private readonly record struct PendingRuneSelection(Player Player, List<RelicModel> Options, uint ChoiceId, bool IsLocal);
	private readonly record struct RuneSelectionResult(RelicModel? SelectedRelic, IReadOnlyList<RelicModel> FinalOptions, int RerollCount, MonsterHexKind? FinalMonsterHex, HextechRuneSelectionScreen? BlockingScreen = null);
	private readonly record struct EnemyHexAdjustmentPayload(int ActIndex, int Sequence, MonsterHexKind? MonsterHex, bool Removed, int RerollCount, bool IsFinal);

	private sealed class EnemyHexAdjustmentSyncContext(
		PlayerChoiceSynchronizer synchronizer,
		Player authorityPlayer,
		uint initialChoiceId,
		int actIndex,
		MonsterHexKind? initialMonsterHex)
	{
		public PlayerChoiceSynchronizer Synchronizer { get; } = synchronizer;
		public Player AuthorityPlayer { get; } = authorityPlayer;
		public uint NextChoiceId { get; set; } = initialChoiceId;
		public int ActIndex { get; } = actIndex;
		public int Sequence { get; set; }
		public MonsterHexKind? CurrentMonsterHex { get; set; } = initialMonsterHex;
		public bool Removed { get; set; }
		public int RerollCount { get; set; }
		public bool FinalSent { get; set; }
		public Task? RemoteReceiveTask { get; set; }
	}

	private const int FirstActSilverWeight = 20;
	private const int FirstActGoldWeight = 50;
	private const int FirstActPrismaticWeight = 30;
	private const int HextechChoiceMagic = 0x48585452; // HXTR
	private const int ChoiceKindActRoll = 1;
	private const int ChoiceKindRuneSelection = 2;
	private const int ChoiceKindActSelectionApplied = 3;
	private const int ChoiceKindEnemyHexAdjustment = 4;
	private const int ActSelectionAppliedAckTimeoutFrames = 600;

	private static bool _handlingActSelection;
	private static RunState? _handlingActSelectionRunState;

	public static void ResetActSelectionState()
	{
		_handlingActSelection = false;
		_handlingActSelectionRunState = null;
	}

	public static Task HandleActStarted(HextechMayhemModifier modifier)
	{
		return HandleActSelection(modifier.ActiveRunState, modifier);
	}

	public static async Task HandleActSelection(RunState runState, HextechMayhemModifier modifier)
	{
		int actIndex = runState.CurrentActIndex;
		if (!modifier.IsActResolved(actIndex) && modifier.TryRecoverResolvedActsFromPlayerRelics(nameof(HandleActSelection)))
		{
			HextechEnemyUi.Refresh(modifier);
		}

		if (_handlingActSelection
			&& _handlingActSelectionRunState != null
			&& !ReferenceEquals(_handlingActSelectionRunState, runState))
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection: clearing stale handling state for previous run");
			ResetActSelectionState();
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection enter: room={runState.CurrentRoom?.GetType().Name ?? "null"} actIndex={actIndex} resolved={modifier.IsActResolved(actIndex)} handling={_handlingActSelection}");
		if (_handlingActSelection || !IsCurrentRun(runState) || actIndex < 0 || actIndex > 2 || modifier.IsActResolved(actIndex))
		{
			Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection skip");
			return;
		}

		_handlingActSelection = true;
		_handlingActSelectionRunState = runState;
		bool reopenMapAfterSelection = false;
		try
		{
			if (NMapScreen.Instance?.IsOpen == true && NGame.Instance != null)
			{
				Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection: closing map before showing selection overlay");
				NMapScreen.Instance.Close(animateOut: false);
				reopenMapAfterSelection = true;
				await NGame.Instance.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
			}
			if (!IsCurrentRun(runState))
			{
				Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection abort: run is no longer current");
				return;
			}

			foreach (Player player in runState.Players)
			{
				RemoveRunesFromGrabBags(player);
			}

			(HextechRarityTier rarity, MonsterHexKind monsterHex) = await ResolveActRoll(runState, modifier, actIndex);
			Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection rarity: act={actIndex} rarity={rarity}");
			Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection monsterHex: act={actIndex} hex={monsterHex}");
			MonsterHexKind? finalMonsterHex = monsterHex;
			RelicModel? monsterHexRelic = CreateMonsterHexRelic(finalMonsterHex);

			NetGameType gameType = RunManager.Instance.NetService.Type;
			if (gameType is NetGameType.Singleplayer or NetGameType.None)
			{
				foreach (Player player in runState.Players)
				{
					HashSet<ModelId> excludedIds = CreateBaseExcludedIds(modifier, player, monsterHexRelic);
					List<RelicModel> options = BuildSelectableRunesForRarity(player, rarity, runState, excludedIds);
					HashSet<ModelId> enemyRerollExcludedIds = CreateEnemyHexRerollExcludedIds(options);
					Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection options: player={player.NetId} count={options.Count} ids={string.Join(",", options.Select(o => (o.CanonicalInstance?.Id ?? o.Id).Entry))}");
					RuneSelectionResult selection = await SelectRune(
						modifier,
						player,
						options,
						monsterHexRelic,
						new HextechEnemyHexAdjustmentOptions
						{
							InitialHex = finalMonsterHex,
							ControlsEnabled = true,
							RerollFunc = (currentHex, rerollOrdinal) => RerollEnemyHexForAct(modifier, rarity, runState, actIndex, currentHex, rerollOrdinal, enemyRerollExcludedIds)
						});
					if (!IsCurrentRun(runState))
					{
						Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection abort: selection returned for stale run");
						return;
					}
					finalMonsterHex = selection.FinalMonsterHex;
					monsterHexRelic = CreateMonsterHexRelic(finalMonsterHex);
					RelicModel selected = selection.SelectedRelic ?? options[0];
					HextechTelemetry.RecordRuneChoice(runState, actIndex, rarity, player, selection.FinalOptions, selected, selection.RerollCount);
					await RelicCmd.Obtain(selected, player);
					Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection obtained: player={player.NetId} relic={(selected.CanonicalInstance?.Id ?? selected.Id).Entry}");
				}
			}
			else
			{
				finalMonsterHex = await SelectRunesForAllPlayersMultiplayer(runState, modifier, actIndex, rarity, finalMonsterHex, monsterHexRelic);
			}
			if (!IsCurrentRun(runState))
			{
				Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection abort: run changed before resolving act");
				return;
			}

			if (finalMonsterHex.HasValue)
			{
				modifier.SetMonsterHexForAct(actIndex, finalMonsterHex.Value);
			}
			else
			{
				modifier.ClearMonsterHexForAct(actIndex);
			}
			modifier.SetActResolved(actIndex, true);
			HextechEnemyUi.Refresh(modifier);
			await modifier.ApplyToCurrentEnemiesIfNeeded();
			await PersistActSelection(runState, actIndex);
			Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection resolved: act={actIndex}");
		}
		catch (OperationCanceledException)
		{
			Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection abort: selection overlay closed before choice act={actIndex}");
		}
		finally
		{
			if (reopenMapAfterSelection
				&& IsCurrentRun(runState)
				&& NMapScreen.Instance != null
				&& !NMapScreen.Instance.IsOpen)
			{
				Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection: reopening map after selection overlay");
				NMapScreen.Instance.Open();
			}

			if (ReferenceEquals(_handlingActSelectionRunState, runState))
			{
				ResetActSelectionState();
			}
			Log.Info($"[{ModInfo.Id}][Mayhem] HandleHextechActSelection exit: act={actIndex}");
		}
	}

	private static async Task PersistActSelection(RunState runState, int actIndex)
	{
		try
		{
			if (!IsCurrentRun(runState) || RunManager.Instance.NetService.Type == NetGameType.Replay)
			{
				return;
			}

			await SaveManager.Instance.SaveRun(null!, saveProgress: false);
			Log.Info($"[{ModInfo.Id}][Mayhem] PersistActSelection: saved current run after resolving act={actIndex}");
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] PersistActSelection failed: act={actIndex} error={ex}");
		}
	}
}
