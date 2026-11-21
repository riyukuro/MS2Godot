using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The party search packets: SendOp/RecvOp PartySearch (CPartyManager::SendMatchPartyMode0/1/2 and OnMatchParty @0x1416310d0,
// partymanager.cpp:1579) and the party send that joins a listed party (Party u8 0x17). Strings are UTF-16 with a u16 length
// (FUN_1401fbd30, unit_1401f9eb0_packetpayloadserializers.cpp:685).

/// <summary>
/// A listed party, CMatchPartyRecruit (commonfiles/matchpartyrecruitschcond.cpp). Read (FUN_140694dc0, :228) and Write
/// (FUN_140694c60) in this order: i64 id, i32 party id, i32 category, i32 kind, title, u8 auto join, i32 members, i32 max
/// members, i64 leader account id, i64 leader character id, leader name, i64 creation time. The client's member names for the
/// two counts and the ids after them are placeholders: the list row shows the first count over the second (FUN_140f8f410), the
/// row's own test compares the own character id with the second id, the register request fills the auto join flag and the
/// max count (FUN_140694b70).
/// </summary>
public sealed class PartyRecruit {
    public long Id;
    public int PartyId;
    public int Category;
    public int Kind;
    public string Title = "";
    public bool AutoJoin;
    public int MemberCount;
    public int MaxMembers;
    public long LeaderAccountId;
    public long LeaderCharacterId;
    public string LeaderName = "";
    public long Created;

    /// <summary>FUN_140695080: no id.</summary>
    public bool IsEmpty => Id == 0;

    public static PartyRecruit Read(ByteReader r) => new() {
        Id = r.ReadLong(), PartyId = r.ReadInt(), Category = r.ReadInt(), Kind = r.ReadInt(), Title = r.ReadUnicodeString(),
        AutoJoin = r.ReadByte() != 0, MemberCount = r.ReadInt(), MaxMembers = r.ReadInt(),
        LeaderAccountId = r.ReadLong(), LeaderCharacterId = r.ReadLong(), LeaderName = r.ReadUnicodeString(), Created = r.ReadLong(),
    };

    public void Write(ByteWriter w) {
        w.WriteLong(Id); w.WriteInt(PartyId); w.WriteInt(Category); w.WriteInt(Kind); w.WriteUnicodeString(Title ?? "");
        w.WriteByte((byte) (AutoJoin ? 1 : 0)); w.WriteInt(MemberCount); w.WriteInt(MaxMembers);
        w.WriteLong(LeaderAccountId); w.WriteLong(LeaderCharacterId); w.WriteUnicodeString(LeaderName ?? ""); w.WriteLong(Created);
    }

    /// <summary>CopyFrom (:270).</summary>
    public PartyRecruit Copy() => (PartyRecruit) MemberwiseClone();
}

/// <summary>
/// The search condition, CMatchPartyRecruitSchCond (matchpartyrecruitschcond.cpp). The search dialog's InitSearchCondition
/// (@0x140f8e670) sets page 1 and sort 0xc; QueueSearch sets the page and a page size of 0.
/// </summary>
public sealed class PartySearchCondition {
    public int FieldId;
    public int Category;
    /// <summary>The sort (the dialog's sort row value: 0xc, 0xb, 0x16 or 0x15).</summary>
    public int Kind = 0xc;
    public string Text = "";
    public int Page = 1;
    public int PageSize;

    public PartySearchRequest ToRequest() => new(FieldId, Category, Kind, Text ?? "", Page, PageSize);
}

/// <summary>SendMatchPartyMode0 (partymanager.cpp:1592): register the own party, u8 0 then FUN_140694280 (manufacture.cpp:94):
/// title, u8 auto join, i32 max members.</summary>
public sealed record PartyRecruitRegisterRequest(string Title, bool AutoJoin, int MaxMembers) : IOutPacket {
    public Outbound Op => Outbound.PartySearch;
    public void Write(ByteWriter w) { w.WriteByte(0); w.WriteUnicodeString(Title ?? ""); w.WriteByte((byte) (AutoJoin ? 1 : 0)); w.WriteInt(MaxMembers); }
}

/// <summary>SendMatchPartyMode1 (partymanager.cpp:1603): remove the own listing, u8 1.</summary>
public sealed record PartyRecruitRemoveRequest : IOutPacket {
    public Outbound Op => Outbound.PartySearch;
    public void Write(ByteWriter w) => w.WriteByte(1);
}

/// <summary>SendMatchPartyMode2 (partymanager.cpp:1614): search, u8 2 then the condition's WriteStream (@0x1406946b0,
/// matchpartyrecruitschcond.cpp:96): i32 field id, i32 category, u8 sort, text, i32 page, i32 page size.</summary>
public sealed record PartySearchRequest(int FieldId, int Category, int Kind, string Text, int Page, int PageSize) : IOutPacket {
    public Outbound Op => Outbound.PartySearch;
    public void Write(ByteWriter w) {
        w.WriteByte(2); w.WriteInt(FieldId); w.WriteInt(Category); w.WriteByte((byte) Kind); w.WriteUnicodeString(Text ?? "");
        w.WriteInt(Page); w.WriteInt(PageSize);
    }
}

/// <summary>SendPartyMode23 (partymanager.cpp:775), from SendPartyInviteFromRecruit (:792): ask to join a listed party, Party
/// u8 0x17 then Unk_PayloadIdentity (FUN_1401fbf70): i32 the listing's party id, the leader's name, i64 the listing's id.</summary>
public sealed record PartyRecruitJoinRequest(int PartyId, string LeaderName, long RecruitId) : IOutPacket {
    public Outbound Op => Outbound.Party;
    public void Write(ByteWriter w) { w.WriteByte(0x17); w.WriteInt(PartyId); w.WriteUnicodeString(LeaderName ?? ""); w.WriteLong(RecruitId); }
}

/// <summary>The party search packet's message (OnMatchParty's mode byte, then the mode's body).</summary>
public abstract record PartySearchMessage(int Mode) {
    public static PartySearchMessage Read(ByteReader r) {
        int mode = r.ReadByte();
        switch (mode) {
            case 0: return new PartyRecruitRegistered(PartyRecruit.Read(r));                  // OnMatchPartyRecruitRegistered (:1641)
            case 1: return new PartyRecruitRemoved(r.ReadLong());                              // OnMatchPartyRecruitRemoved (:1652)
            case 2: {                                                                          // OnMatchPartySearchResults (:1664)
                uint count = (uint) r.ReadInt();
                var list = new List<PartyRecruit>();
                for (uint i = 0; i < count; i++)
                    if (r.ReadByte() != 0) list.Add(PartyRecruit.Read(r));
                return new PartySearchResults(list);
            }
            case 4: { int type = r.ReadByte(); return new PartySearchError(type, r.ReadInt()); }   // OnMatchPartyError (:1689)
            default: return new PartySearchOtherMode(mode);
        }
    }
}
public sealed record PartyRecruitRegistered(PartyRecruit Recruit) : PartySearchMessage(0);
public sealed record PartyRecruitRemoved(long Id) : PartySearchMessage(1);
/// <summary>The valid entries of a results page (an entry whose u8 flag is 0 has no body and is skipped).</summary>
public sealed record PartySearchResults(IReadOnlyList<PartyRecruit> Recruits) : PartySearchMessage(2);
/// <summary>The request's type (0 register, 1 remove or join, 2 search) and the result code.</summary>
public sealed record PartySearchError(int Type, int Result) : PartySearchMessage(4);
public sealed record PartySearchOtherMode(int OtherMode) : PartySearchMessage(OtherMode);

/// <summary>Mode 0x1a, OnPartyRecruitUpdate (partymanager.cpp:1306): u8 has, then the listing when set (empty otherwise).</summary>
public sealed record PartyRecruitUpdate(PartyRecruit Recruit) : PartyMessage(0x1a);

/// <summary>CPartyManager's party search part: the own listing (recruitment), the pending flags and the last results.</summary>
public sealed partial class PartyBook {
    /// <summary>recruitment (GetRecruitment): the own party's listing, empty without one.</summary>
    public PartyRecruit Recruitment { get; private set; } = new();
    /// <summary>recruitPending (IsRecruitPending :1729): a register sent and not answered.</summary>
    public bool RecruitPending { get; private set; }
    /// <summary>searchPending: a first page asked for; its results replace the list.</summary>
    public bool SearchPending { get; private set; }
    readonly List<PartyRecruit> results = new();
    readonly Dictionary<long, PartyRecruit> recruits = new();
    /// <summary>results (FUN_141631d20).</summary>
    public IReadOnlyList<PartyRecruit> Results => results;
    /// <summary>FindRecruit: a listing of the results by id, null when not there.</summary>
    public PartyRecruit FindRecruit(long id) => recruits.TryGetValue(id, out var r) ? r : null;

    /// <summary>The sends (FUN_140710590); the host sets it.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>A system notice by common string id and its arguments (FUN_141275260(id, 1, 1), FUN_140f8ff40, the formatted
    /// PartyRecruitMessage @0x141632340 with the error's type and result).</summary>
    public event Action<int, object[]> Notice;
    /// <summary>A message box of a common string id (FUN_140d73bc0(id, 0, 0, "")).</summary>
    public event Action<int> Box;
    /// <summary>A message box of a common string key (ShowMessageBox(GetStringCommonByKey(key), 0, 0, "")).</summary>
    public event Action<string> BoxByKey;
    /// <summary>The search dialog's FUN_140f8ee00 (the listing, then updateDataTo and forceClickRefresh) when true, else its
    /// updateDataTo alone (RefreshMemberSearchState); raised whether or not the dialog is open, as the host finds it.</summary>
    public event Action<PartyRecruit, bool> RecruitShown;
    /// <summary>The search dialog's FUN_140f8ed40(results, last): a results page came in.</summary>
    public event Action<IReadOnlyList<PartyRecruit>, int> ResultsReceived;
    /// <summary>The register dialog's ClearRegisteredTitle (error 0x6c on a register).</summary>
    public event Action RegisterTitleCleared;
    /// <summary>The search dialog's ClearSearchText (error 0x6c on a search).</summary>
    public event Action SearchTextCleared;

    /// <summary>SendMatchPartyMode0 (:1592): the register send; recruitPending set.</summary>
    public void RegisterRecruit(string title, bool autoJoin, int maxMembers) {
        Send(new PartyRecruitRegisterRequest(title, autoJoin, maxMembers));
        RecruitPending = true;
    }

    /// <summary>SendMatchPartyMode1 (:1603): only with an own listing.</summary>
    public void RemoveRecruit() {
        if (Recruitment.Id == 0) return;
        Send(new PartyRecruitRemoveRequest());
    }

    /// <summary>SendMatchPartyMode2 (:1614): the search; a first page (page 1 or less) marks the next results as a new list.</summary>
    public void Search(PartySearchCondition condition) {
        Send(condition.ToRequest());
        if (condition.Page <= 1) SearchPending = true;
    }

    /// <summary>SendPartyInviteFromRecruit (:792) and SendPartyMode23 (:775): nothing without a leader name; the own name is
    /// notice 0x32e; else the join send.</summary>
    public void JoinRecruit(PartyRecruit recruit, string ownName) {
        if (recruit == null || string.IsNullOrEmpty(recruit.LeaderName)) return;
        if (string.Equals(recruit.LeaderName, ownName, StringComparison.OrdinalIgnoreCase)) { Notice?.Invoke(0x32e, Array.Empty<object>()); return; }
        Send(new PartyRecruitJoinRequest(recruit.PartyId, recruit.LeaderName, recruit.Id));
    }

    /// <summary>OnMatchParty (@0x1416310d0, partymanager.cpp:1579).</summary>
    public void Apply(PartySearchMessage message) {
        switch (message) {
            case PartyRecruitRegistered reg:                    // :1641
                RecruitPending = false;
                SetRecruitment(reg.Recruit);
                break;
            case PartyRecruitRemoved rem:                       // :1652
                if (Recruitment.Id == rem.Id) SetRecruitment(new PartyRecruit());
                break;
            case PartySearchResults res: {                      // :1664
                if (SearchPending) { results.Clear(); recruits.Clear(); SearchPending = false; }
                int last = Math.Max(results.Count - 1, 0);
                foreach (var r in res.Recruits) { results.Add(r); recruits[r.Id] = r; }
                ResultsReceived?.Invoke(results, last);
                break;
            }
            case PartySearchError err:
                ApplyError(err.Type, err.Result);
                break;
        }
    }

    /// <summary>FUN_141631f30 (:1772) then CopyFrom: on a changed id, notice 0xb30 for an old listing, 0xb2e for a new one, and
    /// the search dialog's FUN_140f8ee00 with the new listing.</summary>
    void SetRecruitment(PartyRecruit updated) {
        var old = Recruitment;
        Recruitment = updated.Copy();
        if (old.Id == updated.Id) return;
        if (old.Id != 0) Notice?.Invoke(0xb30, Array.Empty<object>());
        if (updated.Id != 0) Notice?.Invoke(0xb2e, Array.Empty<object>());
        RecruitShown?.Invoke(Recruitment, true);
    }

    /// <summary>ClearRecruitmentAndResetSearchUI (:1555) and ResetRecruitmentAndRefreshSearch (:1569): no listing, no pending
    /// register, the search dialog's updateDataTo.</summary>
    void ClearRecruitment() {
        Recruitment = new PartyRecruit();
        RecruitPending = false;
        RecruitShown?.Invoke(Recruitment, false);
    }

    /// <summary>OnPartyInfo's listing (:1131-1145): read over the kept one (no Clear first); a changed id shows it in the search
    /// dialog (FUN_140f8ee00, only while open: the host's check).</summary>
    void ApplyInfoRecruit(PartyRecruit recruit) {
        long previous = Recruitment.Id;
        if (recruit != null) Recruitment = recruit.Copy();
        if (previous != Recruitment.Id) RecruitShown?.Invoke(Recruitment, true);
    }

    /// <summary>OnMatchPartyError (@0x141631700). A type 0 error ends the pending register. FUN_140f8ff40 is taken as the
    /// system notice of its id.</summary>
    void ApplyError(int type, int result) {
        if (type == 0) RecruitPending = false;
        switch (result) {
            case 99: Notice?.Invoke(0xb46, Array.Empty<object>()); return;
            case 0x65: Box?.Invoke(0xb47); return;
            case 0x6e: Box?.Invoke(0xb48); return;
            case 2: Box?.Invoke(0xb49); return;
            case 0xe: case 0xc: case 0xf: Box?.Invoke(0xb4a); return;
            case 0x6d: Notice?.Invoke(0xb51, Array.Empty<object>()); return;
        }
        if (type == 0) {
            switch (result) {
                case 0x69: Box?.Invoke(0xb4a); return;
                case 0x66: Box?.Invoke(0xb4b); return;
                case 0x68: Box?.Invoke(0xb4c); return;
                case 0x6c: RegisterTitleCleared?.Invoke(); Box?.Invoke(0xb4e); return;
                case 0x6f: Box?.Invoke(0xb50); return;
            }
        } else if (type == 1) {
            if (result == 0x67) { Box?.Invoke(0xb4d); return; }
            if (result == 0x6a) {
                BoxByKey?.Invoke("s_partysearch_err_server_not_find_recruit");
                ClearRecruitment();
                return;
            }
        } else if (type == 2 && result == 0x6c) {
            SearchTextCleared?.Invoke();
            Box?.Invoke(0xb4f);
            return;
        }
        Notice?.Invoke(0xb52, new object[] { type, result });
    }
}
