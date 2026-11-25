using System.Text;
using Ms2.Avm2;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The data the room panel (UIDungeonRoomInfo) asks other client managers for and the port has no source of yet. Each hook is optional:
/// a hook left null means the line, colour or state it feeds is not shown, never a guessed default (STILL-OPEN in each summary).
/// </summary>
public sealed class DungeonRoomHooks
{
    /// <summary>The string table 0x15 text of a room id (the description in m_dungeonDesc). STILL-OPEN: the file is not mapped in GameTables.</summary>
    public Func<uint, string> RoomDescription;
    /// <summary>The local player's name (vfunc_77) and character id (the party leader test).</summary>
    public Func<string> LocalName;
    public Func<long> SelfCharacterId;
    /// <summary>pPlayer.IsVip (RefreshLimits, the VIP line).</summary>
    public Func<bool?> IsVip;
    /// <summary>FUN_140970020: the local player has the additional effect (RefreshLimits). STILL-OPEN.</summary>
    public Func<uint, bool?> AdditionalEffectActive;
    /// <summary>FUN_1415272b0: the player wears a recommended weapon (RefreshLimits). STILL-OPEN.</summary>
    public Func<bool?> RecommendWeaponEquipped;
    /// <summary>GetEventTimeLimitDungeonRanges(roomId): (hour, minute, hour, minute) ranges the room may be entered. STILL-OPEN.</summary>
    public Func<uint, IReadOnlyList<(int StartHour, int StartMinute, int EndHour, int EndMinute)>> TimeLimitRanges;
    /// <summary>GetDungeonOpenPeriod().GetPeriod(tag): the tag's period is open. STILL-OPEN (the blob is unrecovered).</summary>
    public Func<string, bool?> OpenPeriodOpen;
    /// <summary>The list text of FormatDayOfWeekList / FormatYearMonthDayHour / GetDayOfWeekName / FormatMonthDayWeekday. STILL-OPEN: the helper bodies.</summary>
    public Func<byte, string> DayOfWeekList;
    public Func<long, string> YearMonthDayHour;
    public Func<int, string> DayOfWeekName;
    /// <summary>CGameRule.GetDungeonExtraRewardCount(roomId): the extra reward event's count (also <see cref="DungeonRecordBook.ExtraRewardEventCount"/>).</summary>
    public Func<uint, uint?> ExtraRewardEventCount;
    /// <summary>GetDungeonEnterState(roomId) of the local player (the clear state 0..18, 11 and up are limits); GetMemberClearCount of a party member. STILL-OPEN.</summary>
    public Func<uint, int?> ClearState;
    public Func<long, uint, int?> MemberClearState;
    /// <summary>FUN_140d9f5d0(3) on the player's character, and FUN_140930b40 (the enter block). STILL-OPEN.</summary>
    public Func<bool?> SoloAllowed;
    public Func<bool> EnterBlocked;
    /// <summary>The achievement of limitAchieveID: completed, and its name (AchieveTexts.Name).</summary>
    public Func<int, bool?> AchieveCompleted;
    public Func<int, string> AchieveName;
    /// <summary>DungeonMissionDataTable.GetMission(id): the mission's name and top score. STILL-OPEN: no table in the repo.</summary>
    public Func<uint, (string Name, int MaxScore)?> Mission;
    /// <summary>FUN_140719b50: the score text. STILL-OPEN: the formatter is unidentified.</summary>
    public Func<int, string> ScoreText;
    /// <summary>The helper bonus of the helper event (CGameEventValueManager, > 0 shows m_dungeonHelpEvent). STILL-OPEN.</summary>
    public Func<int> HelperBonus;
    /// <summary>The banner loader (ASLoader on m_dungeonBanner): loads the image of room.dungeonBanner. STILL-OPEN: the image loader.</summary>
    public Action<object, string> LoadBanner;
    /// <summary>The achievement dialog (UI 0x4e73, FUN_1418aa9b0(id)) and the VIP dialog (0x4f1b).</summary>
    public Action<int> OpenAchievement;
    public Action OpenVip;
}

/// <summary>
/// UIDungeonRoomInfo (client/ms2/uidungeonroomdialog.cpp:535-1827, ctor @0x1412a99a0): the panel on the movie's m_dungeonInfo that shows
/// the selected room: banner, title, description, enter buttons, party status, limits, rewards and the rank panels. Its Refresh runs in
/// the client's order. STILL-OPEN: RefreshRewardPreview's event boxes (CGameRule::FUN_14047b300), its dev key fallback, the reward items of a
/// round group (RewardContentsTable needs RewardContentItemTable and a random roll; <see cref="RefreshRewardPreview"/>) and the transfer
/// count; every <see cref="DungeonRoomHooks"/> member left null; the party member frame labels ("green", "blue", "red") and the sub panels'
/// children of tabs 3 and 4 are read from the movie by their names and checked live.
/// </summary>
public sealed class DungeonRoomInfoPanel
{
    public const int SubCount = 5;
    public const int PartyMemberCount = 10;
    /// <summary>nRewardItems (:578).</summary>
    public const int RewardItemCount = 0x1c;

    readonly DungeonRoomDialogController owner;
    readonly object info;
    readonly object[] subTabs = new object[SubCount];
    readonly object[] subPanels = new object[SubCount];
    readonly object[] partyMembers = new object[PartyMemberCount];
    readonly object contents;
    /// <summary>m_rewardItems: m_rewardItem0..0x1b of sub panel 2 (ctor :578-579); a name the movie lacks stays null.</summary>
    readonly object[] rewardItems = new object[RewardItemCount];
    ItemSlotBinder slotBinder;
    readonly object scroll;
    readonly object title, desc, limitText, requireCondition, cooldownIcon, favorite, enterStatus, reset, rewardText1, rewardText2,
        addExtra, rewardIcon1, rewardIcon2, enterSolo, enterParty, partySearch, clearCount, changeExpert, findHelperStatus, helpCoinInfo,
        findHelper, findMember, helpEvent, searching, partyMembersClip, banner;
    readonly RankPanel rank;
    readonly SeasonRankReward season;
    readonly LimitTextGroup limits;
    readonly Dictionary<string, string> tooltips = new();
    string strBanner = "";
    bool[] tabSelected = new bool[SubCount];

    /// <summary>m_spRoom.</summary>
    public DungeonRoomData Room { get; private set; }
    /// <summary>GetBanner (m_strBanner): the party status tooltip text.</summary>
    public string Banner => strBanner;

    /// <summary>The packets this panel sends and the boxes it shows (as in <see cref="PartyController"/>).</summary>
    public Action<IOutPacket> Send = _ => { };
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    UiHost Host => owner.Host;
    DungeonRoomHooks Hooks => owner.Hooks;
    DungeonRecordBook Records => owner.Records;
    PartyBook Party => owner.Party;
    UiModel Model => owner.Model;
    string Common(string key) => owner.CommonText(key);

