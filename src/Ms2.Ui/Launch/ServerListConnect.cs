namespace Ms2.Ui.Launch;

/// <summary>How a server list connect from the lobby ended, for the flow to act on.</summary>
public enum ServerListConnectEnd
{
    /// <summary>Not started: the flow is not on character select, or a connect is already running.</summary>
    Ignored,
    /// <summary>Logged in again with the flow still on character select: the new list enters it.</summary>
    Entered,
    /// <summary>AlreadyLogin (4 or 5) with the flow still on character select: its box, and the lobby stays.</summary>
    AlreadyLogin,
    /// <summary>Any other failure with the flow still on character select.</summary>
    Failed,
    /// <summary>The flow left character select during the connect, which then failed: whatever moved it showed its own box.</summary>
    Left,
    /// <summary>The flow left character select during the connect, which then logged in: the new login is dropped.</summary>
    LeftLoggedIn,
}

/// <summary>What the session's connect gave: a good login, or the refusal's state (null when it failed otherwise).</summary>
public readonly record struct ServerListConnectResult(bool LoggedIn, int? RefusalState = null);

/// <summary>
/// The lobby's server list connect: the character list's server list popdown (RequestServerList, then the server scene's
/// connect) and the AlreadyLogin box's OK (ConnectToConfiguredServer). In the client both run on the one server list dialog,
/// whose m_nConnecting is set before the connect and cleared on its result (uiserverlistdialog.cpp:502-517, FUN_141ad2ef0
/// :665-671), so one connect runs at a time. The guard here is set before the first await for the same reason: a second
/// start while one is connecting would send a second RequestServerList and a second login, whose new connection drops the
/// first one mid-login. The flow is read again once the connect ends, since a lost login connection may have moved it to
/// the login scene with its box meanwhile.
/// </summary>
public sealed class ServerListConnect
{
    /// <summary>True from the start of a connect to its end.</summary>
    public bool Running { get; private set; }

    /// <param name="inLobby">The flow is on character select.</param>
    /// <param name="connect">The session's connect.</param>
    public async Task<ServerListConnectEnd> RunAsync(Func<bool> inLobby, Func<Task<ServerListConnectResult>> connect)
    {
        if (Running || !inLobby()) return ServerListConnectEnd.Ignored;
        Running = true;
        try
        {
            var r = await connect();
            if (!inLobby()) return r.LoggedIn ? ServerListConnectEnd.LeftLoggedIn : ServerListConnectEnd.Left;
            if (r.LoggedIn) return ServerListConnectEnd.Entered;
            return r.RefusalState is 4 or 5 ? ServerListConnectEnd.AlreadyLogin : ServerListConnectEnd.Failed;
        }
        finally { Running = false; }
    }
}
