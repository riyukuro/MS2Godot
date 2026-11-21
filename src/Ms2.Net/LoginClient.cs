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
}

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

    readonly TcpTransport transport;
    public LoginClient(TcpTransport transport) { this.transport = transport; }

    /// <summary>Connects (when not yet connected) and logs in. <paramref name="user"/> is sent as given: the real client appends the region code ("KR") itself.</summary>
    public async Task<LoginOutcome> LoginAsync(string host, int port, string user, string password, byte[] machineId = null, CancellationToken ct = default) {
        if (transport.Session == null) await transport.ConnectAsync(host, port, ct);
        return await RunAsync(user, password, machineId ?? new byte[16], ct);
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
                    await transport.SendAsync(new ResponseLogin(CharacterListType, user, password, 0, machineId, 0, 0, "en"), ct);
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
                    else if (m.Command == CharacterListCommand.EndList) return new LoginOutcome(result ?? throw new InvalidDataException("CharacterList before LoginResult"), list) { CharacterLimit = limit, CharacterRows = rows, Endpoint = endpoint };
                    break;
            }
        }
    }
    /// <summary>
    /// The return to the login server after GameToLogin (CNetworkService::OnGameToLogin, DAT_1439f4a78 = 1): the connect, the
    /// key check with the GameToLogin tokens (OnRequestKey), OnMoveResult 0 for state 1 (character select, 0x620000d) which
    /// sends SendServerEnter (networkservice.cpp:899-901); the login server then lists the characters again (the same
    /// CharacterList messages as the login). A MoveResult error throws <see cref="MoveRefusedException"/>.
    /// </summary>
    public async Task<LoginOutcome> ReturnAsync(string host, ushort port, ulong token, long accountId, byte[] machineId, CancellationToken ct = default, int timeoutMs = 15000, int connectTimeoutMs = 10000) {
        if (transport.Session == null) {
            // the client's ConnectTo bounds the connect at 10 s (CSceneStateGame's reconnect timer)
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connect.CancelAfter(connectTimeoutMs);
            try { await transport.ConnectAsync(host, port, connect.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("login connect timed out"); }
        }
        var list = new List<CharacterEntry>();
        int limit = 0, rows = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (!transport.Incoming.TryDequeue(out var p)) {
                if (transport.IsClosed) throw new IOException("login connection closed", transport.Failure);
                if (Environment.TickCount64 > deadline) throw new TimeoutException("login return timed out");
                await Task.Delay(5, ct);
                continue;
            }
            if (transport.OpOf(p) is Inbound lop) switch (lop) {
                case Inbound.RequestKey:
                    await transport.SendAsync(new ResponseKey(accountId, token, machineId ?? new byte[16]), ct);
                    break;
                case Inbound.MoveResult: {
                    var m = MoveResult.Read(p.Reader());
                    if (m.Error != 0) throw new MoveRefusedException(m.Error);
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
                    else if (cl.Command == CharacterListCommand.EndList) return new LoginOutcome(new LoginResult(0, 0, "", 0, 0, 0, 0, 0, null), list) { CharacterLimit = limit, CharacterRows = rows };
                    break;
            }
        }
    }
}

public sealed class CharacterCreateRefused : Exception {
    public byte Kind { get; }
    public CharacterCreateRefused(byte kind, string message) : base($"character create refused, kind {kind} {message}") { Kind = kind; }
}