    /// <summary>The constructor (@0x1412a99a0, :536-602): each child by its instance name, the click handlers and the sub panels.
    /// A missing child is noted (DR_SWF_ERROR in the client) and its feature left out.</summary>
    public DungeonRoomInfoPanel(DungeonRoomDialogController owner, object info)
    {
        this.owner = owner;
        this.info = info;
        object Need(string name, int code, object parent = null)
        {
            var c = Host.Child(name, parent ?? info);
            if (c == null) Host.RT.NoteMissing($"dungeon room panel: {name} ({code})");
            return c;
        }
        enterSolo = Need("m_enterSoloButton", 303);
        enterParty = Need("m_enterPartyButton", 304);
        partySearch = Need("m_partySearchButton", 305);
        reset = Need("m_resetButton", 306);
        addExtra = Need("m_addExtraRewardCount", 307);
        rewardIcon1 = Need("m_rewardIcon1", 308);
        rewardIcon2 = Need("m_rewardIcon2", 309);
        banner = Need("m_dungeonBanner", 310);
        scroll = Need("m_dungeonScrollInfo", 311);
        enterStatus = Need("m_enterStatusText", 312);
        findHelper = Need("m_findHelperButton", 313);
        findMember = Need("m_findMemberButton", 314);
        changeExpert = Need("m_changeExpertButton", 315);
        clearCount = Need("m_clearCount", 316);
        helpCoinInfo = Need("m_helpCoinInfo", 317);
        helpEvent = Need("m_dungeonHelpEvent", 318);
        searching = Need("m_dungeonSearching", 319);
        favorite = Need("m_favoriteMark", 320);
        title = Host.Child("m_title", info);
        findHelperStatus = Host.Child("m_findHelperStatus", info);
        rewardText1 = Host.Child("m_rewardText1", info);
        rewardText2 = Host.Child("m_rewardText2", info);
        cooldownIcon = Host.Child("m_cooldownIcon", info);
        partyMembersClip = Host.Child("m_partyMembers", info);
        contents = scroll == null ? null : Need("m_contents", 334, scroll);
        for (int i = 0; i < SubCount; i++)
        {
            subTabs[i] = Need("m_subTabButton" + i, 337, contents);
            subPanels[i] = Need("m_subPanel" + i, 340, contents);
        }
        if (subPanels[2] != null)
            for (int i = 0; i < RewardItemCount; i++) rewardItems[i] = Host.Child("m_rewardItem" + i, subPanels[2]);
        desc = subPanels[0] == null ? null : Need("m_dungeonDesc", 342, subPanels[0]);
        limitText = subPanels[1] == null ? null : Need("m_limitText", 343, subPanels[1]);
        requireCondition = subPanels[1] == null ? null : Need("m_requireCondition", 344, subPanels[1]);
        for (int i = 0; i < PartyMemberCount; i++) partyMembers[i] = partyMembersClip == null ? null : Host.Child("m_partyMember" + i, partyMembersClip);

        if (helpEvent != null) Host.SetVisible(helpEvent, false);
        foreach (var t in new[] { rewardText1, rewardText2 }) if (t != null) Host.Set(t, "autoSize", 2);
        if (limitText != null && subPanels[1] != null) limits = new LimitTextGroup(Host, subPanels[1], limitText);
        rank = new RankPanel(this, subPanels.Length > 3 ? subPanels[3] : null);
        season = new SeasonRankReward(this, subPanels.Length > 4 ? subPanels[4] : null);

        for (int i = 0; i < SubCount; i++)
        {
            int tab = i;
            if (subTabs[i] != null) Click(subTabs[i], () => OnSubTab(tab));
        }
        Click(enterSolo, OnEnterSolo);
        Click(enterParty, OnEnterParty);
        Click(partySearch, OnPartySearch);
        Click(addExtra, OnAddExtraRewardCount);
        Click(changeExpert, OnChangeExpert);
        if (findHelper != null) Host.Set(findHelper, "label", Common("s_dungeonRoom_find_helper_button"));
        Click(findHelper, OnFindHelper);
        Click(findMember, OnFindMember);
        Click(requireCondition, OnRequireCondition);
        Click(reset, OnReset);
        Click(favorite, OnFavoriteMark);
    }

    internal UiHost UiHost => Host;
    internal string Text(string key) => Common(key);
    internal DungeonRoomDialogController Owner => owner;

    void Click(object target, Action handler)
    {
        if (target == null) return;
        Func<object[], object> f = _ => { Host.RT.Guard("dungeon room panel click", handler); return null; };
        Host.Invoke(target, "addEventListener", "buttonClick", f, false, 0, false);
    }

    // ---- the text helpers (the client's SetText family) ----

    /// <summary>The text field of a clip: the object itself when it is a text field, else its "textField" child.</summary>
    object Field(object o) => o == null ? null : Host.Has(o, "htmlText") ? o : Host.Child("textField", o);
    void SetText(object o, string text) { if (Field(o) is { } f) Host.Set(f, "text", text); }
    void SetHtml(object o, string text) { if (Field(o) is { } f) Host.Set(f, "htmlText", text); }
    void Show(object o, bool visible) { if (o != null) Host.SetVisible(o, visible); }
    void SetPos(object o, double x, double y) { if (o == null) return; Host.Set(o, "x", x); Host.Set(o, "y", y); }
    double Num(object o, string member) => o == null ? 0 : Rt.ToNumber(Host.Get(o, member));
    /// <summary>FUN_14183e930: the text wrapped in a font colour.</summary>
    internal static string Colored(string text, int color) => $"<FONT color='#{color:x}'>{text}</FONT>";
    internal string F(string key, params object[] args) => ClientFormat.Format(Common(key), args);
    string Block(int id, params object[] args) => ClientFormat.Format(Model.Tables.Common(id) ?? "", args);
    long Now => Records.ServerTime();
    bool InParty => Party != null && Party.IsInParty;
    long Self => Hooks.SelfCharacterId?.Invoke() ?? Records.CharacterId();
    bool IsLeader => Party != null && Party.IsLeader(Self);
    bool Searching(DungeonRoomData room) => Party != null && Party.IsMatchSearching(QuestCommon.ClubBuffTypeOf(room.GroupType));
    internal bool FeatureOn(int id) => Model.IsFeatureEnabled(id);

    // ---- room and refresh ----

    /// <summary>SetRoom: m_spRoom.</summary>
    public void SetRoom(DungeonRoomData room) => Room = room;

    /// <summary>
    /// Refresh (@0x1412adb40, :627): the banner, the title, the description (string table 0x15), the cooldown icon, the favourite mark, then
    /// the sub tabs, limits, helper, enter buttons, rewards, reward preview, mission rank, reset button, helper layout, the season
    /// reward panel and the sub panel layout.
    /// </summary>
    public void Refresh()
    {
        if (Room == null || Host?.Entity == null) return;
        var room = Room;
        if (banner != null) Hooks.LoadBanner?.Invoke(banner, room.DungeonBanner);
        SetText(title, owner.GetRoomListText(room, false));
        if (desc != null)
        {
            // :631: m_dungeonDesc->SetTextHTMLW(CStringTable::FUN_141811980(0x15, room id)), then the height from the text
            SetHtml(desc, Hooks.RoomDescription?.Invoke(room.Id) ?? Model.Tables.StringTableText(0x15, room.Id));
            Host.Set(desc, "height", Num(Field(desc), "textHeight") + 0x14);
        }
        Show(cooldownIcon, IsCooldown());
        if (favorite != null) Host.Set(favorite, "selected", Records.IsSeasonRoom(room.Id));
        RefreshSubTabVisibility();
        RefreshLimits();
        RefreshFindHelper();
        RefreshEnterButtons();
        RefreshReward();
        RefreshRewardPreview();
        RefreshMissionRank();
        RefreshResetButton();
        RefreshHelper();
        season.Refresh(room);
        LayoutSubPanels();
    }

    /// <summary>IsCooldown (:1798): the room limits entering (LimitType 2) and the record of its reward room cannot enter.</summary>
    public bool IsCooldown() => Room != null && Room.LimitType == 2 && !Records.CanEnter(Room.RewardRoomId);

