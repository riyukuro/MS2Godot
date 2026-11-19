using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// The guild state the guild dialog reads, CGuildManager (client/ms2/guildmanager.cpp), with what its type switch
/// FUN_1413b6370 (:301) does on the Guild packets (0x8A) the port reads, and its checked sends (SendOp::Guild 0x4B). The
/// client's UI calls come out as events. The not-in-guild side: the guild id, this character's join requests
/// (m_joinRequestsForChar), the search results and the search propensity (m_468). The in-guild side: the guild info, the
/// join requests sent to the guild (m_joinRequestsForGuild) and the members. STILL-OPEN: the invite (m_invite) and the
/// rest of the in-guild updates.
/// </summary>
public sealed class GuildBook
{
    readonly List<GuildJoinRequest> myRequests = new();     // m_joinRequestsForChar
    readonly List<GuildJoinRequestForGuild> requests = new();   // m_joinRequestsForGuild
    readonly List<GuildSearchEntry> results = new();        // the search results (ClearSearchResults clears them)

    /// <summary>GetStringCommon: the common string of an id.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>GetStringCommonByKey: the common string of a key.</summary>
    public Func<string, string> CommonByKey = _ => "";
    /// <summary>FUN_140710590: the send (SendOp::Guild 0x4B).</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>CBanTextChecker::CheckGuildName (CommonString GetChecker): null when the name passes, else the message. The
    /// client skips the check without a checker; so does a null here.</summary>
    public Func<string, string> CheckName;
    /// <summary>The local player's level (PCBase vfunc_78), or null without a local player.</summary>
    public Func<int?> Level = () => null;
    /// <summary>The local player's name (CGuildManager::GetMyName), or null without a local player.</summary>
    public Func<string> PlayerName = () => null;
    /// <summary>Client::Constants GuildNameLengthMin and GuildCreateMinLevel.</summary>
    public int NameLengthMin = 2, CreateMinLevel;

    /// <summary>ShowSystemNotice(text, 1, 1): a notice of the text, its type and whether it also goes to the chat log.</summary>
    public event Action<string, int, bool> Notice;
    /// <summary>FUN_1412751b0(text, 0xd): a chat log line of the kind.</summary>
    public event Action<string, int> ChatLine;
    /// <summary>PlaySystemSound(key).</summary>
    public event Action<string> Sound;
    /// <summary>ShowMessageBox(text, 0, null, ""): a message box.</summary>
    public event Action<string> Box;
    /// <summary>CloseDialog(0x4e4f): the guild dialog closes.</summary>
    public event Action DialogClose;
    /// <summary>The open guild dialog's FUN_14196d0c0(page): the search result page again (0 keeps the page).</summary>
    public event Action<int> SearchRefresh;
    /// <summary>The open guild dialog's FUN_14196d660: this character's join requests again.</summary>
    public event Action MyRequestsRefresh;
    /// <summary>The open guild dialog's FUN_14196da70: the requests for the guild again.</summary>
    public event Action RequestsForGuildRefresh;
    /// <summary>The open guild dialog's UpdateGuildInfo: the header (the member count) changed.</summary>
    public event Action GuildInfoRefresh;
    /// <summary>The open guild dialog's FUN_141973b20(name): one member's row again, then the master and the player's
    /// rights (the client calls it also when the row's member is gone from the list).</summary>
    public event Action<string> MemberRefresh;
    /// <summary>The open guild dialog's RemoveMember(name).</summary>
    public event Action<string> MemberRemoved;

    /// <summary>GetGuildId: CGuildInfo's first field, 0 outside a guild.</summary>
    public long GuildId { get; private set; }

    /// <summary>CGuildManager's CGuildInfo (the header, members, grades and skills), or null outside a guild.</summary>
    public GuildInfo Info { get; private set; }

    /// <summary>GetGuildName.</summary>
    public string GuildName => Info?.Name ?? "";
    /// <summary>GetCapacity / GetMemberCount.</summary>
    public int Capacity => Info?.MaxMembers ?? 0;
    public int MemberCount => Info?.Members.Count ?? 0;

