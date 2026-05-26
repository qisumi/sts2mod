using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace CustomDifficulty;

internal static class CustomDifficultyPanel
{
	private const string PanelName = "CustomDifficultyPanel";
	private const float PreferredPanelWidth = 540f;
	private const float MinimumPanelWidth = 360f;
	private const float PanelHeight = 142f;
	private const float PanelMargin = 16f;

	private static PanelContainer? _root;
	private static HSlider? _hpSlider;
	private static HSlider? _attackSlider;
	private static Label? _hpValueLabel;
	private static Label? _attackValueLabel;
	private static Label? _statusLabel;
	private static bool _refreshing;

	public static void Inject(NCharacterSelectScreen screen)
	{
		try
		{
			RemoveFrom(screen);
			_root = BuildPanel();
			screen.AddChild(_root);
			screen.MoveChild(_root, screen.GetChildCount() - 1);
			PlacePanel(_root);
			Refresh();
			CustomDifficultySettings.Changed -= Refresh;
			CustomDifficultySettings.Changed += Refresh;
			Log.Info($"[{ModInfo.Id}] Character select panel injected.");
		}
		catch (Exception ex)
		{
			Log.Warn($"[{ModInfo.Id}] Failed to inject character select panel: {ex}");
		}
	}

	public static void RemoveFrom(Node node)
	{
		foreach (Node child in node.GetChildren())
		{
			if (child.Name == PanelName)
			{
				node.RemoveChild(child);
				child.QueueFree();
			}
		}

		if (_root != null && !GodotObject.IsInstanceValid(_root))
		{
			ClearReferences();
		}
	}

	public static void Refresh()
	{
		if (_root == null || !GodotObject.IsInstanceValid(_root))
		{
			ClearReferences();
			return;
		}

		_refreshing = true;
		try
		{
			bool canEdit = CustomDifficultySync.CanLocalEdit;
			RefreshSlider(_hpSlider, CustomDifficultySettings.MonsterHpSliderValue, canEdit);
			RefreshSlider(_attackSlider, CustomDifficultySettings.MonsterAttackSliderValue, canEdit);
			if (_hpValueLabel != null)
			{
				_hpValueLabel.Text = CustomDifficultySettings.FormatMultiplier(CustomDifficultySettings.MonsterHpTicks);
			}
			if (_attackValueLabel != null)
			{
				_attackValueLabel.Text = CustomDifficultySettings.FormatMultiplier(CustomDifficultySettings.MonsterAttackTicks);
			}
			if (_statusLabel != null)
			{
				_statusLabel.Text = GetStatusText();
				_statusLabel.AddThemeColorOverride("font_color", canEdit ? StsColors.green : StsColors.gray);
			}
		}
		finally
		{
			_refreshing = false;
		}
	}

