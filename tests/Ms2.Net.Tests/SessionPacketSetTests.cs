using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The client's session packet list (33 server to client, 21 client to server ops) with their KMS2 2525 and GMS2 12 numbers, and
/// the layouts of the session packets read here.
/// </summary>
public class SessionPacketSetTests
{
    // ApplyTimeSync sets the process-wide CMsTime zone; each test puts it back as it found it, so the order of tests does not matter.
    MsTime.State savedZone;
    [SetUp] public void SaveZone() => savedZone = MsTime.Save();
    [TearDown] public void RestoreZone() => MsTime.Restore(savedZone);

    public sealed record InboundRow(Inbound Op, ushort KmsOpcode, ushort? Gms2Opcode, string ClientCite, string Modes);
    public sealed record OutboundRow(Outbound Op, ushort KmsOpcode, ushort? Gms2Opcode, string ClientCite, string Modes);

    /// <summary>The session packets the server sends (RecvOp, 33 ops).</summary>
    public static readonly InboundRow[] InboundInventory =
    {
        new(Inbound.RequestVersion,        0x01,  0x01,  "networkservice.cpp:835 @0x14153ad10",    "patch 0/1/2 + mismatch"),
        new(Inbound.VersionResult,         0x02,  0x02,  "networkservice.cpp:854 @0x14153af70",    "empty"),
        new(Inbound.DataPatch,             0x03,  null,  "onlinepatchmanagercli.cpp:110 @0x140a84960", "modes 1,2,3,5"),
        new(Inbound.RequestKey,            0x04,  0x04,  "networkservice.cpp:859 @0x14153b010",    "reconnect / ResponseKey / RelocateWorld"),
        new(Inbound.Reconnect,             0x05,  0x05,  "reconnectionmanagercli.cpp:291 @0x140aada20", "modes 0,2,3"),
        new(Inbound.Ping,                  0x06,  0x06,  "networkservice.cpp:876 @0x14153b220",    "i32 acked sequence"),
        new(Inbound.ReconnectUnk,          0x07,  0x07,  "gamesocket.cpp:320, networkservice.cpp:882 @0x14153b2d0", "i32 server sequence"),
        new(Inbound.FinalizeReconnect,     0x08,  0x08,  "gamesocket.cpp:321, networkservice.cpp:884 @0x14153b310", "i32 server seq + i32 retransmit seq"),
        new(Inbound.RequestLogin,          0x09,  0x09,  "uilogindialog.cpp:204 @0x1419e1160, uiserverlistdialog.cpp:578 @0x141ad28f0, scenestategame.cpp:2054 @0x14170dc90", "modes 1/2/return"),
        new(Inbound.LoginResult,           0x0A,  0x0A,  "networkservice.cpp:991 @0x14153c670, uiserverlistdialog.cpp:604 @0x141ad2b50", "state 0 + refusal states"),
        new(Inbound.ServerList,            0x0B,  0x0B,  "uiserverlistdialog.cpp:310 @0x141ad1150, scenestatelogin.cpp:117 @0x14174c200", "KMS count 0/>0, GMS2 cmd 0/1"),
        new(Inbound.CharacterList,         0x0C,  0x0C,  "uicharacterlistdialog.cpp:605 @0x1418ea860, :894 @0x1418ed880", "modes 0..7"),
        new(Inbound.MoveResult,            0x0D,  0x0D,  "networkservice.cpp:897 @0x14153b440",    "0 ok (target 1/2), !=0 error"),
        new(Inbound.LoginToGame,           0x0E,  0x0E,  "networkservice.cpp:908 @0x14153b570, serverpacketdef.cpp:288 @0x1407a4c60", "0 ok, 0x31 rename+i64, 0x19, 0x1a, other"),
        new(Inbound.GameToLogin,           0x0F,  0x0F,  "networkservice.cpp:928 @0x14153bc00",    "0 ok, !=0 error"),
        new(Inbound.GameToGame,            0x10,  0x10,  "networkservice.cpp:942 @0x14153be30",    "0 ok (incl. kind 0x11), !=0 error"),
        new(Inbound.ResponseTimeSync,      0x11,  0x11,  "networkservice.cpp:756 @0x14153a380",    "modes 0,1,2,3,4"),
        new(Inbound.RequestHeartbeat,      0x12,  0x12,  "networkservice.cpp:749 @0x14153a2e0",    "i32 tick"),
        new(Inbound.RequestClientTickSync, 0x13,  0x13,  "networkservice.cpp:792 @0x14153a780",    "i32 tick"),
        new(Inbound.SyncNumber,            0x14,  0x14,  "networkservice.cpp:799 @0x14153a820",    "u8 sync number"),
        new(Inbound.ServerEnter,           0x15,  0x15,  "scenestategame.cpp:2068 @0x14170dd60",   "KMS / GMS2"),
        new(Inbound.RequestFieldEnter,     0x16,  0x16,  "scenestategame.cpp:2335 @0x14170f930",   "0 ok, !=0 error"),
        new(Inbound.CharacterCreate,       0x46,  0x47,  "uicameraeditdialog.cpp:1608",            "u8 kind + wstr msg"),
        new(Inbound.Gvg,                   0x6D,  0x6F,  "networkservice.cpp:811 @0x14153a8f0",    "kinds 1,7,25,34,42,43"),
        new(Inbound.RelocateWorld,         0x72,  0x74,  "networkservice.cpp:957 @0x14153c0c0",    "modes 0,2(ok/err),4"),
        new(Inbound.DynamicChannel,        0xA7,  0xA9,  "networkservice.cpp:977 @0x14153c4b0",    "modes 0,1"),
        new(Inbound.WaitingTicketUpdate,   0xB4,  0xB6,  "scenestatemulti.cpp:733 @0x14174f8d0",   "i32 waiting"),
        new(Inbound.CharMaxCount,          0xB8,  0xBA,  "networkservice.cpp:1069 @0x14153e050",   "i32 limit, i32 rows"),
        new(Inbound.NpsInfo,               0xC6,  0xC8,  "networkservice.cpp:1075 @0x14153e0d0",   "i64 id, wstr name"),
        new(Inbound.AaErr,                 0xD0,  0xD2,  "networkservice.cpp:1080 @0x14153e150",   "empty -> notice 0xc07"),
        new(Inbound.BypassKey,             0xE6,  0xE8,  "networkservice.cpp:891 @0x14153b3b0",    "i32 clientValue, i64 token"),
        new(Inbound.SyncWorld,             0x10F, 0x111, "scenestatemulti.cpp:766 @0x14174fcc0",   "i64 serverTime"),
        new(Inbound.WorldTimeSync,         0x110, 0x112, "networkservice.cpp:806 @0x14153a8a0",    "i32 idleIntervalMs"),
    };

