// Layout from the restored client's CBuddyManager::OnBuddy (client/ms2/buddymanager.cpp:95; RecvOp::Buddy 0x61), the header
// it reads first (Unk_SPDB::ReadFrom @0x1407a3cc0, commonfiles/serverpacketdef.cpp:114) and the entry it reads,
// CBuddy::ReadFromPacket (@0x1401cc7c0, commonfiles/buddy.cpp:188). The sends are WriteBuddyRequest (@0x1401fc790) and
// SendBuddyMode20 (@0x140ae55a0) on SendOp::Buddy 0x30.
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// One entry of the friend list, CBuddy (commonfiles/buddy.cpp), as ReadFromPacket reads it: i64 id, i64 the character's id
/// (the field the client keys FindByAccount on and passes as a character id), i64 the account id (m_48), the name, the
/// request message (m_30), u16 channel (m_a8), i32 map id (m_9c), i32 job group (m_a0), i32 job code (m_a4), i16 level (m_98),
/// u8 × 5 the flags f0 (a request from them), f1 (our request waits), f2 (blocked), f5 (online), f6 (away), i32 m_50, i32 m_58
/// (their meaning is STILL-OPEN), the profile image path (m_28, ReadUnicodeStringA: the same u16 + UTF-16 on the wire), the
/// today memo (m_60), the block memo (memo), i32 m_b8, m_bc, m_c0 and the home name (m_c8): the home address CAddressTable
/// formats, i64 m_90 (STILL-OPEN), then the trophy counts (Unk_IntTriple: combat, adventure, life). The flag fields are one bit
/// each, so a byte keeps its low bit.
/// </summary>
public sealed class BuddyInfo {
    public long Id;                 // id
    public long CharacterId;        // account (FUN_1401cc230)
    public long AccountId;          // m_48 (FUN_1401cc250)
    public string Name = "";
    public string Message = "";     // m_30 (FUN_1401cc550): the request message
    public int Channel;             // m_a8
    public int MapId;               // m_9c
    public int JobGroup = 1;        // m_a0: the ctor sets 1
    public int JobCode;             // m_a4
    public int Level;               // m_98
    public bool Received;           // f0 (FUN_1401cc5d0)
    public bool Waiting;            // f1 (FUN_1401cc660)
    public bool Blocked;            // f2 (FUN_1401cc5b0)
    public bool Online;             // f5
    public bool Away;               // f6
    public int Value50, Value58;    // m_50, m_58
    public string ProfilePath = ""; // m_28
    public string TodayMemo = "";   // m_60
    public string Memo = "";        // memo
    public int HomeMapId, HomePlot, HomeApartment; // m_b8, m_bc, m_c0 (FUN_1401cc3e0, FUN_1401cc3a0, FUN_1401cc3c0)
    public string HomeName = "";    // m_c8
    public long Value90;            // m_90
    public (int Combat, int Adventure, int Life) Trophy;   // m_ac
    public bool IsNew;              // m_d0

    /// <summary>GetJobCode (@0x1401cc2b0): the job code, or the group × 10 when it is 0.</summary>
    public int Job => JobCode == 0 ? JobGroup * 10 : JobCode;
    /// <summary>IsFriend (@0x1401cc5f0): none of the three request/block flags.</summary>
    public bool IsFriend => !Blocked && !Received && !Waiting;
    /// <summary>Unk_IntTriple::Sum.</summary>
    public int TrophyTotal => Trophy.Combat + Trophy.Adventure + Trophy.Life;

    /// <summary>ConsumeIsNew (@0x1401cc4f0).</summary>
    public bool ConsumeIsNew() { bool v = IsNew; IsNew = false; return v; }

