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

/// <summary>How the client's login scene logs in (<see cref="LaunchRoute.LoginStartFor"/>).</summary>
public enum LoginStart
{
    /// <summary>The login dialog: the player types an id and a password.</summary>
    Dialog,
    /// <summary>The launcher's ticket: no login dialog, the scene connects and sends the ticket login by itself.</summary>
    Passport,
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
    /// address alone did before. An address alone leaves the launcher page in place. Null when the address is incomplete or alone,
    /// or when the arguments have a problem (a bad --port with a bare port still has an address).
    /// </summary>
    public static string LiveAddress(Ms2.Net.LauncherArgs args, string liveAccount) =>
        args is { Problem: null, HasAddress: true } && (args.Ticket != null || !string.IsNullOrEmpty(liveAccount)) ? $"{args.Host}:{args.Port}" : null;

    /// <summary>
    /// How the login scene starts. A launcher ticket with its address is the Nexon platform's passport start: the platform
    /// takes its passport from the command line (vfunc_17 -> ParseLaunchArguments @0x1414b1980, unit_1414b1230.cpp:241-272;
    /// without one it stops with INVALID_LAUNCH, unit_140c77ab0_clientplatformnexonkorea.cpp:288-305), the login scene opens no login dialog and connects
    /// once its load is done (UISceneLogin::vfunc_15, uiscenelogin.cpp:191-220), and the login request carries the passport
    /// instead of an id and password (UILoginDialog::SendLoginRequest, uilogindialog.cpp:216-221 -> vfunc_20, :332-353).
    /// Every other start (no arguments, an address alone or with <c>MS2_LIVE_ACCOUNT</c>, the launcher's entries, single
    /// player, arguments with a problem) is the id and password start: the login dialog (OpenLoginDialogs, :413-418).
    /// </summary>
    public static LoginStart LoginStartFor(Ms2.Net.LauncherArgs args) =>
        args is { Problem: null, HasAddress: true, Ticket: not null } ? LoginStart.Passport : LoginStart.Dialog;

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
