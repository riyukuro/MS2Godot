using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The kinds of a chat log line (MessengerData::TalkLog::m_nKind), as the panel filters read them
/// (messengerdata.cpp:286-581). Only the kinds the port writes are named.
/// </summary>
public static class TalkKind
{
    /// <summary>RequireSetting "meso" (FUN_1414818b0): the meso gain and spend lines (FUN_1413612c0).</summary>
    public const int Meso = 5;
    /// <summary>RequireSetting "item" (FUN_1414818b0): the item gain lines (FUN_141a24890).</summary>
    public const int Item = 9;
    /// <summary>RequireSetting "merat" and "token" (FUN_1414818b0): the merat lines (OnMeret, OnMeretUse) and the token gain lines (FUN_141361460).</summary>
    public const int Merat = 6, Token = 7;
    /// <summary>NoticeSetting "friend" (FUN_141481040, messengerdata.cpp:299): the buddy lines (CBuddyManager::OnBuddy's
    /// FUN_1412751b0(.., 12)).</summary>
    public const int Friend = 0xc;
    /// <summary>SystemSetting "event" and "notice" (FUN_1414813b0): the welcome line and the chat copies of the notices.</summary>
    public const int SystemEvent = 0x13, SystemNotice = 0x14;
    /// <summary>SubjectSetting "party" (messengerdata.cpp:561): the party chat lines; NoticeSetting "party" (:299): the party
    /// notice lines (FUN_1412751b0(.., 0xe)); SubjectSetting "partyInvite": the invite line (FUN_141a239e0).</summary>
    public const int PartyChat = 2, PartyNotice = 0xe, PartyInvite = 0x19;
    /// <summary>SubjectSetting::FUN_141481e90 never hides it: the top layer notices (ShowTopLayerNotify @0x141275400).</summary>
    public const int TopLayer = 0x1d;
}

/// <summary>MessengerData::ScreenSetting (messengerdata.cpp:374): fontSize 17, backgroundAlpha 50, showDamage 1.</summary>
public sealed class ScreenSetting { public int FontSize = 0x11, BackgroundAlpha = 0x32, ShowDamage = 1; }

/// <summary>
/// MessengerData::PanelSetting (messengerdata.cpp:234-683): the switches of a tab, which choose the log lines it shows.
/// Each setting reads its element (XmlChild(.., true) adds a missing one, so the read always succeeds), and its "all" is
/// then recomputed: 0 when every other switch is 0, else 1. A kind is hidden when its setting's "all" is 0 or its own
/// switch is 0.
/// </summary>
public sealed class PanelSetting
{
    /// <summary>The settings' elements and switches, in read order (each default 1: the setting ctors).</summary>
    static readonly (string Element, string[] Keys)[] Settings =
    {
        ("Notice", new[] { "all", "user", "trade", "community", "friend", "guild", "club", "party", "mentoring", "etc" }),   // FUN_141480d00
        ("System", new[] { "all", "event", "notice" }),                                                                       // FUN_141481250
        ("Require", new[] { "all", "meso", "merat", "token", "item", "experience", "auth" }),                                // FUN_141481640
        ("Subject", new[] { "all", "super", "world", "channel", "social", "party", "guild", "whisper", "club", "partyInvite", "dungeonHelp" }),   // FUN_141481ab0
    };

    /// <summary>The kinds each setting decides (FUN_141481040, FUN_1414813b0, FUN_1414818b0, FUN_141481e90): kind, element, switch.</summary>
    static readonly Dictionary<int, (string Element, string Key)> Kinds = new()
    {
        [10] = ("Notice", "user"), [0xb] = ("Notice", "community"), [0xc] = ("Notice", "friend"), [0xd] = ("Notice", "guild"),
        [0xe] = ("Notice", "party"), [0xf] = ("Notice", "club"), [0x10] = ("Notice", "mentoring"), [0x11] = ("Notice", "etc"),
        [0x12] = ("Notice", "trade"),
        [0x13] = ("System", "event"), [0x14] = ("System", "notice"),
        [5] = ("Require", "meso"), [6] = ("Require", "merat"), [7] = ("Require", "token"), [8] = ("Require", "experience"),
        [9] = ("Require", "item"), [0x1c] = ("Require", "auth"),
        [0x17] = ("Subject", "super"), [0x15] = ("Subject", "world"), [0x16] = ("Subject", "channel"), [1] = ("Subject", "social"),
        [2] = ("Subject", "party"), [3] = ("Subject", "guild"), [0x18] = ("Subject", "guild"), [4] = ("Subject", "whisper"),
        [0x1a] = ("Subject", "club"), [0x19] = ("Subject", "partyInvite"), [0x1b] = ("Subject", "dungeonHelp"),
    };

    /// <summary>The club and dungeon help switches are read only with features 0x56 (== 1) and 0x93 (FUN_141481ab0).</summary>
    public const int ClubFeature = 0x56, DungeonHelpFeature = 0x93;

    readonly Dictionary<string, Dictionary<string, int>> switches = Settings.ToDictionary(s => s.Element, s => s.Keys.ToDictionary(k => k, _ => 1));
    /// <summary>m_screen, the tab's own screen setting.</summary>
    public readonly ScreenSetting Screen = new();

    /// <summary>A switch's value (1 = shown).</summary>
    public int this[string element, string key] => switches[element][key];

    /// <summary>
    /// FUN_141482220 (:620), the &lt;PanelSetting&gt; of a tab: "global" into the browser's s_nGlobal (kept when missing), the
    /// notice, system and screen settings, require, subject, and while s_nGlobal is set the screen setting again into the
    /// shared one (DAT_143adda48).
    /// </summary>
    public void Read(XElement panel, MessengerBrowser shared, Func<int, bool> featureEnabled)
    {
        int g = shared.Global ? 1 : 0;
        Attr(panel, "global", ref g);
        shared.Global = g != 0;
        foreach (var (element, keys) in Settings)
        {
            if (element == "Require") ReadScreen(Screen, panel);
            var e = panel?.Element(element);
            var s = switches[element];
            foreach (var key in keys)
            {
                if (key == "club" && element == "Subject" && !featureEnabled(ClubFeature)) continue;
                if (key == "dungeonHelp" && !featureEnabled(DungeonHelpFeature)) continue;
                int v = s[key];
                Attr(e, key, ref v);
                s[key] = v;
            }
            s["all"] = keys.Skip(1).All(k => s[k] == 0) ? 0 : 1;
        }
        if (shared.Global) ReadScreen(shared.GlobalScreen, panel);
    }

    /// <summary>ScreenSetting::FUN_141481480 (:380): fontSize and backgroundAlpha are 0 when missing, showDamage is kept.</summary>
    static void ReadScreen(ScreenSetting s, XElement panel)
    {
        int fontSize = 0, alpha = 0;
        var e = panel?.Element("Screen");
        Attr(e, "fontSize", ref fontSize);
        Attr(e, "backgroundAlpha", ref alpha);
        Attr(e, "showDamage", ref s.ShowDamage);
        s.FontSize = fontSize;
        s.BackgroundAlpha = alpha;
    }

