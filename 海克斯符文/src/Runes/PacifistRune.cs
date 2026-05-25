using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace HextechRunes;

public sealed class PacifistRune : HextechRelicBase
{
	private static readonly HashSet<PacifistRune> RunesWithPendingDoom = new();

	private readonly List<PendingDoomApplication> _pendingDoomApplications = [];
	private int _replacementDoomApplicationDepth;

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("SustainMultiplier", 1.5m),
		new PowerVar<DoomPower>(1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<DoomPower>()
	];

	public decimal SustainMultiplier => DynamicVars["SustainMultiplier"].BaseValue;

	public override Task BeforeCombatStart()
	{
		ClearPendingDoomApplicationsForRune();
		_replacementDoomApplicationDepth = 0;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom room)
	{
		ClearPendingDoomApplicationsForRune();
		_replacementDoomApplicationDepth = 0;
		return Task.CompletedTask;
	}

	public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, HextechCombatState combatState)
	{
		ClearPendingDoomApplicationsForRune();
		return Task.CompletedTask;
	}

	public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
	{
		return target == Owner?.Creature ? SustainMultiplier : 1m;
	}

	public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (_replacementDoomApplicationDepth > 0)
		{
			return 0m;
		}

		if (Owner == null || target?.Side != CombatSide.Enemy || amount <= 0m || !IsDamageFromOwner(dealer, cardSource))
		{
			return 1m;
		}

		long commandId = HextechCombatHooks.CurrentActualDamageCommandId;
		if (commandId != 0L && target.CombatId is uint combatId)
		{
			EnqueuePendingDoomApplication(commandId, combatId, amount, cardSource);
		}

		return 0m;
	}

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (Owner == null || target.Side != CombatSide.Enemy || target.CombatId is not uint combatId)
		{
			return;
		}

		long commandId = HextechCombatHooks.CurrentActualDamageCommandId;
		if (commandId == 0L || !TryTakePendingDoomApplication(commandId, combatId, out PendingDoomApplication? pending))
		{
			return;
		}

		PendingDoomApplication doom = pending!;
		Flash([target]);
		await ApplyReplacementDoom(target, doom.Amount, doom.CardSource);
	}

	internal static void ClearPendingDoomApplications(long commandId)
	{
		if (RunesWithPendingDoom.Count == 0)
		{
			return;
		}

		PacifistRune[] runes = RunesWithPendingDoom.ToArray();
		foreach (PacifistRune rune in runes)
		{
			rune.ClearPendingDoomApplicationsForCommand(commandId);
		}
	}

	private void EnqueuePendingDoomApplication(long commandId, uint combatId, decimal amount, CardModel? cardSource)
	{
		decimal doom = Math.Max(1m, Math.Floor(amount));
		for (int i = _pendingDoomApplications.Count - 1; i >= 0; i--)
		{
			PendingDoomApplication pending = _pendingDoomApplications[i];
			if (pending.CommandId == commandId && pending.CombatId == combatId)
			{
				_pendingDoomApplications[i] = pending with
				{
					Amount = Math.Max(pending.Amount, doom),
					CardSource = cardSource ?? pending.CardSource
				};
				RunesWithPendingDoom.Add(this);
				return;
			}
		}

		_pendingDoomApplications.Add(new PendingDoomApplication(commandId, combatId, doom, cardSource));
		RunesWithPendingDoom.Add(this);
	}

	private bool TryTakePendingDoomApplication(long commandId, uint combatId, out PendingDoomApplication? pending)
	{
		for (int i = 0; i < _pendingDoomApplications.Count; i++)
		{
			pending = _pendingDoomApplications[i];
			if (pending.CommandId != commandId || pending.CombatId != combatId)
			{
				continue;
			}

			_pendingDoomApplications.RemoveAt(i);
			RemoveFromPendingRegistryIfEmpty();
			return true;
		}

		pending = null;
		return false;
	}

	private void ClearPendingDoomApplicationsForCommand(long commandId)
	{
		_pendingDoomApplications.RemoveAll(pending => pending.CommandId == commandId);
		RemoveFromPendingRegistryIfEmpty();
	}

	private void ClearPendingDoomApplicationsForRune()
	{
		_pendingDoomApplications.Clear();
		RunesWithPendingDoom.Remove(this);
	}

	private void RemoveFromPendingRegistryIfEmpty()
	{
		if (_pendingDoomApplications.Count == 0)
		{
			RunesWithPendingDoom.Remove(this);
		}
	}

	private async Task ApplyReplacementDoom(Creature target, decimal amount, CardModel? cardSource)
	{
		// Prevent damage-on-debuff effects, such as Sleight of Flesh, from escaping Pacifist or recursively becoming more Doom.
		_replacementDoomApplicationDepth++;
		try
		{
			await PowerCmd.Apply<DoomPower>(target, amount, Owner!.Creature, cardSource);
		}
		finally
		{
			_replacementDoomApplicationDepth--;
		}
	}

	private sealed record PendingDoomApplication(long CommandId, uint CombatId, decimal Amount, CardModel? CardSource);
}
