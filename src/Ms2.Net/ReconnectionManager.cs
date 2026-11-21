using Ms2.Net.Packets;

namespace Ms2.Net;

/// <summary>What CReconnectionManagerCli reaches outside itself: the socket, the scene, the progress dialog and the window messages it posts.</summary>
public interface IReconnectHost {
    /// <summary>CGetTickCountMs2: milliseconds, wrapping like the client's unsigned tick.</summary>
    uint Now { get; }
    /// <summary>The scene is the game scene (state 0x620000c); reconnects only happen there.</summary>
    bool InGame { get; }
    bool HasConnection { get; }
    /// <summary>CGameSocket::GetDisconnect: the client closed the connection itself.</summary>
    bool SocketDisconnected { get; }
    /// <summary>DAT_143af7d44 == 1 (the client is shutting down) or g_socketClosing == 1.</summary>
    bool Closing { get; }
    string SocketHost { get; }
    int SocketPort { get; }
    int SocketSequence { get; }
    bool IsConnected { get; }
    /// <summary>CNetworkService::ConnectTo(host, port, 1, 1, 1): one connect attempt to the saved address; false when it failed at once.</summary>
    bool Connect(string host, int port);
    void Close();
    void SendReconnectRequest(ReconnectRequest request);
    /// <summary>UIReconnectTopLayerNotify (id 0x4f78): attempt a of b; the dialog is hidden by <see cref="HideProgress"/>.</summary>
    void ShowProgress(int attempt, int attempts);
    void HideProgress();
    /// <summary>The system notice the client prints for each step (debug builds' "[DEBUG] ..." lines).</summary>
    void Notice(string text);
    /// <summary>The window message 0x46b: tells the main loop to start a reconnect attempt with this code.</summary>
    void PostAttempt(int code);
    /// <summary>The window message 0x465: the reconnect is over (failed or given up) with this code.</summary>
    void PostGaveUp(int code);
    /// <summary>Kind 3 while in game: SendRequestQuitAndDisconnect, the scene change to 0x6200009 and the message box with string id 0x20f.</summary>
    void EndSession();
}

/// <summary>
/// CReconnectionManagerCli (reconnectionmanagercli.cpp @0x140aace60-0x140aadd70), the state machine for getting a dropped game connection back: the options and
/// context the server sent, the attempt counters, and the decisions (whether to try, whether it is over). Time and the socket come from <see cref="IReconnectHost"/>.
/// STILL-OPEN: the client's socket read loop that calls <see cref="ShouldReconnect"/> and <see cref="Begin"/> on a drop is not hooked into LiveSession.
/// </summary>
public sealed class ReconnectionManager {
    /// <summary>Codes the client raises itself: the socket closed (0x2745), a result arrived outside the game scene (0xcc), the time ran out (0xc9).</summary>
    public const int ErrorSocketClosed = 0x2745, ErrorNotInGame = 0xcc, ErrorTimedOut = 0xc9;

    readonly IReconnectHost host;
    readonly ReconnectionOptions options = new();
    ReconnectionContext context = new(0, "", 0);
    uint lastTick;           // m_08: the Tick throttle
    // ReconnectionContextCli
    int attempt, maxAttempts, postParam, session, lastError, port;
    bool pending, disabled;
    uint startTick, lastAttemptTick, deadline;
    string savedHost = "";
    long account, secondId; string machineId = "";

    public ReconnectionManager(IReconnectHost host) { this.host = host; }

    public ReconnectionOptions Options => options;
    public ReconnectionContext Context => context;
    public int Attempt => attempt;
    public int LastError => lastError;
    public bool Pending => pending;

    /// <summary>SetOptions (@0x140aad050), the Gvg packet's kind 43.</summary>
    public void SetOptions(ReconnectionOptions value) => options.Assign(value);

    /// <summary>The ids SendReconnectRequest writes: the account (DAT_143ab2820), the second id (DAT_143ab2828) and the machine id text.</summary>
    public void SetIdentity(long accountId, long secondSessionId, string machine) { account = accountId; secondId = secondSessionId; machineId = machine; }

    /// <summary>IsReconnecting (@0x140aad1e0): a reconnect has begun and not finished.</summary>
    public bool IsReconnecting => startTick != 0;

    /// <summary>IsReconnectTimedOut (@0x140aad220): disabled, out of attempts, or past the give-up seconds since it began.</summary>
    public bool IsTimedOut() {
        if (disabled) return true;
        if (attempt > maxAttempts) return true;
        uint limit = unchecked((uint) options[ReconnectionOptions.GiveUpSeconds] * 1000 + startTick);
        return limit < host.Now;
    }

    /// <summary>IsFatalReconnectError (@0x140aad2d0): codes the server uses for "do not try again".</summary>
    public static bool IsFatalError(int code) => code is 100 or 101 or 102 or 103 or 104 or 105 or 106 or 203 or 204 or 400 or 401 or 402 or 403 or 404 or 405 or 407 or 408;