    internal static void Attr(XElement e, string name, ref int v)
    {
        if ((string)e?.Attribute(name) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) v = i;
    }

    /// <summary>
    /// FUN_141482340 (:647), the tab's &lt;PanelSetting&gt; written under <paramref name="tab"/> (XmlChild(.., true): found or
    /// added): "global" from s_nGlobal, then Notice, System, Screen, Require and Subject with every switch ("club" only with
    /// feature 0x56, "dungeonHelp" only with 0x93, FUN_141481cf0), and while s_nGlobal is set the shared screen setting over
    /// the Screen element (DAT_143adda48.FUN_141481540).
    /// </summary>
    public void Write(XElement tab, MessengerBrowser shared, Func<int, bool> featureEnabled)
    {
        var panel = Child(tab, "PanelSetting");
        panel.SetAttributeValue("global", shared.Global ? 1 : 0);
        foreach (var (element, keys) in Settings)
        {
            if (element == "Require") WriteScreen(Screen, panel);
            var e = Child(panel, element);
            foreach (var key in keys)
            {
                if (key == "club" && element == "Subject" && !featureEnabled(ClubFeature)) continue;
                if (key == "dungeonHelp" && !featureEnabled(DungeonHelpFeature)) continue;
                e.SetAttributeValue(key, switches[element][key]);
            }
        }
        if (shared.Global) WriteScreen(shared.GlobalScreen, panel);
    }

    /// <summary>ScreenSetting::FUN_141481540 (:398).</summary>
    static void WriteScreen(ScreenSetting s, XElement panel)
    {
        var e = Child(panel, "Screen");
        e.SetAttributeValue("fontSize", s.FontSize);
        e.SetAttributeValue("backgroundAlpha", s.BackgroundAlpha);
        e.SetAttributeValue("showDamage", s.ShowDamage);
    }

    /// <summary>XmlChild(e, name, true) (tinyxmlhelper.cpp:101): the first child of that name, added when missing.</summary>
    internal static XElement Child(XElement e, string name)
    {
        var c = e.Element(name);
        if (c == null) e.Add(c = new XElement(name));
        return c;
    }

    /// <summary>FUN_141482460 (:665): a kind one of the settings hides (kind 0x1d never is).</summary>
    public bool Hidden(int kind)
    {
        if (!Kinds.TryGetValue(kind, out var k)) return false;
        var s = switches[k.Element];
        return s["all"] == 0 || s[k.Key] == 0;
    }
}

/// <summary>
/// UIMessengerBrowser (uimessenger.cpp), the side all chat windows share: the log of chat lines (m_logs, at most 500), the
/// lines not yet shown (m_pendingLogs), the windows, and the panel statics (s_nGlobal and the shared screen setting
/// DAT_143adda48, messengerdata.cpp:600). The browser's own movie and its listener slots other than the item gain are not
/// ported; nor is the font size rewrite of the log (FUN_141a27ad0 :3873). A user's chat line (RecvOp 0x1d) goes in
/// through <see cref="OnUserChat"/>, the items lines link (RecvOp 0x1e) through <see cref="OnItemLinks"/>.
/// </summary>
public sealed class MessengerBrowser
{
    /// <summary>The ctor's (@0x141a20f90, :2690) m_18c, m_190 and m_198: the log's size, and the lines past which (shown
    /// plus pending) or at which (pending) the windows redraw their tab instead of appending.</summary>
    public const int MaxLogs = 500, RedrawTotal = 600, RedrawPending = 10;

    /// <summary>MessengerData::TalkLog: the line's html and its kind.</summary>
    public readonly record struct TalkLog(string Msg, int Kind);

    readonly LinkedList<TalkLog> logs = new();                // m_logs
    readonly List<TalkLog> pending = new();                   // m_pendingLogs
    int shown;                                                // m_194, lines the windows show
    /// <summary>m_windows.</summary>
    public readonly List<MessengerController> Windows = new();
    /// <summary>PanelSetting::s_nGlobal: the tabs use <see cref="GlobalScreen"/> (FUN_1414821b0).</summary>
    public bool Global;
    /// <summary>DAT_143adda48, the shared screen setting: its font size is the one the lines are made with.</summary>
    public readonly ScreenSetting GlobalScreen = new();
    /// <summary>The tables and the local player the lines are made with.</summary>
    public UiModel Model;

    public MessengerBrowser(UiModel model = null) { Model = model; }

    public IReadOnlyCollection<TalkLog> Logs => logs;
    public IReadOnlyList<TalkLog> Pending => pending;

    /// <summary>FUN_1414821b0 (:600): the shared screen setting while s_nGlobal is set, else the tab's.</summary>
    public ScreenSetting ScreenOf(PanelSetting panel) => Global ? GlobalScreen : panel.Screen;

    /// <summary>FUN_141a26e00 (:3732): the line joins the log and the pending lines; the oldest goes past 500.</summary>
    public void AddLog(string html, int kind)
    {
        var log = new TalkLog(html, kind);
        logs.AddLast(log);
        pending.Add(log);
        if (logs.Count > MaxLogs) logs.RemoveFirst();
    }

    GameTables Tables => Model?.Tables ?? GameTables.Empty;

    /// <summary>Unk_MsgHistory (uimessenger.cpp): a sent line and the links the input held (m_keys).</summary>
    public readonly record struct HistoryEntry(string Text, IReadOnlyList<string> Links);

    /// <summary>The history FUN_141a25f70 (@0x141a25f70, :3555) keeps: at most this many lines (nMax).</summary>
    public const int MaxHistory = 10;
    readonly List<HistoryEntry> history = new();              // m_history
    int historyAt;                                            // m_itHistory as an index, history.Count = end()
    public IReadOnlyList<HistoryEntry> History => history;
    public int HistoryPosition => historyAt;