    /// <summary>RefreshSubTabVisibility (@0x1412adfc0): tab 3 (mission rank) needs feature 0x71 and missions, tab 4 (season rank) a season rank reward id.</summary>
    void RefreshSubTabVisibility()
    {
        if (Room == null) return;
        Show(subTabs[3], FeatureOn(0x71) && Room.HasMissions);
        Show(subPanels[3], FeatureOn(0x71) && Room.HasMissions);
        bool seasonRank = Room.SeasonRankRewardId != 0;
        Show(subTabs[4], seasonRank);
        Show(subPanels[4], seasonRank);
        if (seasonRank) tooltips["m_subTabButton4"] = Common("s_dungeonRoom_seasonRankBar_tooltip");
    }

    // ---- limits ----

    /// <summary>
    /// RefreshLimits (@0x1412ae2b0, :678): the HTML lines of the room's limits, a line coloured white when the player meets it and red
    /// when not, into m_limitText; the additional effects as labels of the LimitTextGroup below it; the open period lines; then
    /// m_requireCondition below them (the achievement, else VIP). STILL-OPEN: the lines whose hook is null (day of week list, time
    /// ranges, open period, the weapon, effect and VIP checks) are left out.
    /// </summary>
    void RefreshLimits()
    {
        var room = Room;
        var me = owner.LocalPlayer();
        if (room == null || me == null || limitText == null) return;
        var sb = new StringBuilder();
        void Line(string text, int? color = null) => sb.Append(color is { } c ? Colored(text, c) : text).Append('\n');
        const int Met = 0xffffff, NotMet = 0xff0000;
        if (room.LimitPlayerLevel > 0) Line(F("s_dungeonRoom_limit_Level", (int) room.LimitPlayerLevel), me.Value.Level >= room.LimitPlayerLevel ? Met : NotMet);
        if (room.GearScore > 0) Line(F("s_dungeonRoom_limit_GearScore", (int) room.GearScore), me.Value.GearScore >= room.GearScore ? Met : NotMet);
        if (room.LimitDayOfWeeks != 0 && Hooks.DayOfWeekList?.Invoke(room.LimitDayOfWeeks) is { } days)
            Line(F("s_dungeonRoom_limit_DayOfWeeks", days));
        if (room.MinUserCount > 0)
        {
            int members = Party != null && Party.IsInParty ? Party.Members.Count : 1;
            Line(F("s_dungeonRoom_limit_minUserCount", (int) room.MinUserCount), members >= room.MinUserCount ? Met : NotMet);
        }
        foreach (var id in room.LimitClearDungeon)
            Line(F("s_dungeonRoom_limit_clearDungeon", Model.Tables.FieldEnteranceName((int) id) ?? ""), Records.HasProgress(id) ? Met : NotMet);
        if (room.LimitRecommendWeapon && Hooks.RecommendWeaponEquipped?.Invoke() is { } weapon)
            Line(F(weapon ? "s_dungeonRoom_limit_recommendJobs_weapon_equip" : "s_dungeonRoom_limit_recommendJobs_weapon"), weapon ? Met : NotMet);
        if (room.IsLimitTime && Hooks.TimeLimitRanges?.Invoke(room.Id) is { } ranges)
        {
            if (ranges.Count == 0) Line(F("s_dungeonRoom_limit_cannot_enter"), NotMet);
            else
            {
                var rt = new StringBuilder();
                foreach (var r in ranges) rt.Append($"{r.StartHour:00}:{r.StartMinute:00}~{r.EndHour:00}:{r.EndMinute:00} ");
                Line(F("s_dungeonRoom_limit_time", rt.ToString()));
            }
        }
        Host.Set(Field(limitText), "htmlText", sb.ToString());
        double y = Num(limitText, "y");
        double nY = Num(Field(limitText), "textHeight") + y + 5;
        Host.Set(limitText, "height", nY);
        limits.Clear();
        limits.SetPosition(Num(limitText, "x"), nY);
        foreach (var id in room.LimitAdditionalEffects)
        {
            if (Hooks.AdditionalEffectActive?.Invoke(id) is not { } active) continue;
            string name = Model.Tables.StringTableText(0x4c, id) ?? "";
            limits.AddText(F("s_dungeonRoom_limit_additionalEffect", name), active ? Met : NotMet, name);
        }
        if (room.OpenPeriodDateLocal != 0 && Now < room.OpenPeriodDateLocal && Hooks.YearMonthDayHour?.Invoke(room.OpenPeriodDateLocal) is { } when)
            limits.AddText(F("s_dungeonRoom_limit_open_period_upcoming", when), NotMet, "");
        else if (!string.IsNullOrEmpty(room.OpenPeriodTag) && Hooks.OpenPeriodOpen?.Invoke(room.OpenPeriodTag) is { } open)
            limits.AddText(F(open ? "s_dungeonRoom_limit_open_period" : "s_dungeonRoom_limit_open_period_closed"), open ? Met : NotMet, "");
        nY = limits.Bottom;
        if (requireCondition == null) return;
        Show(requireCondition, false);
        SetPos(requireCondition, Num(requireCondition, "x"), 0);
        if (room.LimitAchieveId != 0)
        {
            Show(requireCondition, true);
            SetPos(requireCondition, Num(requireCondition, "x"), nY + 10);
            SetText(requireCondition, Hooks.AchieveName?.Invoke(room.LimitAchieveId) ?? "");
            bool? done = Hooks.AchieveCompleted?.Invoke(room.LimitAchieveId);
            if (done != null) SetColor(requireCondition, done.Value ? Met : NotMet);
            Frame(Host.Child("icon", requireCondition), "achieve");
        }
        else if (room.LimitVip)
        {
            Show(requireCondition, true);
            SetPos(requireCondition, Num(requireCondition, "x"), nY + 10);
            SetText(requireCondition, Common("s_dungeonRoom_limit_VIP"));
            bool? vip = Hooks.IsVip?.Invoke();
            if (vip != null) SetColor(requireCondition, vip.Value ? Met : NotMet);
            Frame(Host.Child("icon", requireCondition), "VIP");
        }
    }

    void SetColor(object clip, int color) { if (Field(clip) is { } f) Host.Set(f, "textColor", color); }
    void Frame(object clip, string frame) { if (clip != null) Host.Invoke(clip, "gotoAndStop", frame); }

    // ---- helper / find helper ----

