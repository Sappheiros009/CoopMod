using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Piggyback
{
    private const string HarmonyId =
        "com.peak.coopmod.piggyback";

    private const float CharacterSpawnTimeout =
        30f;

    private const string RolePreferencePropertyKey =
        "CoopMod.RolePreference";

    public enum RolePreferenceMode : byte
    {
        Random = 0,
        Climber = 1,
        Carrier = 2
    }

    public static ConfigEntry<RolePreferenceMode>
        RolePreference;

    private static Harmony harmony;

    private static CoopMod plugin;

    private static Coroutine assignmentCoroutine;

    private static bool initialPairingWindow;

    private static readonly Dictionary<int, int>
        LockedRiderToCarrier =
            new Dictionary<int, int>();

    public static void Initialize(
        CoopMod owner)
    {
        if (harmony != null)
        {
            return;
        }

        if (owner == null)
        {
            return;
        }

        plugin =
            owner;

        RolePreference =
            plugin.Config.Bind(
                "Co-op Role",
                "Role Preference",
                RolePreferenceMode.Random,
                "역할 배정 선호도입니다. Random은 무작위, Climber는 등반자만, Carrier는 운반자만 배정됩니다."
            );

        RolePreference.SettingChanged +=
            OnRolePreferenceChanged;

        PublishLocalRolePreference();

        harmony =
            new Harmony(
                HarmonyId);

        Patch(
            typeof(
                CharacterCarrying_RPCA_StartCarry_Patch));

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
                CharacterMovement_CanMoveCamera_Patch));

        Patch(
            typeof(
                MainCameraMovement_LateUpdate_LocalCarrier_Patch));

        SceneManager.sceneLoaded +=
            OnSceneLoaded;

        HandleSceneLoaded(
            SceneManager.GetActiveScene());
    }

    public static void Shutdown()
    {
        SceneManager.sceneLoaded -=
            OnSceneLoaded;

        StopAssignmentCoroutine();

        LockedRiderToCarrier.Clear();

        initialPairingWindow =
            false;

        if (RolePreference != null)
        {
            RolePreference.SettingChanged -=
                OnRolePreferenceChanged;

            RolePreference =
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

    private static void OnSceneLoaded(
        Scene scene,
        LoadSceneMode mode)
    {
        HandleSceneLoaded(
            scene);
    }

    private static void HandleSceneLoaded(
        Scene scene)
    {
        StopAssignmentCoroutine();

        LockedRiderToCarrier.Clear();

        initialPairingWindow =
            false;

        if (plugin == null ||
            !IsGameplayScene(
                scene))
        {
            return;
        }

        initialPairingWindow =
            true;

        assignmentCoroutine =
            plugin.StartCoroutine(
                WaitForPlayersAndAssignTeams());
    }

    private static void StopAssignmentCoroutine()
    {
        if (plugin != null &&
            assignmentCoroutine != null)
        {
            plugin.StopCoroutine(
                assignmentCoroutine);
        }

        assignmentCoroutine =
            null;
    }

    private static bool IsGameplayScene(
        Scene scene)
    {
        if (!scene.IsValid())
        {
            return false;
        }

        return
            scene.name.Contains(
                "Island") ||
            scene.name.Contains(
                "Level_");
    }

    private static bool IsOnShore()
    {
        if (!GameHandler.IsOnIsland)
        {
            return false;
        }

        return
            MapHandler.CurrentSegmentNumber ==
            Segment.Beach;
    }

    private static IEnumerator
        WaitForPlayersAndAssignTeams()
    {
        float timeoutTime =
            Time.realtimeSinceStartup +
            CharacterSpawnTimeout;

        while (
            Time.realtimeSinceStartup <
            timeoutTime)
        {
            if (plugin == null)
            {
                assignmentCoroutine =
                    null;

                yield break;
            }

            if (!PhotonNetwork.InRoom ||
                PhotonNetwork.CurrentRoom == null)
            {
                yield return null;
                continue;
            }

            PublishLocalRolePreference();

            if (!GameHandler.IsOnIsland)
            {
                yield return null;
                continue;
            }

            if (!IsOnShore())
            {
                initialPairingWindow =
                    false;

                assignmentCoroutine =
                    null;

                yield break;
            }

            int playerCount =
                PhotonNetwork
                    .CurrentRoom
                    .PlayerCount;

            if (playerCount != 2 &&
                playerCount != 4)
            {
                initialPairingWindow =
                    false;

                assignmentCoroutine =
                    null;

                yield break;
            }

            if (!PhotonNetwork.IsMasterClient)
            {
                assignmentCoroutine =
                    null;

                yield break;
            }

            if (AllPlayerCharactersReady() &&
                AllRolePreferencesReady())
            {
                AssignTeamsByPreference();

                assignmentCoroutine =
                    null;

                yield break;
            }

            yield return null;
        }

        initialPairingWindow =
            false;

        assignmentCoroutine =
            null;
    }

    private static bool
        AllPlayerCharactersReady()
    {
        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null)
        {
            return false;
        }

        Photon.Realtime.Player[] players =
            PhotonNetwork.PlayerList;

        if (players == null ||
            players.Length !=
            PhotonNetwork
                .CurrentRoom
                .PlayerCount)
        {
            return false;
        }

        if (players.Length != 2 &&
            players.Length != 4)
        {
            return false;
        }

        for (int i = 0;
            i < players.Length;
            i++)
        {
            Character character;

            if (!PlayerHandler.TryGetCharacter(
                    players[i].ActorNumber,
                    out character))
            {
                return false;
            }

            if (!CharacterReady(
                    character))
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
            character.refs.carriying != null &&
            character.photonView != null &&
            character.player != null;
    }

    private static void OnRolePreferenceChanged(
        object sender,
        EventArgs e)
    {
        PublishLocalRolePreference();
    }

    private static void PublishLocalRolePreference()
    {
        if (RolePreference == null ||
            !PhotonNetwork.InRoom ||
            PhotonNetwork.LocalPlayer == null)
        {
            return;
        }

        byte value =
            (byte)RolePreference.Value;

        object currentValue;

        if (PhotonNetwork
                .LocalPlayer
                .CustomProperties != null &&
            PhotonNetwork
                .LocalPlayer
                .CustomProperties
                .TryGetValue(
                    RolePreferencePropertyKey,
                    out currentValue))
        {
            if (currentValue is byte &&
                (byte)currentValue ==
                value)
            {
                return;
            }

            if (currentValue is int &&
                (int)currentValue ==
                value)
            {
                return;
            }
        }

        ExitGames.Client.Photon.Hashtable properties =
            new ExitGames.Client.Photon.Hashtable
            {
                {
                    RolePreferencePropertyKey,
                    value
                }
            };

        PhotonNetwork
            .LocalPlayer
            .SetCustomProperties(
                properties);
    }

    private static bool AllRolePreferencesReady()
    {
        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null)
        {
            return false;
        }

        Photon.Realtime.Player[] players =
            PhotonNetwork.PlayerList;

        if (players == null ||
            players.Length == 0)
        {
            return false;
        }

        for (int i = 0;
            i < players.Length;
            i++)
        {
            if (players[i] == null ||
                players[i].CustomProperties ==
                    null ||
                !players[i]
                    .CustomProperties
                    .ContainsKey(
                        RolePreferencePropertyKey))
            {
                return false;
            }
        }

        return true;
    }

    private static RolePreferenceMode
        GetRolePreference(
            Photon.Realtime.Player player)
    {
        if (player == null ||
            player.CustomProperties == null)
        {
            return
                RolePreferenceMode.Random;
        }

        object value;

        if (!player
                .CustomProperties
                .TryGetValue(
                    RolePreferencePropertyKey,
                    out value))
        {
            return
                RolePreferenceMode.Random;
        }

        if (value is byte)
        {
            byte byteValue =
                (byte)value;

            if (byteValue <=
                (byte)RolePreferenceMode.Carrier)
            {
                return
                    (RolePreferenceMode)
                    byteValue;
            }
        }

        if (value is int)
        {
            int intValue =
                (int)value;

            if (intValue >= 0 &&
                intValue <=
                (int)RolePreferenceMode.Carrier)
            {
                return
                    (RolePreferenceMode)
                    intValue;
            }
        }

        return
            RolePreferenceMode.Random;
    }

    private static void Shuffle(
        List<int> values,
        System.Random random)
    {
        if (values == null ||
            random == null)
        {
            return;
        }

        for (int i =
            values.Count - 1;
            i > 0;
            i--)
        {
            int randomIndex =
                random.Next(
                    i + 1);

            int temp =
                values[i];

            values[i] =
                values[randomIndex];

            values[randomIndex] =
                temp;
        }
    }

    private static void AssignTeamsByPreference()
    {
        if (!PhotonNetwork.IsMasterClient ||
            !PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null)
        {
            return;
        }

        Photon.Realtime.Player[] players =
            PhotonNetwork.PlayerList;

        if (players == null ||
            (
                players.Length != 2 &&
                players.Length != 4
            ))
        {
            return;
        }

        int pairCount =
            players.Length / 2;

        List<int> carriers =
            new List<int>(
                pairCount);

        List<int> climbers =
            new List<int>(
                pairCount);

        List<int> randomPlayers =
            new List<int>(
                players.Length);

        for (int i = 0;
            i < players.Length;
            i++)
        {
            RolePreferenceMode preference =
                GetRolePreference(
                    players[i]);

            if (preference ==
                RolePreferenceMode.Carrier)
            {
                carriers.Add(
                    players[i].ActorNumber);
            }
            else if (preference ==
                RolePreferenceMode.Climber)
            {
                climbers.Add(
                    players[i].ActorNumber);
            }
            else
            {
                randomPlayers.Add(
                    players[i].ActorNumber);
            }
        }

        if (carriers.Count >
                pairCount ||
            climbers.Count >
                pairCount)
        {
            initialPairingWindow =
                false;

            return;
        }

        int carrierSlots =
            pairCount -
            carriers.Count;

        int climberSlots =
            pairCount -
            climbers.Count;

        if (carrierSlots +
            climberSlots !=
            randomPlayers.Count)
        {
            initialPairingWindow =
                false;

            return;
        }

        System.Random random =
            new System.Random(
                Environment.TickCount ^
                PhotonNetwork.ServerTimestamp);

        Shuffle(
            randomPlayers,
            random);

        for (int i = 0;
            i < carrierSlots;
            i++)
        {
            carriers.Add(
                randomPlayers[i]);
        }

        for (int i =
            carrierSlots;
            i < randomPlayers.Count;
            i++)
        {
            climbers.Add(
                randomPlayers[i]);
        }

        Shuffle(
            carriers,
            random);

        Shuffle(
            climbers,
            random);

        for (int i = 0;
            i < pairCount;
            i++)
        {
            Character carrier;
            Character rider;

            if (!PlayerHandler.TryGetCharacter(
                    carriers[i],
                    out carrier))
            {
                continue;
            }

            if (!PlayerHandler.TryGetCharacter(
                    climbers[i],
                    out rider))
            {
                continue;
            }

            if (!CharacterReady(
                    carrier) ||
                !CharacterReady(
                    rider) ||
                carrier == rider)
            {
                continue;
            }

            if (carrier.data.carriedPlayer !=
                null)
            {
                continue;
            }

            if (rider.data.isCarried ||
                rider.data.carrier != null)
            {
                continue;
            }

            if (carrier.data.dead ||
                rider.data.dead)
            {
                continue;
            }

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
        }
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

    private static void RegisterLockedPair(
        Character carrier,
        Character rider)
    {
        if (!CharacterReady(
                carrier) ||
            !CharacterReady(
                rider))
        {
            return;
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
            return;
        }

        LockedRiderToCarrier[
            riderActor] =
            carrierActor;

        int expectedPairCount =
            GetExpectedPairCount();

        if (expectedPairCount > 0 &&
            LockedRiderToCarrier.Count >=
            expectedPairCount)
        {
            initialPairingWindow =
                false;
        }
    }

    private static void RemoveLockedPair(
        Character carrier,
        Character rider)
    {
        if (rider == null)
        {
            return;
        }

        int riderActor =
            GetActorNumber(
                rider);

        if (riderActor <= 0)
        {
            return;
        }

        int registeredCarrier;

        if (!LockedRiderToCarrier.TryGetValue(
                riderActor,
                out registeredCarrier))
        {
            return;
        }

        if (carrier != null)
        {
            int carrierActor =
                GetActorNumber(
                    carrier);

            if (carrierActor > 0 &&
                registeredCarrier !=
                carrierActor)
            {
                return;
            }
        }

        LockedRiderToCarrier.Remove(
            riderActor);
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

    private static int GetExpectedPairCount()
    {
        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null)
        {
            return 0;
        }

        int playerCount =
            PhotonNetwork
                .CurrentRoom
                .PlayerCount;

        if (playerCount == 2)
        {
            return 1;
        }

        if (playerCount == 4)
        {
            return 2;
        }

        return 0;
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
            return
                character;
        }

        return
            component
                .GetComponentInParent<Character>();
    }

    private static bool IsCameraCarrier(
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

    private static bool IsCameraClimber(
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

    internal static bool TryGetCarrierCameraTarget(
        out Character carrier,
        out Character climber)
    {
        carrier =
            null;

        climber =
            null;

        Character localCharacter =
            Character.localCharacter;

        if (localCharacter == null ||
            !localCharacter.IsLocal)
        {
            return false;
        }

        if (IsCameraCarrier(
                localCharacter))
        {
            carrier =
                localCharacter;

            climber =
                localCharacter
                    .data
                    .carriedPlayer;

            return
                climber != null &&
                climber.data != null;
        }

        if (IsCameraClimber(
                localCharacter))
        {
            climber =
                localCharacter;

            carrier =
                localCharacter
                    .data
                    .carrier;

            return
                carrier != null &&
                carrier.data != null;
        }

        return false;
    }

    private static bool TryBeginLocalCarrierCamera(
        out Character originalLocalCharacter)
    {
        originalLocalCharacter =
            null;

        Character climber =
            Character.localCharacter;

        if (climber == null ||
            !climber.IsLocal ||
            !IsCameraClimber(
                climber) ||
            climber.data == null)
        {
            return false;
        }

        Character carrier =
            climber
                .data
                .carrier;

        if (carrier == null ||
            carrier.data == null)
        {
            return false;
        }

        originalLocalCharacter =
            climber;

        Character.localCharacter =
            carrier;

        return true;
    }

    private static void EndLocalCarrierCamera(
        Character originalLocalCharacter)
    {
        if (originalLocalCharacter == null)
        {
            return;
        }

        Character.localCharacter =
            originalLocalCharacter;
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

    [HarmonyPatch(
        typeof(CharacterMovement),
        "CanMoveCamera")]
    private static class CharacterMovement_CanMoveCamera_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            CharacterMovement __instance,
            ref bool __result)
        {
            Character character =
                ResolveCharacter(
                    __instance);

            if (!IsCameraClimber(
                    character))
            {
                return true;
            }

            __result =
                false;

            return false;
        }
    }

    [HarmonyPatch(
        typeof(MainCameraMovement),
        "LateUpdate")]
    private static class
        MainCameraMovement_LateUpdate_LocalCarrier_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref Character __state)
        {
            __state =
                null;

            TryBeginLocalCarrierCamera(
                out __state);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Character __state)
        {
            EndLocalCarrierCamera(
                __state);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            EndLocalCarrierCamera(
                __state);

            return
                __exception;
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
            if (!initialPairingWindow ||
                __instance == null ||
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

            if (!CharacterReady(
                    carrier) ||
                !CharacterReady(
                    rider))
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

            RegisterLockedPair(
                carrier,
                rider);

            MakeRiderAlive(
                rider);
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

            if (ShouldAllowRelease(
                    carrier,
                    rider))
            {
                return true;
            }

            return false;
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

            RemoveLockedPair(
                carrier,
                rider);
        }
    }
}
