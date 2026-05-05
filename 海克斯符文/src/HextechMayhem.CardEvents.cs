using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (TrackPlayerAttackCardPlayedThisTurn(cardPlay)
			&& cardPlay.Card.Owner?.Creature.CombatState is HextechCombatState combatState)
		{
			RefreshPlayerAttackCostDoublingPreviews(GetAlivePlayerSideCreatures(combatState));
		}

		if (!HasActiveMonsterHex(MonsterHexKind.MasterOfDuality)
			|| cardPlay.Card.Owner?.Creature.Side != CombatSide.Player)
		{
			return;
		}

		Creature playerCreature = cardPlay.Card.Owner.Creature;
		if (!playerCreature.IsAlive)
		{
			return;
		}

		if (cardPlay.Card.Type == CardType.Skill)
		{
			await PowerCmd.Apply<HextechTemporaryStrengthLossPower>(playerCreature, 1m, playerCreature, cardPlay.Card);
		}
		else if (cardPlay.Card.Type == CardType.Attack)
		{
			await PowerCmd.Apply<HextechTemporaryDexterityLossPower>(playerCreature, 1m, playerCreature, cardPlay.Card);
		}
	}

	public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
	{
		if (!HasActiveMonsterHex(MonsterHexKind.WarmogsSpirit)
			|| card.Owner?.Creature.Side != CombatSide.Player
			|| card.Owner.Creature.CombatState?.RunState != RunState)
		{
			return;
		}

		if (IsNetworkMultiplayer())
		{
			return;
		}

		Player owner = card.Owner;
		ulong playerId = owner.NetId;
		int cardsDrawn = _combatTracking.PlayerCardsDrawnThisCombat.GetValueOrDefault(playerId, 0) + 1;
		_combatTracking.PlayerCardsDrawnThisCombat[playerId] = cardsDrawn;
		if (cardsDrawn % 8 != 0)
		{
			return;
		}

		HextechCombatState combatState = owner.Creature.CombatState;
		foreach (Creature enemy in GetAliveEnemies(combatState))
		{
			await HextechEnemyPowerScalingHooks.Apply<PlatingPower>(enemy, 1m, enemy, null);
		}
	}

	public override async Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (IsNetworkMultiplayer() && cardPlay.Card.Owner?.Creature.CombatState is HextechCombatState combatStateForWarmogs)
		{
			await ResolveWarmogsSpiritDrawProgressFromHistory(combatStateForWarmogs);
		}

		Player? owner = cardPlay.Card.Owner;
		if (owner == null
			|| cardPlay.Card.Type != CardType.Power
			|| owner.Creature.CombatState?.RunState != RunState
			|| owner.Creature.GetPower<StormPower>() is not StormPower stormPower)
		{
			return;
		}

		int lightningCount = Math.Max(0, (int)Math.Floor((decimal)stormPower.Amount));
		for (int i = 0; i < lightningCount; i++)
		{
			OrbModel orb = ModelDb.Orb<LightningOrb>().ToMutable();
			await OrbCmd.Channel(new BlockingPlayerChoiceContext(), orb, owner);
		}
	}

	public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)
	{
		if (IsNetworkMultiplayer() && player.Creature.CombatState is HextechCombatState combatState)
		{
			await ResolveWarmogsSpiritDrawProgressFromHistory(combatState);
		}
	}

#if !STS2_104_OR_NEWER
	public override async Task BeforePlayPhaseStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (IsNetworkMultiplayer() && player.Creature.CombatState is HextechCombatState combatState)
		{
			await ResolveWarmogsSpiritDrawProgressFromHistory(combatState);
		}
	}
#endif

	private async Task ResolveWarmogsSpiritDrawProgressFromHistory(HextechCombatState combatState)
	{
		if (!HasActiveMonsterHex(MonsterHexKind.WarmogsSpirit)
			|| combatState.RunState != RunState)
		{
			return;
		}

		int pendingPlating = 0;
		foreach (Player player in combatState.Players.OrderBy(static player => player.NetId))
		{
			int drawnCards = CountPlayerDrawnCardsFromHistory(player);
			int previousDrawnCards = _combatTracking.PlayerCardsDrawnThisCombat.GetValueOrDefault(player.NetId, 0);
			if (drawnCards <= previousDrawnCards)
			{
				continue;
			}

			pendingPlating += drawnCards / 8 - previousDrawnCards / 8;
			_combatTracking.PlayerCardsDrawnThisCombat[player.NetId] = drawnCards;
		}

		if (pendingPlating <= 0)
		{
			return;
		}

		foreach (Creature enemy in GetAliveEnemies(combatState))
		{
			await HextechEnemyPowerScalingHooks.Apply<PlatingPower>(enemy, pendingPlating, enemy, null);
		}
	}

	private static int CountPlayerDrawnCardsFromHistory(Player player)
	{
		return CombatManager.Instance.History.Entries
			.OfType<CardDrawnEntry>()
			.Count(entry => entry.Card.Owner?.NetId == player.NetId);
	}

	private static bool IsNetworkMultiplayer()
	{
		return RunManager.Instance.NetService.Type is NetGameType.Host or NetGameType.Client;
	}
}
