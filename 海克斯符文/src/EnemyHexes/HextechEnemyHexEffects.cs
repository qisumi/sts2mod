namespace HextechRunes;

internal static class HextechEnemyHexEffects
{
	private static readonly IReadOnlyList<HextechEnemyHexEffect> OrderedEffects =
	[
		new SlapEnemyHex(),
		new EscapePlanEnemyHex(),
		new HeavyHitterEnemyHex(),
		new BigStrengthEnemyHex(),
		new TormentorEnemyHex(),
		new ProtectiveVeilEnemyHex(),
		new RepulsorEnemyHex(),
		new ThornmailEnemyHex(),
		new LightEmUpEnemyHex(),
		new MountainSoulEnemyHex(),
		new FirstAidKitEnemyHex(),
		new SpeedDemonEnemyHex(),
		new FrostWraithEnemyHex(),
		new BloodPactEnemyHex(),
		new StartupRoutineEnemyHex(),
		new DizzySpinningEnemyHex(),
		new BrutalForceEnemyHex(),
		new ZealotEnemyHex(),
		new SturdyEnemyHex(),
		new DawnbringersResolveEnemyHex(),
		new ShrinkRayEnemyHex(),
		new FirebrandEnemyHex(),
		new SuperBrainEnemyHex(),
		new NightstalkingEnemyHex(),
		new AstralBodyEnemyHex(),
		new TankEngineEnemyHex(),
		new ShrinkEngineEnemyHex(),
		new GetExcitedEnemyHex(),
		new TwiceThriceEnemyHex(),
		new LoopEnemyHex(),
		new ServantMasterEnemyHex(),
		new CuttingEdgeAlchemistEnemyHex(),
		new DivineInterventionEnemyHex(),
		new SonataEnemyHex(),
		new DevilsDanceEnemyHex(),
		new ImmortalBoneEnemyHex(),
		new DoomsdayEnemyHex(),
		new WarmogsSpiritEnemyHex(),
		new BloodArmorEnemyHex(),
		new JinlianBoxEnemyHex(),
		new MirrorReflectionEnemyHex(),
		new BlueCandleMedkitEnemyHex(),
		new TanksShieldEnemyHex(),
		new ScaredStiffEnemyHex(),
		new CourageOfColossusEnemyHex(),
		new GlassCannonEnemyHex(),
		new GoliathEnemyHex(),
		new QueenEnemyHex(),
		new HandOfBaronEnemyHex(),
		new CantTouchThisEnemyHex(),
		new MasterOfDualityEnemyHex(),
		new GoldrendEnemyHex(),
		new FeelTheBurnEnemyHex(),
		new BackToBasicsEnemyHex(),
		new DrawYourSwordEnemyHex(),
		new MadScientistEnemyHex(),
		new FeyMagicEnemyHex(),
		new FinalFormEnemyHex(),
		new UnmovableMountainEnemyHex(),
		new MikaelsBlessingEnemyHex(),
		new ClownCollegeEnemyHex(),
		new SingularityAIEnemyHex(),
		new ProteinShakeEnemyHex(),
		new GoldenSpatulaEnemyHex(),
		new HailToTheKingEnemyHex(),
		new EightPennyGateEnemyHex(),
		new DuffsVintageEnemyHex(),
		new HastyScribbleEnemyHex(),
		new MiseryEnemyHex(),
		new ShoulderVakuEnemyHex(),
		new UpgradeEnemyHex(),
		new NearDeathFeastEnemyHex(),
		new GhostFormEnemyHex(),
		new SerpentsFangEnemyHex(),
		new PandorasBoxEnemyHex(),
		new ForbiddenGrimoireEnemyHex()
	];

	internal static IEnumerable<HextechEnemyHexEffect> GetActive(HextechMayhemModifier modifier)
	{
		foreach (HextechEnemyHexEffect effect in OrderedEffects)
		{
			if (modifier.HasActiveMonsterHex(effect.Kind))
			{
				yield return effect;
			}
		}
	}

	internal static bool HasActiveAttackCostPreviewEffect(HextechMayhemModifier modifier)
	{
		return GetActive(modifier).Any(static effect => effect.AffectsPlayerAttackCostPreview);
	}

	internal static IReadOnlySet<MonsterHexKind> RegisteredKinds => OrderedEffects
		.Select(static effect => effect.Kind)
		.ToHashSet();
}
