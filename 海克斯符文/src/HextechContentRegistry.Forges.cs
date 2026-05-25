namespace HextechRunes;

internal static partial class HextechContentRegistry
{
    private static readonly IReadOnlyList<ForgeRegistration> ForgeRegistrations =
    [
        Forge<StrengthForge>(HextechRarityTier.Silver),
        Forge<DexterityForge>(HextechRarityTier.Silver),
        Forge<SilverPlatingForge>(HextechRarityTier.Silver),
        Forge<UpgradeForge>(HextechRarityTier.Silver),
        Forge<FocusForge>(HextechRarityTier.Silver),
        Forge<LifeForge>(HextechRarityTier.Silver),
        Forge<PreparedForge>(HextechRarityTier.Silver),
        Forge<NecrobinderForge>(HextechRarityTier.Silver),
        Forge<SilverStarsForge>(HextechRarityTier.Silver),
        Forge<SilverOrbForge>(HextechRarityTier.Silver),

        Forge<ConstitutionForge>(HextechRarityTier.Gold),
        Forge<DisasterForge>(HextechRarityTier.Gold),
        Forge<GoldLifeForge>(HextechRarityTier.Gold),
        Forge<GoldFocusForge>(HextechRarityTier.Gold),
        Forge<DrawForge>(HextechRarityTier.Gold),
        Forge<GoldUpgradeForge>(HextechRarityTier.Gold),
        Forge<StarsForge>(HextechRarityTier.Gold),
        Forge<OrbSlotForge>(HextechRarityTier.Gold),
        Forge<PlatingForge>(HextechRarityTier.Gold),
        Forge<ThornsForge>(HextechRarityTier.Gold),
        Forge<ArtifactForge>(HextechRarityTier.Gold),

        Forge<PrismaticLifeForge>(HextechRarityTier.Prismatic),
        Forge<AttackForge>(HextechRarityTier.Prismatic),
        Forge<ProtectionForge>(HextechRarityTier.Prismatic),
        Forge<EnergyForge>(HextechRarityTier.Prismatic),
        Forge<RitualForge>(HextechRarityTier.Prismatic),
        Forge<RegenForge>(HextechRarityTier.Prismatic),
        Forge<BufferForge>(HextechRarityTier.Prismatic),
        Forge<SlipperyForge>(HextechRarityTier.Prismatic),
        Forge<PrismaticArtifactForge>(HextechRarityTier.Prismatic),
        Forge<FortuneForge>(HextechRarityTier.Prismatic)
    ];
}
