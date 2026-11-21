using System.Globalization;
using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.ChatEmoticons;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIChatEmoticonDialog (ms2/client/ms2/uichatemoticondialog.cpp, UI 0x4f80, "ChatEmoticon", movie uichatemoticondialog): the
/// tabs (the favorites, one per category of an owned pack) and the grid of the selected tab's emoticons, over the local
/// player's <see cref="ChatEmoticonState"/>. A pick is posted to the messenger window named by <see cref="CurrentMessenger"/>
/// (PostEmoticonSelected @0x140bd28b0).
/// STILL-OPEN: onClickShopButton and the shop tab (-2) open the Merat market dialog (OnClickShopButton @0x140bd2990,
/// SearchByCategory(1, 0x9dd0)), which is not ported, so they do nothing; onFocusIn / onFocusOut / onShiftRClick are
/// not handled by the client's vfunc_28 either.
/// </summary>
public sealed class ChatEmoticonDialogController : UiController
{
    public override string Movie => "uichatemoticondialog";
    protected override string EntityMethod => "buildTab";
    public override string TypeName => "ChatEmoticon";

    /// <summary>The favorites tab's id and its icon (buildTab @0x140bd1720, s_strCategoryPath).</summary>
    public const int FavoritesTab = -1;
    public const string FavoritesIcon = "./Data/Resource/Image/Emoticon/Category/tab_bookmark.png";
    /// <summary>The shop tab's id (SelectTab's -2).</summary>
    public const int ShopTab = -2;
    /// <summary>The guide occurrence AddNewGroup queues (GuideOccurrence.ChatEmoticon).</summary>
    public const int ChatEmoticonOccurrence = 9;

    readonly ChatEmoticonState state;
    readonly HashSet<uint> newGroups = new();       // m_setNewGroups
    readonly List<int> tabs = new();                // m_tabs: the categories in the order of the packs
    readonly HashSet<int> newTabs = new();          // m_setNewTabs
    readonly Dictionary<int, List<(bool New, uint Id)>> emoticons = new();      // m_emoticons by category
    int selTab = -1;            // m_nSelTab
    long nextExpire;            // m_nNextExpire: the soonest expiry among the shown enabled emoticons; 0 = none

    /// <summary>NotifyChatEmoticonSelected(messenger name, emoticon id) (PostEmoticonSelected).</summary>
    public Action<string, uint> Selected = (_, _) => { };
    /// <summary>The current messenger's name (GetState()->GetCurrentName()).</summary>
    public Func<string> CurrentMessenger = () => "";
    /// <summary>The guide manager's QueueOccurrence; null = no guide manager.</summary>
    public Action<int, int> QueueOccurrence;
    /// <summary>CStringTable common string by id (blocks 0xb8 / 0xb9).</summary>
    public Func<int, string> CommonText = _ => "";

    public ChatEmoticonDialogController(ChatEmoticonState state) { this.state = state; }

    bool NoMovie => Host?.Entity == null;

    public IReadOnlyList<int> Tabs => tabs;
    public int SelectedTab => selTab;
    public long NextExpire => nextExpire;
    public bool IsNewTab(int id) => newTabs.Contains(id);
    public IReadOnlyList<(bool New, uint Id)> TabEmoticons(int tab) => emoticons.TryGetValue(tab, out var l) ? l : Array.Empty<(bool, uint)>();
    /// <summary>The last buildTab / buildItem arrays.</summary>
    public IReadOnlyList<object> TabData { get; private set; } = Array.Empty<object>();
    public IReadOnlyList<object> ItemData { get; private set; } = Array.Empty<object>();

