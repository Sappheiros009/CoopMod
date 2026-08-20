using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public static class ShareDeath
{
    private const string HarmonyId =
        "com.peak.coopmod.sharedeath";

    private const byte DeathEventCode =
        188;

    private static Harmony harmony;

    private static ShareDeathRuntime runtime;

    private static readonly Dictionary<int, int>
        PartnerByActor =
            new Dictionary<int, int>();

    private static readonly HashSet<int>
        SuppressNextDeathSend =
            new HashSet<int>();

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

        PartnerByActor.Clear();
        SuppressNextDeathSend.Clear();

        harmony =
            new Harmony(
                HarmonyId);

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterCarrying_RPCA_StartCarry_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    Character_RPCA_Die_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterInput_Sample_DeathHold_Patch))
            .Patch();

        runtime =
            plugin.gameObject
                .GetComponent<ShareDeathRuntime>();

        if (runtime == null)
        {
            runtime =
                plugin.gameObject
                    .AddComponent<ShareDeathRuntime>();
        }

        runtime.Activate();
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

        PartnerByActor.Clear();
        SuppressNextDeathSend.Clear();

        if (harmony != null)
        {
            harmony.UnpatchSelf();
            harmony = null;
        }
    }

    internal static void RuntimeUpdate()
    {
        Character localCharacter =
            Character.localCharacter;

        if (localCharacter == null ||
            !localCharacter.IsLocal ||
            localCharacter.data == null)
        {
            return;
        }

        Character partner;

        if (TryGetDirectPartner(
                localCharacter,
                out partner))
        {
            RegisterPair(
                localCharacter,
                partner);
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

    private static void RegisterPair(
        Character first,
        Character second)
    {
        if (first == null ||
            second == null ||
            first == second)
        {
            return;
        }

        int firstActor =
            GetActorNumber(
                first);

        int secondActor =
            GetActorNumber(
                second);

        if (firstActor <= 0 ||
            secondActor <= 0)
        {
            return;
        }

        PartnerByActor[firstActor] =
            secondActor;

        PartnerByActor[secondActor] =
            firstActor;
    }

    private static bool TryGetDirectPartner(
        Character character,
        out Character partner)
    {
        partner =
            null;

        if (character == null ||
            character.data == null)
        {
            return false;
        }

        if (character.data.isCarried)
        {
            Character carrier =
                character
                    .data
                    .carrier;

            if (carrier != null &&
                carrier.data != null &&
                carrier.data.carriedPlayer ==
                    character)
            {
                partner =
                    carrier;

                return true;
            }
        }

        Character rider =
            character
                .data
                .carriedPlayer;

        if (rider != null &&
            rider.data != null &&
            rider.data.isCarried &&
            rider.data.carrier ==
                character)
        {
            partner =
                rider;

            return true;
        }

        return false;
    }

    private static bool TryGetPartner(
        Character character,
        out Character partner)
    {
        partner =
            null;

        if (character == null)
        {
            return false;
        }

        if (TryGetDirectPartner(
                character,
                out partner))
        {
            RegisterPair(
                character,
                partner);

            return true;
        }

        int actorNumber =
            GetActorNumber(
                character);

        int partnerActor;

        if (actorNumber <= 0 ||
            !PartnerByActor.TryGetValue(
                actorNumber,
                out partnerActor))
        {
            return false;
        }

        if (!PlayerHandler.TryGetCharacter(
                partnerActor,
                out partner) ||
            partner == null)
        {
            partner =
                null;

            return false;
        }

        return true;
    }

    private static void SendSharedDeath(
        Character dyingCharacter)
    {
        if (!PhotonNetwork.InRoom ||
            dyingCharacter == null ||
            dyingCharacter.data == null ||
            !dyingCharacter.IsLocal ||
            dyingCharacter.data.dead)
        {
            return;
        }

        int dyingActor =
            GetActorNumber(
                dyingCharacter);

        if (dyingActor <= 0)
        {
            return;
        }

        if (SuppressNextDeathSend.Remove(
                dyingActor))
        {
            return;
        }

        Character partner;

        if (!TryGetPartner(
                dyingCharacter,
                out partner) ||
            partner == null ||
            partner.data == null ||
            partner.data.dead)
        {
            return;
        }

        int partnerActor =
            GetActorNumber(
                partner);

        if (partnerActor <= 0)
        {
            return;
        }

        RaiseEventOptions options =
            new RaiseEventOptions
            {
                TargetActors =
                    new int[]
                    {
                        partnerActor
                    }
            };

        PhotonNetwork.RaiseEvent(
            DeathEventCode,
            new object[]
            {
                dyingActor,
                partnerActor
            },
            options,
            SendOptions.SendReliable);
    }

    internal static void HandlePhotonEvent(
        EventData photonEvent)
    {
        if (photonEvent.Code !=
            DeathEventCode)
        {
            return;
        }

        object[] payload =
            photonEvent.CustomData
                as object[];

        if (payload == null ||
            payload.Length < 2)
        {
            return;
        }

        int senderActor =
            (int)payload[0];

        int targetActor =
            (int)payload[1];

        if (PhotonNetwork.LocalPlayer ==
                null ||
            PhotonNetwork
                .LocalPlayer
                .ActorNumber !=
                targetActor)
        {
            return;
        }

        Character localCharacter =
            Character.localCharacter;

        if (localCharacter == null ||
            !localCharacter.IsLocal ||
            localCharacter.data == null ||
            localCharacter.data.dead ||
            localCharacter.photonView == null)
        {
            return;
        }

        int localActor =
            GetActorNumber(
                localCharacter);

        if (localActor !=
            targetActor)
        {
            return;
        }

        Character partner;

        if (!TryGetPartner(
                localCharacter,
                out partner) ||
            partner == null ||
            GetActorNumber(partner) !=
                senderActor)
        {
            return;
        }

        SuppressNextDeathSend.Add(
            localActor);

        localCharacter
            .photonView
            .RPC(
                "RPCA_Die",
                RpcTarget.All,
                Array.Empty<object>());
    }

    private static bool IsCarrier(
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

    [HarmonyPatch(
        typeof(CharacterInput),
        "Sample",
        new Type[]
        {
            typeof(bool)
        })]
    [HarmonyAfter(
        "com.peak.coopmod.separaterole")]
    private static class
        CharacterInput_Sample_DeathHold_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            CharacterInput __instance)
        {
            Character localCharacter =
                Character.localCharacter;

            if (localCharacter == null ||
                !localCharacter.IsLocal ||
                localCharacter.input !=
                    __instance ||
                localCharacter.data == null ||
                localCharacter.data.dead ||
                !localCharacter.data.fullyPassedOut ||
                !IsCarrier(
                    localCharacter) ||
                CharacterInput.action_interact ==
                    null)
            {
                return;
            }

            __instance.interactWasPressed =
                CharacterInput
                    .action_interact
                    .WasPressedThisFrame();

            __instance.interactIsPressed =
                CharacterInput
                    .action_interact
                    .IsPressed();

            __instance.interactWasReleased =
                CharacterInput
                    .action_interact
                    .WasReleasedThisFrame();
        }
    }

    [HarmonyPatch(
        typeof(CharacterCarrying),
        nameof(
            CharacterCarrying.RPCA_StartCarry),
        new Type[]
        {
            typeof(PhotonView)
        })]
    private static class CharacterCarrying_RPCA_StartCarry_Patch
    {
        [HarmonyPostfix]
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

            if (carrier == null ||
                rider == null ||
                carrier.data == null ||
                rider.data == null)
            {
                return;
            }

            RegisterPair(
                carrier,
                rider);
        }
    }

    [HarmonyPatch(
        typeof(Character),
        "RPCA_Die")]
    private static class Character_RPCA_Die_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            Character __instance)
        {
            SendSharedDeath(
                __instance);
        }
    }
}

public sealed class ShareDeathRuntime :
    MonoBehaviour,
    IOnEventCallback
{
    private bool active;

    public void Activate()
    {
        if (active)
        {
            return;
        }

        PhotonNetwork.AddCallbackTarget(
            this);

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
    }

    private void Update()
    {
        if (!active)
        {
            return;
        }

        ShareDeath.RuntimeUpdate();
    }

    public void OnEvent(
        EventData photonEvent)
    {
        if (!active)
        {
            return;
        }

        ShareDeath.HandlePhotonEvent(
            photonEvent);
    }

    private void OnDestroy()
    {
        Deactivate();
    }
}