    /// <summary>GetMembers (@0x1404d02b0): the members in the order of CGuildInfo's name tree (f320, case-insensitive).</summary>
    public IReadOnlyList<GuildMember> Members =>
        Info == null ? Array.Empty<GuildMember>() : Info.Members.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>FindMemberByName.</summary>
    public GuildMember FindMemberByName(string name) =>
        Info?.Members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>GetMasterMember: the member whose id is the master's.</summary>
    public GuildMember MasterMember => Info?.Members.FirstOrDefault(m => m.CharId == Info.MasterCharId);

    /// <summary>GetGrade (@0x1404d0310): the grade of the index, or null.</summary>
    public GuildGrade GetGrade(int index) => Info?.Grades.FirstOrDefault(g => g.Grade == index);

    /// <summary>IsMaster (@0x1404d0510): the member of the name has the master's id.</summary>
    public bool IsMaster(string name) => FindMemberByName(name) is { } m && Info != null && m.CharId == Info.MasterCharId;

    /// <summary>GetMemberPermissions / HasMemberPermission (@0x1404d06a0): the member's grade's permission bits against a mask.</summary>
    public int MemberPermissions(string name) => FindMemberByName(name) is { } m ? GetGrade(m.Grade)?.Permissions ?? 0 : 0;
    public bool HasMemberPermission(string name, int mask) => (MemberPermissions(name) & mask) != 0;

    /// <summary>GetTotalTrophy, GetTotalItemLevel and the character level total (FUN_1404d0d00) over the members.</summary>
    public int TotalTrophy => Info?.Members.Sum(m => m.TrophyTotal) ?? 0;
    public int TotalItemLevel => Info?.Members.Sum(m => m.ItemLevel) ?? 0;
    public int TotalCharLevel => Info?.Members.Sum(m => (int)(ushort)m.Level) ?? 0;

    /// <summary>IsInGuild (@0x1413b6160): the guild id above 0 (unsigned).</summary>
    public bool InGuild => GuildId != 0;

    /// <summary>HasGuildInvite (@0x1413b61a0): not in a guild and m_invite set. STILL-OPEN: the invite (type 5,
    /// OnPacketInviteResult) is not read, so never.</summary>
    public bool HasInvite => false;

    /// <summary>GetSearchPropensity / SetSearchPropensity (guildmanager.h:116): m_468, 0 in the ctor.</summary>
    public int SearchPropensity { get; set; }

    /// <summary>m_joinRequestsForChar: the join requests this character sent, in the order read.</summary>
    public IReadOnlyList<GuildJoinRequest> MyRequests => myRequests;

    /// <summary>m_joinRequestsForGuild (FUN_1413c7f80): the join requests sent to the guild, in the order read.</summary>
    public IReadOnlyList<GuildJoinRequestForGuild> RequestsForGuild => requests;

    /// <summary>The search results of the last type 0x57.</summary>
    public IReadOnlyList<GuildSearchEntry> SearchResults => results;

    /// <summary>FUN_1413c8000 (@0x1413c8000): the request of this character to the guild, or null.</summary>
    public GuildJoinRequest FindRequestByGuild(long guildId) => myRequests.FirstOrDefault(r => r.GuildId == guildId);

    /// <summary>ClearSearchResults: the guild dialog's close clears the results.</summary>
    public void ClearSearchResults() => results.Clear();