    /// <summary>
    /// RefreshFindHelper (@0x1412b0110, :799): hides the helper widgets, shows the clear count of a single-round room, then, with
    /// feature 0x93: the find helper button and status for a beginner, the change-to-expert button, the helper coin info, and the find
    /// member button of the helper room (1004). STILL-OPEN: the helper bonus (m_dungeonHelpEvent) needs <see cref="DungeonRoomHooks.HelperBonus"/>.
    /// </summary>
    void RefreshFindHelper()
    {
        var room = Room;
        if (room == null || owner.LocalPlayer() == null) return;
        foreach (var o in new[] { findHelper, findMember, helpEvent, helpCoinInfo, changeExpert }) Show(o, false);
        SetHtml(findHelperStatus, "");
        int clears = Records.GetRoomValue5c(room.Id);
        if (room.IsSingleRound) { Show(clearCount, true); SetHtml(clearCount, F("s_dungeonRoom_clear_count", clears)); }
        else Show(clearCount, false);
        if (!FeatureOn(0x93)) return;
        bool canClaim = Records.CanClaimHelperReward(room.Id);
        int need = room.DungeonHelperRequireClearCount;
        if (need == 0) SetHtml(findHelperStatus, F("s_dungeonRoom_findHelper_dont_use"));
        else if (!canClaim)
        {
            Show(findHelper, true);
            tooltips["m_findHelperButton"] = F("s_dungeonRoom_findHelper_button_tooltip", need);
            SetHtml(findHelperStatus, F("s_dungeonRoom_findHelper_status_beginner"));
            Show(changeExpert, clears >= need);
        }
        else
        {
            Show(helpCoinInfo, true);
            Show(helpEvent, Hooks.HelperBonus?.Invoke() > 0);
        }
        var helperRoom = owner.Table.GetRoom1004();
        if (helperRoom == null) return;
        Show(findMember, true);
        tooltips["m_findMemberButton"] = F("s_dungeonRoom_findMember_button_tooltip", (int) helperRoom.RewardCount);
        var rewardCount = Host.Child("rewardCount", helpCoinInfo);
        SetHtml(rewardCount, F("s_dungeonRoom_helper_reward_count", WeeklyRewardCountText(0x3eb)));
        var icon = Host.Child("m_dungeonHelperIcon", helpCoinInfo);
        if (icon != null && rewardCount != null) SetPos(icon, (int) (Num(rewardCount, "x") + Num(Field(rewardCount), "textWidth") + 10), 0);
    }

    // ---- enter buttons ----

    /// <summary>RefreshEnterButtons (@0x1412b3120): the party or the solo status.</summary>
    void RefreshEnterButtons()
    {
        if (Room == null) return;
        if (InParty) RefreshPartyStatus(); else RefreshSoloStatus();
    }

    /// <summary>GetCountColor (:1737): the frame label of a clear state.</summary>
    internal static string CountColor(int state) => state switch { 0 => "green", 1 => "blue", _ => "red" };

    /// <summary>
    /// GetPartyStatusTooltip (:1752): the state's text in " ({0})" (red from state 10 on) and the line s_dungeonRoom_party_status_tooltip with the
    /// member's name.
    /// </summary>
    internal string PartyStatusTooltip(int state, string name)
    {
        string text = state switch
        {
            0 => Common("s_dungeonRoom_beginner"), 1 => Common("s_dungeonRoom_expert"),
            11 => Block(0x9aa), 12 => Block(0x9ab), 13 => Block(0x9ac),
            14 => Common("s_field_enteracne_party_status_vip"), 15 => Common("s_field_enteracne_party_status_gearScore"),
            16 => Common("s_field_enteracne_party_status_clearDungeon"), 17 => Common("s_field_enteracne_party_status_additionalEffect"),
            18 => Common("s_dungeon_enter_limit_status_recommend_weapon"),
            _ => "",
        };
        if (text.Length > 0) text = Colored(ClientFormat.Format(" ({0})", text), state >= 10 ? 0xff0000 : 0xffffff);
        return F("s_dungeonRoom_party_status_tooltip", name, text);
    }

    void SetMember(int i, int? state, string name)
    {
        if (i >= PartyMemberCount || partyMembers[i] is not { } m) return;
        if (state is not { } s) { Show(m, false); return; }
        Show(m, true);
        Frame(m, CountColor(s));
        strBanner += PartyStatusTooltip(s, name);
    }

    /// <summary>RefreshSoloStatus (@0x1412b3180): status text 0x9af, the first member icon with the player's state, the rest hidden.</summary>
    void RefreshSoloStatus()
    {
        if (owner.LocalPlayer() == null || partyMembersClip == null) return;
        SetText(enterStatus, Block(0x9af));
        strBanner = "";
        for (int i = 1; i < PartyMemberCount; i++) Show(partyMembers[i], false);
        SetMember(0, Hooks.ClearState?.Invoke(Room.Id), Hooks.LocalName?.Invoke() ?? "");
    }

    /// <summary>RefreshPartyStatus (@0x1412b3430): status text 0x9b0, each party member's icon and banner line; a slot without a member hidden.</summary>
    void RefreshPartyStatus()
    {
        strBanner = "";
        SetText(enterStatus, Block(0x9b0));
        for (int i = 0; i < PartyMemberCount; i++)
        {
            if (i >= Party.Members.Count) { Show(partyMembers[i], false); continue; }
            var m = Party.Members[i];
            SetMember(i, Hooks.MemberClearState?.Invoke(m.User.CharacterId, Room.Id), m.User.Name);
        }
    }

    /// <summary>RefreshResetButton (@0x1412b3720): shown for the leader of a party in a matchmade run of this room (more than one user).</summary>
    void RefreshResetButton()
    {
        if (Room == null) return;
        Show(reset, false);
        if (Hooks.EnterBlocked?.Invoke() == true) return;
        Show(reset, InParty && Room.MaxUserCount > 1 && Party.Matchmaking && Party.DungeonId == (int) Room.Id && IsLeader);
    }

    /// <summary>RefreshHelper (@0x1412b3850): the button enabling, the layout and the searching indicator.</summary>
    void RefreshHelper()
    {
        RefreshEnterEnabled();
        LayoutEnterButtons();
        RefreshSearchingIndicator();
    }

    /// <summary>FUN_1412b3890: solo enabled when the room needs no party (or the player may solo); party enabled for the leader, or "already entered" in a matchmade run.</summary>
    void RefreshEnterEnabled()
    {
        var room = Room;
        Host.Set(enterSolo, "enabled", false);
        Host.Set(enterParty, "enabled", false);
        if (room == null || Hooks.EnterBlocked?.Invoke() == true) return;
        if (room.MinUserCount == 0 || Hooks.SoloAllowed?.Invoke() == true) Host.Set(enterSolo, "enabled", true);
        if (!InParty || room.MaxUserCount <= 1) return;
        if (Party.Matchmaking && Party.DungeonId == (int) room.Id)
        {
            Host.Set(enterParty, "enabled", true);
            Host.Set(enterParty, "label", Common("s_dungeonRoom_enterButton_already_enter"));
        }
        else if (IsLeader)
        {
            Host.Set(enterParty, "enabled", true);
            Host.Set(enterParty, "label", Common("s_dungeonRoom_enterButton_party_enter"));
        }
    }

    /// <summary>LayoutEnterButtons (@0x1412b3b40): the visible buttons spread over the left (8), centre (0x7e) and right (0xfc) places.</summary>
    void LayoutEnterButtons()
    {
        var room = Room;
        if (room == null) return;
        var list = new List<object>();
        bool solo = !room.IsPartyOnly && room.MinUserCount == 0 || Hooks.SoloAllowed?.Invoke() == true;
        Show(enterSolo, solo);
        if (solo) list.Add(enterSolo);
        Show(partySearch, room.IsUseRandomMatch);
        if (room.IsUseRandomMatch) { RefreshPartySearchButton(); list.Add(partySearch); }
        list.Add(enterParty);
        double y = Num(enterSolo, "y");
        int[] xs = list.Count switch { 1 => new[] { 0x7e }, 2 => new[] { 8, 0xfc }, _ => new[] { 8, 0x7e, 0xfc } };
        for (int i = 0; i < list.Count && i < xs.Length; i++) SetPos(list[i], xs[i], y);
    }

