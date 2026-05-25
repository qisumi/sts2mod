using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace RemoveHandLimit;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
	private const string HarmonyId = "Natsuki.RemoveHandLimit";

	private const int HandLimit = 20;

	private const int RowLimit = 10;

	private const float UpperRowYOffset = -72f;

	private const float LowerRowYOffset = 28f;

	private const float UpperRowAngleFactor = 0.82f;

	private const float UpperRowHitboxHeightFactor = 0.56f;

	private const int HoverTipZIndex = 1000;

	private static Harmony? _harmony;

	private static readonly Dictionary<ulong, Rect2> OriginalHitboxRects = new();

	private static readonly HashSet<ulong> HoveredHolderIds = new();

	private static bool _loggedSkippedBlockedHoverTipAlignment;

	private static readonly FieldInfo SelectCardShortcutsField = RequireField(typeof(NPlayerHand), "_selectCardShortcuts", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo DraggedHolderIndexField = RequireField(typeof(NPlayerHand), "_draggedHolderIndex", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo HoldersAwaitingQueueField = RequireField(typeof(NPlayerHand), "_holdersAwaitingQueue", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo CurrentCardPlayField = RequireField(typeof(NPlayerHand), "_currentCardPlay", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo LastFocusedHolderIndexField = RequireField(typeof(NPlayerHand), "_lastFocusedHolderIdx", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo HoverTipOwnerField = RequireField(typeof(NHoverTipSet), "_owner", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly FieldInfo HandPosCardPositionDataField = RequireField(typeof(HandPosHelper), "_cardPositionData", BindingFlags.Static | BindingFlags.NonPublic);

	private static readonly FieldInfo HandPosCardAngleDataField = RequireField(typeof(HandPosHelper), "_cardAngleData", BindingFlags.Static | BindingFlags.NonPublic);

	private static readonly FieldInfo HandPosBaseScaleField = RequireField(typeof(HandPosHelper), "_baseScale", BindingFlags.Static | BindingFlags.NonPublic);

	private static readonly Vector2[][] VanillaCardPositionData = GetStaticFieldValue<Vector2[][]>(HandPosCardPositionDataField);

	private static readonly float[][] VanillaCardAngleData = GetStaticFieldValue<float[][]>(HandPosCardAngleDataField);

	private static readonly Vector2 VanillaBaseScale = GetStaticFieldValue<Vector2>(HandPosBaseScaleField);

	private static readonly MethodInfo RefreshLayoutMethod = RequireMethod(typeof(NPlayerHand), "RefreshLayout", BindingFlags.Instance | BindingFlags.NonPublic);

	private static readonly MethodInfo ReturnHolderToHandMethod = RequireMethod(typeof(NPlayerHand), "ReturnHolderToHand", BindingFlags.Instance | BindingFlags.NonPublic, typeof(NHandCardHolder));

	private static readonly PropertyInfo FocusedHolderProperty = RequireProperty(typeof(NPlayerHand), nameof(NPlayerHand.FocusedHolder), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	public static void Initialize()
	{
		InstallHooks();
		Log.Info($"[RemoveHandLimit] Loaded. Hand limit patched to {HandLimit} with two 10-card rows.");
	}

	private static void InstallHooks()
	{
		Harmony harmony = _harmony ??= new Harmony(HarmonyId);
		MethodInfo addCardsMethod = RequireMethod(
			typeof(CardPileCmd),
			nameof(CardPileCmd.Add),
			BindingFlags.Public | BindingFlags.Static,
			typeof(IEnumerable<CardModel>),
			typeof(CardPile),
			typeof(CardPilePosition),
			typeof(AbstractModel),
			typeof(bool));
		MethodInfo drawMethod = RequireMethod(
			typeof(CardPileCmd),
			nameof(CardPileCmd.Draw),
			BindingFlags.Public | BindingFlags.Static,
			typeof(PlayerChoiceContext),
			typeof(decimal),
			typeof(Player),
			typeof(bool));
		MethodInfo checkCanDrawMethod = RequireMethod(
			typeof(CardPileCmd),
			"CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot",
			BindingFlags.NonPublic | BindingFlags.Static,
			typeof(Player));
		MethodInfo handPosGetPosition = RequireMethod(
			typeof(HandPosHelper),
			nameof(HandPosHelper.GetPosition),
			BindingFlags.Public | BindingFlags.Static,
			typeof(int),
			typeof(int));
		MethodInfo handPosGetAngle = RequireMethod(
			typeof(HandPosHelper),
			nameof(HandPosHelper.GetAngle),
			BindingFlags.Public | BindingFlags.Static,
			typeof(int),
			typeof(int));
		MethodInfo handPosGetScale = RequireMethod(
			typeof(HandPosHelper),
			nameof(HandPosHelper.GetScale),
			BindingFlags.Public | BindingFlags.Static,
			typeof(int));
		MethodInfo refreshLayoutMethod = RequireMethod(
			typeof(NPlayerHand),
			"RefreshLayout",
			BindingFlags.Instance | BindingFlags.NonPublic);
		MethodInfo handCardHoverMethod = RequireMethod(
			typeof(NHandCardHolder),
			"DoCardHoverEffects",
			BindingFlags.Instance | BindingFlags.NonPublic,
			typeof(bool));
		MethodInfo onHolderFocusedMethod = RequireMethod(
			typeof(NPlayerHand),
			"OnHolderFocused",
			BindingFlags.Instance | BindingFlags.NonPublic,
			typeof(NHandCardHolder));
		MethodInfo onHolderUnfocusedMethod = RequireMethod(
			typeof(NPlayerHand),
			"OnHolderUnfocused",
			BindingFlags.Instance | BindingFlags.NonPublic,
			typeof(NHandCardHolder));
		MethodInfo hoverTipAlignmentMethod = RequireMethod(
			typeof(NHoverTipSet),
			nameof(NHoverTipSet.SetAlignmentForCardHolder),
			BindingFlags.Instance | BindingFlags.Public,
			typeof(NCardHolder));
		MethodInfo startCardPlayMethod = RequireMethod(
			typeof(NPlayerHand),
			"StartCardPlay",
			BindingFlags.Instance | BindingFlags.NonPublic,
			typeof(NHandCardHolder),
			typeof(bool));

		harmony.Patch(GetAsyncStateMachineTarget(addCardsMethod), transpiler: new HarmonyMethod(typeof(ModEntry), nameof(PatchAddCardsTranspiler)));
		harmony.Patch(GetAsyncStateMachineTarget(drawMethod), transpiler: new HarmonyMethod(typeof(ModEntry), nameof(PatchDrawTranspiler)));
		harmony.Patch(checkCanDrawMethod, transpiler: new HarmonyMethod(typeof(ModEntry), nameof(PatchCheckCanDrawTranspiler)));
		harmony.Patch(handPosGetPosition, prefix: new HarmonyMethod(typeof(ModEntry), nameof(GetPositionPrefix)));
		harmony.Patch(handPosGetAngle, prefix: new HarmonyMethod(typeof(ModEntry), nameof(GetAnglePrefix)));
		harmony.Patch(handPosGetScale, prefix: new HarmonyMethod(typeof(ModEntry), nameof(GetScalePrefix)));
		harmony.Patch(
			refreshLayoutMethod,
			prefix: new HarmonyMethod(typeof(ModEntry), nameof(RefreshLayoutPrefix)),
			postfix: new HarmonyMethod(typeof(ModEntry), nameof(RefreshLayoutPostfix)));
		harmony.Patch(
			handCardHoverMethod,
			prefix: new HarmonyMethod(typeof(ModEntry), nameof(DoCardHoverEffectsPrefix)),
			postfix: new HarmonyMethod(typeof(ModEntry), nameof(DoCardHoverEffectsPostfix)));
		harmony.Patch(onHolderFocusedMethod, prefix: new HarmonyMethod(typeof(ModEntry), nameof(OnHolderFocusedPrefix)));
		harmony.Patch(onHolderUnfocusedMethod, prefix: new HarmonyMethod(typeof(ModEntry), nameof(OnHolderUnfocusedPrefix)));
		harmony.Patch(
			hoverTipAlignmentMethod,
			prefix: new HarmonyMethod(typeof(ModEntry), nameof(SetAlignmentForCardHolderPrefix)),
			postfix: new HarmonyMethod(typeof(ModEntry), nameof(SetAlignmentForCardHolderPostfix)));
		harmony.Patch(startCardPlayMethod, prefix: new HarmonyMethod(typeof(ModEntry), nameof(StartCardPlayPrefix)));
	}

	private static IEnumerable<CodeInstruction> PatchAddCardsTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		return ReplaceIntLoads(instructions, expectedCount: 1, __originalMethod);
	}

	private static IEnumerable<CodeInstruction> PatchDrawTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		return ReplaceIntLoads(instructions, expectedCount: 3, __originalMethod);
	}

	private static IEnumerable<CodeInstruction> PatchCheckCanDrawTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
	{
		return ReplaceIntLoads(instructions, expectedCount: 1, __originalMethod);
	}

	private static IEnumerable<CodeInstruction> ReplaceIntLoads(IEnumerable<CodeInstruction> instructions, int expectedCount, MethodBase originalMethod)
	{
		List<CodeInstruction> patchedInstructions = instructions.ToList();
		int patched = 0;
		foreach (CodeInstruction instruction in patchedInstructions)
		{
			if (!TryGetLoadedInt32(instruction, out int value) || value != 10)
			{
				continue;
			}

			instruction.opcode = OpCodes.Ldc_I4;
			instruction.operand = HandLimit;
			patched++;
		}

		if (patched != expectedCount)
		{
			throw new InvalidOperationException($"[RemoveHandLimit] Expected to patch {expectedCount} int loads in {originalMethod.FullDescription()}, but patched {patched}.");
		}

		return patchedInstructions;
	}

	private static bool TryGetLoadedInt32(CodeInstruction instruction, out int value)
	{
		OpCode opcode = instruction.opcode;
		if (opcode == OpCodes.Ldc_I4_M1)
		{
			value = -1;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_0)
		{
			value = 0;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_1)
		{
			value = 1;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_2)
		{
			value = 2;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_3)
		{
			value = 3;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_4)
		{
			value = 4;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_5)
		{
			value = 5;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_6)
		{
			value = 6;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_7)
		{
			value = 7;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_8)
		{
			value = 8;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte shortValue)
		{
			value = shortValue;
			return true;
		}

		if (opcode == OpCodes.Ldc_I4 && instruction.operand is int intValue)
		{
			value = intValue;
			return true;
		}

		value = 0;
		return false;
	}

	private static bool GetPositionPrefix(int handSize, int cardIndex, ref Vector2 __result)
	{
		if (handSize <= RowLimit)
		{
			return true;
		}

		ValidateHandIndex(handSize, cardIndex);
		if (cardIndex < RowLimit)
		{
			Vector2 basePosition = GetVanillaPosition(RowLimit, cardIndex);
			__result = new Vector2(basePosition.X, basePosition.Y + LowerRowYOffset);
			return false;
		}

		int upperRowCount = handSize - RowLimit;
		int upperRowIndex = cardIndex - RowLimit;
		Vector2 upperBasePosition = GetVanillaPosition(upperRowCount, upperRowIndex);
		__result = new Vector2(upperBasePosition.X, upperBasePosition.Y + UpperRowYOffset);
		return false;
	}

	private static bool GetAnglePrefix(int handSize, int cardIndex, ref float __result)
	{
		if (handSize <= RowLimit)
		{
			return true;
		}

		ValidateHandIndex(handSize, cardIndex);
		if (cardIndex < RowLimit)
		{
			__result = GetVanillaAngle(RowLimit, cardIndex);
			return false;
		}

		int upperRowCount = handSize - RowLimit;
		int upperRowIndex = cardIndex - RowLimit;
		__result = GetVanillaAngle(upperRowCount, upperRowIndex) * UpperRowAngleFactor;
		return false;
	}

	private static bool GetScalePrefix(int handSize, ref Vector2 __result)
	{
		if (handSize <= RowLimit)
		{
			return true;
		}

		__result = GetVanillaScale(RowLimit);
		return false;
	}

	private static Vector2 GetVanillaPosition(int handSize, int cardIndex)
	{
		return VanillaCardPositionData[handSize - 1][cardIndex];
	}

	private static float GetVanillaAngle(int handSize, int cardIndex)
	{
		return VanillaCardAngleData[handSize - 1][cardIndex];
	}

	private static Vector2 GetVanillaScale(int handSize)
	{
		float multiplier = handSize switch
		{
			8 => 0.95f,
			9 => 0.9f,
			10 => 0.85f,
			11 => 0.8f,
			12 => 0.75f,
			_ => 1f
		};
		return VanillaBaseScale * multiplier;
	}

	private static bool RefreshLayoutPrefix(NPlayerHand __instance)
	{
		if (__instance.ActiveHolders.Count > RowLimit)
		{
			SetFocusedHolder(__instance, null);
		}

		return true;
	}

	private static void RefreshLayoutPostfix(NPlayerHand __instance)
	{
		UpdateHolderZIndices(__instance);
	}

	private static void DoCardHoverEffectsPrefix(NHandCardHolder __instance, bool isHovered)
	{
		SetHolderHoverState(__instance, isHovered);
	}

	private static void DoCardHoverEffectsPostfix(NHandCardHolder __instance, bool isHovered)
	{
		ApplyHolderZIndex(__instance, isHovered);
	}

	private static bool OnHolderFocusedPrefix(NPlayerHand __instance, NHandCardHolder holder)
	{
		if (__instance.ActiveHolders.Count <= RowLimit)
		{
			return true;
		}

		SetHolderHoverState(holder, isHovered: true);
		SetLastFocusedHolderIndex(__instance, holder.GetIndex());
		if (holder.CardModel != null)
		{
			RunManager.Instance.HoveredModelTracker.OnLocalCardHovered(holder.CardModel);
		}
		UpdateHolderZIndices(__instance);
		return false;
	}

	private static bool OnHolderUnfocusedPrefix(NPlayerHand __instance, NHandCardHolder holder)
	{
		if (__instance.ActiveHolders.Count <= RowLimit)
		{
			return true;
		}

		SetHolderHoverState(holder, isHovered: false);
		RunManager.Instance.HoveredModelTracker.OnLocalCardUnhovered();
		UpdateHolderZIndices(__instance);
		return false;
	}

	private static bool SetAlignmentForCardHolderPrefix(NHoverTipSet __instance, NCardHolder holder, out bool __state)
	{
		__state = CanAlignCardHoverTipSet(__instance, holder);
		if (!__state)
		{
			LogSkippedBlockedHoverTipAlignment();
		}

		return __state;
	}

	private static void SetAlignmentForCardHolderPostfix(NHoverTipSet __instance, bool __state)
	{
		if (__state)
		{
			BringHoverTipToFront(__instance);
		}
	}

	private static bool StartCardPlayPrefix(NPlayerHand __instance, NHandCardHolder holder, bool startedViaShortcut)
	{
		StringName[] shortcuts = GetSelectCardShortcuts(__instance);
		int holderIndex = holder.GetIndex();
		if ((NControllerManager.Instance?.IsUsingController ?? false) || holderIndex < shortcuts.Length)
		{
			return true;
		}

		SetDraggedHolderIndex(__instance, holderIndex);
		GetHoldersAwaitingQueue(__instance).Add(holder, holderIndex);
		holder.Reparent(__instance);
		holder.BeginDrag();

		NCardPlay currentCardPlay = NMouseCardPlay.Create(holder, MegaInput.releaseCard, startedViaShortcut);
		SetCurrentCardPlay(__instance, currentCardPlay);
		__instance.AddChildSafely(currentCardPlay);
		currentCardPlay.Connect(NCardPlay.SignalName.Finished, Callable.From(delegate(bool success)
		{
			RunManager.Instance.HoveredModelTracker.OnLocalCardDeselected();
			if (!success)
			{
				InvokeReturnHolderToHand(__instance, holder);
			}

			SetDraggedHolderIndex(__instance, -1);
			InvokeRefreshLayout(__instance);
		}));

		CardModel selectedCard = holder.CardNode?.Model ?? throw new InvalidOperationException("[RemoveHandLimit] Tried to start card play without a card node.");
		RunManager.Instance.HoveredModelTracker.OnLocalCardSelected(selectedCard);
		currentCardPlay.Start();
		InvokeRefreshLayout(__instance);
		holder.SetIndexLabel(holderIndex + 1);
		return false;
	}

	private static T GetStaticFieldValue<T>(FieldInfo fieldInfo)
	{
		return fieldInfo.GetValue(null) is T value
			? value
			: throw new InvalidOperationException($"[RemoveHandLimit] Could not read field {fieldInfo.DeclaringType?.FullName}.{fieldInfo.Name}.");
	}

	private static bool CanAlignCardHoverTipSet(NHoverTipSet hoverTipSet, NCardHolder holder)
	{
		if (NHoverTipSet.shouldBlockHoverTips
			|| !GodotObject.IsInstanceValid(hoverTipSet)
			|| !GodotObject.IsInstanceValid(holder))
		{
			return false;
		}

		if (HoverTipOwnerField.GetValue(hoverTipSet) is not Control owner || !GodotObject.IsInstanceValid(owner))
		{
			return false;
		}

		Control? hitbox = holder.Hitbox;
		return hitbox != null && GodotObject.IsInstanceValid(hitbox);
	}

	private static void LogSkippedBlockedHoverTipAlignment()
	{
		if (_loggedSkippedBlockedHoverTipAlignment)
		{
			return;
		}

		_loggedSkippedBlockedHoverTipAlignment = true;
		Log.Warn("[RemoveHandLimit] Skipped card hover-tip alignment while the hover tip set was blocked or detached.", 1);
	}

	private static StringName[] GetSelectCardShortcuts(NPlayerHand hand)
	{
		return (StringName[])(SelectCardShortcutsField.GetValue(hand) ?? Array.Empty<StringName>());
	}

	private static Dictionary<NHandCardHolder, int> GetHoldersAwaitingQueue(NPlayerHand hand)
	{
		return (Dictionary<NHandCardHolder, int>)(HoldersAwaitingQueueField.GetValue(hand)
			?? throw new InvalidOperationException("[RemoveHandLimit] Could not access _holdersAwaitingQueue."));
	}

	private static void SetDraggedHolderIndex(NPlayerHand hand, int value)
	{
		DraggedHolderIndexField.SetValue(hand, value);
	}

	private static void SetCurrentCardPlay(NPlayerHand hand, NCardPlay cardPlay)
	{
		CurrentCardPlayField.SetValue(hand, cardPlay);
	}

	private static void SetLastFocusedHolderIndex(NPlayerHand hand, int value)
	{
		LastFocusedHolderIndexField.SetValue(hand, value);
	}

	private static void SetFocusedHolder(NPlayerHand hand, NHandCardHolder? holder)
	{
		FocusedHolderProperty.SetValue(hand, holder);
	}

	private static void InvokeRefreshLayout(NPlayerHand hand)
	{
		RefreshLayoutMethod.Invoke(hand, null);
	}

	private static void InvokeReturnHolderToHand(NPlayerHand hand, NHandCardHolder holder)
	{
		ReturnHolderToHandMethod.Invoke(hand, new object[] { holder });
	}

	private static void ValidateHandIndex(int handSize, int cardIndex)
	{
		if (handSize <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(handSize), handSize, "Hand size must be positive.");
		}

		if (handSize > HandLimit)
		{
			throw new ArgumentOutOfRangeException(nameof(handSize), handSize, $"Hand size {handSize} is greater than {HandLimit}.");
		}

		if (cardIndex < 0 || cardIndex >= handSize)
		{
			throw new ArgumentOutOfRangeException(nameof(cardIndex), cardIndex, $"Card index {cardIndex} is invalid for hand size {handSize}.");
		}
	}

	private static void UpdateHolderZIndices(NPlayerHand hand)
	{
		foreach (NHandCardHolder holder in hand.ActiveHolders)
		{
			ApplyHolderZIndex(holder, IsHolderHovered(holder));
			UpdateHolderHitbox(holder);
		}
	}

	private static void ApplyHolderZIndex(NHandCardHolder holder, bool isHovered)
	{
		if (holder.GetParent() is not Control parent || parent.GetParent() is not NPlayerHand hand)
		{
			return;
		}

		int activeCount = hand.ActiveHolders.Count;
		int holderIndex = holder.GetIndex();
		bool useTwoRows = activeCount > RowLimit;
		bool isLowerRow = !useTwoRows || holderIndex < RowLimit;
		int baseZ = isLowerRow ? 10 : 0;
		int visualZ = baseZ + (isHovered ? 100 : 0);
		holder.ZIndex = visualZ;
		holder.Hitbox.ZIndex = baseZ;
	}

	private static void SetHolderHoverState(NHandCardHolder holder, bool isHovered)
	{
		ulong holderId = holder.GetInstanceId();
		if (isHovered)
		{
			HoveredHolderIds.Add(holderId);
		}
		else
		{
			HoveredHolderIds.Remove(holderId);
		}
	}

	private static bool IsHolderHovered(NHandCardHolder holder)
	{
		return HoveredHolderIds.Contains(holder.GetInstanceId());
	}

	private static void BringHoverTipToFront(NHoverTipSet hoverTipSet)
	{
		if (NGame.Instance?.HoverTipsContainer is CanvasItem hoverTipsContainer)
		{
			hoverTipsContainer.ZIndex = HoverTipZIndex;
			hoverTipsContainer.MoveToFront();
		}

		hoverTipSet.ZIndex = HoverTipZIndex;
		hoverTipSet.MoveToFront();
	}

	private static void UpdateHolderHitbox(NHandCardHolder holder)
	{
		NClickableControl hitbox = holder.Hitbox;
		ulong hitboxId = hitbox.GetInstanceId();
		if (!OriginalHitboxRects.TryGetValue(hitboxId, out Rect2 originalRect))
		{
			originalRect = new Rect2(hitbox.Position, hitbox.Size);
			OriginalHitboxRects[hitboxId] = originalRect;
		}

		hitbox.Position = originalRect.Position;
		hitbox.Size = originalRect.Size;

		if (holder.GetParent() is not Control parent || parent.GetParent() is not NPlayerHand hand)
		{
			return;
		}

		int activeCount = hand.ActiveHolders.Count;
		if (activeCount <= RowLimit)
		{
			return;
		}

		int holderIndex = holder.GetIndex();
		bool isUpperRow = holderIndex >= RowLimit;
		if (!isUpperRow)
		{
			return;
		}

		hitbox.Size = new Vector2(originalRect.Size.X, originalRect.Size.Y * UpperRowHitboxHeightFactor);
	}

	private static MethodInfo RequireMethod(Type type, string name, BindingFlags bindingFlags, params Type[] parameterTypes)
	{
		MethodInfo? method = type.GetMethod(name, bindingFlags, null, parameterTypes, null);
		if (method == null)
		{
			throw new InvalidOperationException($"Could not find method {type.FullName}.{name}.");
		}

		return method;
	}

	private static PropertyInfo RequireProperty(Type type, string name, BindingFlags bindingFlags)
	{
		PropertyInfo? property = type.GetProperty(name, bindingFlags);
		if (property == null)
		{
			throw new InvalidOperationException($"Could not find property {type.FullName}.{name}.");
		}

		return property;
	}

	private static MethodBase GetAsyncStateMachineTarget(MethodInfo method)
	{
		AsyncStateMachineAttribute? attribute = method.GetCustomAttribute<AsyncStateMachineAttribute>();
		if (attribute?.StateMachineType == null)
		{
			return method;
		}

		MethodInfo? moveNext = attribute.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		if (moveNext == null)
		{
			throw new InvalidOperationException($"Could not find MoveNext on async state machine {attribute.StateMachineType.FullName}.");
		}

		return moveNext;
	}

	private static FieldInfo RequireField(Type type, string name, BindingFlags bindingFlags)
	{
		FieldInfo? field = type.GetField(name, bindingFlags);
		if (field == null)
		{
			throw new InvalidOperationException($"Could not find field {type.FullName}.{name}.");
		}

		return field;
	}
}