    /// <summary>The session packets the client sends (SendOp, 21 ops).</summary>
    public static readonly OutboundRow[] OutboundInventory =
    {
        new(Outbound.ResponseVersion,        0x01, 0x01, "networkservice.cpp:532 @0x141538cb0",         "KMS / GMS2"),
        new(Outbound.Patch,                  0x02, 0x02, "networkservice.cpp:539 @0x141538d50, onlinepatchmanagercli.cpp:230 @0x140a85530", "modes 0,4"),
        new(Outbound.ResponseLogin,          0x03, 0x03, "uilogindialog.cpp:204 @0x1419e1160, uiserverlistdialog.cpp:578 @0x141ad28f0", "modes 1,2 (A/B/GMS2)"),
        new(Outbound.ResponseKey,            0x04, 0x04, "networkservice.cpp:865 @0x14153b010",         "i64 account, u64 token, 16B machineId"),
        new(Outbound.Reconnect,              0x05, 0x05, "reconnectionmanagercli.cpp:243 @0x140aad7f0", "mode 1"),
        new(Outbound.Pong,                   0x06, 0x06, "networkservice.cpp:879 @0x14153b220",         "i32 receivedCount"),
        new(Outbound.ReconnectUnk,           0x07, 0x07, "clientreconnectionstructs.cpp:92 @0x140208ab0", "retransmit frame"),
        new(Outbound.FinalizeReconnect,      0x08, 0x08, "clientreconnectionstructs.cpp:104 @0x140208ab0", "empty"),
        new(Outbound.CharacterManagement,    0x09, 0x09, "uiserverlistdialog.cpp:628 @0x141ad2c50, uicharacterlistdialog.cpp", "modes 0..5"),
        new(Outbound.ResponseHeartbeat,      0x0A, 0x0A, "networkservice.cpp:749 @0x14153a2e0",         "i32 serverTick, i32 clientTick"),
        new(Outbound.RequestTimeSync,        0x0B, 0x0B, "networkservice.cpp:743 @0x14153a250",         "i32 sequence"),
        new(Outbound.ResponseClientTickSync, 0x0C, 0x0C, "networkservice.cpp:792 @0x14153a780",         "i32 serverTick, i32 clientTick"),
        new(Outbound.RequestQuit,            0x0D, 0x0D, "networkservice.cpp:501 @0x141538820, :507 @0x1415388a0", "modes 0,1"),
        new(Outbound.ResponseFieldEnter,     0x10, 0x10, "scenestategame.cpp:2002",                     "i32 portal"),
        new(Outbound.RequestServerList,      0x14, 0x14, "uicharacterlistdialog.cpp:600 @0x1418ea860",  "empty"),
        new(Outbound.Channel,                0x3B, 0x3C, "worldservice_1.cpp:4936 @0x141bc80c0",        "i16 channel"),
        new(Outbound.RelocateWorld,          0x3E, 0x3F, "networkservice.cpp:867 @0x14153b010",         "mode 5"),
        new(Outbound.WaitingTicket,          0x6D, 0x6E, "uiserverlistdialog.cpp:720 @0x141ad3260",     "mode 1"),
        new(Outbound.GlobalFactor,           0x82, 0x83, "scenestategame.cpp:2144 @0x14170dd60",        "empty"),
        new(Outbound.SyncWorld,              0xA5, 0xA6, "worldservice_1.cpp:762 @0x141bca110",         "bool idle"),
        new(Outbound.ServerEnter,            0xB0, 0xB2, "networkservice.cpp:594 @0x141539260",         "i32 language"),
    };

