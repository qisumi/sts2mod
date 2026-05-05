using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
    private async Task ApplyCombatStartEnemyHexes(CombatRoom room)
    {
        await ApplyMonsterCombatStartHexes(room);
        await ApplyCombatStartPlayerDebuffHexes(room);
    }

    private async Task ApplyMonsterCombatStartHexes(CombatRoom room)
    {
        IReadOnlyList<Creature> enemies = GetAliveEnemies(room.CombatState);
        if (enemies.Count == 0)
        {
            return;
        }

        enemies = enemies
            .Where(static enemy => !ShouldDeferInitialBossStartHexes(enemy))
            .ToList();
        if (enemies.Count == 0)
        {
            return;
        }

        if (HasActiveMonsterHex(MonsterHexKind.StartupRoutine))
        {
            foreach (Creature enemy in enemies)
            {
                await ApplyMonsterCombatStartHexesToEnemy(enemy, room, applyStartupRoutine: true, applyHailToTheKing: false);
            }
        }

        if (!HasActiveMonsterHex(MonsterHexKind.HailToTheKing)
            || room.RoomType is not (RoomType.Elite or RoomType.Boss))
        {
            return;
        }

        foreach (Creature enemy in enemies)
        {
            await ApplyMonsterCombatStartHexesToEnemy(enemy, room, applyStartupRoutine: false, applyHailToTheKing: true);
        }
    }

    private async Task ApplyMonsterCombatStartHexesToEnemy(
        Creature enemy,
        CombatRoom room,
        bool applyStartupRoutine = true,
        bool applyHailToTheKing = true)
    {
        if (applyStartupRoutine && HasActiveMonsterHex(MonsterHexKind.StartupRoutine))
        {
            await CreatureCmd.GainBlock(enemy, 15m, ValueProp.Unpowered, null);
        }

        if (!applyHailToTheKing
            || !HasActiveMonsterHex(MonsterHexKind.HailToTheKing)
            || room.RoomType is not (RoomType.Elite or RoomType.Boss))
        {
            return;
        }

        int sustain = Math.Max(1, (int)Math.Floor(enemy.MaxHp * 0.05m));
        await HextechEnemyPowerScalingHooks.Apply<ArtifactPower>(enemy, 3m, enemy, null);
        await HextechEnemyPowerScalingHooks.Apply<PlatingPower>(enemy, sustain, enemy, null);
        await HextechEnemyPowerScalingHooks.Apply<RegenPower>(enemy, sustain, enemy, null);
    }

    private async Task ApplyCombatStartPlayerDebuffHexes(CombatRoom room)
    {
        IReadOnlyList<Creature> players = GetAlivePlayerSideCreatures(room.CombatState);
        if (players.Count == 0)
        {
            return;
        }

        if (HasActiveMonsterHex(MonsterHexKind.Queen))
        {
            await RunGroupedPlayerDebuffBurst(async () =>
            {
                await PowerCmd.Apply<ChainsOfBindingPower>(players, 1m, null, null);
            });
        }

        if (HasActiveMonsterHex(MonsterHexKind.HandOfBaron))
        {
            await RunGroupedPlayerDebuffBurst(async () =>
            {
                await PowerCmd.Apply<ShrinkPower>(players, 99m, null, null);
            });
        }
    }
}
