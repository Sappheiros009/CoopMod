using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

public static class ShowUI
{
    private const string HarmonyId =
        "com.peak.coopmod.showui.localfeedback";

    private static Harmony harmony;

    private static ShowUIRuntime runtime;

    private static Item shadowUseItem;

    private static bool shadowUseSecondary;

    private static bool shadowUseHeld;

    private static float shadowUseStartTime;

    private static bool shadowDropHeld;

    private static float shadowDropStartTime;

    public static void Initialize(
        CoopMod plugin)
    {
        if (harmony != null ||
            plugin == null)
        {
            return;
        }

        harmony =
            new Harmony(
                HarmonyId);

        Patch(
            typeof(
                GUIManager_UpdateItems_Patch));

        Patch(
            typeof(
                GUIManager_UpdateItemPrompts_Patch));

        Patch(
            typeof(
                GUIManager_TestUpdateItemPrompts_Patch));

        Patch(
            typeof(
                GUIManager_UpdateThrow_Patch));

        Patch(
            typeof(
                GUIManager_UpdateRope_Patch));

        Patch(
            typeof(
                UI_UseItemProgress_UpdateFillAmount_Patch));

        Patch(
            typeof(
                BackpackWheel_InitWheel_Patch));

        Patch(
            typeof(
                BackpackWheel_Choose_Patch));

        Patch(
            typeof(
                BackpackWheel_Hover_Patch));

        Patch(
            typeof(
                BackpackWheelSlice_Hover_Patch));

        Patch(
            typeof(
                BackpackWheelSlice_InitStashSlot_Patch));

        Patch(
            typeof(
                Constructable_Update_Patch));

        runtime =
            plugin.gameObject
                .GetComponent<ShowUIRuntime>();

        if (runtime == null)
        {
            runtime =
                plugin.gameObject
                    .AddComponent<ShowUIRuntime>();
        }

        runtime.Activate();

        ResetShadowState();
    }

    public static void Shutdown()
    {
        ResetShadowState();

        if (runtime != null)
        {
            runtime.Deactivate();

            UnityEngine.Object.Destroy(
                runtime);

            runtime =
                null;
        }

        if (harmony == null)
        {
            return;
        }

        harmony.UnpatchSelf();

        harmony =
            null;
    }

    private static void Patch(
        Type patchType)
    {
        harmony
            .CreateClassProcessor(
                patchType)
            .Patch();
    }

    private static bool TryGetLocalPair(
        out Character climber,
        out Character carrier)
    {
        climber =
            Character.localCharacter;

        carrier =
            null;

        if (climber == null ||
            !climber.IsLocal ||
            !SeparateRole.IsClimber(
                climber) ||
            climber.data == null)
        {
            return false;
        }

        carrier =
            climber
                .data
                .carrier;

        return
            carrier != null &&
            carrier.data != null;
    }

    private static bool TryBeginCarrierItemView(
        out Character originalLocalCharacter)
    {
        originalLocalCharacter =
            null;

        Character climber;
        Character carrier;

        if (!TryGetLocalPair(
                out climber,
                out carrier))
        {
            return false;
        }

        originalLocalCharacter =
            climber;

        Character.localCharacter =
            carrier;

        return true;
    }

    private static Exception FinishCarrierItemView(
        Exception exception,
        Character originalLocalCharacter)
    {
        if (originalLocalCharacter !=
            null)
        {
            Character.localCharacter =
                originalLocalCharacter;
        }

        return
            exception;
    }

    private static bool IsPressed(
        InputAction action)
    {
        return
            action != null &&
            action.IsPressed();
    }

    private static void ResetShadowState()
    {
        shadowUseItem =
            null;

        shadowUseSecondary =
            false;

        shadowUseHeld =
            false;

        shadowUseStartTime =
            0f;

        shadowDropHeld =
            false;

        shadowDropStartTime =
            0f;
    }

