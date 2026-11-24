using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Net;

/// <summary>What the login server gave back: the LoginResult, and the character list when the login was accepted.</summary>
public sealed record LoginOutcome(LoginResult Result, IReadOnlyList<CharacterEntry> Characters) {
    public bool Success => Result.Success;
    /// <summary>CharMaxCount 0xB8 (CNetworkService::OnCharMaxCount, networkservice.cpp:1069): DAT_1439f4a84, the account's character limit, and DAT_1439f4a88, the list's row count. 0 when the server sent none.</summary>
    public int CharacterLimit { get; init; }
    public int CharacterRows { get; init; }
    /// <summary>Ugc mode 0x11 (CUgcManager::OnUgc), the web server's addresses the login server sends after the login result; null when none came.</summary>
    public UgcEndpoint Endpoint { get; init; }
    /// <summary>The list's EndList flag (OnCharacterListPacket mode 4, uicharacterlistdialog.cpp:906): m_1e8, the game start's
    /// rename check is on.</summary>
    public bool CheckNames { get; init; }
}

/// <summary>The login server's answer to a rename on character select: CharacterList 7 (<see cref="NameChanged"/>, its
/// code in Error), or the list sent again (<see cref="List"/>, with the EndList flag).</summary>
public sealed record RenameAnswer(CharacterListMessage NameChanged, LoginOutcome List);

/// <summary>
/// Login server exchange up to the character list: hello and ResponseVersion (the session does those), RequestLogin 0x09, ResponseLogin,
/// LoginResult 0x0A, then CharacterList 0x0C (StartList, List/AppendEntry, EndList). The caller owns the transport; no Godot types.
/// </summary>
public sealed class LoginClient {
    /// <summary>ResponseLogin type byte for the character list: UIServerListDialog sends 2 (uiserverlistdialog.cpp:587), the server reads 2 as CharacterList (LoginHandler.cs:22-25). UILoginDialog sends 1 (the server list, then the server drops the socket).</summary>
    public const byte CharacterListType = 2;

