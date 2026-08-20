using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core;

public static class Rolechanger
{
    private const string HarmonyId =
        "com.peak.coopmod.rolechanger";

    private const byte SwapEventCode =
        194;

    private static Harmony harmony;

    private static CoopMod plugin;

    private static RolechangerRuntime runtime;

    private static readonly Dictionary<int, int>
        LockedRiderToCarrier =
            new Dictionary<int, int>();

    public static void Initialize(
        CoopMod owner)
    {
        if (harmony != null ||
            owner == null)
        {
            return;
        }

        plugin =
            owner;

        harmony =
            new Harmony(
                HarmonyId);

        Patch(
            typeof(
                MapHandler_GoToSegment_Patch));

        Patch(
            typeof(
                CharacterCarrying_Update_Patch));

        Patch(
            typeof(
                CharacterCarrying_Drop_Patch));

        Patch(
            typeof(
                CharacterCarrying_RPCA_Drop_Patch));

        runtime =
            owner
                .gameObject
                .GetComponent<
                    RolechangerRuntime>();

        if (runtime == null)
        {
            runtime =
                owner
                    .gameObject
                    .AddComponent<
                        RolechangerRuntime>();
        }

        runtime.InitializeRuntime();
    }

    public static void Shutdown()
    {
        LockedRiderToCarrier.Clear();

        if (runtime != null)
        {
            runtime.ShutdownRuntime();

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

        plugin =
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

    internal static void RequestSegmentSwap(
        int targetSegment)
    {
        if (runtime == null)
        {
            return;
        }

        runtime.RequestSegmentSwap(
            targetSegment);
    }

    internal static void HandleSwapEvent(
        EventData photonEvent)
    {
        if (photonEvent == null ||
            photonEvent.Code !=
                SwapEventCode)
        {
            return;
        }

        byte[] payload =
            photonEvent.CustomData
                as byte[];

        if (payload == null ||
            payload.Length <
                12)
        {
            return;
        }

        int offset =
            0;

        int targetSegment =
            ReadInt32(
                payload,
                ref offset);

        int oldCarrierActor =
            ReadInt32(
                payload,
                ref offset);

        int oldRiderActor =
            ReadInt32(
                payload,
                ref offset);

        ApplySwap(
            targetSegment,
            oldCarrierActor,
            oldRiderActor);
    }

    internal static bool SwapPair(
        int targetSegment,
        Character oldCarrier,
        Character oldRider)
    {
        if (!PhotonNetwork.IsMasterClient ||
            !PhotonNetwork.InRoom ||
            oldCarrier == null ||
            oldRider == null)
        {
            return false;
        }

        int oldCarrierActor =
            GetActorNumber(
                oldCarrier);

        int oldRiderActor =
            GetActorNumber(
                oldRider);

        if (oldCarrierActor <= 0 ||
            oldRiderActor <= 0 ||
            oldCarrierActor ==
                oldRiderActor)
        {
            return false;
        }

        if (!ApplySwap(
                targetSegment,
                oldCarrierActor,
                oldRiderActor))
        {
            return false;
        }

        byte[] payload =
            new byte[12];

        int offset =
            0;

        WriteInt32(
            payload,
            ref offset,
            targetSegment);

        WriteInt32(
            payload,
            ref offset,
            oldCarrierActor);

        WriteInt32(
            payload,
            ref offset,
            oldRiderActor);

        RaiseEventOptions options =
            new RaiseEventOptions
            {
                Receivers =
                    ReceiverGroup.Others
            };

        PhotonNetwork.RaiseEvent(
            SwapEventCode,
            payload,
            options,
            SendOptions.SendReliable);

        return true;
    }

    internal static void RemovePairsForActor(
        int actorNumber)
    {
        if (actorNumber <= 0 ||
            LockedRiderToCarrier.Count ==
                0)
        {
            return;
        }

        List<int> ridersToRemove =
            null;

        foreach (
            KeyValuePair<int, int>
                pair
            in LockedRiderToCarrier)
        {
            if (pair.Key ==
                    actorNumber ||
                pair.Value ==
                    actorNumber)
            {
                if (ridersToRemove ==
                    null)
                {
                    ridersToRemove =
                        new List<int>();
                }

                ridersToRemove.Add(
                    pair.Key);
            }
        }

        if (ridersToRemove == null)
        {
            return;
        }

        for (int i = 0;
            i < ridersToRemove.Count;
            i++)
        {
            LockedRiderToCarrier.Remove(
                ridersToRemove[i]);
        }
    }

    internal static void ClearPairs()
    {
        LockedRiderToCarrier.Clear();
    }

    private static bool ApplySwap(
        int targetSegment,
        int oldCarrierActor,
        int oldRiderActor)
    {
        Character oldCarrier;
        Character oldRider;

        if (!PlayerHandler.TryGetCharacter(
                oldCarrierActor,
                out oldCarrier) ||
            !PlayerHandler.TryGetCharacter(
                oldRiderActor,
                out oldRider))
        {
            return false;
        }

        if (!CharacterReady(
                oldCarrier) ||
            !CharacterReady(
                oldRider))
        {
            return false;
        }

        if (oldCarrier.data.carriedPlayer !=
                oldRider ||
            !oldRider.data.isCarried ||
            oldRider.data.carrier !=
                oldCarrier)
        {
            return false;
        }

        Character newCarrier =
            oldRider;

        Character newRider =
            oldCarrier;

        RemovePairsForActor(
            oldCarrierActor);

        RemovePairsForActor(
            oldRiderActor);

        ClearCarriedState(
            oldCarrier,
            oldRider);

        if (newCarrier.refs.items !=
            null)
        {
            newCarrier
                .refs
                .items
                .EquipSlot(
                    Optionable<byte>.None);
        }

        ApplyCarriedState(
            newCarrier,
            newRider);

        int newCarrierActor =
            oldRiderActor;

        int newRiderActor =
            oldCarrierActor;

        LockedRiderToCarrier[
            newRiderActor] =
            newCarrierActor;

        return true;
    }

    private static void ClearCarriedState(
        Character carrier,
        Character rider)
    {
        if (carrier == null ||
            rider == null ||
            carrier.data == null ||
            rider.data == null)
        {
            return;
        }

        if (rider.refs != null)
        {
            if (rider.refs.ragdoll !=
                null)
            {
                rider
                    .refs
                    .ragdoll
                    .ToggleCollision(
                        true);
            }

            if (rider.refs.animator !=
                null)
            {
                rider
                    .refs
                    .animator
                    .SetBool(
                        "IsCarried",
                        false);
            }
        }

        rider.data.isCarried =
            false;

        rider.data.carrier =
            null;

        carrier.data.carriedPlayer =
            null;
    }

    private static void ApplyCarriedState(
        Character carrier,
        Character rider)
    {
        if (carrier == null ||
            rider == null ||
            carrier.data == null ||
            rider.data == null)
        {
            return;
        }

        if (rider.refs != null)
        {
            if (rider.refs.ragdoll !=
                null)
            {
                rider
                    .refs
                    .ragdoll
                    .ToggleCollision(
                        false);
            }

            if (rider.refs.animator !=
                null)
            {
                rider
                    .refs
                    .animator
                    .SetBool(
                        "IsCarried",
                        true);
            }

            if (rider.refs.afflictions !=
                null)
            {
                rider
                    .refs
                    .afflictions
                    .SubtractStatus(
                        CharacterAfflictions
                            .STATUSTYPE
                            .FlyTrap,
                        1f,
                        true,
                        false);

                rider
                    .refs
                    .afflictions
                    .SubtractStatus(
                        CharacterAfflictions
                            .STATUSTYPE
                            .Web,
                        1f,
                        true,
                        false);
            }
        }

        rider.data.deathTimer =
            0f;

        rider.data.passOutValue =
            0f;

        rider.data.passedOutOnTheBeach =
            0f;

        rider.data.fallSeconds =
            0f;

        rider.data.passedOut =
            false;

        rider.data.fullyPassedOut =
            false;

        rider.data.ragdollControlClamp =
            1f;

        rider.data.currentRagdollControll =
            1f;

        rider.data.isCarried =
            true;

        rider.data.carrier =
            carrier;

        carrier.data.carriedPlayer =
            rider;
    }

    private static bool CharacterReady(
        Character character)
    {
        return
            character != null &&
            character.data != null &&
            character.refs != null &&
            character.refs.carriying !=
                null &&
            character.refs.ragdoll !=
                null &&
            character.photonView !=
                null &&
            character.player !=
                null;
    }

    private static int GetActorNumber(
        Character character)
    {
        if (character == null ||
            character.photonView == null ||
            character.photonView.Owner ==
                null)
        {
            return -1;
        }

        return
            character
                .photonView
                .Owner
                .ActorNumber;
    }

    private static bool IsLockedPair(
        Character carrier,
        Character rider)
    {
        if (carrier == null ||
            rider == null)
        {
            return false;
        }

        int carrierActor =
            GetActorNumber(
                carrier);

        int riderActor =
            GetActorNumber(
                rider);

        if (carrierActor <= 0 ||
            riderActor <= 0)
        {
            return false;
        }

        int registeredCarrier;

        if (!LockedRiderToCarrier.TryGetValue(
                riderActor,
                out registeredCarrier))
        {
            return false;
        }

        return
            registeredCarrier ==
            carrierActor;
    }

    private static bool ShouldAllowRelease(
        Character carrier,
        Character rider)
    {
        if (carrier == null ||
            rider == null ||
            carrier.data == null ||
            rider.data == null)
        {
            return true;
        }

        if (carrier.data.dead ||
            rider.data.dead)
        {
            return true;
        }

        if (!rider.data.isCarried)
        {
            return true;
        }

        if (rider.data.carrier !=
            carrier)
        {
            return true;
        }

        if (carrier.data.carriedPlayer !=
            rider)
        {
            return true;
        }

        return false;
    }

    private static void WriteInt32(
        byte[] buffer,
        ref int offset,
        int value)
    {
        buffer[offset++] =
            (byte)value;

        buffer[offset++] =
            (byte)(
                value >>
                8);

        buffer[offset++] =
            (byte)(
                value >>
                16);

        buffer[offset++] =
            (byte)(
                value >>
                24);
    }

    private static int ReadInt32(
        byte[] buffer,
        ref int offset)
    {
        int value =
            buffer[offset] |
            (
                buffer[offset + 1] <<
                8
            ) |
            (
                buffer[offset + 2] <<
                16
            ) |
            (
                buffer[offset + 3] <<
                24
            );

        offset +=
            4;

        return
            value;
    }

    [HarmonyPatch(
        typeof(MapHandler),
        nameof(
            MapHandler.GoToSegment),
        new Type[]
        {
            typeof(Segment)
        })]
    private static class
        MapHandler_GoToSegment_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Segment s)
        {
            if (!PhotonNetwork.InRoom ||
                !PhotonNetwork.IsMasterClient)
            {
                return;
            }

            RequestSegmentSwap(
                (int)s);
        }
    }