    /// <summary>RefreshPartySearchButton (@0x1412b3e90): selected with the cancel label while the room's group is being searched.</summary>
    public void RefreshPartySearchButton()
    {
        if (partySearch == null || Room == null || Host.Get(partySearch, "visible") is false) return;
        bool on = Searching(Room);
        Host.Set(partySearch, "selected", on);
        Host.Set(partySearch, "label", on ? "$s_dungeonSearch_cancel_button" : "$s_dungeonSearch_find_button");
    }

    /// <summary>RefreshSearchingIndicator (@0x1412b3fe0): the "searching" indicator while a random match for the room's group is running.</summary>
    public void RefreshSearchingIndicator()
    {
        if (searching == null || Room == null) return;
        if (!Room.IsUseRandomMatch) { Show(searching, false); return; }
        bool on = Searching(Room);
        if (on) LayoutSearching();
        Show(searching, on);
    }

    /// <summary>LayoutSearching (:1621): the icon left of the centred text.</summary>
    void LayoutSearching()
    {
        var area = Host.Child("textArea", searching);
        var text = Host.Child("textField", Host.Child("m_text", area));
        var icon = Host.Child("searchIcon", searching);
        if (icon == null || area == null) return;
        SetPos(icon, (int) (Num(area, "width") / 2 - (int) (Num(text, "textWidth") / 2)) - 0x30, 8);
    }

    // ---- rewards ----

    /// <summary>GetWeeklyRewardCountText (@0x140e14d70): "" without a record, else "{count} / {max}" and the extra reward event's count.</summary>
    internal string WeeklyRewardCountText(uint id)
    {
        var r = Records.Peek(id);
        if (r == null) return "";
        string text = $"{Records.BonusRewardCountNow(r)} / {Records.MaxCount(r)}";
        uint? ev = Records.ExtraRewardEventCount(id);
        if (ev != null) text += " " + F("s_dungeonRoom_event_reward_count", Records.ExtraCountNow(r), (int) ev.Value);
        return text;
    }

    /// <summary>GetDailyRewardCountText (@0x140e14fd0): "{clears today} / {SubRewardCount}", "" without a record or room.</summary>
    internal string DailyRewardCountText(uint id)
    {
        var r = Records.Peek(id);
        var room = owner.Table.GetRoom(id);
        if (r == null || room == null) return "";
        return $"{DungeonRecordBook.ClearCount(r, Now)} / {room.SubRewardCount}";
    }

    /// <summary>GetRewardCooldownTooltip (@0x140e15130): the daily or weekly reset text of the room.</summary>
    internal string RewardCooldownTooltip(uint roomId)
    {
        var room = owner.Table.GetRoom(roomId);
        if (room == null) return "";
        if (room.CooldownType == 2) return Block(0x9b1);
        if (room.CooldownType == 1 && Hooks.DayOfWeekName?.Invoke(room.CooldownValue) is { } day) return Block(0x9b2, day);
        return "";
    }

    /// <summary>RefreshReward (@0x1412b0d30): the round group's or the normal reward.</summary>
    void RefreshReward()
    {
        if (Room == null) return;
        if (Room.HasRoundGroup) RefreshRoundReward(); else RefreshNormalReward();
    }

    string UnionInfo(DungeonRoomData room, DungeonRoomData rewardRoom) =>
        room.UnionRewardId != 0 ? F("s_dungeonRoom_unionRewardInfo", Model.Tables.FieldEnteranceName((int) rewardRoom.Id) ?? "") : "";

    string Period(DungeonRoomData rewardRoom) => Common(rewardRoom.CooldownType == 2 ? "s_dungeonRoom_reward_daily" : "s_dungeonRoom_reward_weekly")
        + (rewardRoom.IsAccountReward ? Common("s_dungeonRoom_reward_account") : "");

    /// <summary>
    /// RefreshNormalReward (@0x1412b0da0): the daily and weekly reward counts of the reward room (two rows with a sub reward count), the
    /// icon tooltips with the union reward info, and the extra reward ticket button.
    /// </summary>
    void RefreshNormalReward()
    {
        var room = Room;
        var rr = owner.Table.GetRoom(room.RewardRoomId);
        if (rr == null) return;
        if (rr.SubRewardCount > 0)
        {
            SetText(rewardText1, ClientFormat.Format("{0} {1}", Common("s_dungeonRoom_reward_daily") + (rr.IsAccountReward ? Common("s_dungeonRoom_reward_account") : ""), DailyRewardCountText(rr.Id)));
            tooltips["m_rewardIcon1"] = Block(0x9b1) + UnionInfo(room, rr);
            SetText(rewardText2, ClientFormat.Format("{0} {1}", Common("s_dungeonRoom_reward_weekly") + (rr.IsAccountReward ? Common("s_dungeonRoom_reward_account") : ""), WeeklyRewardCountText(rr.Id)));
            Show(rewardIcon2, true);
            tooltips["m_rewardIcon2"] = RewardCooldownTooltip(rr.Id) + UnionInfo(room, rr);
        }
        else
        {
            SetText(rewardText1, ClientFormat.Format("{0} {1}", Period(rr), WeeklyRewardCountText(rr.Id)));
            SetText(rewardText2, "");
            tooltips["m_rewardIcon1"] = RewardCooldownTooltip(rr.Id) + UnionInfo(room, rr);
            Show(rewardIcon2, false);
        }
        if (rr.HasExtraRewardTicket)
        {
            Show(addExtra, true);
            int max = owner.Table.CouponMaxExtraCount(rr);
            Host.Set(addExtra, "label", max == 0 ? "$s_dungeon_room_addExtraRewardCount" : F("s_dungeonRoom_addExtraRewardCount", Records.GetRoomBonusCount(rr.Id), max));
        }
        else Show(addExtra, false);
    }

    /// <summary>RefreshRoundReward (@0x1412b1d50): the round clear count of the reward room's record.</summary>
    void RefreshRoundReward()
    {
        var room = Room;
        var rr = owner.Table.GetRoom(room.RewardRoomId);
        if (rr == null) return;
        var r = Records.Peek(rr.Id);
        if (r == null) return;
        SetText(rewardText1, F("s_dungeonRoom_reward_round", Records.BonusRewardCountNow(r), Records.MaxCount(r)));
        SetText(rewardText2, "");
        tooltips["m_rewardIcon1"] = RewardCooldownTooltip(rr.Id) + UnionInfo(room, rr);
        Show(rewardIcon2, false);
        Show(addExtra, false);
    }

