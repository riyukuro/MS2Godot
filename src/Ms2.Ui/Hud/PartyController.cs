using System.Globalization;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the party window, UIPartyDialog (client/ms2/uipartydialog.cpp; UI id 0x4e45), for its movie uipartydialog.
/// The member frames (name, level, job icon, HP bar, leader mark, channel) are the movie's PartyMember clips; this fills their
/// objects from the party (<see cref="PartyBook"/>, CPartyManager) and takes the movie's calls.
/// </summary>
public sealed class PartyController : UiController
{
    public override string Movie => "uipartydialog";

    /// <summary>UIPartyDialog's ctor sets the flag 0 (uipartydialog.cpp:401): vfunc_56, an always-on one.</summary>
    public override bool AlwaysOn => true;
    protected override string EntityMethod => "initMembers";
    public override string TypeName => "PartyDialog";      // GetTypeName @0x141a74d50 (:410)
    /// <summary>UIPartyDialog's ctor (@0x141a74b40, uipartydialog.cpp:397) has no SetTweenEnabled: m_c4 stays 0, so CloseDialog
    /// unloads at once (FUN_141924330, uidialog.cpp:393) and a following OpenDialog loads it again.</summary>
    protected override bool TweenEnabled => false;

    /// <summary>CPartyManager's party.</summary>
    public PartyBook Party = new();
    /// <summary>DAT_143ab2828: the own character id.</summary>
    public long SelfCharacterId;
    /// <summary>The current field's data (GetFieldData); null when there is none.</summary>
    public FieldData Field;
    /// <summary>The local player is dead (PCBase::IsDead, the live MyCharacter.Dead); unset: false.</summary>
    public Func<bool> LocalPlayerDead = () => false;
    /// <summary>The popup menu (FindPopupMenu(0x4e39)) is open for a party slot (not IsClosed, GetInfoString "partySlot").</summary>
    public Func<bool> SlotMenuOpen = () => false;
    /// <summary>CStringTable::FUN_141811980(3, id): the map name.</summary>
    public Func<int, string> MapName = _ => "";
    /// <summary>CStringTable::FUN_141811980(0x14, id): the dungeon name.</summary>
    public Func<int, string> DungeonName = _ => "";

    /// <summary>onPopupMenu (vfunc_28, :640): the member menu at (x, y) for a character (FUN_141a77760, :794): the own one
    /// opens the PC menu, another member PopUpMenuFromPartyMember; the host opens <see cref="PopupMenuController"/>.</summary>
    public event Action<float, float, long> MemberMenu;
    /// <summary>onCameraClick (:655): MyPC::StartObserving the member's PC. Raised only.</summary>
    public event Action<long> ObserveMember;
    /// <summary>onPartyRecall (:668): FUN_141a77eb0's checks, then the recall scroll or the merat box. Raised only.</summary>
    public event Action Recall;
    /// <summary>FUN_140710590: a packet to the game server.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>The message box queue's show (FUN_14151f150).</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>ShowSystemNotice(text, 1, 1): a center notice (the host shows or logs it).</summary>
    public event Action<string> SystemNotice;
    /// <summary>CBanTextChecker::CheckClubName (commonstringtable.cpp:988): null when the name passes, else the message.</summary>
    public Func<string, string> ClubNameError = _ => null;
    /// <summary>CClubClientManager::IsClubLimitReached (clubcli.cpp:577): the clubs and club invites number 3 or more.
    /// STILL-OPEN: the club packets are not read, so never.</summary>
    public Func<bool> ClubLimitReached = () => false;

    /// <summary>GetServerTime: the server's unix seconds (the kick vote's time left); unset: the local clock.</summary>
    public Func<long> ServerNow = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>GetTickCountMs2: the client tick in milliseconds (the deadline and the refresh interval).</summary>
    public Func<long> NowMs = () => Environment.TickCount64;

    /// <summary>m_deadline (SetDeadline @0x141a75a00): the tick after which the ready marks are cleared (0 none).</summary>
    public long Deadline { get; private set; }
    public void SetDeadline(long tick) => Deadline = tick;
    long lastTick;                                      // m_timer: Unk_IntervalTimer's last firing (0 never)