    static byte[] WriteOut(IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.ToArray();
    }

    static byte[] WriteProfileOut(IProfileOutPacket p, ProtocolProfile profile)
    {
        var w = new ByteWriter();
        p.Write(w, profile);
        return w.ToArray();
    }

    [Test]
    public void WholeSessionInventoryMatchesKmsAndGms2OpcodeProfiles()
    {
        Assert.That(InboundInventory, Has.Length.EqualTo(33));
        Assert.That(OutboundInventory, Has.Length.EqualTo(21));

        var seenIn = new HashSet<Inbound>();
        foreach (var row in InboundInventory)
        {
            Assert.That(seenIn.Add(row.Op), Is.True, $"duplicate Inbound {row.Op}");
            Assert.That(ProtocolProfile.Kms2525.Number(row.Op), Is.EqualTo(row.KmsOpcode), $"KMS Inbound.{row.Op}");
            Assert.That(row.ClientCite, Is.Not.Empty);
            Assert.That(row.Modes, Is.Not.Empty);
            if (row.Gms2Opcode is ushort gms)
                Assert.That(ProtocolProfile.Gms2.Number(row.Op), Is.EqualTo(gms), $"GMS2 Inbound.{row.Op}");
            else
                Assert.Throws<KeyNotFoundException>(() => ProtocolProfile.Gms2.Number(row.Op), $"GMS2 Inbound.{row.Op} unmapped");
        }

        var seenOut = new HashSet<Outbound>();
        foreach (var row in OutboundInventory)
        {
            Assert.That(seenOut.Add(row.Op), Is.True, $"duplicate Outbound {row.Op}");
            Assert.That(ProtocolProfile.Kms2525.Number(row.Op), Is.EqualTo(row.KmsOpcode), $"KMS Outbound.{row.Op}");
            Assert.That(row.ClientCite, Is.Not.Empty);
            Assert.That(row.Modes, Is.Not.Empty);
            if (row.Gms2Opcode is ushort gms)
                Assert.That(ProtocolProfile.Gms2.Number(row.Op), Is.EqualTo(gms), $"GMS2 Outbound.{row.Op}");
            else
                Assert.Throws<KeyNotFoundException>(() => ProtocolProfile.Gms2.Number(row.Op), $"GMS2 Outbound.{row.Op} unmapped");
        }
    }

