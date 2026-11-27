using System.Globalization;
using Ms2.Avm2;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the dungeon finder, UIDungeonControlCenter (client/ms2/uidungeoncategory.cpp, 0x140c0b180-0x140c0e050; UI id 0x4f84),
/// for its movie uidungeoncontrolcenter. The game menu and the hotkey open it when feature 0xf9 is on (UIGameMenuDialog @0x14193d5e0,
/// keytable.cpp:806-808), else they open UIDungeonSearchDialog (0x4f1c). Two main tabs: tab 0 shows the recommended rooms and the rooms of
/// the level's sub tab (<see cref="DungeonCategoryTable"/>), tab 1 the event, item, guild raid, dark stream and VIP groups. Below sit the
/// find component (match, party search, setting) and the weekly reward component. The bookmarks (m_bookmarks) are the room ids the find
/// button matches for; they persist in the dialog's settings file as the attribute "dungeon".
/// STILL-OPEN: the weekly reward preview (RewardContentsTable::GetReward of the next united weekly step: the first item's binding, the meso and
/// the exp, <see cref="WeeklyRewardPreview"/>) has no table here, so those three members stay unset; FUN_140930b40 (the account session test,
/// <see cref="SessionBlocksDungeon"/>) and DAT_143ab2878 (<see cref="PartySearchBlocked"/>) have no feed and read false; the local player's gear
/// score (a hook, FUN_14152ea50).
/// </summary>
public sealed class DungeonControlCenterController : UiController
{
    public const int UiId = 0x4f84;
    /// <summary>The older finder UIGameMenuDialog opens when feature <see cref="FinderFeature"/> is off (UIDungeonSearchDialog).</summary>
    public const int SearchDialogUiId = 0x4f1c;
    /// <summary>UIGameMenuDialog @0x14193d5e0 and keytable.cpp:806: IsFeatureEnabled(0xf9) picks this window over 0x4f1c.</summary>
    public const int FinderFeature = 0xf9;
    /// <summary>The count of recommended rooms (GetRecommendedRooms stops at 4).</summary>
    public const int RecommendedCount = 4;
    /// <summary>The room id the weekly reward component reads (GetRecord(0x3ee)).</summary>
    public const uint WeeklyRecordRoom = 0x3ee;

    /// <summary>The key action of the finder (CKeyTable::FUN_14142b300 case 400018, keytable.cpp:806).</summary>
    public const long OpenKeyAction = 400018;

    /// <summary>True when DirectInput key <paramref name="dik"/> is bound to <see cref="OpenKeyAction"/> in <paramref name="keys"/>.</summary>
    public static bool OpensWithKey(KeyTable keys, int dik)
    {
        foreach (int k in keys.KeysOfAction(OpenKeyAction))
            if (k == dik) return true;
        return false;
    }

    public override string Movie => "uidungeoncontrolcenter";
    protected override string EntityMethod => "buildTab";
    /// <summary>GetTypeName: the settings file's name (the binder's "DungeonControlCenter").</summary>
    public override string TypeName => "DungeonControlCenter";

    readonly PartyBook book;

    public DungeonControlCenterController(PartyBook book = null) { this.book = book; }

    /// <summary>The record manager (the weekly record 0x3ee and the room rows); the owner sets the session's book.</summary>
    public DungeonRecordBook Records { get; set; } = new();
    /// <summary>The tables the settings load reads before the movie exists (OnLoadComplete uses the model's).</summary>
    public GameTables PreloadTables { get; set; }
    GameTables Tables => Host != null ? Model.Tables : PreloadTables ?? GameTables.Empty;

