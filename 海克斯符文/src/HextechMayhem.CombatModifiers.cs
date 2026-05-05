using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer?.Side != CombatSide.Enemy || dealer.CombatState?.RunState != RunState)
        {
            return 1m;
        }

        decimal multiplier = 1m;
        if (HasActiveMonsterHex(MonsterHexKind.HeavyHitter))
        {
            multiplier *= 1m + Math.Min(30m, Math.Floor(dealer.MaxHp / 10m)) / 100m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.BigStrength))
        {
            multiplier *= 1.2m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.GlassCannon))
        {
            multiplier *= 1.5m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.AstralBody))
        {
            multiplier *= 0.9m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.Goliath))
        {
            multiplier *= 1.2m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.DrawYourSword))
        {
            multiplier *= 1.4m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.HandOfBaron))
        {
            multiplier *= 1.1m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.Goldrend))
        {
            multiplier *= 1.1m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.GoldenSpatula))
        {
            multiplier *= 1.35m;
        }

        return multiplier;
    }

    public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target.Side != CombatSide.Enemy || target.CombatState?.RunState != RunState)
        {
            return 1m;
        }

        decimal multiplier = 1m;
        if (HasActiveMonsterHex(MonsterHexKind.Goliath))
        {
            multiplier *= 1.2m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.FirstAidKit))
        {
            multiplier *= 1.25m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.ProteinShake))
        {
            multiplier *= GetMonsterProteinShakeSustainMultiplier(target);
        }

        if (HasActiveMonsterHex(MonsterHexKind.GoldenSpatula))
        {
            multiplier *= 0.5m;
        }

        return multiplier;
    }

    public override decimal ModifyHandDraw(Player player, decimal count)
    {
        if (!HasActiveMonsterHex(MonsterHexKind.Loop)
            || player.Creature.CombatState?.RunState != RunState)
        {
            return count;
        }

        return Math.Max(0m, count - 1m);
    }

    public override bool TryModifyEnergyCostInCombat(CardModel card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (card.Owner?.Creature.Side != CombatSide.Player
            || card.Type != CardType.Attack
            || card.Pile?.Type != PileType.Hand
            || card.EnergyCost.CostsX
            || originalCost <= 0m
            || card.Owner.Creature.CombatState?.RunState != RunState)
        {
            return false;
        }

        int nextAttackIndex = GetPlayerAttacksPlayedThisTurn(card) + 1;
        decimal multiplier = 1m;
        if (HasActiveMonsterHex(MonsterHexKind.LightEmUp) && nextAttackIndex % 4 == 0)
        {
            multiplier *= 2m;
        }

        if (HasActiveMonsterHex(MonsterHexKind.TwiceThrice) && nextAttackIndex % 3 == 0)
        {
            multiplier *= 2m;
        }

        if (multiplier == 1m)
        {
            return false;
        }

        modifiedCost = originalCost * multiplier;
        return true;
    }

    public override (PileType, CardPilePosition) ModifyCardPlayResultPileTypeAndPosition(CardModel card, bool isAutoPlay, ResourceInfo resources, PileType pileType, CardPilePosition position)
    {
        return TryConsumeEnemyEightPennyGate(card, isAutoPlay)
            ? (PileType.Exhaust, position)
            : (pileType, position);
    }
}
