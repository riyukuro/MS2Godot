using Ms2.Avm2;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the dungeon room window, UIDungeonRoomDialog (client/ms2/uidungeonroomdialog.cpp, 0x1412a74a0-0x1412c2020; UI id 0x4ebd),
/// for its movie uidungeonroomdialog. A group drop-down (m_dungoenGroupCombo, the movie's spelling) picks the group of rooms, a tile list
/// (m_dungeonList, 21 renderers m_dungeonListItem1..21) shows the rooms of the group, and the room's panel (UIDungeonRoomInfo, on
/// m_dungeonInfo) shows the selected room. This class is the window itself: the group and room selection, the list texts, the season
/// summary tooltip and the packet refreshes; the room panel is <see cref="DungeonRoomInfoPanel"/>.
/// STILL-OPEN: the panel's own list (see its summary: the reward preview and the hooks of <see cref="DungeonRoomHooks"/>);
/// the favourites group 0 (the character record's GetSeasonRooms); RefreshSeasonSummary's season values (every branch of the decompiled
/// function ends with vfunc_4(false), so the summary is hidden; the real gauge and text are not recovered); the m_cooldownIcon tooltip (the
/// record manager's expire date); the achievement tooltip of m_requireCondition (ShowAchieveTooltip); FUN_141924630 (the guard before the
/// 0x1a refreshes, treated as true); the local player's gear score (a hook, no source yet).
/// </summary>
public sealed class DungeonRoomDialogController : UiController
{
    public const int UiId = 0x4ebd;
    /// <summary>The count of list renderers the movie declares (OnLoadComplete's loop, uidungeonroomdialog.cpp:1837).</summary>
    public const int ItemCount = 21;
    /// <summary>The group index the room list is shown for before a pick (m_180's start), and the groups the combo offers (0..15).</summary>
    public const int GroupCount = 16;

    /// <summary>The window's key action: CKeyTable::FUN_14142b300 case 400037 runs ToggleDialog(0x4ebd) (keytable.cpp:828); the default key
    /// table binds DIK 40 to it.</summary>
    public const long OpenKeyAction = 400037;

    /// <summary>True when DirectInput key <paramref name="dik"/> is bound to <see cref="OpenKeyAction"/> in <paramref name="keys"/>.</summary>
    public static bool OpensWithKey(KeyTable keys, int dik)
    {
        foreach (int k in keys.KeysOfAction(OpenKeyAction))
            if (k == dik) return true;
        return false;
    }

    public override string Movie => "uidungeonroomdialog";
    protected override string EntityMethod => "alignment";
    /// <summary>GetTypeName.</summary>
    public override string TypeName => "DungeonRoomDialog";

    readonly PartyBook book;

    public DungeonRoomDialogController(PartyBook book = null) { this.book = book; }

    /// <summary>The party (TSingleton PartyManager); null without one.</summary>
    internal PartyBook Party => book;
    /// <summary>The data the room panel asks other managers for (see <see cref="DungeonRoomHooks"/>).</summary>
    public DungeonRoomHooks Hooks { get; } = new();
    /// <summary>The record manager (DungeonRecordManagerCli) the panel reads; the owner sets it to the session's book.</summary>
    public DungeonRecordBook Records { get; set; } = new();
    /// <summary>The room panel (UIDungeonRoomInfo, m_dungeonInfo); null before the load and after Close.</summary>
    public DungeonRoomInfoPanel Panel { get; private set; }
    /// <summary>The packets the room panel sends and the boxes it shows.</summary>
    public Action<IOutPacket> Send = _ => { };
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>ShowSystemNotice(text, 1, 1): a system notice of the text itself (<see cref="Notice"/> takes a common string key).</summary>
    public Action<string> NoticeText = _ => { };