    bool open;
    public bool IsOpen => open;

    /// <summary>The account session state test FUN_140930b40 (unit_14092f420_accountsessionstate.cpp:785: DAT_143ab2878 == 2 or
    /// DAT_143ab287c == 8). STILL-OPEN: no feed for those globals, so false.</summary>
    static bool SessionHidesDungeon => false;

    /// <summary>OnLoadComplete @0x141a75a80 (:560): the bottom buttons and the dungeon name, the vote page, the click handlers,
    /// RefreshAll. Its 0x53 handler is <see cref="OnPartyMessage"/> (the host calls it); the 0x100 one touches only the give
    /// up button (not fed).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        open = true;
        VotePageInit();
        Click("m_createClub", OnCreateClubClick);
        Click("m_enterDungeon", OnEnterDungeonClick);
        Click("m_giveUpDungeon", OnGiveUpDungeonClick);
        RefreshAll();
    }

    void Click(string name, Action a)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("party window: " + name); return; }
        Func<object[], object> f = _ => { a(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);    // ASButton::AddClickHandler
    }

    public override void Close()
    {
        open = false;
        base.Close();
    }

    /// <summary>OnEnterDungeonClick (:859): CPartyManager::SendRoomDungeon(0).</summary>
    public void OnEnterDungeonClick() => Send(new RoomDungeonEnterRequest(0));

    /// <summary>OnGiveUpDungeonClick (:888): FUN_140d74090, a yes / no box of s_party_dungeon_giveup answered by
    /// SendDungeonReward (:783): yes sends DungeonReward 5; the box closes either way.</summary>
    public void OnGiveUpDungeonClick() =>
        ShowBox(MessageBoxQueue.CreateWithCallback(Common("s_party_dungeon_giveup"), 1, (button, _) =>
        {
            if (button == 0) Send(new DungeonGiveUpRequest());
            return true;
        }));

    /// <summary>
    /// OnCreateClubClick (:865): the leader only. With the club limit reached the notice BlockFormat(0x381, 3)
    /// (s_club_err_full_club, commonstringtable.cpp:2498); else a type 10 box of s_party_create_club formatted with
    /// ClubNameLengthMax / 2 and ClubNameLengthMax, its input (SetInput(2, "", max, 0, 0)) answered by OnCreateClubMsgBox.
    /// </summary>
    public void OnCreateClubClick()
    {
        if (!Party.IsLeader(SelfCharacterId)) return;
        if (ClubLimitReached())
        {
            SystemNotice?.Invoke(ClientFormat.Format(Common("s_club_err_full_club"), 3));
            return;
        }
        int max = Model?.Tables.ClubNameLengthMax ?? 12;
        var box = MessageBoxQueue.Create(10, ClientFormat.Format(Common("s_party_create_club"), max / 2, max), null, "");
        box.SetInput(2, "", max, 0, 0);
        box.Callback = OnCreateClubAnswer;
        ShowBox(box);                                                   // FUN_140d73c60
    }

    /// <summary>OnCreateClubMsgBox (:770): not OK closes; the input (FUN_141a02d50, the clicked tag) trimmed of ","; empty
    /// keeps the box open; else FUN_140b15c10 and close.</summary>
    bool OnCreateClubAnswer(int button, MessageBoxData box)
    {
        if (button != 0) return true;
        string name = (box.ClickedTag ?? "").Trim(',');
        if (name.Length == 0) return false;
        CreateClub(name);
        return true;
    }

    /// <summary>
    /// CClubClientManager::FUN_140b15c10 (clubcli.cpp:890): the club limit (notice 0x381), the name length within
    /// ClubNameLengthMin..Max (silent), no space (FUN_141275260(0x386) s_club_err_clubname_has_blank), in a party and its
    /// leader (0x36c s_club_err_create_no_party: commonstringtable.cpp:2477, :2503), the ban-text check (its message);
    /// then Club 1 with the name.
    /// </summary>
    public bool CreateClub(string name)
    {
        name ??= "";
        if (ClubLimitReached())
        {
            SystemNotice?.Invoke(ClientFormat.Format(Common("s_club_err_full_club"), 3));
            return false;
        }
        int min = Model?.Tables.ClubNameLengthMin ?? 2, max = Model?.Tables.ClubNameLengthMax ?? 12;
        if (name.Length < min || name.Length > max) return false;
        if (name.Contains(' ')) { SystemNotice?.Invoke(Common("s_club_err_clubname_has_blank")); return false; }
        if (!Party.IsInParty || !Party.IsLeader(SelfCharacterId)) { SystemNotice?.Invoke(Common("s_club_err_create_no_party")); return false; }
        if (ClubNameError(name) is { } error) { SystemNotice?.Invoke(error); return false; }
        Send(new ClubCreateRequest(name));
        return true;
    }

    /// <summary>RefreshAll @0x141a792b0 (:1087): the buttons, then initMembers with each member's object (flags 0x1ff).</summary>
    public void RefreshAll()
    {
        if (Host?.Entity == null) return;
        UpdateEnterDungeonButton();
        UpdateDungeonName();
        UpdateRecallButton();
        UpdateCreateClubButton();
        UpdateGiveUpDungeonButton();
        UpdateVotePage();
        ArrangeBottomButton();
        var list = new List<object>();
        foreach (var m in Party.Members) list.Add(MemberObject(m, 0x1ff));
        Host.Invoke(Host.Entity, "initMembers", list);
    }

    /// <summary>UpdateMember @0x141a74d50 (:416): updateMember(charSN, the flagged members).</summary>
    public void UpdateMember(PartyMember m, int flags)
    {
        if (Host?.Entity == null || m == null) return;
        Host.Invoke(Host.Entity, "updateMember", Id(m.CharacterId), MemberObject(m, flags));
    }

    /// <summary>RefreshAllMembers @0x141a74ea0 (:426): each member with flags 0x180 (channel and observe), in a party only.</summary>
    public void RefreshAllMembers()
    {
        if (!Party.IsInParty) return;
        foreach (var m in Party.Members) UpdateMember(m, 0x180);
    }

    /// <summary>AddMember @0x141a752a0 (:456).</summary>
    public void AddMember(PartyMember m)
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "addMember", MemberObject(m, 0x1ff));
    }

    /// <summary>RemoveMemberEntry @0x141a753d0 (:470).</summary>
    public void RemoveMember(PartyMember m)
    {
        if (Host?.Entity == null || m == null) return;
        Host.Invoke(Host.Entity, "removeMember", Id(m.CharacterId));
    }

    /// <summary>ChangeChief @0x141a74f50 (:441): changeChief(old, new) ("" for none); with feature 0x56 the club button and
    /// the bottom row again.</summary>
    public void ChangeChief(PartyMember old, PartyMember now)
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Host.Entity, "changeChief", old != null ? Id(old.CharacterId) : "", now != null ? Id(now.CharacterId) : "");
        if (Model.IsFeatureEnabled(0x56))
        {
            UpdateCreateClubButton();
            ArrangeBottomButton();
        }
    }

    static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);    // FUN_140719530 / FUN_140719630: decimal text

    /// <summary>
    /// FillMemberObject @0x141a784b0 (:975): the members <paramref name="flags"/> asks for. 8 level, 4 job, 0x20 name, 2 HP
    /// (hp, maxhp, oriMaxhp = max HP, tombType), 0x10 chief, 1 state and readyState (tombType 0 when offline), 0x80 the channel
    /// (ch "diff" when the member is elsewhere, chName), 0x100 observeMode; with any of 0x1ff the charSN (and returnUser
    /// when the name tag option FUN_1406c7610()->m_30 is on: STILL-OPEN, left out).
    /// </summary>
    public Dictionary<string, object> MemberObject(PartyMember m, int flags)
    {
        var u = m.User;
        var o = new Dictionary<string, object>();
        if ((flags & 8) != 0) o["level"] = (int)u.Level;
        if ((flags & 4) != 0) o["job"] = u.Job;
        if ((flags & 0x20) != 0) o["name"] = u.Name;
        if ((flags & 2) != 0)
        {
            o["hp"] = u.Hp;
            o["maxhp"] = u.MaxHp;
            o["oriMaxhp"] = u.MaxHp;
            o["tombType"] = u.TombType;
        }
        if ((flags & 0x10) != 0) o["chief"] = m.Leader ? 1 : 0;
        if ((flags & 1) != 0)
        {
            if (m.Offline) o["tombType"] = 0;      // Unk_PdTombNone
            o["state"] = m.State;
            o["readyState"] = m.ShownReadyState;
        }
        if ((flags & 0x80) != 0)
        {
            string chName = u.Channel == 0 ? Common("s_party_ch_dungeon") : Format(Common("s_party_ch"), u.Channel);
            string diff = "";
            if (Party.Find(SelfCharacterId) is { } mine)
            {
                var me = mine.User;
                if (me.Channel != u.Channel) diff = "diff";
                else if (me.Channel == 0 ? me.MapInstanceId != u.MapInstanceId : me.MapId != u.MapId) diff = "diff";
            }
            o["ch"] = diff;
            o["chName"] = chName;
        }
        if ((flags & 0x100) != 0) o["observeMode"] = ObserveMode(m);
        if ((flags & 0x1ff) != 0) o["charSN"] = Id(u.CharacterId);
        return o;
    }

    /// <summary>FillMemberObject's flag 0x100 (:1048): the member's role when the own PC is dead in a split field that checks
    /// party observe and the member is in the same channel, map and instance; else 0. The role is not fed (0).</summary>
    int ObserveMode(PartyMember m)
    {
        if (Party.Find(SelfCharacterId) is not { } mine || m.CharacterId == SelfCharacterId || !LocalPlayerDead()) return 0;
        if (Field == null || !Field.GlobalSplit || !Field.CheckPartyObserve) return 0;
        var me = mine.User; var u = m.User;
        if (u.Channel != me.Channel || u.MapId != me.MapId || u.MapInstanceId != me.MapInstanceId) return 0;
        return 0;   // pMember->role: STILL-OPEN, set only by CPartyManager::SetLeaderByName (partymanager.cpp:681, role 1 / 2) from MyPC::StartObserving (mypc.cpp:3698), the observe camera, which is not ported; no party packet carries it
    }

    /// <summary>ASPartyVotePage::OnInit @0x141a73a70 (:310) on m_partyVote: the page hidden, its yes / no buttons' clicks.</summary>
    void VotePageInit()
    {
        if (Host.Child("m_partyVote") is not { } page) { Host.RT.NoteMissing("party window: m_partyVote"); return; }
        Host.SetVisible(page, false);
        foreach (var (name, yes) in new[] { ("m_yesButton", true), ("m_noButton", false) })
            if (Host.FindNamed(name, page as Avm2.Flash.AsDisplayObject) is { } b)
            {
                Func<object[], object> f = _ => { AnswerVote(yes); return null; };
                Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
            }
    }

    /// <summary>The vote page's yes / no (OnYesButtonClick / OnNoButtonClick @0x141a74a60 / @0x141a74a90): SendVote
    /// @0x141a74ac0, PartyVoteCli::SendParty of the running vote; nothing without one.</summary>
    public event Action<bool> Vote;

    /// <summary>The yes / no buttons' click (OnYesButtonClick / OnNoButtonClick); scripted runs call it for the button.</summary>
    public void AnswerVote(bool yes) { Vote?.Invoke(yes); SendVote(yes); }

    void SendVote(bool yes)
    {
        if (Party.Vote is not { } v) return;
        Send(new PartyVoteAnswer(v.Id, yes));
    }

    /// <summary>
    /// ASPartyVotePage::FUN_141a74760 (:352): without a vote, or one the page does not show (vfunc_5: only the kick vote),
    /// the page is hidden; else shown with the vote's title (vfunc_4, as HTML), the yes and no counts, the yes / no buttons
    /// until the own character voted and the voted text after.
    /// </summary>
    void UpdateVotePage()
    {
        if (Host.Child("m_partyVote") is not { } page) return;
        var v = Party.Vote;
        if (v == null || !v.ShowsPage)
        {
            Host.SetVisible(page, false);
            return;
        }
        Host.SetVisible(page, true);
        var at = page as Avm2.Flash.AsDisplayObject;
        if (Host.FindNamed("m_title", at) is { } title) Host.Set(title, "htmlText", VoteTitle(Model.Tables, v, ServerNow()));
        if (Host.FindNamed("m_yesCount", at) is { } yes) Host.Set(yes, "text", Id(v.Yes.Count));
        if (Host.FindNamed("m_noCount", at) is { } no) Host.Set(no, "text", Id(v.No.Count));
        bool voted = v.HasVoted(SelfCharacterId);
        foreach (var (name, shown) in new[] { ("m_yesButton", !voted), ("m_noButton", !voted), ("m_votedText", voted) })
            if (Host.FindNamed(name, at) is { } o) Host.SetVisible(o, shown);
    }

    /// <summary>PartyVoteKickUserCli::vfunc_4 @0x140a89a00: s_party_vote_title_kick_user of the target's name, the reason
    /// (the votes the kick needs) and the time left (GetRemainingTimeText @0x140a89720); "" for the other votes (the base).</summary>
    public static string VoteTitle(GameTables tables, PartyVote v, long serverNow)
    {
        if (v.Kind != PartyVote.KickUser) return "";
        return Format(tables.Common("s_party_vote_title_kick_user") ?? "", v.Name, (int)v.Reason, RemainingTimeText(tables, v.Expires, serverNow));
    }

    /// <summary>GetRemainingTimeText @0x140a89720: the end (no earlier than now) less now, as FormatMinutesSeconds
    /// @0x140b8fbc0, s_common_min_sec_2 of the span's minutes and seconds parts.</summary>
    public static string RemainingTimeText(GameTables tables, long expires, long serverNow)
    {
        long span = Math.Max(expires, serverNow) - serverNow;
        return Format(tables.Common("s_common_min_sec_2") ?? "", (int)(span / 60 % 60), (int)(span % 60));
    }

    /// <summary>FUN_141a759b0: the vote page again.</summary>
    public void RefreshVote() { if (Host?.Entity != null) UpdateVotePage(); }

    /// <summary>
    /// OnPartyPacket (:822), the window's own 0x53 handler: 0x19 the enter button and the dungeon name, 0x11 the give up
    /// button, 0x30 and 0x31 (a vote answer, the vote's end) the vote page; each then the bottom row.
    /// </summary>
    public void OnPartyMessage(PartyMessage message)
    {
        if (Host?.Entity == null) return;
        switch (message.Mode)
        {
            case 0x19: UpdateEnterDungeonButton(); UpdateDungeonName(); ArrangeBottomButton(); break;
            case 0x11: UpdateGiveUpDungeonButton(); ArrangeBottomButton(); break;
            case 0x30: case 0x31: RefreshVote(); ArrangeBottomButton(); break;
        }
    }

    /// <summary>vfunc_15 @0x141a769d0: once a second (Unk_IntervalTimer, 1000 ms) the vote page again (its time left), and
    /// past the deadline every member's ready mark cleared (CPartyManager::SetAllMembersState(0), which also clears the
    /// deadline).</summary>
    public void Process()
    {
        if (Host?.Entity == null) return;
        long now = NowMs();
        if (lastTick != 0 && now - lastTick < 1000) return;
        lastTick = now;
        if (Party.Vote != null) UpdateVotePage();
        if (Deadline != 0 && now > Deadline)
        {
            Deadline = 0;
            Party.SetAllMembersState(0);
        }
    }

    /// <summary>UpdateEnterDungeonButton @0x141a794e0 (:1115): shown while matchmaking.</summary>
    void UpdateEnterDungeonButton() => Show("m_enterDungeon", !SessionHidesDungeon && Party.Matchmaking);

    /// <summary>UpdateDungeonName @0x141a79570 (:1125): setDungeonName(the dungeon's name) while the party has a dungeon.</summary>
    void UpdateDungeonName()
    {
        bool shown = !SessionHidesDungeon && Party.DungeonId != 0;
        Show("m_dungeonName", shown);
        if (shown) Host.Invoke(Host.Entity, "setDungeonName", DungeonName(Party.DungeonId) ?? "");
    }

    /// <summary>UpdateRecallButton @0x141a796b0 (:1142): shown when the field allows recalling other users.</summary>
    void UpdateRecallButton() => Show("m_recall", Field != null && !Field.RecallOtherUserProhibit);

    /// <summary>UpdateCreateClubButton @0x141a79770 (:1152): with feature 0x56, shown to the leader.</summary>
    void UpdateCreateClubButton() => Show("m_createClub", Model.IsFeatureEnabled(0x56) && Party.IsLeader(SelfCharacterId));

    /// <summary>UpdateGiveUpDungeonButton @0x141a79840 (:1168): shown to the leader in a dungeon stage with its flag set.
    /// STILL-OPEN: no dungeon stage feed, so hidden.</summary>
    void UpdateGiveUpDungeonButton() => Show("m_giveUpDungeon", false);

    /// <summary>ArrangeBottomButton @0x141a75a20 (:552).</summary>
    public void ArrangeBottomButton() { if (Host?.Entity != null) Host.Invoke(Host.Entity, "arrangeBottomButton"); }

    /// <summary>ASDisplayObject vfunc_3 / vfunc_4: SetVisible.</summary>
    void Show(string name, bool visible)
    {
        if (Host.Child(name) is { } c) Host.SetVisible(c, visible);
    }

    /// <summary>vfunc_28 @0x141a76a80 (:635).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Is(name, "onPopupMenu"))
        {
            if (args.Length >= 3 && long.TryParse(Avm2.Rt.ToString(args[2]), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                MemberMenu?.Invoke((float)Avm2.Rt.ToNumber(args[0]), (float)Avm2.Rt.ToNumber(args[1]), id);
            return true;
        }
        if (Is(name, "onCameraClick"))
        {
            if (args.Length >= 1 && long.TryParse(Avm2.Rt.ToString(args[0]), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                ObserveMember?.Invoke(id);
            return true;
        }
        if (Is(name, "onPartyRecall")) { Recall?.Invoke(); return false; }   // the client's branch falls out with 0
        return false;
    }

    /// <summary>
    /// vfunc_45 @0x141a76ff0 (:692) "getTooltip"(charSN): none while the popup menu is open for a party slot; offline
    /// s_word_offline, else s_tooltip_party of s_word_online and the place (s_tooltip_party_ch of the channel and a space,
    /// none in a dungeon, then the map name); align 4, offset (-30, -1).
    /// </summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (name != "getTooltip") return base.GetUserData(name, args);
        if (SlotMenuOpen()) return null;
        if (args.Length < 1 || !long.TryParse(Avm2.Rt.ToString(args[0]), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) || id <= 0)
            return null;
        if (Party.Find(id) is not { } m) return null;
        string text;
        if (m.Offline) text = Common("s_word_offline");
        else
        {
            string place = m.User.Channel == 0 ? "" : Format(Common("s_tooltip_party_ch"), m.User.Channel) + " ";
            place += MapName(m.User.MapId) ?? "";
            text = Format(Common("s_tooltip_party"), Common("s_word_online"), place);
        }
        var tip = new AsTooltip();
        if (!tip.ApplyTextTooltip(text)) return null;
        tip.SetTooltipAlign(4);
        tip["offsetX"] = -30;
        tip["offsetY"] = -1;
        return tip;
    }

    string Common(string key) => Model.Tables.Common(key) ?? "";

    /// <summary>StringFormat: "{n}" and "{n:spec}" (e.g. "{0:02d}") replaced by the n-th argument.</summary>
    static string Format(string f, params object[] args) => ClientFormat.Format(f, args);

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