    /// <summary>The local player's level and gear score (vfunc_78, FUN_14152ea50); null without a local player.</summary>
    public Func<(ushort Level, uint GearScore)?> LocalPlayer = () => null;
    /// <summary>The local character id (IsLocalPlayerLeader's compare).</summary>
    public Func<long> SelfCharacterId = () => 0;
    /// <summary>GetDungeonEnterState of the local player (the clear state 0..18, 10 and up refuse an entry); null as unknown.</summary>
    public Func<uint, int?> ClearState = _ => null;
    /// <summary>GetMemberClearCount of a party member (counters.Get(room)).</summary>
    public Func<long, uint, int?> MemberClearState = (_, _) => null;
    /// <summary>FUN_140930b40: the account session state that hides the dungeon features. STILL-OPEN: no feed, false.</summary>
    public Func<bool> SessionBlocksDungeon = () => false;
    /// <summary>DAT_143ab2878 == 2: the state that disables the party search button. STILL-OPEN: no feed, false.</summary>
    public Func<bool> PartySearchBlocked = () => false;
    /// <summary>CFeatureManager::IsFeatureEnabled; the model's after the load.</summary>
    public Func<int, bool> IsFeatureEnabled;
    /// <summary>GetMyCharacter()->GetRecord(): the server clock.</summary>
    public Func<long> ServerTime;
    /// <summary>RewardContentsTable::GetReward(rewardId, level) for the weekly step: the first item's binding string, the meso and the exp
    /// (before the boost rates). STILL-OPEN: null, so the three members stay unset.</summary>
    public Func<uint, ushort, (string Binding, long Meso, long Exp)?> WeeklyRewardPreview;

    /// <summary>The packets the find component sends (FUN_140710590).</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>ToggleDialog(0x4eda): the party search window.</summary>
    public Action TogglePartySearch = () => { };
    /// <summary>GetDungeonSearchSettingDialog(0x4f86)->SetSelected(bookmarks) and its open.</summary>
    public Action<IReadOnlyCollection<uint>> OpenSetting = _ => { };
    /// <summary>ToggleDialog(0x4f85): the detail popup.</summary>
    public Action TogglePopupDetail = () => { };
    /// <summary>OnBookmark: RequestCloseDialog(0x4ebd), the room dialog's ResetGroup, OpenDialog(0x4ebd).</summary>
    public Action OpenBookmarkRoomDialog = () => { };
    /// <summary>OpenDungeonRoomDialog: RequestCloseDialog(0x4ebd), OpenRoom(id), OpenDialog(0x4ebd).</summary>
    public Action<uint> OpenRoomDialog = _ => { };

    // m_nSubTab and m_bookmarks.
    int subTab;
    readonly SortedSet<uint> bookmarks = new();

    public int SubTab => subTab;
    /// <summary>m_bookmarks, in the set's order.</summary>
    public IReadOnlyCollection<uint> Bookmarks => bookmarks;

    bool Feature(int id) => IsFeatureEnabled != null ? IsFeatureEnabled(id) : Host != null && Model.IsFeatureEnabled(id);

    /// <summary>SetBookmarks (@0x140c0ba70): the set is replaced.</summary>
    public void SetBookmarks(IEnumerable<uint> ids)
    {
        bookmarks.Clear();
        foreach (uint id in ids) bookmarks.Add(id);
    }

    /// <summary>FUN_140c0dc60 (@0x140c0dc60): the lobby (the RoomDungeon byte) a group type enters; -1 for none.</summary>
    public static int LobbyOfGroup(int groupType) => groupType switch { 3 => 4, 4 => 0xc, 0xd => 0xd, 5 => 0xe, _ => -1 };

    // ---- settings (vfunc_53, vfunc_54) ----

    /// <summary>vfunc_53 (@0x140c0b800): the attribute "dungeon" is the bookmarks, each id followed by a comma.</summary>
    protected override void SaveSettings(DialogSettings s)
    {
        var text = new System.Text.StringBuilder();
        foreach (uint id in bookmarks) text.Append(id.ToString(CultureInfo.InvariantCulture)).Append(',');
        s.SetAttr("dungeon", text.ToString());
    }

