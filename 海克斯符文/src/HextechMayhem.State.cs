using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	private readonly HextechMayhemActState _actState = new();
	private readonly HextechMayhemCombatTrackingState _combatTracking = new();
	private readonly HextechMayhemChoiceHistoryState _choiceHistory = new();
	private IReadOnlyList<MonsterHexKind>? _cachedActiveMonsterHexes;
	private HashSet<MonsterHexKind>? _cachedActiveMonsterHexSet;
	private int _cachedActiveMonsterHexActIndex = int.MinValue;
	private bool _cachedActiveMonsterHexCombatRecovery;

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedRarityByAct
	{
		get => _actState.SavedRarityByAct;
		set
		{
			_actState.SavedRarityByAct = value;
			InvalidateActiveMonsterHexCache();
		}
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedMonsterHexByAct
	{
		get => _actState.SavedMonsterHexByAct;
		set
		{
			_actState.SavedMonsterHexByAct = value;
			InvalidateActiveMonsterHexCache();
		}
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public int[] SavedResolvedActs
	{
		get => _actState.SavedResolvedActs;
		set
		{
			_actState.SavedResolvedActs = value;
			InvalidateActiveMonsterHexCache();
		}
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedTelemetryChoicesJson
	{
		get => _choiceHistory.SavedTelemetryChoicesJson;
		set => _choiceHistory.SavedTelemetryChoicesJson = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedSeenPlayerRuneIdsJson
	{
		get => _choiceHistory.SavedSeenPlayerRuneIdsJson;
		set => _choiceHistory.SavedSeenPlayerRuneIdsJson = value;
	}

	[SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
	public string SavedCombatTrackingJson
	{
		get => _combatTracking.Serialize();
		set => _combatTracking.Restore(value);
	}

	public override LocString Title => new("modifiers", "HEXTECH_MAYHEM.title");

	public override LocString Description => new("modifiers", "HEXTECH_MAYHEM.description");

	protected override string IconPath => ImageHelper.GetImagePath("powers/missing_power.png");

	public override IEnumerable<IHoverTip> HoverTips => [];

	public RunState ActiveRunState => RunState;

	public bool IsActResolved(int actIndex)
	{
		return _actState.IsResolved(actIndex);
	}

	public void SetActResolved(int actIndex, bool resolved)
	{
		_actState.SetResolved(actIndex, resolved);
		InvalidateActiveMonsterHexCache();
	}

	public bool TryRecoverResolvedActsFromPlayerRelics(string reason)
	{
		int currentActIndex = Math.Min(RunState.CurrentActIndex, _actState.ActCount - 1);
		if (currentActIndex < 0 || RunState.Players.Count == 0)
		{
			return false;
		}

		int telemetryRecoverThroughAct = GetHighestActResolvedByTelemetryChoices(currentActIndex);
		int countRecoverThroughAct = GetHighestActResolvedByPlayerRuneCounts(currentActIndex == 0 ? 0 : currentActIndex - 1);
		int recoverThroughAct = Math.Max(telemetryRecoverThroughAct, countRecoverThroughAct);
		if (recoverThroughAct < 0)
		{
			return false;
		}

		bool changed = false;
		for (int actIndex = 0; actIndex <= recoverThroughAct; actIndex++)
		{
			changed |= _actState.TryMarkResolved(actIndex);

			if (TryInferRarityForAct(actIndex, out HextechRarityTier rarity))
			{
				changed |= _actState.TrySetRarityIfMissing(actIndex, rarity);
			}
		}

		if (changed)
		{
			InvalidateActiveMonsterHexCache();
			Log.Info($"[{ModInfo.Id}][Mayhem] Recovered resolved acts from saved choices/player relics: reason={reason} currentAct={RunState.CurrentActIndex} recoverThrough={recoverThroughAct} telemetryThrough={telemetryRecoverThroughAct} countThrough={countRecoverThroughAct} {_actState.Describe()} counts={DescribePlayerHexCounts()} choices={DescribeTelemetryChoiceCounts()}");
		}

		return changed;
	}

	public string DescribeActState()
	{
		return _actState.Describe();
	}

	public HextechRarityTier? GetRarityForAct(int actIndex)
	{
		return _actState.GetRarity(actIndex);
	}

	public void SetRarityForAct(int actIndex, HextechRarityTier rarity)
	{
		_actState.SetRarity(actIndex, rarity);
		InvalidateActiveMonsterHexCache();
	}

	public MonsterHexKind? GetMonsterHexForAct(int actIndex)
	{
		return _actState.GetMonsterHex(actIndex);
	}

	public void SetMonsterHexForAct(int actIndex, MonsterHexKind hex)
	{
		_actState.SetMonsterHex(actIndex, hex);
		InvalidateActiveMonsterHexCache();
	}

	public void ClearMonsterHexForAct(int actIndex)
	{
		_actState.ClearMonsterHex(actIndex);
		InvalidateActiveMonsterHexCache();
	}

	public IReadOnlyList<MonsterHexKind> GetActiveMonsterHexes()
	{
		EnsureActiveMonsterHexCache();
		return _cachedActiveMonsterHexes!;
	}

	private bool ShouldRecoverMonsterHexInCombat(int actIndex)
	{
		return actIndex <= RunState.CurrentActIndex && RunState.CurrentRoom is CombatRoom;
	}

	public void ResetForNewRun()
	{
		_actState.Reset();
		_choiceHistory.Reset();
		ResetCombatTracking();
		InvalidateActiveMonsterHexCache();
	}

	public void DebugSetOnlyMonsterHex(int actIndex, MonsterHexKind hex, HextechRarityTier rarity)
	{
		_actState.DebugSetOnlyMonsterHex(actIndex, hex, rarity);
		_choiceHistory.Reset();

		ResetCombatTracking();
		InvalidateActiveMonsterHexCache();
	}

	public bool HasActiveMonsterHex(MonsterHexKind hex)
	{
		EnsureActiveMonsterHexCache();
		return _cachedActiveMonsterHexSet!.Contains(hex);
	}

	private void EnsureActiveMonsterHexCache()
	{
		int actIndex = RunState.CurrentActIndex;
		bool combatRecovery = ShouldRecoverMonsterHexInCombat(actIndex);
		if (_cachedActiveMonsterHexes != null
			&& _cachedActiveMonsterHexSet != null
			&& _cachedActiveMonsterHexActIndex == actIndex
			&& _cachedActiveMonsterHexCombatRecovery == combatRecovery)
		{
			return;
		}

		IReadOnlyList<MonsterHexKind> activeHexes = _actState.GetActiveMonsterHexes(actIndex, ShouldRecoverMonsterHexInCombat);
		_cachedActiveMonsterHexes = activeHexes;
		_cachedActiveMonsterHexSet = activeHexes.ToHashSet();
		_cachedActiveMonsterHexActIndex = actIndex;
		_cachedActiveMonsterHexCombatRecovery = combatRecovery;
	}

	private void InvalidateActiveMonsterHexCache()
	{
		_cachedActiveMonsterHexes = null;
		_cachedActiveMonsterHexSet = null;
		_cachedActiveMonsterHexActIndex = int.MinValue;
		_cachedActiveMonsterHexCombatRecovery = false;
	}

	public IReadOnlyList<HextechTelemetry.RuneChoiceRecord> GetTelemetryChoiceRecords()
	{
		return _choiceHistory.GetTelemetryChoiceRecords();
	}

	public void RecordTelemetryChoice(HextechTelemetry.RuneChoiceRecord record)
	{
		_choiceHistory.RecordTelemetryChoice(record);
	}

	public HashSet<ModelId> GetSeenPlayerRuneIds(Player player)
	{
		return _choiceHistory.GetSeenPlayerRuneIds(player, RunState);
	}

	public void RecordSeenPlayerRunes(Player player, IEnumerable<RelicModel> relics)
	{
		_choiceHistory.RecordSeenPlayerRunes(player, relics, RunState);
	}

	private int GetHighestActResolvedByTelemetryChoices(int maxActIndex)
	{
		int lastActIndex = _actState.LastActIndexFor(maxActIndex);
		if (lastActIndex < 0 || RunState.Players.Count == 0)
		{
			return -1;
		}

		IReadOnlyList<HextechTelemetry.RuneChoiceRecord> records = GetTelemetryChoiceRecords();
		if (records.Count == 0)
		{
			return -1;
		}

		int highest = -1;
		for (int actIndex = 0; actIndex <= lastActIndex; actIndex++)
		{
			HashSet<int> playerSlots = records
				.Where(record => record.ActIndex == actIndex)
				.Select(static record => record.PlayerSlot)
				.ToHashSet();
			bool allPlayersRecorded = true;
			for (int playerSlot = 0; playerSlot < RunState.Players.Count; playerSlot++)
			{
				if (!playerSlots.Contains(playerSlot))
				{
					allPlayersRecorded = false;
					break;
				}
			}

			if (!allPlayersRecorded)
			{
				break;
			}

			highest = actIndex;
		}

		return highest;
	}

	private int GetHighestActResolvedByPlayerRuneCounts(int maxActIndex)
	{
		int lastActIndex = _actState.LastActIndexFor(maxActIndex);
		if (lastActIndex < 0)
		{
			return -1;
		}

		int minHexCount = int.MaxValue;
		foreach (Player player in RunState.Players)
		{
			int count = player.Relics.Count(HextechCatalog.IsHextechRelic);
			minHexCount = Math.Min(minHexCount, count);
		}

		if (minHexCount == int.MaxValue || minHexCount <= 0)
		{
			return -1;
		}

		return Math.Min(lastActIndex, minHexCount - 1);
	}

	private bool TryInferRarityForAct(int actIndex, out HextechRarityTier rarity)
	{
		return TryInferRarityForActFromTelemetryChoices(actIndex, out rarity)
			|| TryInferRarityForActFromPlayerRelics(actIndex, out rarity);
	}

	private bool TryInferRarityForActFromTelemetryChoices(int actIndex, out HextechRarityTier rarity)
	{
		foreach (HextechTelemetry.RuneChoiceRecord record in GetTelemetryChoiceRecords().Where(record => record.ActIndex == actIndex))
		{
			if (Enum.TryParse(record.Rarity, ignoreCase: true, out rarity))
			{
				return true;
			}
		}

		rarity = default;
		return false;
	}

	private bool TryInferRarityForActFromPlayerRelics(int actIndex, out HextechRarityTier rarity)
	{
		foreach (Player player in RunState.Players)
		{
			RelicModel? relic = player.Relics
				.Where(HextechCatalog.IsHextechRelic)
				.ElementAtOrDefault(actIndex);
			if (HextechCatalog.TryGetPlayerRuneRarity(relic, out rarity))
			{
				return true;
			}
		}

		rarity = default;
		return false;
	}

	private string DescribePlayerHexCounts()
	{
		return string.Join(",", RunState.Players.Select(player => $"{player.NetId}:{player.Relics.Count(HextechCatalog.IsHextechRelic)}"));
	}

	private string DescribeTelemetryChoiceCounts()
	{
		return string.Join(",", GetTelemetryChoiceRecords()
			.GroupBy(static record => record.ActIndex)
			.OrderBy(static group => group.Key)
			.Select(static group => $"{group.Key}:{group.Count()}"));
	}
}
