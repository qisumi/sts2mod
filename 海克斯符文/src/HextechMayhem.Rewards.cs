using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunes;

internal sealed partial class HextechMayhemModifier
{
	public override bool TryModifyRewards(Player player, List<Reward> rewards, AbstractRoom? room)
	{
		HextechEnemyHexContext context = new(this);
		bool modified = false;
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this))
		{
			modified |= effect.TryModifyRewards(context, player, rewards, room);
		}

		return modified;
	}

	public override CardCreationOptions ModifyCardRewardCreationOptions(Player player, CardCreationOptions options)
	{
		HextechEnemyHexContext context = new(this);
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this))
		{
			options = effect.ModifyCardRewardCreationOptions(context, player, options);
		}

		return options;
	}

	public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
	{
		HextechEnemyHexContext context = new(this);
		bool modified = false;
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this))
		{
			modified |= effect.TryModifyCardRewardOptions(context, player, cardRewardOptions, creationOptions);
		}

		return modified;
	}

	public override bool TryModifyCardRewardOptionsLate(Player player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
	{
		HextechEnemyHexContext context = new(this);
		bool modified = false;
		foreach (HextechEnemyHexEffect effect in HextechEnemyHexEffects.GetActive(this))
		{
			modified |= effect.TryModifyCardRewardOptionsLate(context, player, cardRewardOptions, creationOptions);
		}

		return modified;
	}
}