    /// <summary>FUN_1413b6370: one Guild packet.</summary>
    public void Apply(GuildMessage m)
    {
        switch (m.Type)
        {
            case 0:
                // OnPacketGuildInfo (:1525): Read, then both request lists are cleared. Read keeps the header, the members,
                // the grades and the skills; a packet only the id of it reads leaves no Info.
                if (InGuild) ResetGuild();
                GuildId = m.GuildId;
                Info = m.Info;
                myRequests.Clear();
                requests.Clear();
                break;
            case 1:
                // OnPacketCreateGuild (@0x1413bd8a0): a result is ShowGuildError(1, result, 0); else the sound, the box
                // FormatStringId(0x389, name) and CloseDialog(0x4e4f).
                if (m.Result != 0) { ShowGuildError(1, m.Result, 0); break; }
                Sound?.Invoke("PC_GuildMake");
                Box?.Invoke(ClientFormat.Format(Common(0x389), m.Name));
                DialogClose?.Invoke();
                break;
            case 0x52:
            case 0x53:
                // FUN_1413c5360 / FUN_1413c5440: not in a guild; a guild id above 0 is the chat line
                // FormatStringId(0x3ce / 0x3cf, name) of kind 0xd, then SendGuildMode86.
                if (InGuild) break;
                if (m.GuildId != 0)                 // unsigned > 0
                {
                    ChatLine?.Invoke(ClientFormat.Format(Common(m.Type == 0x52 ? 0x3ce : 0x3cf), m.Name), 0xd);
                    SendMyRequests();
                }
                break;
            case 0x56:
                // FUN_1413c5840: not in a guild; the requests replaced, then the dialog's FUN_14196d0c0(0) and FUN_14196d660.
                if (InGuild) break;
                myRequests.Clear();
                myRequests.AddRange(m.Requests);
                SearchRefresh?.Invoke(0);
                MyRequestsRefresh?.Invoke();
                break;
            case 0x57:
                // OnPacketSearchResults (@0x1413c59e0): not in a guild; the results replaced; a count of 0 is the box of
                // common 0x3ca; then the dialog's FUN_14196d0c0(1).
                if (InGuild) break;
                results.Clear();
                if (m.SearchCount == 0) Box?.Invoke(Common(0x3ca));
                results.AddRange(m.Results);
                SearchRefresh?.Invoke(1);
                break;
            case 0x30:
            {
                // OnPacketMyJoinRequestRemoved (@0x1413c5f90): not in a guild; the first request of the id is removed;
                // joined closes the dialog, else FUN_14196d0c0(0) and FUN_14196d660.
                if (InGuild) break;
                int i = myRequests.FindIndex(r => r.Id == m.RequestId);
                if (i >= 0) myRequests.RemoveAt(i);
                if (m.Joined) DialogClose?.Invoke();
                else { SearchRefresh?.Invoke(0); MyRequestsRefresh?.Invoke(); }
                break;
            }
            case 2:
                // FUN_1413bda00 (@0x1413bda00): a result is ShowGuildError(2, result, 0); else FUN_1413b5f90 (the guild is
                // gone), the box of common 0x38a and CloseDialog(0x4e4f).
                if (m.Result != 0) { ShowGuildError(2, m.Result, 0); break; }
                ResetGuild();
                Box?.Invoke(Common(0x38a));
                DialogClose?.Invoke();
                break;
            case 7:
                // FUN_1413be350 (@0x1413be350): no body; the guild is gone, the dialog closes and the line 0x395 names the
                // guild it just left.
                if (!InGuild) break;
                string left = GuildName;
                ResetGuild();
                DialogClose?.Invoke();
                ChatLine?.Invoke(ClientFormat.Format(Common(0x395), left), 0xd);
                break;
            case 0x12:
            {
                // OnPacketMemberJoined (@0x1413bfaf0): the member is added (or updated) by name; a full record (mode 3)
                // also draws the dialog again and prints the line 0x3a8 when the notice byte is set.
                if (!InGuild) break;
                if (FindMemberByName(m.Applicant) is { } old) Info.Members.Remove(old);
                if (m.Member is { } member) Info.Members.Add(member);
                if (m.Member?.Mode == 3)
                {
                    if (m.Notify) ChatLine?.Invoke(ClientFormat.Format(Common(0x3a8), m.Name, m.Applicant), 0xd);
                    GuildInfoRefresh?.Invoke();
                    MemberRefresh?.Invoke(m.Applicant);
                }
                break;
            }
            case 0x13:
            {
                // OnPacketMemberLeft (@0x1413bfe50): the member of the name is removed, the line 0x3a6, and the dialog's
                // UpdateGuildInfo and RemoveMember.
                if (!InGuild) break;
                if (FindMemberByName(m.Name) is { } gone) Info.Members.Remove(gone);
                ChatLine?.Invoke(ClientFormat.Format(Common(0x3a6), m.Name), 0xd);
                GuildInfoRefresh?.Invoke();
                MemberRemoved?.Invoke(m.Name);
                break;
            }
            case 0x54:
                // OnPacketJoinRequestAnswer (@0x1413c5520): the line 0x3d0 (accepted) or 0x3d1 (rejected) of the name.
                if (!InGuild) break;
                if (m.GuildId != 0)       // unsigned > 0
                    ChatLine?.Invoke(ClientFormat.Format(Common(m.Accepted ? 0x3d0 : 0x3d1), m.Applicant), 0xd);
                break;
            case 0x55:
                // OnPacketJoinRequestsForGuild (@0x1413c5690): the list replaced, then the dialog's FUN_14196da70.
                if (!InGuild) break;
                requests.Clear();
                requests.AddRange(m.GuildRequests);
                RequestsForGuildRefresh?.Invoke();
                break;
            case 0x2d:
                // OnPacketJoinRequestReceived (@0x1413c6460): the request added, then the dialog's FUN_14196da70.
                if (!InGuild) break;
                requests.Add(m.NewRequest);
                RequestsForGuildRefresh?.Invoke();
                break;
            case 0x2e:
            {
                // OnPacketJoinRequestWithdrawn (@0x1413c6580): the first request of the id is removed, then the dialog's
                // FUN_14196da70.
                if (!InGuild) break;
                int i = requests.FindIndex(r => r.Id == m.RequestId);
                if (i >= 0) requests.RemoveAt(i);
                RequestsForGuildRefresh?.Invoke();
                break;
            }
            case 0x2f:
            {
                // FUN_1413c6180 (@0x1413c6180): the first request of the id is removed, the dialog draws the list again
                // and the line 0x3bd (accepted) or 0x3be (rejected) names the guild and the applicant.
                if (!InGuild) break;
                int i = requests.FindIndex(r => r.Id == m.RequestId);
                if (i >= 0) requests.RemoveAt(i);
                RequestsForGuildRefresh?.Invoke();
                ChatLine?.Invoke(ClientFormat.Format(Common(m.Accepted ? 0x3bd : 0x3be), m.Name, m.Applicant), 0xd);
                break;
            }
        }
    }

