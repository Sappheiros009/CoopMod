using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public static class ItemFix
{
    private const string HarmonyId =
        "com.peak.coopmod.itemfix";

    private const byte ItemMovementEventCode =
        186;

    private const byte RescueForceAction =
        1;

    private const byte RescueFallAction =
        2;

    private const byte RescueLetGoAction =
        3;

    private const byte WarpAction =
        4;

    private const int HeaderLength =
        9;

    private const int RescueForcePayloadLength =
        33;

    private const int RescueFallPayloadLength =
        13;

    private const int RescueLetGoPayloadLength =
        13;

    private const int WarpPayloadLength =
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

    private static Harmony harmony;

    private static RescueHook activeRescueHook;

    private static Character activeRescueClimber;

    private static bool activeRescueCanSend;

    private static RescueHook activeSelfRescueHook;

    private struct ThrownItemState
    {
        public Character Character;

        public float Time;
    }

    private static readonly Dictionary<int, ThrownItemState>
        thrownItemStates =
            new Dictionary<int, ThrownItemState>();

    private static readonly Dictionary<int, float>
        recentWarpItems =
            new Dictionary<int, float>();

    private static readonly HashSet<Character>
        trackedWarpClimbers =
            new HashSet<Character>();

    private static readonly int[]
        targetActors =
            new int[1];

    private static readonly RaiseEventOptions
        raiseEventOptions =
            new RaiseEventOptions();

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
                RescueHook_FixedUpdate_ContextPatch));

        Patch(
            typeof(
                Character_AddForce_RescueTransportPatch));

        Patch(
            typeof(
                RescueHook_RPCA_RescueWall_TransportPatch));

        Patch(
            typeof(
                RescueHook_RPCA_LetGo_TransportPatch));

        Patch(
            typeof(
                RescueHook_OnDestroy_TransportPatch));

        Patch(
            typeof(
                Item_RPC_SetThrownData_TrackPatch));

        Patch(
            typeof(
                WarpOnThrow_OnCollisionEnter_TransportPatch));

        Patch(
            typeof(
                Character_WarpPlayerRPC_CarriedCollisionPatch));

        PhotonNetwork
            .NetworkingClient
            .EventReceived +=
                HandleEvent;
    }

    public static void Shutdown()
    {
        PhotonNetwork
            .NetworkingClient
            .EventReceived -=
                HandleEvent;

        foreach (
            Character climber
            in trackedWarpClimbers)
        {
            if (climber != null)
            {
                climber.WarpCompleted -=
                    OnWarpCompleted;
            }
        }

        trackedWarpClimbers.Clear();

        recentWarpItems.Clear();

        thrownItemStates.Clear();

        activeRescueHook =
            null;

        activeRescueClimber =
            null;

        activeRescueCanSend =
            false;

        activeSelfRescueHook =
            null;

        if (harmony != null)
        {
            harmony.UnpatchSelf();

            harmony =
                null;
        }
    }

    private static void Patch(
        Type patchType)
    {
        harmony
            .CreateClassProcessor(
                patchType)
            .Patch();
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

    private static bool TryGetCarrier(
        Character climber,
        out Character carrier)
    {
        carrier =
            null;

        if (climber == null ||
            climber.data == null ||
            !climber.IsLocal ||
            !SeparateRole.IsClimber(
                climber))
        {
            return false;
        }

        carrier =
            climber
                .data
                .carrier;

        return
            carrier != null &&
            carrier.data != null &&
            SeparateRole.IsCarrier(
                carrier);
    }

    private static bool TryGetCarrierReplica(
        Character climber,
        out Character carrier)
    {
        carrier =
            null;

        if (climber == null ||
            climber.data == null ||
            !SeparateRole.IsClimber(
                climber))
        {
            return false;
        }

        carrier =
            climber
                .data
                .carrier;

        return
            carrier != null &&
            carrier.data != null &&
            carrier.data.carriedPlayer ==
                climber &&
            SeparateRole.IsCarrier(
                carrier);
    }

    private static void ApplyAcceleration(
        Character carrier,
        Vector3 force,
        float minRandomMultiplier,
        float maxRandomMultiplier)
    {
        if (carrier == null ||
            carrier.refs == null ||
            carrier.refs.ragdoll == null ||
            carrier.refs.ragdoll.partList == null)
        {
            return;
        }

        for (int i = 0;
            i < carrier.refs.ragdoll.partList.Count;
            i++)
        {
            Bodypart part =
                carrier
                    .refs
                    .ragdoll
                    .partList[i];

            if (part == null)
            {
                continue;
            }

            Vector3 appliedForce =
                force;

            if (minRandomMultiplier !=
                maxRandomMultiplier)
            {
                appliedForce *=
                    UnityEngine.Random.Range(
                        minRandomMultiplier,
                        maxRandomMultiplier);
            }

            part.AddForce(
                appliedForce,
                ForceMode.Acceleration);
        }
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

    private static bool PrepareTarget(
        Character climber,
        Character carrier,
        out int sourceActor,
        out int targetActor)
    {
        sourceActor =
            GetActorNumber(
                climber);

        targetActor =
            GetActorNumber(
                carrier);

        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null ||
            sourceActor <= 0 ||
            targetActor <= 0)
        {
            return false;
        }

        targetActors[0] =
            targetActor;

        raiseEventOptions.TargetActors =
            targetActors;

        return true;
    }

    private static void SendRescueForce(
        Character climber,
        Character carrier,
        Vector3 force,
        float minRandomMultiplier,
        float maxRandomMultiplier,
        float extraDrag)
    {
        if (!PrepareTarget(
                climber,
                carrier,
                out int sourceActor,
                out int targetActor))
        {
            return;
        }

        byte[] payload =
            new byte[
                RescueForcePayloadLength];

        int offset =
            0;

        payload[offset++] =
            RescueForceAction;

        WriteInt32(
            payload,
            ref offset,
            sourceActor);

        WriteInt32(
            payload,
            ref offset,
            targetActor);

        WriteSingle(
            payload,
            ref offset,
            force.x);

        WriteSingle(
            payload,
            ref offset,
            force.y);

        WriteSingle(
            payload,
            ref offset,
            force.z);

        WriteSingle(
            payload,
            ref offset,
            minRandomMultiplier);

        WriteSingle(
            payload,
            ref offset,
            maxRandomMultiplier);

        WriteSingle(
            payload,
            ref offset,
            extraDrag);

        PhotonNetwork.RaiseEvent(
            ItemMovementEventCode,
            payload,
            raiseEventOptions,
            SendOptions.SendUnreliable);
    }

    private static void SendRescueFall(
        Character climber,
        Character carrier,
        float seconds)
    {
        if (!PrepareTarget(
                climber,
                carrier,
                out int sourceActor,
                out int targetActor))
        {
            return;
        }

        byte[] payload =
            new byte[
                RescueFallPayloadLength];

        int offset =
            0;

        payload[offset++] =
            RescueFallAction;

        WriteInt32(
            payload,
            ref offset,
            sourceActor);

        WriteInt32(
            payload,
            ref offset,
            targetActor);

        WriteSingle(
            payload,
            ref offset,
            seconds);

        PhotonNetwork.RaiseEvent(
            ItemMovementEventCode,
            payload,
            raiseEventOptions,
            SendOptions.SendReliable);
    }

    private static void SendRescueLetGo(
        Character climber,
        Character carrier,
        float extraDrag)
    {
        if (!PrepareTarget(
                climber,
                carrier,
                out int sourceActor,
                out int targetActor))
        {
            return;
        }

        byte[] payload =
            new byte[
                RescueLetGoPayloadLength];

        int offset =
            0;

        payload[offset++] =
            RescueLetGoAction;

        WriteInt32(
            payload,
            ref offset,
            sourceActor);

        WriteInt32(
            payload,
            ref offset,
            targetActor);

        WriteSingle(
            payload,
            ref offset,
            extraDrag);

        PhotonNetwork.RaiseEvent(
            ItemMovementEventCode,
            payload,
            raiseEventOptions,
            SendOptions.SendReliable);
    }

    private static void SendWarp(
        Character climber,
        Character carrier,
        Vector3 position,
        bool poof)
    {
        if (!PrepareTarget(
                climber,
                carrier,
                out int sourceActor,
                out int targetActor))
        {
            return;
        }

        byte[] payload =
            new byte[
                WarpPayloadLength];

        int offset =
            0;

        payload[offset++] =
            WarpAction;

        WriteInt32(
            payload,
            ref offset,
            sourceActor);

        WriteInt32(
            payload,
            ref offset,
            targetActor);

        WriteSingle(
            payload,
            ref offset,
            position.x);

        WriteSingle(
            payload,
            ref offset,
            position.y);

        WriteSingle(
            payload,
            ref offset,
            position.z);

        payload[offset++] =
            poof
                ? (byte)1
                : (byte)0;

        PhotonNetwork.RaiseEvent(
            ItemMovementEventCode,
            payload,
            raiseEventOptions,
            SendOptions.SendReliable);
    }

    private static void HandleEvent(
        EventData photonEvent)
    {
        if (photonEvent == null ||
            photonEvent.Code !=
                ItemMovementEventCode)
        {
            return;
        }

        byte[] payload =
            photonEvent.CustomData
                as byte[];

        if (payload == null ||
            payload.Length <
                HeaderLength)
        {
            return;
        }

        int offset =
            0;

        byte action =
            payload[offset++];

        int sourceActor =
            ReadInt32(
                payload,
                ref offset);

        int targetActor =
            ReadInt32(
                payload,
                ref offset);

        Character carrier =
            Character.localCharacter;

        if (carrier == null ||
            !carrier.IsLocal ||
            !SeparateRole.IsCarrier(
                carrier))
        {
            return;
        }

        int localActor =
            GetActorNumber(
                carrier);

        Character climber =
            carrier
                .data
                .carriedPlayer;

        int expectedSourceActor =
            GetActorNumber(
                climber);

        if (targetActor !=
                localActor ||
            sourceActor !=
                expectedSourceActor ||
            photonEvent.Sender !=
                sourceActor)
        {
            return;
        }

        if (action ==
            RescueForceAction)
        {
            if (payload.Length <
                RescueForcePayloadLength)
            {
                return;
            }

            Vector3 force =
                new Vector3(
                    ReadSingle(
                        payload,
                        ref offset),
                    ReadSingle(
                        payload,
                        ref offset),
                    ReadSingle(
                        payload,
                        ref offset));

            float minRandomMultiplier =
                ReadSingle(
                    payload,
                    ref offset);

            float maxRandomMultiplier =
                ReadSingle(
                    payload,
                    ref offset);

            float extraDrag =
                ReadSingle(
                    payload,
                    ref offset);

            if (carrier.refs != null &&
                carrier.refs.movement != null)
            {
                carrier
                    .refs
                    .movement
                    .ApplyExtraDrag(
                        extraDrag,
                        true);
            }

            carrier.data.sinceGrounded =
                0f;

            ApplyAcceleration(
                carrier,
                force,
                minRandomMultiplier,
                maxRandomMultiplier);

            return;
        }

        if (action ==
            RescueFallAction)
        {
            if (payload.Length <
                RescueFallPayloadLength ||
                carrier.photonView == null)
            {
                return;
            }

            float seconds =
                ReadSingle(
                    payload,
                    ref offset);

            carrier.photonView.RPC(
                "RPCA_Fall",
                RpcTarget.All,
                seconds,
                0f);

            return;
        }

        if (action ==
            RescueLetGoAction)
        {
            if (payload.Length <
                RescueLetGoPayloadLength)
            {
                return;
            }

            float extraDrag =
                ReadSingle(
                    payload,
                    ref offset);

            if (carrier.refs != null &&
                carrier.refs.movement != null)
            {
                carrier
                    .refs
                    .movement
                    .ApplyExtraDrag(
                        extraDrag,
                        true);
            }

            return;
        }

        if (action ==
            WarpAction)
        {
            if (payload.Length <
                WarpPayloadLength ||
                carrier.photonView == null)
            {
                return;
            }

            Vector3 position =
                new Vector3(
                    ReadSingle(
                        payload,
                        ref offset),
                    ReadSingle(
                        payload,
                        ref offset),
                    ReadSingle(
                        payload,
                        ref offset));

            bool poof =
                payload[offset] !=
                    0;

            carrier.photonView.RPC(
                "WarpPlayerRPC",
                RpcTarget.All,
                position,
                poof);
        }
    }

    private static void TrackThrownItem(
        Item item,
        int characterViewId)
    {
        if (item == null)
        {
            return;
        }

        PhotonView characterView =
            PhotonNetwork.GetPhotonView(
                characterViewId);

        Character character =
            null;

        if (characterView != null)
        {
            characterView.TryGetComponent<Character>(
                out character);
        }

        thrownItemStates[
            item.GetInstanceID()] =
                new ThrownItemState
                {
                    Character =
                        character,
                    Time =
                        Time.time
                };
    }

    private static bool TryGetThrownItemState(
        Item item,
        out ThrownItemState state)
    {
        state =
            default;

        if (item == null)
        {
            return false;
        }

        return
            thrownItemStates.TryGetValue(
                item.GetInstanceID(),
                out state) &&
            state.Character != null;
    }

    private static bool TryMarkWarpItem(
        int viewId)
    {
        if (viewId <= 0)
        {
            return true;
        }

        float now =
            Time.realtimeSinceStartup;

        if (recentWarpItems.TryGetValue(
                viewId,
                out float expiry) &&
            now <
                expiry)
        {
            return false;
        }

        recentWarpItems[viewId] =
            now + 3f;

        return true;
    }

    private static void TrackWarpClimber(
        Character climber)
    {
        if (climber == null ||
            !SeparateRole.IsClimber(
                climber) ||
            trackedWarpClimbers.Contains(
                climber))
        {
            return;
        }

        trackedWarpClimbers.Add(
            climber);

        climber.WarpCompleted +=
            OnWarpCompleted;
    }

    private static void OnWarpCompleted(
        Character climber)
    {
        if (climber != null)
        {
            climber.WarpCompleted -=
                OnWarpCompleted;
        }

        trackedWarpClimbers.Remove(
            climber);

        if (climber == null ||
            !SeparateRole.IsClimber(
                climber) ||
            climber.refs == null ||
            climber.refs.ragdoll == null)
        {
            return;
        }

        climber
            .refs
            .ragdoll
            .ToggleCollision(
                false);
    }

    [HarmonyPatch(
        typeof(RescueHook),
        "FixedUpdate")]
    private static class
        RescueHook_FixedUpdate_ContextPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            RescueHook __instance)
        {
            activeRescueHook =
                null;

            activeRescueClimber =
                null;

            activeRescueCanSend =
                false;

            Character climber =
                __instance != null
                    ? __instance.playerHoldingItem
                    : null;

            if (climber == null ||
                !SeparateRole.IsClimber(
                    climber))
            {
                return;
            }

            activeRescueHook =
                __instance;

            activeRescueClimber =
                climber;

            activeRescueCanSend =
                climber.IsLocal &&
                __instance.photonView != null &&
                __instance.photonView.IsMine;
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception)
        {
            activeRescueHook =
                null;

            activeRescueClimber =
                null;

            activeRescueCanSend =
                false;

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(Character),
        "AddForce",
        new Type[]
        {
            typeof(Vector3),
            typeof(float),
            typeof(float)
        })]
    private static class
        Character_AddForce_RescueTransportPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            Character __instance,
            Vector3 __0,
            float __1,
            float __2)
        {
            if (activeRescueHook == null ||
                activeRescueClimber == null ||
                __instance !=
                    activeRescueClimber ||
                !SeparateRole.IsClimber(
                    __instance))
            {
                return true;
            }

            if (activeRescueCanSend &&
                TryGetCarrier(
                    __instance,
                    out Character carrier))
            {
                SendRescueForce(
                    __instance,
                    carrier,
                    __0,
                    __1,
                    __2,
                    activeRescueHook.extraDragSelf);
            }

            return false;
        }
    }

    [HarmonyPatch(
        typeof(RescueHook),
        nameof(
            RescueHook.RPCA_RescueWall),
        new Type[]
        {
            typeof(bool),
            typeof(Vector3)
        })]
    private static class
        RescueHook_RPCA_RescueWall_TransportPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            RescueHook __instance)
        {
            Character climber =
                __instance != null
                    ? __instance.playerHoldingItem
                    : null;

            if (climber == null ||
                !climber.IsLocal ||
                __instance.photonView == null ||
                !__instance.photonView.IsMine ||
                !TryGetCarrier(
                    climber,
                    out Character carrier))
            {
                return;
            }

            activeSelfRescueHook =
                __instance;

            SendRescueFall(
                climber,
                carrier,
                __instance.selfFallSeconds);
        }
    }

    [HarmonyPatch(
        typeof(RescueHook),
        "RPCA_LetGo")]
    private static class
        RescueHook_RPCA_LetGo_TransportPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            RescueHook __instance)
        {
            if (__instance == null ||
                __instance !=
                    activeSelfRescueHook)
            {
                return;
            }

            Character climber =
                __instance.playerHoldingItem;

            if (climber != null &&
                climber.IsLocal &&
                __instance.photonView != null &&
                __instance.photonView.IsMine &&
                TryGetCarrier(
                    climber,
                    out Character carrier))
            {
                SendRescueLetGo(
                    climber,
                    carrier,
                    __instance.extraDragSelf);
            }

            activeSelfRescueHook =
                null;
        }
    }

    [HarmonyPatch(
        typeof(RescueHook),
        "OnDestroy")]
    private static class
        RescueHook_OnDestroy_TransportPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            RescueHook __instance)
        {
            if (__instance ==
                activeSelfRescueHook)
            {
                activeSelfRescueHook =
                    null;
            }
        }
    }

    [HarmonyPatch(
        typeof(Item),
        nameof(
            Item.RPC_SetThrownData),
        new Type[]
        {
            typeof(int),
            typeof(float)
        })]
    private static class
        Item_RPC_SetThrownData_TrackPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Item __instance,
            int characterID)
        {
            TrackThrownItem(
                __instance,
                characterID);
        }
    }

    [HarmonyPatch(
        typeof(Peak.WarpOnThrow),
        "OnCollisionEnter",
        new Type[]
        {
            typeof(Collision)
        })]
    private static class
        WarpOnThrow_OnCollisionEnter_TransportPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            Peak.WarpOnThrow __instance,
            Collision collision)
        {
            if (__instance == null ||
                collision == null ||
                __instance.item == null ||
                __instance.item.itemState !=
                    ItemState.Ground ||
                __instance.photonView == null ||
                !__instance.photonView.IsMine ||
                collision.contactCount <=
                    0 ||
                !TryGetThrownItemState(
                    __instance.item,
                    out ThrownItemState throwState))
            {
                return;
            }

            Character climber =
                throwState.Character;

            if (!TryGetCarrierReplica(
                    climber,
                    out Character carrier) ||
                carrier.photonView == null)
            {
                return;
            }

            int layer =
                collision
                    .gameObject
                    .layer;

            if ((
                    HelperFunctions
                        .terrainMapMask &
                    1 << layer
                ) == 0 ||
                collision
                    .relativeVelocity
                    .magnitude <=
                        __instance.minVelocity)
            {
                return;
            }

            float elapsed =
                Time.time -
                throwState.Time;

            if (elapsed <=
                    __instance.minTime ||
                elapsed >=
                    __instance.maxTime)
            {
                return;
            }

            if (!TryMarkWarpItem(
                    __instance
                        .photonView
                        .ViewID))
            {
                return;
            }

            ContactPoint contact =
                collision
                    .contacts[0];

            Vector3 position =
                contact.point +
                contact.normal *
                __instance
                    .moveAwayFromWallDistance;

            carrier
                .photonView
                .RPC(
                    "WarpPlayerRPC",
                    RpcTarget.All,
                    position,
                    true);
        }
    }

    [HarmonyPatch(
        typeof(Character),
        nameof(
            Character.WarpPlayerRPC),
        new Type[]
        {
            typeof(Vector3),
            typeof(bool)
        })]
    private static class
        Character_WarpPlayerRPC_CarriedCollisionPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Character __instance)
        {
            if (__instance == null ||
                !SeparateRole.IsClimber(
                    __instance))
            {
                return;
            }

            TrackWarpClimber(
                __instance);
        }
    }
}
