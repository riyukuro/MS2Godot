using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x02 (RecvOp::VersionResult, CNetworkService::OnVersionResult @0x14153af70, networkservice.cpp:854-857):
/// hides UILoadingProgress (0x3ed) and opens message box 0x214 tagged "forcelogin".
/// </summary>
public sealed record VersionResult
{
    public static VersionResult Read(ByteReader r) { r.ExpectEnd("VersionResult"); return new VersionResult(); }
}

/// <summary>
/// Inbound 0xE6 (RecvOp::BypassKey, CNetworkService::OnBypassKey @0x14153b3b0, networkservice.cpp:891-895):
/// i32 DAT_143ab2858 (sent in ResponseLogin), i64 platform token (passed to IClientPlatform::vfunc_21).
/// </summary>
public sealed record BypassKey(int ClientValue, long PlatformToken)
{
    public static BypassKey Read(ByteReader r)
    {
        var p = new BypassKey(r.ReadInt(), r.ReadLong());
        r.ExpectEnd("BypassKey");
        return p;
    }
}

/// <summary>One world row in RecvOp::RelocateWorld mode 0 (networkservice.cpp:961).</summary>
public sealed record RelocateWorldEntry(int Id, string Name);

/// <summary>
/// Inbound 0x72 (RecvOp::RelocateWorld, CNetworkService::OnRelocateWorld @0x14153c0c0, networkservice.cpp:957-975;
/// CSceneStateRelocateWorld, scenestaterelocateworld.cpp:29-68):
/// mode 0 (world list), mode 2 (transfer endpoint + payload on result 0, else box 0x227), mode 4 (CSceneStateRelocateWorld::SetState(1)).
/// </summary>
public abstract record RelocateWorldMessage(byte Mode)
{
    public sealed record WorldList(IReadOnlyList<RelocateWorldEntry> Worlds) : RelocateWorldMessage(0);
    public sealed record Transfer(int Result, string Address, ushort Port, byte[] Payload) : RelocateWorldMessage(2)
    {
        public bool Ok => Result == 0;
    }
    public sealed record Ready() : RelocateWorldMessage(4);

    public static RelocateWorldMessage Read(ByteReader r)
    {
        byte kind = r.ReadByte();
        switch (kind)
        {
            case 0:
            {
                int count = r.ReadInt();
                var list = new List<RelocateWorldEntry>(Math.Max(0, count));
                for (int i = 0; i < count; i++)
                    list.Add(new RelocateWorldEntry(r.ReadInt(), r.ReadUnicodeString()));
                r.ExpectEnd("RelocateWorld 0");
                return new WorldList(list);
            }
            case 2:
            {
                int result = r.ReadInt();
                if (result != 0)
                {
                    r.ExpectEnd("RelocateWorld 2 error");
                    return new Transfer(result, "", 0, Array.Empty<byte>());
                }
                string address = r.ReadUnicodeString();
                ushort port = r.ReadUShort();
                int size = r.ReadInt();
                if (size < 0) throw new PacketLayoutException($"RelocateWorld 2 size {size}");
                byte[] payload = r.ReadBytes(size);
                r.ExpectEnd("RelocateWorld 2");
                return new Transfer(0, address, port, payload);
            }
            case 4:
                r.ExpectEnd("RelocateWorld 4");
                return new Ready();
            default:
                throw new PacketLayoutException($"RelocateWorld unknown mode {kind}");
        }
    }
}

/// <summary>
/// Inbound 0xB4 (RecvOp::WaitingTicketUpdate, CSceneStateMulti::OnWaitingTicketUpdate @0x14174f8d0,
/// scenestatemulti.cpp:733-763): i32 waitingCount.
/// </summary>
public sealed record WaitingTicketUpdate(int WaitingCount)
{
    public static WaitingTicketUpdate Read(ByteReader r)
    {
        var p = new WaitingTicketUpdate(r.ReadInt());
        r.ExpectEnd("WaitingTicketUpdate");
        return p;
    }
}

/// <summary>
/// Inbound 0x10F (GMS2 0x111; RecvOp::SyncWorld, CSceneStateMulti::OnServerTimePacket @0x14174fcc0, scenestatemulti.cpp:766-769):
/// i64 serverTime stored on CSceneStateMulti::m_nServerTime.
/// </summary>
public sealed record SyncWorldMessage(long ServerTime)
{
    public static SyncWorldMessage Read(ByteReader r)
    {
        var p = new SyncWorldMessage(r.ReadLong());
        r.ExpectEnd("SyncWorld");
        return p;
    }
}

/// <summary>
/// Inbound 0x110 (GMS2 0x112; RecvOp::WorldTimeSync, CNetworkService::OnWorldTimeSync @0x14153a8a0, networkservice.cpp:806-809):
/// i32 idleIntervalMs -> WorldService::SetIdleInterval (@0x141bca170, worldservice_1.cpp:771), <see cref="WorldIdleTimer.SetIdleInterval"/>.
/// </summary>
public sealed record WorldTimeSync(int IdleIntervalMs)
{
    public static WorldTimeSync Read(ByteReader r)
    {
        var p = new WorldTimeSync(r.ReadInt());
        r.ExpectEnd("WorldTimeSync");
        return p;
    }
}

/// <summary>
/// Inbound 0xC6 (RecvOp::NpsInfo, CNetworkService::OnNpsInfo @0x14153e0d0, networkservice.cpp:1075-1078;
/// Unk_SPDH::ReadFrom @0x1407a4ac0, serverpacketdef.cpp:263-266): i64 id, unicode name -> CNPSMan::SetPlayer / FUN_140db9040.
/// </summary>
public sealed record NpsInfo(long Id, string Name)
{
    public static NpsInfo Read(ByteReader r)
    {
        var p = new NpsInfo(r.ReadLong(), r.ReadUnicodeString());
        r.ExpectEnd("NpsInfo");
        return p;
    }
}

/// <summary>
/// Inbound 0xD0 (RecvOp::AaErr, CNetworkService::OnAaErr @0x14153e150, networkservice.cpp:1080):
/// empty payload; shows system notice 0xc07 (FUN_141275260(0xc07, 1, 1)).
/// </summary>
public sealed record AaErr
{
    public const int NoticeStringId = 0xc07;
    public static AaErr Read(ByteReader r) { r.ExpectEnd("AaErr"); return new AaErr(); }
}

/// <summary>
/// Outbound SyncWorld (SendOp::SyncWorld, KMS 0xA5 / GMS2 0xA6; WorldService::SendSyncWorld @0x141bca110, worldservice_1.cpp:762-767):
/// one bool, true when the idle timer ran out (<see cref="WorldIdleTimer.CheckIdleTimeout"/>), false when it is reset or switched off.
/// </summary>
public sealed record SyncWorldRequest(bool Idle) : IOutPacket
{
    public Outbound Op => Outbound.SyncWorld;
    public void Write(ByteWriter w) => w.WriteBool(Idle);
}
