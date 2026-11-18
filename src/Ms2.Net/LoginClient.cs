using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Net;

/// <summary>What the login server gave back: the LoginResult, and the character list when the login was accepted.</summary>
public sealed record LoginOutcome(LoginResult Result, IReadOnlyList<CharacterEntry> Characters) {
    public bool Success => Result.Success;
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
                var m = CharacterListMessage.Read(p.Reader());
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
            var m = CharacterListMessage.Read(p.Reader());
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
                case Inbound.CharacterList:
                    var m = CharacterListMessage.Read(p.Reader());
                    if (m.Command == CharacterListCommand.StartList) list.Clear();
                    else if (m.Command is CharacterListCommand.List or CharacterListCommand.AppendEntry) list.AddRange(m.Entries);
                    else if (m.Command == CharacterListCommand.EndList) return new LoginOutcome(result ?? throw new InvalidDataException("CharacterList before LoginResult"), list);
                    break;
            }
        }
    }
}

public sealed class CharacterCreateRefused : Exception {
    public byte Kind { get; }
    public CharacterCreateRefused(byte kind, string message) : base($"character create refused, kind {kind} {message}") { Kind = kind; }
}
