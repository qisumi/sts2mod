using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Heartsteel;

public sealed class OrnnsForge : EventModel
{
	private const int GreetingGold = 60;

	private const int TradeGoldCost = 250;

	private const int TradeMaxHpGain = 6;

	private const int StealHpLoss = 28;

	protected override IReadOnlyList<EventOption> GenerateInitialOptions()
	{
		Player owner = GetOwnerOrThrow();
		RelicModel fairTradeRelic = ModelDb.Relic<HeartsteelRelic>().ToMutable();
		RelicModel stealRelic = ModelDb.Relic<HeartsteelRelic>().ToMutable();

		List<EventOption> options =
		[
			new EventOption(this, Greet, "ORNNS_FORGE.pages.INITIAL.options.GREET"),
			(owner.Gold >= TradeGoldCost
				? CreateRelicOptionWithHoverTips(fairTradeRelic, FairTrade, "ORNNS_FORGE.pages.INITIAL.options.FAIR_TRADE")
				: new EventOption(this, null, "ORNNS_FORGE.pages.INITIAL.options.FAIR_TRADE_LOCKED")),
			(owner.Creature.CurrentHp >= StealHpLoss + 1
				? CreateRelicOptionWithHoverTips(stealRelic, GrabAndRun, "ORNNS_FORGE.pages.INITIAL.options.GRAB_AND_RUN").ThatDoesDamage(StealHpLoss)
				: new EventOption(this, null, "ORNNS_FORGE.pages.INITIAL.options.GRAB_AND_RUN_LOCKED"))
		];

		return options;
	}

	public override bool IsAllowed(IRunState runState)
	{
		return runState.Players.All(static player => player.Gold >= TradeGoldCost || player.Creature.CurrentHp >= StealHpLoss + 1);
	}

	public override IEnumerable<string> GetAssetPaths(IRunState runState)
	{
		return base.GetAssetPaths(runState)
			.Where(static path => path != ModInfo.OrnnsForgePortraitRequestPath);
	}

	private EventOption CreateRelicOptionWithHoverTips(RelicModel relic, Func<Task> onChosen, string textKey)
	{
		return new EventOption(this, onChosen, textKey, relic.HoverTips).WithRelic(relic);
	}

	private async Task Greet()
	{
		Player owner = GetOwnerOrThrow();
		await PlayerCmd.GainGold(GreetingGold, owner);
		SetEventFinished(L10NLookup("ORNNS_FORGE.pages.GREET.description"));
	}

	private async Task FairTrade()
	{
		Player owner = GetOwnerOrThrow();
		await PlayerCmd.LoseGold(TradeGoldCost, owner, GoldLossType.Spent);
		await RelicCmd.Obtain<HeartsteelRelic>(owner);
		await CreatureCmd.GainMaxHp(owner.Creature, TradeMaxHpGain);
		SetEventFinished(L10NLookup("ORNNS_FORGE.pages.FAIR_TRADE.description"));
	}

	private async Task GrabAndRun()
	{
		Player owner = GetOwnerOrThrow();
		await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), owner.Creature, StealHpLoss, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
		await RelicCmd.Obtain<HeartsteelRelic>(owner);
		SetEventFinished(L10NLookup("ORNNS_FORGE.pages.GRAB_AND_RUN.description"));
	}

	private Player GetOwnerOrThrow()
	{
		return Owner ?? throw new InvalidOperationException("Ornn's Forge event has no owner.");
	}
}

public static class OrnnsForgeRegistration
{
	private static bool _hooksInstalled;

	public static void Install(Harmony harmony)
	{
		InstallHooks(harmony);
		Log.Info("[Heartsteel] Registered Ornn's Forge shared event.");
	}

	private static void InstallHooks(Harmony harmony)
	{
		if (_hooksInstalled)
		{
			return;
		}

		MethodInfo allSharedEventsGetter = typeof(ModelDb).GetProperty(nameof(ModelDb.AllSharedEvents), BindingFlags.Static | BindingFlags.Public)?.GetMethod
			?? throw new InvalidOperationException("Could not find ModelDb.AllSharedEvents getter.");

		harmony.Patch(allSharedEventsGetter, postfix: new HarmonyMethod(typeof(OrnnsForgeRegistration), nameof(AllSharedEventsPostfix)));
		_hooksInstalled = true;
	}

	private static void AllSharedEventsPostfix(ref IEnumerable<EventModel> __result)
	{
		__result = __result.Concat([ModelDb.Event<OrnnsForge>()]).Distinct();
	}
}