	private static PanelContainer BuildPanel()
	{
		PanelContainer panel = new()
			{
				Name = PanelName,
				MouseFilter = Control.MouseFilterEnum.Stop,
				ZIndex = 360,
				AnchorLeft = 0f,
				AnchorTop = 0f,
				AnchorRight = 0f,
				AnchorBottom = 0f,
				CustomMinimumSize = new Vector2(PreferredPanelWidth, PanelHeight),
				Size = new Vector2(PreferredPanelWidth, PanelHeight)
			};
		ApplyPanelStyle(panel);

		MarginContainer margin = new()
		{
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		margin.AddThemeConstantOverride("margin_left", 14);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 14);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		panel.AddChild(margin);

		VBoxContainer stack = new()
		{
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		stack.AddThemeConstantOverride("separation", 8);
		margin.AddChild(stack);

		Label title = CreateLabel(ModInfo.Name, 19, StsColors.gold);
		stack.AddChild(title);

		_hpSlider = CreateSlider();
		_hpValueLabel = CreateValueLabel();
		stack.AddChild(CreateSliderRow("怪物血量", _hpSlider, _hpValueLabel));

		_attackSlider = CreateSlider();
		_attackValueLabel = CreateValueLabel();
		stack.AddChild(CreateSliderRow("怪物攻击", _attackSlider, _attackValueLabel));

		_statusLabel = CreateLabel("", 13, StsColors.green);
		stack.AddChild(_statusLabel);

		_hpSlider.Connect(Godot.Range.SignalName.ValueChanged, Callable.From<double>(OnHpValueChanged));
		_attackSlider.Connect(Godot.Range.SignalName.ValueChanged, Callable.From<double>(OnAttackValueChanged));
		return panel;
	}

	private static HBoxContainer CreateSliderRow(string labelText, HSlider slider, Label valueLabel)
	{
		HBoxContainer row = new()
		{
			MouseFilter = Control.MouseFilterEnum.Pass
		};
		row.AddThemeConstantOverride("separation", 10);

		Label label = CreateLabel(labelText, 15, StsColors.cream);
		label.CustomMinimumSize = new Vector2(82f, 26f);
		row.AddChild(label);
		row.AddChild(slider);
		row.AddChild(valueLabel);
		return row;
	}

		private static HSlider CreateSlider()
		{
			return new HSlider
			{
			MinValue = 0.1,
			MaxValue = 5.0,
			Step = 0.1,
			Value = 1.0,
				CustomMinimumSize = new Vector2(320f, 26f),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				MouseFilter = Control.MouseFilterEnum.Stop,
				FocusMode = Control.FocusModeEnum.All
		};
	}

	private static Label CreateValueLabel()
	{
		Label label = CreateLabel("x1.0", 15, StsColors.gold);
		label.HorizontalAlignment = HorizontalAlignment.Right;
		label.CustomMinimumSize = new Vector2(48f, 26f);
		return label;
	}

	private static Label CreateLabel(string text, int fontSize, Color color)
	{
		Label label = new()
		{
			Text = text,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", color);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.72f));
		label.AddThemeConstantOverride("outline_size", 2);
		return label;
	}

	private static void RefreshSlider(HSlider? slider, double value, bool canEdit)
	{
		if (slider == null)
		{
			return;
		}

		slider.Value = value;
		slider.Editable = canEdit;
		slider.FocusMode = canEdit ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
		slider.MouseFilter = canEdit ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		slider.Modulate = canEdit ? Colors.White : new Color(0.68f, 0.68f, 0.68f, 0.8f);
	}

	private static void OnHpValueChanged(double value)
	{
		if (_refreshing || !CustomDifficultySync.CanLocalEdit)
		{
			return;
		}

		CustomDifficultySettings.SetLocal(
			CustomDifficultySettings.SliderValueToTicks(value),
			CustomDifficultySettings.MonsterAttackTicks,
			broadcast: true);
	}

	private static void OnAttackValueChanged(double value)
	{
		if (_refreshing || !CustomDifficultySync.CanLocalEdit)
		{
			return;
		}

		CustomDifficultySettings.SetLocal(
			CustomDifficultySettings.MonsterHpTicks,
			CustomDifficultySettings.SliderValueToTicks(value),
			broadcast: true);
	}

	private static string GetStatusText()
	{
		return CustomDifficultySync.CurrentGameType switch
		{
			NetGameType.Host => "房主可调整；会同步给加入玩家",
			NetGameType.Client => "仅房主可调整",
			_ => "单人模式可调整"
		};
	}

		private static void PlacePanel(PanelContainer panel)
		{
			Vector2 viewportSize = panel.GetViewportRect().Size;
			float availableWidth = Math.Max(MinimumPanelWidth, viewportSize.X - PanelMargin * 2f);
			float panelWidth = Math.Min(PreferredPanelWidth, availableWidth);
			panel.CustomMinimumSize = new Vector2(panelWidth, PanelHeight);
			panel.Size = new Vector2(panelWidth, PanelHeight);
			panel.Position = new Vector2(
				Math.Max(PanelMargin, (viewportSize.X - panelWidth) / 2f),
				PanelMargin);
		}

	private static void ApplyPanelStyle(PanelContainer panel)
	{
		StyleBoxFlat style = new()
		{
			BgColor = new Color(0.045f, 0.052f, 0.064f, 0.84f),
			BorderColor = new Color(0.86f, 0.68f, 0.28f, 0.7f)
		};
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(6);
		panel.AddThemeStyleboxOverride("panel", style);
	}

	private static void ClearReferences()
	{
		_root = null;
		_hpSlider = null;
		_attackSlider = null;
		_hpValueLabel = null;
		_attackValueLabel = null;
		_statusLabel = null;
	}
}
