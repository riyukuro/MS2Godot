using Ms2.Avm2;
using Ms2.Formats.World;

namespace Ms2.Ui.Hud;

/// <summary>
/// The dungeon finder's room picker, UIDungeonSearchSettingDialog (uidungeonsearchsettingdialog.cpp; UI id 0x4f86, a UIModalDialog,
/// movie uidungeonsearchsettingdialog, GetTypeName "DungeonSearchSettingDialog"). The control center's setting button
/// (<see cref="DungeonControlCenterController.OnSetting"/>) hands it the bookmarked rooms (SetSelected @0x140c112a0) and opens it; it
/// lists the rooms of the two dungeon groups (or one) as check boxes, red where the player cannot enter, and its OK button gives the
/// ticked ids back to the control center (SetBookmarks) and closes. The ctor (@0x140c0fdc0) sets SetTweenEnabled(1) and group 0.
/// STILL-OPEN: the optional m_groupDesc text (FUN_140d01340(target, 2, 2)) is not recovered, and the movie has no such member on its
/// root; the redraws the list makes on its own besides a scroll are not followed (<see cref="Tick"/>).
/// </summary>
public sealed class DungeonSearchSettingController : ModalDialogController
{
    public const int UiId = 0x4f86;                     // vfunc_48
    public override int TypeId => UiId;

    /// <summary>The check box rows of the movie (m_listItem1..11).</summary>
    public const int ItemCount = 11;
    /// <summary>The dropdown's rows: all, then the two dungeon groups (<see cref="SelectGroup"/>).</summary>
    public const int GroupRows = 3;

    public override string Movie => "uidungeonsearchsettingdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "DungeonSearchSettingDialog";
    protected override bool TweenEnabled => true;

    /// <summary>The colours of a room's text (FUN_14183e930): white where the player can enter it, red otherwise.</summary>
    public const int EnterableColor = 0xffffff, BlockedColor = 0xff0000;

    // m_selected (the ticked room ids), m_rooms (the shown rooms), m_nGroup and m_roomOfRow (the row's index into m_rooms).
    readonly SortedSet<uint> selected = new();
    readonly List<DungeonRoomData> rooms = new();
    readonly int[] roomOfRow = new int[ItemCount];
    int group;
    bool applying;
    int lastScroll = -1;
    object page;

    /// <summary>The ids ticked now (m_selected).</summary>
    public IReadOnlyCollection<uint> Selected => selected;
    /// <summary>The rooms the list shows now (m_rooms).</summary>
    public IReadOnlyList<DungeonRoomData> Rooms => rooms;
    /// <summary>m_nGroup: 0 both groups, 1 or 2 one.</summary>
    public int Group => group;

    /// <summary>The room table (GetGroupRooms).</summary>
    public Func<DungeonRoomTable> RoomTable = () => null;
    /// <summary>CanEnterDungeonRoom(room id): the control center's rule for the local player.</summary>
    public Func<uint, bool> CanEnterRoom = _ => false;
    /// <summary>The local player exists (the row fill returns without one).</summary>
    public Func<bool> HasLocalPlayer = () => true;
    /// <summary>GetDungeonControlCenter(0x4f84)->SetBookmarks(m_selected); not called while the control center is not open.</summary>
    public Action<IReadOnlyCollection<uint>> ApplyBookmarks = _ => { };

    string Common(string key) => Model.Tables.Common(key) ?? "";

    /// <summary>TranslateEnumDungeonGroup: s_enum_dungeon_group_&lt;key&gt;, "" outside the list.</summary>
    string GroupName(int type) => type > 0 && type < AchieveController.DungeonGroupKeys.Length
        ? Common("s_enum_dungeon_group_" + AchieveController.DungeonGroupKeys[type]) : "";

    /// <summary>SetSelected (@0x140c112a0): the id set is replaced.</summary>
    public void SetSelected(IEnumerable<uint> ids)
    {
        selected.Clear();
        foreach (uint id in ids) selected.Add(id);
    }