    [Test]
    public void RelocateWorldAllModesAndRemainingSessionPacketsRoundTrip()
    {
        // RelocateWorld modes 0, 2 (ok and error), 4
        var rw0 = new ByteWriter(); rw0.WriteByte(0); rw0.WriteInt(2); rw0.WriteInt(1); rw0.WriteUnicodeString("Scania"); rw0.WriteInt(2); rw0.WriteUnicodeString("Bera");
        var wl = (RelocateWorldMessage.WorldList)RelocateWorldMessage.Read(new ByteReader(rw0.ToArray()));
        Assert.That(wl.Worlds, Is.EqualTo(new[] { new RelocateWorldEntry(1, "Scania"), new RelocateWorldEntry(2, "Bera") }));

        var rw2Ok = new ByteWriter(); rw2Ok.WriteByte(2); rw2Ok.WriteInt(0); rw2Ok.WriteUnicodeString("192.0.2.50"); rw2Ok.WriteShort(20500); rw2Ok.WriteInt(4); rw2Ok.WriteBytes(new byte[] { 10, 20, 30, 40 });
        var trOk = (RelocateWorldMessage.Transfer)RelocateWorldMessage.Read(new ByteReader(rw2Ok.ToArray()));
        Assert.That((trOk.Ok, trOk.Address, trOk.Port, trOk.Payload), Is.EqualTo((true, "192.0.2.50", (ushort)20500, new byte[] { 10, 20, 30, 40 })));

        var rw2Err = new ByteWriter(); rw2Err.WriteByte(2); rw2Err.WriteInt(9);
        var trErr = (RelocateWorldMessage.Transfer)RelocateWorldMessage.Read(new ByteReader(rw2Err.ToArray()));
        Assert.That((trErr.Ok, trErr.Result), Is.EqualTo((false, 9)));

        var rw4 = new ByteWriter(); rw4.WriteByte(4);
        Assert.That(RelocateWorldMessage.Read(new ByteReader(rw4.ToArray())), Is.TypeOf<RelocateWorldMessage.Ready>());

        // VersionResult, BypassKey, WaitingTicketUpdate, SyncWorld, WorldTimeSync, NpsInfo, AaErr
        Assert.That(VersionResult.Read(new ByteReader(Array.Empty<byte>())), Is.Not.Null);
        Assert.Throws<PacketLayoutException>(() => VersionResult.Read(new ByteReader(new byte[] { 1 })));

        var bkW = new ByteWriter(); bkW.WriteInt(0x77); bkW.WriteLong(0x1122334455L);
        Assert.That(BypassKey.Read(new ByteReader(bkW.ToArray())), Is.EqualTo(new BypassKey(0x77, 0x1122334455L)));

        var wtW = new ByteWriter(); wtW.WriteInt(42);
        Assert.That(WaitingTicketUpdate.Read(new ByteReader(wtW.ToArray())), Is.EqualTo(new WaitingTicketUpdate(42)));

        var swW = new ByteWriter(); swW.WriteLong(1700001234L);
        Assert.That(SyncWorldMessage.Read(new ByteReader(swW.ToArray())).ServerTime, Is.EqualTo(1700001234L));

        var wtsW = new ByteWriter(); wtsW.WriteInt(60000);
        Assert.That(WorldTimeSync.Read(new ByteReader(wtsW.ToArray())).IdleIntervalMs, Is.EqualTo(60000));

        var npsW = new ByteWriter(); npsW.WriteLong(9001L); npsW.WriteUnicodeString("PlayerOne");
        Assert.That(NpsInfo.Read(new ByteReader(npsW.ToArray())), Is.EqualTo(new NpsInfo(9001L, "PlayerOne")));

        Assert.That(AaErr.Read(new ByteReader(Array.Empty<byte>())), Is.Not.Null);
        Assert.Throws<PacketLayoutException>(() => AaErr.Read(new ByteReader(new byte[] { 0 })));
    }

