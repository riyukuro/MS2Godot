using Ms2.Quest;
using System.Globalization;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIGroupMessengerBrowser (ms2/client/ms2/uigroupmessenger.cpp), the C++ side of uigroupmessengerbrowser: the bar of group
/// chat windows above the chat, and the messenger state's group chat side (the game state's m_messenger,
/// <see cref="IGroupMessengerUi"/>, which <see cref="GroupChatBook"/> calls). The movie's GroupMessengerBrowser draws the
/// window buttons and the insert button; the windows are <see cref="GroupMessengerController"/>s (at most 3, by slot), and
/// the browser keeps each chat's lines (m_logs), the items they link (m_items) and the lines that came in this frame.
/// STILL-OPEN: its settings file (vfunc_53 / vfunc_54: offsetX, offsetY, alignType) is not read or written; the place is the
/// ctor's. OnMessage's "groupList" box is not ported.
/// </summary>
public sealed class GroupMessengerBrowserController : UiController, IGroupMessengerUi
{
    /// <summary>The ctor (@0x141950770) has no SetTweenEnabled: m_c4 stays 0, no open / close tween.</summary>
    protected override bool TweenEnabled => false;

    public override string Movie => "uigroupmessengerbrowser";

    /// <summary>UIGroupMessengerBrowser's ctor sets the flag 0 (uigroupmessenger.cpp:1439): vfunc_56, an always-on
    /// one — the group messenger browser of FUN_141adcbb0's own check, which never blocks it there.</summary>
    public override bool AlwaysOn => true;
    protected override string EntityMethod => "setData";
    public override string TypeName => "GroupMessengerBrowser";      // GetTypeName (:1449)

    /// <summary>OpenChatWindow's window limit (:1975).</summary>
    public const int MaxWindows = 3;

    // the constructor (:1431): offset (0, -300), align type 7 (bottom left)
    float offsetX, offsetY = -300f;
    int alignType = 7;

    /// <summary>A line of a chat (GroupMessengerData::TalkLog, messengerdata.cpp:984): the speaker, the html, a system line
    /// (m_nA) and m_nB, set when the line came while its window was minimized (FUN_141484290 reads it, FUN_141484270 clears
    /// it).</summary>
    public sealed class TalkLog
    {
        public string Name, Text;
        public bool System, CameMinimized;
    }

    sealed class TalkLogList
    {
        public readonly List<TalkLog> Logs = new();          // m_logs
        public readonly List<string> Lines = new();          // m_lines, the report lines
    }

    readonly SortedDictionary<int, GroupMessengerController> windows = new();    // m_windows, by slot
    readonly Dictionary<int, TalkLogList> logs = new();                           // m_logs, by group id
    readonly List<(int Group, string Name, string Text)> pending = new();         // m_pending (QueueTalkLog)
    readonly Dictionary<long, Item> items = new();                                // m_items

    /// <summary>CGroupChatManager.</summary>
    public GroupChatBook Book = new();
    /// <summary>The messenger state's names (Unk_MdNames), the current window's name, shared with the chat windows.</summary>
    public MessengerBrowser Names = new();
    /// <summary>GetStringCommon.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>The local player's name (GetMessageFrameName's "me"); "" without one.</summary>
    public Func<string> LocalName = () => "";
    /// <summary>The local player's character id (OpenJoinedChatWindows, the tooltips' owner); 0 without one.</summary>
    public Func<long> LocalCharacterId = () => 0;
    /// <summary>The local player's CEquipment by item uid for a linked item's tooltip; null leaves that block out.</summary>
    public Func<long, bool> OwnerWearsUid;
    /// <summary>PlaySystemSound; null plays nothing.</summary>
    public Action<string> PlaySound;
    /// <summary>ShowSystemNotice(text, type, bLog); null shows nothing.</summary>
    public Action<string, int, bool> ShowSystemNotice;
    /// <summary>BlindManager::IsBlocked(0, name). STILL-OPEN: the blind list is not ported, so nobody is blocked.</summary>
    public Func<string, bool> IsBlinded = _ => false;
    /// <summary>The window's open (UIDialog vfunc_65: the movie made and loaded); false when it could not be opened.</summary>
    public Func<GroupMessengerController, bool> OpenWindow = _ => true;

