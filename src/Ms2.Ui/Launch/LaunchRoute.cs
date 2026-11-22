namespace Ms2.Ui.Launch;

/// <summary>What a game start does before the client's first screen.</summary>
public enum LaunchMode
{
    /// <summary>The launcher screen: the player picks single player or a saved server.</summary>
    Launcher,
    /// <summary>The offline flow, unchanged: the client's login screen, any id, the stub character list.</summary>
    Offline,
    /// <summary>The live mode against a login server.</summary>
    Live,
}

/// <summary>
/// The start a launcher choice picks. <see cref="Server"/> is set when the address comes from the launcher's list; it is
/// null when the command line's <c>--ip</c>/<c>--port</c> name the address, which the session reads itself.
/// </summary>
public readonly record struct LaunchDecision(LaunchMode Mode, ServerEntry Server = null)
{
    public static LaunchDecision Launcher => new(LaunchMode.Launcher);
    public static LaunchDecision Offline => new(LaunchMode.Offline);
    public static LaunchDecision Live(ServerEntry server = null) => new(LaunchMode.Live, server);
}

/// <summary>
/// Which start a run takes. The launcher is MS2Godot's own page before the client's Logo/Login states; the client has no
/// such page. It runs only for the flow scene and only when no live address is given, so the test and perf scenes
/// (setup, the HUD and the field scenes, tests/*.tscn) and the live runs start exactly as they did before.
/// </summary>
public static class LaunchRoute
{
    /// <summary>The scene that starts the client flow; only this one shows the launcher.</summary>
    public const string FlowScene = "res://flow/flow.tscn";

    /// <summary>
    /// The live address of the command line (<c>--ip</c> and <c>--port</c>, the launcher's form): it counts only together with a
    /// ticket (<c>--ticket</c>, the launcher's own start) or <c>MS2_LIVE_ACCOUNT</c> (the account comes from the variable), as the
    /// address alone did before. An address alone leaves the launcher page in place. Null when the address is incomplete or alone.
    /// </summary>
    public static string LiveAddress(Ms2.Net.LauncherArgs args, string liveAccount) =>
        args is { HasAddress: true } && (args.Ticket != null || !string.IsNullOrEmpty(liveAccount)) ? $"{args.Host}:{args.Port}" : null;

    /// <summary>
    /// <paramref name="liveLogin"/> is the <see cref="LiveAddress"/> (null without one); <paramref name="singlePlayer"/> is the launcher's Single
    /// player choice; <paramref name="server"/> is a saved entry whose Connect was pressed. With the variable set the
    /// live mode wins over the launcher and the list (the override the headless runs use), and the session reads the
    /// address from it.
    /// </summary>
    public static LaunchDecision Decide(string scenePath, string liveLogin, bool singlePlayer, ServerEntry server)
    {
        if (!string.IsNullOrEmpty(liveLogin)) return LaunchDecision.Live();
        if (scenePath != FlowScene) return LaunchDecision.Offline;
        if (server != null) return LaunchDecision.Live(server);
        if (singlePlayer) return LaunchDecision.Offline;
        return LaunchDecision.Launcher;
    }
}
