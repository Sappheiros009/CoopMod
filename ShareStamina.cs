using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Zorro.Core.Serizalization;

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

    private static bool hasSharedStatusBaseline =
        false;

    private static bool hasSharedWeight =
        false;

    private static float sharedWeight =
        0f;

    private static readonly Dictionary<int, byte[]>
        lastClimberAfflictionData =
            new Dictionary<int, byte[]>();

    private static readonly HashSet<int>
        mirroredAfflictionTypes =
            new HashSet<int>();

    private static readonly HashSet<int>
        mirroredAfflictionsOwnedByShare =
            new HashSet<int>();

    private static readonly CharacterAfflictions.STATUSTYPE[]
        SharedStatusTypes =
        new CharacterAfflictions.STATUSTYPE[]
        {
            CharacterAfflictions.STATUSTYPE.Injury,
            CharacterAfflictions.STATUSTYPE.Hunger,
            CharacterAfflictions.STATUSTYPE.Cold,
            CharacterAfflictions.STATUSTYPE.Poison,
            CharacterAfflictions.STATUSTYPE.Curse,
            CharacterAfflictions.STATUSTYPE.Drowsy,
            CharacterAfflictions.STATUSTYPE.Weight,
            CharacterAfflictions.STATUSTYPE.Hot,
            CharacterAfflictions.STATUSTYPE.Spores
        };

    private static readonly float[]
        lastSharedStatusValues =
        new float[9];

    private static readonly float[]
        syncedLastAddedTimes =
        new float[9];

    private enum StaminaAction : byte
    {
        Delta = 1,
        FullSync = 2,
        SharedStatusSync = 3,
        CarrierStatusDelta = 4,
        SharedAfflictionApply = 5,
        SharedAfflictionRemove = 6
    }

    private struct StaminaSnapshot
    {
        public bool Track;
        public float Current;
        public float Extra;
    }

    private struct PassiveStatusSnapshotState
    {
        public bool Applied;
        public CharacterAfflictions Afflictions;
        public float Drowsy;
        public float Cold;
        public float Hunger;
        public float Poison;
        public float Hot;
        public float Spores;
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

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterAfflictionsUpdateNormalStatusesPatch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterAfflictionsSetStatusWeightPatch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterAfflictionsLastAddedStatusPatch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterAfflictionsAddAfflictionCarriedBypassPatch))
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

        ResetSharedStatusState();

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

        ResetSharedStatusState();

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

        bool pairChanged =
            currentPartnerActor !=
                partnerActor ||
            currentRoleIsCarrier !=
                isCarrier;

        if (pairChanged)
        {
            currentPartnerActor =
                partnerActor;

            currentRoleIsCarrier =
                isCarrier;

            ResetSharedStatusState();

            CaptureSharedStatusBaseline(
                localCharacter);

            if (isCarrier)
            {
                NormalizeSharedStamina(
                    localCharacter,
                    partner);

                SendFullSync(
                    localCharacter,
                    partner);
            }
            else
            {
                SendSharedStatusSync(
                    localCharacter,
                    partner,
                    GetAllSharedStatusMask());
            }

            RefreshStaminaBar();
        }

        if (isCarrier)
        {
            SendCarrierStatusDeltasIfChanged(
                localCharacter,
                partner);

            CleanupExpiredMirroredAfflictions(
                localCharacter);
        }
        else
        {
            SendClimberStatusesIfChanged(
                localCharacter,
                partner);

            SendClimberAfflictionsIfChanged(
                localCharacter,
                partner);
        }
    }

    internal static void HandlePhotonEvent(
        EventData photonEvent)
    {
        if (!initialized ||
            photonEvent == null ||
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

        StaminaAction action =
            (StaminaAction)actionValue;

        bool staminaAction =
            false;

        int canonicalResponseMask =
            0;

        suppressSendDepth++;

        try
        {
            if (action ==
                StaminaAction.Delta)
            {
                if (payload.Length < 6)
                {
                    return;
                }

                staminaAction =
                    true;

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

                staminaAction =
                    true;

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
            else if (action ==
                StaminaAction.SharedStatusSync)
            {
                ApplySharedStatusSync(
                    localCharacter,
                    payload);
            }
            else if (action ==
                StaminaAction.CarrierStatusDelta)
            {
                canonicalResponseMask =
                    ApplyCarrierStatusDelta(
                        localCharacter,
                        payload);
            }
            else if (action ==
                StaminaAction.SharedAfflictionApply)
            {
                ApplySharedAffliction(
                    localCharacter,
                    payload);
            }
            else if (action ==
                StaminaAction.SharedAfflictionRemove)
            {
                RemoveSharedAffliction(
                    localCharacter,
                    payload);
            }
        }
        finally
        {
            suppressSendDepth--;
        }

        if (canonicalResponseMask !=
                0 &&
            IsClimber(
                localCharacter))
        {
            SendSharedStatusSync(
                localCharacter,
                partner,
                canonicalResponseMask);
        }

        if (staminaAction &&
            IsCarrier(
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

        ResetSharedStatusState();
    }

    private static void ResetSharedStatusState()
    {
        hasSharedStatusBaseline =
            false;

        hasSharedWeight =
            false;

        sharedWeight =
            0f;

        lastClimberAfflictionData.Clear();
        mirroredAfflictionTypes.Clear();
        mirroredAfflictionsOwnedByShare.Clear();

        for (int i = 0;
            i < lastSharedStatusValues.Length;
            i++)
        {
            lastSharedStatusValues[i] =
                0f;

            syncedLastAddedTimes[i] =
                0f;
        }
    }

    private static int GetAllSharedStatusMask()
    {
        return
            (1 <<
                SharedStatusTypes.Length) -
            1;
    }

    private static int GetSharedStatusIndex(
        CharacterAfflictions.STATUSTYPE
            statusType)
    {
        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            if (SharedStatusTypes[i] ==
                statusType)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSharedStatus(
        CharacterAfflictions.STATUSTYPE
            statusType)
    {
        return
            GetSharedStatusIndex(
                statusType) >=
            0;
    }

    private static bool TryGetLocalAfflictionsCharacter(
        CharacterAfflictions afflictions,
        out Character character)
    {
        character =
            null;

        if (afflictions == null)
        {
            return false;
        }

        character =
            afflictions.character;

        if (character == null)
        {
            character =
                afflictions
                    .GetComponent<Character>();
        }

        return
            character != null &&
            character.IsLocal;
    }

    private static void CaptureSharedStatusBaseline(
        Character character)
    {
        if (character == null ||
            character.refs == null ||
            character.refs.afflictions ==
                null)
        {
            hasSharedStatusBaseline =
                false;

            return;
        }

        CharacterAfflictions afflictions =
            character.refs.afflictions;

        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            lastSharedStatusValues[i] =
                afflictions.GetCurrentStatus(
                    SharedStatusTypes[i]);
        }

        hasSharedStatusBaseline =
            true;
    }

    private static void UpdateSharedStatusBaseline(
        Character character,
        int mask)
    {
        if (character == null ||
            character.refs == null ||
            character.refs.afflictions ==
                null)
        {
            return;
        }

        CharacterAfflictions afflictions =
            character.refs.afflictions;

        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            if (
                (
                    mask &
                    1 << i
                ) ==
                0
            )
            {
                continue;
            }

            lastSharedStatusValues[i] =
                afflictions.GetCurrentStatus(
                    SharedStatusTypes[i]);
        }

        hasSharedStatusBaseline =
            true;
    }

    private static void SendClimberStatusesIfChanged(
        Character climber,
        Character carrier)
    {
        if (suppressSendDepth > 0 ||
            climber == null ||
            climber.refs == null ||
            climber.refs.afflictions ==
                null)
        {
            return;
        }

        if (!hasSharedStatusBaseline)
        {
            CaptureSharedStatusBaseline(
                climber);

            SendSharedStatusSync(
                climber,
                carrier,
                GetAllSharedStatusMask());

            return;
        }

        int changedMask =
            0;

        CharacterAfflictions afflictions =
            climber.refs.afflictions;

        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            float value =
                afflictions.GetCurrentStatus(
                    SharedStatusTypes[i]);

            if (Mathf.Abs(
                    value -
                    lastSharedStatusValues[i]) >
                ValueEpsilon)
            {
                changedMask |=
                    1 << i;
            }
        }

        if (changedMask ==
            0)
        {
            return;
        }

        SendSharedStatusSync(
            climber,
            carrier,
            changedMask);

        UpdateSharedStatusBaseline(
            climber,
            changedMask);
    }

    private static void SendCarrierStatusDeltasIfChanged(
        Character carrier,
        Character climber)
    {
        if (suppressSendDepth > 0 ||
            carrier == null ||
            carrier.refs == null ||
            carrier.refs.afflictions ==
                null)
        {
            return;
        }

        if (!hasSharedStatusBaseline)
        {
            CaptureSharedStatusBaseline(
                carrier);

            return;
        }

        CharacterAfflictions afflictions =
            carrier.refs.afflictions;

        int changedMask =
            0;

        float[] deltas =
            new float[
                SharedStatusTypes.Length];

        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            CharacterAfflictions.STATUSTYPE
                statusType =
                    SharedStatusTypes[i];

            if (statusType ==
                CharacterAfflictions
                    .STATUSTYPE
                    .Weight ||
                ShouldSuppressCarrierStatusDelta(
                    statusType))
            {
                lastSharedStatusValues[i] =
                    afflictions.GetCurrentStatus(
                        statusType);

                continue;
            }

            float value =
                afflictions.GetCurrentStatus(
                    statusType);

            float delta =
                value -
                lastSharedStatusValues[i];

            if (Mathf.Abs(
                    delta) <=
                ValueEpsilon)
            {
                continue;
            }

            changedMask |=
                1 << i;

            deltas[i] =
                delta;

            lastSharedStatusValues[i] =
                value;
        }

        if (changedMask ==
            0)
        {
            return;
        }

        SendCarrierStatusDelta(
            carrier,
            climber,
            changedMask,
            deltas);
    }

    private static void SendSharedStatusSync(
        Character sender,
        Character partner,
        int mask)
    {
        if (sender == null ||
            sender.refs == null ||
            sender.refs.afflictions ==
                null ||
            mask ==
                0)
        {
            return;
        }

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

        object[] payload =
            new object[
                4 +
                SharedStatusTypes.Length];

        payload[0] =
            (byte)
                StaminaAction
                    .SharedStatusSync;

        payload[1] =
            senderActor;

        payload[2] =
            targetActor;

        payload[3] =
            mask;

        CharacterAfflictions afflictions =
            sender.refs.afflictions;

        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            payload[
                4 +
                i] =
                afflictions.GetCurrentStatus(
                    SharedStatusTypes[i]);
        }

        SendToPartner(
            sender,
            partner,
            payload);
    }

    private static void SendCarrierStatusDelta(
        Character sender,
        Character partner,
        int mask,
        float[] deltas)
    {
        if (sender == null ||
            partner == null ||
            deltas == null ||
            mask ==
                0)
        {
            return;
        }

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

        object[] payload =
            new object[
                4 +
                SharedStatusTypes.Length];

        payload[0] =
            (byte)
                StaminaAction
                    .CarrierStatusDelta;

        payload[1] =
            senderActor;

        payload[2] =
            targetActor;

        payload[3] =
            mask;

        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            payload[
                4 +
                i] =
                deltas[i];
        }

        SendToPartner(
            sender,
            partner,
            payload);
    }

    private static void ApplySharedStatusSync(
        Character character,
        object[] payload)
    {
        if (character == null ||
            character.refs == null ||
            character.refs.afflictions ==
                null ||
            payload == null ||
            payload.Length <
                4 +
                SharedStatusTypes.Length)
        {
            return;
        }

        int mask =
            (int)payload[3];

        CharacterAfflictions afflictions =
            character.refs.afflictions;

        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            if (
                (
                    mask &
                    1 << i
                ) ==
                0
            )
            {
                continue;
            }

            CharacterAfflictions.STATUSTYPE
                statusType =
                    SharedStatusTypes[i];

            float value =
                (float)payload[
                    4 +
                    i];

            if (statusType ==
                CharacterAfflictions
                    .STATUSTYPE
                    .Weight)
            {
                if (!IsCarrier(
                        character))
                {
                    continue;
                }

                hasSharedWeight =
                    true;

                sharedWeight =
                    Mathf.Max(
                        0f,
                        value);
            }

            ApplySyncedStatusValue(
                afflictions,
                statusType,
                value);

            lastSharedStatusValues[i] =
                afflictions.GetCurrentStatus(
                    statusType);
        }

        hasSharedStatusBaseline =
            true;
    }

    private static int ApplyCarrierStatusDelta(
        Character character,
        object[] payload)
    {
        if (!IsClimber(
                character) ||
            character.refs == null ||
            character.refs.afflictions ==
                null ||
            payload == null ||
            payload.Length <
                4 +
                SharedStatusTypes.Length)
        {
            return 0;
        }

        int mask =
            (int)payload[3];

        CharacterAfflictions afflictions =
            character.refs.afflictions;

        int appliedMask =
            0;

        for (int i = 0;
            i < SharedStatusTypes.Length;
            i++)
        {
            if (
                (
                    mask &
                    1 << i
                ) ==
                0
            )
            {
                continue;
            }

            CharacterAfflictions.STATUSTYPE
                statusType =
                    SharedStatusTypes[i];

            if (statusType ==
                CharacterAfflictions
                    .STATUSTYPE
                    .Weight)
            {
                continue;
            }

            float delta =
                (float)payload[
                    4 +
                    i];

            if (Mathf.Abs(
                    delta) <=
                ValueEpsilon)
            {
                continue;
            }

            float current =
                afflictions.GetCurrentStatus(
                    statusType);

            float target =
                current +
                delta;

            if (delta >
                ValueEpsilon)
            {
                syncedLastAddedTimes[i] =
                    Time.time;
            }

            ApplySyncedStatusValue(
                afflictions,
                statusType,
                target);

            lastSharedStatusValues[i] =
                afflictions.GetCurrentStatus(
                    statusType);

            appliedMask |=
                1 << i;
        }

        hasSharedStatusBaseline =
            true;

        return
            appliedMask;
    }

    private static void ApplySyncedStatusValue(
        CharacterAfflictions afflictions,
        CharacterAfflictions.STATUSTYPE statusType,
        float value)
    {
        if (afflictions == null)
        {
            return;
        }

        float current =
            afflictions.GetCurrentStatus(
                statusType);

        float delta =
            value -
            current;

        if (Mathf.Abs(
                delta) <=
            ValueEpsilon)
        {
            return;
        }

        if (statusType ==
            CharacterAfflictions
                .STATUSTYPE
                .Weight)
        {
            afflictions.SetStatus(
                statusType,
                value,
                true);

            return;
        }

        afflictions.AdjustStatus(
            statusType,
            delta,
            true);

        float applied =
            afflictions.GetCurrentStatus(
                statusType);

        if (Mathf.Abs(
                applied -
                value) >
            ValueEpsilon)
        {
            afflictions.SetStatus(
                statusType,
                value,
                true);
        }
    }

    private static bool ShouldMirrorAffliction(
        Affliction.AfflictionType type)
    {
        return
            type ==
                Affliction.AfflictionType.InfiniteStamina ||
            type ==
                Affliction.AfflictionType.FasterBoi ||
            type ==
                Affliction.AfflictionType.ColdOverTime ||
            type ==
                Affliction.AfflictionType.PreventPoisonHealing ||
            type ==
                Affliction.AfflictionType.Sunscreen ||
            type ==
                Affliction.AfflictionType.BingBongShield ||
            type ==
                Affliction.AfflictionType.Invincibility ||
            type ==
                Affliction.AfflictionType.LowGravity ||
            type ==
                Affliction.AfflictionType.Blind ||
            type ==
                Affliction.AfflictionType.Numb ||
            type ==
                Affliction.AfflictionType.ClimbingChalk ||
            type ==
                Affliction.AfflictionType.NoHunger ||
            type ==
                Affliction.AfflictionType.DoubleJumpAmulet ||
            type ==
                Affliction.AfflictionType.MassSuperJump ||
            type ==
                Affliction.AfflictionType.RadiateInfiniteStam;
    }

    private static bool ShouldSuppressCarrierStatusDelta(
        CharacterAfflictions.STATUSTYPE statusType)
    {
        if (statusType ==
                CharacterAfflictions
                    .STATUSTYPE
                    .Cold &&
            mirroredAfflictionTypes.Contains(
                (int)
                    Affliction
                        .AfflictionType
                        .ColdOverTime))
        {
            return true;
        }

        if (statusType ==
                CharacterAfflictions
                    .STATUSTYPE
                    .Drowsy &&
            mirroredAfflictionTypes.Contains(
                (int)
                    Affliction
                        .AfflictionType
                        .FasterBoi))
        {
            return true;
        }

        return false;
    }

    private static byte[] SerializeAffliction(
        Affliction affliction)
    {
        if (affliction == null)
        {
            return null;
        }

        return
            IBinarySerializable
                .ToManagedArray<
                    AfflictionSyncData>(
                    new AfflictionSyncData
                    {
                        afflictions =
                            new List<Affliction>
                            {
                                affliction
                            }
                    });
    }

    private static Affliction DeserializeAffliction(
        byte[] data)
    {
        if (data == null ||
            data.Length ==
                0)
        {
            return null;
        }

        AfflictionSyncData syncData =
            IBinarySerializable
                .GetFromManagedArray<
                    AfflictionSyncData>(
                    data);

        if (syncData.afflictions ==
                null ||
            syncData.afflictions.Count ==
                0)
        {
            return null;
        }

        return
            syncData.afflictions[0];
    }

    private static bool ByteArraysEqual(
        byte[] first,
        byte[] second)
    {
        if (ReferenceEquals(
                first,
                second))
        {
            return true;
        }

        if (first == null ||
            second == null ||
            first.Length !=
                second.Length)
        {
            return false;
        }

        for (int i = 0;
            i < first.Length;
            i++)
        {
            if (first[i] !=
                second[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void SendClimberAfflictionsIfChanged(
        Character climber,
        Character carrier)
    {
        if (suppressSendDepth > 0 ||
            climber == null ||
            climber.refs == null ||
            climber.refs.afflictions ==
                null ||
            carrier == null)
        {
            return;
        }

        Dictionary<int, byte[]>
            current =
                new Dictionary<int, byte[]>();

        List<Affliction> afflictions =
            climber
                .refs
                .afflictions
                .afflictionList;

        if (afflictions != null)
        {
            for (int i = 0;
                i < afflictions.Count;
                i++)
            {
                Affliction affliction =
                    afflictions[i];

                if (affliction == null)
                {
                    continue;
                }

                Affliction.AfflictionType type =
                    affliction.GetAfflictionType();

                if (!ShouldMirrorAffliction(
                        type))
                {
                    continue;
                }

                byte[] data =
                    SerializeAffliction(
                        affliction);

                if (data == null)
                {
                    continue;
                }

                int typeValue =
                    (int)type;

                current[
                    typeValue] =
                    data;

                byte[] previous;

                if (!lastClimberAfflictionData
                        .TryGetValue(
                            typeValue,
                            out previous) ||
                    !ByteArraysEqual(
                        previous,
                        data))
                {
                    SendSharedAfflictionApply(
                        climber,
                        carrier,
                        data);
                }
            }
        }

        List<int> removed =
            new List<int>();

        foreach (
            KeyValuePair<int, byte[]>
                entry
            in lastClimberAfflictionData)
        {
            if (!current.ContainsKey(
                    entry.Key))
            {
                removed.Add(
                    entry.Key);
            }
        }

        for (int i = 0;
            i < removed.Count;
            i++)
        {
            SendSharedAfflictionRemove(
                climber,
                carrier,
                removed[i]);
        }

        lastClimberAfflictionData.Clear();

        foreach (
            KeyValuePair<int, byte[]>
                entry
            in current)
        {
            lastClimberAfflictionData[
                entry.Key] =
                entry.Value;
        }
    }

    private static void SendSharedAfflictionApply(
        Character sender,
        Character partner,
        byte[] data)
    {
        if (sender == null ||
            partner == null ||
            data == null)
        {
            return;
        }

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
                (byte)
                    StaminaAction
                        .SharedAfflictionApply,
                senderActor,
                targetActor,
                data
            });
    }

    private static void SendSharedAfflictionRemove(
        Character sender,
        Character partner,
        int type)
    {
        if (sender == null ||
            partner == null)
        {
            return;
        }

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
                (byte)
                    StaminaAction
                        .SharedAfflictionRemove,
                senderActor,
                targetActor,
                type
            });
    }

    private static void ApplySharedAffliction(
        Character character,
        object[] payload)
    {
        if (!IsCarrier(
                character) ||
            character.refs == null ||
            character.refs.afflictions ==
                null ||
            payload == null ||
            payload.Length <
                4)
        {
            return;
        }

        byte[] data =
            payload[3]
                as byte[];

        Affliction affliction =
            DeserializeAffliction(
                data);

        if (affliction == null)
        {
            return;
        }

        Affliction.AfflictionType type =
            affliction.GetAfflictionType();

        if (!ShouldMirrorAffliction(
                type))
        {
            return;
        }

        int typeValue =
            (int)type;

        Affliction existing;

        bool alreadyHad =
            character
                .refs
                .afflictions
                .HasAfflictionType(
                    type,
                    out existing);

        character
            .refs
            .afflictions
            .AddAffliction(
                affliction,
                true);

        mirroredAfflictionTypes.Add(
            typeValue);

        if (!alreadyHad)
        {
            mirroredAfflictionsOwnedByShare.Add(
                typeValue);
        }
    }

    private static void RemoveSharedAffliction(
        Character character,
        object[] payload)
    {
        if (!IsCarrier(
                character) ||
            character.refs == null ||
            character.refs.afflictions ==
                null ||
            payload == null ||
            payload.Length <
                4)
        {
            return;
        }

        int typeValue =
            (int)payload[3];

        Affliction.AfflictionType type =
            (Affliction.AfflictionType)
                typeValue;

        mirroredAfflictionTypes.Remove(
            typeValue);

        bool owned =
            mirroredAfflictionsOwnedByShare
                .Remove(
                    typeValue);

        if (!owned)
        {
            return;
        }

        Affliction existing;

        if (character
                .refs
                .afflictions
                .HasAfflictionType(
                    type,
                    out existing) &&
            existing != null)
        {
            character
                .refs
                .afflictions
                .RemoveAffliction(
                    existing,
                    true,
                    false);
        }
    }

    private static void CleanupExpiredMirroredAfflictions(
        Character carrier)
    {
        if (carrier == null ||
            carrier.refs == null ||
            carrier.refs.afflictions ==
                null ||
            mirroredAfflictionTypes.Count ==
                0)
        {
            return;
        }

        List<int> expired =
            new List<int>();

        foreach (
            int typeValue
            in mirroredAfflictionTypes)
        {
            Affliction existing;

            if (!carrier
                    .refs
                    .afflictions
                    .HasAfflictionType(
                        (Affliction.AfflictionType)
                            typeValue,
                        out existing))
            {
                expired.Add(
                    typeValue);
            }
        }

        for (int i = 0;
            i < expired.Count;
            i++)
        {
            mirroredAfflictionTypes.Remove(
                expired[i]);

            mirroredAfflictionsOwnedByShare.Remove(
                expired[i]);
        }
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

    private static void RestorePassiveStatus(
        CharacterAfflictions afflictions,
        CharacterAfflictions.STATUSTYPE statusType,
        float value)
    {
        if (afflictions == null)
        {
            return;
        }

        float current =
            afflictions.GetCurrentStatus(
                statusType);

        if (Mathf.Abs(
                current -
                value) <=
            ValueEpsilon)
        {
            return;
        }

        suppressSendDepth++;

        try
        {
            afflictions.SetStatus(
                statusType,
                value,
                true);
        }
        finally
        {
            suppressSendDepth--;
        }
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

    [HarmonyPatch(
        typeof(CharacterAfflictions),
        "UpdateNormalStatuses")]
    private static class
        CharacterAfflictionsUpdateNormalStatusesPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            CharacterAfflictions __instance,
            out PassiveStatusSnapshotState __state)
        {
            __state =
                new PassiveStatusSnapshotState();

            Character character;

            if (!TryGetLocalAfflictionsCharacter(
                    __instance,
                    out character) ||
                !IsCarrier(
                    character))
            {
                return;
            }

            __state.Applied =
                true;

            __state.Afflictions =
                __instance;

            __state.Drowsy =
                __instance.GetCurrentStatus(
                    CharacterAfflictions
                        .STATUSTYPE
                        .Drowsy);

            __state.Cold =
                __instance.GetCurrentStatus(
                    CharacterAfflictions
                        .STATUSTYPE
                        .Cold);

            __state.Hunger =
                __instance.GetCurrentStatus(
                    CharacterAfflictions
                        .STATUSTYPE
                        .Hunger);

            __state.Poison =
                __instance.GetCurrentStatus(
                    CharacterAfflictions
                        .STATUSTYPE
                        .Poison);

            __state.Hot =
                __instance.GetCurrentStatus(
                    CharacterAfflictions
                        .STATUSTYPE
                        .Hot);

            __state.Spores =
                __instance.GetCurrentStatus(
                    CharacterAfflictions
                        .STATUSTYPE
                        .Spores);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            PassiveStatusSnapshotState __state)
        {
            if (!__state.Applied ||
                __state.Afflictions == null)
            {
                return;
            }

            RestorePassiveStatus(
                __state.Afflictions,
                CharacterAfflictions
                    .STATUSTYPE
                    .Drowsy,
                __state.Drowsy);

            RestorePassiveStatus(
                __state.Afflictions,
                CharacterAfflictions
                    .STATUSTYPE
                    .Cold,
                __state.Cold);

            RestorePassiveStatus(
                __state.Afflictions,
                CharacterAfflictions
                    .STATUSTYPE
                    .Hunger,
                __state.Hunger);

            RestorePassiveStatus(
                __state.Afflictions,
                CharacterAfflictions
                    .STATUSTYPE
                    .Poison,
                __state.Poison);

            RestorePassiveStatus(
                __state.Afflictions,
                CharacterAfflictions
                    .STATUSTYPE
                    .Hot,
                __state.Hot);

            RestorePassiveStatus(
                __state.Afflictions,
                CharacterAfflictions
                    .STATUSTYPE
                    .Spores,
                __state.Spores);
        }
    }

    [HarmonyPatch(
        typeof(CharacterAfflictions),
        nameof(
            CharacterAfflictions.SetStatus),
        new Type[]
        {
            typeof(
                CharacterAfflictions
                    .STATUSTYPE),
            typeof(float),
            typeof(bool)
        })]
    private static class
        CharacterAfflictionsSetStatusWeightPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            CharacterAfflictions __instance,
            CharacterAfflictions.STATUSTYPE
                statusType,
            ref float amount)
        {
            if (statusType !=
                    CharacterAfflictions
                        .STATUSTYPE
                        .Weight ||
                !hasSharedWeight)
            {
                return;
            }

            Character character;

            if (!TryGetLocalAfflictionsCharacter(
                    __instance,
                    out character) ||
                !IsCarrier(
                    character))
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

            amount =
                sharedWeight;
        }
    }

    [HarmonyPatch(
        typeof(CharacterAfflictions),
        nameof(
            CharacterAfflictions
                .AddAffliction))]
    private static class
        CharacterAfflictionsAddAfflictionCarriedBypassPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            CharacterAfflictions __instance,
            out Character __state)
        {
            __state =
                null;

            Character character;

            if (!TryGetLocalAfflictionsCharacter(
                    __instance,
                    out character) ||
                !IsClimber(
                    character) ||
                character.data ==
                    null ||
                !character.data.isCarried)
            {
                return;
            }

            __state =
                character;

            character.data.isCarried =
                false;
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            Character __state)
        {
            if (__state != null &&
                __state.data !=
                    null)
            {
                __state.data.isCarried =
                    true;
            }

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(CharacterAfflictions),
        nameof(
            CharacterAfflictions
                .LastAddedStatus),
        new Type[]
        {
            typeof(
                CharacterAfflictions
                    .STATUSTYPE)
        })]
    private static class
        CharacterAfflictionsLastAddedStatusPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            CharacterAfflictions __instance,
            CharacterAfflictions.STATUSTYPE
                statusType,
            ref float __result)
        {
            Character character;

            if (!TryGetLocalAfflictionsCharacter(
                    __instance,
                    out character) ||
                !IsClimber(
                    character))
            {
                return;
            }

            int index =
                GetSharedStatusIndex(
                    statusType);

            if (index <
                0)
            {
                return;
            }

            if (syncedLastAddedTimes[index] >
                __result)
            {
                __result =
                    syncedLastAddedTimes[index];
            }
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