    /// <summary>The open windows by slot.</summary>
    public IReadOnlyCollection<GroupMessengerController> Windows => windows.Values;

    /// <summary>
    /// OnLoadInit (:1504), before the load's layout (and here in place of the settings file, which is this class's own
    /// vfunc_54 :1490 and not read): the joined chats' windows (OpenJoinedChatWindows) and the window list.
    /// </summary>
    public override void LoadTransform()
    {
        OpenJoinedChatWindows();
        RefreshWindowList();
    }

    /// <summary>
    /// vfunc_43 (:1513): placed by its align type and offsets (ASDisplayObject::vfunc_2), which are then taken back from
    /// the place (UIDialog::FUN_141926620). m_60 (a saved rate above 1) does not occur without the file.
    /// </summary>
    public override void Layout(double stageW, double stageH)
    {
        Host.Align(Host.Entity, alignType, (int)offsetX, (int)offsetY);
        (alignType, offsetX, offsetY) = AlignFromPlace(Host, alignType, offsetX, offsetY);
    }

    /// <summary>UIDialog::FUN_141926620 (uidialog.cpp:1056).</summary>
    internal static (int, float, float) AlignFromPlace(UiHost host, int align, float ox, float oy)
    {
        float x = (float)Ms2.Avm2.Rt.ToNumber(host.Get(host.Entity, "x")), y = (float)Ms2.Avm2.Rt.ToNumber(host.Get(host.Entity, "y"));
        float w = (float)Math.Ceiling(Ms2.Avm2.Rt.ToNumber(host.Get(host.Entity, "width")));
        float h = (float)Math.Ceiling(Ms2.Avm2.Rt.ToNumber(host.Get(host.Entity, "height")));
        float hw = (int)host.Player.StageW * 0.5f, hh = (int)host.Player.StageH * 0.5f;
        if (x <= hw && y <= hh) return (5, x, y);
        if (x >= hw && y <= hh) return (6, x + w - hw * 2f, y);
        if (x <= hw && y >= hh) return (7, x, y + h - hh * 2f);
        if (x >= hw && y >= hh) return (8, x + w - hw * 2f, y + h - hh * 2f);
        return (align, ox, oy);
    }

    bool Loaded => Host?.Entity != null;

    /// <summary>vfunc_67 (@0x141950a10, :1456): UIDialog's close, then every window out of the list and closed.</summary>
    public override void Close()
    {
        if (Loaded) base.Close();
        var all = windows.Values.ToArray();
        windows.Clear();
        foreach (var w in all) w.Close();
    }

