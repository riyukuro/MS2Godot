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

/// <summary>Outbound 0x17 (SendOp::RequestItemInventory) mode 8: UIInventoryDialog::SendRequestItemInventory 0x14199c230 (uiinventorydialog.cpp:1598): u8 8, u8 tab, bool stack.
/// The sort request ("send the sort request", uiinventorydialog.h:87): sent by OnClickSort (:856) and OnSortConfirm (:1391), not on a tab switch
/// (OnTabChanged 0x14199a4b0 sends nothing).</summary>
public sealed record ItemInventorySortRequest(byte Tab, bool Stack) : IOutPacket {
    public Outbound Op => Outbound.RequestItemInventory;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(8); w.WriteByte(Tab); w.WriteBool(Stack); }
}

/// <summary>Outbound 0x17 (SendOp::RequestItemInventory) mode 3: InvenSlot::vfunc_18 (invenslot.cpp:1141), an inventory item dropped on
/// another inventory slot: u8 3, i64 the dragged item's uid, i16 the target slot's index (m_nIndex). The client checks nothing first and
/// moves nothing itself: the source slot is pushed again as it is and the server's item packets move the item.</summary>
public sealed record ItemInventoryMoveRequest(long Uid, short Slot) : IOutPacket {
    public Outbound Op => Outbound.RequestItemInventory;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(3); w.WriteLong(Uid); w.WriteShort(Slot); }
}

/// <summary>Outbound 0x16 (SendOp::ItemEquip) mode 0: Unk_Inventory::RequestEquipItem (inventory.cpp:1238), u8 0 then the payload as
/// FUN_1401fb760 writes it: i64 the item's uid, u8 the equip slot, u8 the layer mode (FUN_1401fb870: 0 / 1, and 2 only with feature 142,
/// else 1; any other value 0). The client equips nothing itself: the server's equip packets do.</summary>
public sealed record ItemEquipRequest(long Uid, byte Slot, byte Mode) : IOutPacket {
    public Outbound Op => Outbound.ItemEquip;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(0); w.WriteLong(Uid); w.WriteByte(Slot); w.WriteByte(Mode); }
}

/// <summary>Outbound 0x16 (SendOp::ItemEquip) mode 1: Unk_Inventory::RequestUnequipItem (inventory.cpp:1293), u8 1 then FUN_1401fb910's
/// i64 the worn item's uid.</summary>
public sealed record ItemUnequipRequest(long Uid) : IOutPacket {
    public Outbound Op => Outbound.ItemEquip;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(1); w.WriteLong(Uid); }
}

/// <summary>Outbound 0x16 (SendOp::ItemEquip) mode 2: EquipSlot::vfunc_18's swap (equipslot.cpp:169-177), u8 2 then FUN_1401fb9a0
/// (unit_1401f9eb0_packetpayloadserializers.cpp:609): i64 the dragged worn item's uid, u8 the target equip slot.</summary>
public sealed record ItemSwapEquipRequest(long Uid, byte Slot) : IOutPacket {
    public Outbound Op => Outbound.ItemEquip;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(2); w.WriteLong(Uid); w.WriteByte(Slot); }
}

/// <summary>Outbound 0x47 (SendOp::BadgeEquip, opcodes.h:376) mode 0: EquipGem (gemslot_2.cpp:441), u8 0 then FUN_1401fd190
/// (unit_1401f9eb0_packetpayloadserializers.cpp:987): i64 the gem item's uid, u8 the gem slot (-1, no worn gem, as 0xff).</summary>
public sealed record GemEquipRequest(long Uid, byte Slot) : IOutPacket {
    public Outbound Op => Outbound.BadgeEquip;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(0); w.WriteLong(Uid); w.WriteByte(Slot); }
}

/// <summary>Outbound 0x19 (SendOp::RequestItemUse): Unk_Inventory::RequestUseItem (inventory.cpp:1201), the payload as FUN_1401fbc40
/// writes it: i64 the item's uid, then the argument as a unicode string ("" from a plain use). The client uses up nothing itself:
/// the server's item packets do.</summary>
public sealed record ItemUseRequest(long Uid, string Argument) : IOutPacket {
    public Outbound Op => Outbound.RequestItemUse;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteLong(Uid); w.WriteUnicodeString(Argument ?? ""); }
}

