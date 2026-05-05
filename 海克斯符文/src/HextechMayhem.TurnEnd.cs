using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
    public override async Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        CombatRoom? combatRoom = RunState.CurrentRoom as CombatRoom;

        if (side == CombatSide.Player
            && combatRoom != null
            && IsNetworkMultiplayer())
        {
            await ResolveWarmogsSpiritDrawProgressFromHistory(combatRoom.CombatState);
        }

        if (side == CombatSide.Player)
        {
            _combatTracking.PreparePlayerSideTurnEnd();
            if (combatRoom != null)
            {
                RefreshPlayerAttackCostDoublingPreviews(GetAlivePlayerSideCreatures(combatRoom.CombatState));
            }
        }

        if (side != CombatSide.Player
            || !HasActiveMonsterHex(MonsterHexKind.HastyScribble)
            || combatRoom == null)
        {
            return;
        }

        foreach (Creature playerCreature in GetAlivePlayerSideCreatures(combatRoom.CombatState))
        {
            Player? player = playerCreature.Player;
            if (player == null)
            {
                continue;
            }

            int handCount = PileType.Hand.GetPile(player).Cards.Count;
            if (handCount > 0)
            {
                await CreatureCmd.Damage(choiceContext, playerCreature, handCount, ValueProp.Unpowered, null, null);
            }
        }
    }
}