    private static void ApplyCarrierInventoryUi(
        GUIManager gui)
    {
        if (gui == null)
        {
            return;
        }

        Character climber;
        Character carrier;

        if (!TryGetLocalPair(
                out climber,
                out carrier) ||
            carrier.player == null)
        {
            return;
        }

        Character original =
            Character.localCharacter;

        Character.localCharacter =
            carrier;

        try
        {
            if (gui.items != null)
            {
                for (int i = 0;
                    i <
                        gui.items.Length;
                    i++)
                {
                    InventoryItemUI itemUi =
                        gui.items[i];

                    if (itemUi == null)
                    {
                        continue;
                    }

                    if (i <
                        carrier
                            .player
                            .itemSlots
                            .Length)
                    {
                        itemUi.SetItem(
                            carrier
                                .player
                                .itemSlots[i]);
                    }
                    else
                    {
                        itemUi.Clear();
                    }
                }
            }

            if (gui.temporaryItem !=
                null)
            {
                ItemSlot temporarySlot =
                    carrier
                        .player
                        .GetItemSlot(
                            250);

                if (temporarySlot !=
                        null &&
                    !temporarySlot.IsEmpty())
                {
                    gui
                        .temporaryItem
                        .gameObject
                        .SetActive(
                            true);

                    gui
                        .temporaryItem
                        .SetItem(
                            temporarySlot);
                }
                else
                {
                    gui
                        .temporaryItem
                        .gameObject
                        .SetActive(
                            false);

                    gui
                        .temporaryItem
                        .Clear();
                }
            }

            if (gui.backpack !=
                null)
            {
                gui.backpack.SetItem(
                    carrier
                        .player
                        .backpackSlot);

                gui.backpack.SetSelected();
            }
        }
        finally
        {
            Character.localCharacter =
                original;
        }
    }

    private static float GetShadowUseDuration(
        Item item,
        bool secondary)
    {
        if (item == null)
        {
            return
                0f;
        }

        if (!secondary)
        {
            RopeTier ropeTier =
                item.GetComponent<RopeTier>();

            if (ropeTier != null &&
                ropeTier.castTime >
                    0f)
            {
                return
                    ropeTier.castTime;
            }

            return
                item.usingTimePrimary;
        }

        return
            item.totalSecondaryUsingTime;
    }

    private static bool RopePlacementIsLocallyValid(
        Item item,
        Character climber)
    {
        RopeTier ropeTier =
            item != null
                ? item.GetComponent<RopeTier>()
                : null;

        if (ropeTier == null ||
            MainCamera.instance == null ||
            climber == null)
        {
            return
                true;
        }

        Transform cameraTransform =
            MainCamera
                .instance
                .transform;

        Vector3 start =
            cameraTransform.position;

        RaycastHit hit =
            HelperFunctions.LineCheck(
                start,
                start +
                    cameraTransform.forward *
                    ropeTier.maxAnchorGhostDistance,
                HelperFunctions.LayerType.TerrainMap,
                0f,
                QueryTriggerInteraction.Ignore);

        if (hit.collider == null)
        {
            return
                false;
        }

        return
            Vector3.Distance(
                hit.point,
                climber.Center) <
            ropeTier.maxAnchorDistance;
    }

    private static bool TryGetShadowUseProgress(
        Character climber,
        Character carrier,
        Item item,
        out float progress)
    {
        progress =
            0f;

        if (item == null)
        {
            shadowUseHeld =
                false;

            shadowUseItem =
                null;

            return
                false;
        }

        bool primaryHeld =
            IsPressed(
                CharacterInput
                    .action_usePrimary);

        bool secondaryHeld =
            IsPressed(
                CharacterInput
                    .action_useSecondary);

        bool useHeld =
            primaryHeld ||
            secondaryHeld;

        bool secondary =
            !primaryHeld &&
            secondaryHeld;

        if (!useHeld)
        {
            shadowUseHeld =
                false;

            shadowUseItem =
                null;

            return
                false;
        }

        if (!RopePlacementIsLocallyValid(
                item,
                climber))
        {
            shadowUseHeld =
                false;

            shadowUseItem =
                null;

            return
                false;
        }

        Constructable constructable =
            item.GetComponent<Constructable>();

        if (constructable != null &&
            !item.CanUsePrimary())
        {
            shadowUseHeld =
                false;

            shadowUseItem =
                null;

            return
                false;
        }

        float duration =
            GetShadowUseDuration(
                item,
                secondary);

        if (duration <=
                0f ||
            !item.showUseProgress)
        {
            shadowUseHeld =
                false;

            shadowUseItem =
                null;

            return
                false;
        }

        if (!shadowUseHeld ||
            shadowUseItem !=
                item ||
            shadowUseSecondary !=
                secondary)
        {
            shadowUseHeld =
                true;

            shadowUseItem =
                item;

            shadowUseSecondary =
                secondary;

            shadowUseStartTime =
                Time.unscaledTime;
        }

        progress =
            Mathf.Clamp01(
                (
                    Time.unscaledTime -
                    shadowUseStartTime
                ) /
                duration);

        if (item.shouldShowCastProgress &&
            item.progress >
                progress)
        {
            progress =
                Mathf.Clamp01(
                    item.progress);
        }

        return
            progress >
            0f;
    }

