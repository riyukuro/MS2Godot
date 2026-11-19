using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The group chat packets (client/ms2/groupchatmanager.cpp): RecvOp GroupChat 0x8B, read by CGroupChatManager::OnGroupChat
// (@0x14137f2f0, :577), and SendOp GroupChat 0x4C, the manager's sends. Strings are WriteUnicodeString / ReadUnicodeString
// (u16 length + UTF-16).

/// <summary>
/// CGroupChatMember (groupchatmanager.cpp:172): a member's state and its user record (the Unk_UserProxy the party reads too,
/// <see cref="PartyUser"/>). State 1 = joined (FUN_14137b160, :190), 2 = invited and not joined, or offline (the member
/// offline packet sets 2, :703).
/// </summary>
public sealed class GroupChatMember {
    public int State;
    public PartyUser User = new();
    /// <summary>FUN_14137b160 (:190): the state is 1.</summary>
    public bool Joined => State == 1;
    public string Name => User.Name;

    /// <summary>ReadFromPacket @0x14137b0c0 (:179): u8 state, then the user record (FUN_1408eeb60) when the state is 1 or 2;
    /// another state keeps the empty record. Never fails.</summary>
    public static GroupChatMember Read(ByteReader r) {
        var m = new GroupChatMember { State = r.ReadByte() };
        if (m.State == 1 || m.State == 2) m.User = PartyUser.Read(r);
        return m;
    }

    public void Write(ByteWriter w) {
        w.WriteByte((byte)State);
        if (State == 1 || State == 2) User.Write(w);
    }
}

/// <summary>
/// CGroupChat (groupchatmanager.cpp:194): a group chat, its object id and its members. m_members is a CRBMap keyed by the
/// member's name (CStringW, compared as wcscmp), so the members walk in the names' ordinal order.
/// </summary>
public sealed class GroupChat {
    /// <summary>AddMember's limit (:248).</summary>
    public const int MaxMembers = 0x14;

    public int Oid;
    readonly SortedDictionary<string, GroupChatMember> members = new(StringComparer.Ordinal);
    /// <summary>The members in the map's order.</summary>
    public IEnumerable<GroupChatMember> Members => members.Values;

    /// <summary>ReadFromPacket @0x14137b280 (:201): i32 oid, u8 count, then the members; null when AddMember fails (a 21st
    /// member). The members after the failing one are left unread, as in the client.</summary>
    public static GroupChat Read(ByteReader r) {
        var c = new GroupChat { Oid = r.ReadInt() };
        int count = r.ReadByte();
        for (int i = 0; i < count; i++)
            if (!c.AddMember(GroupChatMember.Read(r))) return null;
        return c;
    }

    public void Write(ByteWriter w) {
        w.WriteInt(Oid);
        w.WriteByte((byte)members.Count);
        foreach (var m in members.Values) m.Write(w);
    }

    /// <summary>AddMember @0x14137b680 (:243): false for null or when the chat has 20 members; else set by the name (a
    /// member of the same name is replaced).</summary>
    public bool AddMember(GroupChatMember member) {
        if (member == null) return false;
        if (MemberCount >= MaxMembers) return false;
        members[member.Name] = member;
        return true;
    }

    /// <summary>HasMemberById @0x14137b410 (:215): a member whose record's character id (m_8) is the id.</summary>
    public bool HasMemberById(long characterId) => members.Values.Any(m => m.User.CharacterId == characterId);
    /// <summary>HasMemberByName @0x14137b510 (:229): a member of the name (CStringW ==, case-sensitive).</summary>
    public bool HasMemberByName(string name) => members.Values.Any(m => m.Name == name);
    /// <summary>RemoveMember @0x14137b7d0 (:258): RemoveKey(name).</summary>
    public void RemoveMember(string name) => members.Remove(name ?? "");
    /// <summary>FindMemberByName @0x14137ba10 (:290): Lookup(name).</summary>
    public GroupChatMember FindMemberByName(string name) => members.TryGetValue(name ?? "", out var m) ? m : null;
    /// <summary>GetMemberCount @0x14137bad0 (:301).</summary>
    public int MemberCount => members.Count;
    /// <summary>FUN_14137bb00 (:304): the members with state 1.</summary>
    public int JoinedCount => members.Values.Count(m => m.Joined);
    /// <summary>FUN_14137bdc0 (:347): the members with state 1, in the map's order.</summary>
    public IEnumerable<GroupChatMember> JoinedMembers => members.Values.Where(m => m.Joined);
    /// <summary>FUN_14137bee0 (:361): the members whose state is not 1, in the map's order.</summary>
    public IEnumerable<GroupChatMember> OtherMembers => members.Values.Where(m => !m.Joined);
}

