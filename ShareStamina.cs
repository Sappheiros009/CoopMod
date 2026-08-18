using System;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public static class ShareStamina
{
    private const string HarmonyId =
        "com.peak.coopmod.sharestamina";

    private const byte StaminaEventCode =
        187;

    private const float ValueEpsilon =
        0.0001f;

    private static Harmony harmony;

    private static ShareStaminaRuntime runtime;

    private static bool initialized =
        false;

    private static int currentPartnerActor =
        -1;

    private static bool currentRoleIsCarrier =
        false;

    private static int suppressSendDepth =
        0;

    private static int addStaminaDepth =
        0;

    private static int useStaminaDepth =
        0;

    private enum StaminaAction : byte
    {
        Delta = 1,
        FullSync = 2
    }

    private struct StaminaSnapshot
    {
        public bool Track;
        public float Current;
        public float Extra;
    }

    public static void Initialize(
        CoopMod plugin)
    {
        if (initialized ||
            plugin == null)
        {
            return;
        }

        harmony =
            new Harmony(
                HarmonyId);

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterUseStaminaPatch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterCanRegenStaminaPatch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterAddStaminaPatch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterClampStaminaPatch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterSetExtraStaminaPatch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterAddExtraStaminaPatch))
            .Patch();

        runtime =
            plugin.gameObject
                .GetComponent<ShareStaminaRuntime>();

        if (runtime == null)
        {
            runtime =
                plugin.gameObject
                    .AddComponent<ShareStaminaRuntime>();
        }

        runtime.Activate();

        currentPartnerActor =
            -1;

        currentRoleIsCarrier =
            false;

        suppressSendDepth =
            0;

        addStaminaDepth =
            0;

        useStaminaDepth =
            0;

        initialized =
            true;
    }

    public static void Shutdown()
    {
        if (!initialized)
        {
            return;
        }

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

        currentPartnerActor =
            -1;

        currentRoleIsCarrier =
            false;

        suppressSendDepth =
            0;

        addStaminaDepth =
            0;

        useStaminaDepth =
            0;

        initialized =
            false;
    }

    internal static void RuntimeUpdate()
    {
        if (!initialized)
        {
            return;
        }

        Character localCharacter =
            Character.localCharacter;

        if (localCharacter == null ||
            !localCharacter.IsLocal)
        {
            ResetPartnerState();
            return;
        }

        Character partner;

        if (!TryGetPartner(
                localCharacter,
                out partner))
        {
            ResetPartnerState();
            return;
        }

        int partnerActor =
            GetActorNumber(
                partner);

        if (partnerActor <= 0)
        {
            ResetPartnerState();
            return;
        }

        bool isCarrier =
            IsCarrier(
                localCharacter);

        if (currentPartnerActor ==
                partnerActor &&
            currentRoleIsCarrier ==
                isCarrier)
        {
            return;
        }

        currentPartnerActor =
            partnerActor;

        currentRoleIsCarrier =
            isCarrier;

        if (isCarrier)
        {
            NormalizeSharedStamina(
                localCharacter,
                partner);

            SendFullSync(
                localCharacter,
                partner);
        }
    }

    internal static void HandlePhotonEvent(
        EventData photonEvent)
    {
        if (!initialized ||
            photonEvent.Code !=
                StaminaEventCode)
        {
            return;
        }

        object[] payload =
            photonEvent.CustomData
                as object[];

        if (payload == null ||
            payload.Length < 3)
        {
            return;
        }

        if (PhotonNetwork.LocalPlayer ==
            null)
        {
            return;
        }

        byte actionValue =
            (byte)payload[0];

        int senderActor =
            (int)payload[1];

        int targetActor =
            (int)payload[2];

        if (PhotonNetwork
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
            localCharacter.data == null)
        {
            return;
        }

        Character partner;

        if (!TryGetPartner(
                localCharacter,
                out partner))
        {
            return;
        }

        if (GetActorNumber(
                partner) !=
            senderActor)
        {
            return;
        }

        suppressSendDepth++;

        try
        {
            StaminaAction action =
                (StaminaAction)actionValue;

            if (action ==
                StaminaAction.Delta)
            {
                if (payload.Length < 6)
                {
                    return;
                }

                float currentDelta =
                    (float)payload[3];

                float extraDelta =
                    (float)payload[4];

                bool resetUseTimer =
                    (bool)payload[5];

                ApplyDelta(
                    localCharacter,
                    partner,
                    currentDelta,
                    extraDelta,
                    resetUseTimer);
            }
            else if (action ==
                StaminaAction.FullSync)
            {
                if (payload.Length < 6)
                {
                    return;
                }

                float current =
                    (float)payload[3];

                float extra =
                    (float)payload[4];

                float sinceUseStamina =
                    (float)payload[5];

                ApplyFullSync(
                    localCharacter,
                    partner,
                    current,
                    extra,
                    sinceUseStamina);
            }
        }
        finally
        {
            suppressSendDepth--;
        }

        if (IsCarrier(
                localCharacter))
        {
            SendFullSync(
                localCharacter,
                partner);
        }
    }

    private static void ResetPartnerState()
    {
        currentPartnerActor =
            -1;

        currentRoleIsCarrier =
            false;
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

    private static bool IsClimber(
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

    private static bool TryGetPartner(
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

    private static float GetSharedMaxStamina(
        Character first,
        Character second)
    {
        if (first == null)
        {
            return 0f;
        }

        float firstMax =
            Mathf.Max(
                0f,
                first.GetMaxStamina());

        if (second == null)
        {
            return firstMax;
        }

        float secondMax =
            Mathf.Max(
                0f,
                second.GetMaxStamina());

        return
            Mathf.Min(
                firstMax,
                secondMax);
    }

    private static void NormalizeSharedStamina(
        Character character,
        Character partner)
    {
        if (character == null ||
            character.data == null)
        {
            return;
        }

        float sharedMaximum =
            GetSharedMaxStamina(
                character,
                partner);

        character.data.currentStamina =
            Mathf.Clamp(
                character.data.currentStamina,
                0f,
                sharedMaximum);

        character.data.extraStamina =
            Mathf.Clamp(
                character.data.extraStamina,
                0f,
                1f);
    }

    private static void ApplyDelta(
        Character character,
        Character partner,
        float currentDelta,
        float extraDelta,
        bool resetUseTimer)
    {
        if (character == null ||
            character.data == null)
        {
            return;
        }

        character.data.currentStamina +=
            currentDelta;

        character.data.extraStamina +=
            extraDelta;

        NormalizeSharedStamina(
            character,
            partner);

        if (resetUseTimer)
        {
            character.data.sinceUseStamina =
                0f;
        }

        RefreshStaminaBar();
    }

    private static void ApplyFullSync(
        Character character,
        Character partner,
        float currentStamina,
        float extraStamina,
        float sinceUseStamina)
    {
        if (character == null ||
            character.data == null)
        {
            return;
        }

        float sharedMaximum =
            GetSharedMaxStamina(
                character,
                partner);

        character.data.currentStamina =
            Mathf.Clamp(
                currentStamina,
                0f,
                sharedMaximum);

        character.data.extraStamina =
            Mathf.Clamp(
                extraStamina,
                0f,
                1f);

        character.data.sinceUseStamina =
            Mathf.Max(
                0f,
                sinceUseStamina);

        RefreshStaminaBar();
    }

    private static void RefreshStaminaBar()
    {
        if (GUIManager.instance != null &&
            GUIManager.instance.bar != null)
        {
            GUIManager.instance
                .bar
                .ChangeBar();
        }
    }

    private static StaminaSnapshot CaptureSnapshot(
        Character character)
    {
        StaminaSnapshot snapshot =
            new StaminaSnapshot();

        if (suppressSendDepth > 0 ||
            character == null ||
            character.data == null ||
            !character.IsLocal)
        {
            return snapshot;
        }

        Character partner;

        if (!TryGetPartner(
                character,
                out partner))
        {
            return snapshot;
        }

        snapshot.Track =
            true;

        snapshot.Current =
            character.data.currentStamina;

        snapshot.Extra =
            character.data.extraStamina;

        return snapshot;
    }

    private static void SendSnapshotDelta(
        Character character,
        StaminaSnapshot snapshot)
    {
        if (!snapshot.Track ||
            suppressSendDepth > 0 ||
            character == null ||
            character.data == null ||
            !character.IsLocal)
        {
            return;
        }

        Character partner;

        if (!TryGetPartner(
                character,
                out partner))
        {
            return;
        }

        NormalizeSharedStamina(
            character,
            partner);

        float currentDelta =
            character.data.currentStamina -
            snapshot.Current;

        float extraDelta =
            character.data.extraStamina -
            snapshot.Extra;

        if (Mathf.Abs(
                currentDelta) <=
                ValueEpsilon &&
            Mathf.Abs(
                extraDelta) <=
                ValueEpsilon)
        {
            return;
        }

        bool resetUseTimer =
            currentDelta <
                -ValueEpsilon ||
            extraDelta <
                -ValueEpsilon;

        SendDelta(
            character,
            partner,
            currentDelta,
            extraDelta,
            resetUseTimer);

        RefreshStaminaBar();
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

        int targetActor =
            GetActorNumber(
                partner);

        if (targetActor <= 0)
        {
            return;
        }

        RaiseEventOptions options =
            new RaiseEventOptions
            {
                TargetActors =
                    new int[]
                    {
                        targetActor
                    }
            };

        PhotonNetwork.RaiseEvent(
            StaminaEventCode,
            payload,
            options,
            SendOptions.SendReliable);
    }

    private static void SendDelta(
        Character sender,
        Character partner,
        float currentDelta,
        float extraDelta,
        bool resetUseTimer)
    {
        int senderActor =
            GetActorNumber(
                sender);

        int targetActor =
            GetActorNumber(
                partner);

        if (senderActor <= 0 ||
            targetActor <= 0)
        {
            return;
        }

        SendToPartner(
            sender,
            partner,
            new object[]
            {
                (byte)StaminaAction.Delta,
                senderActor,
                targetActor,
                currentDelta,
                extraDelta,
                resetUseTimer
            });
    }

    private static void SendFullSync(
        Character sender,
        Character partner)
    {
        if (sender == null ||
            sender.data == null)
        {
            return;
        }

        NormalizeSharedStamina(
            sender,
            partner);

        int senderActor =
            GetActorNumber(
                sender);

        int targetActor =
            GetActorNumber(
                partner);

        if (senderActor <= 0 ||
            targetActor <= 0)
        {
            return;
        }

        SendToPartner(
            sender,
            partner,
            new object[]
            {
                (byte)StaminaAction.FullSync,
                senderActor,
                targetActor,
                sender.data.currentStamina,
                sender.data.extraStamina,
                sender.data.sinceUseStamina
            });
    }

    [HarmonyPatch(
        typeof(Character),
        "UseStamina",
        new Type[]
        {
            typeof(float),
            typeof(bool),
            typeof(bool)
        })]
    private static class CharacterUseStaminaPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Character __instance,
            out StaminaSnapshot __state)
        {
            __state =
                CaptureSnapshot(
                    __instance);

            if (__state.Track)
            {
                useStaminaDepth++;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(
            Character __instance,
            StaminaSnapshot __state)
        {
            try
            {
                SendSnapshotDelta(
                    __instance,
                    __state);
            }
            finally
            {
                if (__state.Track &&
                    useStaminaDepth > 0)
                {
                    useStaminaDepth--;
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(Character),
        "CanRegenStamina")]
    private static class CharacterCanRegenStaminaPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Character __instance,
            ref bool __result)
        {
            if (__instance == null ||
                !__instance.IsLocal ||
                suppressSendDepth > 0)
            {
                return;
            }

            Character partner;

            if (!TryGetPartner(
                    __instance,
                    out partner))
            {
                return;
            }

            if (IsClimber(
                    __instance))
            {
                __result =
                    false;
            }
        }
    }

    [HarmonyPatch(
        typeof(Character),
        nameof(Character.AddStamina),
        new Type[]
        {
            typeof(float)
        })]
    private static class CharacterAddStaminaPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Character __instance,
            out StaminaSnapshot __state)
        {
            __state =
                CaptureSnapshot(
                    __instance);

            if (__state.Track)
            {
                addStaminaDepth++;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(
            Character __instance,
            StaminaSnapshot __state)
        {
            try
            {
                SendSnapshotDelta(
                    __instance,
                    __state);
            }
            finally
            {
                if (__state.Track &&
                    addStaminaDepth > 0)
                {
                    addStaminaDepth--;
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(Character),
        nameof(Character.ClampStamina))]
    private static class CharacterClampStaminaPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Character __instance,
            out StaminaSnapshot __state)
        {
            __state =
                CaptureSnapshot(
                    __instance);
        }

        [HarmonyPostfix]
        private static void Postfix(
            Character __instance,
            StaminaSnapshot __state)
        {
            if (addStaminaDepth > 0 ||
                useStaminaDepth > 0)
            {
                return;
            }

            SendSnapshotDelta(
                __instance,
                __state);
        }
    }

    [HarmonyPatch(
        typeof(Character),
        nameof(Character.SetExtraStamina),
        new Type[]
        {
            typeof(float)
        })]
    private static class CharacterSetExtraStaminaPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Character __instance,
            out StaminaSnapshot __state)
        {
            __state =
                CaptureSnapshot(
                    __instance);
        }

        [HarmonyPostfix]
        private static void Postfix(
            Character __instance,
            StaminaSnapshot __state)
        {
            SendSnapshotDelta(
                __instance,
                __state);
        }
    }

    [HarmonyPatch(
        typeof(Character),
        nameof(Character.AddExtraStamina),
        new Type[]
        {
            typeof(float)
        })]
    private static class CharacterAddExtraStaminaPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Character __instance,
            out StaminaSnapshot __state)
        {
            __state =
                CaptureSnapshot(
                    __instance);
        }

        [HarmonyPostfix]
        private static void Postfix(
            Character __instance,
            StaminaSnapshot __state)
        {
            SendSnapshotDelta(
                __instance,
                __state);
        }
    }
}

public sealed class ShareStaminaRuntime :
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

    private void Update()
    {
        if (!active)
        {
            return;
        }

        ShareStamina.RuntimeUpdate();
    }

    public void OnEvent(
        EventData photonEvent)
    {
        if (!active)
        {
            return;
        }

        ShareStamina.HandlePhotonEvent(
            photonEvent);
    }

    private void OnDestroy()
    {
        Deactivate();
    }
}
