using MegaCrit.Sts2.Core.Models;

namespace KeystoneRunes;

internal static class ModInfo
{
	public readonly record struct RuneSeriesGroup(string LocalizationKey, IReadOnlyList<RelicModel> Relics);

	public const string Id = "KeystoneRunes";

	public const string DisplayName = "基石符文";

#if STS2_104_OR_NEWER
	public const string TargetGameVersion = "0.104.0";
#else
	public const string TargetGameVersion = "0.103.2";
#endif

	public const string KeystoneSubcategoryKey = "KEYSTONE_RUNES_SUBCATEGORY";

	public const string ElectrocuteIconPath = "res://KeystoneRunes/images/relics/electrocute.png";

	public const string FirstStrikeIconPath = "res://KeystoneRunes/images/relics/first_strike.png";

	public const string GraspIconPath = "res://KeystoneRunes/images/relics/grasp.png";

	public const string ConquerorIconPath = "res://KeystoneRunes/images/relics/conqueror.png";

	public const string AeryIconPath = "res://KeystoneRunes/images/relics/aery.png";

	public const string LethalTempoIconPath = "res://KeystoneRunes/images/relics/lethal_tempo.png";

	public const string PhaseRushIconPath = "res://KeystoneRunes/images/relics/phase_rush.png";

	public const string UnsealedSpellbookIconPath = "res://KeystoneRunes/images/relics/unsealed_spellbook.png";

	public const string HailOfBladesIconPath = "res://KeystoneRunes/images/relics/hail_of_blades.png";

	public const string FleetFootworkIconPath = "res://KeystoneRunes/images/relics/fleet_footwork.png";

	public const string ArcaneCometIconPath = "res://KeystoneRunes/images/relics/arcane_comet.png";

	public const string DarkHarvestIconPath = "res://KeystoneRunes/images/relics/dark_harvest.png";

	public const string GlacialAugmentIconPath = "res://KeystoneRunes/images/relics/glacial_augment.png";

	public const string AftershockIconPath = "res://KeystoneRunes/images/relics/aftershock.png";

	public const string GuardianIconPath = "res://KeystoneRunes/images/relics/guardian.png";

	public static IReadOnlyList<RelicModel> GetCanonicalRunes()
	{
		return
		[
			ModelDb.Relic<Keystone_ElectrocuteRune>(),
			ModelDb.Relic<Keystone_FirstStrikeRune>(),
			ModelDb.Relic<Keystone_UndyingGraspRune>(),
			ModelDb.Relic<Keystone_ConquerorRune>(),
			ModelDb.Relic<Keystone_SummonAeryRune>(),
			ModelDb.Relic<Keystone_LethalTempoRune>(),
			ModelDb.Relic<Keystone_PhaseRushRune>(),
			ModelDb.Relic<Keystone_UnsealedSpellbookRune>(),
			ModelDb.Relic<Keystone_HailOfBladesRune>(),
			ModelDb.Relic<Keystone_FleetFootworkRune>(),
			ModelDb.Relic<Keystone_ArcaneCometRune>(),
			ModelDb.Relic<Keystone_DarkHarvestRune>(),
			ModelDb.Relic<Keystone_GlacialAugmentRune>(),
			ModelDb.Relic<Keystone_AftershockRune>(),
			ModelDb.Relic<Keystone_GuardianRune>()
		];
	}

	public static bool IsKeystoneRelic(RelicModel? relic)
	{
		if (relic == null)
		{
			return false;
		}

		ModelId id = relic.CanonicalInstance?.Id ?? relic.Id;
		return id == ModelDb.GetId<Keystone_ElectrocuteRune>()
			|| id == ModelDb.GetId<Keystone_FirstStrikeRune>()
			|| id == ModelDb.GetId<Keystone_UndyingGraspRune>()
			|| id == ModelDb.GetId<Keystone_ConquerorRune>()
			|| id == ModelDb.GetId<Keystone_SummonAeryRune>()
			|| id == ModelDb.GetId<Keystone_LethalTempoRune>()
			|| id == ModelDb.GetId<Keystone_PhaseRushRune>()
			|| id == ModelDb.GetId<Keystone_UnsealedSpellbookRune>()
			|| id == ModelDb.GetId<Keystone_HailOfBladesRune>()
			|| id == ModelDb.GetId<Keystone_FleetFootworkRune>()
			|| id == ModelDb.GetId<Keystone_ArcaneCometRune>()
			|| id == ModelDb.GetId<Keystone_DarkHarvestRune>()
			|| id == ModelDb.GetId<Keystone_GlacialAugmentRune>()
			|| id == ModelDb.GetId<Keystone_AftershockRune>()
			|| id == ModelDb.GetId<Keystone_GuardianRune>();
	}

