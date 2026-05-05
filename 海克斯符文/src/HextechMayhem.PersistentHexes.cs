using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	private async Task ApplyPersistentMonsterHexes(Creature creature, bool replayOneShotPowers = false)
	{
		int? maxHpBaseOverride = replayOneShotPowers ? creature.MaxHp : null;

		if (HasActiveMonsterHex(MonsterHexKind.Goliath)
			&& TryMarkPersistentHexApplied(_combatTracking.GoliathApplied, creature, replayOneShotPowers))
		{
			await EnsureMonsterMaxHpBonus(creature, 0.3m, maxHpBaseOverride);
			UpdateEnemyScale(creature);
		}

		if (HasActiveMonsterHex(MonsterHexKind.AstralBody)
			&& TryMarkPersistentHexApplied(_combatTracking.AstralBodyApplied, creature, replayOneShotPowers))
		{
			await EnsureMonsterMaxHpBonus(creature, 0.3m, maxHpBaseOverride);
		}

		if (HasActiveMonsterHex(MonsterHexKind.GoldenSpatula)
			&& TryMarkPersistentHexApplied(_combatTracking.GoldenSpatulaApplied, creature, replayOneShotPowers))
		{
			await EnsureMonsterMaxHpBonus(creature, 0.35m, maxHpBaseOverride);
		}

		if (HasActiveMonsterHex(MonsterHexKind.MadScientist)
			&& creature.CombatId != null
			&& TryMarkPersistentHexApplied(_combatTracking.MadScientistApplied, creature, replayOneShotPowers))
		{
			int maxHpLoss = Math.Max(1, (int)Math.Floor(creature.MaxHp * 0.2m));
			int newMaxHp = Math.Max(1, creature.MaxHp - maxHpLoss);
			if (newMaxHp < creature.MaxHp)
			{
				await CreatureCmdCompat.SetMaxHp(creature, newMaxHp);
			}
		}

		if (HasActiveMonsterHex(MonsterHexKind.DrawYourSword)
			&& TryMarkPersistentHexApplied(_combatTracking.DrawYourSwordApplied, creature, replayOneShotPowers))
		{
			await PowerCmd.Apply<ImbalancedPower>(creature, 1m, creature, null);
		}

		if (HasActiveMonsterHex(MonsterHexKind.ProtectiveVeil)
			&& TryMarkPersistentHexApplied(_combatTracking.ProtectiveVeilApplied, creature, replayOneShotPowers))
		{
			await HextechEnemyPowerScalingHooks.Apply<ArtifactPower>(creature, ProtectiveVeilInitialArtifactStacks, creature, null);
		}

		if (HasActiveMonsterHex(MonsterHexKind.Thornmail)
			&& TryMarkPersistentHexApplied(_combatTracking.ThornmailApplied, creature, replayOneShotPowers))
		{
			await HextechEnemyPowerScalingHooks.Apply<ReflectPower>(creature, 5m, creature, null);
		}

		if (HasActiveMonsterHex(MonsterHexKind.SuperBrain)
			&& TryMarkPersistentHexApplied(_combatTracking.SuperBrainApplied, creature, replayOneShotPowers))
		{
			int plating = (int)Math.Floor(creature.MaxHp * 0.04m);
			if (plating > 0)
			{
				await HextechEnemyPowerScalingHooks.Apply<PlatingPower>(creature, plating, creature, null);
			}
		}

		if (HasActiveMonsterHex(MonsterHexKind.GlassCannon))
		{
			int hpCap = Math.Max(1, (int)Math.Floor(creature.MaxHp * 0.7m));
			if (creature.CurrentHp > hpCap)
			{
				await CreatureCmd.SetCurrentHp(creature, hpCap);
			}
		}

		if (HasActiveMonsterHex(MonsterHexKind.UnmovableMountain)
			&& TryMarkPersistentHexApplied(_combatTracking.UnmovableMountainApplied, creature, replayOneShotPowers))
		{
			await PowerCmd.Apply<BarricadePower>(creature, 1m, creature, null);
		}

		if (HasActiveMonsterHex(MonsterHexKind.ImmortalBone)
			&& creature.GetPowerAmount<HardenedShellPower>() <= 0m)
		{
			int shell = Math.Max(12, (int)Math.Floor(creature.MaxHp * 0.6m));
			await HextechEnemyPowerScalingHooks.Apply<HardenedShellPower>(creature, shell, creature, null);
		}

		await TryApplyServantMasterIllusion(creature, creature, null);
	}

	private static async Task EnsureMonsterMaxHpBonus(Creature creature, decimal bonusPercent, int? baseMaxHpOverride = null)
	{
		int baseMaxHp = baseMaxHpOverride ?? creature.MonsterMaxHpBeforeModification ?? creature.MaxHp;
		int expectedMaxHp = baseMaxHp + (int)Math.Floor(baseMaxHp * bonusPercent);
		int missingMaxHp = expectedMaxHp - creature.MaxHp;
		if (missingMaxHp > 0)
		{
			await CreatureCmd.GainMaxHp(creature, missingMaxHp);
		}
	}

	private void UpdateEnemyScale(Creature creature)
	{
		float baseScale = HasActiveMonsterHex(MonsterHexKind.Goliath) ? 1.35f : 1f;
		int tankStacks = creature.CombatId == null ? 0 : _combatTracking.TankEngineStacks.GetValueOrDefault(creature.CombatId.Value, 0);
		int shrinkStacks = creature.CombatId == null ? 0 : _combatTracking.ShrinkEngineStacks.GetValueOrDefault(creature.CombatId.Value, 0);
		float finalScale = Math.Max(0.2f, baseScale + tankStacks * 0.05f - shrinkStacks * 0.02f);
		NCombatRoom.Instance?.GetCreatureNode(creature)?.SetDefaultScaleTo(finalScale, 0f);
	}
}