    /// <summary>ReadFromPacket: the fields replace the entry's (an update reads into the known entry).</summary>
    public void ReadFrom(ByteReader r) {
        Id = r.ReadLong(); CharacterId = r.ReadLong(); AccountId = r.ReadLong();
        Name = r.ReadUnicodeString(); Message = r.ReadUnicodeString();
        Channel = (ushort)r.ReadShort(); MapId = r.ReadInt(); JobGroup = r.ReadInt(); JobCode = r.ReadInt(); Level = r.ReadShort();
        Received = (r.ReadByte() & 1) != 0; Waiting = (r.ReadByte() & 1) != 0; Blocked = (r.ReadByte() & 1) != 0;
        Online = (r.ReadByte() & 1) != 0; Away = (r.ReadByte() & 1) != 0;
        Value50 = r.ReadInt(); Value58 = r.ReadInt();
        ProfilePath = r.ReadUnicodeString(); TodayMemo = r.ReadUnicodeString(); Memo = r.ReadUnicodeString();
        HomeMapId = r.ReadInt(); HomePlot = r.ReadInt(); HomeApartment = r.ReadInt(); HomeName = r.ReadUnicodeString();
        Value90 = r.ReadLong();
        Trophy = (r.ReadInt(), r.ReadInt(), r.ReadInt());
    }

    public static BuddyInfo Read(ByteReader r) { var b = new BuddyInfo(); b.ReadFrom(r); return b; }
}

/// <summary>
/// Buddy (0x61): u8 protocol, then the header Unk_SPDB::ReadFrom reads for it (into Unk_BuddyResponse: Result is the u8 the
/// client keeps as an int, Id the i64 at +0x10, Name and Memo the two strings, Flag the u8 at +0x28), then a body for
/// protocols 1, 8, 9 and 13, which the client reads again from just after the protocol byte. The header reads nothing for
/// those four, so here the body follows the protocol at once. The header's strings have a maximum (0x18 for the name, 0x32
/// for the memo: Unk_WideMaximum) that is not checked here.
/// </summary>
public sealed record BuddyMessage(byte Protocol) {
    /// <summary>The header's result (protocols 2-7, 10, 12, 17; 0 = success); 0 when the protocol has none.</summary>
    public int Result { get; init; }
    /// <summary>The entry id (protocols 3-7, 10, 11, 14, 17).</summary>
    public long Id { get; init; }
    /// <summary>Protocols 3 and 7: i64 unknown30 then i64 unknown08 after the id (their meaning is STILL-OPEN).</summary>
    public long Value30 { get; init; }
    public long Value08 { get; init; }
    public string Name { get; init; } = "";
    public string Memo { get; init; } = "";
    /// <summary>Protocol 14: 0 a friend came online, 1 went offline.</summary>
    public byte Flag { get; init; }
    /// <summary>Protocol 1: the whole list; protocols 8 and 9: the one entry (Entries[0]).</summary>
    public IReadOnlyList<BuddyInfo> Entries { get; init; } = Array.Empty<BuddyInfo>();
    /// <summary>Protocol 13: the answers to our requests while we were away (name, "approved"/"declined"/"removed", id).</summary>
    public IReadOnlyList<(string Name, string Action, long Id)> Answers { get; init; } = Array.Empty<(string, string, long)>();

    /// <summary>Reads one buddy packet (CBuddyManager::OnBuddy, buddymanager.cpp:95). Every protocol it decodes ends with ExpectEnd.
    /// Protocol 0x13 has no handler and no body in the client (Unk_SPDB::ReadFrom reads the type and OnBuddy has no case for 0x13);
    /// the KMS2 server sends an i32 after it, which is dropped here. Protocols it does not decode keep their body unread.</summary>
    public static BuddyMessage Read(ByteReader r) {
        var m = ReadBody(r);
        if (m.Protocol == 0x13) r.ReadBytes(r.Available);
        else if (m.Decoded) r.ExpectEnd($"Buddy protocol {m.Protocol}");
        return m;
    }

    bool Decoded => Protocol is (>= 1 and <= 0xe) or 0x11;

