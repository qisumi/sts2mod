using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	private readonly record struct HealPostState(Player? Player, Creature Creature, decimal Amount, bool ShouldProcess);

	private static bool HealPrefix(Creature creature, ref decimal amount, ref Task __result, out HealPostState __state)
	{
		Player? player = creature.Player;
		if (player != null && creature == player.Creature)
		{
			if (player.GetRelic<OverflowRune>() != null)
			{
				amount *= 2m;
			}

			if (player.GetRelic<FirstAidKitRune>() != null)
			{
				amount *= 1.25m;
			}

			if (player.GetRelic<PacifistRune>() is PacifistRune pacifistRune)
			{
				amount *= pacifistRune.SustainMultiplier;
			}

			if (player.GetRelic<SacrificeRune>() is SacrificeRune sacrificeRune)
			{
				amount *= sacrificeRune.SustainMultiplier;
			}

			if (player.GetRelic<BackToBasicsRune>() != null)
			{
				amount *= 1.4m;
			}

			if (player.GetRelic<GoliathRune>() != null)
			{
				amount *= 1.2m;
			}

			if (player.GetRelic<ProteinShakeRune>() is ProteinShakeRune proteinShakeRune)
			{
				amount *= proteinShakeRune.SustainMultiplier;
			}

			if (player.GetRelic<ProtectionForge>() is ProtectionForge protectionForge)
			{
				amount *= protectionForge.SustainMultiplier;
			}

			if (player.GetRelic<MoreTheMerrierRune>() is MoreTheMerrierRune moreTheMerrierRune)
			{
				amount *= moreTheMerrierRune.SustainMultiplier;
			}

			if (player.GetRelic<GoldenSpatulaRune>() is GoldenSpatulaRune goldenSpatulaRune)
			{
				amount *= goldenSpatulaRune.SustainMultiplier;
			}
		}

		if (player?.GetRelic<GlassCannonRune>() is GlassCannonRune glassCannonRune && creature == player.Creature)
		{
			int healCap = (int)Math.Floor(creature.MaxHp * glassCannonRune.HealCapPercent);
			amount = Math.Min(amount, Math.Max(0, healCap - creature.CurrentHp));
			if (amount <= 0m)
			{
				__state = default;
				__result = Task.CompletedTask;
				return false;
			}
		}

		RunState? currentRunState = creature.CombatState?.RunState as RunState;
		HextechMayhemModifier? modifier = null;
		bool isEnemyReviveHeal = IsEnemyReviveHeal(creature, amount);
		if (creature.Side == CombatSide.Enemy
			&& currentRunState != null
			&& !isEnemyReviveHeal
			&& GetMayhemModifier(currentRunState) is HextechMayhemModifier activeModifier)
		{
			modifier = activeModifier;
			amount = modifier.ModifyEnemyHealAmount(creature, amount);
			if (amount <= 0m)
			{
				__state = default;
				__result = Task.CompletedTask;
				return false;
			}
		}

		if (!isEnemyReviveHeal && TryQueueEnemyHealAsDelayedBlock(creature, amount, currentRunState, modifier))
		{
			__state = default;
			__result = Task.CompletedTask;
			return false;
		}

		if (amount <= 0m)
		{
			__state = default;
			__result = Task.CompletedTask;
			return false;
		}

		__state = new HealPostState(player, creature, amount, ShouldProcess: true);
		return true;
	}

	private static void HealPostfix(HealPostState __state, ref Task __result)
	{
		if (!__state.ShouldProcess)
		{
			return;
		}

		__result = HealAfterOriginal(__result, __state);
	}

	private static async Task HealAfterOriginal(Task original, HealPostState state)
	{
		await original;

		Player? player = state.Player;
		Creature creature = state.Creature;
		decimal amount = state.Amount;
		if (player?.GetRelic<HolyFireRune>() != null
			&& creature == player.Creature
			&& creature.CombatState != null
			&& CombatManager.Instance.IsInProgress)
		{
			List<Creature> enemies = creature.CombatState.Enemies.Where(static enemy => enemy.IsAlive).ToList();
			int burnAmount = (int)Math.Floor(amount);
			if (enemies.Count > 0 && burnAmount > 0)
			{
				Creature target = enemies[HextechStableRandom.Index(
					(RunState)player.RunState,
					enemies.Count,
					"holy-fire-heal-target",
					HextechStableRandom.PlayerKey(player),
					creature.CombatState.RoundNumber.ToString(),
					burnAmount.ToString(),
					CombatManager.Instance.History.Entries.Count().ToString())];
				await PowerCmd.Apply<HextechBurnPower>(target, burnAmount, player.Creature, null);
			}
		}

		if (player?.GetRelic<CircleOfDeathRune>() is CircleOfDeathRune circleOfDeathRune
			&& creature == player.Creature
			&& creature.CombatState != null)
		{
			await circleOfDeathRune.HandleSustainGained(amount);
		}
	}

	private static bool IsSkulkingColony(Creature creature)
	{
		return creature.Side == CombatSide.Enemy && creature.Monster is SkulkingColony;
	}

	private static bool IsEnemyReviveHeal(Creature creature, decimal amount)
	{
		return creature.Side == CombatSide.Enemy && creature.IsDead && amount > 0m;
	}

	private static bool TryQueueEnemyHealAsDelayedBlock(
		Creature creature,
		decimal amount,
		RunState? runState,
		HextechMayhemModifier? modifier)
	{
		if (creature.Side != CombatSide.Enemy || amount <= 0m || runState == null)
		{
			return false;
		}

		List<RegenerationSuppressionRune> suppressionRunes = runState.Players
			.Select(static player => player.GetRelic<RegenerationSuppressionRune>())
			.OfType<RegenerationSuppressionRune>()
			.ToList();
		if (!IsSkulkingColony(creature) && suppressionRunes.Count == 0)
		{
			return false;
		}

		modifier ??= ModEntry.EnsureMayhemModifier(runState);
		if (!modifier.QueueEnemyHealingBlock(creature, amount))
		{
			return false;
		}

		foreach (RegenerationSuppressionRune rune in suppressionRunes)
		{
			rune.NotifyEnemyHealSuppressed(creature);
		}

		return true;
	}
}
