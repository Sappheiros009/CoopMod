using BepInEx;
using UnityEngine;

[BepInPlugin(
    PluginGuid,
    PluginName,
    PluginVersion)]
public sealed class CoopMod : BaseUnityPlugin
{
    public const string PluginGuid =
        "com.peak.coopmod";

    public const string PluginName =
        "Co-op Mod";

    public const string PluginVersion =
        "0.0.7";

    private void Awake()
    {
        Debug.Log(
            "[CoopMod] Starting Co-op Mod v" +
            PluginVersion);

        PairPlayerStartLog
            .EnsureCreated();

        OnlyEven
            .Initialize(this);

        Piggyback
            .Initialize(this);

        SeparateRole
            .Initialize(this);

        ShowUI
            .Initialize(this);

        ShareStamina
            .Initialize(this);

        ShareDeath
            .Initialize(this);

        ShareAlive
            .Initialize(this);

        Debug.Log(
            "[CoopMod] All systems initialized.");
    }

    private void OnDestroy()
    {
        ShareAlive
            .Shutdown();

        ShareDeath
            .Shutdown();

        ShareStamina
            .Shutdown();

        ShowUI
            .Shutdown();

        SeparateRole
            .Shutdown();

        Piggyback
            .Shutdown();

        OnlyEven
            .Shutdown();

        Debug.Log(
            "[CoopMod] All systems shutdown.");
    }
}
