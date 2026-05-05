using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override async Task BeforeCombatStart()
	{
		HextechGoldrendSync.ResetCombat();
		ResetCombatTracking();
		HextechEnemyUi.Refresh(this);
		await ApplyToCurrentEnemiesIfNeeded();

		if (RunState.CurrentRoom is CombatRoom combatRoom)
		{
			await ApplyCombatStartEnemyHexes(combatRoom);
		}

		_combatTracking.EnemyProtectiveVeilTurnCounter = 0;
	}

	public override async Task AfterCombatEnd(CombatRoom room)
	{
		await HextechGoldrendSync.ApplyPendingCombatGoldLosses(RunState);
		ResetCombatTracking();
	}

	public override Task AfterCombatVictory(CombatRoom room)
	{
		return Task.CompletedTask;
	}

	public override async Task AfterCreatureAddedToCombat(Creature creature)
	{
		if (creature.Side != CombatSide.Enemy || !creature.IsAlive)
		{
			return;
		}

		if (RunState.CurrentRoom is CombatRoom combatRoom
			&& await TryApplyDeferredBossStartHexes(creature, combatRoom))
		{
			HextechEnemyUi.Refresh(this);
			return;
		}

		if (ShouldDeferInitialBossStartHexes(creature))
		{
			HextechEnemyUi.Refresh(this);
			return;
		}

		await ApplyPersistentMonsterHexes(creature);
		await TryApplyServantMasterIllusion(creature, creature, null);
		HextechEnemyUi.Refresh(this);
	}

	public async Task ApplyToCurrentEnemiesIfNeeded()
	{
		if (RunState.CurrentRoom is not CombatRoom combatRoom)
		{
			return;
		}

		foreach (Creature enemy in combatRoom.CombatState.Enemies.Where(static creature => creature.IsAlive))
		{
			if (ShouldDeferInitialBossStartHexes(enemy))
			{
				continue;
			}

			await ApplyPersistentMonsterHexes(enemy);
		}

		HextechEnemyUi.Refresh(this);
	}

	public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		await ApplyDeferredBossStartHexes(combatState);
		await NormalizeEnemyPainfulStabsPowers(combatState);

		IReadOnlyList<Creature> players = GetAlivePlayerSideCreatures(combatState);

		if (side == CombatSide.Player)
		{
			await BeforePlayerSideTurnStart(combatState, players);
			return;
		}

		if (side == CombatSide.Enemy)
		{
			await BeforeEnemySideTurnStart(combatState, players);
		}
	}
}
