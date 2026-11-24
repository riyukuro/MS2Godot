using System;
using Godot;
using Ms2.Net;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>The login server's connection outside the field: how a game start ended, and a connection lost on the login,
/// character select or create scene (the window service's socket messages, <see cref="Ms2.Ui.FrontEndConnection"/>).</summary>
public partial class LiveSession
{
    /// <summary>How <see cref="EnterAsync"/> ended.</summary>
    public enum EnterEnd
    {
        /// <summary>The game server's RequestFieldEnter came: the field loads.</summary>
        Entered,
        /// <summary>The login server refused the start (LoginToGame with a result, <see cref="EnterResult.Answer"/>).</summary>
        Refused,
        /// <summary>The game server's connect failed (window message 0x467, the code in <see cref="EnterResult.Code"/>).</summary>
        ConnectFailed,
        /// <summary>The game server refused the key (its MoveResult error in <see cref="EnterResult.Code"/>).</summary>
        MoveRefused,
        /// <summary>The login connection was gone (<see cref="EnterResult.Code"/>: the socket error, 0 for a clean close).</summary>
        Dropped,
        /// <summary>Anything else: no answer in time, the game connection lost before the field.</summary>
        Failed,
    }

    public readonly record struct EnterResult(EnterEnd End, int Code = 0, LoginToGame Answer = null);

    /// <summary>The login connection closed with a socket error while it was the session's (window message 0x465 outside the
    /// field): the code. Raised on the main thread.</summary>
    public event Action<int> LoginConnectionLost;

    /// <summary>The socket error of the last login whose connect failed (window message 0x467); 0 when it did not fail at the
    /// connect.</summary>
    public int LastConnectError { get; private set; }

    /// <summary>True from the good LoginToGame on: the login server ends its session after it (a clean close), which is the
    /// move, not a loss.</summary>
    bool leavingLogin;

    /// <summary>The session's login connection from now on, watched for a close (<see cref="OnLoginClosed"/>).</summary>
    void UseLogin(TcpTransport t)
    {
        login = t;
        leavingLogin = false;
        t.Closed += () => OnLoginClosed(t);
    }

    /// <summary>
    /// CGameSocket::OnClose (gamesocket.cpp:221-236) for the login connection: only a close with a socket error on the
    /// session's connection reaches the window service (0x465); a clean close by the server (error 0) and our own close
    /// (Dispose: no failure) only log, as the client's else branch does.
    /// </summary>
    void OnLoginClosed(TcpTransport t)
    {
        if (t.Failure == null) return;
        int code = Ms2.Ui.FrontEndConnection.CodeOf(t.Failure);
        if (code == 0 || leavingLogin)
        {
            GD.Print($"live: login connection closed ({t.Failure.Message}), no socket error: nothing is shown, as in the client");
            return;
        }
        GD.Print($"live: login connection lost, socket error {code} ({t.Failure.Message})");
        Callable.From(() => { if (login == t) LoginConnectionLost?.Invoke(code); }).CallDeferred();
    }
}