    /// <summary>
    /// UIDungeonRoomInfo::RefreshRewardPreview (@0x1412b2250, uidungeonroomdialog.cpp:966-1054): clears the binder; with a local player
    /// and a room, takes the room's preview row (RewardPreview_dungeon.xml, <see cref="RewardPreviewTable.GetDungeon"/>) filtered by the
    /// player's job group and level (GetValidVector), or, with no row, the unique items of each of the room's reward drop boxes
    /// (<see cref="IndividualDropTable.GetUniqueBoxItems"/>, job group and gender 2 = any); sorts them with RewardPreviewItem::Compare
    /// (<see cref="RewardPreviewItem.Less"/>) and fills the 0x1c slots: none leaves "" in the slot, an item that cannot be made leaves the
    /// slot as it was, else the New or Event tag's badge, the slot shown and its binder name set.
    /// STILL-OPEN: the event boxes (CGameRule::FUN_14047b300 with the room id and group type: no source here), the dev key fallback (keys
    /// 0x2a and 0x1d held force the box path), the reward items of the room's round group (RewardContentsTable::GetReward per round: the
    /// RewardContentItemTable and its random roll are not ported; the box path is what the rooms with boxes show), the transfer count of a
    /// tradable item (SetTransferCount acts only on an item with a transfer record, which a port item has not).
    /// </summary>
    void RefreshRewardPreview()
    {
        slotBinder ??= new ItemSlotBinder(Model, "DungeonRoomDialog") { IconType = 0x403 };   // ctor :537-539
        slotBinder.Clear();
        if (owner.LocalPlayer() is not { } me || Room == null) return;
        var tables = Model.Tables;
        var items = PreviewItems(tables, Room, owner.LocalJobGroup(), me.Level);
        for (int i = 0; i < rewardItems.Length; i++)
        {
            var slot = rewardItems[i];
            if (slot == null) continue;
            if (i >= items.Count) { Host.Invoke(slot, "setData", ""); continue; }
            var item = Item.Create(tables.Items, items[i].ItemId, items[i].Rank);
            if (item == null) continue;                         // FUN_14052b130 gave none: the slot stays as it was
            if (items[i].Tag == RewardPreviewItemTag.New) item.IsNew = true;
            else if (items[i].Tag == RewardPreviewItemTag.Event) item.IsEvent = true;
            Host.SetVisible(slot, true);
            Host.Invoke(slot, "setData", slotBinder.BindItem(item));
        }
    }

    /// <summary>
    /// The items RefreshRewardPreview lists for a room, sorted (:966-1054, see <see cref="RefreshRewardPreview"/>): the room's preview row
    /// filtered by <paramref name="job"/> and <paramref name="level"/>, else the unique items of each of its reward drop boxes.
    /// </summary>
    public static List<RewardPreviewItem> PreviewItems(GameTables tables, DungeonRoomData room, int job, int level)
    {
        var items = new List<RewardPreviewItem>();
        if (tables.RewardPreviews.GetDungeon((int) room.Id) is { } preview)
        {
            foreach (var i in preview) if (i.IsValid(job, level)) items.Add(i);
        }
        else
        {
            var drops = new List<ItemDropEntry>();
            foreach (var box in room.RewardDropBoxIds)
            {
                drops.Clear();
                if (tables.IndividualDrops.GetUniqueBoxItems(tables.Items, (int) box, job, 2, drops))
                    foreach (var d in drops) items.Add(RewardPreviewItem.Of(d));
            }
        }
        SortPreview(tables.Items, items);
        return items;
    }

    /// <summary>The sort of :1020 (std::sort over RewardPreviewItem::Compare); an insertion sort, as the compare is false for an item without data.</summary>
    static void SortPreview(ItemDataTable table, List<RewardPreviewItem> items)
    {
        for (int i = 1; i < items.Count; i++)
        {
            var x = items[i];
            int j = i - 1;
            while (j >= 0 && RewardPreviewItem.Less(table, x, items[j])) { items[j + 1] = items[j]; j--; }
            items[j + 1] = x;
        }
    }

    // ---- mission rank ----

    /// <summary>FUN_1412b9ec0: the rank's name key, s_dungeon_rank_reward_c..sp for 1..5.</summary>
    internal string RankName(int rank) => Common(rank switch
    {
        1 => "s_dungeon_rank_reward_c", 2 => "s_dungeon_rank_reward_b", 3 => "s_dungeon_rank_reward_a", 4 => "s_dungeon_rank_reward_s",
        5 => "s_dungeon_rank_reward_sp", _ => "s_dungeon_rank_reward_unknown",
    });

    /// <summary>RefreshMissionRank (@0x1412b2e70): tab 3's tooltip (the rank names with their scores) and the rank panel.</summary>
    void RefreshMissionRank()
    {
        var room = Room;
        if (room == null || !room.HasMissions) return;
        var sb = new StringBuilder(Common("s_dungeonRoom_missionBar_tooltip"));
        var req = owner.Table.GetRankRequire(room);
        for (int i = 0; req != null && i < req.Count; i++)
            sb.Append(F("s_dungeonRoom_missionBar_tooltip_item", RankName(i + 1), (int) req.GetScore(i)));
        tooltips["m_subTabButton3"] = sb.ToString();
        rank.Refresh(room);
    }

    // ---- layout, tabs ----

    /// <summary>OnSubTab (:1578): the tab's selected state toggles and the panels are laid out again.</summary>
    void OnSubTab(int tab)
    {
        if (tab < 0 || tab >= SubCount) return;
        LayoutSubPanels();
    }

    /// <summary>LayoutSubPanels (:1595): a column of tab buttons (0x22 high) each followed by its panel unless the tab is selected (collapsed).</summary>
    void LayoutSubPanels()
    {
        double y = 0;
        for (int i = 0; i < SubCount; i++)
        {
            if (subTabs[i] == null) continue;
            SetPos(subTabs[i], 0, y);
            y += 0x22;
            if (Host.Get(subTabs[i], "visible") is false) continue;
            bool collapsed = Host.Get(subTabs[i], "selected") is true;
            if (subPanels[i] == null) continue;
            if (collapsed) { Show(subPanels[i], false); continue; }
            Show(subPanels[i], true);
            SetPos(subPanels[i], 0, y);
            y += Math.Ceiling(Num(subPanels[i], "height"));
        }
        if (scroll != null) Host.Invoke(scroll, "setContentsHeight", y);
    }

    // ---- handlers ----

    /// <summary>GetClearCount (:1720): 0xb without a room, else the local player's enter state of the room (STILL-OPEN hook; 0xb when unset).</summary>
    int ClearCountOfSelf => Room == null ? 0xb : Hooks.ClearState?.Invoke(Room.Id) ?? 0xb;

    /// <summary>OnEnterSolo (@0x1412b41a0): the extra ticket question, else the entry.</summary>
    public void OnEnterSolo()
    {
        if (owner.LocalPlayer() == null || Room == null) return;
        if (AskExtraTicketEnter(false)) return;
        RequestEntry(false, false);
    }

    /// <summary>OnEnterParty (@0x1412b4220): in a matchmade run of the room the dungeon id is sent (SendRoomDungeon), else the leader enters.</summary>
    public void OnEnterParty()
    {
        if (owner.LocalPlayer() == null || Room == null) return;
        if (Party != null && Party.Matchmaking && Party.DungeonId == (int) Room.Id) { Send(new RoomDungeonEnterRequest((int) Room.DungeonId)); return; }
        if (!IsLeader) return;
        if (AskExtraTicketEnter(true)) return;
        RequestEntry(true, false);
    }

    /// <summary>OnPartySearch (@0x1412b4330): not selected starts the room's search, selected cancels the group's.</summary>
    public void OnPartySearch()
    {
        if (Room == null || partySearch == null) return;
        if (Host.Get(partySearch, "selected") is not true) Send(new PartyDungeonSearchStartRequest(Room.Id));
        else Send(new PartyDungeonSearchCancelRequest((byte) QuestCommon.ClubBuffTypeOf(Room.GroupType)));
    }

    /// <summary>RequestEntry (:1702): RoomDungeon 2.</summary>
    public void RequestEntry(bool party, bool useTicket)
    {
        if (owner.LocalPlayer() == null || Room == null) return;
        Send(new RoomDungeonEntryRequest(Room.Id, party, (uint) owner.PortalId, useTicket));
    }

