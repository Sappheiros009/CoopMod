using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ShareAlive
{
    private const string HarmonyId =
        "com.peak.coopmod.sharealive";

    private const byte ReviveEventCode =
        189;

    private static Harmony harmony;

    private static ShareAliveRuntime runtime;

    private static bool initialized =
        false;

    private static bool applyingRemoteEvent =
        false;

    private static readonly Dictionary<int, int>
        PartnerByActor =
            new Dictionary<int, int>();

    public static void Initialize(
        CoopMod plugin)
    {
        if (initialized)
        {
            return;
        }

        if (plugin == null)
        {
            return;
        }

        PartnerByActor.Clear();
        applyingRemoteEvent = false;

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
                    Character_RPCA_ReviveAtPosition_Patch))
            .Patch();

        runtime =
            plugin.gameObject
                .GetComponent<ShareAliveRuntime>();

        if (runtime == null)
        {
            runtime =
                plugin.gameObject
                    .AddComponent<ShareAliveRuntime>();
        }

        runtime.Activate();

        SceneManager.sceneLoaded +=
            OnSceneLoaded;

        initialized =
            true;

        CaptureExistingPairs();
    }

    public static void Shutdown()
    {
        if (!initialized)
        {
            return;
        }

        SceneManager.sceneLoaded -=
            OnSceneLoaded;

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

        PartnerByActor.Clear();
        applyingRemoteEvent = false;
        initialized = false;
    }

    private static void OnSceneLoaded(
        Scene scene,
        LoadSceneMode mode)
    {
        PartnerByActor.Clear();
        applyingRemoteEvent = false;
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
        Character carrier,
        Character climber)
    {
        if (carrier == null ||
            climber == null ||
            carrier == climber)
        {
            return;
        }

        int carrierActor =
            GetActorNumber(
                carrier);

        int climberActor =
            GetActorNumber(
                climber);

        if (carrierActor <= 0 ||
            climberActor <= 0)
        {
            return;
        }

        PartnerByActor[carrierActor] =
            climberActor;

        PartnerByActor[climberActor] =
            carrierActor;
    }

    private static void CaptureExistingPairs()
    {
        List<Character> characters =
            PlayerHandler
                .GetAllPlayerCharacters();

        if (characters == null)
        {
            return;
        }

        for (int i = 0;
            i < characters.Count;
            i++)
        {
            Character carrier =
                characters[i];

            if (carrier == null ||
                carrier.data == null)
            {
                continue;
            }

            Character climber =
                carrier
                    .data
                    .carriedPlayer;

            if (climber == null ||
                climber.data == null)
            {
                continue;
            }

            if (!climber.data.isCarried ||
                climber.data.carrier != carrier)
            {
                continue;
            }

            RegisterPair(
                carrier,
                climber);
        }
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

        int actorNumber =
            GetActorNumber(
                character);

        int partnerActor;

        if (actorNumber > 0 &&
            PartnerByActor.TryGetValue(
                actorNumber,
                out partnerActor) &&
            partnerActor > 0 &&
            PlayerHandler.TryGetCharacter(
                partnerActor,
                out partner) &&
            partner != null)
        {
            return true;
        }

        if (character.data == null)
        {
            partner =
                null;

            return false;
        }

        Character climber =
            character
                .data
                .carriedPlayer;

        if (climber != null &&
            climber.data != null &&
            climber.data.isCarried &&
            climber.data.carrier == character)
        {
            partner =
                climber;

            RegisterPair(
                character,
                climber);

            return true;
        }

        if (character.data.isCarried)
        {
            Character carrier =
                character
                    .data
                    .carrier;

            if (carrier != null &&
                carrier.data != null &&
                carrier.data.carriedPlayer == character)
            {
                partner =
                    carrier;

                RegisterPair(
                    carrier,
                    character);

                return true;
            }
        }

        partner =
            null;

        return false;
    }

    private static bool NeedsRevive(
        Character character)
    {
        if (character == null ||
            character.data == null)
        {
            return false;
        }

        return
            character.data.dead ||
            character.data.fullyPassedOut;
    }

    private static bool IsAlive(
        Character character)
    {
        if (character == null ||
            character.data == null)
        {
            return false;
        }

        return
            !character.data.dead &&
            !character.data.fullyPassedOut;
    }

    private static void SendToPartner(
        Character sender,
        Character partner,
        object[] payload)
    {
        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null ||
            sender == null ||
            partner == null ||
            payload == null)
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
            ReviveEventCode,
            payload,
            options,
            SendOptions.SendReliable);
    }

    private static void ShareReviveAtPosition(
        Character revivedCharacter,
        Vector3 position,
        bool applyStatus,
        int statueSegment)
    {
        if (applyingRemoteEvent ||
            revivedCharacter == null ||
            !revivedCharacter.IsLocal ||
            !IsAlive(revivedCharacter))
        {
            return;
        }

        Character partner;

        if (!TryGetPartner(
                revivedCharacter,
                out partner) ||
            !NeedsRevive(partner))
        {
            return;
        }

        int senderActor =
            GetActorNumber(
                revivedCharacter);

        int partnerActor =
            GetActorNumber(
                partner);

        if (senderActor <= 0 ||
            partnerActor <= 0)
        {
            return;
        }

        SendToPartner(
            revivedCharacter,
            partner,
            new object[]
            {
                senderActor,
                partnerActor,
                position.x,
                position.y,
                position.z,
                applyStatus,
                statueSegment
            });
    }

    internal static void HandlePhotonEvent(
        EventData photonEvent)
    {
        if (!initialized ||
            photonEvent.Code != ReviveEventCode)
        {
            return;
        }

        object[] payload =
            photonEvent.CustomData
                as object[];

        if (payload == null ||
            payload.Length < 7)
        {
            return;
        }

        if (PhotonNetwork.LocalPlayer == null)
        {
            return;
        }

        int senderActor =
            (int)payload[0];

        int targetActor =
            (int)payload[1];

        if (PhotonNetwork.LocalPlayer.ActorNumber !=
            targetActor)
        {
            return;
        }

        Character localCharacter =
            Character.localCharacter;

        if (localCharacter == null ||
            !localCharacter.IsLocal ||
            !NeedsRevive(localCharacter) ||
            localCharacter.photonView == null)
        {
            return;
        }

        Character partner;

        if (!TryGetPartner(
                localCharacter,
                out partner) ||
            GetActorNumber(partner) != senderActor)
        {
            return;
        }

        Vector3 position =
            new Vector3(
                (float)payload[2],
                (float)payload[3],
                (float)payload[4]);

        bool applyStatus =
            (bool)payload[5];

        int statueSegment =
            (int)payload[6];

        applyingRemoteEvent =
            true;

        try
        {
            localCharacter
                .photonView
                .RPC(
                    "RPCA_ReviveAtPosition",
                    RpcTarget.All,
                    new object[]
                    {
                        position,
                        applyStatus,
                        statueSegment
                    });
        }
        finally
        {
            applyingRemoteEvent =
                false;
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
            if (!initialized ||
                __instance == null ||
                targetView == null)
            {
                return;
            }

            Character carrier =
                __instance
                    .GetComponent<Character>();

            Character climber =
                targetView
                    .GetComponent<Character>();

            if (carrier == null ||
                climber == null ||
                carrier.data == null ||
                climber.data == null)
            {
                return;
            }

            if (carrier.data.carriedPlayer != climber ||
                !climber.data.isCarried ||
                climber.data.carrier != carrier)
            {
                return;
            }

            RegisterPair(
                carrier,
                climber);
        }
    }

    [HarmonyPatch(
        typeof(Character),
        "RPCA_ReviveAtPosition",
        new Type[]
        {
            typeof(Vector3),
            typeof(bool),
            typeof(int)
        })]
    private static class Character_RPCA_ReviveAtPosition_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Character __instance,
            Vector3 position,
            bool applyStatus,
            int statueSegment)
        {
            if (!initialized)
            {
                return;
            }

            ShareReviveAtPosition(
                __instance,
                position,
                applyStatus,
                statueSegment);
        }
    }


}

public sealed class ShareAliveRuntime :
    MonoBehaviour,
    IOnEventCallback
{
    private bool active =
        false;

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

    public void OnEvent(
        EventData photonEvent)
    {
        if (!active)
        {
            return;
        }

        ShareAlive.HandlePhotonEvent(
            photonEvent);
    }

    private void OnDestroy()
    {
        Deactivate();
    }
}
