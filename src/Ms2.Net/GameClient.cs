using System.Net;
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
/// (OnTabChanged 0x14199a4b0 sends nothing).
/// GMS2: mode 10 with the wire's tab (<see cref="Gms2InventoryTab.ToWire"/>): the 6 recorded GMS2 sort requests are u8 10, u8 0,
/// bool 0, and the upstream server's ItemInventoryHandler reads Sort = 10; a mode 8 is a command it does not have.</summary>
public sealed record ItemInventorySortRequest(byte Tab, bool Stack) : IProfileOutPacket {
    public Outbound Op => Outbound.RequestItemInventory;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(Maple2.PacketLib.Tools.ByteWriter w, ProtocolProfile profile) {
        if (profile.IsGms2) { w.WriteByte(10); w.WriteByte((byte) Gms2InventoryTab.ToWire(Tab)); }
        else { w.WriteByte(8); w.WriteByte(Tab); }
        w.WriteBool(Stack);
    }
}

/// <summary>Outbound 0x17 (SendOp::RequestItemInventory) mode 4: CNetworkService::SendRequestItemInventoryMode4 @0x141538930
/// (networkservice.cpp:514): u8 4, i64 the item's uid, i32 the count. The drop of a bag item on the ground: FUN_14140c4d0
/// (invenslot.cpp:1840) for a single non-quest item, else the "drop" box's yes (UIMessageBox::HandleDropButton, uimessagebox.cpp:2008).
/// The same on GMS2 (the recorded GMS2 drop is 13 bytes; the upstream server's Drop = 4 reads i64, i32).</summary>
public sealed record ItemInventoryDropRequest(long Uid, int Count) : IOutPacket {
    public Outbound Op => Outbound.RequestItemInventory;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(4); w.WriteLong(Uid); w.WriteInt(Count); }
}

/// <summary>Outbound 0x17 (SendOp::RequestItemInventory) mode 5: CNetworkService::SendRequestItemInventoryMode5 @0x1415389b0
/// (networkservice.cpp:516): u8 5, i64 the item's uid. The "destroy_item" box's yes (UIMessageBox::HandleDestroyItemButton,
/// uimessagebox.cpp:1352) for an item that cannot go on the ground. The same on GMS2 (13 recorded, 9 bytes each).</summary>
public sealed record ItemInventoryDestroyRequest(long Uid) : IOutPacket {
    public Outbound Op => Outbound.RequestItemInventory;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(5); w.WriteLong(Uid); }
}

/// <summary>Outbound 0x17 (SendOp::RequestItemInventory) mode 9: Unk_Inventory::SendRequestItemInventory @0x140a62cc0
/// (inventory.cpp:1303): u8 9, u8 the tab. The bag expand (UIInventoryDialog::RequestExpandSlots, uiinventorydialog.cpp:1874).
/// GMS2: mode 11 with the wire's tab, as the upstream server's Expand = 11 reads it (no GMS2 expand is recorded).</summary>
public sealed record ItemInventoryExpandRequest(byte Tab) : IProfileOutPacket {
    public Outbound Op => Outbound.RequestItemInventory;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(Maple2.PacketLib.Tools.ByteWriter w, ProtocolProfile profile) {
        if (profile.IsGms2) { w.WriteByte(11); w.WriteByte((byte) Gms2InventoryTab.ToWire(Tab)); }
        else { w.WriteByte(9); w.WriteByte(Tab); }
    }
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
/// <summary>GMS2 (every one of the 160 recorded mode 0 packets; the other recorded 0x16 are 46 mode 1, none mode 2): the slot goes as its two letter name, a unicode string, in place of the slot byte: u8 0, i64 uid, string slot name (the EquipSlot name), u8 layer mode.</summary>
public sealed record ItemEquipRequest(long Uid, byte Slot, byte Mode) : IProfileOutPacket {
    public Outbound Op => Outbound.ItemEquip;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(Maple2.PacketLib.Tools.ByteWriter w, ProtocolProfile profile) {
        w.WriteByte(0); w.WriteLong(Uid);
        if (profile.IsGms2) {
            if (Slot is < 1 or > 19) throw new ArgumentOutOfRangeException(nameof(Slot), "no equip slot name for " + Slot);
            w.WriteUnicodeString(((Ms2.Net.Packets.EquipSlot) Slot).ToString());
        } else w.WriteByte(Slot);
        w.WriteByte(Mode);
    }
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

/// <summary>Outbound 0x47 (SendOp::BadgeEquip) mode 1: UnequipGem (gemslot_2.cpp:496), u8 1 then FUN_1401fd250: u8 the gem slot.</summary>
public sealed record GemUnequipRequest(byte Slot) : IOutPacket {
    public Outbound Op => Outbound.BadgeEquip;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) { w.WriteByte(1); w.WriteByte(Slot); }
}

/// <summary>Outbound 0x47 (SendOp::BadgeEquip) mode 3: the transparency gem setting (uigemsettingdialog.cpp:154), u8 3 then FUN_1401fd2e0:
/// u8 the gem type, then ten u8 hidden flags (cap, eye, clothes, pants, mantle, ear, face hat, gloves, ring, shoes: the dialog's m_cp..m_sh boxes).</summary>
public sealed record GemSettingRequest(byte Type, bool[] Hidden) : IOutPacket {
    public Outbound Op => Outbound.BadgeEquip;
    public void Write(Maple2.PacketLib.Tools.ByteWriter w) {
        w.WriteByte(3); w.WriteByte(Type);
        for (int i = 0; i < 10; i++) w.WriteByte((byte) (Hidden != null && i < Hidden.Length && Hidden[i] ? 1 : 0));
    }
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