/// <summary>What the game server sent from the key check up to the field enter request.</summary>
public sealed record GameEntry(ServerEnter Enter, SyncNumber Sync, RequestFieldEnter Field) {
    /// <summary>
    /// Every packet after the key's MoveResult up to RequestFieldEnter, in arrival order, but RequestKey, MoveResult, ServerEnter
    /// and RequestFieldEnter themselves. The client is in its game state from that MoveResult on (CNetworkService::OnMoveResult,
    /// networkservice.cpp:899-902, state 0x620000c after a LoginToGame) and handles each of them on arrival (DispatchPacket's
    /// managers, CSceneStateGame's handlers), so none is dropped: the server loads the adventure level, the item, quest,
    /// trophy, title, key table and other states there (GameSession.EnterServer).
    /// </summary>
    public IReadOnlyList<Ms2.Net.Wire.RawPacket> Early { get; init; } = Array.Empty<Ms2.Net.Wire.RawPacket>();
    /// <summary>How many of <see cref="Early"/> came before ServerEnter (all of them when none came).</summary>
    public int EarlyBeforeEnter { get; init; }

    /// <summary>The entry in arrival order: <paramref name="handle"/> for each of <see cref="Early"/>, with
    /// <paramref name="serverEnter"/> where ServerEnter came.</summary>
    public void Replay(Action<Ms2.Net.Wire.RawPacket> handle, Action serverEnter) {
        for (int i = 0; i < Early.Count; i++) {
            if (i == EarlyBeforeEnter) serverEnter();
            handle(Early[i]);
        }
        if (EarlyBeforeEnter >= Early.Count) serverEnter();
    }
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
    public static Task<(GameEntry entry, TcpTransport game)> EnterAsync(LoginToGame go, long accountId, byte[] machineId, string host = null, int timeoutMs = 20000, CancellationToken ct = default, bool requestGlobalFactor = false) {
        if (!go.Ok) throw new InvalidOperationException("LoginToGame error " + go.Error + " " + go.Message);
        return EnterAsync(host ?? go.Address.ToString(), go.Port, go.Token, accountId, machineId, timeoutMs, ct, requestGlobalFactor);
    }

    /// <summary>The same entry after a GameToGame (CSceneStateMove::OnUpdate @0x14174cb70 connects to the stored address, OnRequestKey
    /// answers with the stored tokens, OnMoveResult 0 goes to the game state, networkservice.cpp:899-902).</summary>
    public static Task<(GameEntry entry, TcpTransport game)> EnterAsync(GameToGame go, long accountId, byte[] machineId, string host = null, int timeoutMs = 20000, CancellationToken ct = default, bool requestGlobalFactor = false) {
        if (!go.Ok) throw new InvalidOperationException("GameToGame error " + go.Error);
        return EnterAsync(host ?? go.Address.ToString(), go.Port, go.Token, accountId, machineId, timeoutMs, ct, requestGlobalFactor);
    }

    static async Task<(GameEntry entry, TcpTransport game)> EnterAsync(string address, ushort port, ulong token, long accountId, byte[] machineId, int timeoutMs, CancellationToken ct, bool requestGlobalFactor) {
        var game = new TcpTransport();
        try {
            await game.ConnectAsync(address, port, ct);
            ServerEnter enter = null; SyncNumber sync = null; var early = new List<Ms2.Net.Wire.RawPacket>(); int beforeEnter = -1; bool keyed = false;
            while (true) {
                var p = await NextAsync(game, timeoutMs, ct);
                if (game.OpOf(p) is Inbound gop) switch (gop) {
                    case Inbound.RequestKey:
                        await game.SendAsync(new ResponseKey(accountId, token, machineId), ct); break;
                    case Inbound.MoveResult: {
                        var m = MoveResult.Read(p.Reader());
                        if (m.Error != 0) throw new MoveRefusedException(m.Error);
                        keyed = true;
                        break;
                    }
                    case Inbound.ServerEnter:
                        enter = ServerEnter.Read(p.Reader());
                        if (beforeEnter < 0) beforeEnter = early.Count;
                        if (requestGlobalFactor) await game.SendAsync(new GlobalFactorRequest(), ct);   // OnServerEnter sends it before SendServerEnter
                        break;
                    case Inbound.SyncNumber: sync = SyncNumber.Read(p.Reader()); early.Add(p); break;
                    case Inbound.RequestFieldEnter:
                        return (new GameEntry(enter, sync, RequestFieldEnter.Read(p.Reader())) {
                            Early = early, EarlyBeforeEnter = beforeEnter < 0 ? early.Count : beforeEnter,
                        }, game);
                    default: if (keyed) early.Add(p); break;
                }
                else if (keyed) early.Add(p);
            }
        } catch { game.Dispose(); throw; }
    }
}

/// <summary>The game server's MoveResult with an error after the key (OnMoveResult: the client shows GetFieldMoveErrorMessage's box).</summary>
public sealed class MoveRefusedException(byte error) : IOException("game server refused the key, move error " + error) {
    public byte Error { get; } = error;
}