    private static bool TryApplyCombinedProgress(
        UI_UseItemProgress ui,
        ref bool result)
    {
        Character climber;
        Character carrier;

        if (!TryGetLocalPair(
                out climber,
                out carrier) ||
            ui == null ||
            ui.fill == null)
        {
            return
                false;
        }

        Interaction interaction =
            Interaction.instance;

        if (interaction != null &&
            interaction.currentHeldInteractible !=
                null &&
            interaction.constantInteractableProgress >
                0f)
        {
            ui.fill.fillAmount =
                Mathf.Clamp01(
                    interaction
                        .constantInteractableProgress);

            result =
                true;

            return
                true;
        }

        if (carrier.refs != null &&
            carrier.refs.items != null &&
            carrier
                .refs
                .items
                .climbingSpikeCastProgress >
                    0f)
        {
            ui.fill.fillAmount =
                Mathf.Clamp01(
                    carrier
                        .refs
                        .items
                        .climbingSpikeCastProgress);

            result =
                true;

            return
                true;
        }

        Item item =
            carrier.data.currentItem;

        float shadowProgress;

        if (TryGetShadowUseProgress(
                climber,
                carrier,
                item,
                out shadowProgress))
        {
            ui.fill.fillAmount =
                shadowProgress;

            result =
                true;

            return
                true;
        }

        if (item != null &&
            item.shouldShowCastProgress &&
            item.progress >
                0f)
        {
            ui.fill.fillAmount =
                Mathf.Clamp01(
                    item.progress);

            result =
                true;

            return
                true;
        }

        result =
            false;

        return
            true;
    }

    private static void ApplyLocalDropFeedback(
        GUIManager gui)
    {
        Character climber;
        Character carrier;

        if (!TryGetLocalPair(
                out climber,
                out carrier) ||
            gui == null ||
            gui.throwGO == null ||
            carrier.refs == null ||
            carrier.refs.items == null)
        {
            shadowDropHeld =
                false;

            return;
        }

        Item item =
            carrier.data.currentItem;

        bool dropHeld =
            IsPressed(
                CharacterInput
                    .action_drop);

        bool useHeld =
            IsPressed(
                CharacterInput
                    .action_usePrimary) ||
            IsPressed(
                CharacterInput
                    .action_useSecondary);

        if (!dropHeld ||
            useHeld ||
            item == null ||
            !item.UIData.canDrop)
        {
            shadowDropHeld =
                false;

            gui.throwGO.SetActive(
                false);

            return;
        }

        if (!shadowDropHeld)
        {
            shadowDropHeld =
                true;

            shadowDropStartTime =
                Time.unscaledTime;
        }

        CharacterItems items =
            carrier.refs.items;

        float elapsed =
            Time.unscaledTime -
            shadowDropStartTime;

        float charge =
            0f;

        if (elapsed >
            items.delayBeforeThrowCharge)
        {
            float chargeTime =
                Mathf.Max(
                    0.01f,
                    items.throwChargeTime);

            charge =
                Mathf.Clamp01(
                    (
                        elapsed -
                        items.delayBeforeThrowCharge
                    ) /
                    chargeTime);
        }

        gui.throwGO.SetActive(
            charge >
            0f);

        if (charge <=
            0f)
        {
            return;
        }

        if (gui.throwBar !=
            null)
        {
            gui.throwBar.fillAmount =
                Mathf.Lerp(
                    0.692f,
                    0.808f,
                    charge);

            if (gui.throwGradient !=
                null)
            {
                gui.throwBar.color =
                    gui
                        .throwGradient
                        .Evaluate(
                            charge);
            }
        }
    }