/// <summary>A GroupChat packet: OnGroupChat's u8 mode, then the mode's body. Modes 5 and 0xe up are not read (the switch
/// at :579 has no case for them).</summary>
public abstract record GroupChatMessage(int Mode) {
    public static GroupChatMessage Read(ByteReader r) {
        int mode = r.ReadByte();
        switch (mode) {
            case 0: return new GroupChatAdd(GroupChat.Read(r));                                                         // OnPacketAddChat :597
            case 1: return new GroupChatCreated(r.ReadInt());                                                           // OnPacketChatCreated :608
            case 2: { string a = r.ReadUnicodeString(), b = r.ReadUnicodeString(); return new GroupChatInviteSent(a, b, r.ReadInt()); }  // FUN_14137f640 :618
            case 3: { string a = r.ReadUnicodeString(), b = r.ReadUnicodeString(); return new GroupChatInvited(a, b, r.ReadInt()); }     // FUN_14137f790 :630
            case 4: return new GroupChatRemove(r.ReadInt());                                                            // OnPacketRemoveChat :646
            case 6: { int oid = r.ReadInt(); string name = r.ReadUnicodeString(); return new GroupChatMemberAdd(oid, name, GroupChatMember.Read(r)); }   // OnPacketAddMember :657
            case 7: { int oid = r.ReadInt(); int reason = r.ReadByte(); return new GroupChatMemberRemove(oid, reason, r.ReadUnicodeString()); }        // OnPacketRemoveMember :672
            case 8: { int oid = r.ReadInt(); return new GroupChatMemberOnline(oid, r.ReadUnicodeString()); }           // OnPacketMemberOnline :688
            case 9: { int oid = r.ReadInt(); return new GroupChatMemberOffline(oid, r.ReadUnicodeString()); }          // OnPacketMemberOffline :703
            case 10: { int oid = r.ReadInt(); string name = r.ReadUnicodeString(); return new GroupChatTalk(oid, name, r.ReadUnicodeString()); }       // OnPacketChatMessage :718
            case 0xb: { int oid = r.ReadInt(); string name = r.ReadUnicodeString(); return new GroupChatItemLinks(oid, name, ChatItemLinks.Read(r)); } // FUN_141380320 :733
            case 0xc: { int oid = r.ReadInt(); string name = r.ReadUnicodeString(); return new GroupChatMemberPicture(oid, name, r.ReadUnicodeString()); } // OnPacketMemberPicture :750
            case 0xd: { int flag = r.ReadByte(); int code = r.ReadInt(); string a = r.ReadUnicodeString(); return new GroupChatResult(flag, code, a, r.ReadUnicodeString()); } // OnPacketResult :768
            default: return new GroupChatOtherMode(mode);
        }
    }
}

