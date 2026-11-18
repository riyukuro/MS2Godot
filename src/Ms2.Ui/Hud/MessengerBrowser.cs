using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The kinds of a chat log line (MessengerData::TalkLog::m_nKind), as the panel filters read them
/// (messengerdata.cpp:286-581). Only the kinds the port writes are named.
/// </summary>
public static class TalkKind
{
    /// <summary>RequireSetting "item" (FUN_1414818b0): the item gain lines (FUN_141a24890).</summary>
    public const int Item = 9;
    /// <summary>SystemSetting "event" and "notice" (FUN_1414813b0): the welcome line and the chat copies of the notices.</summary>
    public const int SystemEvent = 0x13, SystemNotice = 0x14;
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
/// ported; nor are the chat packets (RecvOp 0x1d / 0x1e, vfunc_84 / vfunc_85) and the font size rewrite of the log
/// (FUN_141a27ad0 :3873).
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
        string name = Model != null ? new ItemTooltip(Model, 0).Name(item) ?? "" : t.ItemName(item.ItemId);
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

    /// <summary>FUN_141a23c20 (:3114): the log, the pending lines and the shown count cleared (of what it clears, the
    /// rest is not ported).</summary>
    public void Clear()
    {
        shown = 0;
        logs.Clear();
        pending.Clear();
    }

    static readonly Regex FontSizes = new("size='(1[2-8])'", RegexOptions.CultureInvariant);

    /// <summary>UIMessenger::ReplaceFontSize (:1143): each size='12' .. size='18' becomes the given size.</summary>
    public static string ReplaceFontSize(uint size, string html) =>
        FontSizes.Replace(html, "size='" + size.ToString(CultureInfo.InvariantCulture) + "'");
}