    /// <summary>AskExtraTicketEnter (:1675): for a room in cooldown with an extra reward ticket, a yes / no box offering to use the ticket.</summary>
    bool AskExtraTicketEnter(bool party)
    {
        var room = Room;
        if (room == null || room.ExtraRewardTicketTableId == 0 || !IsCooldown()) return false;
        uint cost = Records.GetExtraTicketCost(room);
        string item = Model.Tables.ItemName((int) owner.Table.CouponBaseItemId(room)) ?? "";
        string text = F("s_dungeonRoom_use_extra_count_item_enter", item, (int) cost, Model.Tables.FieldEnteranceName((int) room.Id) ?? "");
        var box = MessageBoxQueue.CreateWithCallback(text, 1, (button, _) =>
        {
            if (button == 0) RequestEntry(party, true);
            return true;
        });
        box.SetButtonPreset(1);
        ShowBox(box);
        return true;
    }

    /// <summary>OnAddExtraRewardCount (@0x1412b4410): the extra reward ticket box of the reward room; no ticket gives the "cannot use" box.</summary>
    public void OnAddExtraRewardCount()
    {
        var room = Room;
        if (room == null) return;
        var rr = owner.Table.GetRoom(room.RewardRoomId);
        if (rr == null) return;
        if (!rr.HasExtraRewardTicket)
        {
            ShowBox(MessageBoxQueue.Create(0, Common("s_room_dungeon_reward_CantUseExtraReward"), null, ""));
            return;
        }
        uint cost = Records.GetExtraTicketCost(rr);
        if (cost == 0) return;
        string item = Model.Tables.ItemName((int) owner.Table.CouponBaseItemId(rr)) ?? "";
        string name = Model.Tables.FieldEnteranceName((int) rr.Id) ?? "";
        string text = F(rr.CooldownType == 1 ? "s_dungeonRoom_use_extra_count_item_week" : "s_dungeonRoom_use_extra_count_item", item, (int) cost, name);
        var box = MessageBoxQueue.CreateWithCallback(text, 1, (button, b) =>
        {
            if (button == 0) Send(new RoomDungeonUseExtraRewardRequest(rr.Id));
            return true;
        });
        box.SetButtonPreset(1);
        box.Append(rr.Id);
        ShowBox(box);
    }

    /// <summary>OnChangeExpert (@0x1412b4890): the confirm box, then RoomDungeon 0x11 with the room.</summary>
    public void OnChangeExpert()
    {
        var room = Room;
        if (room == null) return;
        uint id = room.Id;
        var box = MessageBoxQueue.CreateWithCallback(Common("s_dungeonRoom_change_expert_message"), 1, (button, _) =>
        {
            if (button == 0) Send(new RoomDungeonChangeExpertRequest(id));
            return true;
        });
        box.Append(id);
        ShowBox(box);
    }

    /// <summary>OnFindHelper / OnFindMember (@0x1412b4a60 / @0x1412b4b90): need the room and CanAskHelp; a party in another dungeon is asked first.</summary>
    void OnFindHelper() => AskHelp(id => new RoomDungeonFindHelperRequest(id));
    void OnFindMember() => AskHelp(id => new RoomDungeonFindMemberRequest(id));

    void AskHelp(Func<uint, IOutPacket> request)
    {
        var room = Room;
        if (room == null || !CanAskHelp()) return;
        int partyRoom = Party?.DungeonId ?? 0;
        if (InParty && partyRoom != 0 && room.Id != partyRoom)
        {
            uint id = room.Id;
            string text = F("s_dungeonRoom_change_help_dungeon", Model.Tables.FieldEnteranceName(partyRoom) ?? "", Model.Tables.FieldEnteranceName((int) id) ?? "");
            var box = MessageBoxQueue.CreateWithCallback(text, 1, (button, _) =>
            {
                if (button == 0) Send(request(id));
                return true;
            });
            box.Append(id);
            ShowBox(box);
            return;
        }
        Send(request(room.Id));
    }

    /// <summary>CanAskHelp (:1641): the leader of a party with room left, not in a matchmade run, and a clear state under 10.</summary>
    bool CanAskHelp()
    {
        if (owner.LocalPlayer() == null) return false;
        if (InParty)
        {
            if (!IsLeader) { owner.Notice("s_party_err_not_chief"); return false; }
            if (Party.Members.Count >= Room.MaxUserCount) { owner.Notice("s_dungeonRoom_cant_ask_help_full_members"); return false; }
            if (Party.Matchmaking)
            {
                ShowBox(MessageBoxQueue.CreateWithCallback(Common("s_dungeonRoom_cant_ask_help_has_dungeon"), 1, (button, _) =>
                {
                    if (button == 0) Send(new RoomDungeonLeaveRequest());
                    return true;
                }));
                return false;
            }
        }
        int state = ClearCountOfSelf;
        if (state >= 10) { owner.ShowEnterLimitMessage(Room.Id, state); return false; }
        return true;
    }

    /// <summary>OnRequireCondition: the achievement dialog of the limit, else the VIP dialog.</summary>
    void OnRequireCondition()
    {
        if (Room == null) return;
        if (Room.LimitAchieveId != 0) Hooks.OpenAchievement?.Invoke(Room.LimitAchieveId);
        else if (Room.LimitVip) Hooks.OpenVip?.Invoke();
    }

    /// <summary>OnReset (@0x1412b5260): the leader asks to enter again (the extra ticket question first).</summary>
    void OnReset()
    {
        if (owner.LocalPlayer() == null || Room == null || !IsLeader) return;
        if (AskExtraTicketEnter(true)) return;
        RequestEntry(true, false);
    }

    /// <summary>OnFavoriteMark (@0x1412b5320): the favourite request with the mark's new state.</summary>
    void OnFavoriteMark()
    {
        if (Room == null || favorite == null) return;
        Send(new RoomDungeonFavoriteRequest(Room.Id, Host.Get(favorite, "selected") is true));
    }

    // ---- tooltips ----

    /// <summary>The text tooltip of a child name set by the last Refresh (the sub tab bars, the find buttons and the reward icons), null for none.</summary>
    public string TooltipText(string name) => tooltips.TryGetValue(name, out var t) && !string.IsNullOrEmpty(t) ? t : null;

    // ---- the rank panels ----

    /// <summary>RankPanel (ctor @0x1412a74a0): the mission rank of the room: its missions' labels, the rank icon and the best scores.</summary>
    sealed class RankPanel
    {
        readonly DungeonRoomInfoPanel p;
        readonly object panel, icon, summary;
        readonly List<object> labels = new();

        public RankPanel(DungeonRoomInfoPanel p, object panel)
        {
            this.p = p;
            this.panel = panel;
            if (panel == null) return;
            icon = p.Host.Child("m_missionRankIcon", panel);
            if (icon == null) p.Host.RT.NoteMissing("dungeon room panel: m_missionRankIcon (42)");
            summary = p.Host.Child("m_summaryScore", panel);
            if (summary == null) p.Host.RT.NoteMissing("dungeon room panel: m_summaryScore (43)");
        }

