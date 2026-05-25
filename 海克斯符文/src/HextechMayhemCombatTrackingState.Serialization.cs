using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace HextechRunes;

internal sealed partial class HextechMayhemCombatTrackingState
{
	private static readonly IReadOnlyList<CombatTrackingFieldBinding> PersistentFieldBindings = CreatePersistentFieldBindings();
	private static readonly IReadOnlyList<FieldInfo> TransientFields = CreateTransientFields();

	private readonly record struct CombatTrackingFieldBinding(FieldInfo StateField, PropertyInfo SnapshotProperty);

	public string Serialize()
	{
		if (!HasState())
		{
			return "";
		}

		return JsonSerializer.Serialize(CreateSnapshot());
	}

	public void Restore(string? json)
	{
		Clear();
		if (string.IsNullOrWhiteSpace(json))
		{
			return;
		}

		try
		{
			CombatTrackingSnapshot? snapshot = JsonSerializer.Deserialize<CombatTrackingSnapshot>(json);
			if (snapshot == null)
			{
				return;
			}

			RestoreSnapshot(snapshot);
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}][Mayhem] Failed to restore combat tracking snapshot: {ex}");
			Clear();
		}
	}

	private CombatTrackingSnapshot CreateSnapshot()
	{
		CombatTrackingSnapshot snapshot = new();
		foreach (CombatTrackingFieldBinding binding in PersistentFieldBindings)
		{
			binding.SnapshotProperty.SetValue(
				snapshot,
				CopyStateValue(binding.StateField.GetValue(this), binding.SnapshotProperty.PropertyType));
		}

		return snapshot;
	}

	private void RestoreSnapshot(CombatTrackingSnapshot snapshot)
	{
		foreach (CombatTrackingFieldBinding binding in PersistentFieldBindings)
		{
			RestoreStateField(binding.StateField, binding.SnapshotProperty.GetValue(snapshot));
		}
	}

	private bool HasState()
	{
		return PersistentFieldBindings.Any(binding => HasNonDefaultValue(binding.StateField.GetValue(this)));
	}

	private void Clear()
	{
		foreach (CombatTrackingFieldBinding binding in PersistentFieldBindings)
		{
			ClearStateField(binding.StateField);
		}

		foreach (FieldInfo field in TransientFields)
		{
			ClearStateField(field);
		}
	}

	private static IReadOnlyList<CombatTrackingFieldBinding> CreatePersistentFieldBindings()
	{
		Dictionary<string, FieldInfo> stateFields = GetPublicStateFields()
			.ToDictionary(static field => field.Name, StringComparer.Ordinal);

		CombatTrackingFieldBinding[] bindings = typeof(CombatTrackingSnapshot)
			.GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Select(property =>
			{
				if (!stateFields.TryGetValue(property.Name, out FieldInfo? stateField))
				{
					throw new InvalidOperationException($"Combat tracking snapshot field '{property.Name}' has no matching state field.");
				}

				if (stateField.GetCustomAttribute<CombatTrackingTransientAttribute>() != null)
				{
					throw new InvalidOperationException($"Combat tracking field '{stateField.Name}' cannot be both saved and transient.");
				}

				ValidateSnapshotPropertyType(stateField, property);
				return new CombatTrackingFieldBinding(stateField, property);
			})
			.ToArray();

		ValidateStateFieldCoverage(stateFields.Values, bindings);
		return bindings;
	}

	private static IReadOnlyList<FieldInfo> CreateTransientFields()
	{
		return GetPublicStateFields()
			.Where(static field => field.GetCustomAttribute<CombatTrackingTransientAttribute>() != null)
			.ToArray();
	}

	private static FieldInfo[] GetPublicStateFields()
	{
		return typeof(HextechMayhemCombatTrackingState)
			.GetFields(BindingFlags.Instance | BindingFlags.Public);
	}

	private static void ValidateStateFieldCoverage(IEnumerable<FieldInfo> stateFields, IReadOnlyList<CombatTrackingFieldBinding> persistentBindings)
	{
		HashSet<string> persistentFieldNames = persistentBindings
			.Select(static binding => binding.StateField.Name)
			.ToHashSet(StringComparer.Ordinal);
		string[] missingFields = stateFields
			.Where(static field => field.GetCustomAttribute<CombatTrackingTransientAttribute>() == null)
			.Where(field => !persistentFieldNames.Contains(field.Name))
			.Select(static field => field.Name)
			.OrderBy(static name => name, StringComparer.Ordinal)
			.ToArray();
		if (missingFields.Length > 0)
		{
			throw new InvalidOperationException($"Combat tracking fields must be saved or marked transient: {string.Join(", ", missingFields)}.");
		}
	}

	private static void ValidateSnapshotPropertyType(FieldInfo stateField, PropertyInfo snapshotProperty)
	{
		Type stateType = stateField.FieldType;
		Type snapshotType = snapshotProperty.PropertyType;
		if (typeof(IDictionary).IsAssignableFrom(stateType))
		{
			ValidateAssignableGenericArguments(stateField, snapshotProperty, typeof(Dictionary<,>));
			return;
		}

		if (IsHashSet(stateType))
		{
			ValidateAssignableGenericArguments(stateField, snapshotProperty, typeof(List<>));
			return;
		}

		if (snapshotType != stateType)
		{
			throw new InvalidOperationException($"Combat tracking snapshot property '{snapshotProperty.Name}' has type {snapshotType}, expected {stateType}.");
		}
	}

	private static void ValidateAssignableGenericArguments(FieldInfo stateField, PropertyInfo snapshotProperty, Type expectedSnapshotGenericType)
	{
		Type stateType = stateField.FieldType;
		Type snapshotType = snapshotProperty.PropertyType;
		if (!snapshotType.IsGenericType || snapshotType.GetGenericTypeDefinition() != expectedSnapshotGenericType)
		{
			throw new InvalidOperationException($"Combat tracking snapshot property '{snapshotProperty.Name}' has type {snapshotType}, expected {expectedSnapshotGenericType}.");
		}

		Type[] stateArguments = stateType.GetGenericArguments();
		Type[] snapshotArguments = snapshotType.GetGenericArguments();
		if (!stateArguments.SequenceEqual(snapshotArguments))
		{
			throw new InvalidOperationException($"Combat tracking snapshot property '{snapshotProperty.Name}' generic arguments do not match field '{stateField.Name}'.");
		}
	}

	private static object? CopyStateValue(object? source, Type snapshotType)
	{
		if (source == null)
		{
			return null;
		}

		if (source is IDictionary dictionary)
		{
			return CopyDictionary(dictionary, snapshotType);
		}

		if (IsHashSet(source.GetType()))
		{
			return CopySet(source, snapshotType);
		}

		return source is int counter ? Math.Max(0, counter) : source;
	}

	private static object CopyDictionary(IDictionary source, Type snapshotType)
	{
		IDictionary target = (IDictionary)(Activator.CreateInstance(snapshotType)
			?? throw new InvalidOperationException($"Failed to create combat tracking dictionary {snapshotType}."));
		foreach (object key in OrderedValues(source.Keys))
		{
			target.Add(key, source[key]);
		}

		return target;
	}

	private static object CopySet(object source, Type snapshotType)
	{
		IList target = (IList)(Activator.CreateInstance(snapshotType)
			?? throw new InvalidOperationException($"Failed to create combat tracking list {snapshotType}."));
		foreach (object value in OrderedValues((IEnumerable)source))
		{
			target.Add(value);
		}

		return target;
	}

	private void RestoreStateField(FieldInfo field, object? snapshotValue)
	{
		object? target = field.GetValue(this);
		if (target is IDictionary targetDictionary)
		{
			targetDictionary.Clear();
			if (snapshotValue is IDictionary sourceDictionary)
			{
				foreach (DictionaryEntry entry in sourceDictionary)
				{
					targetDictionary[entry.Key] = entry.Value;
				}
			}

			return;
		}

		if (target != null && IsHashSet(target.GetType()))
		{
			ClearCollection(target);
			if (snapshotValue is IEnumerable sourceValues)
			{
				foreach (object value in sourceValues)
				{
					AddToCollection(target, value);
				}
			}

			return;
		}

		if (field.FieldType == typeof(int))
		{
			field.SetValue(this, Math.Max(0, snapshotValue is int value ? value : 0));
		}
	}

	private void ClearStateField(FieldInfo field)
	{
		object? target = field.GetValue(this);
		if (target != null && TryInvokeClear(target))
		{
			return;
		}

		if (field.FieldType == typeof(string))
		{
			field.SetValue(this, null);
		}
		else if (field.FieldType == typeof(bool))
		{
			field.SetValue(this, false);
		}
		else if (field.FieldType == typeof(int))
		{
			field.SetValue(this, 0);
		}
	}

	private static bool HasNonDefaultValue(object? value)
	{
		return value switch
		{
			null => false,
			IDictionary dictionary => dictionary.Count > 0,
			_ when TryGetCount(value, out int count) => count > 0,
			int counter => counter > 0,
			bool flag => flag,
			string text => !string.IsNullOrEmpty(text),
			_ => false
		};
	}

	private static bool TryGetCount(object value, out int count)
	{
		if (value is ICollection collection)
		{
			count = collection.Count;
			return true;
		}

		PropertyInfo? countProperty = value.GetType().GetProperty(nameof(ICollection.Count), BindingFlags.Instance | BindingFlags.Public);
		if (countProperty?.GetValue(value) is int propertyCount)
		{
			count = propertyCount;
			return true;
		}

		count = 0;
		return false;
	}

	private static bool IsHashSet(Type type)
	{
		return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>);
	}

	private static IEnumerable<object> OrderedValues(IEnumerable values)
	{
		return values.Cast<object>().OrderBy(static value => value, Comparer<object>.Create(CompareValues));
	}

	private static int CompareValues(object? left, object? right)
	{
		if (ReferenceEquals(left, right))
		{
			return 0;
		}

		if (left == null)
		{
			return -1;
		}

		if (right == null)
		{
			return 1;
		}

		return left is IComparable comparable
			? comparable.CompareTo(right)
			: string.CompareOrdinal(left.ToString(), right.ToString());
	}

	private static bool TryInvokeClear(object target)
	{
		MethodInfo? clear = target.GetType().GetMethod(nameof(List<object>.Clear), Type.EmptyTypes);
		if (clear == null)
		{
			return false;
		}

		clear.Invoke(target, null);
		return true;
	}

	private static void ClearCollection(object target)
	{
		if (!TryInvokeClear(target))
		{
			throw new InvalidOperationException($"Combat tracking collection {target.GetType()} does not expose Clear().");
		}
	}

	private static void AddToCollection(object target, object value)
	{
		MethodInfo? add = target.GetType()
			.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			.FirstOrDefault(static method => method.Name == nameof(List<object>.Add) && method.GetParameters().Length == 1);
		if (add == null)
		{
			throw new InvalidOperationException($"Combat tracking collection {target.GetType()} does not expose Add().");
		}

		add.Invoke(target, [value]);
	}

	private sealed class CombatTrackingSnapshot
	{
		public Dictionary<uint, int> SlapProcsThisTurn { get; set; } = new();
		public Dictionary<uint, int> TormentorProcsThisTurn { get; set; } = new();
		public Dictionary<uint, int> CourageProcsThisTurn { get; set; } = new();
		public Dictionary<uint, int> BloodPactProcsThisTurn { get; set; } = new();
		public Dictionary<uint, int> BloodArmorHpLossThisPlayerTurn { get; set; } = new();
		public Dictionary<uint, int> ClownCollegeProcsThisTurn { get; set; } = new();
		public List<uint> EscapePlanTriggered { get; set; } = [];
		public List<uint> EscapePlanPending { get; set; } = [];
		public List<uint> RepulsorTriggered { get; set; } = [];
		public List<uint> RepulsorPending { get; set; } = [];
		public List<uint> DawnTriggered { get; set; } = [];
		public List<uint> NearDeathFeastTriggered { get; set; } = [];
		public List<uint> SpeedDemonPending { get; set; } = [];
		public Dictionary<uint, int> DelayedEnemyHealingBlock { get; set; } = new();
		public List<uint> DevilsDanceTriggeredThisTurn { get; set; } = [];
		public List<uint> FinalFormTriggeredThisTurn { get; set; } = [];
		public List<uint> FeelTheBurnTriggered { get; set; } = [];
		public Dictionary<uint, uint> FeyMagicPendingNoDrawPlayers { get; set; } = new();
		public Dictionary<uint, int> MikaelsBlessingTriggers { get; set; } = new();
		public List<uint> GoliathApplied { get; set; } = [];
		public List<uint> ProtectiveVeilApplied { get; set; } = [];
		public List<uint> ThornmailApplied { get; set; } = [];
		public List<uint> SuperBrainApplied { get; set; } = [];
		public List<uint> AstralBodyApplied { get; set; } = [];
		public List<uint> DrawYourSwordApplied { get; set; } = [];
		public List<uint> MadScientistApplied { get; set; } = [];
		public List<uint> UnmovableMountainApplied { get; set; } = [];
		public List<uint> GoldenSpatulaApplied { get; set; } = [];
		public List<uint> DoormakerRealStartApplied { get; set; } = [];
		public Dictionary<uint, int> TestSubjectPhaseStartApplied { get; set; } = new();
		public Dictionary<uint, int> TankEngineStacks { get; set; } = new();
		public Dictionary<uint, int> TankEngineLastAppliedRound { get; set; } = new();
		public Dictionary<uint, int> ShrinkEngineStacks { get; set; } = new();
		public Dictionary<uint, int> GetExcitedPending { get; set; } = new();
		public List<uint> FeelTheBurnPending { get; set; } = [];
		public List<uint> MountainSoulHasPreviousTurn { get; set; } = [];
		public List<uint> MountainSoulDamagedSinceLastTurn { get; set; } = [];
		public Dictionary<ulong, int> PlayerAttackCardsPlayedThisTurn { get; set; } = new();
		public Dictionary<ulong, int> PlayerCardsDrawnThisCombat { get; set; } = new();
		public List<ulong> VakuuControlledPlayersThisCombat { get; set; } = [];
		public List<ulong> EightPennyGatePlayersTriggeredThisTurn { get; set; } = [];
		public int EnemyProtectiveVeilTurnCounter { get; set; }
	}
}
