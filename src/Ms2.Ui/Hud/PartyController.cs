using System.Globalization;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the party window, UIPartyDialog (client/ms2/uipartydialog.cpp; UI id 0x4e45), for its movie uipartydialog.
/// The member frames (name, level, job icon, HP bar, leader mark, channel) are the movie's PartyMember clips; this fills their
/// objects from the party (<see cref="PartyBook"/>, CPartyManager) and takes the movie's calls.
/// </summary>
public sealed class PartyController : UiController
{
    public override string Movie => "uipartydialog";
    protected override string EntityMethod => "initMembers";
    public override string TypeName => "PartyDialog";      // GetTypeName @0x141a74d50 (:410)

    /// <summary>CPartyManager's party.</summary>
    public PartyBook Party = new();
    /// <summary>DAT_143ab2828: the own character id.</summary>
    public long SelfCharacterId;
    /// <summary>The current field's data (GetFieldData); null when there is none.</summary>
    public FieldData Field;
    /// <summary>The local player is dead (PCBase::IsDead); no feed yet: false.</summary>
    public Func<bool> LocalPlayerDead = () => false;
    /// <summary>CStringTable::FUN_141811980(3, id): the map name.</summary>
    public Func<int, string> MapName = _ => "";
    /// <summary>CStringTable::FUN_141811980(0x14, id): the dungeon name.</summary>
    public Func<int, string> DungeonName = _ => "";

    /// <summary>onPopupMenu (vfunc_28, :640): the member menu at (x, y) for a character (FUN_141a77760, :794): the own one
    /// opens the PC menu, another member PopUpMenuFromPartyMember (invite, leave, kick, leader change). The popup menu is not
    /// ported: raised only.</summary>
    public event Action<float, float, long> MemberMenu;
    /// <summary>onCameraClick (:655): MyPC::StartObserving the member's PC. Raised only.</summary>
    public event Action<long> ObserveMember;
    /// <summary>onPartyRecall (:668): FUN_141a77eb0's checks, then the recall scroll or the merat box. Raised only.</summary>
    public event Action Recall;
    /// <summary>m_enterDungeon's click (OnEnterDungeonClick, :859): SendRoomDungeon(0). Raised only.</summary>
    public event Action EnterDungeon;
    /// <summary>m_createClub's click (OnCreateClubClick, :865). Raised only.</summary>
    public event Action CreateClub;
    /// <summary>m_giveUpDungeon's click (OnGiveUpDungeonClick, :888). Raised only.</summary>
    public event Action GiveUpDungeon;

    bool open;
    public bool IsOpen => open;

    /// <summary>The account session state test FUN_140930b40 (unit_14092f420_accountsessionstate.cpp:785: DAT_143ab2878 == 2 or
    /// DAT_143ab287c == 8). STILL-OPEN: no feed for those globals, so false.</summary>
    static bool SessionHidesDungeon => false;

    /// <summary>OnLoadComplete @0x141a75a80 (:560): the bottom buttons and the dungeon name, the vote page (not ported), the
    /// click handlers, RefreshAll. The packet handlers 0x53 and 0x100 it registers only touch the dungeon buttons.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        open = true;
        VotePageInit();
        Click("m_createClub", () => CreateClub?.Invoke());
        Click("m_enterDungeon", () => EnterDungeon?.Invoke());
        Click("m_giveUpDungeon", () => GiveUpDungeon?.Invoke());
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

    static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);    // FUN_140719530: decimal text

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
        return 0;   // pMember->role: not in the party packets read here (STILL-OPEN)
    }

    /// <summary>ASPartyVotePage::OnInit @0x141a73a70 (:310) on m_partyVote: the page hidden, its yes / no buttons' clicks.</summary>
    void VotePageInit()
    {
        if (Host.Child("m_partyVote") is not { } page) { Host.RT.NoteMissing("party window: m_partyVote"); return; }
        Host.SetVisible(page, false);
        foreach (var (name, yes) in new[] { ("m_yesButton", true), ("m_noButton", false) })
            if (Host.FindNamed(name, page as Avm2.Flash.AsDisplayObject) is { } b)
            {
                Func<object[], object> f = _ => { Vote?.Invoke(yes); return null; };
                Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
            }
    }

    /// <summary>The vote page's yes / no (OnYesButtonClick / OnNoButtonClick, :374: PartyVoteCli::SendParty). Raised only.</summary>
    public event Action<bool> Vote;

    /// <summary>ASPartyVotePage::FUN_141a74760 (:352), FUN_141a759b0: the page shows the running vote, hidden without one.
    /// STILL-OPEN: the vote packets (0x2f-0x31, PartyVoteCli) are not read, so there is never a vote.</summary>
    void UpdateVotePage()
    {
        if (Host.Child("m_partyVote") is { } page) Host.SetVisible(page, false);
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
    void ArrangeBottomButton() => Host.Invoke(Host.Entity, "arrangeBottomButton");

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
    /// vfunc_45 @0x141a76ff0 (:692) "getTooltip"(charSN): none while the member popup menu is open (not ported); offline
    /// s_word_offline, else s_tooltip_party of s_word_online and the place (s_tooltip_party_ch of the channel and a space,
    /// none in a dungeon, then the map name); align 4, offset (-30, -1).
    /// </summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (name != "getTooltip") return base.GetUserData(name, args);
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

    /// <summary>StringFormat: "{n}" replaced by the n-th argument.</summary>
    static string Format(string f, params object[] args)
    {
        for (int i = 0; i < args.Length; i++) f = f.Replace("{" + i + "}", Convert.ToString(args[i], CultureInfo.InvariantCulture));
        return f;
    }

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
