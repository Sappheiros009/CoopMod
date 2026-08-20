using System;
using System.Collections.Generic;
using BepInEx;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Latejoin
{
    private const string HarmonyId =
        "com.peak.coopmod.latejoin";

    private const byte PairEventCode =
        193;

    private const byte PairAction =
        1;

    private const byte PromoteCarrierAction =
        2;

    private const byte RemovePairAction =
        3;

    private static Harmony harmony;

    private static CoopMod plugin;

    private static LatejoinRuntime runtime;

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
                CharacterCarrying_Update_Patch));

        Patch(
            typeof(
                CharacterCarrying_Drop_Patch));

        Patch(
            typeof(
                CharacterCarrying_RPCA_Drop_Patch));

        Patch(
            typeof(
                CharacterCarrying_RPCA_StartCarry_Patch));

        runtime =
            owner
                .gameObject
                .GetComponent<
                    LatejoinRuntime>();

        if (runtime == null)
        {
            runtime =
                owner
                    .gameObject
                    .AddComponent<
                        LatejoinRuntime>();
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

    internal static void HandlePairEvent(
        EventData photonEvent)
    {
        if (photonEvent == null ||
            photonEvent.Code !=
                PairEventCode)
        {
            return;
        }

        byte[] payload =
            photonEvent.CustomData
                as byte[];

        if (payload == null ||
            payload.Length <
                9)
        {
            return;
        }

        int offset =
            0;

        byte action =
            payload[offset++];

        int firstActor =
            ReadInt32(
                payload,
                ref offset);

        int secondActor =
            ReadInt32(
                payload,
                ref offset);

        if (action ==
            PairAction)
        {
            RegisterPairActors(
                firstActor,
                secondActor);

            Character rider;

            if (PlayerHandler.TryGetCharacter(
                    secondActor,
                    out rider))
            {
                MakeRiderAlive(
                    rider);
            }

            return;
        }

        if (action ==
            PromoteCarrierAction)
        {
            PromoteActorToCarrier(
                firstActor);

            return;
        }

        if (action ==
            RemovePairAction)
        {
            RemovePairsForActor(
                firstActor);

            if (secondActor > 0)
            {
                RemovePairsForActor(
                    secondActor);
            }
        }
    }

    internal static bool StartLatejoinPair(
        Character carrier,
        Character rider)
    {
        if (!PhotonNetwork.IsMasterClient ||
            !PhotonNetwork.InRoom ||
            carrier == null ||
            rider == null ||
            carrier.data == null ||
            rider.data == null ||
            carrier.photonView == null ||
            rider.photonView == null)
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
            riderActor <= 0 ||
            carrierActor ==
                riderActor)
        {
            return false;
        }

        RegisterPairActors(
            carrierActor,
            riderActor);

        BroadcastPairAction(
            PairAction,
            carrierActor,
            riderActor);

        carrier
            .photonView
            .RPC(
                nameof(
                    CharacterCarrying
                        .RPCA_StartCarry),
                RpcTarget.All,
                new object[]
                {
                    rider.photonView
                });

        bool paired =
            carrier.data.carriedPlayer ==
                rider &&
            rider.data.isCarried &&
            rider.data.carrier ==
                carrier;

        if (!paired)
        {
            RemovePairsForActor(
                riderActor);

            BroadcastPairAction(
                RemovePairAction,
                riderActor,
                carrierActor);

            return false;
        }

        MakeRiderAlive(
            rider);

        return true;
    }

    internal static void PromoteCarrier(
        int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient ||
            actorNumber <= 0)
        {
            return;
        }

        PromoteActorToCarrier(
            actorNumber);

        BroadcastPairAction(
            PromoteCarrierAction,
            actorNumber,
            0);
    }

    internal static bool IsActorLockedAsRider(
        int actorNumber)
    {
        return
            actorNumber > 0 &&
            LockedRiderToCarrier
                .ContainsKey(
                    actorNumber);
    }

    internal static bool IsActorLockedAsCarrier(
        int actorNumber)
    {
        if (actorNumber <= 0)
        {
            return false;
        }

        foreach (
            KeyValuePair<int, int>
                pair
            in LockedRiderToCarrier)
        {
            if (pair.Value ==
                actorNumber)
            {
                return true;
            }
        }

        return false;
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

    private static void RegisterPairActors(
        int carrierActor,
        int riderActor)
    {
        if (carrierActor <= 0 ||
            riderActor <= 0 ||
            carrierActor ==
                riderActor)
        {
            return;
        }

        RemovePairsForActor(
            riderActor);

        RemovePairsForActor(
            carrierActor);

        LockedRiderToCarrier[
            riderActor] =
            carrierActor;
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

    private static void MakeRiderAlive(
        Character rider)
    {
        if (rider == null ||
            rider.data == null ||
            rider.data.dead)
        {
            return;
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
    }

    private static void BroadcastPairAction(
        byte action,
        int firstActor,
        int secondActor)
    {
        if (!PhotonNetwork.InRoom)
        {
            return;
        }

        byte[] payload =
            new byte[9];

        int offset =
            0;

        payload[offset++] =
            action;

        WriteInt32(
            payload,
            ref offset,
            firstActor);

        WriteInt32(
            payload,
            ref offset,
            secondActor);

        RaiseEventOptions options =
            new RaiseEventOptions
            {
                Receivers =
                    ReceiverGroup.Others
            };

        PhotonNetwork.RaiseEvent(
            PairEventCode,
            payload,
            options,
            SendOptions.SendReliable);
    }

    private static void PromoteActorToCarrier(
        int actorNumber)
    {
        if (actorNumber <= 0)
        {
            return;
        }

        RemovePairsForActor(
            actorNumber);

        Character character;

        if (!PlayerHandler.TryGetCharacter(
                actorNumber,
                out character) ||
            character == null ||
            character.data == null)
        {
            return;
        }

        Character oldCarrier =
            character
                .data
                .carrier;

        if (oldCarrier != null &&
            oldCarrier.data != null &&
            oldCarrier.data.carriedPlayer ==
                character)
        {
            oldCarrier.data.carriedPlayer =
                null;
        }

        Character oldRider =
            character
                .data
                .carriedPlayer;

        if (oldRider != null &&
            oldRider.data != null &&
            oldRider.data.carrier ==
                character)
        {
            oldRider.data.carrier =
                null;

            oldRider.data.isCarried =
                false;
        }

        character.data.isCarried =
            false;

        character.data.carrier =
            null;

        character.data.carriedPlayer =
            null;

        if (character.refs != null)
        {
            if (character.refs.ragdoll !=
                null)
            {
                character
                    .refs
                    .ragdoll
                    .ToggleCollision(
                        true);
            }

            if (character.refs.animator !=
                null)
            {
                character
                    .refs
                    .animator
                    .SetBool(
                        "IsCarried",
                        false);
            }
        }

        MakeRiderAlive(
            character);
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

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterCarrying __instance,
            PhotonView targetView)
        {
            if (__instance == null ||
                targetView == null)
            {
                return;
            }

            Character carrier =
                __instance
                    .GetComponent<Character>();

            Character rider =
                targetView
                    .GetComponent<Character>();

            if (rider == null ||
                rider.data == null)
            {
                return;
            }

            if (rider.data.isCarried &&
                rider.data.carrier ==
                    carrier)
            {
                return;
            }

            int riderActor =
                GetActorNumber(
                    rider);

            if (riderActor > 0)
            {
                LockedRiderToCarrier.Remove(
                    riderActor);
            }
        }
    }

    [HarmonyPatch(
        typeof(CharacterCarrying),
        nameof(
            CharacterCarrying
                .RPCA_StartCarry),
        new Type[]
        {
            typeof(PhotonView)
        })]
    private static class
        CharacterCarrying_RPCA_StartCarry_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterCarrying __instance,
            PhotonView targetView)
        {
            if (__instance == null ||
                targetView == null)
            {
                return;
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
                return;
            }

            if (carrier == null ||
                carrier.data == null ||
                rider == null ||
                rider.data == null)
            {
                return;
            }

            if (carrier.data.carriedPlayer !=
                    rider ||
                !rider.data.isCarried ||
                rider.data.carrier !=
                    carrier)
            {
                return;
            }

            MakeRiderAlive(
                rider);
        }
    }
}