/// <summary>Mode 0: a chat (null when its read failed).</summary>
public sealed record GroupChatAdd(GroupChat Chat) : GroupChatMessage(0);
/// <summary>Mode 1: the chat this client asked for was made.</summary>
public sealed record GroupChatCreated(int Oid) : GroupChatMessage(1);
/// <summary>Mode 2 (FUN_14137f640): two names and the chat. STILL-OPEN: what the names are; the UI's handler
/// (FUN_141951720) calls an empty window method and the invite dialog's (0x4e90), not ported.</summary>
public sealed record GroupChatInviteSent(string First, string Second, int Oid) : GroupChatMessage(2);
/// <summary>Mode 3 (FUN_14137f790): this character was invited to the chat by <paramref name="Inviter"/>; the second name
/// goes to the window's line 0x80e with the inviter.</summary>
public sealed record GroupChatInvited(string Inviter, string Second, int Oid) : GroupChatMessage(3);
/// <summary>Mode 4: the chat is gone.</summary>
public sealed record GroupChatRemove(int Oid) : GroupChatMessage(4);
/// <summary>Mode 6: a member joined the chat, invited by <paramref name="Name"/>.</summary>
public sealed record GroupChatMemberAdd(int Oid, string Name, GroupChatMember Member) : GroupChatMessage(6);
/// <summary>Mode 7: a member left (reason 0) or refused (reason 1; see FUN_14194a3c0).</summary>
public sealed record GroupChatMemberRemove(int Oid, int Reason, string Name) : GroupChatMessage(7);
/// <summary>Mode 8: a member came online.</summary>
public sealed record GroupChatMemberOnline(int Oid, string Name) : GroupChatMessage(8);
/// <summary>Mode 9: a member went offline.</summary>
public sealed record GroupChatMemberOffline(int Oid, string Name) : GroupChatMessage(9);
/// <summary>Mode 10: a member's line.</summary>
public sealed record GroupChatTalk(int Oid, string Name, string Message) : GroupChatMessage(10);
/// <summary>Mode 0xb: the items a member's line links (UIGroupMessengerBrowser::ReadItemLinks reads the 0x1e shape,
/// <see cref="ChatItemLinks"/>). The client reads them only when the chat and the member are known; nothing follows them.</summary>
public sealed record GroupChatItemLinks(int Oid, string Name, ChatItemLinks Links) : GroupChatMessage(0xb);
/// <summary>Mode 0xc: a member's profile picture.</summary>
public sealed record GroupChatMemberPicture(int Oid, string Name, string Picture) : GroupChatMessage(0xc);
/// <summary>Mode 0xd: a result code with two names; codes 3, 8, 10 and 0xd show a box.</summary>
public sealed record GroupChatResult(int Flag, int Code, string First, string Second) : GroupChatMessage(0xd);
/// <summary>A mode OnGroupChat ignores.</summary>
public sealed record GroupChatOtherMode(int Value) : GroupChatMessage(Value);

/// <summary>CGroupChatManager::Create @0x14137e8a0 (:485): make a chat (u8 1, the name the client passes; the browser's
/// button sends "").</summary>
public sealed record GroupChatCreateRequest(string Name) : IOutPacket {
    public Outbound Op => Outbound.GroupChat;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteUnicodeString(Name ?? ""); }
}

/// <summary>CGroupChatManager::Invite @0x14137eb00 (:504): invite a character by name (u8 2, name, i32 oid).</summary>
public sealed record GroupChatInviteRequest(string Name, int Oid) : IOutPacket {
    public Outbound Op => Outbound.GroupChat;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteUnicodeString(Name ?? ""); w.WriteInt(Oid); }
}

/// <summary>CGroupChatManager::SendGroupChatMode4 @0x14137efa0 (:540): leave the chat (flag 0, the window's exit box) or
/// refuse its invite (flag 1, the refuse option): u8 4, i32 oid, u8 flag.</summary>
public sealed record GroupChatLeaveRequest(int Oid, int Flag) : IOutPacket {
    public Outbound Op => Outbound.GroupChat;
    public void Write(ByteWriter w) { w.WriteByte(4); w.WriteInt(Oid); w.WriteByte((byte)Flag); }
}

/// <summary>CGroupChatManager::SendGroupChatMode10 @0x14137f080 (:551): a line (u8 10, text, i32 oid).</summary>
public sealed record GroupChatTalkRequest(string Text, int Oid) : IOutPacket {
    public Outbound Op => Outbound.GroupChat;
    public void Write(ByteWriter w) { w.WriteByte(10); w.WriteUnicodeString(Text ?? ""); w.WriteInt(Oid); }
}
