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
/// null when <c>MS2_LIVE_LOGIN</c> names the address, which the session reads itself as it did before the launcher.
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
    /// <paramref name="liveLogin"/> is <c>MS2_LIVE_LOGIN</c>; <paramref name="singlePlayer"/> is the launcher's Single
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