    // The client's fields.
    int portalId;                 // m_178
    int groupType;                // m_17c: the group type to open on
    int currentGroup;             // m_180: the group shown
    string summaryTooltip = "";   // m_188
    readonly List<DungeonRoomData> rooms = new();     // m_rooms
    readonly List<int> dungeonIds = new();            // m_dungeonIds: the group ids the combo lists
    int lastScroll = -1;
    bool applying;

    /// <summary>m_spRoom.</summary>
    public DungeonRoomData Room { get; private set; }
    /// <summary>m_rooms: the rooms of the shown group, in list order.</summary>
    public IReadOnlyList<DungeonRoomData> Rooms => rooms;
    /// <summary>m_dungeonIds: the group ids of the combo's rows.</summary>
    public IReadOnlyList<int> GroupIds => dungeonIds;
    /// <summary>m_180: the shown group.</summary>
    public int CurrentGroup => currentGroup;
    /// <summary>GetPortalId (m_178).</summary>
    public int PortalId => portalId;

    /// <summary>The tables the set-before-load calls (OpenRoom, OpenDungeonOrRoom) read while the movie is not loaded and the
    /// <see cref="UiController.Model"/> does not exist yet; the loaded dialog uses the model's.</summary>
    public GameTables PreloadTables { get; set; }

    /// <summary>The dungeon room tables (CDungeonRoomTable).</summary>
    internal DungeonRoomTable Table => (Host != null ? Model.Tables : PreloadTables ?? GameTables.Empty).DungeonRooms;

    /// <summary>The local player's level and gear score (vfunc_78, FUN_14152ea50); null without a local player.
    /// STILL-OPEN: the gear score has no source in the model yet, so the default is level only.</summary>
    public Func<(ushort Level, uint GearScore)?> LocalPlayer = () => null;
    /// <summary>FUN_140d73ff0: the system notice of a common string key.</summary>
    public Action<string> Notice = _ => { };

    string Common(string key) => Model.Tables.Common(key) ?? "";
    internal string CommonText(string key) => Common(key);

    // ---- the open calls (the callers set these before the movie loads) ----

    /// <summary>SetPortalId.</summary>
    public void SetPortalId(int id) => portalId = id;

    /// <summary>SetGroupType: the group type OnLoadComplete selects when no room is set.</summary>
    public void SetGroupType(int type) => groupType = type;

    /// <summary>FUN_1412b9110: m_180 = 0.</summary>
    public void ResetGroup() => currentGroup = 0;

    /// <summary>SetRoom: m_spRoom, and the room panel's SetRoom when it exists.</summary>
    public void SetRoom(DungeonRoomData room) { Room = room; Panel?.SetRoom(room); }

    /// <summary>
    /// OpenDungeonOrRoom (@0x1412b8f00): an id over 100000 is a room id (OpenRoom); else it is a dungeon id and its first room opens
    /// (GetFirstRoom; none shows s_msg_preparing and returns false).
    /// </summary>
    public bool OpenDungeonOrRoom(uint id)
    {
        if (id > 100000) return OpenRoom(id);
        var room = Table.GetFirstRoom(id);
        if (room == null) { Notice("s_msg_preparing"); return false; }
        return OpenRoom(room.Id);
    }

    /// <summary>OpenRoom (@0x1412b8ff0): the room of the id, or s_msg_preparing and false.</summary>
    public bool OpenRoom(uint id)
    {
        var room = Table.GetRoom(id);
        if (room == null) { Notice("s_msg_preparing"); return false; }
        SetRoom(room);
        return true;
    }

    // ---- group mapping (uidungeonhelper.cpp) ----

    /// <summary>TypeOfGroup: the dungeon group type a combo group id stands for (0 for the favourites and anything else).</summary>
    public static int TypeOfGroup(int group) => group switch
    {
        2 => 1, 3 => 2, 4 => 3, 5 => 4, 6 => 5, 8 => 6, 9 => 7, 10 => 8, 11 => 12, 13 => 9, 14 => 10, 15 => 11, 12 => 13, 7 => 14,
        _ => 0,
    };

