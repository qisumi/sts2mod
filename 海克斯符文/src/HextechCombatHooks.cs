using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using static HextechRunes.HextechHookReflection;

namespace HextechRunes;

internal static partial class HextechCombatHooks
{
	private static bool _handlingGoliathMaxHp;

	public static void Install(Harmony harmony)
	{
		HarmonyMethod canPlayPostfix = new(typeof(HextechCombatHooks), nameof(CardCanPlayPostfix))
		{
			priority = Priority.Last
		};
		HarmonyMethod canPlayWithReasonPostfix = new(typeof(HextechCombatHooks), nameof(CardCanPlayWithReasonPostfix))
		{
			priority = Priority.Last
		};

		harmony.Patch(
			RequireMethod(typeof(CardPileCmd), nameof(CardPileCmd.Draw), BindingFlags.Public | BindingFlags.Static, typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(DrawPrefix)));
		harmony.Patch(
			RequireMethod(typeof(CreatureCmd), nameof(CreatureCmd.Heal), BindingFlags.Public | BindingFlags.Static, typeof(Creature), typeof(decimal), typeof(bool)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(HealPrefix)),
			postfix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(HealPostfix)));
		harmony.Patch(
			RequireMethod(typeof(CardModel), nameof(CardModel.CanPlay), BindingFlags.Instance | BindingFlags.Public),
			postfix: canPlayPostfix);
		harmony.Patch(
			RequireMethod(typeof(CardModel), nameof(CardModel.CanPlay), BindingFlags.Instance | BindingFlags.Public, typeof(UnplayableReason).MakeByRefType(), typeof(AbstractModel).MakeByRefType()),
			postfix: canPlayWithReasonPostfix);
		harmony.Patch(
			RequireMethod(typeof(CardModel), nameof(CardModel.SpendResources), BindingFlags.Instance | BindingFlags.Public),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(CardSpendResourcesPrefix)));
		harmony.Patch(
			RequireMethod(typeof(CardModel), nameof(CardModel.OnPlayWrapper), BindingFlags.Instance | BindingFlags.Public, typeof(PlayerChoiceContext), typeof(Creature), typeof(bool), typeof(ResourceInfo), typeof(bool)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(CardOnPlayWrapperPrefix)),
			postfix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(CardOnPlayWrapperPostfix)));
		harmony.Patch(
			RequireMethod(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp), BindingFlags.Public | BindingFlags.Static, typeof(Creature), typeof(decimal)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(GainMaxHpPrefix)),
			postfix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(ResetGoliathTaskPostfix)));
		harmony.Patch(
			RequireMethod(typeof(CreatureCmd), nameof(CreatureCmd.LoseMaxHp), BindingFlags.Public | BindingFlags.Static, typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(bool)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(LoseMaxHpPrefix)),
			postfix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(ResetGoliathTaskPostfix)));
		MethodInfo setMaxHpMethod = RequireMethod(typeof(CreatureCmd), nameof(CreatureCmd.SetMaxHp), BindingFlags.Public | BindingFlags.Static, typeof(Creature), typeof(decimal));
		harmony.Patch(
			setMaxHpMethod,
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(SetMaxHpPrefix)),
			postfix: new HarmonyMethod(
				typeof(HextechCombatHooks),
				setMaxHpMethod.ReturnType == typeof(Task<decimal>)
					? nameof(ResetGoliathDecimalTaskPostfix)
					: nameof(ResetGoliathTaskPostfix)));
		harmony.Patch(
			RequireMethod(typeof(StormPower), nameof(StormPower.BeforeCardPlayed), BindingFlags.Public | BindingFlags.Instance, typeof(CardPlay)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(StormBeforeCardPlayedPrefix)));
		harmony.Patch(
			RequireMethod(typeof(StormPower), nameof(StormPower.AfterCardPlayed), BindingFlags.Public | BindingFlags.Instance, typeof(PlayerChoiceContext), typeof(CardPlay)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(StormAfterCardPlayedPrefix)));
		harmony.Patch(
			RequireMethod(typeof(EntropyPower), nameof(EntropyPower.AfterPlayerTurnStart), BindingFlags.Public | BindingFlags.Instance, typeof(PlayerChoiceContext), typeof(Player)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(EntropyAfterPlayerTurnStartPrefix)));
		harmony.Patch(
			RequireMethod(typeof(ForbiddenGrimoirePower), nameof(ForbiddenGrimoirePower.AfterCombatEnd), BindingFlags.Public | BindingFlags.Instance, typeof(CombatRoom)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(ForbiddenGrimoireAfterCombatEndPrefix)));
		harmony.Patch(
			RequireMethod(typeof(OutbreakPower), nameof(OutbreakPower.AfterPowerAmountChanged), BindingFlags.Public | BindingFlags.Instance, typeof(PowerModel), typeof(decimal), typeof(Creature), typeof(CardModel)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(OutbreakPowerAfterPowerAmountChangedPrefix)),
			postfix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(OutbreakPowerAfterPowerAmountChangedPostfix)));
		harmony.Patch(
			RequireMethod(typeof(SleightOfFleshPower), nameof(SleightOfFleshPower.AfterPowerAmountChanged), BindingFlags.Public | BindingFlags.Instance, typeof(PowerModel), typeof(decimal), typeof(Creature), typeof(CardModel)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(SleightOfFleshPowerAfterPowerAmountChangedPrefix)),
			postfix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(SleightOfFleshPowerAfterPowerAmountChangedPostfix)));
		harmony.Patch(
			RequireMethod(
				typeof(CreatureCmd),
				nameof(CreatureCmd.Damage),
				BindingFlags.Public | BindingFlags.Static,
				typeof(PlayerChoiceContext),
				typeof(IEnumerable<Creature>),
				typeof(decimal),
				typeof(ValueProp),
				typeof(Creature),
				typeof(CardModel)),
			prefix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(ActualDamageCommandPrefix)),
			postfix: new HarmonyMethod(typeof(HextechCombatHooks), nameof(ActualDamageCommandPostfix)));
		InstallRuneSpecificHooks(harmony);
	}
}