    [Test]
    public void HandshakeTimeSyncAndRemainingOutboundSessionOpsRoundTrip()
    {
        // Handshake patch modes 0, 1, 2 on KMS2 2525 and GMS2 12
        foreach (byte patch in new byte[] { 0, 1, 2 })
        {
            var frame = new byte[6 + 19];
            BitConverter.TryWriteBytes(frame.AsSpan(2, 4), 19);
            BitConverter.TryWriteBytes(frame.AsSpan(6, 2), (ushort)1);
            BitConverter.TryWriteBytes(frame.AsSpan(8, 4), 2525u);
            BitConverter.TryWriteBytes(frame.AsSpan(12, 4), 1u);
            BitConverter.TryWriteBytes(frame.AsSpan(16, 4), 2u);
            BitConverter.TryWriteBytes(frame.AsSpan(20, 4), 3u);
            frame[24] = patch;
            Assert.That(Handshake.TryParse(frame, out var hs, out int consumed), Is.True);
            Assert.That((hs.Version, hs.PatchType, consumed), Is.EqualTo((2525u, patch, 25)));
        }

        // ResponseVersion KMS & GMS2
        Assert.That(ResponseVersion.Read(new ByteReader(WriteProfileOut(ResponseVersion.For(ProtocolProfile.Kms2525), ProtocolProfile.Kms2525)), ProtocolProfile.Kms2525).Version, Is.EqualTo(2525u));
        Assert.That(ResponseVersion.Read(new ByteReader(WriteProfileOut(ResponseVersion.For(ProtocolProfile.Gms2), ProtocolProfile.Gms2)), ProtocolProfile.Gms2).Version, Is.EqualTo(12u));

        // ResponseTimeSync modes 0, 1, 2, 3, 4
        var s = new ClientSession(new MapleChannel(new MemoryStream()));
        ByteReader Ts(Action<ByteWriter> fill) { var w = new ByteWriter(); fill(w); return new ByteReader(w.ToArray()); }
        Assert.That(s.ApplyTimeSync(Ts(w => { w.WriteByte(0); w.WriteInt(100); w.WriteLong(1700000000L); w.WriteInt(0); w.WriteByte(0); w.WriteInt(1); })), Is.False);
        Assert.That(s.ApplyTimeSync(Ts(w => { w.WriteByte(1); w.WriteInt(200); w.WriteLong(1700000005L); w.WriteInt(0); w.WriteByte(0); })), Is.True);
        Assert.That(s.ApplyTimeSync(Ts(w => { w.WriteByte(2); w.WriteBytes(new byte[17]); })), Is.True);
        Assert.That(s.ApplyTimeSync(Ts(w => { w.WriteByte(3); w.WriteLong(1700000100L); })), Is.False);
        Assert.That(s.SetTime, Is.EqualTo(1700000100L));
        Assert.That(s.ApplyTimeSync(Ts(w => { w.WriteByte(4); w.WriteLong(1700000200L); })), Is.False);
        Assert.That(s.VersionTime, Is.EqualTo(1700000200L));

        // RequestQuit modes 0 & 1, RequestServerList, ResponseHeartbeat, RequestTimeSync, ResponseClientTickSync, ResponseFieldEnter
        Assert.That(RequestQuit.Read(new ByteReader(WriteOut(new RequestQuit(RequestQuit.SelectCharacter)))).Mode, Is.EqualTo(0));
        Assert.That(RequestQuit.Read(new ByteReader(WriteOut(new RequestQuit(RequestQuit.Disconnect)))).Mode, Is.EqualTo(1));
        Assert.That(WriteOut(new RequestServerList()), Is.Empty);
        Assert.That(ResponseHeartbeat.Read(new ByteReader(WriteOut(new ResponseHeartbeat(10, 20)))), Is.EqualTo(new ResponseHeartbeat(10, 20)));
        Assert.That(RequestTimeSync.Read(new ByteReader(WriteOut(new RequestTimeSync(7)))).Sequence, Is.EqualTo(7));
        Assert.That(ResponseClientTickSync.Read(new ByteReader(WriteOut(new ResponseClientTickSync(30, 40)))), Is.EqualTo(new ResponseClientTickSync(30, 40)));
        var rfe = WriteOut(new ResponseFieldEnter(3));
        Assert.That(new ByteReader(rfe).ReadInt(), Is.EqualTo(3));
    }

    sealed class DuplexMemoryStream(Stream read, Stream write) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => write.Flush();
        public override int Read(byte[] buffer, int offset, int count) => read.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => read.ReadAsync(buffer, ct);
        public override void Write(byte[] buffer, int offset, int count) => write.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => write.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
