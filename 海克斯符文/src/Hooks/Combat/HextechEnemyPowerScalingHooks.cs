using System.Reflection;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Singleton;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static class HextechEnemyPowerScalingHooks
{
	private enum ScalingOverride
	{
		Unscaled,
		PlayerCount,
		FinalAmount
	}

	private static readonly AsyncLocal<ScalingOverride?> CurrentOverride = new();

	public static void Install(Harmony harmony)
	{
#if STS2_107_OR_NEWER
		HarmonyMethod prefix = new(typeof(HextechEnemyPowerScalingHooks), nameof(ModifyPowerAmountGivenHookPrefix))
		{
			priority = Priority.First
		};
#else
		HarmonyMethod prefix = new(typeof(HextechEnemyPowerScalingHooks), nameof(ModifyPowerAmountGivenPrefix))
		{
			priority = Priority.First
		};
#endif

		MethodInfo? modifyPowerAmountGivenTarget = TryResolveModifyPowerAmountGivenTarget();
		if (modifyPowerAmountGivenTarget != null)
		{
			harmony.Patch(modifyPowerAmountGivenTarget, prefix: prefix);
		}
		else
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem][Compat] Enemy power multiplayer scaling hook skipped: ModifyPowerAmountGiven target not found in this runtime.");
		}

#if STS2_105_OR_NEWER
		HarmonyMethod scaledPrefix = new(typeof(HextechEnemyPowerScalingHooks), nameof(GetScaledAmountForMultiplayerPrefix))
		{
			priority = Priority.First
		};

		foreach (MethodInfo scaledTarget in ResolveGetScaledAmountForMultiplayerTargets())
		{
			harmony.Patch(scaledTarget, prefix: scaledPrefix);
		}
#endif
	}

	public static async Task<T?> Apply<T>(Creature target, decimal amount, Creature? applier, CardModel? cardSource, bool silent = false)
		where T : PowerModel
	{
		ScalingOverride? scalingOverride = GetScalingOverride(typeof(T));
		if (scalingOverride == null)
		{
			return await PowerCmd.Apply<T>(target, amount, applier, cardSource, silent);
		}

		decimal finalAmount = CalculateFinalAmount(target, amount, applier, scalingOverride.Value);
		finalAmount = ClampPowerOffsetForApply<T>(target, finalAmount);
		if (finalAmount == 0m)
		{
			return target.GetPower<T>();
		}

		Creature? effectiveApplier = ShouldClearSelfApplier(target, applier) ? null : applier;
		using (BeginOverride(ScalingOverride.FinalAmount))
		{
			return await PowerCmd.Apply<T>(target, finalAmount, effectiveApplier, cardSource, silent);
		}
	}

#if STS2_107_OR_NEWER
	private static bool ModifyPowerAmountGivenHookPrefix(
		ICombatState combatState,
		PowerModel power,
		Creature? giver,
		decimal amount,
		Creature? target,
		CardModel? cardSource,
		ref IEnumerable<AbstractModel> modifiers,
		ref decimal __result)
	{
		if (!TryCalculateModifiedPowerAmountGiven(power, giver, amount, target, out decimal modifiedAmount))
		{
			return true;
		}

		modifiers = Array.Empty<AbstractModel>();
		__result = modifiedAmount;
		return false;
	}
#else
	private static bool ModifyPowerAmountGivenPrefix(
		PowerModel power,
		Creature? giver,
		decimal amount,
		Creature? target,
		CardModel? cardSource,
		ref decimal __result)
	{
		if (!TryCalculateModifiedPowerAmountGiven(power, giver, amount, target, out decimal modifiedAmount))
		{
			return true;
		}

		__result = modifiedAmount;
		return false;
	}
#endif

	private static bool TryCalculateModifiedPowerAmountGiven(
		PowerModel power,
		Creature? giver,
		decimal amount,
		Creature? target,
		out decimal modifiedAmount)
	{
		modifiedAmount = amount;
		ScalingOverride? activeOverride = CurrentOverride.Value;
		ScalingOverride? powerOverride = GetScalingOverride(power.GetType());
		if (activeOverride == null
			|| target == null
			|| (!target.IsPrimaryEnemy && !target.IsSecondaryEnemy)
			|| powerOverride == null
			|| (activeOverride.Value != ScalingOverride.FinalAmount && powerOverride != activeOverride))
		{
			return false;
		}

		modifiedAmount = activeOverride.Value switch
		{
			ScalingOverride.PlayerCount => ClampPowerOffsetForApply(power, target, MultiplyByPlayerCount(amount, GetPlayerCount(giver, target))),
			ScalingOverride.Unscaled => ClampPowerOffsetForApply(power, target, amount),
			ScalingOverride.FinalAmount => ClampPowerOffsetForApply(power, target, amount),
			_ => ClampPowerOffsetForApply(power, target, amount)
		};
		return true;
	}

