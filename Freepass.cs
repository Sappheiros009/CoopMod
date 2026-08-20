using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

public static class Freepass
{
    private const string HarmonyId =
        "com.peak.coopmod.freepass";

    private static Harmony harmony;

    private static bool initialized;

    private static readonly Dictionary<int, FreepassState>
        states =
            new Dictionary<int, FreepassState>();

    private sealed class FreepassState
    {
        public Character Climber;
        public Character Carrier;

        public Collider[] ClimberColliders;
        public Collider[] CarrierColliders;

        public Item Item;
        public Collider[] ItemColliders;
    }

    private struct GroundedProxyState
    {
        public Character Climber;
        public bool Applied;
        public bool OriginalGrounded;
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
                    CharacterItems_FixedUpdate_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    Character_FixedUpdate_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    RopeShooter_WillAttach_GroundedProxy_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    RopeShooter_OnPrimaryFinishedCast_GroundedProxy_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    VineShooter_WillAttach_GroundedProxy_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    VineShooter_OnPrimaryFinishedCast_GroundedProxy_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    Constructable_TryUpdatePreview_GroundedProxy_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    CharacterItems_RaycastClimbingSpikeStart_GroundedProxy_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    ActionRaycastSpawnSomething_FixedUpdate_GroundedProxy_Patch))
            .Patch();

        initialized =
            true;
    }

    public static void Shutdown()
    {
        if (!initialized)
        {
            return;
        }

        RestoreAll();

        if (harmony != null)
        {
            harmony.UnpatchSelf();

            harmony =
                null;
        }

        initialized =
            false;
    }

    private static bool TryGetPair(
        Character climber,
        out Character carrier)
    {
        carrier =
            null;

        if (climber == null ||
            climber.data == null ||
            !climber.data.isCarried)
        {
            return false;
        }

        carrier =
            climber
                .data
                .carrier;

        if (carrier == null ||
            carrier.data == null ||
            carrier.data.carriedPlayer !=
                climber)
        {
            carrier =
                null;

            return false;
        }

        return true;
    }

    private static Item GetHeldItem(
        Character climber)
    {
        if (climber == null ||
            climber.data == null)
        {
            return null;
        }

        Item item =
            climber
                .data
                .currentItem;

        if (item == null ||
            item.itemState !=
                ItemState.Held ||
            item.holderCharacter !=
                climber)
        {
            return null;
        }

        return
            item;
    }

    private static Collider[]
        GetCharacterBodyColliders(
            Character character)
    {
        if (character == null ||
            character.refs == null ||
            character.refs.ragdoll == null)
        {
            return
                Array.Empty<Collider>();
        }

        RigCreatorCollider[] rigColliders =
            character
                .refs
                .ragdoll
                .GetComponentsInChildren<
                    RigCreatorCollider>(
                    true);

        if (rigColliders == null ||
            rigColliders.Length ==
                0)
        {
            return
                Array.Empty<Collider>();
        }

        HashSet<Collider> unique =
            new HashSet<Collider>();

        for (int i = 0;
            i < rigColliders.Length;
            i++)
        {
            RigCreatorCollider rigCollider =
                rigColliders[i];

            if (rigCollider == null)
            {
                continue;
            }

            Collider collider =
                rigCollider
                    .GetComponent<Collider>();

            if (collider == null)
            {
                continue;
            }

            Character owner =
                collider
                    .GetComponentInParent<
                        Character>();

            if (owner !=
                character)
            {
                continue;
            }

            unique.Add(
                collider);
        }

        if (unique.Count ==
            0)
        {
            return
                Array.Empty<Collider>();
        }

        Collider[] result =
            new Collider[
                unique.Count];

        unique.CopyTo(
            result);

        return
            result;
    }

    private static Collider[]
        GetItemColliders(
            Item item)
    {
        if (item == null)
        {
            return
                Array.Empty<Collider>();
        }

        Collider[] all =
            item
                .GetComponentsInChildren<
                    Collider>(
                    true);

        if (all == null ||
            all.Length ==
                0)
        {
            return
                Array.Empty<Collider>();
        }

        List<Collider> result =
            new List<Collider>(
                all.Length);

        for (int i = 0;
            i < all.Length;
            i++)
        {
            Collider collider =
                all[i];

            if (collider == null)
            {
                continue;
            }

            Item owner =
                collider
                    .GetComponentInParent<
                        Item>();

            if (owner !=
                item)
            {
                continue;
            }

            result.Add(
                collider);
        }

        return
            result.ToArray();
    }

    private static void SetCollisionIgnore(
        Collider[] first,
        Collider[] second,
        bool ignore)
    {
        if (first == null ||
            second == null)
        {
            return;
        }

        for (int i = 0;
            i < first.Length;
            i++)
        {
            Collider firstCollider =
                first[i];

            if (firstCollider == null)
            {
                continue;
            }

            for (int j = 0;
                j < second.Length;
                j++)
            {
                Collider secondCollider =
                    second[j];

                if (secondCollider == null ||
                    firstCollider ==
                        secondCollider)
                {
                    continue;
                }

                if (Physics.GetIgnoreCollision(
                        firstCollider,
                        secondCollider) ==
                    ignore)
                {
                    continue;
                }

                Physics.IgnoreCollision(
                    firstCollider,
                    secondCollider,
                    ignore);
            }
        }
    }

    private static void RestoreHeldItem(
        FreepassState state)
    {
        if (state == null ||
            state.Item == null)
        {
            return;
        }

        SetCollisionIgnore(
            state.ItemColliders,
            state.CarrierColliders,
            false);

        SetCollisionIgnore(
            state.ItemColliders,
            state.ClimberColliders,
            false);

        state.Item =
            null;

        state.ItemColliders =
            null;
    }

    private static void ApplyHeldItemIsolation(
        FreepassState state,
        Item item)
    {
        if (state == null ||
            item == null)
        {
            return;
        }

        if (state.Item !=
            item)
        {
            RestoreHeldItem(
                state);

            state.Item =
                item;

            state.ItemColliders =
                GetItemColliders(
                    item);
        }

        SetCollisionIgnore(
            state.ItemColliders,
            state.CarrierColliders,
            true);

        SetCollisionIgnore(
            state.ItemColliders,
            state.ClimberColliders,
            true);
    }

    private static FreepassState CreateState(
        Character climber,
        Character carrier)
    {
        FreepassState state =
            new FreepassState
            {
                Climber =
                    climber,

                Carrier =
                    carrier,

                ClimberColliders =
                    GetCharacterBodyColliders(
                        climber),

                CarrierColliders =
                    GetCharacterBodyColliders(
                        carrier)
            };

        SetCollisionIgnore(
            state.ClimberColliders,
            state.CarrierColliders,
            true);

        return
            state;
    }

    private static void RestoreState(
        int climberId)
    {
        FreepassState state;

        if (!states.TryGetValue(
                climberId,
                out state))
        {
            return;
        }

        if (state != null)
        {
            RestoreHeldItem(
                state);

            SetCollisionIgnore(
                state.ClimberColliders,
                state.CarrierColliders,
                false);
        }

        states.Remove(
            climberId);
    }

    private static void RestoreAll()
    {
        List<int> keys =
            new List<int>(
                states.Keys);

        for (int i = 0;
            i < keys.Count;
            i++)
        {
            RestoreState(
                keys[i]);
        }
    }

    private static FreepassState EnsureState(
        Character climber,
        Character carrier)
    {
        int climberId =
            climber.GetInstanceID();

        FreepassState state;

        if (states.TryGetValue(
                climberId,
                out state))
        {
            if (state != null &&
                state.Climber ==
                    climber &&
                state.Carrier ==
                    carrier)
            {
                return
                    state;
            }

            RestoreState(
                climberId);
        }

        state =
            CreateState(
                climber,
                carrier);

        states[
            climberId] =
            state;

        return
            state;
    }

    private static void UpdateFreepass(
        Character character)
    {
        if (!initialized ||
            character == null)
        {
            return;
        }

        Character carrier;

        if (!TryGetPair(
                character,
                out carrier))
        {
            RestoreState(
                character.GetInstanceID());

            return;
        }

        FreepassState state =
            EnsureState(
                character,
                carrier);

        if (state == null)
        {
            return;
        }

        SetCollisionIgnore(
            state.ClimberColliders,
            state.CarrierColliders,
            true);

        Item item =
            GetHeldItem(
                character);

        if (item == null)
        {
            RestoreHeldItem(
                state);
        }
        else
        {
            ApplyHeldItemIsolation(
                state,
                item);
        }
    }

    private static bool IsCarriedClimberItems(
        CharacterItems items,
        out Character climber,
        out Character carrier)
    {
        climber =
            null;

        carrier =
            null;

        if (items == null)
        {
            return false;
        }

        climber =
            items
                .GetComponent<Character>();

        return
            TryGetPair(
                climber,
                out carrier);
    }

    private static GroundedProxyState BeginGroundedProxy(
        Character climber)
    {
        GroundedProxyState state =
            new GroundedProxyState();

        if (!initialized ||
            climber == null ||
            climber.data == null)
        {
            return
                state;
        }

        Character carrier;

        if (!TryGetPair(
                climber,
                out carrier) ||
            carrier == null ||
            carrier.data == null)
        {
            return
                state;
        }

        state.Climber =
            climber;

        state.OriginalGrounded =
            climber.data.isGrounded;

        state.Applied =
            true;

        climber.data.isGrounded =
            carrier.data.isGrounded;

        return
            state;
    }

    private static GroundedProxyState BeginGroundedProxy(
        Item item)
    {
        if (item == null)
        {
            return
                new GroundedProxyState();
        }

        return
            BeginGroundedProxy(
                item.holderCharacter);
    }

    private static void EndGroundedProxy(
        GroundedProxyState state)
    {
        if (!state.Applied ||
            state.Climber == null ||
            state.Climber.data == null)
        {
            return;
        }

        state.Climber.data.isGrounded =
            state.OriginalGrounded;
    }

    [HarmonyPatch(
        typeof(RopeShooter),
        nameof(
            RopeShooter.WillAttach))]
    private static class
        RopeShooter_WillAttach_GroundedProxy_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            RopeShooter __instance,
            out GroundedProxyState __state)
        {
            __state =
                BeginGroundedProxy(
                    __instance != null
                        ? __instance.item
                        : null);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            GroundedProxyState __state)
        {
            EndGroundedProxy(
                __state);

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(RopeShooter),
        "OnPrimaryFinishedCast")]
    private static class
        RopeShooter_OnPrimaryFinishedCast_GroundedProxy_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            RopeShooter __instance,
            out GroundedProxyState __state)
        {
            __state =
                BeginGroundedProxy(
                    __instance != null
                        ? __instance.item
                        : null);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            GroundedProxyState __state)
        {
            EndGroundedProxy(
                __state);

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(VineShooter),
        nameof(
            VineShooter.WillAttach))]
    private static class
        VineShooter_WillAttach_GroundedProxy_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            VineShooter __instance,
            out GroundedProxyState __state)
        {
            __state =
                BeginGroundedProxy(
                    __instance != null
                        ? __instance.item
                        : null);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            GroundedProxyState __state)
        {
            EndGroundedProxy(
                __state);

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(VineShooter),
        "OnPrimaryFinishedCast")]
    private static class
        VineShooter_OnPrimaryFinishedCast_GroundedProxy_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            VineShooter __instance,
            out GroundedProxyState __state)
        {
            __state =
                BeginGroundedProxy(
                    __instance != null
                        ? __instance.item
                        : null);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            GroundedProxyState __state)
        {
            EndGroundedProxy(
                __state);

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(Constructable),
        nameof(
            Constructable.TryUpdatePreview))]
    private static class
        Constructable_TryUpdatePreview_GroundedProxy_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            Constructable __instance,
            out GroundedProxyState __state)
        {
            __state =
                BeginGroundedProxy(
                    __instance != null
                        ? __instance.item
                        : null);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            GroundedProxyState __state)
        {
            EndGroundedProxy(
                __state);

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(CharacterItems),
        nameof(
            CharacterItems
                .RaycastClimbingSpikeStart))]
    private static class
        CharacterItems_RaycastClimbingSpikeStart_GroundedProxy_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            CharacterItems __instance,
            out GroundedProxyState __state)
        {
            Character climber =
                __instance != null
                    ? __instance
                        .GetComponent<Character>()
                    : null;

            __state =
                BeginGroundedProxy(
                    climber);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            GroundedProxyState __state)
        {
            EndGroundedProxy(
                __state);

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(
            Peak.Action_RaycastSpawnSomething),
        "FixedUpdate")]
    private static class
        ActionRaycastSpawnSomething_FixedUpdate_GroundedProxy_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            Peak.Action_RaycastSpawnSomething
                __instance,
            out GroundedProxyState __state)
        {
            Item item =
                __instance != null
                    ? __instance
                        .GetComponent<Item>()
                    : null;

            __state =
                BeginGroundedProxy(
                    item);
        }

        [HarmonyFinalizer]
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            GroundedProxyState __state)
        {
            EndGroundedProxy(
                __state);

            return
                __exception;
        }
    }

    [HarmonyPatch(
        typeof(CharacterItems),
        "FixedUpdate")]
    private static class
        CharacterItems_FixedUpdate_Patch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            CharacterItems __instance)
        {
            Character climber;
            Character carrier;

            if (!IsCarriedClimberItems(
                    __instance,
                    out climber,
                    out carrier))
            {
                return;
            }

            UpdateFreepass(
                climber);
        }
    }

    [HarmonyPatch(
        typeof(Character),
        "FixedUpdate")]
    private static class
        Character_FixedUpdate_Patch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Character __instance)
        {
            UpdateFreepass(
                __instance);
        }
    }
}
