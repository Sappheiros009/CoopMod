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
        22;

    [StructLayout(
        LayoutKind.Explicit)]
    private struct FloatIntUnion
    {
        [FieldOffset(0)]
        public float FloatValue;

        [FieldOffset(0)]
        public int IntValue;
    }

    private static readonly RaycastHit[]
        interactionRayHitCache =
            new RaycastHit[64];

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

    private static byte pendingActionEdges;

    private sealed class RemoteUpperBodyInput
    {
        public int SourceActor = -1;
        public int TargetActor = -1;
        public int Sequence = -1;

        public Vector2 MovementInput =
            Vector2.zero;

        public bool PrimaryHeld;
        public bool SecondaryHeld;

        public float ReceivedTime = -100f;
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
                Interaction_DoInteractableRaycasts_CarrierOrigin_Patch));

        Patch(
            typeof(
                CharacterItems_DoUsing_RolePatch));

        Patch(
            typeof(
                CharacterItems_DoDropping_RolePatch));

        Patch(
            typeof(
                CharacterItems_DoSwitching_RolePatch));

        Patch(
            typeof(
                Character_CheckMovement_Patch));

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

    private static void ClearClimberCharacterInput(
        CharacterInput input)
    {
        if (input == null)
        {
            return;
        }

        input.movementInput =
            Vector2.zero;

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

        pendingActionEdges =
            0;
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

    private static void ApplyClimberInputToCarrier(
        CharacterInput input,
        Character carrier)
    {
        if (input == null ||
            carrier == null)
        {
            return;
        }

        bool valid =
            RemoteInputIsValid(
                carrier);

        bool heldValid =
            RemoteHeldInputIsValid(
                carrier);

        byte actionEdges =
            pendingActionEdges;

        pendingActionEdges =
            0;

        input.lookInput =
            Vector2.zero;

        input.interactWasPressed =
            false;

        input.interactIsPressed =
            false;

        input.interactWasReleased =
            false;

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

        input.pingWasPressed =
            false;

        input.emoteIsPressed =
            false;

        input.spectateLeftWasPressed =
            false;

        input.spectateRightWasPressed =
            false;

        if (carrier.data == null)
        {
            return;
        }

        Character climber =
            carrier
                .data
                .carriedPlayer;

        if (climber != null &&
            climber.data != null)
        {
            carrier.data.lookValues =
                climber.data.lookValues;
        }

        if (carrier.data.isClimbing)
        {
            input.movementInput =
                valid
                    ? remoteInput.MovementInput
                    : Vector2.zero;
        }

        if (!valid)
        {
            return;
        }

        input.usePrimaryWasPressed =
            (
                actionEdges &
                1
            ) != 0;

        input.usePrimaryIsPressed =
            heldValid &&
            remoteInput.PrimaryHeld;

        input.usePrimaryWasReleased =
            (
                actionEdges &
                2
            ) != 0;

        input.useSecondaryWasPressed =
            (
                actionEdges &
                4
            ) != 0;

        input.useSecondaryIsPressed =
            heldValid &&
            remoteInput.SecondaryHeld;

        input.useSecondaryWasReleased =
            (
                actionEdges &
                8
            ) != 0;
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

        int sequence =
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

        if (remoteInput.SourceActor !=
                sourceActor ||
            remoteInput.TargetActor !=
                targetActor)
        {
            remoteInput =
                new RemoteUpperBodyInput();

            remoteInput.SourceActor =
                sourceActor;

            remoteInput.TargetActor =
                targetActor;
        }

        if (sequence <=
            remoteInput.Sequence)
        {
            return;
        }

        Vector2 movementInput =
            new Vector2(
                ReadSingle(
                    payload,
                    ref offset),
                ReadSingle(
                    payload,
                    ref offset));

        byte flags =
            payload[offset++];

        byte actionEdges =
            payload[offset++];

        remoteInput.SourceActor =
            sourceActor;

        remoteInput.TargetActor =
            targetActor;

        remoteInput.Sequence =
            sequence;

        remoteInput.MovementInput =
            movementInput;

        remoteInput.PrimaryHeld =
            (
                flags &
                1
            ) != 0;

        remoteInput.SecondaryHeld =
            (
                flags &
                2
            ) != 0;

        pendingActionEdges |=
            actionEdges;

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
                if (__instance.interactWasPressed &&
                    !__instance.interactIsPressed)
                {
                    __instance.interactIsPressed =
                        true;
                }

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
                return true;
            }

            if (!IsCarrier(character))
            {
                return true;
            }

            __result =
                false;

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
                return true;
            }

            if (!IsCarrier(character))
            {
                return true;
            }

            __result =
                false;

            return false;
        }
    }


    private static bool IsPairCollider(
        Collider collider,
        Character climber,
        Character carrier)
    {
        if (collider == null)
        {
            return false;
        }

        Character owner =
            collider
                .GetComponentInParent<Character>();

        return
            owner != null &&
            (
                owner ==
                    climber ||
                owner ==
                    carrier
            );
    }

    private static bool TryGetStuckInteractable(
        Character climber,
        Vector3 lookDirection,
        out IInteractible result)
    {
        result =
            null;

        if (climber == null ||
            climber.data == null ||
            climber.refs == null ||
            climber.refs.afflictions == null)
        {
            return false;
        }

        float verticalAngle =
            Vector3.Angle(
                Vector3.down,
                lookDirection);

        if (verticalAngle <=
            10f)
        {
            foreach (
                StickyItemComponent sticky
                in StickyItemComponent
                    .ALL_STUCK_ITEMS)
            {
                if (sticky != null &&
                    sticky.item != null &&
                    sticky.item.Center().y <=
                        climber.Center.y)
                {
                    result =
                        sticky.item;

                    return true;
                }
            }

            foreach (
                ThornOnMe thorn
                in climber
                    .refs
                    .afflictions
                    .physicalThorns)
            {
                if (thorn != null &&
                    thorn.stuckIn &&
                    thorn.ICanAlwaysRemove &&
                    thorn.Center().y <=
                        climber.Center.y)
                {
                    result =
                        thorn;

                    return true;
                }
            }
        }
        else if (verticalAngle >=
            170f)
        {
            foreach (
                StickyItemComponent sticky
                in StickyItemComponent
                    .ALL_STUCK_ITEMS)
            {
                if (sticky != null &&
                    sticky.item != null &&
                    sticky.item.Center().y >=
                        climber.Center.y)
                {
                    result =
                        sticky.item;

                    return true;
                }
            }

            foreach (
                ThornOnMe thorn
                in climber
                    .refs
                    .afflictions
                    .physicalThorns)
            {
                if (thorn != null &&
                    thorn.stuckIn &&
                    thorn.ICanAlwaysRemove &&
                    thorn.Center().y >=
                        climber.Center.y)
                {
                    result =
                        thorn;

                    return true;
                }
            }
        }

        return false;
    }

    [HarmonyPatch(
        typeof(Interaction),
        "DoInteractableRaycasts")]
    private static class
        Interaction_DoInteractableRaycasts_CarrierOrigin_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Interaction __instance,
            ref IInteractible interactableResult,
            float overrideDistance,
            bool ignoreInteractable)
        {
            Character climber =
                Character.localCharacter;

            if (__instance == null ||
                climber == null ||
                !climber.IsLocal ||
                !IsClimber(
                    climber) ||
                climber.data == null)
            {
                return true;
            }

            Character carrier =
                climber
                    .data
                    .carrier;

            if (carrier == null ||
                carrier.data == null)
            {
                return true;
            }

            Vector3 rayDirection =
                climber
                    .data
                    .lookDirection;

            if (rayDirection.sqrMagnitude <
                0.000001f)
            {
                interactableResult =
                    null;

                return false;
            }

            rayDirection.Normalize();

            float distance =
                overrideDistance ==
                    -1f
                    ? __instance.distance
                    : overrideDistance;

            if (TryGetStuckInteractable(
                    climber,
                    rayDirection,
                    out interactableResult))
            {
                return false;
            }

            Vector3 rayOrigin =
                carrier.Head;

            Ray ray =
                new Ray(
                    rayOrigin,
                    rayDirection);

            int hitCount =
                HelperFunctions.LineCheckAll(
                    ray.origin,
                    ray.origin +
                        ray.direction *
                        distance,
                    HelperFunctions
                        .LayerType
                        .AllPhysical,
                    interactionRayHitCache,
                    0f,
                    QueryTriggerInteraction
                        .Collide);

            IInteractible best =
                null;

            RaycastHit nearestHit =
                default(
                    RaycastHit);

            nearestHit.distance =
                float.MaxValue;

            Item currentItem =
                climber
                    .data
                    .currentItem;

            float nearestDistance =
                distance;

            for (int i = 0;
                i < hitCount;
                i++)
            {
                RaycastHit hit =
                    interactionRayHitCache[i];

                if (hit.collider == null ||
                    hit.distance >=
                        nearestDistance ||
                    IsPairCollider(
                        hit.collider,
                        climber,
                        carrier))
                {
                    continue;
                }

                Item hitItem;

                if (Item.TryGetItemFromCollider(
                        hit.collider,
                        out hitItem) &&
                    hitItem != null &&
                    hitItem ==
                        currentItem)
                {
                    continue;
                }

                nearestDistance =
                    hit.distance;

                nearestHit =
                    hit;
            }

            if (nearestHit.collider !=
                null)
            {
                IInteractible direct =
                    nearestHit
                        .collider
                        .GetComponentInParent<
                            IInteractible>();

                if (direct != null &&
                    (
                        ignoreInteractable ||
                        direct.IsInteractible(
                            climber)
                    ))
                {
                    best =
                        direct;
                }
            }

            if (best == null)
            {
                float bestAngle =
                    float.MaxValue;

                int sphereHitCount =
                    Physics.SphereCastNonAlloc(
                        rayOrigin +
                            rayDirection *
                            (
                                __instance.area /
                                2f
                            ),
                        __instance.area,
                        rayDirection,
                        __instance
                            .sphereCastResults,
                        Mathf.Min(
                            nearestHit.distance,
                            distance),
                        HelperFunctions.GetMask(
                            HelperFunctions
                                .LayerType
                                .AllPhysical),
                        QueryTriggerInteraction
                            .Collide);

                int sphereCount =
                    Mathf.Min(
                        sphereHitCount,
                        __instance
                            .sphereCastResults
                            .Length);

                for (int i = 0;
                    i < sphereCount;
                    i++)
                {
                    RaycastHit hit =
                        __instance
                            .sphereCastResults[i];

                    if (hit.collider == null ||
                        IsPairCollider(
                            hit.collider,
                            climber,
                            carrier))
                    {
                        continue;
                    }

                    Item hitItem;

                    if (Item.TryGetItemFromCollider(
                            hit.collider,
                            out hitItem) &&
                        hitItem != null &&
                        hitItem ==
                            currentItem)
                    {
                        continue;
                    }

                    float angle =
                        Vector3.Angle(
                            hit.point -
                                rayOrigin,
                            rayDirection);

                    if (angle >=
                        bestAngle)
                    {
                        continue;
                    }

                    IInteractible candidate =
                        hit
                            .collider
                            .GetComponentInParent<
                                IInteractible>();

                    if (candidate == null ||
                        (
                            !ignoreInteractable &&
                            !candidate
                                .IsInteractible(
                                    climber)
                        ))
                    {
                        continue;
                    }

                    RaycastHit terrainHit =
                        HelperFunctions.LineCheck(
                            ray.origin,
                            hit.point,
                            HelperFunctions
                                .LayerType
                                .TerrainMap,
                            0f,
                            QueryTriggerInteraction
                                .Collide);

                    if (terrainHit.collider !=
                            null &&
                        terrainHit
                            .collider
                            .GetComponentInParent<
                                IInteractible>() !=
                            candidate)
                    {
                        continue;
                    }

                    bestAngle =
                        angle;

                    best =
                        candidate;
                }
            }

            interactableResult =
                best;

            return false;
        }
    }

    [HarmonyPatch(
        typeof(CharacterItems),
        "DoUsing")]
    private static class CharacterItems_DoUsing_RolePatch
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
                !IsCarrier(
                    character);
        }
    }

    [HarmonyPatch(
        typeof(CharacterItems),
        "DoDropping")]
    private static class CharacterItems_DoDropping_RolePatch
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
                !IsCarrier(
                    character);
        }
    }

    [HarmonyPatch(
        typeof(CharacterItems),
        "DoSwitching")]
    private static class CharacterItems_DoSwitching_RolePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterItems __instance)
        {
            Character character =
                ResolveCharacter(
                    __instance);

            if (IsCarrier(
                    character))
            {
                return false;
            }

            if (!IsClimber(
                    character) ||
                character.data == null)
            {
                return true;
            }

            Character carrier =
                character.data.carrier;

            if (carrier == null ||
                carrier.data == null)
            {
                return true;
            }

            return
                !carrier.data.isClimbing;
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

[DefaultExecutionOrder(-10000)]
public sealed class SeparateRoleRuntime :
    MonoBehaviour,
    IOnEventCallback
{
    private bool active;

    private int currentCarrierActor =
        -1;

    private int inputSequence;

    private float nextInputSendTime;
    private float nextInputHeartbeatTime;
    private bool hasSentInputState;

    private Vector2 lastSentMovementInput =
        Vector2.zero;

    private byte lastSentInputFlags;

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

        inputSequence =
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
            carrier == null ||
            climber.data == null ||
            carrier.data == null)
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

        Vector2 movementInput =
            gameplayInput &&
            carrier.data.isClimbing
                ? ReadMovementInputLocal()
                : Vector2.zero;

        bool physicalPrimaryAllowed =
            climber.data.currentItem ==
                null ||
            carrier.data.isClimbing;

        bool physicalSecondaryAllowed =
            climber.data.currentItem ==
                null;

        byte inputFlags =
            0;

        if (gameplayInput &&
            physicalPrimaryAllowed &&
            IsPressed(
                CharacterInput.action_usePrimary))
        {
            inputFlags |=
                1;
        }

        if (gameplayInput &&
            physicalSecondaryAllowed &&
            IsPressed(
                CharacterInput.action_useSecondary))
        {
            inputFlags |=
                2;
        }

        byte actionEdges =
            0;

        if (gameplayInput &&
            physicalPrimaryAllowed &&
            WasPressed(
                CharacterInput.action_usePrimary))
        {
            actionEdges |=
                1;
        }

        if (gameplayInput &&
            physicalPrimaryAllowed &&
            WasReleased(
                CharacterInput.action_usePrimary))
        {
            actionEdges |=
                2;
        }

        if (gameplayInput &&
            physicalSecondaryAllowed &&
            WasPressed(
                CharacterInput.action_useSecondary))
        {
            actionEdges |=
                4;
        }

        if (gameplayInput &&
            physicalSecondaryAllowed &&
            WasReleased(
                CharacterInput.action_useSecondary))
        {
            actionEdges |=
                8;
        }

        bool immediateStateChanged =
            inputFlags !=
                lastSentInputFlags;

        bool movementChanged =
            (
                movementInput -
                lastSentMovementInput
            ).sqrMagnitude >
                0.0004f;

        float now =
            Time.realtimeSinceStartup;

        bool sendReliable =
            immediateStateChanged ||
            actionEdges !=
                0;

        bool shouldSend =
            !hasSentInputState ||
            sendReliable ||
            (
                carrier.data.isClimbing &&
                now >=
                    nextInputSendTime &&
                movementChanged
            ) ||
            (
                inputFlags != 0 &&
                now >=
                    nextInputHeartbeatTime
            );

        if (!shouldSend)
        {
            return;
        }

        inputSequence++;

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

        SeparateRole.WriteInt32Local(
            payload,
            ref offset,
            inputSequence);

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

        payload[offset++] =
            actionEdges;

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

    private static Vector2 ReadMovementInputLocal()
    {
        Vector2 movement =
            Vector2.zero;

        if (CharacterInput.action_move !=
            null)
        {
            movement +=
                CharacterInput
                    .action_move
                    .ReadValue<Vector2>();
        }

        if (CharacterInput.action_moveForward !=
                null &&
            CharacterInput
                .action_moveForward
                .IsPressed())
        {
            movement +=
                Vector2.up;
        }

        if (CharacterInput.action_moveBackward !=
                null &&
            CharacterInput
                .action_moveBackward
                .IsPressed())
        {
            movement -=
                Vector2.up;
        }

        if (CharacterInput.action_moveRight !=
                null &&
            CharacterInput
                .action_moveRight
                .IsPressed())
        {
            movement +=
                Vector2.right;
        }

        if (CharacterInput.action_moveLeft !=
                null &&
            CharacterInput
                .action_moveLeft
                .IsPressed())
        {
            movement -=
                Vector2.right;
        }

        return
            Vector2.ClampMagnitude(
                movement,
                1f);
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

    private static bool WasReleased(
        UnityEngine.InputSystem.InputAction action)
    {
        return
            action != null &&
            action.WasReleasedThisFrame();
    }

    private void OnDestroy()
    {
        Deactivate();
    }
}