    /// <summary>OnLoadComplete (@0x140bd1080): m_nSelTab -1, m_nNextExpire 0, the list request (SendMessenger), then the model,
    /// the tabs and the items.</summary>
    public Action<Ms2.Net.Packets.ChatStickerListRequest> Send = _ => { };

    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        selTab = -1;
        nextExpire = 0;
        state.Refresh -= Rebuild; state.Refresh += Rebuild;
        state.Clear -= ClearEmoticonData; state.Clear += ClearEmoticonData;
        state.NewGroup -= AddNewGroup; state.NewGroup += AddNewGroup;
        Unloaded -= Unhook; Unloaded += Unhook;
        Send(state.ListRequest());
        BuildModel();
        BuildTab();
        BuildItem();
    }

    void Unhook()
    {
        state.Refresh -= Rebuild;
        state.Clear -= ClearEmoticonData;
        state.NewGroup -= AddNewGroup;
    }

    /// <summary>vfunc_26: the new-pack marks are cleared.</summary>
    public void ClearNewGroups() => newGroups.Clear();

    /// <summary>vfunc_72, each frame: an expiry passed in the shown tab rebuilds the items.</summary>
    public void Process()
    {
        if (NoMovie || nextExpire == 0) return;
        if (nextExpire < state.Now()) BuildItem();
    }

    /// <summary>ClearEmoticonData: the marks, tabs and lists emptied.</summary>
    public void ClearEmoticonData()
    {
        newGroups.Clear(); tabs.Clear(); newTabs.Clear(); emoticons.Clear();
    }

    /// <summary>AddNewGroup (@0x140bd26c0): the pack is marked new; the guide gets the ChatEmoticon occurrence.</summary>
    public void AddNewGroup(uint group)
    {
        newGroups.Add(group);
        QueueOccurrence?.Invoke(ChatEmoticonOccurrence, 0);
    }

    /// <summary>FUN_140bd2620: the model rebuilt, then the tabs and the items.</summary>
    public void Rebuild()
    {
        if (NoMovie) return;
        BuildModel();
        BuildTab();
        BuildItem();
    }

    /// <summary>
    /// The model rebuild (@0x140bd1fc0): the tab, new-tab and emoticon lists cleared; per owned pack in order, the pack's
    /// emoticons (new when the pack is marked) go under their category (a category first seen is added to the tabs, and to
    /// the new tabs when the pack is new); then the favorites list (-1) takes the favorite ids whose emoticon exists.
    /// </summary>
    void BuildModel()
    {
        tabs.Clear(); newTabs.Clear(); emoticons.Clear();
        foreach (var g in state.Groups)
        {
            bool isNew = newGroups.Contains(g.Id);
            foreach (var e in state.Table.GetEmoticonsByGroup(g.Id))
            {
                int cat = (int)e.CategoryId;
                if (!emoticons.TryGetValue(cat, out var list))
                {
                    emoticons[cat] = list = new List<(bool, uint)>();
                    tabs.Add(cat);
                }
                if (isNew) newTabs.Add(cat);
                list.Add((isNew, e.Id));
            }
        }
        var fav = new List<(bool, uint)>();
        foreach (uint id in state.Favorites)
            if (state.Table.GetEmoticon(id) != null) fav.Add((false, id));
        emoticons[FavoritesTab] = fav;
    }

    /// <summary>buildTab (@0x140bd1720): the favorites tab, then a tab per known category.</summary>
    void BuildTab()
    {
        var data = new List<object> { Tab(FavoritesTab, FavoritesIcon, false) };
        foreach (int cat in tabs)
        {
            var c = state.Table.GetCategory((uint)cat);
            if (c == null) continue;
            data.Add(Tab(cat, c.IconPath, newTabs.Contains(cat)));
        }
        TabData = data;
        Host?.Invoke("buildTab", data);
    }

    static Dictionary<string, object> Tab(int id, string icon, bool isNew) =>
        new() { ["id"] = id, ["imgPath"] = icon, ["newIcon"] = isNew };

    /// <summary>
    /// buildItem (@0x140bd1a60): the movie selects the tab, then the items of the tab's list: an unknown emoticon is
    /// skipped; "enable" = its pack is not expired; the soonest expiry among the enabled ones becomes m_nNextExpire
    /// (int64 max when none, which the client's vfunc_72 never reaches: it keeps m_nNextExpire as is).
    /// </summary>
    void BuildItem()
    {
        Host?.Invoke("selectTab", selTab);
        var items = new List<object>();
        nextExpire = long.MaxValue;
        foreach (var (isNew, id) in TabEmoticons(selTab))
        {
            var e = state.Table.GetEmoticon(id);
            if (e == null) continue;
            bool enable = !state.IsGroupExpired(e.GroupId);
            items.Add(new Dictionary<string, object> { ["id"] = id, ["imgPath"] = e.ImagePath, ["enable"] = enable, ["newIcon"] = isNew });
            if (enable && state.FindGroup(e.GroupId) is { } g && g.Expire < nextExpire) nextExpire = g.Expire;
        }
        ItemData = items;
        Host?.Invoke("buildItem", items);
    }

    /// <summary>vfunc_28 (@0x140bd1540): onSelectTab, onSelectEmoticon, onClickShopButton.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (base.OnCallback(name, args)) return true;
        switch (name.ToLowerInvariant())
        {
            case "onselecttab":
                if (args.Length == 0) return false;
                SelectTab((int)Rt.ToUint32(args[0]));
                return false;
            case "onselectemoticon":
            {
                if (args.Length == 0) return false;
                uint id = Rt.ToUint32(args[0]);
                if (id != 0) PostEmoticonSelected(id);
                return false;
            }
            case "onclickshopbutton":
                OnClickShopButton();
                return false;
        }
        return false;
    }

    /// <summary>SelectTab (@0x140bd27e0): the shop tab (-2) runs the shop button and reselects the current tab; the same tab
    /// does nothing; another tab is kept and its items built.</summary>
    public void SelectTab(int tab)
    {
        if (tab == ShopTab)
        {
            OnClickShopButton();
            Host?.Invoke("selectTab", selTab);
            return;
        }
        if (tab == selTab) return;
        selTab = tab;
        BuildItem();
    }

    /// <summary>PostEmoticonSelected (@0x140bd28b0): NotifyChatEmoticonSelected(current messenger name, id).</summary>
    public void PostEmoticonSelected(uint id) => Selected(CurrentMessenger() ?? "", id);

    /// <summary>OnClickShopButton (@0x140bd2990). STILL-OPEN: the Merat market dialog (0x4ec1, SearchByCategory(1, 0x9dd0)).</summary>
    public void OnClickShopButton() { }

    /// <summary>GetEmoticonCommand (@0x140bd2720): the emoticon's ":command:", "" when unknown.</summary>
    public string GetEmoticonCommand(uint id) => state.Table.GetEmoticon(id)?.Command ?? "";

    /// <summary>
    /// vfunc_49 (@0x140bd11e0): the emoticon's pack expiry as a text tooltip (align 4): common string 0xb8, or 0xb9 once the
    /// expiry has passed, with the year, month, day, hour and minute of the expiry. No tooltip for an unknown emoticon or
    /// pack, nor for a pack that never expires (int64 max).
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (args.Length == 0) return null;
        var e = state.Table.GetEmoticon(Rt.ToUint32(args[0]));
        if (e == null || state.FindGroup(e.GroupId) is not { } g || g.Expire == long.MaxValue) return null;
        var t = Ms2.Net.MsTime.LocalTm(g.Expire);
        string text = ClientFormat.Format(CommonText(state.Now() > g.Expire ? 0xb9 : 0xb8), t.Year, t.Month, t.Day, t.Hour, t.Minute);
        var tip = new AsTooltip();
        tip["tooltip"] = "export_common_tooltip_tooltipText";
        tip["onDelay"] = 300;
        tip["offDelay"] = 60000;
        tip.SetTooltipAlign(4);
        tip["text"] = text;
        return tip;
    }
}