    /// <summary>GroupOfType: the inverse of <see cref="TypeOfGroup"/>; 1 (the recommended group) for any other type.</summary>
    public static int GroupOfType(int type) => type switch
    {
        1 => 2, 2 => 3, 3 => 4, 4 => 5, 5 => 6, 6 => 8, 7 => 9, 8 => 10, 12 => 11, 9 => 13, 10 => 14, 11 => 15, 13 => 12, 14 => 7,
        _ => 1,
    };

    /// <summary>FUN_140e14520 (uidungeonhelper.cpp:303): the feature switch each dungeon group type needs; true for the others.</summary>
    public bool IsGroupAvailable(int type) => type switch
    {
        3 => Model.IsFeatureEnabled(0x80), 4 => Model.IsFeatureEnabled(0x76), 5 => Model.IsFeatureEnabled(0xcb),
        6 => Model.IsFeatureEnabled(0x79), 7 => Model.IsFeatureEnabled(5), 8 => Model.IsFeatureEnabled(0xaa),
        12 => Model.IsFeatureEnabled(0xab), 9 => Model.IsFeatureEnabled(0x4c), 10 => Model.IsFeatureEnabled(0x4b),
        11 => Model.IsFeatureEnabled(0x55), 13 => Model.IsFeatureEnabled(0xf0), 14 => Model.IsFeatureEnabled(0x103),
        _ => true,
    };

    /// <summary>TranslateEnumDungeonGroup: s_enum_dungeon_group_&lt;key&gt;, "" outside the list.</summary>
    string GroupName(int type) => type > 0 && type < AchieveController.DungeonGroupKeys.Length
        ? Common("s_enum_dungeon_group_" + AchieveController.DungeonGroupKeys[type]) : "";

    // ---- load and close ----

    /// <summary>
    /// OnLoadComplete (@0x1412b7220, uidungeonroomdialog.cpp:1837): UIDialog's, the drag bar, BindCloseButton("m_closeButton"), the room
    /// panel on m_dungeonInfo (the load ends without it), the combo, list, season summary, 21 items and gauge, the combo's and list's
    /// "listIndexChange" handlers, the items' data handlers, RefreshGroupList, then the group and room: the group of the room or of
    /// groupType, SelectGroup, falling back to group 1, group 2 or a close; Refresh at the end.
    /// The panel is built before the group is selected, and gets the room set so far. STILL-OPEN: the packet registrations (0x53, the debug-mode and enter-state notifies) are
    /// made by the owner of the dialog (<see cref="OnPartyApplied"/>, <see cref="Refresh"/>).
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_closeButton");
        if (Host.Child("m_dungeonInfo") == null) { Host.RT.NoteMissing("dungeon room dialog: m_dungeonInfo"); return; }
        var combo = Host.Child("m_dungoenGroupCombo");
        var list = Host.Child("m_dungeonList");
        if (combo == null || list == null) { Host.RT.NoteMissing("dungeon room dialog: combo or list"); return; }
        Records.Rooms ??= Table;
        Panel = new DungeonRoomInfoPanel(this, Host.Child("m_dungeonInfo")) { Send = p => Send(p), ShowBox = b => ShowBox(b) };
        if (Room != null) Panel.SetRoom(Room);
        AddHandler(combo, OnGroupChanged);
        AddHandler(list, OnRoomSelected);