    static BuddyMessage ReadBody(ByteReader r) {
        byte p = r.ReadByte();
        switch (p) {
            case 2: return new(p) { Result = r.ReadByte(), Name = r.ReadUnicodeString(), Memo = r.ReadUnicodeString() };
            case 4: case 6: case 0x11: return new(p) { Result = r.ReadByte(), Id = r.ReadLong() };
            case 3: case 7:
                return new(p) { Result = r.ReadByte(), Id = r.ReadLong(), Value30 = r.ReadLong(), Value08 = r.ReadLong(), Name = r.ReadUnicodeString() };
            case 5: case 10:
                return new(p) { Result = r.ReadByte(), Id = r.ReadLong(), Name = r.ReadUnicodeString(), Memo = r.ReadUnicodeString() };
            case 0xe: return new(p) { Flag = r.ReadByte(), Id = r.ReadLong(), Name = r.ReadUnicodeString() };
            case 0xb: return new(p) { Id = r.ReadLong() };
            case 0xc: return new(p) { Result = r.ReadByte(), Name = r.ReadUnicodeString() };
            case 1: {
                uint n = (uint)r.ReadInt();
                var list = new List<BuddyInfo>();
                for (uint i = 0; i < n; i++) list.Add(BuddyInfo.Read(r));
                return new(p) { Entries = list };
            }
            case 8: case 9: return new(p) { Entries = new[] { BuddyInfo.Read(r) } };
            case 0xd: {
                uint n = (uint)r.ReadInt();
                var list = new List<(string, string, long)>();
                for (uint i = 0; i < n; i++) list.Add((r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadLong()));
                return new(p) { Answers = list };
            }
            default: return new(p);
        }
    }
}

/// <summary>
/// Unk_BuddyRequest through WriteBuddyRequest (@0x1401fc790): u8 protocol; 2: the name and the message; 3, 4, 6, 7, 0x11:
/// i64 the entry id; 5, 10: i64 the entry id (0 for a new block), the name and the memo. Sent by CBuddyManager::SendBuddyRequest
/// (@0x140ae4820) as SendOp::Buddy.
/// </summary>
public sealed record BuddyRequest(byte Protocol, long Id = 0, string Name = "", string Memo = "") : IOutPacket {
    public Outbound Op => Outbound.Buddy;

    /// <summary>2: ask the named character to be a friend, with a message.</summary>
    public static BuddyRequest Invite(string name, string message) => new(2, 0, name, message);
    /// <summary>3: accept a request (UICommunityDialog OnAcceptBuddy).</summary>
    public static BuddyRequest Accept(long id) => new(3, id);
    /// <summary>4: decline a request (OnDeclineBuddy; the auto reject of protocol 9).</summary>
    public static BuddyRequest Decline(long id) => new(4, id);
    /// <summary>5: block the named character with a memo.</summary>
    public static BuddyRequest Block(long id, string name, string memo) => new(5, id, name, memo);
    /// <summary>6: unblock (the query dialog's delBanBuddy box).</summary>
    public static BuddyRequest Unblock(long id) => new(6, id);
    /// <summary>7: remove a friend (OnDeleteBuddy, the delBuddy box).</summary>
    public static BuddyRequest Remove(long id) => new(7, id);
    /// <summary>10: edit a block memo (OnEditBanMemo on the blocked tab).</summary>
    public static BuddyRequest EditMemo(long id, string name, string memo) => new(10, id, name, memo);
    /// <summary>0x11: cancel our request (OnCancelBuddy).</summary>
    public static BuddyRequest Cancel(long id) => new(0x11, id);

    public void Write(ByteWriter w) {
        w.WriteByte(Protocol);
        switch (Protocol) {
            case 2: w.WriteUnicodeString(Name); w.WriteUnicodeString(Memo); break;
            case 3: case 4: case 6: case 7: case 0x11: w.WriteLong(Id); break;
            case 5: case 10: w.WriteLong(Id); w.WriteUnicodeString(Name); w.WriteUnicodeString(Memo); break;
        }
    }
}

/// <summary>SendBuddyMode20 (@0x140ae55a0): u8 20, i32 1 when the game option's auto reject is on (protocol 20 asks for it).</summary>
public sealed record BuddyAutoRejectRequest(bool AutoReject) : IOutPacket {
    public Outbound Op => Outbound.Buddy;
    public void Write(ByteWriter w) { w.WriteByte(20); w.WriteInt(AutoReject ? 1 : 0); }
}
