using Ms2.Net.Packets;
using Ms2.Net.Wire;

namespace Ms2.Net;

/// <summary>Outbound 0xB0 (CNetworkService::SendServerEnter, networkservice.cpp:594): one i32, the client language. Only the login server reads it (it answers with the lists again); the game flow does not need it.</summary>
public sealed record ServerEnterRequest(int Language = 0) : IOutPacket {
    public Outbound Op => Outbound.ServerEnter;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) => w.WriteInt(Language);
}

/// <summary>Outbound 0x82 (SendOp::GlobalFactor, no payload): CSceneStateGame::OnServerEnter (CSceneStateGame::OnServerEnter, scenestategame.cpp:2144, 0x14170dd60) asks for the rate tables while DAT_143ab2918 is not set. A read request.</summary>
public sealed record GlobalFactorRequest : IOutPacket {
    public Outbound Op => Outbound.GlobalFactor;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { }
}

/// <summary>What the game server sent from the key check up to the field enter request.</summary>
public sealed record GameEntry(ServerEnter Enter, SyncNumber Sync, RequestFieldEnter Field) {
    /// <summary>Quest packets (0x52) that arrive before RequestFieldEnter: the server loads the quest states there (QuestManager.Load, after SyncNumber), in order.</summary>
    public IReadOnlyList<Ms2.Net.Wire.RawPacket> EarlyQuests { get; init; } = Array.Empty<Ms2.Net.Wire.RawPacket>();
    /// <summary>The ItemInventory packets that arrived before the field enter, in order.</summary>
    public IReadOnlyList<Ms2.Net.Wire.RawPacket> EarlyItems { get; init; } = Array.Empty<Ms2.Net.Wire.RawPacket>();
    /// <summary>The GlobalFactor (0xD6) answer when it arrived before RequestFieldEnter, else null.</summary>
    public Ms2.Net.Wire.RawPacket? EarlyFactor { get; init; }
}

/// <summary>
/// Select and migration: CharacterManagement select on the login connection, LoginToGame, a new connection to the game endpoint,
/// RequestKey answered with ResponseKey, then packets are read until RequestFieldEnter. Plain data out, no Godot types.
/// </summary>
public sealed class GameClient {
    /// <summary>Select command byte: UIServerListDialog writes 0 then the character id and channel (uiserverlistdialog.cpp:638-640, Unk_SlEnterRequest i64 + channel); the server's Command.Select is 0 (CharacterManagementHandler.cs:35). The Encode1(2) in uicharacterlistdialog.cpp:1529 is the delete request (server Delete = 2).</summary>
    public const byte SelectCommand = 0;

    static async Task<RawPacket> NextAsync(TcpTransport t, int timeoutMs, CancellationToken ct) {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true) {
            ct.ThrowIfCancellationRequested();
            if (t.Incoming.TryDequeue(out var p)) return p;
            if (t.IsClosed) throw new IOException("connection closed", t.Failure);
            if (Environment.TickCount64 > deadline) throw new TimeoutException("no packet");
            await Task.Delay(5, ct);
        }
    }

    /// <summary>On the logged-in login connection: select a character and return the game endpoint. The login connection is left to the caller (the server closes it).</summary>
    public static async Task<LoginToGame> SelectAsync(TcpTransport login, long characterId, short channel = 0, int timeoutMs = 15000, CancellationToken ct = default) {
        await login.SendAsync(new SelectCharacter(characterId, channel), ct);
        while (true) {
            var p = await NextAsync(login, timeoutMs, ct);
            if (login.Is(p, Inbound.LoginToGame)) return LoginToGame.Read(p.Reader());
            if (login.Is(p, Inbound.MoveResult)) throw new InvalidDataException("select refused, move error " + MoveResult.Read(p.Reader()).Error);
        }
    }

    /// <summary>Connects to the game endpoint (host overrides the address in the packet when given), answers RequestKey and reads up to RequestFieldEnter.
    /// The key is account id, then the token as the two i32 the client writes (WriteLoginEnter, classobject.cpp:260-266: token1 is the low half), the same 8 bytes as a little-endian u64.</summary>
    public static async Task<(GameEntry entry, TcpTransport game)> EnterAsync(LoginToGame go, long accountId, byte[] machineId, string host = null, int timeoutMs = 20000, CancellationToken ct = default, bool requestGlobalFactor = false) {
        if (!go.Ok) throw new InvalidOperationException("LoginToGame error " + go.Error + " " + go.Message);
        var game = new TcpTransport();
        try {
            await game.ConnectAsync(host ?? go.Address.ToString(), go.Port, ct);
            ServerEnter enter = null; SyncNumber sync = null; var earlyQuests = new List<Ms2.Net.Wire.RawPacket>(); var earlyItems = new List<Ms2.Net.Wire.RawPacket>(); Ms2.Net.Wire.RawPacket? earlyFactor = null;
            while (true) {
                var p = await NextAsync(game, timeoutMs, ct);
                if (game.OpOf(p) is Inbound gop) switch (gop) {
                    case Inbound.RequestKey:
                        await game.SendAsync(new ResponseKey(accountId, go.Token, machineId), ct); break;
                    case Inbound.MoveResult: {
                        var m = MoveResult.Read(p.Reader());
                        if (m.Error != 0) throw new InvalidDataException("game server refused the key, move error " + m.Error);
                        break;
                    }
                    case Inbound.ServerEnter:
                        enter = ServerEnter.Read(p.Reader());
                        if (requestGlobalFactor) await game.SendAsync(new GlobalFactorRequest(), ct);   // OnServerEnter sends it before SendServerEnter
                        break;
                    case Inbound.GlobalFactor: earlyFactor = p; break;
                    case Inbound.SyncNumber: sync = SyncNumber.Read(p.Reader()); break;
                    case Inbound.Quest: earlyQuests.Add(p); break;
                    case Inbound.ItemInventory: earlyItems.Add(p); break;
                    case Inbound.RequestFieldEnter:
                        return (new GameEntry(enter, sync, RequestFieldEnter.Read(p.Reader())) { EarlyQuests = earlyQuests, EarlyItems = earlyItems, EarlyFactor = earlyFactor }, game);
                }
            }
        } catch { game.Dispose(); throw; }
    }
}
