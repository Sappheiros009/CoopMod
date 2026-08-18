using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public static class SeparateRole
{
    private const string HarmonyId =
        "com.peak.coopmod.separaterole";

    private const byte UpperBodyInputEventCode =
        185;

    private const float RemoteInputTimeout =
        0.5f;

    private const float RemoteHeldInputTimeout =
        3f;

    private const float InputSendInterval =
        1f / 30f;

    private const float InputHeartbeatInterval =
        0.15f;

    private const int InputPayloadLength =
        49;

    [StructLayout(
        LayoutKind.Explicit)]
    private struct FloatIntUnion
    {
        [FieldOffset(0)]
        public float FloatValue;

        [FieldOffset(0)]
        public int IntValue;
    }

    private static Harmony harmony;

    private static SeparateRoleRuntime runtime;

    public static ConfigEntry<bool>
        HideCarrierBody;

    public static ConfigEntry<bool>
        HideCarrierHead;

    public static ConfigEntry<bool>
        HideCarrierFace;

    public static ConfigEntry<bool>
        HideCarrierHat;

    public static ConfigEntry<bool>
        HideCarrierSash;

    public static ConfigEntry<bool>
        HideCarrierCostumes;

    public static ConfigEntry<bool>
        HideCarrierSpecialRenderers;

    private static Character
        visibilityCarrier;

    private static float
        nextVisibilityRefreshTime;

    private static RemoteUpperBodyInput remoteInput =
        new RemoteUpperBodyInput();

    private static AppliedUpperBodyInput appliedInput =
        new AppliedUpperBodyInput();

    private sealed class RemoteUpperBodyInput
    {
        public int SourceActor = -1;
        public int TargetActor = -1;

        public Vector2 LookInput =
            Vector2.zero;

        public Vector2 ClimbMovementInput =
            Vector2.zero;

        public bool InteractHeld;
        public bool PrimaryHeld;
        public bool SecondaryHeld;
        public bool DropHeld;

        public int SlotForwardCounter;
        public int SlotBackwardCounter;
        public int UnselectCounter;
        public int BackpackCounter;
        public int ScrollBackwardCounter;
        public int ScrollForwardCounter;

        public int HotbarHeldMask;

        public float ScrollInput;

        public float ReceivedTime = -100f;
    }

    private sealed class AppliedUpperBodyInput
    {
        public int Frame = -1;

        public bool PreviousInteractHeld;
        public bool PreviousPrimaryHeld;
        public bool PreviousSecondaryHeld;
        public bool PreviousDropHeld;

        public bool InteractPressed;
        public bool InteractReleased;

        public bool PrimaryPressed;
        public bool PrimaryReleased;

        public bool SecondaryPressed;
        public bool SecondaryReleased;

        public bool DropPressed;
        public bool DropReleased;

        public int SlotForwardCounter;
        public int SlotBackwardCounter;
        public int UnselectCounter;
        public int BackpackCounter;
        public int ScrollBackwardCounter;
        public int ScrollForwardCounter;

        public bool SlotForwardPressed;
        public bool SlotBackwardPressed;
        public bool UnselectPressed;
        public bool BackpackPressed;
        public bool ScrollBackwardPressed;
        public bool ScrollForwardPressed;

        public int PreviousHotbarHeldMask;
        public int HotbarPressedMask;

        public void Reset()
        {
            Frame =
                -1;

            PreviousInteractHeld =
                false;

            PreviousPrimaryHeld =
                false;

            PreviousSecondaryHeld =
                false;

            PreviousDropHeld =
                false;

            InteractPressed =
                false;

            InteractReleased =
                false;

            PrimaryPressed =
                false;

            PrimaryReleased =
                false;

            SecondaryPressed =
                false;

            SecondaryReleased =
                false;

            DropPressed =
                false;

            DropReleased =
                false;

            SlotForwardCounter =
                0;

            SlotBackwardCounter =
                0;

            UnselectCounter =
                0;

            BackpackCounter =
                0;

            ScrollBackwardCounter =
                0;

            ScrollForwardCounter =
                0;

            SlotForwardPressed =
                false;

            SlotBackwardPressed =
                false;

            UnselectPressed =
                false;

            BackpackPressed =
                false;

            ScrollBackwardPressed =
                false;

            ScrollForwardPressed =
                false;

            PreviousHotbarHeldMask =
                0;

            HotbarPressedMask =
                0;
        }
    }

    public static void Initialize(
        CoopMod plugin)
    {
        if (harmony != null)
        {
            return;
        }

        if (plugin == null)
        {
            Debug.LogError(
                "[SeparateRole] CoopMod instance is null.");

            return;
        }

        HideCarrierBody =
            plugin.Config.Bind(
                "Climber View",
                "Hide Carrier Body",
                true,
                "등반자 화면에서 운반자의 몸통, 팔, 다리와 하체 의상을 숨깁니다. PEAK에서는 몸통/팔/다리가 하나의 스킨드 메시로 묶여 있습니다."
            );

        HideCarrierHead =
            plugin.Config.Bind(
                "Climber View",
                "Hide Carrier Head",
                true,
                "등반자 화면에서 운반자의 머리 메시를 숨깁니다."
            );

        HideCarrierFace =
            plugin.Config.Bind(
                "Climber View",
                "Hide Carrier Face",
                true,
                "등반자 화면에서 운반자의 눈, 입, 얼굴 액세서리를 숨깁니다."
            );

        HideCarrierHat =
            plugin.Config.Bind(
                "Climber View",
                "Hide Carrier Hat",
                true,
                "등반자 화면에서 운반자의 모자를 숨깁니다."
            );

        HideCarrierSash =
            plugin.Config.Bind(
                "Climber View",
                "Hide Carrier Sash",
                true,
                "등반자 화면에서 운반자의 띠/새시를 숨깁니다."
            );

        HideCarrierCostumes =
            plugin.Config.Bind(
                "Climber View",
                "Hide Carrier Costumes",
                true,
                "등반자 화면에서 운반자의 추가 코스튬 렌더러를 숨깁니다."
            );

        HideCarrierSpecialRenderers =
            plugin.Config.Bind(
                "Climber View",
                "Hide Carrier Special Renderers",
                true,
                "등반자 화면에서 운반자의 블라인드, 치킨, 스켈레톤 등 특수 렌더러를 숨깁니다."
            );

        harmony =
            new Harmony(
                HarmonyId);

        Patch(
            typeof(
                CharacterInput_Sample_Patch));

        Patch(
            typeof(
                CharacterInput_SelectSlotWasPressed_Patch));

        Patch(
            typeof(
                CharacterInput_SelectSlotIsPressed_Patch));

        Patch(
            typeof(
                Character_CheckMovement_Patch));

        Patch(
            typeof(
                Item_RequestPickup_RedirectToCarrier_Patch));

        Patch(
            typeof(
                CharacterItems_DoUsing_Patch));

        Patch(
            typeof(
                CharacterItems_DoDropping_Patch));

        Patch(
            typeof(
                CharacterItems_DoSwitching_Patch));

        Patch(
            typeof(
                CharacterClimbing_CanClimb_Patch));

        Patch(
            typeof(
                CharacterClimbing_StartClimbRpc_Patch));

        Patch(
            typeof(
                CharacterMovement_JumpRpc_Patch));

        Patch(
            typeof(
                CharacterCarrying_PassOutLock_Patch));

        Patch(
            typeof(
                Character_ObservedCharacter_Patch));

        runtime =
            plugin.gameObject
                .GetComponent<SeparateRoleRuntime>();

        if (runtime == null)
        {
            runtime =
                plugin.gameObject
                    .AddComponent<SeparateRoleRuntime>();
        }

        runtime.Activate();

        ResetRemoteInput();

        visibilityCarrier =
            null;

        nextVisibilityRefreshTime =
            0f;

        Debug.Log(
            "[SeparateRole] Initialized. Carrier movement + Climber upper-body/item mode.");
    }

    public static void Shutdown()
    {
        if (runtime != null)
        {
            runtime.Deactivate();

            UnityEngine.Object.Destroy(
                runtime);

            runtime =
                null;
        }

        if (harmony != null)
        {
            harmony.UnpatchSelf();

            harmony =
                null;
        }

        RestoreCarrierVisibility();

        HideCarrierBody =
            null;

        HideCarrierHead =
            null;

        HideCarrierFace =
            null;

        HideCarrierHat =
            null;

        HideCarrierSash =
            null;

        HideCarrierCostumes =
            null;

        HideCarrierSpecialRenderers =
            null;

        ResetRemoteInput();

        Debug.Log(
            "[SeparateRole] Shutdown.");
    }

    private static void Patch(
        Type patchType)
    {
        harmony
            .CreateClassProcessor(
                patchType)
            .Patch();
    }

    private static Character ResolveCharacter(
        Component component)
    {
        if (component == null)
        {
            return null;
        }

        Character character =
            component
                .GetComponent<Character>();

        if (character != null)
        {
            return character;
        }

        return
            component
                .GetComponentInParent<Character>();
    }

    public static bool IsCarrier(
        Character character)
    {
        if (character == null ||
            character.data == null)
        {
            return false;
        }

        Character rider =
            character
                .data
                .carriedPlayer;

        if (rider == null ||
            rider.data == null)
        {
            return false;
        }

        return
            rider.data.isCarried &&
            rider.data.carrier ==
                character;
    }

    public static bool IsClimber(
        Character character)
    {
        if (character == null ||
            character.data == null ||
            !character.data.isCarried)
        {
            return false;
        }

        Character carrier =
            character
                .data
                .carrier;

        if (carrier == null ||
            carrier.data == null)
        {
            return false;
        }

        return
            carrier.data.carriedPlayer ==
            character;
    }

    private static bool CanClimberSendInput(
        Character character)
    {
        return
            character != null &&
            character.data != null &&
            IsClimber(character) &&
            !character.data.dead &&
            !character.data.passedOut &&
            !character.data.fullyPassedOut;
    }

    private static bool CanCarrierReceiveInput(
        Character character)
    {
        return
            character != null &&
            character.data != null &&
            IsCarrier(character) &&
            !character.data.dead &&
            !character.data.passedOut &&
            !character.data.fullyPassedOut;
    }

    private static bool CanUseGameplayInput()
    {
        if (GUIManager.instance == null)
        {
            return false;
        }

        return
            !GUIManager.instance.windowBlockingInput &&
            !GUIManager.instance.wheelActive;
    }

    private static int GetActorNumber(
        Character character)
    {
        if (character == null ||
            character.photonView == null ||
            character.photonView.Owner == null)
        {
            return -1;
        }

        return
            character
                .photonView
                .Owner
                .ActorNumber;
    }

    private static Vector2 ReadLookInput()
    {
        if (CharacterInput.action_look ==
            null)
        {
            return
                Vector2.zero;
        }

        return
            CharacterInput
                .action_look
                .ReadValue<Vector2>();
    }

    private static Vector2 ReadMovementInput()
    {
        Vector2 movement =
            Vector2.zero;

        if (CharacterInput.action_move != null)
        {
            movement +=
                CharacterInput
                    .action_move
                    .ReadValue<Vector2>();
        }

        if (CharacterInput.action_moveForward != null &&
            CharacterInput.action_moveForward.IsPressed())
        {
            movement +=
                Vector2.up;
        }

        if (CharacterInput.action_moveBackward != null &&
            CharacterInput.action_moveBackward.IsPressed())
        {
            movement -=
                Vector2.up;
        }

        if (CharacterInput.action_moveRight != null &&
            CharacterInput.action_moveRight.IsPressed())
        {
            movement +=
                Vector2.right;
        }

        if (CharacterInput.action_moveLeft != null &&
            CharacterInput.action_moveLeft.IsPressed())
        {
            movement -=
                Vector2.right;
        }

        return
            Vector2.ClampMagnitude(
                movement,
                1f);
    }

    private static void ClearClimberCharacterInput(
        CharacterInput input)
    {
        if (input == null)
        {
            return;
        }

        input.lookInput =
            Vector2.zero;

        input.movementInput =
            Vector2.zero;

        input.usePrimaryWasPressed =
            false;

        input.usePrimaryIsPressed =
            false;

        input.usePrimaryWasReleased =
            false;

        input.useSecondaryWasPressed =
            false;

        input.useSecondaryIsPressed =
            false;

        input.useSecondaryWasReleased =
            false;

        input.dropWasPressed =
            false;

        input.dropIsPressed =
            false;

        input.dropWasReleased =
            false;

        input.selectSlotForwardWasPressed =
            false;

        input.selectSlotBackwardWasPressed =
            false;

        input.unselectSlotWasPressed =
            false;

        input.selectBackpackWasPressed =
            false;

        input.scrollBackwardWasPressed =
            false;

        input.scrollForwardWasPressed =
            false;

        input.scrollBackwardIsPressed =
            false;

        input.scrollForwardIsPressed =
            false;

        input.scrollInput =
            0f;

        input.jumpWasPressed =
            false;

        input.jumpIsPressed =
            false;

        input.sprintWasPressed =
            false;

        input.sprintIsPressed =
            false;

        input.sprintToggleWasPressed =
            false;

        input.sprintToggleIsPressed =
            false;

        input.crouchWasPressed =
            false;

        input.crouchIsPressed =
            false;

        input.crouchToggleWasPressed =
            false;

    }

    private static void WriteInt32(
        byte[] buffer,
        ref int offset,
        int value)
    {
        buffer[offset++] =
            (byte)value;

        buffer[offset++] =
            (byte)(value >> 8);

        buffer[offset++] =
            (byte)(value >> 16);

        buffer[offset++] =
            (byte)(value >> 24);
    }

    private static int ReadInt32(
        byte[] buffer,
        ref int offset)
    {
        int value =
            buffer[offset] |
            buffer[offset + 1] << 8 |
            buffer[offset + 2] << 16 |
            buffer[offset + 3] << 24;

        offset +=
            4;

        return
            value;
    }

    private static void WriteSingle(
        byte[] buffer,
        ref int offset,
        float value)
    {
        FloatIntUnion converter =
            new FloatIntUnion
            {
                FloatValue =
                    value
            };

        WriteInt32(
            buffer,
            ref offset,
            converter.IntValue);
    }

    private static float ReadSingle(
        byte[] buffer,
        ref int offset)
    {
        FloatIntUnion converter =
            new FloatIntUnion
            {
                IntValue =
                    ReadInt32(
                        buffer,
                        ref offset)
            };

        return
            converter.FloatValue;
    }

    private static void ResetRemoteInput()
    {
        remoteInput =
            new RemoteUpperBodyInput();

        appliedInput =
            new AppliedUpperBodyInput();

        appliedInput.Reset();

    }

    private static bool RemoteInputIsValid(
        Character carrier)
    {
        if (!CanCarrierReceiveInput(
                carrier))
        {
            return false;
        }

        if (Time.realtimeSinceStartup -
            remoteInput.ReceivedTime >
            RemoteInputTimeout)
        {
            return false;
        }

        int carrierActor =
            GetActorNumber(
                carrier);

        Character climber =
            carrier
                .data
                .carriedPlayer;

        int climberActor =
            GetActorNumber(
                climber);

        return
            carrierActor > 0 &&
            climberActor > 0 &&
            remoteInput.TargetActor ==
                carrierActor &&
            remoteInput.SourceActor ==
                climberActor;
    }

    private static bool RemoteHeldInputIsValid(
        Character carrier)
    {
        if (!CanCarrierReceiveInput(
                carrier))
        {
            return false;
        }

        if (Time.realtimeSinceStartup -
            remoteInput.ReceivedTime >
            RemoteHeldInputTimeout)
        {
            return false;
        }

        int carrierActor =
            GetActorNumber(
                carrier);

        Character climber =
            carrier
                .data
                .carriedPlayer;

        int climberActor =
            GetActorNumber(
                climber);

        return
            carrierActor > 0 &&
            climberActor > 0 &&
            remoteInput.TargetActor ==
                carrierActor &&
            remoteInput.SourceActor ==
                climberActor;
    }

    private static void PrepareAppliedInput(
        Character carrier)
    {
        if (appliedInput.Frame ==
            Time.frameCount)
        {
            return;
        }

        appliedInput.Frame =
            Time.frameCount;

        bool valid =
            RemoteInputIsValid(
                carrier);

        bool heldValid =
            RemoteHeldInputIsValid(
                carrier);

        bool interactHeld =
            valid &&
            remoteInput.InteractHeld;

        bool primaryHeld =
            heldValid &&
            remoteInput.PrimaryHeld;

        bool secondaryHeld =
            heldValid &&
            remoteInput.SecondaryHeld;

        bool dropHeld =
            heldValid &&
            remoteInput.DropHeld;

        appliedInput.InteractPressed =
            interactHeld &&
            !appliedInput
                .PreviousInteractHeld;

        appliedInput.InteractReleased =
            !interactHeld &&
            appliedInput
                .PreviousInteractHeld;

        appliedInput.PrimaryPressed =
            primaryHeld &&
            !appliedInput
                .PreviousPrimaryHeld;

        appliedInput.PrimaryReleased =
            !primaryHeld &&
            appliedInput
                .PreviousPrimaryHeld;

        appliedInput.SecondaryPressed =
            secondaryHeld &&
            !appliedInput
                .PreviousSecondaryHeld;

        appliedInput.SecondaryReleased =
            !secondaryHeld &&
            appliedInput
                .PreviousSecondaryHeld;

        appliedInput.DropPressed =
            dropHeld &&
            !appliedInput
                .PreviousDropHeld;

        appliedInput.DropReleased =
            !dropHeld &&
            appliedInput
                .PreviousDropHeld;

        appliedInput.PreviousInteractHeld =
            interactHeld;

        appliedInput.PreviousPrimaryHeld =
            primaryHeld;

        appliedInput.PreviousSecondaryHeld =
            secondaryHeld;

        appliedInput.PreviousDropHeld =
            dropHeld;

        appliedInput.SlotForwardPressed =
            valid &&
            remoteInput.SlotForwardCounter !=
            appliedInput.SlotForwardCounter;

        appliedInput.SlotBackwardPressed =
            valid &&
            remoteInput.SlotBackwardCounter !=
            appliedInput.SlotBackwardCounter;

        appliedInput.UnselectPressed =
            valid &&
            remoteInput.UnselectCounter !=
            appliedInput.UnselectCounter;

        appliedInput.BackpackPressed =
            valid &&
            remoteInput.BackpackCounter !=
            appliedInput.BackpackCounter;

        appliedInput.ScrollBackwardPressed =
            valid &&
            remoteInput.ScrollBackwardCounter !=
            appliedInput.ScrollBackwardCounter;

        appliedInput.ScrollForwardPressed =
            valid &&
            remoteInput.ScrollForwardCounter !=
            appliedInput.ScrollForwardCounter;

        int hotbarHeldMask =
            valid
                ? remoteInput.HotbarHeldMask
                : 0;

        appliedInput.HotbarPressedMask =
            hotbarHeldMask &
            ~appliedInput.PreviousHotbarHeldMask;

        appliedInput.PreviousHotbarHeldMask =
            hotbarHeldMask;

        if (valid)
        {
            appliedInput.SlotForwardCounter =
                remoteInput
                    .SlotForwardCounter;

            appliedInput.SlotBackwardCounter =
                remoteInput
                    .SlotBackwardCounter;

            appliedInput.UnselectCounter =
                remoteInput
                    .UnselectCounter;

            appliedInput.BackpackCounter =
                remoteInput
                    .BackpackCounter;

            appliedInput.ScrollBackwardCounter =
                remoteInput
                    .ScrollBackwardCounter;

            appliedInput.ScrollForwardCounter =
                remoteInput
                    .ScrollForwardCounter;
        }
    }

    private static void ApplyClimberInputToCarrier(
        CharacterInput input,
        Character carrier)
    {
        if (input == null ||
            carrier == null)
        {
            return;
        }

        PrepareAppliedInput(
            carrier);

        bool valid =
            RemoteInputIsValid(
                carrier);

        bool heldValid =
            RemoteHeldInputIsValid(
                carrier);

        /*
         * 카메라/시선/캐릭터 방향은 운반자(A)가 전담합니다.
         * CharacterInput.Sample이 읽은 운반자의 원래 lookInput을
         * 절대 덮어쓰지 않습니다.
         *
         * 등반자(B)는 운반자의 카메라를 네트워크로 그대로 받아 보기만 하며,
         * 자신의 Look 입력으로 캐릭터 방향을 바꿀 수 없습니다.
         */

        input.interactWasPressed =
            false;

        input.interactIsPressed =
            false;

        input.interactWasReleased =
            false;

        input.usePrimaryWasPressed =
            appliedInput.PrimaryPressed;

        input.usePrimaryIsPressed =
            heldValid &&
            remoteInput.PrimaryHeld;

        input.usePrimaryWasReleased =
            appliedInput.PrimaryReleased;

        input.useSecondaryWasPressed =
            appliedInput.SecondaryPressed;

        input.useSecondaryIsPressed =
            heldValid &&
            remoteInput.SecondaryHeld;

        input.useSecondaryWasReleased =
            appliedInput.SecondaryReleased;

        input.dropWasPressed =
            appliedInput.DropPressed;

        input.dropIsPressed =
            heldValid &&
            remoteInput.DropHeld;

        input.dropWasReleased =
            appliedInput.DropReleased;

        input.selectSlotForwardWasPressed =
            appliedInput.SlotForwardPressed;

        input.selectSlotBackwardWasPressed =
            appliedInput.SlotBackwardPressed;

        input.unselectSlotWasPressed =
            appliedInput.UnselectPressed;

        input.selectBackpackWasPressed =
            appliedInput.BackpackPressed;

        input.scrollBackwardWasPressed =
            appliedInput.ScrollBackwardPressed;

        input.scrollForwardWasPressed =
            appliedInput.ScrollForwardPressed;

        input.scrollBackwardIsPressed =
            appliedInput.ScrollBackwardPressed;

        input.scrollForwardIsPressed =
            appliedInput.ScrollForwardPressed;

        input.scrollInput =
            valid
                ? remoteInput.ScrollInput
                : 0f;

        if (carrier.data.isClimbing ||
            carrier.data.isRopeClimbing ||
            carrier.data.isVineClimbing)
        {
            input.movementInput =
                valid
                    ? remoteInput
                        .ClimbMovementInput
                    : Vector2.zero;
        }
    }

    internal static void RuntimeUpdate()
    {
        Character localCharacter =
            Character.localCharacter;

        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null ||
            localCharacter == null ||
            !localCharacter.IsLocal ||
            !CanClimberSendInput(
                localCharacter))
        {
            return;
        }

        Character carrier =
            localCharacter
                .data
                .carrier;

        if (carrier == null ||
            carrier.data == null)
        {
            return;
        }

        runtime.SendUpperBodyInput(
            localCharacter,
            carrier);
    }

    internal static void HandleInputEvent(
        EventData photonEvent)
    {
        if (photonEvent.Code !=
            UpperBodyInputEventCode)
        {
            return;
        }

        byte[] payload =
            photonEvent.CustomData
                as byte[];

        if (payload == null ||
            payload.Length <
                InputPayloadLength)
        {
            return;
        }

        Character localCharacter =
            Character.localCharacter;

        if (localCharacter == null ||
            !localCharacter.IsLocal ||
            !IsCarrier(localCharacter))
        {
            return;
        }

        int offset =
            0;

        int sourceActor =
            ReadInt32(
                payload,
                ref offset);

        int targetActor =
            ReadInt32(
                payload,
                ref offset);

        int localActor =
            GetActorNumber(
                localCharacter);

        Character climber =
            localCharacter
                .data
                .carriedPlayer;

        int expectedSourceActor =
            GetActorNumber(
                climber);

        if (targetActor !=
                localActor ||
            sourceActor !=
                expectedSourceActor)
        {
            return;
        }

        remoteInput.SourceActor =
            sourceActor;

        remoteInput.TargetActor =
            targetActor;

        remoteInput.LookInput =
            Vector2.zero;

        remoteInput.ClimbMovementInput =
            new Vector2(
                ReadSingle(
                    payload,
                    ref offset),
                ReadSingle(
                    payload,
                    ref offset));

        byte flags =
            payload[offset++];

        remoteInput.InteractHeld =
            (
                flags &
                1
            ) != 0;

        remoteInput.PrimaryHeld =
            (
                flags &
                2
            ) != 0;

        remoteInput.SecondaryHeld =
            (
                flags &
                4
            ) != 0;

        remoteInput.DropHeld =
            (
                flags &
                8
            ) != 0;

        remoteInput.SlotForwardCounter =
            ReadInt32(
                payload,
                ref offset);

        remoteInput.SlotBackwardCounter =
            ReadInt32(
                payload,
                ref offset);

        remoteInput.UnselectCounter =
            ReadInt32(
                payload,
                ref offset);

        remoteInput.BackpackCounter =
            ReadInt32(
                payload,
                ref offset);

        remoteInput.ScrollBackwardCounter =
            ReadInt32(
                payload,
                ref offset);

        remoteInput.ScrollForwardCounter =
            ReadInt32(
                payload,
                ref offset);

        remoteInput.ScrollInput =
            ReadSingle(
                payload,
                ref offset);

        remoteInput.HotbarHeldMask =
            ReadInt32(
                payload,
                ref offset);

        remoteInput.ReceivedTime =
            Time.realtimeSinceStartup;
    }

    [HarmonyPatch(
        typeof(CharacterInput),
        "Sample",
        new Type[]
        {
            typeof(bool)
        })]
    private static class CharacterInput_Sample_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterInput __instance)
        {
            Character character =
                Character.localCharacter;

            if (character == null ||
                character.input != __instance)
            {
                return;
            }

            if (IsCarrier(character))
            {
                ApplyClimberInputToCarrier(
                    __instance,
                    character);

                return;
            }

            if (IsClimber(character))
            {
                ClearClimberCharacterInput(
                    __instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(CharacterInput),
        nameof(
            CharacterInput.SelectSlotWasPressed))]
    private static class CharacterInput_SelectSlotWasPressed_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterInput __instance,
            int key,
            ref bool __result)
        {
            Character character =
                Character.localCharacter;

            if (character == null ||
                character.input != __instance)
            {
                return true;
            }

            if (IsClimber(character))
            {
                __result =
                    false;

                return false;
            }

            if (!IsCarrier(character))
            {
                return true;
            }

            PrepareAppliedInput(
                character);

            if (key < 0 ||
                key >= 31)
            {
                __result =
                    false;

                return false;
            }

            int bit =
                1 << key;

            __result =
                (
                    appliedInput
                        .HotbarPressedMask &
                    bit
                ) != 0;

            return false;
        }
    }

    [HarmonyPatch(
        typeof(CharacterInput),
        nameof(
            CharacterInput.SelectSlotIsPressed))]
    private static class CharacterInput_SelectSlotIsPressed_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterInput __instance,
            int key,
            ref bool __result)
        {
            Character character =
                Character.localCharacter;

            if (character == null ||
                character.input != __instance)
            {
                return true;
            }

            if (IsClimber(character))
            {
                __result =
                    false;

                return false;
            }

            if (!IsCarrier(character))
            {
                return true;
            }

            bool valid =
                RemoteInputIsValid(
                    character);

            if (!valid ||
                key < 0 ||
                key >= 31)
            {
                __result =
                    false;

                return false;
            }

            int bit =
                1 << key;

            __result =
                (
                    remoteInput
                        .HotbarHeldMask &
                    bit
                ) != 0;

            return false;
        }
    }

    [HarmonyPatch(
        typeof(Character),
        "CheckMovement")]
    private static class Character_CheckMovement_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Character __instance,
            ref bool __result)
        {
            if (IsClimber(__instance))
            {
                __result =
                    false;
            }
        }
    }

    [HarmonyPatch(
        typeof(Item),
        "RequestPickup",
        new Type[]
        {
            typeof(PhotonView)
        })]
    private static class Item_RequestPickup_RedirectToCarrier_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref PhotonView characterView)
        {
            if (characterView == null)
            {
                return;
            }

            Character climber =
                characterView
                    .GetComponent<Character>();

            if (!IsClimber(
                    climber) ||
                climber.data == null)
            {
                return;
            }

            Character carrier =
                climber
                    .data
                    .carrier;

            if (carrier == null ||
                carrier.photonView == null)
            {
                return;
            }

            characterView =
                carrier.photonView;
        }
    }

    [HarmonyPatch(
        typeof(CharacterItems),
        "DoUsing")]
    private static class CharacterItems_DoUsing_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterItems __instance)
        {
            Character character =
                ResolveCharacter(
                    __instance);

            return
                !IsClimber(character);
        }
    }

    [HarmonyPatch(
        typeof(CharacterItems),
        "DoDropping")]
    private static class CharacterItems_DoDropping_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterItems __instance)
        {
            Character character =
                ResolveCharacter(
                    __instance);

            return
                !IsClimber(character);
        }
    }

    [HarmonyPatch(
        typeof(CharacterItems),
        "DoSwitching")]
    private static class CharacterItems_DoSwitching_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterItems __instance)
        {
            Character character =
                ResolveCharacter(
                    __instance);

            return
                !IsClimber(character);
        }
    }

    [HarmonyPatch(
        typeof(CharacterClimbing),
        nameof(
            CharacterClimbing.CanClimb))]
    private static class CharacterClimbing_CanClimb_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterClimbing __instance,
            ref bool __result)
        {
            Character character =
                ResolveCharacter(
                    __instance);

            if (IsClimber(character))
            {
                __result =
                    false;
            }
        }
    }

    [HarmonyPatch(
        typeof(CharacterClimbing),
        "StartClimbRpc",
        new Type[]
        {
            typeof(Vector3),
            typeof(Vector3)
        })]
    private static class CharacterClimbing_StartClimbRpc_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterClimbing __instance)
        {
            Character character =
                ResolveCharacter(
                    __instance);

            return
                !IsClimber(character);
        }
    }

    [HarmonyPatch(
        typeof(CharacterMovement),
        nameof(
            CharacterMovement.JumpRpc),
        new Type[]
        {
            typeof(bool)
        })]
    private static class CharacterMovement_JumpRpc_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterMovement __instance)
        {
            Character character =
                ResolveCharacter(
                    __instance);

            return
                !IsClimber(character);
        }
    }

    internal static void UpdateCarrierVisibilityForClimber()
    {
        Character localCharacter =
            Character.localCharacter;

        if (localCharacter == null ||
            !localCharacter.IsLocal ||
            !IsClimber(localCharacter) ||
            localCharacter.data == null)
        {
            RestoreCarrierVisibility();
            return;
        }

        Character carrier =
            localCharacter
                .data
                .carrier;

        if (carrier == null ||
            carrier.data == null ||
            carrier.refs == null ||
            carrier.refs.hideTheBody == null)
        {
            RestoreCarrierVisibility();
            return;
        }

        if (visibilityCarrier !=
            carrier)
        {
            RestoreCarrierVisibility();

            visibilityCarrier =
                carrier;

            nextVisibilityRefreshTime =
                0f;

        }

        if (Time.unscaledTime <
            nextVisibilityRefreshTime)
        {
            return;
        }

        nextVisibilityRefreshTime =
            Time.unscaledTime +
            1f;

        ApplyCarrierVisibility(
            carrier);
    }

    private static void RestoreCarrierVisibility()
    {
        if (visibilityCarrier == null ||
            visibilityCarrier.refs == null ||
            visibilityCarrier.refs.hideTheBody == null)
        {
            visibilityCarrier =
                null;

            nextVisibilityRefreshTime =
                0f;

            return;
        }

        visibilityCarrier
            .refs
            .hideTheBody
            .Refresh();

        visibilityCarrier =
            null;

        nextVisibilityRefreshTime =
            0f;

    }

    private static void ApplyCarrierVisibility(
        Character carrier)
    {
        if (carrier == null ||
            carrier.refs == null ||
            carrier.refs.hideTheBody == null)
        {
            return;
        }

        HideTheBody hide =
            carrier
                .refs
                .hideTheBody;

        CustomizationRefs refs =
            hide.refs;

        hide.Refresh();

        if (HideCarrierBody != null &&
            HideCarrierBody.Value)
        {
            SetRendererHidden(
                hide,
                hide.body);

            if (refs != null)
            {
                SetRendererHidden(
                    hide,
                    refs.mainRenderer);

                SetRendererHidden(
                    hide,
                    refs.shorts);

                SetRendererHidden(
                    hide,
                    refs.skirt);
            }
        }

        if (HideCarrierHead != null &&
            HideCarrierHead.Value)
        {
            SetRendererHidden(
                hide,
                hide.headRend);
        }

        if (HideCarrierFace != null &&
            HideCarrierFace.Value)
        {
            if (hide.face != null)
            {
                HideRendererArray(
                    hide,
                    hide.face
                        .GetComponentsInChildren<Renderer>(
                            true));
            }

            if (refs != null)
            {
                HideRendererArray(
                    hide,
                    refs.EyeRenderers);

                SetRendererHidden(
                    hide,
                    refs.mouthRenderer);

                SetRendererHidden(
                    hide,
                    refs.accessoryRenderer);

                if (refs.thirdEye != null)
                {
                    SetRendererHidden(
                        hide,
                        refs.thirdEye
                            .GetComponent<Renderer>());
                }
            }
        }

        if (HideCarrierHat != null &&
            HideCarrierHat.Value &&
            refs != null)
        {
            HideRendererArray(
                hide,
                refs.playerHats);
        }

        if (HideCarrierSash != null &&
            HideCarrierSash.Value)
        {
            SetRendererHidden(
                hide,
                hide.sash);

            if (refs != null)
            {
                SetRendererHidden(
                    hide,
                    refs.sashRenderer);
            }
        }

        if (HideCarrierCostumes != null &&
            HideCarrierCostumes.Value)
        {
            HideRendererArray(
                hide,
                hide.costumes);
        }

        if (HideCarrierSpecialRenderers != null &&
            HideCarrierSpecialRenderers.Value &&
            refs != null)
        {
            SetRendererHidden(
                hide,
                refs.blindRenderer);

            SetRendererHidden(
                hide,
                refs.chickenRenderer);

            SetRendererHidden(
                hide,
                refs.skeletonRenderer);
        }

    }

    private static void HideRendererArray(
        HideTheBody hide,
        Renderer[] renderers)
    {
        if (hide == null ||
            renderers == null)
        {
            return;
        }

        for (int i = 0;
            i < renderers.Length;
            i++)
        {
            SetRendererHidden(
                hide,
                renderers[i]);
        }
    }

    private static void SetRendererHidden(
        HideTheBody hide,
        Renderer renderer)
    {
        if (hide == null ||
            renderer == null)
        {
            return;
        }

        hide.SetShowing(
            renderer,
            1f);
    }

    [HarmonyPatch(
        typeof(Character),
        "get_observedCharacter")]
    private static class Character_ObservedCharacter_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            ref Character __result)
        {
            Character localCharacter =
                Character.localCharacter;

            if (localCharacter == null ||
                !localCharacter.IsLocal ||
                !IsClimber(localCharacter) ||
                localCharacter.data == null)
            {
                return;
            }

            Character carrier =
                localCharacter
                    .data
                    .carrier;

            if (carrier == null ||
                carrier.data == null)
            {
                return;
            }

            __result =
                carrier;
        }
    }

    [HarmonyPatch(
        typeof(CharacterCarrying),
        "Update")]
    private static class CharacterCarrying_PassOutLock_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterCarrying __instance)
        {
            Character carrier =
                ResolveCharacter(
                    __instance);

            if (!IsCarrier(carrier))
            {
                return true;
            }

            Character rider =
                carrier
                    .data
                    .carriedPlayer;

            if (rider == null ||
                rider.data == null)
            {
                return true;
            }

            if (carrier.data.dead ||
                rider.data.dead)
            {
                return true;
            }

            if (carrier.data.passedOut ||
                carrier.data.fullyPassedOut ||
                rider.data.passedOut ||
                rider.data.fullyPassedOut)
            {
                return false;
            }

            return true;
        }
    }

    internal static byte UpperBodyInputEventCodeLocal
    {
        get
        {
            return
                UpperBodyInputEventCode;
        }
    }

    internal static float InputSendIntervalLocal
    {
        get
        {
            return
                InputSendInterval;
        }
    }

    internal static float InputHeartbeatIntervalLocal
    {
        get
        {
            return
                InputHeartbeatInterval;
        }
    }

    internal static int InputPayloadLengthLocal
    {
        get
        {
            return
                InputPayloadLength;
        }
    }

    internal static void WriteInt32Local(
        byte[] buffer,
        ref int offset,
        int value)
    {
        WriteInt32(
            buffer,
            ref offset,
            value);
    }

    internal static void WriteSingleLocal(
        byte[] buffer,
        ref int offset,
        float value)
    {
        WriteSingle(
            buffer,
            ref offset,
            value);
    }

}