    /// <summary>
    /// FUN_141a25f70 (:3555), a sent line joins the history: none for an empty text; the same text as the last entry only
    /// takes the position back to the end; else the entry (text and a copy of the links) is added, the oldest goes past
    /// ten, and the position is the end.
    /// </summary>
    public void RecordHistory(string text, IReadOnlyList<string> links)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (history.Count > 0 && history[^1].Text == text) { historyAt = history.Count; return; }
        history.Add(new HistoryEntry(text, links.ToArray()));
        if (history.Count > MaxHistory) history.RemoveAt(0);
        historyAt = history.Count;
    }

    /// <summary>
    /// GetHistoryEntry (@0x141a261b0, :3575): none for an empty history. Up (bUp) moves the position back, none at the
    /// first entry; down moves it on, none at the end. Moving onto the end gives no entry but still counts as a move (the
    /// client leaves its out entry empty then, so the input is emptied).
    /// </summary>
    public bool GetHistoryEntry(bool up, out HistoryEntry entry)
    {
        entry = new HistoryEntry("", System.Array.Empty<string>());
        if (history.Count == 0) return false;
        if (up) { if (historyAt == 0) return false; historyAt--; }
        else { if (historyAt == history.Count) return false; historyAt++; }
        if (historyAt != history.Count) entry = history[historyAt];
        return true;
    }

    /// <summary>vfunc_91 (:2150), the window's focus out: the position goes back to the end.</summary>
    public void ResetHistoryPosition() => historyAt = history.Count;

    /// <summary>
    /// FUN_141a247c0 (:3247), the system lines (FUN_1412751b0, unit_141274d10_uihelpers.cpp:247): none for an empty text;
    /// the value words replaced (ReplaceValueWords: the local player's name; the gender words read no gender here), then
    /// FUN_14183f080(line, 0, font size) (texttohtml.cpp:401): s_html_chat_notice (0x676) with s_color21
    /// (chatTextSystemMessage) and the shared font size.
    /// </summary>
    public void AddSystemLine(string text, int kind)
    {
        if (string.IsNullOrEmpty(text)) return;
        string line = Tables.ReservedWords.ReplaceValueWords(text, Model?.HasLocalPlayer == true ? Model.Player.Name : null);
        AddLog(ClientFormat.Format(Tables.Common("s_html_chat_notice"), Tables.ChatTextColors["chatTextSystemMessage"],
            (uint)GlobalScreen.FontSize, line), kind);
    }

    /// <summary>
    /// UIMessengerBrowser::FUN_141a239e0 (uimessenger.cpp:3069) with FUN_141841fd0 (texttohtml.cpp:667): the line that offers
    /// to invite <paramref name="name"/> to the party. s_html_chat_invite_party carries the link 'event:inviteParty,name',
    /// s_html_chat_invite_party_message the name and that link, s_html_chat_system_notice the colour s_color16 and the font size.
    /// </summary>
    public void AddInvitePartyLine(string name)
    {
        string link = ClientFormat.Format(Tables.Common("s_html_chat_invite_party"), $"'event:inviteParty,{name}'");
        string message = ClientFormat.Format(Tables.Common("s_html_chat_invite_party_message"), name, link);
        AddLog(ClientFormat.Format(Tables.Common("s_html_chat_system_notice"), Tables.ChatColor(16), (uint)GlobalScreen.FontSize, message), TalkKind.PartyInvite);
    }

    /// <summary>
    /// OnItemDropNotice (scenestategame.cpp:8023): common string 0x342 of the dropper's name and the item's link
    /// (FUN_140d2e2b0, <see cref="ItemLink"/>), as a system line of kind 0x14 (FUN_1412751b0).
    /// </summary>
    public void AddItemDropNotice(string name, int itemId, int grade, long uid) =>
        AddSystemLine(ClientFormat.Format(Tables.Common(0x342), name, ItemLink(itemId, grade, uid)), TalkKind.SystemNotice);

    /// <summary>
    /// UIMessengerBrowser::vfunc_5 (@0x141a25e10, :3526), the item event dispatcher's new item (ItemInventory mode 7,
    /// CSceneStateGame::OnItemInventory, scenestategame.cpp:2836): nothing for a UGC item (type 0x16), else
    /// FUN_1412750f0(item, count, 0x2ba, 0x2b9) -> FUN_141a24890 (:3258): the item's html (FUN_141841b40,
    /// texttohtml.cpp:645: s_html_chat_linkable_title with '' as the link, the grade's colour, the shared font size and
    /// s_format_item of the name), in s_msg_take_item_count(_ugc_cube) with the count when it is over 1, else in
    /// s_msg_take_item(_ugc_cube) (the _ugc_cube strings for an install item), as a system line of kind 9. The debug
    /// mode's count suffix is left out.
    /// </summary>
    public void AddItemLine(Item item, uint count)
    {
        if (item?.Data == null || item.Data.IsUgcType) return;
        var t = Tables;
        string name = Model != null ? new ItemTooltip(Model, 0).Name(item) : t.ItemName(item.ItemId);
        string html = ClientFormat.Format(t.Common("s_html_chat_linkable_title"), "''", t.Items.TextColor(item.Rarity),
            (uint)GlobalScreen.FontSize, ClientFormat.Format(t.Common("s_format_item"), name));
        bool install = item.Data.IsInstallItem;
        string text = count > 1
            ? ClientFormat.Format(t.Common(install ? "s_msg_take_item_count_ugc_cube" : "s_msg_take_item_count"), html, count)
            : ClientFormat.Format(t.Common(install ? "s_msg_take_item_ugc_cube" : "s_msg_take_item"), html);
        AddSystemLine(text, TalkKind.Item);
    }

    /// <summary>
    /// vfunc_15 (:3095), each frame: with pending lines, the windows redraw their tab when there are 10 or more or the
    /// shown and pending lines pass 600 (FUN_141a27bf0 :3891), else they append them (FlushPendingLogs :3904). The
    /// browser entity's own m_68 / m_4c gates (UISwfEntity) are not modelled: it runs while the HUD does.
    /// </summary>
    public void Process()
    {
        if (pending.Count == 0) return;
        if (pending.Count >= RedrawPending || shown + pending.Count > RedrawTotal) RedrawAll();
        else FlushPending();
    }

    /// <summary>FUN_141a27bf0 (:3891): every window redraws its tab; all lines are shown.</summary>
    void RedrawAll()
    {
        foreach (var w in Windows) w.ReselectCurrentTab();
        shown = logs.Count;
        pending.Clear();
    }

    /// <summary>FlushPendingLogs (:3904): every window appends each pending line its tab shows.</summary>
    void FlushPending()
    {
        foreach (var w in Windows)
            foreach (var log in pending) w.AppendLine(log.Msg, log.Kind);
        shown += pending.Count;
        pending.Clear();
    }

    /// <summary>The end of UIMessenger::vfunc_97 (:2237): a window drew the whole log.</summary>
    internal void Drawn()
    {
        shown = logs.Count;
        pending.Clear();
    }

    /// <summary>FUN_141a23c20 (:3114): the whisper and talk targets and the last channel, the shown count, the log, the
    /// linked items, the pending lines, the reports and the input history (with its position) cleared (of what it
    /// clears, the rest is not ported).</summary>
    public void Clear()
    {
        WhisperTarget = "";
        TalkTarget = "";
        LastChannel = 0;
        shown = 0;
        logs.Clear();
        linkedItems.Clear();
        pending.Clear();
        reports.Clear();
        history.Clear();
        historyAt = 0;
    }

    // ---- RecvOp 0x1e: the linked items ------------------------------------------------------------------------------

    /// <summary>m_linkedItems (+0x268, a CAtlMap of item uid to CItem): the items chat lines link.</summary>
    readonly Dictionary<long, Item> linkedItems = new();

    /// <summary>
    /// vfunc_85 (@0x141a22cd0, :2963): each item, made from its id and rarity with its uid and read body (see
    /// <see cref="Ms2.Net.Packets.ChatItemLinks"/>), set in the linked items by uid. STILL-OPEN: SetObjectId(g_upperTargetId)
    /// is not modelled.
    /// </summary>
    public void OnItemLinks(Ms2.Net.Packets.ChatItemLinks links)
    {
        if (Model == null) return;
        foreach (var e in links.Items)
        {
            var item = InventoryController.ToItem(e, Model.Tables.Items);
            if (item == null) continue;
            item.Slot = -1;                                  // a made item: the ctor's slot; the body has none
            linkedItems[e.Uid] = item;
        }
    }

    /// <summary>FUN_141a263d0 (:3596): the linked item of the uid; null when there is none.</summary>
    public Item LinkedItem(long uid) => linkedItems.TryGetValue(uid, out var item) ? item : null;

    // ---- item links into the input (unit_140d2a7e0_chatlinkhelpers.cpp) ----------------------------------------------

    /// <summary>
    /// The held containers FUN_140d2e2b0 searches by uid: g_143ab2a90.FindItemByUid (the bag), then the local PC's
    /// FUN_141655b80, the pet inventory and MS2_STORE_TABLE; null when none holds it. Only the bag is wired here.
    /// </summary>
    public Func<long, Item> FindHeldItem = _ => null;

    /// <summary>FUN_140d2cfc0 (:578): an item's link (its id, rarity and uid) put in the input; nothing for null.</summary>
    public void LinkItem(Item item)
    {
        if (item == null) return;
        LinkItem(item.Data.Id, item.Rarity, item.Uid);
    }

    /// <summary>
    /// FUN_140d2d060 (:586): <see cref="ItemLink"/> put in the chat log's input (m_pLog, FUN_141a24fb0), then in the group
    /// messenger's (m_pMessenger, UIGroupMessengerBrowser::FUN_141953660, <see cref="GroupLink"/>).
    /// </summary>
    public void LinkItem(int id, int grade, long uid)
    {
        string link = ItemLink(id, grade, uid);
        AppendToInput(link);
        GroupLink?.Invoke(link);
    }

    /// <summary>The game state's m_pMessenger, UIGroupMessengerBrowser::FUN_141953660: the link in the current group
    /// window's input; null without the group messenger.</summary>
    public Action<string> GroupLink;

    /// <summary>
    /// FUN_140d2e2b0 (:716): the href 'event:itemTooltip,{uid},3,{uid},{id},{grade}' (CHATLINK_ITEM_UID; the uid and
    /// the id formatted unsigned), the grade's colour (CItemDataTable::GetTextColorString, itemdatatable.cpp:408:
    /// "#{0:x}" of GetTextColor) and the held item's name (CItem::FUN_140527b10, <see cref="ItemTooltip.Name"/>), else
    /// GetItemDisplayName(id). STILL-OPEN: GetItemDisplayName's pet-item form (itemugcmesh.cpp:1014, type 0xb: common
    /// 0xcd5 / 0xcd6 of the pet's name) is not ported, so an item not held gives its string table name.
    /// </summary>
    public string ItemLink(int id, int grade, long uid)
    {
        string href = "'event:itemTooltip," + ((ulong)uid).ToString(CultureInfo.InvariantCulture) + ",3,"
            + ((ulong)uid).ToString(CultureInfo.InvariantCulture) + "," + ((uint)id).ToString(CultureInfo.InvariantCulture) + ","
            + grade.ToString(CultureInfo.InvariantCulture) + "'";
        string color = "#" + Tables.Items.TextColor(grade).ToString("x", CultureInfo.InvariantCulture);
        var held = FindHeldItem?.Invoke(uid);
        string name = held != null && Model != null ? new ItemTooltip(Model, 0).Name(held) : Tables.ItemName(id);
        return LinkMarkup(href, color, name);
    }

    /// <summary>FUN_140d2ef00 (:802): "&amp;" in the text escaped (EscapeAmpersand, :460), then
    /// &lt;a href={href}&gt;&lt;FONT color='{color}'&gt;[{text}]&lt;/FONT&gt;&lt;/a&gt;.</summary>
    public static string LinkMarkup(string href, string color, string text) =>
        "<a href=" + href + "><FONT color='" + color + "'>[" + (text ?? "").Replace("&", "&amp;") + "]</FONT></a>";

    /// <summary>
    /// FUN_141a24fb0 (uimessenger.cpp:3332): AppendToInput on each window whose name is GetState()->GetCurrentName()
    /// (<see cref="GetCurrentName"/>; a group window's focus makes another name the current one).
    /// </summary>
    public void AppendToInput(string link)
    {
        string current = GetCurrentName();
        foreach (var w in Windows.ToArray())
            if (w != null && w.Name == current) w.AppendToInput(link);
    }

    /// <summary>
    /// FUN_140d2a7e0 (chatlinkhelpers.cpp:180): the link is one of the list's: both without their A and FONT tags
    /// (RemoveHtmlTag; the link's &amp;apos;, &amp;lt;, &amp;gt; and &amp;nbsp; then read back) are the same text, and
    /// both link the same id (<see cref="LinkUid"/>), not 0.
    /// </summary>
    public static bool IsListedLink(string link, IReadOnlyList<string> links)
    {
        string plain = RemoveHtmlTag(RemoveHtmlTag(link, "A"), "FONT")
            .Replace("&apos;", "'").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&nbsp;", " ");
        foreach (var other in links)
        {
            if (plain != RemoveHtmlTag(RemoveHtmlTag(other, "a"), "FONT")) continue;
            long id = LinkUid(link), otherId = LinkUid(other);
            if (id != 0 && id == otherId) return true;
        }
        return false;
    }

    /// <summary>
    /// ColorItemLinks (@0x141a25ad0, :3486): every "&lt;A HREF=" .. "/A&gt;" of the html (FindLinks) wrapped in
    /// FUN_14183e930's "&lt;FONT color='#%x'&gt;" of its linked item's grade colour (CItemDataTable::GetTextColor), white
    /// (0xffffff) when the uid links none; CStringW::Replace replaces every copy of the link, each time it is found.
    /// </summary>
    public string ColorItemLinks(string html)
    {
        foreach (var link in FindLinks(html, "<A HREF=", "/A>"))
        {
            uint color = LinkedItem(LinkUid(link)) is { } item ? Tables.Items.TextColor(item.Rarity) : 0xffffff;
            html = html.Replace(link, $"<FONT color='#{color:x}'>{link}</FONT>", StringComparison.Ordinal);
        }
        return html;
    }

    /// <summary>
    /// FindLinks (@0x140d2c080, unit_140d2a7e0_chatlinkhelpers.cpp:396): every text from a <paramref name="begin"/> through
    /// the next <paramref name="end"/>, in order; none for an empty text, begin or end.
    /// </summary>
    public static List<string> FindLinks(string text, string begin, string end)
    {
        var links = new List<string>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(begin) || string.IsNullOrEmpty(end)) return links;
        int pos = 0;
        while (true)
        {
            int first = text.IndexOf(begin, pos, StringComparison.Ordinal);
            if (first < 0) break;
            int last = text.IndexOf(end, first, StringComparison.Ordinal);
            if (last < 0) break;
            last += end.Length - 1;
            pos = last + 1;
            links.Add(text.Substring(first, last - first + 1));
        }
        return links;
    }

    /// <summary>
    /// FUN_140d2caf0 (chatlinkhelpers.cpp:521), a link's item uid: of each "&lt;" .. "&gt;" group (FUN_14028cf00,
    /// commonutil.cpp:544, which looks for the "&gt;" from the end of the last group, not from the "&lt;"), the second of its
    /// ","-separated numbers (FUN_140d2f020: CStringW::Tokenize, so empty parts are skipped; each _wtoi64) when it has more
    /// than two; else 0. For 'event:itemTooltip,{uid},..' that is the uid.
    /// </summary>
    public static long LinkUid(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int pos = 0;
        while (true)
        {
            int open = text.IndexOf('<', pos);
            if (open == -1) break;
            int close = text.IndexOf('>', pos);
            if (close == -1) break;
            if (close >= open)
            {
                var ids = text.Substring(open, close - open + 1).Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (ids.Length > 2) return NumberText.ParseInt64W(ids[1]);
            }
            pos = close + 1;
        }
        return 0;
    }

    // ---- sending: UIMessenger::SendChat (@0x141a10660, :4208) and FUN_141276f80 (unit_141274d10_uihelpers.cpp:913) ----

    /// <summary>m_1b0: the kind (FUN_141482f00) of the last line sent; with 4 or 0x1a, Return on an unfocused input starts
    /// a whisper to <see cref="WhisperTarget"/> or a talk to <see cref="TalkTarget"/> (FUN_141a11e30 :1532).</summary>
    public int LastChannel;
    /// <summary>m_strWhisperTarget, the last whisper's recipient.</summary>
    public string WhisperTarget = "";
    /// <summary>m_1a8, the last club line's club name.</summary>
    public string TalkTarget = "";

    /// <summary>FUN_140710590 of packet 0x11 (the game session's send); null = no session, nothing goes out.</summary>
    public Action<UserChatRequest> SendPacket;
    /// <summary>MyPC::IsInParty.</summary>
    public Func<bool> IsInParty = () => false;
    /// <summary>HandlePartyLink's world (the party manager, the dungeon table and the sends); null = the links do nothing.</summary>
    public PartyLinks PartyLinks;
    /// <summary>CGuildManager::IsInGuild. STILL-OPEN: the guild manager is not ported, so not in a guild.</summary>
    public Func<bool> IsInGuild = () => false;
    /// <summary>CGuildManager::HasMemberPermission(name, permission). STILL-OPEN: not ported, so none.</summary>
    public Func<string, int, bool> HasGuildPermission = (_, _) => false;
    /// <summary>CClubClientManager::GetClubIdByName; 0 = no such club. STILL-OPEN: the club manager is not ported.</summary>
    public Func<string, long> ClubIdByName = _ => 0;
    /// <summary>CClubClientManager::GetClub(id) is not null. STILL-OPEN: not ported, so no club.</summary>
    public Func<long, bool> HasClub = _ => false;
    /// <summary>The function name of the local player's selected super chat item (MyPC FUN_1416639e0, then the item
    /// data's function.name); "" without one. STILL-OPEN: the selection is not modelled.</summary>
    public Func<string> SuperChatItemFunction = () => "";
    /// <summary>Whether the local player has a super chat item (MyPC FUN_1416639e0 -> Unk_Inventory::FUN_140a5e2e0 finds it
    /// in the bag); Tab skips the super row without one. STILL-OPEN: the selection is not modelled, so none.</summary>
    public Func<bool> HasSuperChatItem = () => false;
    /// <summary>DAT_143ab2914, the local player's GM level (FUN_140930150, scenestategame.cpp:5854); 0 by default.</summary>
    public Func<int> GmLevel = () => 0;
    /// <summary>ExecuteEmotionCommand(text): an emotion's slash command ran. STILL-OPEN: not ported, so none.</summary>
    public Func<string, bool> ExecuteEmotion = _ => false;
    /// <summary>
    /// FUN_141a12380 (:4450): with the community options m_b0 / m_b1 / m_b2 set, a super / world / channel line asks in a
    /// message box first (sent from its OnSuperChatConfirm / OnWorldChatConfirm / OnChannelChatConfirm); true = asked.
    /// STILL-OPEN: the game options and those boxes are not ported, so it never asks.
    /// </summary>
    public Func<int, string, bool> ConfirmSend = (_, _) => false;
    /// <summary>CUIService::OpenDialog(0x4f58), the super chat dialog. STILL-OPEN: not ported; null does nothing.</summary>
    public Action OpenSuperChatDialog;
    /// <summary>CUIService::ToggleDialog(0x4e7a), the dynamic action dialog (uimessenger.cpp:4052-4055); null does nothing.</summary>
    public Action ToggleDynamicAction;
    /// <summary>The local player's CChatEmoticonCli (GetMyCharacter()->GetChatEmoticon()); null = none (the client's asserts return).</summary>
    public ChatEmoticons.ChatEmoticonState ChatEmoticons;
    /// <summary>WorldService::ResetIdleTimer.</summary>
    public Action ResetIdleTimer;
    /// <summary>FUN_141276e00 (:882)'s CBanTextChecker FUN_140225210 of a whisper target; nonzero = refused. STILL-OPEN:
    /// the checker is not ported, so 0.</summary>
    public Func<string, int> TargetCheck = _ => 0;
    /// <summary>CBlockManager::FindBlockedAction(2), a chat penalty: its kind and, for other kinds than 2, the
    /// FUN_140a2fcd0 remaining time line; null = none. STILL-OPEN: the block manager is not ported.</summary>
    public Func<(int Kind, string Remaining)?> ChatPenalty = () => null;
    /// <summary>The field data's HasLimitContent(0x100). STILL-OPEN: the field's limits are not loaded, so false.</summary>
    public Func<bool> FieldChatLimited = () => false;
    /// <summary>The hooks of the slash command functions (<see cref="UserCommands.Execute"/>).</summary>
    public readonly UserCommands.Hooks CommandHooks = new();

    /// <summary>
    /// FUN_141276f80 (unit_141274d10_uihelpers.cpp:913), a CommandChat send. A whisper (kind 4) to the local player's own
    /// name (CompareNoCase, FUN_141276e00 result 10) gets the system line 0x430, a target the checker refuses 0x431; a chat
    /// penalty gets 0x433 (penalty kind 2) or 0x442 and the remaining time; under GM level 0x32 a field that limits chat
    /// gets s_field_limit_content_chat (all kind 0x14 lines, FUN_1412751b0); else packet 0x11 (FUN_1401fa700 writes the
    /// record) goes out. 1 = sent.
    /// </summary>
    public int SendUserChat(UserChatRequest chat)
    {
        var t = Tables;
        if (chat.Kind == 4)
        {
            int result = 0;
            if (Model?.HasLocalPlayer == true && string.Equals(chat.Target ?? "", Model.Player.Name ?? "", StringComparison.OrdinalIgnoreCase)) result = 10;
            else result = TargetCheck(chat.Target ?? "");
            if (result != 0)
            {
                AddSystemLine(t.Common(result == 10 ? 0x430 : 0x431), TalkKind.SystemNotice);
                return 0;
            }
        }
        if (ChatPenalty() is { } penalty)
        {
            if (penalty.Kind == 2) AddSystemLine(t.Common(0x433), TalkKind.SystemNotice);
            else
            {
                AddSystemLine(t.Common(0x442), TalkKind.SystemNotice);
                AddSystemLine(penalty.Remaining, TalkKind.SystemNotice);
            }
            return 0;
        }
        if (GmLevel() < UserCommands.GmCommandLevel && FieldChatLimited())
        {
            AddSystemLine(t.Common("s_field_limit_content_chat"), TalkKind.SystemNotice);
            return 0;
        }
        SendPacket?.Invoke(chat);
        return 1;
    }

    /// <summary>FUN_141275260 (unit_141274d10_uihelpers.cpp:267): the common string of the id, unless empty, through
    /// ShowSystemNotice(text, 1, 1).</summary>
    public void Notice(int id)
    {
        string text = Tables.Common(id);
        if (!string.IsNullOrEmpty(text)) ShowSystemNotice?.Invoke(text, 1, true);
    }

    internal bool IsFeatureEnabled(int id) => Feature(id);

    // ---- RecvOp 0x1d: UIMessengerBrowser::vfunc_84 (@0x141a22140, uimessenger.cpp:2829) -------------------------------

    /// <summary>ShowSystemNotice(text, type, bLog) (@0x1412751f0, unit_141274d10_uihelpers.cpp:255): UISystemText 0x4e41's
    /// ShowMessage(text, type, 0), and with bLog a system line of kind 0x14; nothing without the system text.</summary>
    public Action<string, int, bool> ShowSystemNotice;
    /// <summary>PlaySystemSound(name, "") (@0x141276180). STILL-OPEN: no system sound player; null plays nothing.</summary>
    public Action<string> SystemSound;
    /// <summary>The local player's character id (MyPC FUN_141656a80); 0 without one.</summary>
    public Func<long> LocalCharacterId = () => 0;
    /// <summary>BlindManager::IsBlocked(0, name). STILL-OPEN: the blind list is not ported, so nobody is blocked.</summary>
    public Func<string, bool> IsBlinded = _ => false;
    /// <summary>CBuddyManager FUN_140ae5240(name) / FUN_140ae51b0(character id), the block list. STILL-OPEN: the buddy
    /// manager is not ported, so nobody is blocked.</summary>
    public Func<string, long, bool> IsBlockedBuddy = (_, _) => false;
    /// <summary>
    /// The whisper block option (CGameOption community settings m_b7): instead of showing a whisper the client sends
    /// CommandChat kind 6 to its sender (FUN_141276f80). STILL-OPEN: the game option is not ported, so null = off.
    /// </summary>
    public Action<string> WhisperBlockedReply;
    /// <summary>UIWhisperNotifyDialog (0x4e5e) AddWhisper(character id, text, window). STILL-OPEN: that dialog is not
    /// ported; null without it.</summary>
    public Action<long, string, string> WhisperNotify;
    /// <summary>CUIService FUN_141add1f0, a type 0xd / 0xe line with string id 0x35. STILL-OPEN: not identified.</summary>
    public Action FieldStringNotice;
    /// <summary>FUN_140d2ab10 (unit_140d2a7e0_chatlinkhelpers.cpp:227), a type 0xf announcement: the text made the line
    /// its link kind stands for, null when unusable. STILL-OPEN: not ported, so every type 0xf line is dropped.</summary>
    public Func<string, string> Announcement = _ => null;
    /// <summary>CUserCommandCli::ExecuteInternal(text), a type 10 line. STILL-OPEN: the user commands are not ported.</summary>
    public Action<string> UserCommand;
    /// <summary>ShowChatBalloon(name, text, type), the speech balloon over the speaker (<see cref="ChatSpeakerBalloon.Show"/>).</summary>
    public Action<string, string, int> ChatBalloon;
    /// <summary>CFeatureManager::IsFeatureEnabled; the model's when null.</summary>
    public Func<int, bool> FeatureEnabled;
    /// <summary>CClubClientManager::GetClubName for type 0x14 lines (see <see cref="ChatHtml.Context.ClubName"/>).</summary>
    public Func<long, string> ClubName = _ => "";

    /// <summary>The wedding feature vfunc_84 tests before a type 0x19 line.</summary>
    public const int WeddingFeature = 0x104;
    /// <summary>The system sounds of a whisper and a guild megaphone line (uimessenger.cpp s_strWhisperSound,
    /// s_strGuildMegaphoneSound).</summary>
    public const string WhisperSound = "system_notice_whisper", GuildMegaphoneSound = "System_GuildMegaphone";

    bool Feature(int id) => FeatureEnabled?.Invoke(id) ?? Model?.IsFeatureEnabled(id) ?? false;

    /// <summary>
    /// vfunc_84 (:2829). The text (the reader's: a common string id's GetStringCommon, else the packet's) loses its P, BR,
    /// TEXTFORMAT and FONT tags (FUN_140d2c650 / <see cref="RemoveHtmlTag"/>). STILL-OPEN: CBanTextChecker's
    /// MaskBannedWords and FilterReservedWords and CDynamicBanWordManager::FilterText are not ported, so the text is not
    /// masked. An empty text or a blinded name ends it; then by type: 5 the common string 0x4d as the text and as a system
    /// notice; 6 FormatStringId(0x8cd, name) as a notice; 3 a whisper (the block reply, else each window's
    /// <see cref="MessengerController.WhisperFrom"/>, the notify dialog and the whisper sound); 0x11 and 0x1a the text as a
    /// logged notice; 0xd / 0xe the same, or FUN_141add1f0 for string id 0x35; 0xf only when the announcement is usable;
    /// 0x10 / 0x19 for the local player's own line FormatStringId(0xac7 / 0xaca, the item's name) as a logged notice.
    /// Type 10 is a user command and ends it. Other than a whisper sent (4), a blocked name or character ends it. The item
    /// links are coloured (<see cref="ColorItemLinks"/>, :2939). The balloon, then for 0x12 s_guild_chat_mega_phone as a guild
    /// megaphone notice with its sound. The html is the wedding line for 0x19 (read only with feature 0x104, else the line
    /// is dropped; STILL-OPEN: FUN_141841180 needs the wedding table, so "") or <see cref="ChatHtml.Line"/> at the shared
    /// font size, and goes into the log (FUN_141a26e00) and the report log (<see cref="ReportLine"/>) by its kind
    /// (<see cref="ChatHtml.Kind"/>). <paramref name="readWedding"/> reads the packet's WeddingChatHall.
    /// </summary>
    public void OnUserChat(ChatMessage m, Func<WeddingChatHall> readWedding = null)
    {
        var t = Tables;
        string name = m.Name ?? "";
        string text = m.Common ? t.Common(m.CommonId) ?? "" : m.Text ?? "";
        int type = m.Type;
        long charId = m.CharacterId;
        text = RemoveChatTags(text);
        if (text.Length == 0) return;
        if (IsBlinded(name)) return;
        switch (type)
        {
            case 5:
                text = t.Common(0x4d) ?? "";
                if (text.Length > 0) ShowSystemNotice?.Invoke(text, 1, false);        // FUN_141275260(0x4d, 1, 0)
                break;
            case 6:
                text = ClientFormat.Format(t.Common(0x8cd), name);
                ShowSystemNotice?.Invoke(text, 1, false);
                break;
            case 3:
                if (WhisperBlockedReply != null) { WhisperBlockedReply(name); return; }
                foreach (var w in Windows) w?.WhisperFrom(text, m.WhisperWindow ?? "");
                WhisperNotify?.Invoke(charId, text, m.WhisperWindow ?? "");
                SystemSound?.Invoke(WhisperSound);
                break;
            case 0x11 or 0x1a:
                ShowSystemNotice?.Invoke(text, 1, true);
                break;
            case 0xd or 0xe:
                if (m.CommonId == 0x35) FieldStringNotice?.Invoke();
                else ShowSystemNotice?.Invoke(text, 1, true);
                break;
            case 0xf:
                if (Announcement(text) is not { } line) return;
                text = line;
                break;
            case 0x10 or 0x19:
                if (Model?.HasLocalPlayer == true && LocalCharacterId() == charId)
                    ShowSystemNotice?.Invoke(ClientFormat.Format(t.Common(type == 0x10 ? 0xac7 : 0xaca), t.ItemName(m.ItemId)), 1, true);
                break;
        }
        if (type == 10) { UserCommand?.Invoke(text); return; }
        if (type != 4 && IsBlockedBuddy(name, charId)) return;
        text = ColorItemLinks(text);
        ChatBalloon?.Invoke(name, text, type);
        if (type == 0x12)
        {
            ShowSystemNotice?.Invoke(ClientFormat.Format(t.Common("s_guild_chat_mega_phone"), name, text), SystemTextController.GuildMegaphone, false);
            SystemSound?.Invoke(GuildMegaphoneSound);
        }
        string html;
        if (type == 0x19)
        {
            if (!Feature(WeddingFeature)) return;
            readWedding?.Invoke();
            html = "";
        }
        else
        {
            var c = new ChatHtml.Context
            {
                Tables = t, LocalName = Model?.HasLocalPlayer == true ? Model.Player.Name : null, FeatureEnabled = Feature, ClubName = ClubName,
            };
            html = ChatHtml.Line(c, m, text, (uint)GlobalScreen.FontSize);
        }
        int kind = ChatHtml.Kind(type);
        AddLog(html, kind);
        ReportLine(name, html, kind);
    }

    /// <summary>FUN_140d2c650 (unit_140d2a7e0_chatlinkhelpers.cpp:468): the P, BR, TEXTFORMAT and FONT tags removed.</summary>
    public static string RemoveChatTags(string text)
    {
        foreach (var tag in new[] { "P", "BR", "TEXTFORMAT", "FONT" }) text = RemoveHtmlTag(text, tag);
        return text;
    }

    /// <summary>
    /// RemoveHtmlTag (@0x140d2c730, :478), case-blind: up to 20 times the first "&lt;tag" up to its "&gt;" and then the first
    /// "&lt;/tag&gt;" are removed; an open tag without its "&gt;" or its close clears the text, and so does an open tag
    /// (matched case-sensitively, as the client's last Find on the text) left after 20.
    /// </summary>
    public static string RemoveHtmlTag(string text, string tag)
    {
        string lowerTag = Lower(tag), open = "<" + lowerTag, end = "</" + lowerTag + ">";
        var sb = new System.Text.StringBuilder(text);
        var lower = new System.Text.StringBuilder(Lower(text));
        for (int n = 0; n < 20; n++)
        {
            int begin = lower.ToString().IndexOf(open, StringComparison.Ordinal);
            if (begin < 0) return sb.ToString();
            int close = lower.ToString().IndexOf('>', begin);
            if (close < 0 || close <= begin) return "";
            lower.Remove(begin, close - begin + 1);
            sb.Remove(begin, close - begin + 1);
            begin = lower.ToString().IndexOf(end, StringComparison.Ordinal);
            if (begin < 0) return "";
            lower.Remove(begin, end.Length);
            sb.Remove(begin, end.Length);
        }
        string s = sb.ToString();
        return s.IndexOf(open, StringComparison.Ordinal) >= 0 ? "" : s;
    }

    /// <summary>CStringW::MakeLower, one character for one (the indexes stay the text's).</summary>
    static string Lower(string s) => string.Create(s.Length, s, (span, src) => { for (int i = 0; i < src.Length; i++) span[i] = char.ToLowerInvariant(src[i]); });

    /// <summary>A report line: the speaker and the line's html (MessengerData::ReportLog).</summary>
    public readonly record struct ReportLog(string Name, string Text);

    readonly List<(long Time, string Line)> timedLogs = new();    // m_timedLogs, a multimap by server time
    readonly LinkedList<ReportLog> reports = new();               // m_reports
    /// <summary>GetServerTime for the report lines.</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public IReadOnlyList<(long Time, string Line)> TimedLogs => timedLogs;
    public IReadOnlyCollection<ReportLog> Reports => reports;

    /// <summary>
    /// FUN_141a271b0 (:3772): a user line of kind 1, 2, 3, 0x18, 4, 0x15, 0x16, 0x17 or 0x1a ([Normal], [Party], [Guild],
    /// [GuildMegaphone], [Whisper], [World], [Channel], [Super], [Club]) is kept as "tag[name] html" by the server time
    /// (the earliest dropped past 51) and as a report line (the oldest dropped past 500); false for other kinds.
    /// </summary>
    public bool ReportLine(string name, string html, int kind)
    {
        string tag = kind switch
        {
            1 => "[Normal]", 2 => "[Party]", 3 => "[Guild]", 0x18 => "[GuildMegaphone]", 4 => "[Whisper]", 0x15 => "[World]",
            0x16 => "[Channel]", 0x17 => "[Super]", 0x1a => "[Club]", _ => null,
        };
        if (tag == null) return false;
        if (timedLogs.Count > 0x32) timedLogs.RemoveAt(0);
        long now = ServerTime();
        int at = timedLogs.FindLastIndex(e => e.Time <= now) + 1;   // multimap::insert: after the equal keys
        timedLogs.Insert(at, (now, $"{tag}[{name}] {html}"));
        reports.AddLast(new ReportLog(name, html));
        if (reports.Count > MaxLogs) reports.RemoveFirst();
        return true;
    }

    // Unk_MdNames (messengerdata.cpp:746), the messenger state's window names: the focused ones, the last at the tail
    readonly LinkedList<string> names = new();               // m_names
    string currentName = "";                                  // m_strCurrent

    /// <summary>Unk_MdNames::FUN_141482c80 (messengerdata.cpp:802), a window's focus-in (UIMessenger::vfunc_90,
    /// uimessenger.cpp:2141): the name moves to the tail and is the current one; an empty name is ignored.</summary>
    public void MakeCurrent(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        RemoveName(name);
        names.AddLast(name);
        currentName = name;
    }

    /// <summary>Unk_MdNames::GetCurrentName (@0x1414829e0, messengerdata.cpp:754): with no current name the next one is
    /// taken first (<see cref="SelectNextName"/>).</summary>
    public string GetCurrentName()
    {
        if (currentName.Length == 0) SelectNextName();
        return currentName;
    }

    /// <summary>Unk_MdNames::SelectNextName (@0x141482bb0, messengerdata.cpp:788): with no names the chat log's first
    /// window's (GetFirstWindowName), else the tail name, which leaves the list.</summary>
    void SelectNextName()
    {
        if (names.Count == 0) currentName = Windows.Count > 0 ? Windows[0].Name : "";
        else { currentName = names.Last.Value; names.RemoveLast(); }
    }

    /// <summary>Unk_MdNames::RemoveName (messengerdata.cpp:813).</summary>
    public void RemoveName(string name)
    {
        while (names.Remove(name)) { }
        if (currentName == name) currentName = "";
    }

    /// <summary>Unk_MdNames::GetValidName (messengerdata.cpp:764) with the browser there: the first name from the head
    /// that is a window's (FUN_141a23d70, uimessenger.cpp:3137), else the first window's (GetFirstWindowName, :3454).</summary>
    public string ValidName()
    {
        foreach (var n in names)
            if (Windows.Any(w => w.Name == n)) return n;
        return Windows.Count > 0 ? Windows[0].Name : "";
    }

    /// <summary>FUN_141a252a0 (uimessenger.cpp:3378), a whisper in the valid window: each window of that name begins a
    /// whisper to <paramref name="target"/> (UIMessenger::FUN_141a14bb0).</summary>
    public void Whisper(string target)
    {
        string valid = ValidName();
        foreach (var w in Windows.ToArray())
            if (w.Name == valid) w.WhisperTo(target);
    }

    static readonly Regex FontSizes = new("size='(1[2-8])'", RegexOptions.CultureInvariant);

    // ---- the windows: lookup, new names, closing an empty one, a window for detached tabs

    /// <summary>FUN_141a23d70 (:3137): a window of this name is open.</summary>
    public bool HasWindow(string name) => Windows.Any(w => w.Name == name);

    /// <summary>FindMessengerByName (@0x141a184c0, :2529): the UI entity of that name as a chat window (the entities the
    /// chat windows are, <see cref="Windows"/>); null for none.</summary>
    public MessengerController FindMessengerByName(string name) => Windows.FirstOrDefault(w => w.Name == name);

    /// <summary>FUN_141a26cd0 (@0x141a26cd0, :3717): the first "messengerdialog&lt;i&gt;.xml", i below 100, no window has
    /// ("MessengerDialog" lowered by FUN_140841420, as the default windows' names); "" when all are taken.</summary>
    public string NewWindowName()
    {
        for (int i = 0; i < 100; i++)
        {
            string name = "messengerdialog" + i.ToString(CultureInfo.InvariantCulture) + ".xml";
            if (!HasWindow(name)) return name;
        }
        return "";
    }

    /// <summary>FUN_141a25410 (@0x141a25410, :3389): no window has a tab of that name other than <paramref name="except"/>
    /// (UIMessenger::vfunc_86 each).</summary>
    public bool TabNameFree(MessengerController.TalkTab except, string name) => Windows.All(w => w.HasNoTabNamed(except, name));

    /// <summary>A window this browser made (CreateWindowWithTabs) that its host is to show; the default windows are the host's own.</summary>
    public event Action<MessengerController> WindowCreated;
    /// <summary>A window CloseEmptyWindow took away (vfunc_84 and RemoveEntity): its host frees its movie.</summary>
    public event Action<MessengerController> WindowClosed;

    /// <summary>
    /// CloseEmptyWindow (@0x141a245d0, :3227): the window of that name, when it has no tab left, leaves the focus names
    /// (Unk_MdNames::RemoveName), closes (vfunc_84) and leaves m_windows and the entity manager.
    /// </summary>
    public void CloseEmptyWindow(string name)
    {
        for (int i = 0; i < Windows.Count; i++)
        {
            var w = Windows[i];
            if (w.Name != name || w.Tabs.Count != 0) continue;
            RemoveName(w.Name);
            Windows.RemoveAt(i);
            WindowClosed?.Invoke(w);
            return;
        }
    }

    /// <summary>
    /// CreateWindowWithTabs (@0x141a24160, :3176): a &lt;MessengerDialog&gt; document with width, height, offsetX, offsetY,
    /// alignType 5, additive 0, targetSelectedIndex and a &lt;Tab&gt; per tab (TalkTab::FUN_141482880), loaded as a new
    /// window (not a first load) under the next free name (<see cref="NewWindowName"/>), shown and kept.
    /// </summary>
    public MessengerController CreateWindowWithTabs(IReadOnlyList<MessengerController.TalkTab> tabs, int x, int y, int width, int height, int target)
    {
        var root = new XElement("MessengerDialog",
            new XAttribute("width", width), new XAttribute("height", height),
            new XAttribute("offsetX", x), new XAttribute("offsetY", y),
            new XAttribute("alignType", 5), new XAttribute("additive", 0), new XAttribute("targetSelectedIndex", target));
        Func<int, bool> feature = Model != null ? Model.IsFeatureEnabled : _ => false;
        foreach (var tab in tabs)
            if (tab != null) tab.Write(PanelSetting.Child(root, "Tab"), this, feature);
        var window = new MessengerController(new XDocument(root), NewWindowName(), false, this);
        WindowCreated?.Invoke(window);
        return window;
    }

    /// <summary>The messenger state's m_nNewTabMode and m_pNewTabWindow, read by the tab name dialog (0x4e62): 1 = a new tab
    /// (the tab menu), 2 = rename the additive one just made.</summary>
    public int NewTabMode;
    public MessengerController NewTabWindow;
    /// <summary>CUIService::IsDialogClosed(0x4e62), the tab name dialog; it is not ported, so it reads closed.</summary>
    public Func<bool> IsNewTabDialogClosed = () => true;
    /// <summary>CUIService::OpenDialog(0x4e62), the tab name dialog (STILL-OPEN: not ported; the HUD logs it).</summary>
    public Action OpenNewTabDialog;
    /// <summary>CUIService::ToggleDialog(0x4f80), the chat emoticon dialog (STILL-OPEN: not ported; the HUD logs it).</summary>
    public Action ToggleEmoticonDialog;
    /// <summary>
    /// The checks of SendEmoticon / SetEmoticonFavorite: the emoticon is in CChatEmoticonTable and the local character's
    /// CChatEmoticonCli IsEmoticonUsable. STILL-OPEN: neither the table nor the character's sticker sets are ported, so
    /// none is usable unless the host says so.
    /// </summary>
    public Func<int, bool> IsEmoticonUsable = _ => false;
    /// <summary>The ChatSticker sends (SendOp 0xB7, FUN_140710590).</summary>
    public Action<Ms2.Net.Packets.ChatStickerRequest> SendSticker;

    /// <summary>UIMessenger::ReplaceFontSize (:1143): each size='12' .. size='18' becomes the given size.</summary>
    public static string ReplaceFontSize(uint size, string html) =>
        FontSizes.Replace(html, "size='" + size.ToString(CultureInfo.InvariantCulture) + "'");
}
