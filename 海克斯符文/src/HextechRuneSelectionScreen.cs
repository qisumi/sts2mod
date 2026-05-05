using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;

namespace HextechRunes;

internal sealed class HextechEnemyHexAdjustmentOptions
{
	public MonsterHexKind? InitialHex { get; init; }

	public bool ControlsEnabled { get; init; }

	public Func<MonsterHexKind?, int, MonsterHexKind?>? RerollFunc { get; init; }

	public Action<MonsterHexKind?, bool, int>? Changed { get; init; }

	public Action<HextechRuneSelectionScreen>? ScreenCreated { get; init; }
}

internal sealed partial class HextechRuneSelectionScreen : Control, IOverlayScreen, IScreenContext
{
	private const string LocTable = "relic_collection";
	private const string RerollIconPath = "res://HextechRunes/images/ui/hextechReroll.png";

	private readonly TaskCompletionSource<IEnumerable<RelicModel>> _completionSource = new();
	private readonly Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>>? _rerollFunc;
	private readonly Func<MonsterHexKind?, int, MonsterHexKind?>? _enemyHexRerollFunc;
	private readonly Action<MonsterHexKind?, bool, int>? _enemyHexChanged;
	private List<RelicModel> _relics;
	private MonsterHexKind? _monsterHexKind;
	private MonsterHexKind? _monsterHexBeforeRemoval;
	private RelicModel? _monsterHexRelic;
	private readonly string _rarityKey;
	private readonly List<Button> _holders = new();
	private readonly List<Button> _rerollButtons = new();
	private readonly List<bool> _rerolledSlots = new();
	private readonly List<int> _rerollHistory = new();
	private readonly bool _enemyHexControlsEnabled;
	private HBoxContainer? _cardsRow;
	private VBoxContainer? _enemyPreviewHost;
	private MegaLabel? _statusLabel;
	private bool _choiceLocked;
	private bool _enemyHexRemoved;
	private int _enemyHexRerollCount;
	private bool _restoreAfterMapReopenQueued;
	private bool _closed;

	public NetScreenType ScreenType => NetScreenType.Rewards;

	public bool UseSharedBackstop => true;

	public Control? DefaultFocusedControl => _holders.FirstOrDefault();

	public bool RequestedReroll => false;

	public IReadOnlyList<RelicModel> CurrentRelics => _relics;

	public IReadOnlyList<int> RerollHistory => _rerollHistory;

	public MonsterHexKind? CurrentMonsterHex => _enemyHexRemoved ? null : _monsterHexKind;

	public bool EnemyHexRemoved => _enemyHexRemoved;

	public int EnemyHexRerollCount => _enemyHexRerollCount;

	private HextechRuneSelectionScreen(
		IReadOnlyList<RelicModel> relics,
		RelicModel? monsterHexRelic,
		Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>>? rerollFunc,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions)
	{
		_relics = relics.ToList();
		_rerollFunc = rerollFunc;
		_enemyHexRerollFunc = enemyHexOptions?.RerollFunc;
		_enemyHexChanged = enemyHexOptions?.Changed;
		_enemyHexControlsEnabled = enemyHexOptions?.ControlsEnabled == true || enemyHexOptions?.RerollFunc != null;
		_monsterHexKind = enemyHexOptions?.InitialHex;
		if (_monsterHexKind == null && monsterHexRelic != null && MonsterHexCatalog.TryGetMonsterHexKind(monsterHexRelic, out MonsterHexKind monsterHexKind))
		{
			_monsterHexKind = monsterHexKind;
		}
		_monsterHexRelic = CreateMonsterHexRelic(_monsterHexKind) ?? monsterHexRelic;
		_rarityKey = DetermineRarityKey(relics);
		Name = nameof(HextechRuneSelectionScreen);
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		FocusMode = FocusModeEnum.All;
		FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Enabled;
		Visible = true;
		BuildUi();
	}

	public static HextechRuneSelectionScreen Create(
		IReadOnlyList<RelicModel> relics,
		RelicModel? monsterHexRelic,
		Func<IReadOnlyList<RelicModel>, int, int, IReadOnlyList<RelicModel>>? rerollFunc = null,
		HextechEnemyHexAdjustmentOptions? enemyHexOptions = null)
	{
		Log.Info($"[{ModInfo.Id}][Mayhem] SelectionScreen.Create: count={relics.Count}");
		return new HextechRuneSelectionScreen(relics, monsterHexRelic, rerollFunc, enemyHexOptions);
	}

	public override void _ExitTree()
	{
		_completionSource.TrySetResult(Array.Empty<RelicModel>());
		base._ExitTree();
	}

	private static RelicModel? CreateMonsterHexRelic(MonsterHexKind? monsterHex)
	{
		return monsterHex.HasValue
			? MonsterHexCatalog.GetIconRelicForMonsterHex(monsterHex.Value).ToMutable()
			: null;
	}
}
