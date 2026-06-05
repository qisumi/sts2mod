using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace HextechRunes;

internal sealed class FlyingKickCorpseLaunchDriver
{
	private const float Lifetime = 1.25f;
	private const float InitialVelocityX = 1080f;
	private const float DampingPerFrame = 0.92f;

	private static readonly HashSet<uint> PendingCombatIds = [];
	private static readonly HashSet<Creature> PendingCreatureRefs = new(ReferenceEqualityComparer.Instance);
	private static readonly HashSet<ulong> ActiveCreatureNodes = [];
	private static bool LoggedAndroidSkip;

	private readonly NCreature _creature;
	private NCreatureVisuals? _visuals;
	private Vector2 _velocity = new(InitialVelocityX, 0f);
	private float _elapsed;

	private FlyingKickCorpseLaunchDriver(NCreature creature)
	{
		_creature = creature;
	}

	internal static void MarkPending(Creature creature)
	{
		if (creature.CombatId is uint combatId)
		{
			PendingCombatIds.Add(combatId);
			return;
		}

		PendingCreatureRefs.Add(creature);
	}

	internal static void MarkPendingUntilConsumed(Creature creature)
	{
		MarkPending(creature);
		TaskHelper.RunSafely(ClearPendingAfterDelay(creature));
	}

	internal static bool TryConsumePending(Creature? creature)
	{
		if (creature == null)
		{
			return false;
		}

		if (creature.CombatId is uint combatId)
		{
			return PendingCombatIds.Remove(combatId);
		}

		return PendingCreatureRefs.Remove(creature);
	}

	internal static void ClearPending(Creature creature)
	{
		if (creature.CombatId is uint combatId)
		{
			PendingCombatIds.Remove(combatId);
		}

		PendingCreatureRefs.Remove(creature);
	}

	private static async Task ClearPendingAfterDelay(Creature creature)
	{
		await Task.Delay(TimeSpan.FromSeconds(3));
		ClearPending(creature);
	}

	internal static void TryAttach(NCreature creature)
	{
		try
		{
			if (HextechRuntimeRuneCompatibility.IsAndroidRuntime)
			{
				if (!LoggedAndroidSkip)
				{
					Log.Warn($"[{ModInfo.Id}][Mayhem][Compat] Flying Kick corpse launch visual skipped on Android runtime.");
					LoggedAndroidSkip = true;
				}

				return;
			}

			if (!GodotObject.IsInstanceValid(creature) || creature.Entity?.IsMonster != true)
			{
				return;
			}

			ulong creatureInstanceId = creature.GetInstanceId();
			if (!ActiveCreatureNodes.Add(creatureInstanceId))
			{
				return;
			}

			FlyingKickCorpseLaunchDriver driver = new(creature);
			if (!driver.Start())
			{
				ActiveCreatureNodes.Remove(creatureInstanceId);
				return;
			}

			TaskHelper.RunSafely(driver.RunAsync(creatureInstanceId));
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] Could not attach Flying Kick corpse launch driver: {ex.Message}");
		}
	}

	private bool Start()
	{
		try
		{
			_visuals = _creature.Visuals;
			return GodotObject.IsInstanceValid(_visuals);
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] Could not initialize Flying Kick corpse launch driver: {ex.Message}");
			return false;
		}
	}

	private async Task RunAsync(ulong creatureInstanceId)
	{
		try
		{
			while (_elapsed < Lifetime)
			{
				if (!GodotObject.IsInstanceValid(_creature) || !GodotObject.IsInstanceValid(_visuals))
				{
					return;
				}

				float dt = Mathf.Min(Mathf.Max((float)_visuals.GetProcessDeltaTime(), 1f / 120f), 0.05f);
				_elapsed += dt;

				_creature.Position += _velocity * dt;
				_velocity *= MathF.Pow(DampingPerFrame, dt * 60f);

				SceneTree tree = _visuals.GetTree();
				if (!GodotObject.IsInstanceValid(tree))
				{
					return;
				}

				await _visuals.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
			}
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] Flying Kick corpse launch driver stopped after runtime error: {ex.Message}");
		}
		finally
		{
			ActiveCreatureNodes.Remove(creatureInstanceId);
		}
	}
}