    /// <summary>ShouldReconnect (@0x140aacf50): in game, a live connection the client did not close itself, reconnect on, not timed out, and the server's context said mode 1.</summary>
    public bool ShouldReconnect(int error) {
        if (!host.InGame) return false;
        if (!host.HasConnection) return false;
        if (host.SocketDisconnected) return false;
        if (host.Closing) return false;
        if (options[ReconnectionOptions.Enabled] == 0) return false;
        if (IsReconnecting && IsTimedOut()) return false;
        if (context.Mode != 1) return false;
        return error != 0;    // 0 breaks out of the client's switch to "false"; 0x2745 and every other code say yes
    }

    /// <summary>Reset (@0x140aad0d0): the context and the counters back to their start.</summary>
    public void Reset() {
        lastTick = 0;
        context = new(0, "", 0);
        attempt = maxAttempts = postParam = session = lastError = port = 0; pending = disabled = false;
        startTick = lastAttemptTick = deadline = 0; savedHost = "";
    }

    /// <summary>BeginReconnect (@0x140aad340): starts the clock, remembers the socket sequence, posts the attempt message and shows the progress dialog.</summary>
    public void Begin(uint code) {
        startTick = host.Now; attempt = 0; maxAttempts = options[ReconnectionOptions.MaxAttempts];
        postParam = unchecked((int) code); session = host.SocketSequence;
        host.PostAttempt(postParam);
        host.ShowProgress(1, maxAttempts);
        host.Notice("[DEBUG] * PreReconnect");
    }

    /// <summary>AttemptReconnect (@0x140aad450): counts the attempt and connects to the saved address, unless the delay from DisconnectAndDelay has not passed.</summary>
    public bool TryConnect() {
        uint now = host.Now;
        attempt++; lastAttemptTick = now;
        host.Notice("[DEBUG] * Reconnecting...");
        host.ShowProgress(attempt, maxAttempts);
        if (deadline != 0 && deadline > now) return false;
        return host.Connect(savedHost, port);
    }

    /// <summary>OnReconnectResult (@0x140aad5c0): a good result clears the counters; the dialog is hidden either way.</summary>
    public void OnResult(bool ok) {
        host.HideProgress();
        if (ok) {
            attempt = maxAttempts = postParam = session = lastError = 0; pending = false; startTick = lastAttemptTick = 0;
            host.Notice("[DEBUG] * PostReconnect. succeed");
        } else host.Notice("[DEBUG] !!! PostReconnect. failed");
    }

    /// <summary>OnReconnectFailed (@0x140aad6e0): a fatal code or a timeout ends it (dialog off, connection closed, message 0x465); otherwise another attempt waits for its delay.</summary>
    public void OnFailed(int error) {
        host.Notice("[DEBUG] !! ReconnectFailed");
        lastError = error;
        if (IsFatalError(error) || IsTimedOut()) {
            host.HideProgress();
            if (host.IsConnected) host.Close();
            host.PostGaveUp(postParam);
        } else pending = true;
    }

    /// <summary>SendReconnectRequest (@0x140aad7f0), the answer to RequestKey while reconnecting.</summary>
    public void SendRequest() => host.SendReconnectRequest(new ReconnectRequest(account, secondId, attempt, session, machineId, context));

    public void Disable() => disabled = true;

    /// <summary>DisconnectAndDelayReconnect (@0x140aad9b0): the next attempt waits the seconds; the connection closes and 0x465 is posted with the socket-closed code.</summary>
    public void DisconnectAndDelay(int seconds) {
        deadline = unchecked(host.Now + (uint) (seconds * 1000));
        host.Close();
        host.PostGaveUp(ErrorSocketClosed);
    }

    /// <summary>PostPendingReconnect (@0x140aad140): after a failed attempt, once the retry delay has passed, posts the attempt message again.</summary>
    void PostPending() {
        if (!pending) return;
        uint at = unchecked((uint) options[ReconnectionOptions.RetryDelaySeconds] * 1000 + lastAttemptTick);
        if (at < host.Now) { host.PostAttempt(postParam); pending = false; }
    }

    /// <summary>Tick (@0x140aadd70): once a second; a reconnect that has run out of time fails with 0xc9, otherwise a waiting attempt is posted.</summary>
    public void Tick() {
        uint now = host.Now;
        if (lastTick != 0 && unchecked(lastTick + 1000) > now) return;
        lastTick = now;
        if (IsReconnecting && IsTimedOut()) OnFailed(ErrorTimedOut);
        else PostPending();
    }

    /// <summary>OnReconnectPacket (@0x140aada20).</summary>
    public void OnPacket(ReconnectNotice n) {
        switch (n.Kind) {
            case ReconnectNotice.KindContext:
                context = n.Context; savedHost = host.SocketHost; port = host.SocketPort;
                break;
            case ReconnectNotice.KindResult:
                if (n.Result == 1) { if (host.InGame) OnResult(true); else OnFailed(ErrorNotInGame); }
                else OnFailed(n.Result);
                break;
            case ReconnectNotice.KindEnded:
                Reset();
                if (host.InGame) host.EndSession();
                break;
        }
    }
}