	public static string? TryGetRelicIconPath(RelicModel relic)
	{
		ModelId id = relic.CanonicalInstance?.Id ?? relic.Id;
		if (id == ModelDb.GetId<Keystone_ElectrocuteRune>())
		{
			return ElectrocuteIconPath;
		}

		if (id == ModelDb.GetId<Keystone_FirstStrikeRune>())
		{
			return FirstStrikeIconPath;
		}

		if (id == ModelDb.GetId<Keystone_UndyingGraspRune>())
		{
			return GraspIconPath;
		}

		if (id == ModelDb.GetId<Keystone_ConquerorRune>())
		{
			return ConquerorIconPath;
		}

		if (id == ModelDb.GetId<Keystone_SummonAeryRune>())
		{
			return AeryIconPath;
		}

		if (id == ModelDb.GetId<Keystone_LethalTempoRune>())
		{
			return LethalTempoIconPath;
		}

		if (id == ModelDb.GetId<Keystone_PhaseRushRune>())
		{
			return PhaseRushIconPath;
		}

		if (id == ModelDb.GetId<Keystone_UnsealedSpellbookRune>())
		{
			return UnsealedSpellbookIconPath;
		}

		if (id == ModelDb.GetId<Keystone_HailOfBladesRune>())
		{
			return HailOfBladesIconPath;
		}

		if (id == ModelDb.GetId<Keystone_FleetFootworkRune>())
		{
			return FleetFootworkIconPath;
		}

		if (id == ModelDb.GetId<Keystone_ArcaneCometRune>())
		{
			return ArcaneCometIconPath;
		}

		if (id == ModelDb.GetId<Keystone_DarkHarvestRune>())
		{
			return DarkHarvestIconPath;
		}

		if (id == ModelDb.GetId<Keystone_GlacialAugmentRune>())
		{
			return GlacialAugmentIconPath;
		}

		if (id == ModelDb.GetId<Keystone_AftershockRune>())
		{
			return AftershockIconPath;
		}

		if (id == ModelDb.GetId<Keystone_GuardianRune>())
		{
			return GuardianIconPath;
		}

		return null;
	}

	public static IReadOnlyList<RuneSeriesGroup> GetRuneSeriesGroups(IReadOnlyList<RelicModel> relics)
	{
		Dictionary<ModelId, RelicModel> byId = relics.ToDictionary(static relic => relic.CanonicalInstance?.Id ?? relic.Id);

		IReadOnlyList<RelicModel> BuildGroup(params Type[] runeTypes)
		{
			List<RelicModel> group = new();
			foreach (Type runeType in runeTypes)
			{
				ModelId id = ModelDb.GetId(runeType);
				if (byId.TryGetValue(id, out RelicModel? relic))
				{
					group.Add(relic);
				}
			}

			return group;
		}

		return
		[
			new RuneSeriesGroup("PRECISION", BuildGroup(typeof(Keystone_ConquerorRune), typeof(Keystone_LethalTempoRune), typeof(Keystone_FleetFootworkRune))),
			new RuneSeriesGroup("DOMINATION", BuildGroup(typeof(Keystone_ElectrocuteRune), typeof(Keystone_HailOfBladesRune), typeof(Keystone_DarkHarvestRune))),
			new RuneSeriesGroup("SORCERY", BuildGroup(typeof(Keystone_SummonAeryRune), typeof(Keystone_ArcaneCometRune), typeof(Keystone_PhaseRushRune))),
			new RuneSeriesGroup("RESOLVE", BuildGroup(typeof(Keystone_UndyingGraspRune), typeof(Keystone_AftershockRune), typeof(Keystone_GuardianRune))),
			new RuneSeriesGroup("INSPIRATION", BuildGroup(typeof(Keystone_FirstStrikeRune), typeof(Keystone_UnsealedSpellbookRune), typeof(Keystone_GlacialAugmentRune)))
		];
	}
}
