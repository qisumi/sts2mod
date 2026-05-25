using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;

namespace HextechRunes;

public sealed class ElicitCard : CardModel
{
	public override CardPoolModel Pool => IsMutable && Owner != null
		? Owner.Character.CardPool
		: ModelDb.CardPool<TokenCardPool>();

	public override CardPoolModel VisualCardPool => Pool;

	public override OrbEvokeType OrbEvokeType => OrbEvokeType.All;

	public override string PortraitPath => HextechAssets.ElicitCardPortraitPath;

	public override IEnumerable<string> AllPortraitPaths => [PortraitPath];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.Static(StaticHoverTip.Evoke)
	];

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new RepeatVar(1)
	];

	public override IEnumerable<CardKeyword> CanonicalKeywords =>
	[
		CardKeyword.Retain
	];

	public ElicitCard()
		: base(1, CardType.Skill, CardRarity.Token, TargetType.Self, shouldShowInCardLibrary: true)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		int repeatCount = Math.Max(1, DynamicVars.Repeat.IntValue);
		for (int repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
		{
			int orbCount = Owner.PlayerCombatState?.OrbQueue.Orbs.Count ?? 0;
			if (orbCount <= 0)
			{
				break;
			}

			if (repeatIndex == repeatCount - 1)
			{
				for (int i = 0; i < orbCount; i++)
				{
					await OrbCmd.EvokeNext(choiceContext, Owner);
				}
				continue;
			}

			foreach (OrbModel orb in Owner.PlayerCombatState!.OrbQueue.Orbs.ToArray())
			{
				await EvokeWithoutRemoving(choiceContext, orb);
			}
		}
	}

	private async Task EvokeWithoutRemoving(PlayerChoiceContext choiceContext, OrbModel orb)
	{
		if (Owner?.Creature.CombatState == null
			|| Owner.PlayerCombatState == null
			|| CombatManager.Instance.IsOverOrEnding
			|| !Owner.PlayerCombatState.OrbQueue.Orbs.Contains(orb))
		{
			return;
		}

		IEnumerable<Creature> targets;
		choiceContext.PushModel(orb);
		try
		{
			targets = await orb.Evoke(choiceContext);
		}
		finally
		{
			choiceContext.PopModel(orb);
		}

		await Hook.AfterOrbEvoked(choiceContext, Owner.Creature.CombatState, orb, targets);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Repeat.UpgradeValueBy(1m);
	}
}

public sealed class TrickMagicCard : CardModel
{
	public override CardPoolModel Pool => IsMutable && Owner != null
		? Owner.Character.CardPool
		: ModelDb.CardPool<TokenCardPool>();

	public override CardPoolModel VisualCardPool => Pool;

	public override string PortraitPath => HextechAssets.TrickMagicCardPortraitPath;

	public override IEnumerable<string> AllPortraitPaths => [PortraitPath];

	public override IEnumerable<CardKeyword> CanonicalKeywords =>
	[
		CardKeyword.Exhaust
	];

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new CardsVar(2),
		new PowerVar<BufferPower>(1m),
		new DynamicVar("Replays", 1m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<BufferPower>(),
		HoverTipFactory.FromPower<HextechAttackReplayPower>()
	];

	public TrickMagicCard()
		: base(0, CardType.Skill, CardRarity.Token, TargetType.Self, shouldShowInCardLibrary: true)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner, fromHandDraw: false);
		await PowerCmd.Apply<BufferPower>(Owner.Creature, DynamicVars["BufferPower"].BaseValue, Owner.Creature, this);
		await PowerCmd.Apply<HextechAttackReplayPower>(Owner.Creature, DynamicVars["Replays"].BaseValue, Owner.Creature, this);
	}

	protected override void OnUpgrade()
	{
		DynamicVars["BufferPower"].UpgradeValueBy(1m);
	}
}

public sealed class CatalystCard : CardModel
{
	public override CardPoolModel Pool => IsMutable && Owner != null
		? Owner.Character.CardPool
		: ModelDb.CardPool<TokenCardPool>();

	public override CardPoolModel VisualCardPool => Pool;

	public override string PortraitPath => HextechAssets.CatalystCardPortraitPath;

	public override IEnumerable<string> AllPortraitPaths => [PortraitPath];

	public override IEnumerable<CardKeyword> CanonicalKeywords =>
	[
		CardKeyword.Exhaust
	];

	protected override IEnumerable<DynamicVar> CanonicalVars =>
	[
		new DynamicVar("PoisonMultiplier", 2m)
	];

	protected override IEnumerable<IHoverTip> ExtraHoverTips =>
	[
		HoverTipFactory.FromPower<PoisonPower>()
	];

	public CatalystCard()
		: base(0, CardType.Skill, CardRarity.Token, TargetType.AnyEnemy, shouldShowInCardLibrary: true)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (cardPlay.Target == null || Owner == null)
		{
			return;
		}

		decimal poison = cardPlay.Target.GetPowerAmount<PoisonPower>();
		decimal multiplier = DynamicVars["PoisonMultiplier"].BaseValue;
		decimal additionalPoison = poison * (multiplier - 1m);
		if (additionalPoison <= 0m)
		{
			return;
		}

		await PowerCmd.Apply<PoisonPower>(cardPlay.Target, additionalPoison, Owner.Creature, this);
	}

	protected override void OnUpgrade()
	{
		DynamicVars["PoisonMultiplier"].UpgradeValueBy(1m);
	}
}