    internal static void UpdateLocalConstructablePreview()
    {
        Character climber;
        Character carrier;

        if (!TryGetLocalPair(
                out climber,
                out carrier))
        {
            return;
        }

        Item item =
            carrier.data.currentItem;

        if (item == null)
        {
            return;
        }

        Constructable constructable =
            item.GetComponent<Constructable>();

        if (constructable == null)
        {
            return;
        }

        constructable.TryUpdatePreview();
    }

    private static bool ShouldOwnRemoteConstructablePreview(
        Constructable constructable)
    {
        if (constructable == null ||
            constructable.item == null)
        {
            return
                false;
        }

        Character climber;
        Character carrier;

        if (!TryGetLocalPair(
                out climber,
                out carrier))
        {
            return
                false;
        }

        return
            carrier.data.currentItem ==
            constructable.item;
    }

    [HarmonyPatch(
        typeof(GUIManager),
        nameof(
            GUIManager.UpdateItems))]
    private static class GUIManager_UpdateItems_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            GUIManager __instance)
        {
            ApplyCarrierInventoryUi(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(GUIManager),
        nameof(
            GUIManager.UpdateItemPrompts))]
    private static class GUIManager_UpdateItemPrompts_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginCarrierItemView(
                out __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            return
                FinishCarrierItemView(
                    __exception,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(GUIManager),
        "TestUpdateItemPrompts")]
    private static class GUIManager_TestUpdateItemPrompts_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginCarrierItemView(
                out __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            return
                FinishCarrierItemView(
                    __exception,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(GUIManager),
        "UpdateThrow")]
    private static class GUIManager_UpdateThrow_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            GUIManager __instance)
        {
            ApplyLocalDropFeedback(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(GUIManager),
        "UpdateRope")]
    private static class GUIManager_UpdateRope_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginCarrierItemView(
                out __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            return
                FinishCarrierItemView(
                    __exception,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(UI_UseItemProgress),
        "UpdateFillAmount")]
    private static class
        UI_UseItemProgress_UpdateFillAmount_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            UI_UseItemProgress __instance,
            ref bool __result)
        {
            bool handled =
                TryApplyCombinedProgress(
                    __instance,
                    ref __result);

            return
                !handled;
        }
    }

    [HarmonyPatch(
        typeof(BackpackWheel),
        nameof(
            BackpackWheel.InitWheel),
        new Type[]
        {
            typeof(BackpackReference)
        })]
    private static class BackpackWheel_InitWheel_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginCarrierItemView(
                out __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            return
                FinishCarrierItemView(
                    __exception,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(BackpackWheel),
        nameof(
            BackpackWheel.Choose))]
    private static class BackpackWheel_Choose_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginCarrierItemView(
                out __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            return
                FinishCarrierItemView(
                    __exception,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(BackpackWheel),
        nameof(
            BackpackWheel.Hover),
        new Type[]
        {
            typeof(
                BackpackWheelSlice
                    .SliceData)
        })]
    private static class BackpackWheel_Hover_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginCarrierItemView(
                out __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            return
                FinishCarrierItemView(
                    __exception,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(BackpackWheelSlice),
        nameof(
            BackpackWheelSlice.Hover))]
    private static class BackpackWheelSlice_Hover_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginCarrierItemView(
                out __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            return
                FinishCarrierItemView(
                    __exception,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(BackpackWheelSlice),
        nameof(
            BackpackWheelSlice.InitStashSlot),
        new Type[]
        {
            typeof(BackpackReference),
            typeof(BackpackWheel)
        })]
    private static class
        BackpackWheelSlice_InitStashSlot_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginCarrierItemView(
                out __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            return
                FinishCarrierItemView(
                    __exception,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(Constructable),
        "Update")]
    private static class Constructable_Update_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Constructable __instance)
        {
            return
                !ShouldOwnRemoteConstructablePreview(
                    __instance);
        }
    }
}

[DefaultExecutionOrder(5000)]
public sealed class ShowUIRuntime :
    MonoBehaviour
{
    private bool active;

    public void Activate()
    {
        active =
            true;
    }

    public void Deactivate()
    {
        active =
            false;
    }

    private void Update()
    {
        if (!active)
        {
            return;
        }

        ShowUI.UpdateLocalConstructablePreview();
    }

    private void OnDestroy()
    {
        Deactivate();
    }
}