#if STS2_105_OR_NEWER
	private static bool GetScaledAmountForMultiplayerPrefix(
		PowerModel __instance,
		decimal amount,
		Creature target,
		ref decimal __result)
	{
		if (CurrentOverride.Value != ScalingOverride.FinalAmount
			|| target == null
			|| (!target.IsPrimaryEnemy && !target.IsSecondaryEnemy)
			|| GetScalingOverride(__instance.GetType()) == null)
		{
			return true;
		}

		__result = ClampPowerOffsetForApply(__instance, target, amount);
		return false;
	}
#endif

	private static decimal CalculateFinalAmount(Creature target, decimal amount, Creature? applier, ScalingOverride scalingOverride)
	{
		if (!target.IsPrimaryEnemy && !target.IsSecondaryEnemy)
		{
			return amount;
		}

		return scalingOverride switch
		{
			ScalingOverride.PlayerCount => MultiplyByPlayerCount(amount, GetPlayerCount(applier, target)),
			ScalingOverride.Unscaled => ClampPowerAmount(amount),
			ScalingOverride.FinalAmount => ClampPowerAmount(amount),
			_ => ClampPowerAmount(amount)
		};
	}

	private static decimal ClampPowerOffsetForApply<T>(Creature target, decimal amount)
		where T : PowerModel
	{
		return ClampPowerOffsetForApply(ModelDb.Power<T>(), target, amount);
	}

	private static decimal ClampPowerOffsetForApply(PowerModel power, Creature target, decimal amount)
	{
		decimal clamped = ClampPowerAmount(amount);
		if (IsInstancedPower(power))
		{
			return clamped;
		}

		int currentAmount = target.GetPower(power.Id)?.Amount ?? 0;
		if (clamped > 0m)
		{
			decimal maxOffset = int.MaxValue - (decimal)currentAmount;
			return Math.Min(clamped, Math.Max(0m, maxOffset));
		}

		if (clamped < 0m)
		{
			decimal minOffset = int.MinValue - (decimal)currentAmount;
			return Math.Max(clamped, Math.Min(0m, minOffset));
		}

		return clamped;
	}

	private static bool IsInstancedPower(PowerModel power)
	{
#if STS2_105_OR_NEWER
		return power.InstanceType != PowerInstanceType.None;
#else
		return power.IsInstanced;
#endif
	}

	private static bool ShouldClearSelfApplier(Creature target, Creature? applier)
	{
		return applier != null
			&& ReferenceEquals(target, applier)
			&& (target.IsPrimaryEnemy || target.IsSecondaryEnemy);
	}

	private static ScalingOverride? GetScalingOverride(Type powerType)
	{
		if (powerType == typeof(ArtifactPower) || powerType == typeof(SlipperyPower))
		{
			return ScalingOverride.PlayerCount;
		}

		if (powerType == typeof(HardenedShellPower)
			|| powerType == typeof(RegenPower)
			|| powerType == typeof(PlatingPower)
			|| powerType == typeof(ReflectPower)
			|| powerType == typeof(SkittishPower))
		{
			return ScalingOverride.Unscaled;
		}

		return null;
	}

	private static int GetPlayerCount(Creature? giver, Creature target)
	{
		return target.CombatState?.Players.Count
			?? giver?.CombatState?.Players.Count
			?? 1;
	}

	private static decimal MultiplyByPlayerCount(decimal amount, int playerCount)
	{
		int scale = Math.Clamp(playerCount, 1, 16);
		if (scale <= 1)
		{
			return ClampPowerAmount(amount);
		}

		if (amount >= int.MaxValue / scale)
		{
			return int.MaxValue;
		}
		if (amount <= int.MinValue / scale)
		{
			return int.MinValue;
		}

		try
		{
			return ClampPowerAmount(amount * scale);
		}
		catch (OverflowException)
		{
			return amount < 0m ? int.MinValue : int.MaxValue;
		}
	}

	private static decimal ClampPowerAmount(decimal amount)
	{
		if (amount > int.MaxValue)
		{
			return int.MaxValue;
		}
		if (amount < int.MinValue)
		{
			return int.MinValue;
		}

		return amount;
	}

	private static OverrideScope BeginOverride(ScalingOverride scalingOverride)
	{
		return new OverrideScope(scalingOverride);
	}

	private static MethodInfo? TryResolveModifyPowerAmountGivenTarget()
	{
#if STS2_107_OR_NEWER
		return TryGetMethod(
			typeof(Hook),
			nameof(Hook.ModifyPowerAmountGiven),
			BindingFlags.Public | BindingFlags.Static,
			typeof(ICombatState),
			typeof(PowerModel),
			typeof(Creature),
			typeof(decimal),
			typeof(Creature),
			typeof(CardModel),
			typeof(IEnumerable<AbstractModel>).MakeByRefType());
#else
		MethodInfo? reflectedMethod = TryGetMethod(
			typeof(MultiplayerScalingModel),
			nameof(MultiplayerScalingModel.ModifyPowerAmountGiven),
			BindingFlags.Public | BindingFlags.Instance,
			typeof(PowerModel),
			typeof(Creature),
			typeof(decimal),
			typeof(Creature),
			typeof(CardModel));
		if (reflectedMethod == null)
		{
			return null;
		}

		if (reflectedMethod.DeclaringType == typeof(MultiplayerScalingModel)
			&& reflectedMethod.GetMethodBody() != null)
		{
			return reflectedMethod;
		}

		MethodInfo baseDefinition = reflectedMethod.GetBaseDefinition();
		if (baseDefinition.GetMethodBody() != null)
		{
			return baseDefinition;
		}

		Type declaringType = reflectedMethod.DeclaringType ?? typeof(AbstractModel);
		return TryGetMethod(
			declaringType,
			nameof(AbstractModel.ModifyPowerAmountGiven),
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
			typeof(PowerModel),
			typeof(Creature),
			typeof(decimal),
			typeof(Creature),
			typeof(CardModel));
#endif
	}

