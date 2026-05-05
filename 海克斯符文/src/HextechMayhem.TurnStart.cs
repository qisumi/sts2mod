using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	private async Task BeforePlayerSideTurnStart(HextechCombatState combatState, IReadOnlyList<Creature> players)
	{
		_combatTracking.PreparePlayerSideTurnStart();
		RefreshPlayerAttackCostDoublingPreviews(players);

		await ApplyToCurrentEnemiesIfNeeded();
	    QueueEscapePlanTriggersFromCurrentEnemyState(combatState);
        await ResolvePlayerTurnPendingEnemyEffects(combatState);
        await ApplyPlayerTurnStartEnemyHexes(combatState, players);
    }

    private void QueueEscapePlanTriggersFromCurrentEnemyState(HextechCombatState combatState)
    {
        if (!HasActiveMonsterHex(MonsterHexKind.EscapePlan))
        {
            return;
        }

        foreach (Creature creature in GetAliveEnemies(combatState))
        {
            if (creature.CombatId == null
                || _combatTracking.EscapePlanTriggered.Contains(creature.CombatId.Value)
                || _combatTracking.EscapePlanPending.Contains(creature.CombatId.Value)
                || creature.CurrentHp >= creature.MaxHp * EscapePlanHealthThresholdPercent)
            {
                continue;
            }

            uint combatId = creature.CombatId.Value;
            _combatTracking.EscapePlanTriggered.Add(combatId);
            _combatTracking.EscapePlanPending.Add(combatId);
        }
    }

    private async Task ResolvePlayerTurnPendingEnemyEffects(HextechCombatState combatState)
    {
        if (_combatTracking.EscapePlanPending.Count > 0)
        {
            foreach (uint combatId in _combatTracking.EscapePlanPending.ToList())
            {
                Creature? creature = combatState.GetCreature(combatId);
                _combatTracking.EscapePlanPending.Remove(combatId);
                if (creature == null || !creature.IsAlive)
                {
                    continue;
                }

                int blockAmount = (int)Math.Floor(creature.MaxHp * EscapePlanBlockPercent);
                if (blockAmount > 0)
                {
                    await CreatureCmd.GainBlock(creature, blockAmount, ValueProp.Unpowered, null);
                }

                await PowerCmd.Apply<ShrinkPower>(creature, 1m, creature, null);
            }
        }

        if (_combatTracking.SpeedDemonPending.Count > 0)
        {
            foreach (uint combatId in _combatTracking.SpeedDemonPending.ToList())
            {
                Creature? creature = combatState.GetCreature(combatId);
                _combatTracking.SpeedDemonPending.Remove(combatId);
                if (creature == null || !creature.IsAlive)
                {
                    continue;
                }

                int blockAmount = Math.Max(1, (int)Math.Floor(creature.MaxHp * 0.1m));
                await CreatureCmd.GainBlock(creature, blockAmount, ValueProp.Unpowered, null);
            }
        }

        if (_combatTracking.FeyMagicPendingNoDrawPlayers.Count > 0)
        {
            foreach (KeyValuePair<uint, uint> pending in _combatTracking.FeyMagicPendingNoDrawPlayers.ToList())
            {
                uint combatId = pending.Key;
                Creature? creature = combatState.GetCreature(combatId);
                Creature? source = combatState.GetCreature(pending.Value);
                _combatTracking.FeyMagicPendingNoDrawPlayers.Remove(combatId);
                if (creature == null || !creature.IsAlive || creature.Side != CombatSide.Player)
                {
                    continue;
                }

                await PowerCmd.Apply<NoDrawPower>(creature, 1m, source, null);
            }
        }

        if (_combatTracking.RepulsorPending.Count > 0)
        {
            foreach (uint combatId in _combatTracking.RepulsorPending.ToList())
            {
                Creature? creature = combatState.GetCreature(combatId);
                _combatTracking.RepulsorPending.Remove(combatId);
                if (creature == null || !creature.IsAlive)
                {
                    continue;
                }

                await HextechEnemyPowerScalingHooks.Apply<SlipperyPower>(creature, RepulsorSlipperyStacks, creature, null);
            }
        }
    }

    private async Task ApplyPlayerTurnStartEnemyHexes(HextechCombatState combatState, IReadOnlyList<Creature> players)
    {
        if (HasActiveMonsterHex(MonsterHexKind.MountainSoul))
        {
            foreach (Creature enemy in GetAliveEnemies(combatState))
            {
                if (enemy.CombatId == null)
                {
                    continue;
                }

                uint combatId = enemy.CombatId.Value;
                if (_combatTracking.MountainSoulHasPreviousTurn.Contains(combatId)
                    && !_combatTracking.MountainSoulDamagedSinceLastTurn.Contains(combatId))
                {
                    int block = Math.Max(1, (int)Math.Floor(enemy.MaxHp * 0.1m));
                    await CreatureCmd.GainBlock(enemy, block, ValueProp.Unpowered, null);
                }

                _combatTracking.MountainSoulHasPreviousTurn.Add(combatId);
                _combatTracking.MountainSoulDamagedSinceLastTurn.Remove(combatId);
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.Sonata)
            && combatState.RoundNumber % 2 == 1)
        {
            foreach (Creature enemy in GetAliveEnemies(combatState))
            {
                int block = Math.Max(1, (int)Math.Floor(enemy.MaxHp * 0.1m));
                await CreatureCmd.GainBlock(enemy, block, ValueProp.Unpowered, null);
            }
        }

        IReadOnlyList<Creature> aliveEnemies = GetAliveEnemies(combatState);
        if (HasActiveMonsterHex(MonsterHexKind.ShrinkEngine))
        {
            foreach (Creature enemy in aliveEnemies)
            {
                if (enemy.GetPowerAmount<SlipperyPower>() <= 0m)
                {
                    await HextechEnemyPowerScalingHooks.Apply<SlipperyPower>(enemy, ShrinkEngineSlipperyStacks, enemy, null);
                }
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.DivineIntervention)
            && combatState.RoundNumber > 1
            && combatState.RoundNumber % 4 == 0
            && aliveEnemies.Count > 0)
        {
            await PowerCmd.Apply<IntangiblePower>(aliveEnemies, 1m, null, null);
        }

        if (HasActiveMonsterHex(MonsterHexKind.FrostWraith)
            && combatState.RoundNumber > 1
            && combatState.RoundNumber % 4 == 0
            && players.Count > 0)
        {
#if STS2_104_OR_NEWER
            await RunGroupedPlayerDebuffBurst(async () =>
            {
                await PowerCmd.Apply<BorrowedTimePower>(players, 1m, null, null);
            });
#endif
        }

        if (HasActiveMonsterHex(MonsterHexKind.SingularityAI) && players.Count > 0)
        {
            await AddEnemySingularityAIStatusCards(combatState, players);
        }
    }

	private async Task BeforeEnemySideTurnStart(HextechCombatState combatState, IReadOnlyList<Creature> players)
	{
	    _combatTracking.PrepareEnemySideTurnStart();
		RefreshPlayerAttackCostDoublingPreviews(players);

	    IReadOnlyList<Creature> enemies = GetAliveEnemies(combatState);

        if (HasActiveMonsterHex(MonsterHexKind.TankEngine))
        {
            foreach (Creature enemy in enemies)
            {
                int hpGain = Math.Min(5, Math.Max(1, (int)Math.Floor(enemy.MaxHp * 0.05m)));
                await CreatureCmd.GainMaxHp(enemy, hpGain);
                if (enemy.CombatId != null)
                {
                    uint combatId = enemy.CombatId.Value;
                    _combatTracking.TankEngineStacks[combatId] = _combatTracking.TankEngineStacks.GetValueOrDefault(combatId, 0) + 1;
                    UpdateEnemyScale(enemy);
                }
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.ShrinkEngine))
        {
            foreach (Creature enemy in enemies)
            {
                if (enemy.CombatId != null)
                {
                    uint combatId = enemy.CombatId.Value;
                    _combatTracking.ShrinkEngineStacks[combatId] = _combatTracking.ShrinkEngineStacks.GetValueOrDefault(combatId, 0) + 1;
                    UpdateEnemyScale(enemy);
                }
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.Sturdy))
        {
            foreach (Creature enemy in enemies)
            {
                decimal percent = enemy.CurrentHp * 2 < enemy.MaxHp ? 0.04m : 0.02m;
                int heal = Math.Min(10, Math.Max(1, (int)Math.Floor(enemy.MaxHp * percent)));
                if (heal > 0)
                {
                    await CreatureCmd.Heal(enemy, heal);
                }
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.UnmovableMountain))
        {
            foreach (Creature enemy in enemies)
            {
                if (enemy.Block <= 0)
                {
                    int block = Math.Max(1, (int)Math.Floor(enemy.MaxHp * 0.08m));
                    await CreatureCmd.GainBlock(enemy, block, ValueProp.Unpowered, null);
                }
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.Sonata))
        {
            if (combatState.RoundNumber % 2 == 0)
            {
                foreach (Creature enemy in enemies)
                {
                    int heal = Math.Max(1, (int)Math.Floor(enemy.MaxHp * 0.05m));
                    await CreatureCmd.Heal(enemy, heal);
                }
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.Doomsday) && players.Count > 0)
        {
            await RunGroupedPlayerDebuffBurst(async () =>
            {
                await PowerCmd.Apply<DisintegrationPower>(players, 2m, null, null);
            });
        }

        if (HasActiveMonsterHex(MonsterHexKind.ProtectiveVeil)
            && _combatTracking.EnemyProtectiveVeilTurnCounter % 2 == 0)
        {
            foreach (Creature enemy in enemies)
            {
                await HextechEnemyPowerScalingHooks.Apply<ArtifactPower>(enemy, 1m, enemy, null);
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.HandOfBaron) && players.Count > 0)
        {
            await RunGroupedPlayerDebuffBurst(async () =>
            {
                await PowerCmd.Apply<ShrinkPower>(players, 2m, null, null);
            });
        }

        if (HasActiveMonsterHex(MonsterHexKind.Queen)
            && combatState.RoundNumber > 1
            && combatState.RoundNumber % 2 == 0)
        {
            IReadOnlyList<Creature> queenTargets = players
                .Where(player => player.GetPowerAmount<ChainsOfBindingPower>() < 3m)
                .ToList();
            if (queenTargets.Count > 0)
            {
                await RunGroupedPlayerDebuffBurst(async () =>
                {
                    await PowerCmd.Apply<ChainsOfBindingPower>(queenTargets, 1m, null, null);
                });
            }
        }

        if (HasActiveMonsterHex(MonsterHexKind.FeelTheBurn) && _combatTracking.FeelTheBurnPending.Count > 0 && players.Count > 0)
        {
            foreach (uint combatId in _combatTracking.FeelTheBurnPending.ToList())
            {
                Creature? creature = combatState.GetCreature(combatId);
                _combatTracking.FeelTheBurnPending.Remove(combatId);
                if (creature == null || !creature.IsAlive)
                {
                    continue;
                }

                await RunGroupedPlayerDebuffBurst(async () =>
                {
                    await PowerCmd.Apply<WeakPower>(players, 1m, creature, null);
                    await PowerCmd.Apply<VulnerablePower>(players, 1m, creature, null);
                    await PowerCmd.Apply<HextechBurnPower>(players, 5m, creature, null);
                });
            }
        }
    }
}