    /// <summary>
    /// The host a move connects to: the packet's address, as the client does. OnLoginToGame @0x14153b570, OnGameToLogin @0x14153bc00
    /// and OnGameToGame @0x14153be30 store the packet's ip as text in DAT_143ab2a48 and its port in DAT_143ab2848
    /// (networkservice.cpp:912, 928-937, 951), and the next ConnectTo uses them. <paramref name="fallback"/>, the host the login
    /// was made to, is used only when the packet carries no address (0.0.0.0).
    /// </summary>
    public static string HostOf(IPAddress packet, string fallback) =>
        packet == null || packet.Equals(IPAddress.Any) ? fallback ?? IPAddress.Loopback.ToString() : packet.ToString();

    /// <summary>"ip:port" for the connect log line and the error messages.</summary>
    public static string EndpointText(string host, int port) => host.Contains(':') ? $"[{host}]:{port}" : $"{host}:{port}";

    /// <summary>Connects to the game endpoint (the packet's address, <paramref name="host"/> only when it carries 0.0.0.0: <see cref="HostOf"/>), answers RequestKey and reads up to RequestFieldEnter.
    /// The key is account id, then the token as the two i32 the client writes (WriteLoginEnter, classobject.cpp:260-266: token1 is the low half), the same 8 bytes as a little-endian u64.</summary>
    public static Task<(GameEntry entry, TcpTransport game)> EnterAsync(LoginToGame go, long accountId, byte[] machineId, string host = null, int timeoutMs = 20000, CancellationToken ct = default, bool requestGlobalFactor = false) {
        if (!go.Ok) throw new InvalidOperationException("LoginToGame error " + go.Error + " " + go.Message);
        return EnterAsync(HostOf(go.Address, host), go.Port, go.Token, accountId, machineId, timeoutMs, ct, requestGlobalFactor);
    }

    /// <summary>CSceneStateMove's connect wait: ConnectTo is non-blocking and m_timer.Start(10000) bounds it; when the timer runs out
    /// unconnected the attempt has failed (scenestatemove.cpp:79).</summary>
    public const int MoveConnectWaitMs = 10000;

    /// <summary>The same entry after a GameToGame (CSceneStateMove::OnUpdate @0x14174cb70 connects to the stored address, OnRequestKey
    /// answers with the stored tokens, OnMoveResult 0 goes to the game state, networkservice.cpp:899-902). The connect is bounded by
    /// <paramref name="connectWaitMs"/> (the move's 10 s timer); one not made by then throws a SocketException (TimedOut), a failed
    /// attempt. The connect goes to the packet's address (<see cref="HostOf"/>; <paramref name="host"/> only for 0.0.0.0).</summary>
    public static Task<(GameEntry entry, TcpTransport game)> EnterAsync(GameToGame go, long accountId, byte[] machineId, string host = null, int timeoutMs = 20000, CancellationToken ct = default, bool requestGlobalFactor = false, int connectWaitMs = MoveConnectWaitMs) {
        if (!go.Ok) throw new InvalidOperationException("GameToGame error " + go.Error);
        return EnterAsync(HostOf(go.Address, host), go.Port, go.Token, accountId, machineId, timeoutMs, ct, requestGlobalFactor, connectWaitMs);
    }

    static async Task<(GameEntry entry, TcpTransport game)> EnterAsync(string address, ushort port, ulong token, long accountId, byte[] machineId, int timeoutMs, CancellationToken ct, bool requestGlobalFactor, int connectWaitMs = Timeout.Infinite) {
        var game = new TcpTransport();
        try {
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                if (connectWaitMs != Timeout.Infinite) wait.CancelAfter(connectWaitMs);
                try { await game.ConnectAsync(address, port, wait.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                    throw new System.Net.Sockets.SocketException((int) System.Net.Sockets.SocketError.TimedOut, "game connect timed out (" + EndpointText(address, port) + ")");
                }
            }
            ServerEnter enter = null; SyncNumber sync = null; var early = new List<Ms2.Net.Wire.RawPacket>(); int beforeEnter = -1; bool keyed = false;
            while (true) {
                RawPacket p;
                try { p = await NextAsync(game, timeoutMs, ct); }
                catch (TimeoutException) { throw new TimeoutException("game entry timed out (" + EndpointText(address, port) + (game.Channel?.Ready == true ? ")" : ", no hello)")); }
                catch (IOException e) when (e is not MoveRefusedException) { throw new IOException("game connection closed (" + EndpointText(address, port) + ")", e.InnerException ?? e); }
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
                        enter = ServerEnter.Read(p.Reader(), game.Profile);
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