[DefaultExecutionOrder(10000)]
public sealed class SeparateRoleRuntime :
    MonoBehaviour,
    IOnEventCallback
{
    private bool active;

    private int currentCarrierActor =
        -1;

    private int slotForwardCounter;
    private int slotBackwardCounter;
    private int unselectCounter;
    private int backpackCounter;
    private int scrollBackwardCounter;
    private int scrollForwardCounter;

    private float nextInputSendTime;
    private float nextInputHeartbeatTime;
    private bool hasSentInputState;

    private Vector2 lastSentMovementInput =
        Vector2.zero;

    private byte lastSentInputFlags;

    private float lastSentScrollInput;

    private int lastSentHotbarHeldMask;

    private int lastSentSlotForwardCounter;
    private int lastSentSlotBackwardCounter;
    private int lastSentUnselectCounter;
    private int lastSentBackpackCounter;
    private int lastSentScrollBackwardCounter;
    private int lastSentScrollForwardCounter;

    private readonly int[]
        inputTargetActors =
            new int[1];

    private readonly RaiseEventOptions
        inputRaiseEventOptions =
            new RaiseEventOptions();

    public void Activate()
    {
        if (active)
        {
            return;
        }

        PhotonNetwork.AddCallbackTarget(
            this);

        ResetCounters();

        active =
            true;
    }

    public void Deactivate()
    {
        if (!active)
        {
            return;
        }

        PhotonNetwork.RemoveCallbackTarget(
            this);

        active =
            false;

        ResetCounters();
    }

    private void ResetCounters()
    {
        currentCarrierActor =
            -1;

        slotForwardCounter =
            0;

        slotBackwardCounter =
            0;

        unselectCounter =
            0;

        backpackCounter =
            0;

        scrollBackwardCounter =
            0;

        scrollForwardCounter =
            0;

        nextInputSendTime =
            0f;

        nextInputHeartbeatTime =
            0f;

        hasSentInputState =
            false;

        lastSentMovementInput =
            Vector2.zero;

        lastSentInputFlags =
            0;

        lastSentScrollInput =
            0f;

        lastSentHotbarHeldMask =
            0;

        lastSentSlotForwardCounter =
            0;

        lastSentSlotBackwardCounter =
            0;

        lastSentUnselectCounter =
            0;

        lastSentBackpackCounter =
            0;

        lastSentScrollBackwardCounter =
            0;

        lastSentScrollForwardCounter =
            0;

        inputTargetActors[0] =
            -1;

        inputRaiseEventOptions.TargetActors =
            inputTargetActors;

    }

    private void Update()
    {
        if (!active)
        {
            return;
        }

        SeparateRole.RuntimeUpdate();
    }

    private void LateUpdate()
    {
        if (!active)
        {
            return;
        }

        SeparateRole
            .UpdateCarrierVisibilityForClimber();
    }

    internal void SendUpperBodyInput(
        Character climber,
        Character carrier)
    {
        if (!active ||
            !PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null ||
            climber == null ||
            carrier == null)
        {
            return;
        }

        int sourceActor =
            GetActorNumberLocal(
                climber);

        int targetActor =
            GetActorNumberLocal(
                carrier);

        if (sourceActor <= 0 ||
            targetActor <= 0)
        {
            return;
        }

        if (currentCarrierActor !=
            targetActor)
        {
            ResetCounters();

            currentCarrierActor =
                targetActor;
        }

        bool gameplayInput =
            CanUseGameplayInputLocal();

        if (gameplayInput)
        {
            if (WasPressed(
                    CharacterInput
                        .action_selectSlotForward))
            {
                slotForwardCounter++;
            }

            if (WasPressed(
                    CharacterInput
                        .action_selectSlotBackward))
            {
                slotBackwardCounter++;
            }

            if (WasPressed(
                    CharacterInput
                        .action_unselectSlot))
            {
                unselectCounter++;
            }

            if (CharacterInput.action_selectBackpack !=
                    null &&
                CharacterInput
                    .action_selectBackpack
                    .WasPerformedThisFrame())
            {
                backpackCounter++;
            }

            if (WasPressed(
                    CharacterInput
                        .action_scrollBackward))
            {
                scrollBackwardCounter++;
            }

            if (WasPressed(
                    CharacterInput
                        .action_scrollForward))
            {
                scrollForwardCounter++;
            }
        }

        Vector2 movementInput =
            gameplayInput
                ? ReadMovementInputLocal()
                : Vector2.zero;

        bool interactHeld =
            gameplayInput &&
            IsPressed(
                CharacterInput
                    .action_interact);

        bool primaryHeld =
            gameplayInput &&
            IsPressed(
                CharacterInput
                    .action_usePrimary);

        bool secondaryHeld =
            gameplayInput &&
            IsPressed(
                CharacterInput
                    .action_useSecondary);

        bool dropHeld =
            gameplayInput &&
            IsPressed(
                CharacterInput
                    .action_drop);

        byte inputFlags =
            0;

        if (interactHeld)
        {
            inputFlags |=
                1;
        }

        if (primaryHeld)
        {
            inputFlags |=
                2;
        }

        if (secondaryHeld)
        {
            inputFlags |=
                4;
        }

        if (dropHeld)
        {
            inputFlags |=
                8;
        }

        float scrollInput =
            0f;

        if (gameplayInput &&
            CharacterInput.action_scroll !=
                null)
        {
            scrollInput =
                CharacterInput
                    .action_scroll
                    .ReadValue<float>();
        }

        int hotbarHeldMask =
            0;

        if (gameplayInput &&
            CharacterInput.hotbarActions !=
                null)
        {
            int hotbarCount =
                Mathf.Min(
                    CharacterInput
                        .hotbarActions
                        .Length,
                    30);

            for (int key = 0;
                key < hotbarCount;
                key++)
            {
                if (IsPressed(
                        CharacterInput
                            .hotbarActions[key]))
                {
                    hotbarHeldMask |=
                        1 << key;
                }
            }
        }

        bool counterChanged =
            slotForwardCounter !=
                lastSentSlotForwardCounter ||
            slotBackwardCounter !=
                lastSentSlotBackwardCounter ||
            unselectCounter !=
                lastSentUnselectCounter ||
            backpackCounter !=
                lastSentBackpackCounter ||
            scrollBackwardCounter !=
                lastSentScrollBackwardCounter ||
            scrollForwardCounter !=
                lastSentScrollForwardCounter;

        bool immediateStateChanged =
            inputFlags !=
                lastSentInputFlags ||
            hotbarHeldMask !=
                lastSentHotbarHeldMask;

        bool analogStateChanged =
            (
                movementInput -
                lastSentMovementInput
            ).sqrMagnitude >
                0.0004f ||
            Mathf.Abs(
                scrollInput -
                lastSentScrollInput) >
                0.01f;

        float now =
            Time.realtimeSinceStartup;

        bool sendReliable =
            counterChanged ||
            immediateStateChanged;

        bool shouldSend =
            !hasSentInputState ||
            sendReliable ||
            immediateStateChanged ||
            (
                now >=
                    nextInputSendTime &&
                analogStateChanged
            ) ||
            now >=
                nextInputHeartbeatTime;

        if (!shouldSend)
        {
            return;
        }

        byte[] payload =
            new byte[
                SeparateRole.InputPayloadLengthLocal];

        int offset =
            0;

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            sourceActor);

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            targetActor);

        SeparateRole.WriteSingleLocal(
            payload,
            ref offset,
            movementInput.x);

        SeparateRole.WriteSingleLocal(
            payload,
            ref offset,
            movementInput.y);

        payload[offset++] =
            inputFlags;

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            slotForwardCounter);

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            slotBackwardCounter);

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            unselectCounter);

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            backpackCounter);

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            scrollBackwardCounter);

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            scrollForwardCounter);

        SeparateRole.WriteSingleLocal(
            payload,
            ref offset,
            scrollInput);

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            hotbarHeldMask);

        if (inputTargetActors[0] !=
            targetActor)
        {
            inputTargetActors[0] =
                targetActor;

            inputRaiseEventOptions
                .TargetActors =
                inputTargetActors;
        }

        PhotonNetwork.RaiseEvent(
            SeparateRole.UpperBodyInputEventCodeLocal,
            payload,
            inputRaiseEventOptions,
            sendReliable
                ? SendOptions.SendReliable
                : SendOptions.SendUnreliable);

        hasSentInputState =
            true;

        lastSentMovementInput =
            movementInput;

        lastSentInputFlags =
            inputFlags;

        lastSentScrollInput =
            scrollInput;

        lastSentHotbarHeldMask =
            hotbarHeldMask;

        lastSentSlotForwardCounter =
            slotForwardCounter;

        lastSentSlotBackwardCounter =
            slotBackwardCounter;

        lastSentUnselectCounter =
            unselectCounter;

        lastSentBackpackCounter =
            backpackCounter;

        lastSentScrollBackwardCounter =
            scrollBackwardCounter;

        lastSentScrollForwardCounter =
            scrollForwardCounter;

        nextInputSendTime =
            now +
            SeparateRole.InputSendIntervalLocal;

        nextInputHeartbeatTime =
            now +
            SeparateRole.InputHeartbeatIntervalLocal;
    }

    public void OnEvent(
        EventData photonEvent)
    {
        if (!active)
        {
            return;
        }

        SeparateRole.HandleInputEvent(
            photonEvent);
    }

    private static int GetActorNumberLocal(
        Character character)
    {
        if (character == null ||
            character.photonView == null ||
            character.photonView.Owner == null)
        {
            return -1;
        }

        return
            character
                .photonView
                .Owner
                .ActorNumber;
    }

    private static bool CanUseGameplayInputLocal()
    {
        if (GUIManager.instance == null)
        {
            return false;
        }

        return
            !GUIManager.instance.windowBlockingInput &&
            !GUIManager.instance.wheelActive;
    }

    private static bool IsPressed(
        UnityEngine.InputSystem.InputAction action)
    {
        return
            action != null &&
            action.IsPressed();
    }

    private static bool WasPressed(
        UnityEngine.InputSystem.InputAction action)
    {
        return
            action != null &&
            action.WasPressedThisFrame();
    }

    private static Vector2 ReadLookInputLocal()
    {
        if (CharacterInput.action_look ==
            null)
        {
            return
                Vector2.zero;
        }

        return
            CharacterInput
                .action_look
                .ReadValue<Vector2>();
    }

    private static Vector2 ReadMovementInputLocal()
    {
        Vector2 movement =
            Vector2.zero;

        if (CharacterInput.action_move != null)
        {
            movement +=
                CharacterInput
                    .action_move
                    .ReadValue<Vector2>();
        }

        if (CharacterInput.action_moveForward != null &&
            CharacterInput.action_moveForward.IsPressed())
        {
            movement +=
                Vector2.up;
        }

        if (CharacterInput.action_moveBackward != null &&
            CharacterInput.action_moveBackward.IsPressed())
        {
            movement -=
                Vector2.up;
        }

        if (CharacterInput.action_moveRight != null &&
            CharacterInput.action_moveRight.IsPressed())
        {
            movement +=
                Vector2.right;
        }

        if (CharacterInput.action_moveLeft != null &&
            CharacterInput.action_moveLeft.IsPressed())
        {
            movement -=
                Vector2.right;
        }

        return
            Vector2.ClampMagnitude(
                movement,
                1f);
    }

    private void OnDestroy()
    {
        Deactivate();
    }
}