    /// <summary>
    /// vfunc_28 (@0x141951180, :1578), the movie's calls: OnCreateDefaultGroupMessenger makes a chat with no name
    /// (CGroupChatManager::Create); OnToggleDialog(name) shows or minimizes that window (FUN_141953240).
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (name == "OnCreateDefaultGroupMessenger") { Book.Create(""); return true; }
        if (name == "OnToggleDialog")
        {
            ToggleWindow(args is { Length: > 0 } ? Ms2.Avm2.Rt.ToString(args[0]) : "");
            return true;
        }
        return false;
    }

    /// <summary>
    /// vfunc_15 (@0x141951470, :1623), each frame while shown and loaded: each line queued since (QueueTalkLog) with its item links
    /// coloured (ColorItemLinks) goes to its chat's window (AddChatMessage), and the list is refreshed for a minimized
    /// one; then the queue is cleared. Run every frame: the queue is walked in place (no copy), as nothing in the loop queues
    /// a line (only the GroupChat packets do, through <see cref="QueueTalkLog"/>).
    /// </summary>
    public void Process()
    {
        if (!Loaded || pending.Count == 0) return;
        foreach (var (group, name, text) in pending)
        {
            string msg = ColorItemLinks(text);
            if (FindWindowByGroupId(group) is not { } w) continue;
            w.AddChatMessage(name, msg);
            if (w.Minimized) RefreshWindowList();
        }
        pending.Clear();
    }

    // ---- IGroupMessengerUi -----------------------------------------------------------------------------------------

    /// <summary>OnChatCreated (@0x1419515c0, :1651): a new log, its window opened shown and current, the button_click sound.</summary>
    public void OnChatCreated(int oid)
    {
        logs[oid] = new TalkLogList();
        OpenChatWindow(oid, true, false);
        if (FindWindowByGroupId(oid) is { } w) Names.MakeCurrent(w.Name);
        PlaySound?.Invoke("button_click");
    }

    /// <summary>
    /// FUN_1419517b0 (:1674): a new log, the window opened minimized, the system_GroupChat sound and the notice 0x80f of
    /// the inviter; the window's invite line (0x80e), and the list refreshed for a minimized window.
    /// </summary>
    public void OnInvited(int oid, string inviter, string second)
    {
        logs[oid] = new TalkLogList();
        OpenChatWindow(oid, true, true);
        PlaySound?.Invoke("system_GroupChat");
        ShowSystemNotice?.Invoke(ClientFormat.Format(Common(0x80f), inviter), 3, true);
        if (FindWindowByGroupId(oid) is not { } w) return;
        w.AddSystemMessage(inviter, ClientFormat.Format(Common(0x80e), inviter, second));
        if (w.Minimized) RefreshWindowList();
    }

    /// <summary>RemoveGroup (@0x1419519a0, :1690): the log dropped and the window closed.</summary>
    public void RemoveGroup(int oid)
    {
        logs.Remove(oid);
        CloseGroupWindow(oid);
    }

    /// <summary>OnMemberInvited (@0x1419519e0, :1698): the window's invite line (AddInvitedMessage).</summary>
    public void OnMemberInvited(int oid, string name, string member) => FindWindowByGroupId(oid)?.AddInvitedMessage(name, member);

    /// <summary>FUN_141951a70 (:1710): the window's left (0) or refused (1) line.</summary>
    public void OnMemberRemoved(int oid, string name, int reason) => FindWindowByGroupId(oid)?.AddMemberRemovedMessage(name, reason);

    /// <summary>OnGroupMemberOnline (@0x141951af0, :1722).</summary>
    public void OnGroupMemberOnline(int oid, string name) => FindWindowByGroupId(oid)?.AddMemberOnlineMessage(name);

    /// <summary>OnGroupMemberOffline (@0x141951b70, :1734).</summary>
    public void OnGroupMemberOffline(int oid, string name) => FindWindowByGroupId(oid)?.AddMemberOfflineMessage(name);

    /// <summary>QueueTalkLog (@0x141951c50, :1755): a line of a speaker not blinded is kept for the next frame (<see cref="Process"/>).</summary>
    public void QueueTalkLog(int oid, string name, string message)
    {
        if (IsBlinded(name)) return;
        pending.Add((oid, name, message));
    }

    /// <summary>ReadItemLinks (@0x141951d80, :1764): each item set in m_items by uid, as the chat's item links (a made item, slot -1).</summary>
    public void ReadItemLinks(int oid, string name, ChatItemLinks links)
    {
        if (Names.Model == null) return;
        foreach (var e in links.Items)
        {
            var item = InventoryController.ToItem(e, Names.Model.Tables.Items);
            if (item == null) continue;
            item.Slot = -1;
            items[e.Uid] = item;
        }
    }

    /// <summary>AddNoticeLine (@0x141951bf0, :1746): a system line in the chat's window.</summary>
    public void AddNoticeLine(int oid, string name, string message) => FindWindowByGroupId(oid)?.AddSystemMessage(name, message);

    // ---- the windows -----------------------------------------------------------------------------------------------

    /// <summary>
    /// RefreshWindowList (@0x141952530, :1839): with the movie loaded, setData of one {number (the chat's read line count), name,
    /// minimize} per window in slot order.
    /// </summary>
    public void RefreshWindowList()
    {
        if (!Loaded) return;
        var list = windows.Values.Select(w => (object)Host.Obj(("number", (uint)GetReadLineCount(w.GroupId)),
            ("name", w.Name), ("minimize", w.Minimized ? 1 : 0))).ToArray();
        Host.Invoke("setData", (object)list);
        ListedWindowCount = list.Length;
    }

    /// <summary>The windows in the last list sent to the movie.</summary>
    public int ListedWindowCount { get; private set; }

    /// <summary>FindFreeWindowSlot (@0x141952900, :1898): the first of slots 0 to 2 no window holds, else 3.</summary>
    public int FindFreeWindowSlot()
    {
        for (int i = 0; i < MaxWindows; i++)
            if (!windows.ContainsKey(i)) return i;
        return MaxWindows;
    }

    /// <summary>The chat's lines, oldest first (none without its log).</summary>
    public IReadOnlyList<TalkLog> LogsOf(int gid) => logs.TryGetValue(gid, out var l) ? l.Logs : Array.Empty<TalkLog>();

    /// <summary>The chat's report lines ("[h:m:s][Group][name] msg\n", the last 51).</summary>
    public IReadOnlyList<string> LinesOf(int gid) => logs.TryGetValue(gid, out var l) ? l.Lines : Array.Empty<string>();

    /// <summary>MarkChatRead (@0x141952960, :1911): m_nB cleared on each of the chat's lines (FUN_141484270).</summary>
    public void MarkChatRead(int gid)
    {
        foreach (var log in LogsOf(gid)) log.CameMinimized = false;
    }

    /// <summary>GetReadLineCount (@0x141952a30, :1926): the chat's lines with m_nB set (FUN_141484290), the ones that came
    /// while minimized since the window was last shown; the window list's number.</summary>
    public int GetReadLineCount(int gid) => LogsOf(gid).Count(l => l.CameMinimized);

    /// <summary>GetUnreadLineCount (@0x141952b30, :1944): the chat's lines less <see cref="GetReadLineCount"/>.</summary>
    public int GetUnreadLineCount(int gid) => LogsOf(gid).Count - GetReadLineCount(gid);

    /// <summary>OpenJoinedChatWindows (@0x141952be0, :1957): each chat the local character is in gets a log (when it has none) and a
    /// window as saved (no delete, not minimized); nothing without a local player.</summary>
    public void OpenJoinedChatWindows()
    {
        if (Names.Model?.HasLocalPlayer != true) return;
        foreach (int gid in Book.FindChatsWithMember(LocalCharacterId()))
        {
            if (!logs.ContainsKey(gid)) logs[gid] = new TalkLogList();
            OpenChatWindow(gid, false, false);
        }
    }

    /// <summary>
    /// OpenChatWindow (@0x141952d80, :1975): none past 3 windows or without the chat; the chat's old window closed, then a new one in the
    /// free slot (with <paramref name="deleteSave"/> its file dropped) opened, and with <paramref name="deleteSave"/>
    /// minimized or not; kept in the slot when it opened.
    /// The client's open (vfunc_65) only asks for the movie, whose load (OnLoadComplete, then RefreshWindow and the browser's
    /// <see cref="RefreshWindowList"/>, :1129) ends after the window is in its slot. Here <see cref="OpenWindow"/> loads it
    /// at once, so the window is put in the slot first and taken out again when it did not open.
    /// </summary>
    public bool OpenChatWindow(int gid, bool deleteSave, bool minimized)
    {
        if (windows.Count >= MaxWindows) return false;
        if (Book.FindChat(gid) == null) return false;
        CloseGroupWindow(gid);
        int slot = FindFreeWindowSlot();
        var w = new GroupMessengerController(this, gid, slot, deleteSave);
        windows[slot] = w;
        if (!OpenWindow(w)) { windows.Remove(slot); return false; }
        if (deleteSave) w.SetMinimized(minimized);
        return true;
    }

    /// <summary>FindWindowByGroupId (@0x141952f20, :1996).</summary>
    public GroupMessengerController FindWindowByGroupId(int gid) => windows.Values.FirstOrDefault(w => w.GroupId == gid);

    /// <summary>FindWindowByName (@0x141952fc0, :2011).</summary>
    public GroupMessengerController FindWindowByName(string name) => windows.Values.FirstOrDefault(w => w.Name == name);

    /// <summary>FUN_141953090 (:2026): the window of the name shown, then the list.</summary>
    public void ShowWindow(string name)
    {
        FindWindowByName(name)?.SetMinimized(false);
        RefreshWindowList();
    }

    /// <summary>FUN_141953160 (:2038): the window of the name minimized, then the list.</summary>
    public void MinimizeWindow(string name)
    {
        FindWindowByName(name)?.SetMinimized(true);
        RefreshWindowList();
    }

    /// <summary>FUN_141953240 (:2050): the window of the name shown when minimized, else minimized; then the list.</summary>
    public void ToggleWindow(string name)
    {
        if (FindWindowByName(name) is { } w) w.SetMinimized(!w.Minimized);
        RefreshWindowList();
    }

    /// <summary>CloseGroupWindow (@0x1419532d0, :2065): the chat's window out of the names and the list, its file dropped (vfunc_84(1)),
    /// the list refreshed, then the window closed.</summary>
    public void CloseGroupWindow(int gid) => CloseWindow(FindWindowByGroupId(gid));

    /// <summary>FUN_141953460 (:2087): as <see cref="CloseGroupWindow"/>, by the window's name.</summary>
    public void CloseWindowByName(string name) => CloseWindow(FindWindowByName(name));

    void CloseWindow(GroupMessengerController w)
    {
        if (w == null) return;
        Names.RemoveName(w.Name);
        w.DeleteSave(true);
        windows.Remove(w.Index);
        RefreshWindowList();
        w.Close();
    }

    /// <summary>FUN_141953660 (:2109), an item link put in the chat: each window of the current name
    /// (Unk_MdNames::GetCurrentName) gets it in its input (SetInputText).</summary>
    public void LinkToCurrent(string text)
    {
        string current = Names.GetCurrentName();
        foreach (var w in windows.Values.ToArray())
            if (w.Name == current) w.SetInputText(text);
    }

    /// <summary>
    /// AddTalkLog (@0x141953810, :2121): only for a chat with a log. The line kept (m_nB set when the window is minimized),
    /// and its report line "[%2d:%2d:%2d][Group][name] msg\n" of the server time (CMsTime) added, the oldest dropped
    /// first when there are more than 50.
    /// </summary>
    public void AddTalkLog(int gid, string name, string msg, bool system, bool minimized)
    {
        if (!logs.TryGetValue(gid, out var log)) return;
        log.Logs.Add(new TalkLog { Name = name, Text = msg, System = system, CameMinimized = minimized });
        if (log.Lines.Count > 50) log.Lines.RemoveAt(0);
        var now = MsTime.LocalTm(Names.ServerTime());
        log.Lines.Add(string.Format(CultureInfo.InvariantCulture, "[{0,2}:{1,2}:{2,2}][Group][{3}] {4}\n",
            now.Hour, now.Minute, now.Second, name, msg));
    }

    /// <summary>FindItemByUid (@0x141953df0, :2166): the linked item of the uid; null when there is none.</summary>
    public Item LinkedItem(long uid) => items.TryGetValue(uid, out var item) ? item : null;

    /// <summary>
    /// ColorItemLinks (@0x141953b10, :2142): every "&lt;A HREF=" .. "/A&gt;" of the html wrapped in "&lt;FONT
    /// color='#%x'&gt;" of its linked item's grade colour (CItemDataTable::GetTextColor), white (0xffffff) when the uid
    /// links none.
    /// </summary>
    public string ColorItemLinks(string html)
    {
        var tables = Names.Model?.Tables ?? GameTables.Empty;
        foreach (var link in MessengerBrowser.FindLinks(html, "<A HREF=", "/A>"))
        {
            uint color = LinkedItem(MessengerBrowser.LinkUid(link)) is { } item ? tables.Items.TextColor(item.Rarity) : 0xffffff;
            html = html.Replace(link, $"<FONT color='#{color:x}'>{link}</FONT>", StringComparison.Ordinal);
        }
        return html;
    }
}