    /// <summary>vfunc_54 (@0x140c0b920): the set is cleared and XmlTokenList reads "dungeon"; only the ids the room table has are kept.</summary>
    protected override void LoadSettings(DialogSettings s)
    {
        bookmarks.Clear();
        if (s.GetAttrText("dungeon") is not { } text) return;
        foreach (var token in text.Split(new[] { ',', ' ', '\t', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries))
            if (uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) && Tables.DungeonRooms.GetRoom(id) != null)
                bookmarks.Add(id);
    }

    // ---- the party and the rules of the lists ----

    /// <summary>The level and gear score the lists are for: in a party the lowest of the online members (GetMinMemberLevel and
    /// GetMinMemberGearScore, the first online member starting the minimum), else the player's. Null without a local player.</summary>
    (ushort Level, uint GearScore)? ListLevel()
    {
        if (LocalPlayer() is not { } me) return null;
        if (book == null || !book.IsInParty) return me;
        ushort level = 0;
        uint gear = 0;
        bool first = true;
        foreach (var m in book.Members)
        {
            if (m.Offline) continue;
            ushort l = (ushort)m.User.Level;
            uint gs = (uint)m.User.GearScore;
            if (first) { level = l; gear = gs; first = false; continue; }
            if (l < level) level = l;
            if (gs < gear) gear = gs;
        }
        return (level, gear);
    }

    /// <summary>CPartyManager::CanEnterDungeonRoom (@0x14162ac70, partymanager.cpp:561): alone the player's enter state is under 10; in a party
    /// every member's is. An unknown state counts as 0.</summary>
    public bool CanEnterDungeonRoom(uint roomId)
    {
        if (book == null || !book.IsInParty) return (ClearState(roomId) ?? 0) < 10;
        foreach (var m in book.Members)
            if ((MemberClearState(m.CharacterId, roomId) ?? 0) >= 10) return false;
        return true;
    }

    /// <summary>GetRecommendedRooms (@0x140c0d800): GetRoomsForLevel of the list level, each room whose minimum level the level reaches that
    /// the party can enter, that is not hidden (m_bHidden is the room row's isDisableRandomMatch), and whose group id (representID, when
    /// not 0) no earlier pick has; at most <see cref="RecommendedCount"/>.</summary>
    public List<DungeonRoomData> GetRecommendedRooms()
    {
        var res = new List<DungeonRoomData>();
        if (ListLevel() is not { } lv) return res;
        var groups = new HashSet<uint>();
        foreach (var room in Tables.DungeonRooms.GetRoomsForLevel(lv.Level, lv.GearScore))
        {
            if (lv.Level < room.LimitPlayerLevel) continue;
            if (!CanEnterDungeonRoom(room.Id)) continue;
            if (room.IsDisableRandomMatch) continue;
            if (room.RepresentId != 0 && groups.Contains(room.RepresentId)) continue;
            groups.Add(room.RepresentId);
            res.Add(room);
            if (res.Count >= RecommendedCount) break;
        }
        return res;
    }

    /// <summary>FUN_140c0dab0 (@0x140c0dab0): GetMatchRooms of the list level, the rooms the party can enter that are not hidden.</summary>
    public List<DungeonRoomData> GetEnterableRooms()
    {
        var res = new List<DungeonRoomData>();
        if (ListLevel() is not { } lv) return res;
        foreach (var room in Tables.DungeonRooms.GetMatchRooms(lv.Level, lv.GearScore))
            if (CanEnterDungeonRoom(room.Id) && !room.IsDisableRandomMatch) res.Add(room);
        return res;
    }

    /// <summary>FUN_140e146a0 (@0x140e146a0): the find component's enabled flag: false while FUN_140930b40, or in a party without being its leader.</summary>
    public bool FindEnabled() =>
        !SessionBlocksDungeon() && !(book != null && book.IsInParty && !book.IsLeader(SelfCharacterId()));

    // ---- load ----

