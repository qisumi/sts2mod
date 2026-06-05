using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class OmegaRune : HextechRelicBase
{
	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("StartTurn", 4m),
		new DynamicVar("Damage", 50m)
	];

	public override async Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (Owner == null
			|| Owner.Creature.IsDead
			|| side != Owner.Creature.Side
			|| Owner.Creature.CombatState == null
			|| Owner.Creature.CombatState.RoundNumber < DynamicVars["StartTurn"].IntValue)
		{
			return;
		}

		List<Creature> enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
		if (enemies.Count == 0)
		{
			return;
		}

		Flash(enemies);
		await CreatureCmd.Damage(
			choiceContext,
			enemies,
			DynamicVars["Damage"].BaseValue,
			ValueProp.Unpowered,
			Owner.Creature,
			null);
	}
}
