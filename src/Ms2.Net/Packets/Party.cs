// Layout from the restored client's CPartyManager::OnParty (partymanager.cpp:447; RecvOp::Party 0x53) and the member record it
// reads, Unk_UserProxy::FUN_1408eeb60 (commonfiles/userproxy.cpp:1367).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// A party member's user record (Unk_UserProxy::FUN_1408eeb60, userproxy.cpp:1367). The fields the client's party code reads are
/// kept; the others are read past and written as zeros (the KR platform block, the clubs and the lists as empty).
/// </summary>
public sealed class PartyUser {
    public long AccountId;      // m_0
    public long CharacterId;    // m_8
    public string Name = "";    // SetName
    public int MapId;           // m_2c: the field (string table 3, the map name)
    public int MapInstanceId;   // m_30: compared when the channel is 0 (a dungeon)
    public short Level;         // m_34
    public ushort Channel;      // m_38: 0 in a dungeon
    public int JobGroup;        // m_3c: the job group (the popup menu target's m_n20)
    public int Job;             // m_40: the job code the movie's job icon frame is named by ("_" + job)
    public int MaxHp;           // m_44
    public int Hp;              // m_48
    public short TombFlags;     // m_4c: GetTombType reads the low two bits
    public int GearScore;       // m_d0
    public (int, int, int) Trophy;  // m_d4 (Unk_IntTriple): the trophy counts; the popup menu shows their sum
    public string Picture = ""; // SetImage
    public byte ReturnUserKind; // m_180: 2 = a returning user (the return icon)
    public bool IsDead;         // m_184: FUN_141637c40's state 2

    /// <summary>GetTombType @0x1408f1cf0 (userproxy.cpp:1895): m_4c &amp; 3 is 1 or 2, else 0.</summary>
    public int TombType => (TombFlags & 3) is 1 or 2 ? TombFlags & 3 : 0;

    public static PartyUser Read(ByteReader r) {
        var u = new PartyUser();
        u.AccountId = r.ReadLong();
        u.CharacterId = r.ReadLong();
        u.Name = r.ReadUnicodeString();
        r.ReadByte(); r.ReadByte();                    // m_14, m_78
        r.ReadLong();                                  // m_18
        r.ReadInt();                                   // m_20
        r.ReadInt();                                   // m_28
        u.MapId = r.ReadInt();
        u.MapInstanceId = r.ReadInt();
        u.Level = r.ReadShort();
        u.Channel = (ushort) r.ReadShort();
        u.JobGroup = r.ReadInt();                      // m_3c
        u.Job = r.ReadInt();
        u.MaxHp = r.ReadInt();
        u.Hp = r.ReadInt();
        u.TombFlags = r.ReadShort();
        r.ReadLong(); r.ReadLong(); r.ReadLong();      // m_50, m_b0, m_b8
        r.ReadInt();                                   // m_c0
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat();   // m_c4 (ReadVector3)
        u.GearScore = r.ReadInt();
        r.ReadInt(); r.ReadInt();                      // m_58[2]
        r.ReadLong();                                  // m_70
        u.Trophy = (r.ReadInt(), r.ReadInt(), r.ReadInt());   // m_d4 (Unk_IntTriple::ReadFrom)
        r.ReadLong();                                  // m_80
        r.ReadUnicodeString();                         // FUN_1408f0950
        r.ReadUnicodeString();                         // FUN_1408f08c0
        u.Picture = r.ReadUnicodeString();             // ReadUnicodeStringA: the same u16 + UTF-16 on the wire
        int clubs = r.ReadByte();                      // DecodeClubs: Club::UserClub::FUN_14020cfc0 (clubcommon.cpp:248)
        for (int i = 0; i < clubs; i++)
            if (r.ReadByte() == 1) { r.ReadLong(); r.ReadUnicodeString(); }
        r.ReadByte();                                  // m_24
        for (int i = 0; i < 12; i++) r.ReadInt();      // m_140[12]
        r.ReadUnicodeString(); r.ReadLong();           // m_330: CKRPlatformProxy::vfunc_6 (userproxy.cpp:279; region 0, KMS)
        int set = r.ReadInt();                         // FUN_1408f1110 per value
        for (int i = 0; i < set; i++) r.ReadLong();
        u.ReturnUserKind = r.ReadByte();
        u.IsDead = r.ReadBool();
        r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadLong(); r.ReadInt(); r.ReadLong();   // m_188 .. m_1b0
        int a = r.ReadInt(); for (int i = 0; i < a; i++) r.ReadLong();   // m_1b8
        int b = r.ReadInt(); for (int i = 0; i < b; i++) r.ReadLong();   // m_1e8
        r.ReadShort();                                 // m_1ac
        r.ReadLong();                                  // m_88
        return u;
    }