public sealed class LatejoinRuntime :
    MonoBehaviourPunCallbacks,
    IOnEventCallback
{
    private const float ScanInterval =
        0.5f;

    private readonly HashSet<int>
        knownActors =
            new HashSet<int>();

    private readonly HashSet<int>
        pendingJoinActors =
            new HashSet<int>();

    private bool runtimeInitialized;

    private string activeRoomName;

    private float nextScanTime;

    private bool needsSurvivorNormalization;

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

        RefreshRoomSnapshot();
    }

    internal void ShutdownRuntime()
    {
        if (!runtimeInitialized)
        {
            return;
        }

        SceneManager.sceneLoaded -=
            OnSceneLoaded;

        knownActors.Clear();

        pendingJoinActors.Clear();

        activeRoomName =
            null;

        needsSurvivorNormalization =
            false;

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
        Latejoin.HandlePairEvent(
            photonEvent);
    }

    public override void OnJoinedRoom()
    {
        RefreshRoomSnapshot();
    }

    public override void OnLeftRoom()
    {
        knownActors.Clear();

        pendingJoinActors.Clear();

        activeRoomName =
            null;

        needsSurvivorNormalization =
            false;

        Latejoin.ClearPairs();
    }

    public override void OnMasterClientSwitched(
        Photon.Realtime.Player newMasterClient)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            needsSurvivorNormalization =
                true;
        }
    }

    public override void OnPlayerEnteredRoom(
        Photon.Realtime.Player newPlayer)
    {
        if (newPlayer == null)
        {
            return;
        }

        int actorNumber =
            newPlayer.ActorNumber;

        knownActors.Add(
            actorNumber);

        pendingJoinActors.Add(
            actorNumber);
    }

    public override void OnPlayerLeftRoom(
        Photon.Realtime.Player otherPlayer)
    {
        if (otherPlayer == null)
        {
            return;
        }

        int actorNumber =
            otherPlayer.ActorNumber;

        knownActors.Remove(
            actorNumber);

        pendingJoinActors.Remove(
            actorNumber);

        Latejoin.RemovePairsForActor(
            actorNumber);

        needsSurvivorNormalization =
            true;
    }

    private void OnSceneLoaded(
        Scene scene,
        LoadSceneMode mode)
    {
        pendingJoinActors.Clear();

        needsSurvivorNormalization =
            false;

        Latejoin.ClearPairs();

        RefreshRoomSnapshot();
    }

    private void Update()
    {
        if (!runtimeInitialized)
        {
            return;
        }

        float now =
            Time.realtimeSinceStartup;

        if (now <
            nextScanTime)
        {
            return;
        }

        nextScanTime =
            now +
            ScanInterval;

        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom ==
                null)
        {
            knownActors.Clear();

            pendingJoinActors.Clear();

            activeRoomName =
                null;

            needsSurvivorNormalization =
                false;

            Latejoin.ClearPairs();

            return;
        }

        string roomName =
            PhotonNetwork
                .CurrentRoom
                .Name;

        if (!string.Equals(
                activeRoomName,
                roomName,
                StringComparison.Ordinal))
        {
            RefreshRoomSnapshot();

            return;
        }

        DetectNewActors();

        RemoveMissingActors();

        if (!PhotonNetwork.IsMasterClient)
        {
            return;
        }

        if (needsSurvivorNormalization)
        {
            if (!NormalizeSurvivorsAfterLeave())
            {
                return;
            }

            needsSurvivorNormalization =
                false;
        }

        AssignPendingJoiners();
    }

    private void RefreshRoomSnapshot()
    {
        knownActors.Clear();

        pendingJoinActors.Clear();

        needsSurvivorNormalization =
            false;

        Latejoin.ClearPairs();

        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom ==
                null)
        {
            activeRoomName =
                null;

            return;
        }

        activeRoomName =
            PhotonNetwork
                .CurrentRoom
                .Name;

        Photon.Realtime.Player[] players =
            PhotonNetwork.PlayerList;

        if (players == null)
        {
            return;
        }

        for (int i = 0;
            i < players.Length;
            i++)
        {
            Photon.Realtime.Player player =
                players[i];

            if (player == null)
            {
                continue;
            }

            knownActors.Add(
                player.ActorNumber);
        }
    }

    private void DetectNewActors()
    {
        Photon.Realtime.Player[] players =
            PhotonNetwork.PlayerList;

        if (players == null)
        {
            return;
        }

        for (int i = 0;
            i < players.Length;
            i++)
        {
            Photon.Realtime.Player player =
                players[i];

            if (player == null)
            {
                continue;
            }

            int actorNumber =
                player.ActorNumber;

            if (knownActors.Add(
                    actorNumber))
            {
                pendingJoinActors.Add(
                    actorNumber);
            }
        }
    }

    private void RemoveMissingActors()
    {
        if (knownActors.Count ==
            0)
        {
            return;
        }

        List<int> missingActors =
            null;

        foreach (
            int actorNumber
            in knownActors)
        {
            if (PhotonNetwork
                    .CurrentRoom
                    .GetPlayer(
                        actorNumber) !=
                null)
            {
                continue;
            }

            if (missingActors ==
                null)
            {
                missingActors =
                    new List<int>();
            }

            missingActors.Add(
                actorNumber);
        }

        if (missingActors == null)
        {
            return;
        }

        for (int i = 0;
            i < missingActors.Count;
            i++)
        {
            int actorNumber =
                missingActors[i];

            knownActors.Remove(
                actorNumber);

            pendingJoinActors.Remove(
                actorNumber);

            Latejoin.RemovePairsForActor(
                actorNumber);
        }
    }

    private bool NormalizeSurvivorsAfterLeave()
    {
        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom ==
                null)
        {
            return false;
        }

        Photon.Realtime.Player[] players =
            PhotonNetwork.PlayerList;

        if (players == null ||
            players.Length ==
                0)
        {
            return false;
        }

        if (players.Length ==
            1)
        {
            Photon.Realtime.Player survivor =
                players[0];

            if (survivor == null)
            {
                return false;
            }

            Character survivorCharacter;

            if (!PlayerHandler.TryGetCharacter(
                    survivor.ActorNumber,
                    out survivorCharacter) ||
                !CharacterReady(
                    survivorCharacter))
            {
                return false;
            }

            Latejoin.PromoteCarrier(
                survivor.ActorNumber);

            return true;
        }

        List<Character> characters =
            PlayerHandler
                .GetAllPlayerCharacters();

        if (characters == null)
        {
            return false;
        }

        for (int i = 0;
            i < characters.Count;
            i++)
        {
            Character character =
                characters[i];

            if (!CharacterReady(
                    character))
            {
                continue;
            }

            int actorNumber =
                GetActorNumber(
                    character);

            if (actorNumber <= 0 ||
                PhotonNetwork
                    .CurrentRoom
                    .GetPlayer(
                        actorNumber) ==
                    null)
            {
                continue;
            }

            bool promote =
                false;

            if (character.data.isCarried ||
                character.data.carrier !=
                    null)
            {
                Character carrier =
                    character
                        .data
                        .carrier;

                int carrierActor =
                    GetActorNumber(
                        carrier);

                if (carrier == null ||
                    carrierActor <= 0 ||
                    PhotonNetwork
                        .CurrentRoom
                        .GetPlayer(
                            carrierActor) ==
                        null)
                {
                    promote =
                        true;
                }
            }

            Character rider =
                character
                    .data
                    .carriedPlayer;

            if (rider != null)
            {
                int riderActor =
                    GetActorNumber(
                        rider);

                if (riderActor <= 0 ||
                    PhotonNetwork
                        .CurrentRoom
                        .GetPlayer(
                            riderActor) ==
                        null)
                {
                    promote =
                        true;
                }
            }

            if (promote)
            {
                Latejoin.PromoteCarrier(
                    actorNumber);
            }
        }

        return true;
    }

    private void AssignPendingJoiners()
    {
        if (pendingJoinActors.Count ==
            0)
        {
            return;
        }

        List<int> pendingActors =
            new List<int>(
                pendingJoinActors);

        for (int i = 0;
            i < pendingActors.Count;
            i++)
        {
            int riderActor =
                pendingActors[i];

            if (PhotonNetwork
                    .CurrentRoom
                    .GetPlayer(
                        riderActor) ==
                null)
            {
                pendingJoinActors.Remove(
                    riderActor);

                continue;
            }

            Character rider;

            if (!PlayerHandler.TryGetCharacter(
                    riderActor,
                    out rider) ||
                !CharacterReady(
                    rider))
            {
                continue;
            }

            if (rider.data.dead)
            {
                continue;
            }

            if (rider.data.isCarried ||
                rider.data.carrier !=
                    null ||
                rider.data.carriedPlayer !=
                    null ||
                Latejoin.IsActorLockedAsRider(
                    riderActor) ||
                Latejoin.IsActorLockedAsCarrier(
                    riderActor))
            {
                pendingJoinActors.Remove(
                    riderActor);

                continue;
            }

            Character carrier =
                FindCarrierFor(
                    riderActor);

            if (carrier == null)
            {
                continue;
            }

            if (Latejoin.StartLatejoinPair(
                    carrier,
                    rider))
            {
                pendingJoinActors.Remove(
                    riderActor);
            }
        }
    }

    private Character FindCarrierFor(
        int riderActor)
    {
        List<Character> characters =
            PlayerHandler
                .GetAllPlayerCharacters();

        if (characters == null ||
            characters.Count ==
                0)
        {
            return null;
        }

        Character bestCarrier =
            null;

        int bestActor =
            int.MaxValue;

        for (int i = 0;
            i < characters.Count;
            i++)
        {
            Character candidate =
                characters[i];

            if (!CharacterReady(
                    candidate))
            {
                continue;
            }

            int candidateActor =
                GetActorNumber(
                    candidate);

            if (candidateActor <= 0 ||
                candidateActor ==
                    riderActor ||
                pendingJoinActors.Contains(
                    candidateActor))
            {
                continue;
            }

            if (candidate.data.dead ||
                candidate.data.isCarried ||
                candidate.data.carrier !=
                    null ||
                candidate.data.carriedPlayer !=
                    null)
            {
                continue;
            }

            if (Latejoin.IsActorLockedAsRider(
                    candidateActor) ||
                Latejoin.IsActorLockedAsCarrier(
                    candidateActor))
            {
                continue;
            }

            if (candidateActor <
                bestActor)
            {
                bestActor =
                    candidateActor;

                bestCarrier =
                    candidate;
            }
        }

        return
            bestCarrier;
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
}
