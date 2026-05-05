using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;

namespace HextechRunes;

internal sealed partial class HextechRuneSelectionScreen : Control, IOverlayScreen, IScreenContext
{
	private void OnHolderSelected(RelicModel relic)
	{
		if (_choiceLocked)
		{
			return;
		}

		_choiceLocked = true;
		foreach (Button holder in _holders)
		{
			holder.Disabled = true;
		}
		foreach (Button rerollButton in _rerollButtons)
		{
			rerollButton.Disabled = true;
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.OnHolderSelected: relic={(relic.CanonicalInstance?.Id ?? relic.Id).Entry}");
		GetViewport()?.SetInputAsHandled();
		_completionSource.TrySetResult([relic]);
	}

	private void OnRerollPressed(int slotIndex)
	{
		if (_choiceLocked || _rerollFunc == null || _rerolledSlots.ElementAtOrDefault(slotIndex))
		{
			return;
		}

		IReadOnlyList<RelicModel> rerolled = _rerollFunc(_relics, slotIndex, _rerollHistory.Count);
		if (rerolled.Count != _relics.Count)
		{
			return;
		}

		string oldRelic = (_relics[slotIndex].CanonicalInstance?.Id ?? _relics[slotIndex].Id).Entry;
		string newRelic = (rerolled[slotIndex].CanonicalInstance?.Id ?? rerolled[slotIndex].Id).Entry;
		if (oldRelic == newRelic)
		{
			return;
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.OnRerollPressed: slot={slotIndex} old={oldRelic} new={newRelic}");
		_relics = rerolled.ToList();
		_rerolledSlots[slotIndex] = true;
		_rerollHistory.Add(slotIndex);
		RebuildCards();
	}

	private void OnEnemyHexRerollPressed()
	{
		if (_choiceLocked || _enemyHexRemoved || _enemyHexRerollFunc == null)
		{
			return;
		}

		MonsterHexKind? rerolled = _enemyHexRerollFunc(_monsterHexKind, _enemyHexRerollCount);
		if (rerolled == null || rerolled == _monsterHexKind)
		{
			return;
		}

		_monsterHexKind = rerolled;
		_monsterHexRelic = CreateMonsterHexRelic(rerolled);
		_enemyHexRerollCount++;
		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.OnEnemyHexRerollPressed: hex={rerolled} count={_enemyHexRerollCount}");
		NotifyEnemyHexChanged();
		RebuildEnemyPreview();
	}

	private void OnEnemyHexRemovePressed()
	{
		if (_choiceLocked)
		{
			return;
		}

		if (_enemyHexRemoved)
		{
			_enemyHexRemoved = false;
			_monsterHexKind = _monsterHexBeforeRemoval ?? _monsterHexKind;
			_monsterHexRelic = CreateMonsterHexRelic(_monsterHexKind);
			Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.OnEnemyHexRemovePressed: undo hex={_monsterHexKind}");
		}
		else
		{
			_monsterHexBeforeRemoval = _monsterHexKind;
			_enemyHexRemoved = true;
			Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.OnEnemyHexRemovePressed: remove previous={_monsterHexBeforeRemoval}");
		}

		NotifyEnemyHexChanged();
		RebuildEnemyPreview();
	}

	public void ApplyEnemyHexAdjustment(MonsterHexKind? monsterHex, bool removed, int rerollCount)
	{
		_enemyHexRemoved = removed;
		_enemyHexRerollCount = Math.Max(0, rerollCount);
		if (!removed || monsterHex.HasValue)
		{
			_monsterHexKind = monsterHex;
			_monsterHexRelic = CreateMonsterHexRelic(monsterHex);
		}

		if (removed && monsterHex.HasValue)
		{
			_monsterHexBeforeRemoval = monsterHex;
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.ApplyEnemyHexAdjustment: removed={removed} hex={monsterHex} count={_enemyHexRerollCount}");
		RebuildEnemyPreview();
	}

	private void NotifyEnemyHexChanged()
	{
		_enemyHexChanged?.Invoke(CurrentMonsterHex, _enemyHexRemoved, _enemyHexRerollCount);
	}

	public async Task<IEnumerable<RelicModel>> RelicsSelected(bool removeOverlay = true)
	{
		IEnumerable<RelicModel> result = await _completionSource.Task;
		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.RelicsSelected: begin dismiss mousePressed={Input.IsMouseButtonPressed(MouseButton.Left)}");
		await WaitForMouseReleaseAsync();
		if (!removeOverlay)
		{
			ShowWaitingForRemotePlayers();
			Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.RelicsSelected: keeping overlay until multiplayer sync completes");
			return result;
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.RelicsSelected: removing overlay");
		NOverlayStack.Instance?.Remove(this);
		return result;
	}

	public async Task DismissAfterSelectionComplete()
	{
		if (!IsInsideTree())
		{
			return;
		}

		await WaitForMouseReleaseAsync();
		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.DismissAfterSelectionComplete: removing overlay");
		NOverlayStack.Instance?.Remove(this);
	}

	private async Task WaitForMouseReleaseAsync()
	{
		if (!await AwaitProcessFrameIfInsideTreeAsync())
		{
			return;
		}

		while (Input.IsMouseButtonPressed(MouseButton.Left))
		{
			if (!await AwaitProcessFrameIfInsideTreeAsync())
			{
				return;
			}
		}
		await AwaitProcessFrameIfInsideTreeAsync();
	}

	private async Task<bool> AwaitProcessFrameIfInsideTreeAsync()
	{
		if (!IsInsideTree())
		{
			return false;
		}

		SceneTree tree = GetTree();
		if (tree == null)
		{
			return false;
		}

		await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
		return IsInsideTree();
	}

	private void ShowWaitingForRemotePlayers()
	{
		if (_statusLabel == null)
		{
			return;
		}

		_statusLabel.SetTextAutoSize(new LocString(LocTable, "HEXTECH_WAITING_FOR_PLAYERS").GetRawText());
		_statusLabel.Visible = true;
	}

	public void AfterOverlayOpened()
	{
		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.AfterOverlayOpened");
		Modulate = Colors.White;
		Visible = true;
		TryGrabOverlayFocus();
	}

	public void AfterOverlayClosed()
	{
		if (_closed)
		{
			return;
		}

		_closed = true;
		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.AfterOverlayClosed");
		if (!_choiceLocked)
		{
			Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.AfterOverlayClosed: cancelling unresolved selection");
			_completionSource.TrySetCanceled();
		}

		QueueFree();
	}

	public void AfterOverlayShown()
	{
		Visible = true;
		TryGrabOverlayFocus();
	}

	public void AfterOverlayHidden()
	{
		if (_closed)
		{
			return;
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.AfterOverlayHidden: choiceLocked={_choiceLocked} capstoneOpen={NCapstoneContainer.Instance?.InUse == true} mapOpen={NMapScreen.Instance?.IsOpen == true}");
		Visible = false;
		if (!_choiceLocked && !_restoreAfterMapReopenQueued && IsInsideTree())
		{
			_restoreAfterMapReopenQueued = true;
			_ = TaskHelper.RunSafely(RestoreAfterMapReopenAsync());
		}
	}

	private async Task RestoreAfterMapReopenAsync()
	{
		try
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (!IsInsideTree() || _choiceLocked)
			{
				return;
			}

			bool isTopOverlay = ReferenceEquals(NOverlayStack.Instance?.Peek(), this);
			bool capstoneOpen = NCapstoneContainer.Instance?.InUse == true;
			bool mapOpen = NMapScreen.Instance?.IsOpen == true;
			if (!isTopOverlay || capstoneOpen || !mapOpen)
			{
				return;
			}

			Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.RestoreAfterMapReopen: closing map reopened over unresolved selection");
			NMapScreen.Instance?.Close(animateOut: false);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			NOverlayStack.Instance?.ShowOverlays();
		}
		finally
		{
			_restoreAfterMapReopenQueued = false;
		}
	}

	private void TryGrabOverlayFocus()
	{
		if (_closed || !IsInsideTree() || !IsVisibleInTree() || FocusMode == FocusModeEnum.None)
		{
			return;
		}

		GrabFocus();
	}
}