    /// <summary>Writes the record in the layout <see cref="Read"/> takes (tests and offline fixtures).</summary>
    public void Write(ByteWriter w) {
        w.WriteLong(AccountId); w.WriteLong(CharacterId); w.WriteUnicodeString(Name);
        w.WriteByte(0); w.WriteByte(0); w.WriteLong(0); w.WriteInt(0); w.WriteInt(0);
        w.WriteInt(MapId); w.WriteInt(MapInstanceId); w.WriteShort(Level); w.WriteShort((short) Channel);
        w.WriteInt(JobGroup); w.WriteInt(Job); w.WriteInt(MaxHp); w.WriteInt(Hp); w.WriteShort(TombFlags);
        w.WriteLong(0); w.WriteLong(0); w.WriteLong(0); w.WriteInt(0);
        w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(0);
        w.WriteInt(GearScore); w.WriteInt(0); w.WriteInt(0); w.WriteLong(0);
        w.WriteInt(Trophy.Item1); w.WriteInt(Trophy.Item2); w.WriteInt(Trophy.Item3); w.WriteLong(0);
        w.WriteUnicodeString(""); w.WriteUnicodeString(""); w.WriteUnicodeString(Picture);
        w.WriteByte(0);                                // no clubs
        w.WriteByte(0);
        for (int i = 0; i < 12; i++) w.WriteInt(0);
        w.WriteUnicodeString(""); w.WriteLong(0);
        w.WriteInt(0);
        w.WriteByte(ReturnUserKind); w.WriteBool(IsDead);
        w.WriteLong(0); w.WriteInt(0); w.WriteInt(0); w.WriteLong(0); w.WriteInt(0); w.WriteLong(0);
        w.WriteInt(0); w.WriteInt(0); w.WriteShort(0); w.WriteLong(0);
    }

    public PartyUser Copy() => (PartyUser) MemberwiseClone();
}

/// <summary>The party packet's message (OnParty's mode byte, then the mode's body).</summary>
public abstract record PartyMessage(int Mode) {
    /// <summary>Unk_LockedIntMap::Read (unit_140268010_sharedstatemap.cpp:85): i32 count of { i32 key, u8 value }. Not kept.</summary>
    static void SkipCounters(ByteReader r) { int n = r.ReadInt(); for (int i = 0; i < n; i++) { r.ReadInt(); r.ReadByte(); } }

    public static PartyMessage Read(ByteReader r) {
        int mode = r.ReadByte();
        switch (mode) {
            case 2: { var u = PartyUser.Read(r); SkipCounters(r); return new PartyJoin(u); }            // OnPartyMemberJoin
            case 3: return new PartyLeave(r.ReadLong(), r.ReadBool());                                    // OnPartyMemberLeave
            case 4: return new PartyKick(r.ReadLong());                                                    // OnPartyMemberKick
            case 5: return new PartyOnline(PartyUser.Read(r));                                             // OnPartyMemberOnline
            case 6: return new PartyOffline(r.ReadLong());                                                 // OnPartyMemberOffline
            case 7: return new PartyDisband();                                                             // OnPartyDisband
            case 8: return new PartyLeaderChange(r.ReadLong());                                            // OnPartyLeaderChange
            case 9: return PartyInfo.ReadBody(r);                                                          // OnPartyInfo
            case 0xb: return new PartyInvite(r.ReadUnicodeString(), r.ReadInt());                          // OnPartyInvite
            case 0xc: case 0xd: { long id = r.ReadLong(); return new PartyMemberUpdate(mode, id, PartyUser.Read(r)); }   // OnPartyMemberInfoUpdate
            case 0xf: return new PartyGearScore(r.ReadLong(), r.ReadInt());                                // OnPartyMemberGearScore
            case 0x13: {                                                                                   // FUN_14162dfc0: DecodeFlags
                long id = r.ReadLong();
                return new PartyMemberFlags(id, r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadShort());   // Unk_UserProxyFlagsPacket::Decode (userproxy.cpp:838)
            }
            case 0x2f: {                                                                                   // OnPartyVoteStart
                int kind = r.ReadByte();
                var vote = PartyVote.Create(kind);
                vote?.Read(r);
                return new PartyVoteStart(kind, vote);
            }
            case 0x30: { long id = r.ReadLong(); return new PartyVoteReply(id, r.ReadBool()); }           // OnPartyVoteReply
            case 0x31: return new PartyVoteEnd();                                                          // OnPartyVoteEnd
            default: return new PartyOtherMode(mode);
        }
    }