    [HarmonyPatch(
        typeof(CharacterCarrying),
        "Update")]
    private static class
        CharacterCarrying_Update_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterCarrying __instance)
        {
            if (__instance == null)
            {
                return true;
            }

            Character carrier =
                __instance
                    .GetComponent<Character>();

            if (carrier == null ||
                carrier.data == null)
            {
                return true;
            }

            Character rider =
                carrier
                    .data
                    .carriedPlayer;

            if (!IsLockedPair(
                    carrier,
                    rider))
            {
                return true;
            }

            return
                ShouldAllowRelease(
                    carrier,
                    rider);
        }
    }

    [HarmonyPatch(
        typeof(CharacterCarrying),
        "Drop",
        new Type[]
        {
            typeof(Character)
        })]
    private static class
        CharacterCarrying_Drop_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterCarrying __instance,
            Character target)
        {
            if (__instance == null ||
                target == null)
            {
                return true;
            }

            Character carrier =
                __instance
                    .GetComponent<Character>();

            if (!IsLockedPair(
                    carrier,
                    target))
            {
                return true;
            }

            return
                ShouldAllowRelease(
                    carrier,
                    target);
        }
    }

    [HarmonyPatch(
        typeof(CharacterCarrying),
        nameof(
            CharacterCarrying
                .RPCA_Drop),
        new Type[]
        {
            typeof(PhotonView)
        })]
    private static class
        CharacterCarrying_RPCA_Drop_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterCarrying __instance,
            PhotonView targetView)
        {
            if (__instance == null ||
                targetView == null)
            {
                return true;
            }

            Character carrier =
                __instance
                    .GetComponent<Character>();

            Character rider =
                targetView
                    .GetComponent<Character>();

            if (!IsLockedPair(
                    carrier,
                    rider))
            {
                return true;
            }

            return
                ShouldAllowRelease(
                    carrier,
                    rider);
        }
    }
}