    /// <summary>
    /// OnLoadComplete (@0x140c10020): UIModalDialog's, the drag bar, BindCloseButton("m_closeButton"), then m_groupDropdown, m_customPage
    /// and m_okButton (the load ends without one) with the page's m_customList and m_listItem1..11. The dropdown gets the labels
    /// s_dungeon_search_setting_group_none and the names of the groups 1 and 2, three menu rows and row 0; m_roomOfRow is sized to the
    /// rows; the OK handler (FUN_140c113c0), the dropdown's index-change handler (OnGroupChanged), each row's data handler
    /// (FUN_140c11420) and its check box's select handler (FUN_140c11840) are set; then SelectGroup(m_nGroup) and UpdateListCount.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_closeButton");
        var dropdown = Host.Child("m_groupDropdown");
        page = Host.Child("m_customPage");
        var ok = Host.Child("m_okButton");
        if (dropdown == null || page == null || ok == null) { Host.RT.NoteMissing("dungeon search setting: dropdown, page or ok button"); return; }
        var list = Host.Child("m_customList", page);
        if (list == null) { Host.RT.NoteMissing("dungeon search setting: m_customList"); return; }

        var dp = Host.Get(dropdown, "dataProvider");
        if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");
        if (!Rt.IsNullish(dp))
        {
            Host.Invoke(dp, "push", new Dictionary<string, object> { ["label"] = Common("s_dungeon_search_setting_group_none") });
            Host.Invoke(dp, "push", new Dictionary<string, object> { ["label"] = GroupName(1) });
            Host.Invoke(dp, "push", new Dictionary<string, object> { ["label"] = GroupName(2) });
        }
        Host.Set(dropdown, "menuRowCount", (uint)GroupRows);
        Host.Set(dropdown, "selectedIndex", 0);
        Array.Clear(roomOfRow);

        Func<object[], object> onOk = _ => { Host.RT.Guard("dungeon search setting ok", OnOk); return null; };
        Host.Invoke(ok, "addEventListener", "buttonClick", onOk, false, 0, false);
        Func<object[], object> onGroup = _ =>
        {
            Host.RT.Guard("dungeon search setting group", () => OnGroupChanged(IndexOf(dropdown)));
            return null;
        };
        Host.Invoke(dropdown, "addEventListener", "listIndexChange", onGroup, false, 0, false);
        for (int i = 1; i <= ItemCount; i++)
        {
            if (Host.Child("m_listItem" + i, page) is not { } item || Host.Child("m_checkBox", item) is not { } check) continue;
            int row = i - 1;
            Func<object[], object> onCheck = _ =>
            {
                Host.RT.Guard("dungeon search setting check", () => OnCheck(row));
                return null;
            };
            Host.Invoke(check, "addEventListener", "select", onCheck, false, 0, false);
        }

