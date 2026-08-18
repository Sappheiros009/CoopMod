using System;
using BepInEx.Configuration;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

public static class OnlyEven
{
    private const string HarmonyId =
        "com.peak.coopmod.onlyeven";

    private static Harmony harmony;

    private static bool initialized =
        false;

    public static ConfigEntry<bool>
        AllowOnePlayer;

    public static ConfigEntry<bool>
        AllowThreePlayers;

    public static void Initialize(
        CoopMod plugin)
    {
        if (initialized)
        {
            return;
        }

        if (plugin == null)
        {
            Debug.LogError(
                "[OnlyEven] CoopMod instance is null.");

            return;
        }

        AllowOnePlayer =
            plugin.Config.Bind(
                "Player Count",
                "Allow 1 Player",
                false,
                "1명인 상태에서도 게임 시작을 허용합니다."
            );

        AllowThreePlayers =
            plugin.Config.Bind(
                "Player Count",
                "Allow 3 Players",
                false,
                "3명인 상태에서도 게임 시작을 허용합니다."
            );

        harmony =
            new Harmony(
                HarmonyId);

        harmony
            .CreateClassProcessor(
                typeof(
                    AirportCheckInKiosk_StartGame_Patch))
            .Patch();

        harmony
            .CreateClassProcessor(
                typeof(
                    AirportCheckInKiosk_LoadIslandMaster_Patch))
            .Patch();

        initialized =
            true;

        Debug.Log(
            "[OnlyEven] Initialized.");

        Debug.Log(
            "[OnlyEven] Allow 1 Player: " +
            AllowOnePlayer.Value);

        Debug.Log(
            "[OnlyEven] Allow 3 Players: " +
            AllowThreePlayers.Value);
    }

    public static void Shutdown()
    {
        if (!initialized)
        {
            return;
        }

        if (harmony != null)
        {
            harmony.UnpatchSelf();

            harmony =
                null;
        }

        AllowOnePlayer =
            null;

        AllowThreePlayers =
            null;

        initialized =
            false;

        Debug.Log(
            "[OnlyEven] Shutdown.");
    }

    public static int
        GetCurrentPlayerCount()
    {
        if (!PhotonNetwork.InRoom ||
            PhotonNetwork.CurrentRoom == null)
        {
            return 0;
        }

        return
            PhotonNetwork
                .CurrentRoom
                .PlayerCount;
    }

    public static bool
        CanStartGame()
    {
        int playerCount =
            GetCurrentPlayerCount();

        if (playerCount == 2 ||
            playerCount == 4)
        {
            return true;
        }

        if (playerCount == 1 &&
            AllowOnePlayer != null &&
            AllowOnePlayer.Value)
        {
            return true;
        }

        if (playerCount == 3 &&
            AllowThreePlayers != null &&
            AllowThreePlayers.Value)
        {
            return true;
        }

        return false;
    }

    private static void
        ShowBlockedMessage(
            string source)
    {
        int playerCount =
            GetCurrentPlayerCount();

        Debug.Log(
            "[OnlyEven] " +
            source +
            " blocked. PlayerCount=" +
            playerCount);

        PairPlayerStartLog
            .ShowEvenPlayerRequired();
    }

    [HarmonyPatch(
        typeof(AirportCheckInKiosk),
        nameof(AirportCheckInKiosk.StartGame),
        new Type[]
        {
            typeof(int)
        })]
    private static class
        AirportCheckInKiosk_StartGame_Patch
    {
        private static bool Prefix()
        {
            if (CanStartGame())
            {
                return true;
            }

            ShowBlockedMessage(
                "Game start");

            return false;
        }
    }

    [HarmonyPatch(
        typeof(AirportCheckInKiosk),
        nameof(AirportCheckInKiosk.LoadIslandMaster),
        new Type[]
        {
            typeof(int),
            typeof(byte[])
        })]
    private static class
        AirportCheckInKiosk_LoadIslandMaster_Patch
    {
        private static bool Prefix()
        {
            if (CanStartGame())
            {
                return true;
            }

            ShowBlockedMessage(
                "Master scene load");

            return false;
        }
    }
}
