namespace HextechRunes;

internal sealed partial class HextechMayhemCombatTrackingState
{
	[System.AttributeUsage(System.AttributeTargets.Field)]
	private sealed class CombatTrackingTransientAttribute : System.Attribute
	{
	}

	public readonly Dictionary<uint, int> SlapProcsThisTurn = new();
	public readonly Dictionary<uint, int> TormentorProcsThisTurn = new();
	public readonly Dictionary<uint, int> CourageProcsThisTurn = new();
	public readonly Dictionary<uint, int> BloodPactProcsThisTurn = new();
	public readonly Dictionary<uint, int> ClownCollegeProcsThisTurn = new();
	public readonly HashSet<uint> EscapePlanTriggered = new();
	public readonly HashSet<uint> EscapePlanPending = new();
	public readonly HashSet<uint> RepulsorTriggered = new();
	public readonly HashSet<uint> RepulsorPending = new();
	public readonly HashSet<uint> DawnTriggered = new();
	public readonly HashSet<uint> SpeedDemonPending = new();
	public readonly HashSet<uint> DevilsDanceTriggeredThisTurn = new();
	public readonly HashSet<uint> FeelTheBurnTriggered = new();
	public readonly Dictionary<uint, uint> FeyMagicPendingNoDrawPlayers = new();
	public readonly Dictionary<uint, int> MikaelsBlessingTriggers = new();
	public readonly HashSet<uint> GoliathApplied = new();
	public readonly HashSet<uint> ProtectiveVeilApplied = new();
	public readonly HashSet<uint> ThornmailApplied = new();
	public readonly HashSet<uint> SuperBrainApplied = new();
	public readonly HashSet<uint> AstralBodyApplied = new();
	public readonly HashSet<uint> DrawYourSwordApplied = new();
	public readonly HashSet<uint> MadScientistApplied = new();
	public readonly HashSet<uint> UnmovableMountainApplied = new();
	public readonly HashSet<uint> GoldenSpatulaApplied = new();
	public readonly HashSet<uint> DoormakerRealStartApplied = new();
	public readonly Dictionary<uint, int> TestSubjectPhaseStartApplied = new();
	public readonly Dictionary<uint, int> TankEngineStacks = new();
	public readonly Dictionary<uint, int> ShrinkEngineStacks = new();
	public readonly Dictionary<uint, int> GetExcitedPending = new();
	public readonly HashSet<uint> FeelTheBurnPending = new();
	public readonly HashSet<uint> MountainSoulHasPreviousTurn = new();
	public readonly HashSet<uint> MountainSoulDamagedSinceLastTurn = new();
	public readonly Dictionary<ulong, int> PlayerAttackCardsPlayedThisTurn = new();
	public readonly Dictionary<ulong, int> PlayerCardsDrawnThisCombat = new();
	public readonly HashSet<ulong> EightPennyGatePlayersTriggeredThisTurn = new();
	[CombatTrackingTransient]
	public readonly HashSet<string> MonsterDebuffActionProcKeysThisTurn = new();
	[CombatTrackingTransient]
	public readonly HashSet<string> GroupedPlayerDebuffProcKeys = new();
	[CombatTrackingTransient]
	public string? LastEnemyThresholdTriggerKey;
	[CombatTrackingTransient]
	public bool HandlingMonsterTormentorBurn;
	[CombatTrackingTransient]
	public bool HandlingServantMasterIllusion;
	[CombatTrackingTransient]
	public bool HandlingGroupedPlayerDebuffs;
	public int EnemyProtectiveVeilTurnCounter;

	public void PreparePlayerSideTurnStart()
	{
		PlayerAttackCardsPlayedThisTurn.Clear();
		BloodPactProcsThisTurn.Clear();
		ClownCollegeProcsThisTurn.Clear();
		EightPennyGatePlayersTriggeredThisTurn.Clear();
	}

	public void PreparePlayerSideTurnEnd()
	{
		PlayerAttackCardsPlayedThisTurn.Clear();
		EightPennyGatePlayersTriggeredThisTurn.Clear();
	}

	public void PrepareEnemySideTurnStart()
	{
		EnemyProtectiveVeilTurnCounter++;
		PlayerAttackCardsPlayedThisTurn.Clear();
		SlapProcsThisTurn.Clear();
		TormentorProcsThisTurn.Clear();
		CourageProcsThisTurn.Clear();
		BloodPactProcsThisTurn.Clear();
		ClownCollegeProcsThisTurn.Clear();
		DevilsDanceTriggeredThisTurn.Clear();
		MonsterDebuffActionProcKeysThisTurn.Clear();
	}

	public void Reset()
	{
		Clear();
	}
}