        SelectGroup(group);
        UpdateListCount();
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the child's click runs the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("dungeon search setting: " + name); return; }
        Func<object[], object> f = _ => { Close(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
    }

    int IndexOf(object target) => Rt.IsNullish(Host.Get(target, "selectedIndex")) ? -1 : Rt.ToInt32(Host.Get(target, "selectedIndex"));

    /// <summary>
    /// vfunc_26 (@0x140c110b0): UIDialog's close, then m_nGroup = 0 and the rooms, the selection and the rows' indices cleared.
    /// </summary>
    public override void Close()
    {
        base.Close();
        group = 0;
        rooms.Clear();
        selected.Clear();
        Array.Clear(roomOfRow);
        lastScroll = -1;
        page = null;
    }

    /// <summary>
    /// SelectGroup (@0x140c11190): m_rooms is cleared and m_nGroup set; group 0 lists the rooms of the groups 1 then 2
    /// (GetGroupRooms), 1 and 2 their own group's. Any other value leaves the list empty.
    /// </summary>
    public void SelectGroup(int value)
    {
        rooms.Clear();
        group = value;
        var table = RoomTable();
        if (table == null) return;
        if (value == 0 || value == 1) rooms.AddRange(table.GetGroupRooms(1));
        if (value == 0 || value == 2) rooms.AddRange(table.GetGroupRooms(2));
    }

    /// <summary>OnGroupChanged (@0x140c11370): the dropdown's row is the group; the list is resized and redrawn.</summary>
    void OnGroupChanged(int index)
    {
        if (index < 0) return;
        SelectGroup(index);
        UpdateListCount();
    }

    /// <summary>
    /// UpdateListCount (@0x140c11300): ASCoreList::SetEmptyItemCount(m_rooms.size()), then the list is redrawn and the rows take their
    /// data (<see cref="ApplyItems"/>).
    /// </summary>
    public void UpdateListCount()
    {
        if (Host?.Entity == null || page == null || Host.Child("m_customList", page) is not { } list) return;
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        for (int i = 0; i < rooms.Count; i++) Host.Invoke(dp, "push", new Dictionary<string, object>());
        Host.Set(list, "dataProvider", dp);
        Host.Invoke(list, "invalidate");
        Host.Invoke(list, "validateNow");
        ApplyItems();
    }

    /// <summary>OK (FUN_140c113c0): the control center takes the ticked ids as its bookmarks, then the dialog closes (vfunc_67).</summary>
    public void OnOk()
    {
        ApplyBookmarks(selected);
        Close();
    }

    /// <summary>The row's text (FUN_140c11420): GetRoomListText(room, true) in <see cref="EnterableColor"/> or <see cref="BlockedColor"/>
    /// by CanEnterDungeonRoom.</summary>
    public string RowText(DungeonRoomData room)
    {
        bool ok = CanEnterRoom(room.Id);
        return DungeonRoomInfoPanel.Colored(DungeonRoomDialogController.RoomListText(Model.Tables, room, true), ok ? EnterableColor : BlockedColor);
    }

    /// <summary>
    /// The row's data handler (FUN_140c11420): the room at the renderer's list index; past m_rooms the row is hidden; with no local player,
    /// a row or check box missing nothing else happens. Else the row shows, m_roomOfRow[row] takes the index, the check box takes HTML
    /// text (<see cref="RowText"/>) and the state of its id in m_selected. Returns false where the row stays unchanged.
    /// </summary>
    public bool ItemData(int row, int roomIndex)
    {
        if (row < 0 || row >= ItemCount || page == null) return false;
        var item = Host.Child("m_listItem" + (row + 1), page);
        if (item == null) return false;
        if (roomIndex < 0 || roomIndex >= rooms.Count) { Host.Set(item, "visible", false); return true; }
        var check = Host.Child("m_checkBox", item);
        if (check == null || !HasLocalPlayer()) return false;
        var room = rooms[roomIndex];
        Host.Set(item, "visible", true);
        roomOfRow[row] = roomIndex;
        Host.Set(item, "mouseChildren", true);
        Host.Set(check, "isHtml", true);
        Host.Set(check, "label", RowText(room));
        Host.Set(check, "selected", selected.Contains(room.Id));
        return true;
    }

    /// <summary>
    /// The movie never calls the renderers' "fnSetData" (as3-runtime-gaps.md), so the data handler runs here: each renderer reads its list
    /// index and takes <see cref="ItemData"/>. The state it sets raises "select" on the check box, so the handler is guarded.
    /// </summary>
    void ApplyItems()
    {
        applying = true;
        try
        {
            for (int i = 1; i <= ItemCount; i++)
            {
                if (Host.Child("m_listItem" + i, page) is not { } item) continue;
                ItemData(i - 1, Rt.ToInt32(Host.Get(item, "index")));
            }
        }
        finally { applying = false; }
    }

    /// <summary>The check box's select handler (FUN_140c11840): the id of the row's room joins or leaves m_selected by the box's state.</summary>
    void OnCheck(int row)
    {
        if (applying || row < 0 || row >= ItemCount || page == null) return;
        int index = roomOfRow[row];
        if (index < 0 || index >= rooms.Count) return;
        if (Host.Child("m_listItem" + (row + 1), page) is not { } item || Host.Child("m_checkBox", item) is not { } check) return;
        uint id = rooms[index].Id;
        if (Rt.ToBoolean(Host.Get(check, "selected"))) selected.Add(id);
        else selected.Remove(id);
    }

    /// <summary>A click on the row's check box, for checks and scripted runs: the state flips and the select handler runs.</summary>
    public bool Toggle(int row)
    {
        if (page == null || Host.Child("m_listItem" + (row + 1), page) is not { } item || Host.Child("m_checkBox", item) is not { } check) return false;
        Host.Set(check, "selected", !Rt.ToBoolean(Host.Get(check, "selected")));
        return true;
    }

    /// <summary>The per-tick look at the list: a changed scroll position redraws the renderers, whose labels the redraw cleared.</summary>
    public void Tick()
    {
        if (Host?.Entity == null || IsClosed || page == null || Host.Child("m_customList", page) is not { } list) return;
        int pos = Rt.ToInt32(Host.Get(list, "scrollPosition"));
        if (pos == lastScroll) return;
        lastScroll = pos;
        Host.Invoke(list, "validateNow");
        ApplyItems();
    }
}
