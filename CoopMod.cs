using BepInEx;

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
        Freepass
            .Initialize(this);

        PairPlayerStartLog
            .EnsureCreated();

        OnlyEven
            .Initialize(this);

        Piggyback
            .Initialize(this);

        SeparateRole
            .Initialize(this);

        ItemFix
            .Initialize(this);

        ShareStamina
            .Initialize(this);

        ShareDeath
            .Initialize(this);

        ShareAlive
            .Initialize(this);
    }

    private void OnDestroy()
    {
        ShareAlive
            .Shutdown();

        ShareDeath
            .Shutdown();

        ShareStamina
            .Shutdown();

        ItemFix
            .Shutdown();

        SeparateRole
            .Shutdown();

        Piggyback
            .Shutdown();

        OnlyEven
            .Shutdown();

        Freepass
            .Shutdown();
    }
}
