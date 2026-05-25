using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class CorpseExplosionRune : HextechRelicBase
{
	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PoisonPower>()
	];

	public override bool IsAvailableForPlayer(Player player)
	{
		return IsSilentPlayer(player);
	}

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (Owner == null
			|| Owner.Creature.IsDead
			|| target.Side != CombatSide.Enemy
			|| !result.WasTargetKilled
			|| !IsPoisonDamage(target, props, dealer, cardSource)
			|| target.CombatState is not HextechCombatState combatState)
		{
			return;
		}

		List<Creature> enemies = combatState.HittableEnemies
			.Where(enemy => enemy != target && enemy.IsAlive)
			.ToList();
		if (enemies.Count == 0)
		{
			return;
		}

		Flash(enemies);
		await CreatureCmd.Damage(choiceContext, enemies, target.MaxHp, ValueProp.Unpowered, Owner.Creature, null);
	}

	private static bool IsPoisonDamage(Creature target, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		return dealer == null
			&& cardSource == null
			&& target.GetPowerAmount<PoisonPower>() > 0m
			&& (props & ValueProp.Unblockable) != 0
			&& (props & ValueProp.Unpowered) != 0;
	}
}