        /// <summary>Refresh (@0x1412a78a0): feature 0x71; the mission labels, then the room's best score and rank, then the season score.
        /// STILL-OPEN: the mission names and top scores (no DungeonMissionDataTable), FUN_140719b50's score text and the season score text.</summary>
        public void Refresh(DungeonRoomData room)
        {
            if (panel == null || !p.FeatureOn(0x71)) return;
            Reset();
            var missions = room.Missions();
            int n = Math.Max(1, missions.Count);
            AddLabels(n);
            if (summary != null) p.SetPos(summary, 0x88, n * 0x18 + 5);
            if (missions.Count == 0)
            {
                p.Show(labels[0], true);
                p.SetText(Host.Child("titleText", labels[0]), p.Common("s_room_dungeon_empty_mission"));
                return;
            }
            for (int i = 0; i < missions.Count; i++)
            {
                var l = labels[i];
                p.Show(l, true);
                p.SetPos(l, 0x88, i * 0x18 + 5);
                if (p.Hooks.Mission?.Invoke(missions[i]) is { } m)
                {
                    p.SetText(Host.Child("titleText", l), m.Name);
                    p.SetText(Host.Child("scoreText", l), m.MaxScore.ToString());
                }
            }
            short best = p.Records.GetRoomScore60(room.Id);
            if (best >= 0)
            {
                int r = p.Owner.Table.GetRank(room, best);
                p.Frame(icon, RankFrame(r));
                if (p.Hooks.ScoreText?.Invoke(best) is { } t) p.SetText(Host.Child("highScore", summary), t);
            }
        }

        UiHost Host => p.Host;

        /// <summary>The icon frame of a rank: rank_unknown, rank_c, rank_b, rank_a, rank_s, rank_sp.</summary>
        static string RankFrame(int r) => r switch { 1 => "rank_c", 2 => "rank_b", 3 => "rank_a", 4 => "rank_s", 5 => "rank_sp", _ => "rank_unknown" };

        /// <summary>Reset (@0x1412a81d0).</summary>
        void Reset()
        {
            p.Frame(icon, "rank_unknown");
            foreach (var l in labels) p.Show(l, false);
            if (summary != null)
            {
                p.SetText(Host.Child("highScore", summary), "0");
                p.SetText(Host.Child("seasonScore", summary), "0");
            }
        }

        /// <summary>AddMissionLabels (@0x1412a83e0): one "UIDungeonRoom_MissionLabel" clip per missing label.</summary>
        void AddLabels(int n)
        {
            while (labels.Count < n)
            {
                object label = null;
                Host.RT.Guard("mission label", () =>
                {
                    label = Host.RT.Construct(Host.RT.GetClass("UIDungeonRoom_MissionLabel"), Array.Empty<object>());
                    Host.Invoke(panel, "addChild", label);
                });
                if (label == null) { Host.RT.NoteMissing("dungeon room panel: UIDungeonRoom_MissionLabel"); break; }
                var t = Host.Child("titleText", label);
                if (t != null) Host.Set(t, "autoSize", 2);
                p.SetText(t, "");
                p.SetText(Host.Child("scoreText", label), "");
                labels.Add(label);
            }
        }
    }

    // ---- the season reward panel ----

    /// <summary>SeasonRankReward (ctor @0x1412a87e0): the five season rank rewards of the room (tab 4).</summary>
    sealed class SeasonRankReward
    {
        readonly DungeonRoomInfoPanel p;
        readonly object[] rewards = new object[5], ranks = new object[5];
        readonly bool built;

        public SeasonRankReward(DungeonRoomInfoPanel p, object panel)
        {
            this.p = p;
            if (panel == null) return;
            built = true;
            for (int i = 0; i < 5; i++)
            {
                rewards[i] = p.Host.Child("m_seasonRankReward" + i, panel);
                if (rewards[i] == null) p.Host.RT.NoteMissing($"dungeon room panel: m_seasonRankReward{i} (159)");
                ranks[i] = p.Host.Child("m_seasonRank" + i, panel);
                if (ranks[i] == null) p.Host.RT.NoteMissing($"dungeon room panel: m_seasonRank{i} (162)");
            }
        }

        /// <summary>
        /// Refresh (@0x1412a8e20): feature 0x71, a season rank reward id with a reward table; the first MaxRank clips are placed
        /// (left 0x21 and step 0x72 under five ranks, else 0x13 and 0x5f), the others hidden, a rank up to the reward count of the
        /// record complete. STILL-OPEN: the reward item icon of each slot (ASItemSlot.SetItem).
        /// </summary>
        public void Refresh(DungeonRoomData room)
        {
            if (!built || !p.FeatureOn(0x71) || room.SeasonRankRewardId == 0) return;
            var reward = p.Owner.Table.GetRankReward(room.SeasonRankRewardId);
            if (reward == null || reward.MaxRank <= 0) return;
            int max = reward.MaxRank;
            int left = max < 5 ? 0x21 : 0x13, step = max < 5 ? 0x72 : 0x5f;
            double rewardY = p.Num(rewards[0], "y"), rankY = p.Num(ranks[0], "y");
            int count = p.Records.GetRankRewardCount(room.SeasonRankRewardId);
            for (int i = 0; i < 5; i++)
            {
                if (i > max - 1) { p.Show(rewards[i], false); p.Show(ranks[i], false); continue; }
                p.Show(rewards[i], true);
                p.Show(ranks[i], true);
                p.SetPos(rewards[i], left + step * i, rewardY);
                p.SetPos(ranks[i], left + step * i + 0xe, rankY);
                int n = i + 1;
                p.Frame(rewards[i], n <= count ? "complete" : "normal");
                p.Frame(ranks[i], "enable" + n);
                p.Show(p.Host.Child("isComplete", ranks[i]), n <= count);
            }
        }
    }
}

/// <summary>
/// LimitTextGroup (@0x1412a94e0): the labels under the limit text. Each AddText makes a "UIDungeonRoomLimitLabel" clip in the panel at the
/// running position, with the text coloured and an optional tooltip, and moves the position down by the label's height.
/// </summary>
public sealed class LimitTextGroup
{
    readonly UiHost host;
    readonly object parent;
    readonly object anchor;
    readonly List<object> labels = new();
    double x, y;
    readonly Dictionary<string, string> tooltips = new();

    public LimitTextGroup(UiHost host, object parent, object anchor) { this.host = host; this.parent = parent; this.anchor = anchor; }

    /// <summary>SetPosition.</summary>
    public void SetPosition(double x, double y) { this.x = x; this.y = y; }
    /// <summary>GetBottom: the running y.</summary>
    public double Bottom => y;
    public int Count => labels.Count;

    /// <summary>Clear: the labels leave the panel.</summary>
    public void Clear()
    {
        foreach (var l in labels) host.Invoke(parent, "removeChild", l);
        labels.Clear();
        tooltips.Clear();
    }

    /// <summary>AddText: a label of the colour, the tooltip (when non-empty) answered for its name.</summary>
    public void AddText(string text, int color, string tooltip)
    {
        object label = null;
        host.RT.Guard("limit label", () =>
        {
            label = host.RT.Construct(host.RT.GetClass("UIDungeonRoomLimitLabel"), Array.Empty<object>());
            host.Invoke(parent, "addChild", label);
        });
        if (label == null) { host.RT.NoteMissing("dungeon room panel: UIDungeonRoomLimitLabel"); return; }
        host.Set(label, "autoSize", "none");
        var field = host.Has(label, "htmlText") ? label : host.Child("textField", label);
        host.Set(field, "htmlText", DungeonRoomInfoPanel.Colored(text, color));
        host.Set(label, "x", x);
        host.Set(label, "y", y);
        if (!string.IsNullOrEmpty(tooltip)) { host.Set(label, "name", "m_limitLabel" + labels.Count); tooltips["m_limitLabel" + labels.Count] = tooltip; }
        y += Math.Ceiling(Rt.ToNumber(host.Get(field, "textHeight")));
        labels.Add(label);
    }

    /// <summary>The tooltip of a label by its instance name.</summary>
    public string TooltipText(string name) => tooltips.TryGetValue(name, out var t) ? t : null;
}