        RefreshGroupList();
        int group = groupType != 0 ? GroupOfType(groupType) : 0;
        uint roomId = 0;
        if (Room != null) { group = GroupOfType(Room.GroupType); roomId = Room.Id; }
        if (!SelectGroup(group, roomId))
        {
            if (TypeOfGroup(group) != 0)
            {
                Notice("s_room_dungeon_notFoundList");
                Close();
                return;
            }
            if (!SelectGroup(1, 0) && !SelectGroup(2, 0)) { Close(); return; }
        }
        Refresh();
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the child's click runs the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("dungeon room dialog: " + name); return; }
        Func<object[], object> f = _ => { Close(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
    }

    /// <summary>AddListIndexChangeHandler: the AS event "listIndexChange" (asdropdownmenu.cpp:102, ascorelist.cpp:81).</summary>
    void AddHandler(object target, Action<int> handler)
    {
        Func<object[], object> f = a =>
        {
            Host.RT.Guard("dungeon room listIndexChange", () => handler(IndexOf(target)));
            return null;
        };
        Host.Invoke(target, "addEventListener", "listIndexChange", f, false, 0, false);
    }

    int IndexOf(object target) => Rt.IsNullish(Host.Get(target, "selectedIndex")) ? -1 : Rt.ToInt32(Host.Get(target, "selectedIndex"));

    /// <summary>
    /// vfunc_26 (@0x1412b82f0, uidungeonroomdialog.cpp:1909): UIDialog's close, the notify and 0x53 registrations dropped (the owner's),
    /// m_178 = 0, the native bindings of m_partyMembers, m_cooldownIcon, m_requireCondition and m_seasonSummary unbound (FUN_1422c63f0,
    /// native side only), the children let go, the room cleared (SetRoom(null)), m_17c = 0.
    /// </summary>
    public override void Close()
    {
        base.Close();
        Panel = null;
        portalId = 0;
        groupType = 0;
        Room = null;
        rooms.Clear();
        dungeonIds.Clear();
        lastScroll = -1;
    }

    // ---- packets ----

    /// <summary>The 0x53 (Party) handler (OnPartyPacket): the commands 2, 3, 9 and 25 refresh.</summary>
    public void OnPartyApplied(PartyMessage message)
    {
        if (message.Mode is 2 or 3 or 9 or 25) Refresh();
    }

    /// <summary>OnRoomPacket (@0x1412b8d60, packet 0x1a): command 6 or 20 refreshes; command 7 with type 5 while no group is shown shows group 0.
    /// STILL-OPEN: FUN_141924630, the guard of the refresh, is not recovered (treated as true).</summary>
    public void OnRoomPacket(int cmd, int type)
    {
        if (cmd is 6 or 20) Refresh();
        else if (cmd == 7 && type == 5 && currentGroup == 0) ShowGroup(0, 0);
    }

    /// <summary>OnRoomPacket53 (@0x1412b8e10, :2032): the commands 2, 7, 8, 9 and 30 of the Party packet refresh the search button and the
    /// searching indicator. STILL-OPEN: FUN_141924630, the guard, is treated as true.</summary>
    public void OnRoomPacket53(int cmd)
    {
        if (cmd is 2 or 7 or 8 or 9 or 30 && Panel != null)
        {
            Panel.RefreshPartySearchButton();
            Panel.RefreshSearchingIndicator();
        }
    }

    /// <summary>
    /// ShowEnterLimitMessage (@0x1412b9870, :2226): the system notice of the reason an entry was refused (12 the level, 13 the achievement,
    /// 14 VIP, 15 the gear score, 16 a dungeon to clear, 17 the additional effects, 18 the recommended weapon); no room, common string 0x99a.
    /// STILL-OPEN: the achievement's name needs <see cref="DungeonRoomHooks.AchieveName"/>.
    /// </summary>
    public void ShowEnterLimitMessage(uint roomId, int reason)
    {
        var room = Table.GetRoom(roomId);
        if (room == null) { NoticeText(Model.Tables.Common(0x99a) ?? ""); return; }
        string text = reason switch
        {
            12 => ClientFormat.Format(Model.Tables.Common(0x99b) ?? "", (int)room.LimitPlayerLevel),
            13 => ClientFormat.Format(Model.Tables.Common(0x99c) ?? "", Hooks.AchieveName?.Invoke(room.LimitAchieveId) ?? ""),
            14 => Common("s_field_limit_disable_vip"),
            15 => Common("s_field_limit_gearScore"),
            16 => Common("s_field_limit_clear_dungeon"),
            17 => ClientFormat.Format(Common("s_field_limit_additionalEffect"), string.Concat(room.LimitAdditionalEffects.Select(
                id => "[" + (Model.Tables.StringTableText(0x4c, id) ?? "") + "] "))),
            18 => Common("s_dungeon_enter_limit_recommend_weapon"),
            _ => null,
        };
        if (text != null) NoticeText(text);
    }

    // ---- refresh ----

    /// <summary>
    /// Refresh (@0x1412b9130): nothing without the layout, a local player and a room; then the room panel's Refresh, the sub title and
    /// the season summary.
    /// </summary>
    public void Refresh()
    {
        if (Host?.Entity == null || LocalPlayer() == null || Room == null) return;
        Panel?.Refresh();
        RefreshSubTitle();
        RefreshSeasonSummary();
    }

    /// <summary>RefreshSubTitle: s_dungeonRoom_subTitle with the room's group and its most users, in m_subTitle.</summary>
    void RefreshSubTitle()
    {
        var text = ClientFormat.Format(Common("s_dungeonRoom_subTitle"), GroupName(Room.GroupType), Room.MaxUserCount);
        Host.SetChildText("m_subTitle", text);
    }

    /// <summary>
    /// RefreshSeasonSummary (@0x1412b9370): as decompiled every branch (feature 0x72 off, not a season rank room, and the rest) ends in
    /// m_seasonSummary->vfunc_4(false), so the summary is hidden. STILL-OPEN: the season values the shown branch would set (the gauge
    /// m_summaryGauge and the tooltip text m_188).
    /// </summary>
    void RefreshSeasonSummary()
    {
        if (Host.Child("m_seasonSummary") is { } s) Host.SetVisible(s, false);
    }

    // ---- groups ----

    /// <summary>
    /// RefreshGroupList (@0x1412ba490): m_dungeonIds lists the groups 0..15 whose type is 0 (favourites and recommended) or available;
    /// the combo is cleared, sized to the count and given each group's label (group 1 s_dungeonRoom_groupList_recommand, group 0
    /// s_dungeonRoom_groupList_favorite, else the type's group name).
    /// </summary>
    public void RefreshGroupList()
    {
        dungeonIds.Clear();
        for (int g = 0; g < GroupCount; g++)
        {
            int type = TypeOfGroup(g);
            if (type != 0 && !IsGroupAvailable(type)) continue;
            dungeonIds.Add(g);
        }
        if (Host?.Entity == null || Host.Child("m_dungoenGroupCombo") is not { } combo) return;
        var dp = Host.Get(combo, "dataProvider");
        if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");
        Host.Set(combo, "menuRowCount", (uint)dungeonIds.Count);
        if (Rt.IsNullish(dp)) return;
        foreach (var g in dungeonIds)
        {
            string label = g == 1 ? Common("s_dungeonRoom_groupList_recommand")
                : g == 0 ? Common("s_dungeonRoom_groupList_favorite")
                : GroupName(TypeOfGroup(g));
            Host.Invoke(dp, "push", new Dictionary<string, object> { ["label"] = label, ["index"] = g });
        }
    }

    /// <summary>SelectComboGroup: the combo's row of the group; setting it raises "listIndexChange" (OnGroupChanged), as in the client.</summary>
    void SelectComboGroup(int group)
    {
        int index = dungeonIds.IndexOf(group);
        if (index < 0 || Host.Child("m_dungoenGroupCombo") is not { } combo) return;
        Host.Set(combo, "selectedIndex", index);
    }

    /// <summary>
    /// SelectGroup (@0x1412ba3a0): the combo row, the group's rooms, then, when no room is set, the first one (false with none).
    /// </summary>
    public bool SelectGroup(int group, uint roomId)
    {
        SelectComboGroup(group);
        ShowGroup(group, roomId);
        if (Room == null)
        {
            if (rooms.Count == 0) return false;
            SetRoom(rooms[0]);
            if (Host.Child("m_dungeonList") is { } list) Host.Set(list, "selectedIndex", 0);
        }
        return true;
    }

    /// <summary>GetPartyLevel (@0x1412bab90): (0, 0) without a player; in a party the lowest level and gear score of the online members
    /// (CPartyManager::GetMinMemberLevel / GetMinMemberGearScore, the first online member starting the minimum); else the player's.</summary>
    public (ushort Level, uint GearScore) GetPartyLevel()
    {
        if (LocalPlayer() is not { } me) return (0, 0);
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

    /// <summary>
    /// ShowGroup (@0x1412ba830): the rooms of the group in m_rooms. Group 1 is the recommended: the party's level picks the match rooms
    /// (hard, then normal, reversed); a level of 0 stops before the list is touched. Group 0 is the favourites (STILL-OPEN). Any other
    /// group lists the rooms of its type. The list is reset, sized to the rooms (SetEmptyItemCount) and redrawn, and the room of
    /// <paramref name="roomId"/> (else the first) selected.
    /// </summary>
    public void ShowGroup(int group, uint roomId)
    {
        rooms.Clear();
        currentGroup = group;
        if (group == 1)
        {
            var (level, gear) = GetPartyLevel();
            if (level == 0) return;
            rooms.AddRange(Table.GetGroupMatchRooms(DungeonRoomTable.GroupMatchHard, level, gear));
            rooms.AddRange(Table.GetGroupMatchRooms(DungeonRoomTable.GroupMatchNormal, level, gear));
            rooms.Reverse();
        }
        else if (group != 0)
            rooms.AddRange(Table.GetGroupRooms(TypeOfGroup(group)));
        // STILL-OPEN: group 0 (CMyCharacter's GetSeasonRooms) is empty.

        int select = 0;
        if (roomId > 0)
        {
            int i = rooms.FindIndex(r => r.Id == roomId);
            if (i >= 0) select = i;
        }
        if (Host?.Entity == null || Host.Child("m_dungeonList") is not { } list) return;
        Host.Set(list, "selectedIndex", -1);
        SetEmptyItemCount(list, rooms.Count);
        Host.Invoke(list, "invalidate");
        Host.Invoke(list, "validateNow");
        ApplyItems(list);
        Host.Set(list, "selectedIndex", select);
    }

    /// <summary>ASCoreList::SetEmptyItemCount: a DataProvider of that many empty values.</summary>
    void SetEmptyItemCount(object list, int count)
    {
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        for (int i = 0; i < count; i++) Host.Invoke(dp, "push", new Dictionary<string, object>());
        Host.Set(list, "dataProvider", dp);
    }

    // ---- list events ----

    /// <summary>OnGroupChanged: the combo's row (in range) shows its group.</summary>
    void OnGroupChanged(int index)
    {
        if (index < 0 || index >= dungeonIds.Count) return;
        ShowGroup(dungeonIds[index], 0);
    }

    /// <summary>OnRoomSelected: a list row in range of m_rooms, other than the current room, becomes the room and refreshes.</summary>
    void OnRoomSelected(int index)
    {
        if (applying || index < 0 || index >= rooms.Count) return;
        var room = rooms[index];
        if (Room != null && Room.Id == room.Id) return;
        SetRoom(room);
        Refresh();
    }

    // ---- the list items ----

    /// <summary>
    /// GetRoomListText (@0x140e14bd0, uidungeonhelper.cpp:378): "" for no room; else the room's name (string table 0x14 by room id), and
    /// with <paramref name="showLimit"/> and a level limit the format of s_dungeonRoom_list_limit_Level appended.
    /// </summary>
    public string GetRoomListText(DungeonRoomData room, bool showLimit)
    {
        if (room == null) return "";
        var text = Model.Tables.FieldEnteranceName((int)room.Id) ?? "";
        if (showLimit && room.LimitPlayerLevel > 0)
            text += ClientFormat.Format(Common("s_dungeonRoom_list_limit_Level"), (int)room.LimitPlayerLevel);
        return text;
    }

    /// <summary>
    /// OnListItemData (the renderer's data handler, index 0 = the room's row): out of range of m_rooms the item is disabled with no label;
    /// else enabled and labelled with <see cref="GetRoomListText"/>.
    /// </summary>
    public (bool Enabled, string Label) ItemData(int row)
        => row < 0 || row >= rooms.Count ? (false, "") : (true, GetRoomListText(rooms[row], true));

    /// <summary>
    /// The client sets each renderer's "fnSetData" (aslistitemrenderer.cpp:97) and nothing in the movie calls it (as3-runtime-gaps.md), so the
    /// data handler runs here: each of the 21 renderers reads its row (the renderer's "index") and takes <see cref="ItemData"/>.
    /// STILL-OPEN: redraws the list makes on its own (other than a scroll, see <see cref="Tick"/>) are not followed.
    /// </summary>
    void ApplyItems(object list)
    {
        applying = true;
        try
        {
            for (int i = 1; i <= ItemCount; i++)
            {
                if (Host.Child("m_dungeonListItem" + i) is not { } item) continue;
                var (enabled, label) = ItemData(Rt.ToInt32(Host.Get(item, "index")));
                Host.Set(item, "enabled", enabled);
                Host.Set(item, "label", label);
            }
        }
        finally { applying = false; }
    }

    /// <summary>The per-tick look at the list: a changed scroll position redraws the renderers, whose labels the redraw cleared.</summary>
    public void Tick()
    {
        if (Host?.Entity == null || IsClosed || Host.Child("m_dungeonList") is not { } list) return;
        int pos = Rt.ToInt32(Host.Get(list, "scrollPosition"));
        if (pos == lastScroll) return;
        lastScroll = pos;
        Host.Invoke(list, "validateNow");
        ApplyItems(list);
    }

    // ---- tooltips ----

    /// <summary>
    /// vfunc_49 (the tooltips of the movie's names; they need a room): "m_seasonSummary" shows m_188 (text tooltip, align 7); "m_requireCondition"
    /// shows the VIP text when the room is limited to VIP (s_vip_tooltip_info).
    /// "m_partyMembers" shows the panel's banner (ASTooltip(1, 2), align 5), the panel's reward, find and sub tab bar texts are answered
    /// by their names. STILL-OPEN: "m_cooldownIcon" (the record's expire date, FormatMonthDayWeekday) and the achievement tooltip of
    /// "m_requireCondition" (limitAchieveID != 0, ShowAchieveTooltip).
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (Room == null || args == null || args.Length == 0) return null;
        var name = Rt.ToString(args[0]);
        switch (name)
        {
            case "m_seasonSummary":
            {
                // ASTooltip(1, 2) with the class, delays, align 7 and the text m_188 set directly: an empty text still answers.
                var t = new AsTooltip { ["tooltip"] = "export_common_tooltip_tooltipText", ["onDelay"] = 300, ["offDelay"] = 60000, ["text"] = summaryTooltip };
                t.SetTooltipAlign(7);
                return t;
            }
            case "m_partyMembers":
            {
                var t = new AsTooltip();
                if (Panel == null || !t.ApplyTextTooltip(Panel.Banner)) return null;
                t.SetTooltipAlign(5);
                return t;
            }
            case "m_requireCondition":
                if (Room.LimitAchieveId == 0 && Room.LimitVip)
                {
                    var t = new AsTooltip();
                    return t.ApplyTextTooltip(Common("s_vip_tooltip_info")) ? t : null;
                }
                return null;
        }
        if (Panel?.TooltipText(name) is { } text)
        {
            var t = new AsTooltip();
            return t.ApplyTextTooltip(text) ? t : null;
        }
        return null;
    }
}