    internal static void WriteCounters(ByteWriter w) => w.WriteInt(0);
}

public sealed record PartyJoin(PartyUser User) : PartyMessage(2) {
    public byte[] Write() { var w = new ByteWriter(); w.WriteByte(2); User.Write(w); WriteCounters(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
}
public sealed record PartyLeave(long CharacterId, bool Disband) : PartyMessage(3);
public sealed record PartyKick(long CharacterId) : PartyMessage(4);
public sealed record PartyOnline(PartyUser User) : PartyMessage(5);
public sealed record PartyOffline(long CharacterId) : PartyMessage(6);
public sealed record PartyDisband() : PartyMessage(7);
public sealed record PartyLeaderChange(long CharacterId) : PartyMessage(8);
public sealed record PartyMemberUpdate(int UpdateMode, long CharacterId, PartyUser User) : PartyMessage(UpdateMode);
public sealed record PartyGearScore(long CharacterId, int Value) : PartyMessage(0xf);
/// <summary>Mode 0x13: ApplyFlags (userproxy.cpp:1539) sets m_0, m_44 (max HP), m_48 (HP) and m_4c (tomb flags).</summary>
public sealed record PartyMemberFlags(long CharacterId, long AccountId, int MaxHp, int Hp, short TombFlags) : PartyMessage(0x13);
/// <summary>OnPartyInvite @0x14162c8b0 (partymanager.cpp:968): an invite, the inviter's name (partyName) and the party's
/// kind (partyKind; the Maple2 server sends its party id). The answer sends both back (SendPartyMode2).</summary>
public sealed record PartyInvite(string PartyName, int Kind) : PartyMessage(0xb);
/// <summary>Mode 0x2f, OnPartyVoteStart (partymanager.cpp:1450): u8 kind, then the vote's body (vfunc_1) for the kinds
/// CreateVote makes; <see cref="Vote"/> is null for any other kind (its body is not read) and replaces the running vote.</summary>
public sealed record PartyVoteStart(int VoteKind, PartyVote Vote) : PartyMessage(0x2f) {
    public byte[] Write() { var w = new ByteWriter(); w.WriteByte(0x2f); w.WriteByte((byte) VoteKind); Vote?.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
}
/// <summary>Mode 0x30, OnPartyVoteReply @0x141630670: i64 member, bool yes (PartyVoteCli::AddVote).</summary>
public sealed record PartyVoteReply(long CharacterId, bool Yes) : PartyMessage(0x30) {
    public byte[] Write() { var w = new ByteWriter(); w.WriteByte(0x30); w.WriteLong(CharacterId); w.WriteBool(Yes); return w.Buffer.AsSpan(0, w.Length).ToArray(); }
}
/// <summary>Mode 0x31, OnPartyVoteEnd @0x141630720: no body.</summary>
public sealed record PartyVoteEnd() : PartyMessage(0x31);

/// <summary>
/// PartyVoteCli (partyvotecli.cpp): the party's running vote. CreateVote @0x140a89240 makes kind 1 (PartyVoteKickUserCli)
/// and 2 (PartyVoteReadyCli). vfunc_1 @0x140a89380 reads i32 id, i64 end time (server seconds), then three sets of i64
/// members (i32 count each: the participants, the yes and the no votes); the kick vote then reads i64 owner, i64 target,
/// the target's name and u8 reason (the votes the kick needs).
/// </summary>
public sealed class PartyVote {
    public const int KickUser = 1, Ready = 2;
    public int Kind { get; private set; }
    public int Id;
    public long Expires;
    /// <summary>std::set&lt;unsigned __int64&gt;: sorted members.</summary>
    public readonly SortedSet<long> Participants = new(), Yes = new(), No = new();
    public long Owner, Target;
    public string Name = "";
    public byte Reason;

    /// <summary>CreateVote @0x140a89240: null for a kind other than 1 and 2.</summary>
    public static PartyVote Create(int kind) => kind is KickUser or Ready ? new PartyVote { Kind = kind } : null;

    /// <summary>vfunc_5: the party window's vote page shows the vote (the kick vote's 1; the base and the ready vote 0).</summary>
    public bool ShowsPage => Kind == KickUser;

    /// <summary>HasVoted @0x140a895c0: the own character (DAT_143ab2828) is in the yes or the no votes.</summary>
    public bool HasVoted(long self) => Yes.Contains(self) || No.Contains(self);

    public void Read(ByteReader r) {
        Id = r.ReadInt();
        Expires = r.ReadLong();
        ReadSet(r, Participants); ReadSet(r, Yes); ReadSet(r, No);
        if (Kind == KickUser) {                         // PartyVoteKickUserCli::vfunc_1 @0x140a89820
            Owner = r.ReadLong(); Target = r.ReadLong(); Name = r.ReadUnicodeString(); Reason = r.ReadByte();
        }
    }

    /// <summary>FUN_140a8a230: i32 count, then the members.</summary>
    static void ReadSet(ByteReader r, SortedSet<long> set) {
        uint n = (uint) r.ReadInt();
        for (uint i = 0; i < n; i++) set.Add(r.ReadLong());
    }

    /// <summary>The body <see cref="Read"/> takes (tests and offline fixtures).</summary>
    public void Write(ByteWriter w) {
        w.WriteInt(Id); w.WriteLong(Expires);
        foreach (var set in new[] { Participants, Yes, No }) { w.WriteInt(set.Count); foreach (long id in set) w.WriteLong(id); }
        if (Kind == KickUser) { w.WriteLong(Owner); w.WriteLong(Target); w.WriteUnicodeString(Name ?? ""); w.WriteByte(Reason); }
    }
}

/// <summary>A mode the party window does not use (search, dungeon matching, notices); its body is not read.</summary>
public sealed record PartyOtherMode(int OtherMode) : PartyMessage(OtherMode);

/// <summary>One member of mode 9: the offline flag, the record (its counters are read past).</summary>
public sealed record PartyInfoMember(bool Offline, PartyUser User);

/// <summary>
/// Mode 9, OnPartyInfo (partymanager.cpp:1104): bool announce, i32 party id, i64 leader id, u8 count (at most 10 read) of
/// { bool offline, record, counters }, bool matchmaking, i32 dungeon id, u8 search permissions, bool survival squad, u8 has
/// recruit (then the recruit). The permissions, the survival squad flag and the recruit are read past.
/// </summary>
public sealed record PartyInfo(bool Announce, int PartyId, long LeaderId, IReadOnlyList<PartyInfoMember> Members, bool Matchmaking, int DungeonId)
    : PartyMessage(9) {
    internal static PartyInfo ReadBody(ByteReader r) {
        bool announce = r.ReadBool();
        int id = r.ReadInt();
        long leader = r.ReadLong();
        int count = r.ReadByte();
        var members = new List<PartyInfoMember>();
        for (int i = 0; i < count && i < 10; i++) {
            bool offline = r.ReadBool();
            var u = PartyUser.Read(r);
            int n = r.ReadInt(); for (int k = 0; k < n; k++) { r.ReadInt(); r.ReadByte(); }
            members.Add(new PartyInfoMember(offline, u));
        }
        bool matchmaking = r.ReadBool();
        int dungeon = r.ReadInt();
        r.ReadByte();                                  // >> permissions: Unk_PartyBits, one u8 (dungeonrecord.cpp:34)
        r.ReadBool();                                  // flag29 (IsSurvivalSquadRegistered)
        if (r.ReadByte() != 0) {                       // the recruit (matchpartyrecruitschcond.cpp:228); not kept
            r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadUnicodeString();   // id, field, category, kind, title
            r.ReadByte(); r.ReadInt(); r.ReadInt();                                       // minimum level, maximum level, members
            r.ReadLong(); r.ReadLong(); r.ReadUnicodeString(); r.ReadLong();              // owner, created, owner name, flags
        }
        return new PartyInfo(announce, id, leader, members, matchmaking, dungeon);
    }

    /// <summary>Writes a mode 9 body (tests and offline fixtures): no search permissions, no survival squad, no recruit.</summary>
    public byte[] Write() {
        var w = new ByteWriter();
        w.WriteByte(9); w.WriteBool(Announce); w.WriteInt(PartyId); w.WriteLong(LeaderId); w.WriteByte((byte) Members.Count);
        foreach (var m in Members) { w.WriteBool(m.Offline); m.User.Write(w); WriteCounters(w); }
        w.WriteBool(Matchmaking); w.WriteInt(DungeonId); w.WriteByte(0); w.WriteBool(false); w.WriteByte(0);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }
}

/// <summary>A party member (CPartyMember, partymember.cpp): the record and the client's own flags.</summary>
public sealed class PartyMember {
    public PartyUser User;
    public bool Offline;        // IsOffline (:108)
    public bool Leader;         // SetLeader
    public int ReadyState;      // SetReadyState: the ready check's 1 waiting, 2 ready, 3 not ready (0 none)
    public int Index;           // SetIndex
    public long CharacterId => User.CharacterId;

    /// <summary>FUN_141637c40 (:115): 0 offline, 2 when m_184 is set, else 1.</summary>
    public int State => Offline ? 0 : User.IsDead ? 2 : 1;
    /// <summary>GetReadyState (:125): 0 offline, else the ready state.</summary>
    public int ShownReadyState => Offline ? 0 : ReadyState;
}

/// <summary>
/// CPartyManager (partymanager.cpp): the party the party packets build. The window's calls are raised as events, in the order the
/// handlers make them (FindPartyDialog(0x4e45) then the dialog's method).
/// </summary>
public sealed class PartyBook {
    public const int MaxMembers = 10;
    readonly List<PartyMember> members = new();
    public IReadOnlyList<PartyMember> Members => members;
    public int PartyId { get; private set; }
    public long LeaderId { get; private set; }
    public bool Matchmaking { get; private set; }
    public int DungeonId { get; private set; }
    /// <summary>partyName (GetPartyName @0x14162b4f0): the last invite's name, kept for its answer.</summary>
    public string PartyName { get; private set; } = "";
    /// <summary>partyKind (GetPartyKind @0x14162b540): the last invite's kind, kept for its answer.</summary>
    public int PartyKind { get; private set; }

    /// <summary>OpenDialog(0x4e45): OnPartyInfo with more than one member, or a join while the window is closed (FUN_141924680).</summary>
    public event Action Opened;
    /// <summary>CloseDialog(0x4e45): a disband, or the own leave / kick.</summary>
    public event Action Closed;
    /// <summary>UIPartyDialog::AddMember (the record's flags 0x1ff).</summary>
    public event Action<PartyMember> MemberAdded;
    /// <summary>UIPartyDialog::RemoveMemberEntry.</summary>
    public event Action<PartyMember> MemberRemoved;
    /// <summary>UIPartyDialog::UpdateMember(member, flags).</summary>
    public event Action<PartyMember, int> MemberUpdated;
    /// <summary>UIPartyDialog::RefreshAllMembers (flags 0x180 for each).</summary>
    public event Action MembersRefreshed;
    /// <summary>UIPartyDialog::ChangeChief(old, new); either may be null.</summary>
    public event Action<PartyMember, PartyMember> ChiefChanged;
    /// <summary>OnPartyInvite's answer or box, after <see cref="PartyName"/> and <see cref="PartyKind"/> are kept.</summary>
    public event Action<PartyInvite> Invited;

    /// <summary>IsInParty (:526): more than one member.</summary>
    public bool IsInParty => members.Count > 1;
    public PartyMember Find(long characterId) => members.FirstOrDefault(m => m.CharacterId == characterId);
    /// <summary>FindMemberByName (partymanager.cpp:629): the first member with the name, case ignored (CompareNoCase).</summary>
    public PartyMember FindByName(string name) => members.FirstOrDefault(m => string.Equals(m.User.Name, name, StringComparison.OrdinalIgnoreCase));
    /// <summary>IsLocalPlayerLeader (:530).</summary>
    public bool IsLeader(long characterId) => IsInParty && LeaderId == characterId;

    /// <summary>ClearParty (:425), with ClearVote.</summary>
    void Clear() { members.Clear(); PartyId = 0; LeaderId = 0; Vote = null; }

    /// <summary>vote (GetVote @0x14162c350): the running vote, null without one.</summary>
    public PartyVote Vote { get; private set; }
    /// <summary>The vote's start (vfunc_2), after the ready vote's member marks: the kick vote's chat notice and window
    /// refresh, the ready vote's chat line, sound and answer box (the UI's part).</summary>
    public event Action<PartyVote> VoteStarted;
    /// <summary>The vote's end (vfunc_3), before the ready vote's unanswered marks and before the vote is cleared: the ready
    /// vote's notice and the window's deadline.</summary>
    public event Action<PartyVote> VoteEnded;

    /// <summary>SetMemberReadyState @0x14162c580: the member's ready state, and UpdateMember(member, 1) (gated by the host
    /// on the window, as FindPartyDialog is).</summary>
    public void SetMemberReadyState(long characterId, int state) {
        if (Find(characterId) is not { } m) return;
        m.ReadyState = state;
        MemberUpdated?.Invoke(m, 1);
    }

    /// <summary>SetAllMembersState @0x14162c4a0's member part (the window's SetDeadline(0) is its caller's): every member's
    /// ready state, with UpdateMember(member, 1). Only with the window open.</summary>
    public void SetAllMembersState(int state) {
        foreach (var m in members) { m.ReadyState = state; MemberUpdated?.Invoke(m, 1); }
    }

    /// <summary>FUN_14162c3a0: with the window open, the members still waiting (GetReadyState 1) are not ready (3).</summary>
    void MarkUnanswered(bool windowOpen) {
        if (!windowOpen) return;
        foreach (var m in members) {
            if (m.ShownReadyState != 1) continue;
            m.ReadyState = 3;
            MemberUpdated?.Invoke(m, 1);
        }
    }

    /// <summary>OnParty (partymanager.cpp:447) for the modes the window shows; <paramref name="self"/> is DAT_143ab2828, the own
    /// character id. The chat notices (FUN_1412751b0) are not raised.</summary>
    public void Apply(PartyMessage message, long self, bool windowOpen) {
        ApplyMessage(message, self, windowOpen);
        Applied?.Invoke(message);
    }

    /// <summary>Raised after <see cref="Apply"/> for the UI's own 0x53 handlers (PacketHandlerManager::RegisterHandler, e.g.
    /// UIPopUpMenu::FUN_141a80a50, which closes its menu on modes 7 and 8). Whether the client runs them before or after
    /// CPartyManager::OnParty is not checked.</summary>
    public event Action<PartyMessage> Applied;

    void ApplyMessage(PartyMessage message, long self, bool windowOpen) {
        switch (message) {
            case PartyInfo info: {                          // :1104
                if (PartyId > 0) { Clear(); Closed?.Invoke(); }
                PartyId = info.PartyId; LeaderId = info.LeaderId;
                members.Clear();
                foreach (var m in info.Members)
                    members.Add(new PartyMember { User = m.User, Offline = m.Offline, Index = members.Count, Leader = m.User.CharacterId == info.LeaderId });
                Matchmaking = info.Matchmaking; DungeonId = info.DungeonId;
                if (IsInParty) Opened?.Invoke();
                break;
            }
            case PartyJoin join: {                          // :996
                if (members.Count == MaxMembers) return;
                var m = new PartyMember { User = join.User, Index = members.Count };
                members.Add(m);
                if (windowOpen) MemberAdded?.Invoke(m); else Opened?.Invoke();
                break;
            }
            case PartyLeave leave: {                        // :1020
                var m = Find(leave.CharacterId);
                if (m == null) return;
                if (leave.Disband) { Clear(); Closed?.Invoke(); }
                else RemoveAt(members.IndexOf(m));
                break;
            }
            case PartyKick kick: {                          // :1043
                var m = Find(kick.CharacterId);
                if (m == null) return;
                if (m.CharacterId == self) { Clear(); Closed?.Invoke(); }
                else RemoveAt(members.IndexOf(m));
                break;
            }
            case PartyInvite invite:                        // :968
                PartyName = invite.PartyName ?? ""; PartyKind = invite.Kind;
                Invited?.Invoke(invite);
                break;
            case PartyDisband:                              // :1066
                Clear(); Closed?.Invoke();
                break;
            case PartyLeaderChange lc: {                    // :1076
                if (lc.CharacterId == 0 || lc.CharacterId == LeaderId) return;
                var old = Find(LeaderId);
                if (old != null) old.Leader = false;
                var now = Find(lc.CharacterId);
                if (now != null) now.Leader = true;
                LeaderId = lc.CharacterId;
                ChiefChanged?.Invoke(old, now);
                break;
            }
            case PartyOnline on: {                          // :1151
                var m = Find(on.User.CharacterId);
                if (m != null) { m.Offline = false; MemberUpdated?.Invoke(m, 0x1ff); }
                else {
                    if (members.Count == MaxMembers) return;
                    m = new PartyMember { User = on.User, Index = members.Count };
                    members.Add(m);
                    MemberAdded?.Invoke(m);
                }
                break;
            }
            case PartyOffline off: {                        // :1180
                var m = Find(off.CharacterId);
                if (m == null) return;
                m.Offline = true;
                if (Vote is { Kind: PartyVote.Ready } v) {      // vote->vfunc_6: PartyVoteReadyCli @0x140a8a040
                    v.Participants.Remove(m.CharacterId); v.Yes.Remove(m.CharacterId); v.No.Remove(m.CharacterId);
                }
                MemberUpdated?.Invoke(m, 1);
                break;
            }
            case PartyMemberUpdate up: {                    // :1215
                var m = Find(up.CharacterId);
                if (m == null) return;
                m.User = up.User;
                MemberUpdated?.Invoke(m, 399);
                if (m.CharacterId == self) MembersRefreshed?.Invoke();
                break;
            }
            case PartyMemberFlags f: {                      // :1196
                var m = Find(f.CharacterId);
                if (m == null) return;
                m.User.AccountId = f.AccountId; m.User.MaxHp = f.MaxHp; m.User.Hp = f.Hp; m.User.TombFlags = f.TombFlags;
                MemberUpdated?.Invoke(m, 399);
                if (m.CharacterId == self) MembersRefreshed?.Invoke();
                break;
            }
            case PartyVoteStart start: {                    // :1450
                Vote = start.Vote;
                if (Vote == null) return;
                if (Vote.Kind == PartyVote.Ready)               // PartyVoteReadyCli::vfunc_2 @0x140a89b30
                    foreach (long id in Vote.Participants)
                        SetMemberReadyState(id, Vote.Yes.Contains(id) ? 2 : Vote.No.Contains(id) ? 3 : 1);
                VoteStarted?.Invoke(Vote);
                break;
            }
            case PartyVoteReply reply: {                    // AddVote @0x140a894e0
                if (Vote == null) return;
                (reply.Yes ? Vote.Yes : Vote.No).Add(reply.CharacterId);
                if (Vote.Kind == PartyVote.Ready) SetMemberReadyState(reply.CharacterId, reply.Yes ? 2 : 3);   // vfunc_7 @0x140a8a100
                break;
            }
            case PartyVoteEnd: {                            // :1471
                if (Vote is { } v) {
                    VoteEnded?.Invoke(v);
                    if (v.Kind == PartyVote.Ready) MarkUnanswered(windowOpen);   // PartyVoteReadyCli::vfunc_3 @0x140a89f70
                }
                Vote = null;
                break;
            }
            case PartyGearScore g: {                        // :1245
                if (Find(g.CharacterId) is { } m) m.User.GearScore = g.Value;
                break;
            }
        }
    }

    /// <summary>UpdateMemberHp (:499): a member's actor in the field changed HP (UIStatusEventListener, :266).</summary>
    public void SetHp(long characterId, int hp, int maxHp) {
        if (Find(characterId) is not { } m) return;
        m.User.Hp = hp; m.User.MaxHp = maxHp;
        MemberUpdated?.Invoke(m, 2);
    }

    /// <summary>UpdateMemberLevel (:511).</summary>
    public void SetLevel(long characterId, short level) {
        if (Find(characterId) is not { } m) return;
        m.User.Level = level;
        MemberUpdated?.Invoke(m, 8);
    }

    /// <summary>RemoveMemberAt (:943): the window's entry goes, the later members' indices shift.</summary>
    void RemoveAt(int index) {
        if (index < 0 || index >= members.Count) return;
        var m = members[index];
        members.RemoveAt(index);
        MemberRemoved?.Invoke(m);
        for (int i = index; i < members.Count; i++) members[i].Index = i;
    }
}
