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
	private static HextechRarityTier RollRandomRarity(HextechMayhemModifier modifier, int actIndex, RunState runState)
	{
		if (actIndex == 0)
		{
			return RollWeightedRarity(runState, FirstActSilverWeight, FirstActGoldWeight, FirstActPrismaticWeight, deterministic: false, actIndex);
		}

		if (actIndex == 1 && modifier.GetRarityForAct(0) == HextechRarityTier.Silver)
		{
			return RollWeightedRarity(runState, 0, 1, 1, deterministic: false, actIndex);
		}

		return (HextechRarityTier)runState.Rng.Niche.NextInt(3);
	}

	private static HextechRarityTier RollStableRarity(HextechMayhemModifier modifier, int actIndex, RunState runState)
	{
		if (actIndex == 0)
		{
			return RollWeightedRarity(runState, FirstActSilverWeight, FirstActGoldWeight, FirstActPrismaticWeight, deterministic: true, actIndex);
		}

		if (actIndex == 1 && modifier.GetRarityForAct(0) == HextechRarityTier.Silver)
		{
			return RollWeightedRarity(runState, 0, 1, 1, deterministic: true, actIndex);
		}

		return (HextechRarityTier)HextechStableRandom.Index(runState, 3, "act-roll-rarity", actIndex.ToString());
	}

	private static async Task<(HextechRarityTier Rarity, MonsterHexKind MonsterHex)> ResolveActRoll(RunState runState, HextechMayhemModifier modifier, int actIndex)
	{
		RunManager runManager = RunManager.Instance;
		NetGameType gameType = runManager.NetService.Type;
		bool isMultiplayer = gameType is NetGameType.Host or NetGameType.Client;

		HextechRarityTier localRarity = modifier.GetRarityForAct(actIndex)
			?? (isMultiplayer ? RollStableRarity(modifier, actIndex, runState) : RollRandomRarity(modifier, actIndex, runState));
		modifier.SetRarityForAct(actIndex, localRarity);

		MonsterHexKind localMonsterHex = modifier.GetMonsterHexForAct(actIndex)
			?? (isMultiplayer ? ChooseStableMonsterHexForAct(modifier, localRarity, runState, actIndex) : ChooseMonsterHexForAct(modifier, localRarity, runState));
		modifier.SetMonsterHexForAct(actIndex, localMonsterHex);

		if (gameType is NetGameType.Singleplayer or NetGameType.None or NetGameType.Replay)
		{
			return (localRarity, localMonsterHex);
		}

		PlayerChoiceSynchronizer? synchronizer = await WaitForPlayerChoiceSynchronizerAsync(runManager);
		Player? authorityPlayer = GetActRollAuthorityPlayer(runManager, runState);
		if (synchronizer == null || authorityPlayer == null)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] ResolveActRoll: falling back to local roll act={actIndex} rarity={localRarity} monsterHex={localMonsterHex} synchronizer={synchronizer != null} authority={authorityPlayer?.NetId}");
			return (localRarity, localMonsterHex);
		}

		uint choiceId = synchronizer.ReserveChoiceId(authorityPlayer);
		if (gameType == NetGameType.Host)
		{
			synchronizer.SyncLocalChoice(authorityPlayer, choiceId, CreateActRollChoiceResult(actIndex, localRarity, localMonsterHex));
			Log.Info($"[{ModInfo.Id}][Mayhem] ResolveActRoll host sync: act={actIndex} choiceId={choiceId} authority={authorityPlayer.NetId} rarity={localRarity} monsterHex={localMonsterHex}");
			return (localRarity, localMonsterHex);
		}

		(PlayerChoiceResult remoteChoice, uint receivedChoiceId) = await WaitForRemoteHextechChoice(
			synchronizer,
			authorityPlayer,
			choiceId,
			result => TryDecodeActRollChoiceResult(result, actIndex, out _, out _),
			$"act-roll act={actIndex}");
		if (!TryDecodeActRollChoiceResult(remoteChoice, actIndex, out HextechRarityTier syncedRarity, out MonsterHexKind syncedMonsterHex))
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] ResolveActRoll: malformed host payload act={actIndex}; using local rarity={localRarity} monsterHex={localMonsterHex}");
			return (localRarity, localMonsterHex);
		}

		modifier.SetRarityForAct(actIndex, syncedRarity);
		modifier.SetMonsterHexForAct(actIndex, syncedMonsterHex);
		Log.Info($"[{ModInfo.Id}][Mayhem] ResolveActRoll client sync: act={actIndex} choiceId={receivedChoiceId} authority={authorityPlayer.NetId} rarity={syncedRarity} monsterHex={syncedMonsterHex} localRarity={localRarity} localMonsterHex={localMonsterHex}");
		return (syncedRarity, syncedMonsterHex);
	}

	private static PlayerChoiceResult CreateActRollChoiceResult(int actIndex, HextechRarityTier rarity, MonsterHexKind monsterHex)
	{
		return PlayerChoiceResult.FromIndexes([ HextechChoiceMagic, ChoiceKindActRoll, actIndex, (int)rarity, (int)monsterHex ]);
	}

	private static bool TryDecodeActRollChoiceResult(PlayerChoiceResult result, int expectedActIndex, out HextechRarityTier rarity, out MonsterHexKind monsterHex)
	{
		rarity = default;
		monsterHex = default;
		if (!TryGetIndexPayload(result, out List<int>? payload)
			|| payload.Count < 5
			|| payload[0] != HextechChoiceMagic
			|| payload[1] != ChoiceKindActRoll
			|| payload[2] != expectedActIndex)
		{
			return false;
		}

		if (!Enum.IsDefined(typeof(HextechRarityTier), payload[3]) || !Enum.IsDefined(typeof(MonsterHexKind), payload[4]))
		{
			return false;
		}

		rarity = (HextechRarityTier)payload[3];
		monsterHex = (MonsterHexKind)payload[4];
		return true;
	}

	private static Player? GetActRollAuthorityPlayer(RunManager runManager, RunState runState)
	{
		if (runManager.NetService.Type == NetGameType.Host)
		{
			return runState.Players.FirstOrDefault(player => player.NetId == runManager.NetService.NetId);
		}

		return runState.Players.FirstOrDefault();
	}

	private static HextechRarityTier RollWeightedRarity(RunState runState, int silverWeight, int goldWeight, int prismaticWeight, bool deterministic, int actIndex)
	{
		int totalWeight = silverWeight + goldWeight + prismaticWeight;
		int roll = deterministic
			? HextechStableRandom.Index(runState, totalWeight, "act-roll-weighted-rarity", actIndex.ToString(), silverWeight.ToString(), goldWeight.ToString(), prismaticWeight.ToString())
			: runState.Rng.Niche.NextInt(totalWeight);
		if (roll < silverWeight)
		{
			return HextechRarityTier.Silver;
		}

		roll -= silverWeight;
		if (roll < goldWeight)
		{
			return HextechRarityTier.Gold;
		}

		return HextechRarityTier.Prismatic;
	}

	private static MonsterHexKind ChooseMonsterHexForAct(HextechMayhemModifier modifier, HextechRarityTier rarity, RunState runState)
	{
		HashSet<MonsterHexKind> alreadyChosen = [];
		for (int i = 0; i < 3; i++)
		{
			MonsterHexKind? kind = modifier.GetMonsterHexForAct(i);
			if (kind.HasValue)
			{
				alreadyChosen.Add(kind.Value);
			}
		}

		List<MonsterHexKind> pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity)
			.Where(kind => !alreadyChosen.Contains(kind))
			.ToList();
		if (pool.Count == 0)
		{
			pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity).ToList();
		}

		return pool[runState.Rng.Niche.NextInt(pool.Count)];
	}

	private static MonsterHexKind ChooseStableMonsterHexForAct(HextechMayhemModifier modifier, HextechRarityTier rarity, RunState runState, int actIndex)
	{
		List<MonsterHexKind> pool = BuildMonsterHexPoolForAct(modifier, rarity);
		return pool[HextechStableRandom.Index(
			runState,
			pool.Count,
			"act-roll-monster-hex",
			actIndex.ToString(),
			((int)rarity).ToString(),
			string.Join(",", pool.Select(static kind => ((int)kind).ToString()).OrderBy(static key => key, StringComparer.Ordinal)))];
	}

	private static List<MonsterHexKind> BuildMonsterHexPoolForAct(HextechMayhemModifier modifier, HextechRarityTier rarity)
	{
		HashSet<MonsterHexKind> alreadyChosen = [];
		for (int i = 0; i < 3; i++)
		{
			MonsterHexKind? kind = modifier.GetMonsterHexForAct(i);
			if (kind.HasValue)
			{
				alreadyChosen.Add(kind.Value);
			}
		}

		List<MonsterHexKind> pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity)
			.Where(kind => !alreadyChosen.Contains(kind))
			.ToList();
		if (pool.Count == 0)
		{
			pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity).ToList();
		}

		return pool;
	}

	private static MonsterHexKind? RerollEnemyHexForAct(
		HextechMayhemModifier modifier,
		HextechRarityTier rarity,
		RunState runState,
		int actIndex,
		MonsterHexKind? currentHex,
		int rerollOrdinal,
		IReadOnlySet<ModelId> excludedIconRelicIds)
	{
		List<MonsterHexKind> pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity)
			.Where(kind => kind != currentHex)
			.Where(kind => !IsMonsterHexChosenInOtherAct(modifier, actIndex, kind))
			.Where(kind => !excludedIconRelicIds.Contains(GetMonsterHexIconRelicId(kind)))
			.ToList();
		if (pool.Count == 0)
		{
			pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity)
				.Where(kind => kind != currentHex)
				.Where(kind => !IsMonsterHexChosenInOtherAct(modifier, actIndex, kind))
				.ToList();
		}
		if (pool.Count == 0)
		{
			pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity)
				.Where(kind => kind != currentHex)
				.ToList();
		}
		if (pool.Count == 0)
		{
			pool = MonsterHexCatalog.GetMonsterHexesForRarity(rarity).ToList();
		}
		if (pool.Count == 0)
		{
			return currentHex;
		}

		string poolKey = string.Join(",", pool.Select(static kind => ((int)kind).ToString()).OrderBy(static key => key, StringComparer.Ordinal));
		int index = HextechStableRandom.Index(
			runState,
			pool.Count,
			"enemy-hex-reroll",
			actIndex.ToString(),
			((int)rarity).ToString(),
			(currentHex.HasValue ? ((int)currentHex.Value).ToString() : "none"),
			rerollOrdinal.ToString(),
			poolKey);
		return pool[index];
	}

	private static bool IsMonsterHexChosenInOtherAct(HextechMayhemModifier modifier, int actIndex, MonsterHexKind hex)
	{
		for (int i = 0; i < 3; i++)
		{
			if (i != actIndex && modifier.GetMonsterHexForAct(i) == hex)
			{
				return true;
			}
		}

		return false;
	}

	private static ModelId GetMonsterHexIconRelicId(MonsterHexKind hex)
	{
		RelicModel relic = MonsterHexCatalog.GetIconRelicForMonsterHex(hex);
		return relic.CanonicalInstance?.Id ?? relic.Id;
	}
}