#if STS2_105_OR_NEWER
	private static IEnumerable<MethodInfo> ResolveGetScaledAmountForMultiplayerTargets()
	{
		List<MethodInfo> targets = new();
		foreach (Type powerType in GetPowerTypesWithScalingOverride())
		{
			MethodInfo? method = TryGetMethod(
				powerType,
				nameof(PowerModel.GetScaledAmountForMultiplayer),
				BindingFlags.Public | BindingFlags.Instance,
				typeof(HextechCombatState),
				typeof(Creature),
				typeof(decimal),
				typeof(Creature),
				typeof(CardModel));
			if (method == null)
			{
				continue;
			}

			Type declaringType = method.DeclaringType ?? typeof(PowerModel);
			method = TryGetMethod(
				declaringType,
				nameof(PowerModel.GetScaledAmountForMultiplayer),
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
				typeof(HextechCombatState),
				typeof(Creature),
				typeof(decimal),
				typeof(Creature),
				typeof(CardModel));
			if (method == null)
			{
				continue;
			}

			if (!ContainsMethod(targets, method))
			{
				targets.Add(method);
			}
		}

		if (targets.Count == 0)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem][Compat] Enemy power multiplayer scaling hook skipped: GetScaledAmountForMultiplayer targets not found in this runtime.");
		}

		return targets;
	}

	private static IEnumerable<Type> GetPowerTypesWithScalingOverride()
	{
		yield return typeof(ArtifactPower);
		yield return typeof(SlipperyPower);
		yield return typeof(HardenedShellPower);
		yield return typeof(RegenPower);
		yield return typeof(PlatingPower);
		yield return typeof(ReflectPower);
		yield return typeof(SkittishPower);
	}

	private static bool ContainsMethod(IEnumerable<MethodInfo> methods, MethodInfo candidate)
	{
		foreach (MethodInfo method in methods)
		{
			if (method.Module == candidate.Module && method.MetadataToken == candidate.MetadataToken)
			{
				return true;
			}
		}

		return false;
	}
#endif

	private sealed class OverrideScope : IDisposable
	{
		private readonly ScalingOverride? _previousOverride;

		public OverrideScope(ScalingOverride scalingOverride)
		{
			_previousOverride = CurrentOverride.Value;
			CurrentOverride.Value = scalingOverride;
		}

		public void Dispose()
		{
			CurrentOverride.Value = _previousOverride;
		}
	}
}