    // ResponseLogin as the client builds it (uiserverlistdialog.cpp:586-598, WriteLoginA classobject.cpp:112-121): u8 type 2; unicode id (+ region code when m_bLocaledName, :596-597);
    // unicode password; i16 login type = the selected channel m_nChannel (:591; 0 before a pick); the 16 byte machine id (CMachineIDManager::GetID, :592); i32 machine value
    // (CMachineIDManager::GetValue = m_n18, machineidmanager.cpp:58-61); i32 DAT_143ab2858 (:593, filled by the server's bypass key packet, networkservice.cpp:892, else 0);
    // the language code string (FUN_140652030(GetLanguage()), :595; the code depends on the client language setting and is not resolved here).
    // The server reads only up to the machine id (LoginHandler.cs:36-40), so the trailing fields do not change the outcome.
    /// <summary>Sends a create request on the logged-in connection and waits for the answer: the new entry (CharacterList AppendEntry) or the refusal (0x46).
    /// Throws <see cref="CharacterCreateRefused"/> for a refusal.</summary>
    public async Task<CharacterEntry> CreateAsync(CreateCharacter request, int timeoutMs = 15000, CancellationToken ct = default) {
        await transport.SendAsync(request, ct);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (!transport.Incoming.TryDequeue(out var p)) {
                if (transport.IsClosed) throw new IOException("login connection closed", transport.Failure);
                if (Environment.TickCount64 > deadline) throw new TimeoutException("character create timed out");
                await Task.Delay(5, ct);
                continue;
            }
            if (transport.Is(p, Inbound.CharacterCreate)) { var a = CharacterCreateAnswer.Read(p.Reader()); throw new CharacterCreateRefused(a.Kind, a.Message); }
            if (transport.Is(p, Inbound.CharacterList)) {
                var m = CharacterListMessage.Read(p.Reader(), "KR", transport.Profile);
                if (m.Command == CharacterListCommand.AppendEntry && m.Entries.Count > 0) return m.Entries[0];
            }
        }
    }

    /// <summary>Sends BeginDelete/CancelDelete/ConfirmDelete for a character and waits for the matching CharacterList reply (DeleteEntry, BeginDelete or CancelDelete for that id).
    /// The reply is returned as read; the caller applies it (DeleteEntry with Error 0 removes the entry).</summary>
    public async Task<CharacterListMessage> ManageAsync(CharacterIdCommand command, long characterId, int timeoutMs = 15000, CancellationToken ct = default) {
        await transport.SendAsync(new ManageCharacter(command, characterId), ct);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (!transport.Incoming.TryDequeue(out var p)) {
                if (transport.IsClosed) throw new IOException("login connection closed", transport.Failure);
                if (Environment.TickCount64 > deadline) throw new TimeoutException("character delete timed out");
                await Task.Delay(5, ct);
                continue;
            }
            if (!transport.Is(p, Inbound.CharacterList)) continue;
            var m = CharacterListMessage.Read(p.Reader(), "KR", transport.Profile);
            if (m.Command is CharacterListCommand.DeleteEntry or CharacterListCommand.BeginDelete or CharacterListCommand.CancelDelete && m.CharacterId == characterId) return m;
        }
    }

    /// <summary>
    /// SendCharacterManagementMode5 (uicharacterlistdialog.cpp:1712-1721): the forced rename of character
    /// <paramref name="characterId"/> to <paramref name="name"/>, and the answer: CharacterList 7 with its code
    /// (UICharacterListDialog::vfunc_79 case 0xc, :605-633), or the list again (StartList, the entries, EndList), which a
    /// server sends when it took the name.
    /// </summary>
    public async Task<RenameAnswer> RenameAsync(long characterId, string name, int timeoutMs = 15000, CancellationToken ct = default) {
        await transport.SendAsync(new RenameCharacter(characterId, name), ct);
        var deadline = Environment.TickCount64 + timeoutMs;
        var list = new List<CharacterEntry>();
        bool listing = false;
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (!transport.Incoming.TryDequeue(out var p)) {
                if (transport.IsClosed) throw new IOException("login connection closed", transport.Failure);
                if (Environment.TickCount64 > deadline) throw new TimeoutException("character rename timed out");
                await Task.Delay(5, ct);
                continue;
            }
            if (!transport.Is(p, Inbound.CharacterList)) continue;
            var m = CharacterListMessage.Read(p.Reader(), "KR", transport.Profile);
            switch (m.Command) {
                case CharacterListCommand.NameChanged: return new RenameAnswer(m, null);
                case CharacterListCommand.StartList: list.Clear(); listing = true; break;
                case CharacterListCommand.List or CharacterListCommand.AppendEntry: if (listing) list.AddRange(m.Entries); break;
                case CharacterListCommand.EndList when listing:
                    return new RenameAnswer(null, new LoginOutcome(new LoginResult(0, 0, "", 0, 0, 0, 0, 0, null), list) { CheckNames = m.CheckNames });
            }
        }
    }

    readonly TcpTransport transport;
    /// <summary>GMS2: the launcher ticket (--ticket) sent in the login token field; null keeps the upstream user/password form.</summary>
    public string Ticket { get; init; }
    /// <summary>GMS2 with a ticket: the file that keeps the machine id; read after the connect (the profile is known then), never touched for KMS.</summary>
    public string MachineIdPath { get; init; }
    public LoginClient(TcpTransport transport) { this.transport = transport; }

    /// <summary>Connects (when not yet connected) and logs in. <paramref name="user"/> is sent as given: the real client appends the region code ("KR") itself.</summary>
    public async Task<LoginOutcome> LoginAsync(string host, int port, string user, string password, byte[] machineId = null, CancellationToken ct = default) {
        if (transport.Session == null) await transport.ConnectAsync(host, port, ct);
        machineId ??= new byte[16];
        if (Ticket != null && MachineIdPath != null && transport.Profile.IsGms2 && machineId.Length == 16) MachineIdFile.LoadOrCreate(MachineIdPath, machineId);
        return await RunAsync(user, password, machineId, ct);
    }

    /// <summary>
    /// The login packet. KMS (or GMS2 without a ticket): the upstream/KMS form, language "en", login type 0. GMS2 with a ticket: the recorded form, where the login
    /// type follows the command as in all 14 recorded logins (command 2 carries 1, command 1 carries 0), the language is "EN", the local IPv4 is the connected
    /// socket's, the nonce is random per login and the trailer is 2 (the newer recorded series).
    /// </summary>
    ResponseLogin BuildLogin(string user, string password, byte[] machineId) {
        if (Ticket == null || !transport.Profile.IsGms2) return new ResponseLogin(CharacterListType, user, password, 0, machineId, 0, 0, "en");
        return new ResponseLogin(CharacterListType, user, password, (short) (CharacterListType == 2 ? 1 : 0), machineId, 0, 0, "EN") {
            Ticket = Ticket, AccountId = 0, LocalIp = transport.LocalIPv4, Nonce = System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue), Trailer = 2,
        };
    }

    public async Task<LoginOutcome> RunAsync(string user, string password, byte[] machineId, CancellationToken ct = default, int timeoutMs = 15000) {
        LoginResult result = null;
        int limit = 0, rows = 0;
        UgcEndpoint endpoint = null;
        var list = new List<CharacterEntry>();
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (!transport.Incoming.TryDequeue(out var p)) {
                if (transport.IsClosed) throw new IOException("login connection closed", transport.Failure);
                if (Environment.TickCount64 > deadline) throw new TimeoutException("login timed out");
                await Task.Delay(5, ct);
                continue;
            }
            if (transport.OpOf(p) is Inbound lop) switch (lop) {
                case Inbound.RequestLogin:
                    await transport.SendAsync(BuildLogin(user, password, machineId), ct);
                    break;
                case Inbound.LoginResult:
                    result = LoginResult.Read(p.Reader());
                    if (!result.Success) return new LoginOutcome(result, list);
                    break;
                case Inbound.Ugc:
                    if (UgcMessage.Read(p.Reader()) is UgcEndpoint e) endpoint = e;
                    break;
                case Inbound.CharMaxCount:
                    var cr = p.Reader(); limit = cr.ReadInt(); rows = cr.ReadInt();
                    break;
                case Inbound.CharacterList:
                    var m = CharacterListMessage.Read(p.Reader(), "KR", transport.Profile);
                    if (m.Command == CharacterListCommand.StartList) list.Clear();
                    else if (m.Command is CharacterListCommand.List or CharacterListCommand.AppendEntry) list.AddRange(m.Entries);
                    else if (m.Command == CharacterListCommand.EndList) return new LoginOutcome(result ?? throw new InvalidDataException("CharacterList before LoginResult"), list) { CharacterLimit = limit, CharacterRows = rows, Endpoint = endpoint, CheckNames = m.CheckNames };
                    break;
            }
        }
    }
    /// <summary>
    /// The return to the login server after GameToLogin (CNetworkService::OnGameToLogin, DAT_1439f4a78 = 1): the connect, the
    /// key check with the GameToLogin tokens (OnRequestKey), OnMoveResult 0 for state 1 (character select, 0x620000d) which
    /// sends SendServerEnter (networkservice.cpp:899-901); the login server then lists the characters again (the same
    /// CharacterList messages as the login). A MoveResult error throws <see cref="MoveRefusedException"/>. <paramref name="onMoveResult"/>
    /// runs at the good MoveResult, the client's early scene switch.
    /// </summary>
    public async Task<LoginOutcome> ReturnAsync(string host, ushort port, ulong token, long accountId, byte[] machineId, CancellationToken ct = default, int timeoutMs = 15000, int connectTimeoutMs = 10000, Action onMoveResult = null) {
        if (transport.Session == null) {
            // the client's ConnectTo bounds the connect at 10 s (CSceneStateGame's reconnect timer)
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connect.CancelAfter(connectTimeoutMs);
            try { await transport.ConnectAsync(host, port, connect.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("login connect timed out (" + GameClient.EndpointText(host, port) + ")"); }
        }
        var list = new List<CharacterEntry>();
        int limit = 0, rows = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (!transport.Incoming.TryDequeue(out var p)) {
                if (transport.IsClosed) throw new IOException("login connection closed (" + GameClient.EndpointText(host, port) + ")", transport.Failure);
                if (Environment.TickCount64 > deadline) throw new TimeoutException("login return timed out (" + GameClient.EndpointText(host, port) + (transport.Channel?.Ready == true ? ")" : ", no hello)"));
                await Task.Delay(5, ct);
                continue;
            }
            if (transport.OpOf(p) is Inbound lop) switch (lop) {
                // CNetworkService::OnRequestKey @0x14153b010 (networkservice.cpp:859-866) and, from the game scene state the return
                // runs in, CSceneStateGame::OnRequestLogin @0x14170dc90 (scenestategame.cpp:2054-2064): both send ResponseKey with the
                // account, the GameToLogin tokens and the machine id. A login server that asks with RequestLogin after the version
                // check gets the key the same way.
                case Inbound.RequestKey:
                case Inbound.RequestLogin:
                    await transport.SendAsync(new ResponseKey(accountId, token, machineId ?? new byte[16]), ct);
                    break;
                case Inbound.MoveResult: {
                    var m = MoveResult.Read(p.Reader());
                    if (m.Error != 0) throw new MoveRefusedException(m.Error);
                    onMoveResult?.Invoke();   // OnMoveResult 0 switches to the character select scene (FUN_1417e25a0(0x620000d)) before ServerEnter and the list
                    await transport.SendAsync(new ServerEnterRequest(), ct);
                    break;
                }
                case Inbound.CharMaxCount:
                    var cr = p.Reader(); limit = cr.ReadInt(); rows = cr.ReadInt();
                    break;
                case Inbound.CharacterList:
                    var cl = CharacterListMessage.Read(p.Reader(), "KR", transport.Profile);
                    if (cl.Command == CharacterListCommand.StartList) list.Clear();
                    else if (cl.Command is CharacterListCommand.List or CharacterListCommand.AppendEntry) list.AddRange(cl.Entries);
                    else if (cl.Command == CharacterListCommand.EndList) return new LoginOutcome(new LoginResult(0, 0, "", 0, 0, 0, 0, 0, null), list) { CharacterLimit = limit, CharacterRows = rows, CheckNames = cl.CheckNames };
                    break;
            }
        }
    }
    /// <summary>
    /// CSceneStateGame::OnUpdate (@0x14170c0c0, scenestategame.cpp:1327-1357) for the GameToLogin target: a 10 s timer, and each time it runs out
    /// with no connection ConnectTo is called again, six times (the loop runs while the counter is at most 5, as GameToGame's); after the sixth timer the client gives up. Each attempt here is
    /// a new transport through <see cref="ReturnAsync"/>; one that fails for a reason other than a refused key (MoveRefusedException) waits out
    /// what is left of <paramref name="waitMs"/>, then the next starts. All six failed: <see cref="ReconnectGaveUpException"/>.
    /// </summary>
    public static async Task<(LoginOutcome outcome, TcpTransport transport)> ReturnWithRetriesAsync(string host, ushort port, ulong token, long accountId, byte[] machineId,
        Action onMoveResult = null, int attempts = 6, int waitMs = 10000, CancellationToken ct = default, Action<int> onAttempt = null) {
        for (int attempt = 1; attempt <= attempts; attempt++) {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var t = new TcpTransport();
            onAttempt?.Invoke(attempt);
            try {
                var o = await new LoginClient(t).ReturnAsync(host, port, token, accountId, machineId, ct, onMoveResult: onMoveResult, connectTimeoutMs: waitMs);
                return (o, t);
            } catch (MoveRefusedException) { t.Dispose(); throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { t.Dispose(); throw; }
            catch (Exception) when (t.Session != null && t.Session.Channel.Ready) { t.Dispose(); throw; }
            catch (Exception) when (t.Session == null || !t.Session.Channel.Ready) {   // the client retries only while !IsConnected() (scenestategame.cpp:1336)
                t.Dispose();
                int left = waitMs - (int) clock.ElapsedMilliseconds;
                if (left > 0) await Task.Delay(left, ct);
            }
        }
        throw new ReconnectGaveUpException(attempts);
    }
}

public sealed class CharacterCreateRefused : Exception {
    public byte Kind { get; }
    public CharacterCreateRefused(byte kind, string message) : base($"character create refused, kind {kind} {message}") { Kind = kind; }

}

/// <summary>The client's give-up after the last reconnect timer: BlockFormat(0x20f, 7) in a box, then TerminateProcess(2) (scenestategame.cpp:1348-1357).</summary>
public sealed class ReconnectGaveUpException(int attempts) : Exception($"no connection after {attempts} attempt(s)") {
    public int Attempts { get; } = attempts;
}