public sealed class RolechangerRuntime :
    MonoBehaviourPunCallbacks,
    IOnEventCallback
{
    private const float RetryInterval =
        0.25f;

    private bool runtimeInitialized;

    private int pendingSegment =
        -1;

    private int lastProcessedSegment =
        -1;

    private float nextRetryTime;

    internal void InitializeRuntime()
    {
        if (runtimeInitialized)
        {
            return;
        }

        runtimeInitialized =
            true;

        SceneManager.sceneLoaded +=
            OnSceneLoaded;
    }

    internal void ShutdownRuntime()
    {
        if (!runtimeInitialized)
        {
            return;
        }

        SceneManager.sceneLoaded -=
            OnSceneLoaded;

        pendingSegment =
            -1;

        lastProcessedSegment =
            -1;

        runtimeInitialized =
            false;
    }

    public override void OnEnable()
    {
        base.OnEnable();
    }

    public override void OnDisable()
    {
        base.OnDisable();
    }

    public void OnEvent(
        EventData photonEvent)
    {
        Rolechanger.HandleSwapEvent(
            photonEvent);
    }

    public override void OnLeftRoom()
    {
        pendingSegment =
            -1;

        lastProcessedSegment =
            -1;

        Rolechanger.ClearPairs();
    }

    public override void OnPlayerLeftRoom(
        Photon.Realtime.Player otherPlayer)
    {
        if (otherPlayer == null)
        {
            return;
        }

        Rolechanger.RemovePairsForActor(
            otherPlayer.ActorNumber);
    }

    public override void OnMasterClientSwitched(
        Photon.Realtime.Player newMasterClient)
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            return;
        }

        pendingSegment =
            -1;

        lastProcessedSegment =
            -1;
    }

    private void OnSceneLoaded(
        Scene scene,
        LoadSceneMode mode)
    {
        pendingSegment =
            -1;

        lastProcessedSegment =
            -1;

        Rolechanger.ClearPairs();
    }

    internal void RequestSegmentSwap(
        int targetSegment)
    {
        if (!PhotonNetwork.InRoom ||
            !PhotonNetwork.IsMasterClient ||
            targetSegment < 0)
        {
            return;
        }

        if (targetSegment ==
            lastProcessedSegment)
        {
            return;
        }

        pendingSegment =
            targetSegment;

        nextRetryTime =
            0f;
    }

    private void Update()
    {
        if (!runtimeInitialized ||
            pendingSegment < 0 ||
            !PhotonNetwork.InRoom ||
            !PhotonNetwork.IsMasterClient)
        {
            return;
        }

        float now =
            Time.realtimeSinceStartup;

        if (now <
            nextRetryTime)
        {
            return;
        }

        nextRetryTime =
            now +
            RetryInterval;

        if (!TrySwapAllPairs(
                pendingSegment))
        {
            return;
        }

        lastProcessedSegment =
            pendingSegment;

        pendingSegment =
            -1;
    }

    private bool TrySwapAllPairs(
        int targetSegment)
    {
        List<Character> characters =
            PlayerHandler
                .GetAllPlayerCharacters();

        if (characters == null ||
            characters.Count ==
                0)
        {
            return false;
        }

        List<PairData> pairs =
            new List<PairData>();

        for (int i = 0;
            i < characters.Count;
            i++)
        {
            Character carrier =
                characters[i];

            if (!CharacterReady(
                    carrier))
            {
                continue;
            }

            Character rider =
                carrier
                    .data
                    .carriedPlayer;

            if (!CharacterReady(
                    rider))
            {
                continue;
            }

            if (!rider.data.isCarried ||
                rider.data.carrier !=
                    carrier)
            {
                continue;
            }

            pairs.Add(
                new PairData
                {
                    Carrier =
                        carrier,

                    Rider =
                        rider
                });
        }

        if (pairs.Count ==
            0)
        {
            return true;
        }

        for (int i = 0;
            i < pairs.Count;
            i++)
        {
            PairData pair =
                pairs[i];

            if (!Rolechanger.SwapPair(
                    targetSegment,
                    pair.Carrier,
                    pair.Rider))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CharacterReady(
        Character character)
    {
        return
            character != null &&
            character.data != null &&
            character.refs != null &&
            character.refs.carriying !=
                null &&
            character.refs.ragdoll !=
                null &&
            character.photonView !=
                null &&
            character.player !=
                null;
    }

    private sealed class PairData
    {
        public Character Carrier;

        public Character Rider;
    }
}