    /// <summary>FUN_1413b5f90's Reset: the guild state is gone (the request lists are kept, as the client keeps them).</summary>
    void ResetGuild()
    {
        GuildId = 0;
        Info = null;
    }

    /// <summary>FUN_1413b6d10 (:405): create a guild. False in a guild or with an invite; a name shorter than
    /// GuildNameLengthMin is the notice s_guild_err_invalide_create_name; a failed CheckGuildName is its message; no local
    /// player is false; a level (u16) not above GuildCreateMinLevel is ShowGuildError(1, 0x13, 0); else the send.</summary>
    public bool Create(string name)
    {
        name ??= "";
        if (InGuild || HasInvite) return false;
        if (name.Length < NameLengthMin) { Notice?.Invoke(CommonByKey("s_guild_err_invalide_create_name"), 1, true); return false; }
        if (CheckName?.Invoke(name) is { } message) { Notice?.Invoke(message, 1, true); return false; }
        if (Level() is not { } level) return false;
        if ((ushort)level <= CreateMinLevel) { ShowGuildError(1, 0x13, 0); return false; }
        Send(new GuildCreateRequest(name));
        return true;
    }

    /// <summary>SendGuildMode82 (@0x1413bc4a0): ask to join; 10 requests already is ShowGuildError(0x52, 0x25, 0).</summary>
    public void Join(long guildId)
    {
        if (myRequests.Count >= 10) { ShowGuildError(0x52, 0x25, 0); return; }
        Send(new GuildJoinRequestSend(guildId));
    }

    /// <summary>SendGuildMode83 (@0x1413bc540): cancel a join request.</summary>
    public void CancelJoin(long id) => Send(new GuildJoinCancelRequest(id));

    /// <summary>SendGuildMode84 (@0x1413bc5b0): answer a join request sent to the guild (the id is the request's, the
    /// movie hands it back as its requestSn).</summary>
    public void Respond(long id, bool accept) => Send(new GuildRespondRequest(id, accept));

    /// <summary>SendGuildMode85 (@0x1413bc630): in a guild, ask for the join requests sent to the guild. The client asks
    /// from a member with the invite right when the local player is complete.</summary>
    public void ListRequests()
    {
        if (!InGuild) return;
        Send(new GuildListRequestsRequest());
    }

    /// <summary>HasMemberPermission by the character id (the client asks by the local player's name).</summary>
    public bool HasMemberPermissionOf(long charId, int mask) =>
        Info?.Members.FirstOrDefault(m => m.CharId == charId) is { } me && ((GetGrade(me.Grade)?.Permissions ?? 0) & mask) != 0;

