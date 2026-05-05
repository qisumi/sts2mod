using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Rooms;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	private const int DoormakerShellMaxHpThreshold = 1_000_000;
	private static readonly FieldInfo DoormakerIsPortalOpenField = RequireField(typeof(Doormaker), "_isPortalOpen");
	private static readonly FieldInfo TestSubjectRespawnsField = RequireField(typeof(TestSubject), "_respawns");

	public override async Task AfterOstyRevived(Creature osty)
	{
		if (osty.Side != CombatSide.Enemy
			|| !osty.IsAlive
			|| osty.CombatState?.RunState != RunState
			|| osty.Monster is not TestSubject testSubject
			|| osty.CombatId == null
			|| RunState.CurrentRoom is not CombatRoom room)
		{
			return;
		}

		int respawns = GetTestSubjectRespawns(testSubject);
		if (respawns <= 0)
		{
			return;
		}

		uint combatId = osty.CombatId.Value;
		int lastAppliedPhase = _combatTracking.TestSubjectPhaseStartApplied.GetValueOrDefault(combatId, 0);
		if (lastAppliedPhase >= respawns)
		{
			return;
		}

		_combatTracking.TestSubjectPhaseStartApplied[combatId] = respawns;
		Log.Info($"[{ModInfo.Id}][Mayhem] Reapplying boss start hexes after TestSubject revive: combatId={combatId} respawns={respawns}");
		await ApplyBossStartHexesToEnemy(osty, room);
		HextechEnemyUi.Refresh(this);
	}

	private async Task ApplyDeferredBossStartHexes(HextechCombatState combatState)
	{
		if (RunState.CurrentRoom is not CombatRoom room)
		{
			return;
		}

		foreach (Creature enemy in GetAliveEnemies(combatState))
		{
			await TryApplyDeferredBossStartHexes(enemy, room);
		}
	}

	private async Task<bool> TryApplyDeferredBossStartHexes(Creature creature, CombatRoom room)
	{
		if (creature.Side != CombatSide.Enemy
			|| !creature.IsAlive
			|| creature.CombatState?.RunState != RunState
			|| !IsDoormakerReadyForDeferredStart(creature)
			|| creature.CombatId == null)
		{
			return false;
		}

		uint combatId = creature.CombatId.Value;
		if (!_combatTracking.DoormakerRealStartApplied.Add(combatId))
		{
			return false;
		}

		Log.Info($"[{ModInfo.Id}][Mayhem] Applying deferred boss start hexes to Doormaker: combatId={combatId} maxHp={creature.MaxHp}");
		await ApplyBossStartHexesToEnemy(creature, room);
		return true;
	}

	private async Task ApplyBossStartHexesToEnemy(Creature creature, CombatRoom room)
	{
		await ApplyPersistentMonsterHexes(creature, replayOneShotPowers: true);
		await ApplyMonsterCombatStartHexesToEnemy(creature, room);
	}

	private static bool ShouldDeferInitialBossStartHexes(Creature creature)
	{
		return IsDoormakerShell(creature);
	}

	private static bool IsDoormakerShell(Creature creature)
	{
		return creature.Monster is Doormaker doormaker
			&& (!IsDoormakerPortalOpen(doormaker)
				|| creature.ShowsInfiniteHp
				|| creature.MaxHp >= DoormakerShellMaxHpThreshold);
	}

	private static bool IsDoormakerReadyForDeferredStart(Creature creature)
	{
		return creature.Monster is Doormaker doormaker
			&& IsDoormakerPortalOpen(doormaker)
			&& !creature.ShowsInfiniteHp
			&& creature.MaxHp < DoormakerShellMaxHpThreshold;
	}

	private static bool IsDoormakerPortalOpen(Doormaker doormaker)
	{
		return DoormakerIsPortalOpenField.GetValue(doormaker) is true;
	}

	private static int GetTestSubjectRespawns(TestSubject testSubject)
	{
		return TestSubjectRespawnsField.GetValue(testSubject) is int respawns ? Math.Max(0, respawns) : 0;
	}
}