    /// <summary>
    /// OnLoadComplete (@0x140c0b180, :353): UIDialog's, the m_webGuideButton hidden, the sub tab of the player's level, UpdateWeeklyReward,
    /// UpdateFindComponent, then tab 0 built. The 0x53 and 0x1a handlers the client registers here are <see cref="OnPartyApplied"/> and
    /// <see cref="OnRewardPacket"/> (the owner feeds them). The close button is bound as the other dialogs do.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_closeButton");
        if (Host.Child("m_webGuideButton") is { } web) Host.SetVisible(web, false);
        if (LocalPlayer() is { } me) subTab = Tables.DungeonCategories.SubTabOfLevel(0, me.Level);
        UpdateWeeklyReward();
        UpdateFindComponent();
        ShowTab(0);
    }

    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("dungeon control center: " + name); return; }
        Func<object[], object> f = _ => { Close(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
    }

    // ---- the movie's calls (vfunc_28) ----

    public override bool OnCallback(string name, object[] args)
    {
        if (base.OnCallback(name, args)) return true;
        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        object Arg(int i) => args != null && i < args.Length ? args[i] : null;
        bool Has() => args != null && args.Length > 0;
        if (Is(name, "onTabChange")) { if (Has()) ShowTab(Rt.ToInt32(Arg(0))); return true; }
        if (Is(name, "onSubTabChange")) { if (Has()) { subTab = Rt.ToInt32(Arg(0)); ShowSubTab(true); } return true; }
        if (Is(name, "onPartySearch")) { TogglePartySearch(); return true; }
        if (Is(name, "onPartyMatch")) { Send(new PartyDungeonMatchRequest(bookmarks)); return true; }
        if (Is(name, "onPartyMatchCancel")) { Send(new PartyDungeonMatchCancelRequest()); return true; }
        if (Is(name, "onSetting")) { OnSetting(); return true; }
        if (Is(name, "onPopupDetail")) { TogglePopupDetail(); return false; }
        if (Is(name, "onBookmark")) { OpenBookmarkRoomDialog(); return true; }
        if (Is(name, "onEnterLobby"))
        {
            if (Has()) SendRoomDungeon(Rt.ToInt32(Arg(0)));
            return true;
        }
        if (Is(name, "onClickTile"))
        {
            if (Has()) OpenRoomDialog((uint)Rt.ToInt32(Arg(0)));
            return true;
        }
        return false;
    }

    /// <summary>SendRoomDungeon (@0x140c0ce20, :668): RoomDungeon with the group's lobby; nothing for a group without one.</summary>
    public void SendRoomDungeon(int groupType)
    {
        int lobby = LobbyOfGroup(groupType);
        if (lobby != -1) Send(new RoomDungeonLobbyRequest((byte)lobby));
    }

    /// <summary>OnSetting (@0x140c0ca80): empty bookmarks become the ids of <see cref="GetEnterableRooms"/>; then the setting dialog opens
    /// on them.</summary>
    public void OnSetting()
    {
        if (bookmarks.Count == 0)
            foreach (var room in GetEnterableRooms()) bookmarks.Add(room.Id);
        OpenSetting(bookmarks);
    }

    // ---- packets ----

    /// <summary>OnPartyMatchPacket (@0x140c0cea0): the commands 2, 3, 7, 8, 9 and 0x1e of the Party packet refresh the find component.</summary>
    public void OnPartyApplied(PartyMessage message)
    {
        if (message.Mode is 2 or 3 or 7 or 8 or 9 or 0x1e) UpdateFindComponent();
    }

    /// <summary>OnDungeonRewardPacket (@0x140c0cf20): the command 0x17 of the RoomDungeon packet refreshes the weekly reward.</summary>
    public void OnRewardPacket(int command)
    {
        if (command == 0x17) UpdateWeeklyReward();
    }

    // ---- the movie's data ----

    /// <summary>UpdateFindComponent (@0x140c0c430): setMatch(IsMatchSearching(1)), setEnabled(FUN_140e146a0()) and the party search button's
    /// enabled flag (DAT_143ab2878 != 2).</summary>
    public void UpdateFindComponent()
    {
        if (Host?.Entity == null || Host.Child("m_dungeonFindComponent") is not { } comp) return;
        Host.Invoke(comp, "setMatch", book != null && book.IsMatchSearching(1));
        Host.Invoke(comp, "setEnabled", FindEnabled());
        if (Host.Child("m_partySearch", comp) is { } button) Host.Set(button, "enabled", !PartySearchBlocked());
    }

    /// <summary>
    /// UpdateWeeklyReward (@0x140c0bab0, :458): the weekly record (room 0x3ee) gives "weekly" and "daily"; the next united weekly step is the
    /// first whose count the weekly count has not reached (none: the maximum, complete, "count" the complete text; else the remaining
    /// count); that step's reward gives binding, meso and exp (STILL-OPEN, see <see cref="WeeklyRewardPreview"/>); then
    /// m_dungeonRewardComponent.setData.
    /// </summary>
    public void UpdateWeeklyReward()
    {
        if (Host?.Entity == null || LocalPlayer() is not { } me) return;
        var data = new Dictionary<string, object>();
        var rooms = Tables.DungeonRooms;
        int weekly = 0, weeklyMax = 0;
        var record = Records.GetRecord(WeeklyRecordRoom);
        {
            weekly = Records.BonusRewardCountNow(record);
            weeklyMax = Records.MaxCount(record);
            data["weekly"] = ClientFormat.Format(Common("s_dungeon_control_center_reward_weekly"), (uint)weekly, (uint)weeklyMax);
            int daily = DungeonRecordBook.ClearCount(record, Now());
            uint dailyMax = rooms.GetRoom(WeeklyRecordRoom)?.SubRewardCount ?? 0;
            data["daily"] = ClientFormat.Format(Common("s_dungeon_control_center_reward_daily"), (uint)daily, dailyMax);
        }
        uint next = 0;
        for (int i = 0; i < rooms.UnitedWeeklyRewardCount; i++)
        {
            if (rooms.GetUnitedWeeklyReward(i) is not { } step) continue;
            if ((uint)weekly < step.RewardCount) { next = step.RewardCount; break; }
        }
        if (next == 0)
        {
            next = (uint)weeklyMax;
            data["complete"] = 1;
            data["count"] = Common("s_dungeon_control_center_require_reward_complete");
        }
        else
        {
            data["complete"] = 0;
            data["count"] = ClientFormat.Format(Common("s_dungeon_control_center_require_reward_count"), (uint)(next - (uint)weekly));
        }
        if (rooms.FindUnitedWeeklyReward(next) is { } reward && WeeklyRewardPreview?.Invoke(reward.RewardId, me.Level) is { } preview)
        {
            long meso = preview.Meso, exp = preview.Exp;
            if (Feature(0xb9) && Records.IsBoostActive())
            {
                meso *= Records.BoostB;             // GetRateB
                exp *= Records.BoostA;              // GetRateA
            }
            if (preview.Binding != null) data["binding"] = preview.Binding;
            data["meso"] = NumberText.Group(meso.ToString(CultureInfo.InvariantCulture));
            data["exp"] = ClientFormat.Format(Common("s_number_string_exp"), NumberText.Group(exp.ToString(CultureInfo.InvariantCulture)));
        }
        if (Host.Child("m_dungeonRewardComponent") is { } comp) Host.Invoke(comp, "setData", data);
    }

    long Now() => ServerTime != null ? ServerTime() : Records.ServerTime();

    string Common(string key) => Tables.Common(key) ?? "";

    /// <summary>FUN_140c0c600 (@0x140c0c600): tab 0 builds the level sub tab's list, tab 1 the second tab's; another tab does nothing.</summary>
    public void ShowTab(int tab)
    {
        if (tab == 0) ShowSubTab(false);
        else if (tab == 1) ShowEventTab();
    }

    /// <summary>FUN_140c0c660 (@0x140c0c660): the sub tab's list under the recommended entry; updateTab(obj) on a sub tab change, else
    /// buildTab(0, obj) with the largest sub tab level and the shown index.</summary>
    void ShowSubTab(bool update)
    {
        if (Host?.Entity == null) return;
        var categories = Tables.DungeonCategories.GetSubTabs(0);
        if (subTab >= categories.Count || subTab < 0) return;
        var list = new List<object>();
        AddRecommended(list);
        AddCategory(categories[subTab], list);
        var obj = new Dictionary<string, object> { ["dungeon"] = list };
        if (update) Host.Invoke("updateTab", obj);
        else
        {
            obj["dungeonLv"] = (uint)Tables.DungeonCategories.GetMaxSubTabLevel(0);
            obj["idx"] = subTab;
            Host.Invoke("buildTab", 0, obj);
        }
    }

    /// <summary>FUN_140c0c8e0 (@0x140c0c8e0): the first sub tab of the second main tab; nothing without one.</summary>
    void ShowEventTab()
    {
        if (Host?.Entity == null) return;
        var categories = Tables.DungeonCategories.GetSubTabs(1);
        if (categories.Count == 0) return;
        var list = new List<object>();
        AddCategory(categories[0], list);
        Host.Invoke("buildTab", 1, new Dictionary<string, object> { ["dungeon"] = list });
    }

    /// <summary>One list entry of a room: id, banner and title (s_dungeon_control_center_dungeon_title with its minimum level and its name from string table 0x14).</summary>
    Dictionary<string, object> RoomItem(DungeonRoomData room) => new()
    {
        ["id"] = room.Id,
        ["banner"] = room.DungeonBanner ?? "",
        ["title"] = ClientFormat.Format(Common("s_dungeon_control_center_dungeon_title"), room.LimitPlayerLevel,
            Tables.FieldEnteranceName((int)room.Id) ?? ""),
    };

    /// <summary>FUN_140c0d260 (@0x140c0d260): the recommended entry (isRecommend 1, its category text and the recommended rooms the party can
    /// enter that are not hidden).</summary>
    void AddRecommended(List<object> output)
    {
        var items = new List<object>();
        foreach (var room in GetRecommendedRooms())
            if (CanEnterDungeonRoom(room.Id) && !room.IsDisableRandomMatch) items.Add(RoomItem(room));
        output.Add(new Dictionary<string, object>
        {
            ["isRecommend"] = 1,
            ["category"] = Common("s_dungeon_control_center_recommend_category"),
            ["list"] = items,
        });
    }

    /// <summary>FUN_140c0d620 (@0x140c0d620): one entry per available group type of the sub tab that has rooms: isRecommend 0, the group's
    /// name, groupType, hasLobby and the rooms (FUN_140c0cf80).</summary>
    void AddCategory(DungeonCategory category, List<object> output)
    {
        foreach (int type in category.GroupTypes)
        {
            if (!DungeonRoomDialogController.GroupAvailable(type, Feature)) continue;
            var items = new List<object>();
            foreach (var room in Tables.DungeonRooms.GetGroupRooms(type))
            {
                if (category.IsValid && !category.Contains(room.LimitPlayerLevel)) continue;
                items.Add(RoomItem(room));
            }
            if (items.Count == 0) continue;
            output.Add(new Dictionary<string, object>
            {
                ["isRecommend"] = 0,
                ["category"] = GroupName(type),
                ["groupType"] = (uint)type,
                ["hasLobby"] = LobbyOfGroup(type) != -1,
                ["list"] = items,
            });
        }
    }

    /// <summary>TranslateEnumDungeonGroup: s_enum_dungeon_group_&lt;key&gt;, "" outside the list.</summary>
    string GroupName(int type) => type > 0 && type < AchieveController.DungeonGroupKeys.Length
        ? Common("s_enum_dungeon_group_" + AchieveController.DungeonGroupKeys[type]) : "";
}