    /// <summary>SendGuildMode7 (@0x1413b75a0): leave the guild. The client sends it only from a member who is not the
    /// master; the master gets the notice of common 0x396 instead.</summary>
    public void Leave()
    {
        if (!InGuild) return;
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return;
        if (IsMaster(myName)) { Notice?.Invoke(Common(0x396), 1, true); return; }
        Send(new GuildLeaveRequest());
    }

    /// <summary>SendGuildMode2 (@0x1413b7020): disband the guild. The client sends it only from the master of a
    /// one-member guild; a guild with members gets the notice of common 0x40e, a member who is not the master nothing.</summary>
    public bool Break()
    {
        if (!InGuild) return false;
        string myName = PlayerName();
        if (string.IsNullOrEmpty(myName)) return false;
        if (MemberCount > 1) { Notice?.Invoke(Common(0x40e), 1, true); return false; }
        if (!IsMaster(myName)) return false;
        Send(new GuildBreakRequest());
        return true;
    }

    /// <summary>SendGuildMode86 (@0x1413bc6a0): not in a guild, ask for this character's join requests.</summary>
    public void SendMyRequests()
    {
        if (InGuild) return;
        Send(new GuildMyRequestsRequest());
    }

    /// <summary>SendGuildMode87 (@0x1413bc710): not in a guild, search by the propensity. Without one, the box of common
    /// 0x3c9 unless <paramref name="anyFocus"/>, which searches with 0x7fffffff.</summary>
    public void Search(bool anyFocus)
    {
        if (InGuild) return;
        int focus = SearchPropensity;
        if (focus == 0)
        {
            if (!anyFocus) { Box?.Invoke(Common(0x3c9)); return; }
            focus = 0x7fffffff;
        }
        Send(new GuildSearchRequest(focus, 1));
    }

    /// <summary>SendGuildMode88 (@0x1413bc860): not in a guild, search by name. Shorter than 2 is the notice
    /// s_guild_err_too_short_to_search; a failed CheckGuildName is its message.</summary>
    public bool SearchByName(string name)
    {
        name ??= "";
        if (InGuild) return false;
        if (name.Length < 2) { Notice?.Invoke(CommonByKey("s_guild_err_too_short_to_search"), 1, true); return false; }
        if (CheckName?.Invoke(name) is { } message) { Notice?.Invoke(message, 1, true); return false; }
        Send(new GuildSearchByNameRequest(name));
        return true;
    }

    /// <summary>ShowGuildError (@0x1413c66f0, :3230): the notice (ShowSystemNotice(text, 1, 1)) of an error code.</summary>
    public void ShowGuildError(int kind, int code, int arg) => Notice?.Invoke(ErrorText(code, arg), 1, true);

    /// <summary>ShowGuildError's text of a code.</summary>
    public string ErrorText(int code, int arg) => code switch
    {
        1 => Common(0x403),
        >= 3 and <= 8 => Common(0x401 + code),
        >= 0xa and <= 0x12 => Common(0x400 + code),
        0x13 => ClientFormat.Format(Common(0x413), CreateMinLevel),   // BlockFormat<int>(0x413, GuildCreateMinLevel)
        >= 0x14 and <= 0x1c => Common(0x400 + code),
        0x1e => Common(0x41d),
        0x20 => Common(0x41e),
        0x21 => Common(0x41f),
        0x29 => Common(0x424),
        >= 0x22 and <= 0x27 => Common(0x3a0 + code - 0x22),
        0x2a => CommonByKey("s_guild_err_not_enough_guild_level"),
        0x2b => CommonByKey("s_guild_err_not_enough_guild_fund"),
        0x2c => CommonByKey("s_guild_err_cannot_use_skill"),
        0x2d => CommonByKey("s_guild_err_too_fast_to_search_by_name"),
        0x2e => Common(0x3c2),
        0x2f => CommonByKey("s_guild_err_isNotVsGameTime"),
        0x30 => ClientFormat.Format(CommonByKey("s_guild_err_requireOnlineUserCount"), arg),   // FUN_14028dfa0<int>
        _ => Common(0x420),
    };
}
