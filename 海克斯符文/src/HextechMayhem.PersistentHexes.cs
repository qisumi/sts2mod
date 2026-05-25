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
		HextechEnemyHexContext context = new(this);
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this).OrderBy(static effect => effect.PersistentOrder))
		{
			await effect.ApplyPersistentToEnemy(context, creature, maxHpBaseOverride, replayOneShotPowers);
		}
	}

	internal static async Task EnsureMonsterMaxHpBonus(Creature creature, decimal bonusPercent, int? baseMaxHpOverride = null)
	{
		int baseMaxHp = baseMaxHpOverride ?? creature.MonsterMaxHpBeforeModification ?? creature.MaxHp;
		int expectedMaxHp = baseMaxHp + (int)Math.Floor(baseMaxHp * bonusPercent);
		int missingMaxHp = expectedMaxHp - creature.MaxHp;
		if (missingMaxHp > 0)
		{
			await GainMonsterMaxHpWithoutHeal(creature, missingMaxHp);
		}
	}

	internal static async Task GainMonsterMaxHpWithoutHeal(Creature creature, int amount)
	{
		if (amount <= 0)
		{
			return;
		}

		int oldMaxHp = creature.MaxHp;
		int oldCurrentHp = creature.CurrentHp;
		await CreatureCmdCompat.SetMaxHp(creature, oldMaxHp + amount);

		int actualMaxHpGain = Math.Max(0, creature.MaxHp - oldMaxHp);
		if (actualMaxHpGain <= 0)
		{
			return;
		}

		int newCurrentHp = Math.Min(creature.MaxHp, oldCurrentHp + actualMaxHpGain);
		if (newCurrentHp != creature.CurrentHp)
		{
			await CreatureCmd.SetCurrentHp(creature, newCurrentHp);
		}
	}

	internal void UpdateEnemyScale(Creature creature)
	{
		float baseScale = HasActiveMonsterHex(MonsterHexKind.Goliath) ? 1.35f : 1f;
		int tankStacks = creature.CombatId == null ? 0 : _combatTracking.TankEngineStacks.GetValueOrDefault(creature.CombatId.Value, 0);
		int shrinkStacks = creature.CombatId == null ? 0 : _combatTracking.ShrinkEngineStacks.GetValueOrDefault(creature.CombatId.Value, 0);
		float finalScale = Math.Max(0.2f, baseScale + tankStacks * 0.05f - shrinkStacks * 0.02f);
		NCombatRoom.Instance?.GetCreatureNode(creature)?.SetDefaultScaleTo(finalScale, 0f);
	}
}
