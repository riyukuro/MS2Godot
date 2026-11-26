#if MS2_DEVTOOLS
using System.Diagnostics;
using System.Text;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Items;
using Ms2.Devtools;
using Ms2Godot.Ui;

namespace Ms2Godot;

/// <summary>
/// The debug "creative menu" (a developer tool, not part of the client port): every item of the KMS2 data in a searchable,
/// tab-filtered grid. A click spawns the item in the bag through the server's own GM command as a chat line
/// (<see cref="ItemSpawnCommand"/>); the server's answer shows in the line under the grid.
/// Gating, like the debug endpoint: compiled only with MS2_DEVTOOLS, built only with env MS2_DEBUG=1
/// (<see cref="DevEndpoint.EnvOn"/>), and opened only for a KMS live session and KMS2 data (the data's locale through
/// <see cref="LobbyFeatures"/>); on GMS2 it logs one line and does not open.
/// Key: NumLock. The client's key table gives a key only to an id 1..0xcf with a keyname row (CKeyTable::InitKeys,
/// keytable.cpp:622-629; string/kr/keyname.xml has no id 69, DIK_NUMLOCK), table/default/defaultkey.xml binds no keyCode
/// 69 (FUN_1414297e0, keytable.cpp:429), our own key map (HudScene.GodotKeyOfDik) has no 0x45, and the navmesh debug's
/// F9 keeps its key.
/// </summary>
public partial class CreativeMenu : Control
{
    /// <summary>The menu of the running HUD (one at a time; a field move rebuilds it).</summary>
    public static CreativeMenu Current { get; private set; }

    public static bool IsOpen => Current is { Visible: true } && IsInstanceValid(Current);

    const int CanvasLayerIndex = 16;        // over the message box (12), the guide (13) and the tooltip (14)
    const int Columns = 10;
    const float CellW = 96, CellH = 84, IconPx = 56;
    const int PoolRows = 7;                 // a 480 px grid shows 5.7 rows of 84; one spare row
    const int IconCacheMax = 256;

    static readonly StringName FontColor = "font_color";   // one native name, not one per call
    static readonly Color Backdrop = new(0, 0, 0, 0.55f);
    static readonly Color PanelColor = new("1e242e");
    static readonly Color MutedColor = new("98a4b5");
    static readonly Color ErrorColor = new("ff7a7a");

    sealed class Cell
    {
        public Button Button;
        public TextureRect Icon;
        public Label Name;
        public int AbsRow = -1, Col, Row, Entry = -1, Rank = -1;
        public string IconEntry;
    }

    UiAssets assets;
    UiModel model;
    PackSet packs;
    GameTables tables;
    ItemCatalog catalog;
    Ms2Godot.Net.LiveSession noticed;

    readonly List<Cell> cells = new();
    readonly List<int> matches = new();
    readonly Dictionary<string, Texture2D> icons = new(StringComparer.OrdinalIgnoreCase);
    readonly Queue<string> iconOrder = new();
    Color[] rankColors;

    LineEdit search;
    OptionButton tabBox, rarityBox;
    SpinBox amountBox;
    ScrollContainer scroll;
    Control grid;
    Label status, details, group, excluded;

    int tab = -1, firstRow = -1, shownTab = -1;
    readonly int[] tabCounts = new int[ItemCatalog.TabKeys.Length];
    readonly int[] visibleTabs = new int[ItemCatalog.TabKeys.Length];
    readonly string[] tabText = new string[ItemCatalog.TabKeys.Length];
    string lastLine = "", lastAnswer = "";
    long scrollEvents, scrollBytes, iconLoads;
    double catalogMs;

    /// <summary>Adds the menu to a HUD when env MS2_DEBUG=1 (the debug endpoint's own switch); a no-op otherwise.</summary>
    public static void Install(Node host, UiAssets assets, UiModel model)
    {
        if (OS.GetEnvironment(DevEndpoint.EnvOn) != "1") return;
        if (Current != null && IsInstanceValid(Current)) return;
        var layer = new CanvasLayer { Name = "CreativeMenuLayer", Layer = CanvasLayerIndex };
        host.AddChild(layer);
        var menu = new CreativeMenu { Name = "CreativeMenu", assets = assets, model = model, Visible = false };
        layer.AddChild(menu);
        menu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        menu.Build();
        Current = menu;
        HookMovement();
    }

    static bool movementHooked;

    /// <summary>
    /// The own player's move keys are read every frame (Unk_PCMove through FUN_141683ef0, <see cref="Ms2.Ui.InputBlock"/>),
    /// not through the HUD's key handling, so a focused OptionButton or SpinBox would let the field walk under the window.
    /// The devtool adds its own hold while it is open; the client's block is kept for everything else.
    /// </summary>
    static void HookMovement()
    {
        if (movementHooked) return;
        movementHooked = true;
        var client = Ms2Godot.Movement.MoveInput.Blocked;
        Ms2Godot.Movement.MoveInput.Blocked = () => IsOpen || client();
    }

    /// <summary>NumLock toggles the menu, also while it is open; true = the key was the menu's.</summary>
    public static bool ToggleKey(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || k.PhysicalKeycode != Key.Numlock) return false;
        if (Current == null || !IsInstanceValid(Current)) return false;
        Current.Toggle();
        return true;
    }

    public override void _ExitTree()
    {
        if (noticed != null) { noticed.Notice -= OnNotice; noticed = null; }
        if (Current == this) Current = null;
    }

    public void Toggle()
    {
        if (Visible) { Close(); return; }
        Open();
    }

    /// <summary>Opens the window (building the catalog on the first open); false when the session or data is not KMS.</summary>
    public bool Open()
    {
        if (!Allowed()) return false;
        if (catalog == null)
        {
            var sw = Stopwatch.StartNew();
            catalog = ItemCatalog.Shared(packs, tables);
            catalogMs = sw.Elapsed.TotalMilliseconds;
            string left = catalog.Removed.Count > 0 ? $"{catalog.Removed.Count} beauty-only ({catalog.RemovedSummary()}) left out, " : "";
            GD.Print($"creative: {catalog.Count} bag items of {catalog.Unfiltered}, {left}{catalog.WithoutName} without a name, catalog in {catalogMs:0} ms");
            foreach (var r in catalog.Removed.GroupBy(r => r.Kind).OrderBy(g => (int)g.Key))
                GD.Print($"creative: left out {ItemCatalog.KindName(r.Key)} {r.Count()}: {string.Join(", ", r.Take(6).Select(x => x.Id + " '" + x.Name + "'"))}");
        }
        excluded.Text = catalog.Removed.Count == 0
            ? ""
            : $"{catalog.Removed.Count} beauty-salon items are not bag items and are left out ({catalog.RemovedSummary()}).";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);   // the window may have been resized while hidden
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null && !ReferenceEquals(noticed, live))
        {
            if (noticed != null) noticed.Notice -= OnNotice;
            noticed = live; noticed.Notice += OnNotice;
        }
        search.GrabFocus();
        Refilter(true);
        return true;
    }

    public void Close()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        if (noticed != null) { noticed.Notice -= OnNotice; noticed = null; }
    }

    /// <summary>
    /// KMS2 only, as the brief says: the live session's profile when there is one (GMS2 refuses with one log line), and the
    /// data's locale (the GMS2 data is NA, <see cref="LobbyFeatures.LocaleOf"/>; its item tables are the per-item layout).
    /// </summary>
    bool Allowed()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null && live.GameProfile.IsGms2)
        {
            GD.Print("creative: the live session's profile is GMS2; the item browser opens on the KMS2 profile only");
            return false;
        }
        string locale = LobbyFeatures.LocaleOf(packs);
        if (!string.Equals(locale, "KR", StringComparison.OrdinalIgnoreCase))
        {
            GD.Print($"creative: the data is {locale}, not KMS2 (locale KR); the item browser opens on the KMS2 data only");
            return false;
        }
        return true;
    }

    // ---- layout -------------------------------------------------------------------------------------------------------

    static Theme MakeTheme()
    {
        var theme = new Theme
        {
            DefaultFont = new SystemFont { FontNames = new[] { "Noto Sans CJK KR", "Noto Sans", "DejaVu Sans", "sans-serif" }, Antialiasing = TextServer.FontAntialiasing.Gray },
            DefaultFontSize = 15,
        };
        var panel = new StyleBoxFlat { BgColor = PanelColor, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 };
        panel.SetContentMarginAll(14);
        theme.SetStylebox("panel", "PanelContainer", panel);
        return theme;
    }

    void Build()
    {
        Theme = MakeTheme();
        packs = assets.Packs;
        tables = model.Tables;
        rankColors = new Color[ItemSpawnCommand.MaxRarity + 1];
        for (int r = 0; r < rankColors.Length; r++) rankColors[r] = FromRgb(tables.Items.TextColor(r));

        var dim = new ColorRect { Color = Backdrop, MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(1040, 0) };
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var title = new Label { Text = "Item browser (debug)" };
        title.AddThemeFontSizeOverride("font_size", 20);
        box.AddChild(title);
        excluded = new Label { Text = "", ClipText = true, CustomMinimumSize = new Vector2(0, 18) };
        excluded.AddThemeFontSizeOverride("font_size", 12);
        excluded.AddThemeColorOverride(FontColor, MutedColor);
        box.AddChild(excluded);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        box.AddChild(top);
        search = new LineEdit { PlaceholderText = "name or id", CustomMinimumSize = new Vector2(300, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        search.TextChanged += _ => Refilter(true);
        top.AddChild(search);
        tabBox = new OptionButton { CustomMinimumSize = new Vector2(170, 0) };
        tabBox.AddItem("All tabs", -1);
        for (int t = 0; t < ItemCatalog.TabKeys.Length; t++) tabBox.AddItem(ItemCatalog.TabName(tables, t), t);
        tabBox.ItemSelected += i => { tab = (int)tabBox.GetItemId((int)i); Refilter(true); };
        top.AddChild(tabBox);
        top.AddChild(new Label { Text = "Amount" });
        amountBox = new SpinBox { MinValue = 1, MaxValue = 99999, Step = 1, Value = 1, CustomMinimumSize = new Vector2(90, 0) };
        top.AddChild(amountBox);
        top.AddChild(new Label { Text = "Rarity" });
        rarityBox = new OptionButton { CustomMinimumSize = new Vector2(120, 0) };
        rarityBox.AddItem("item's own", 0);
        for (int r = 1; r <= ItemSpawnCommand.MaxRarity; r++) rarityBox.AddItem(r.ToString(), r);
        top.AddChild(rarityBox);
        var close = new Button { Text = "Close" };
        close.Pressed += Close;
        top.AddChild(close);

        group = new Label { Text = "", ClipText = true, CustomMinimumSize = new Vector2(0, 20), Visible = false };
        group.AddThemeColorOverride(FontColor, MutedColor);
        box.AddChild(group);
        scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 480), SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        box.AddChild(scroll);
        grid = new Control { CustomMinimumSize = new Vector2(Columns * CellW, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(grid);
        scroll.GetVScrollBar().ValueChanged += OnScroll;

        for (int i = 0; i < Columns * PoolRows; i++) cells.Add(MakeCell(i % Columns, i / Columns));

        details = new Label { Text = "", ClipText = true, CustomMinimumSize = new Vector2(0, 22) };
        box.AddChild(details);
        status = new Label { Text = "", ClipText = true, CustomMinimumSize = new Vector2(0, 22) };
        box.AddChild(status);
    }

    Cell MakeCell(int col, int row)
    {
        var b = new Button
        {
            Flat = true, Size = new Vector2(CellW, CellH), Position = new Vector2(col * CellW, row * CellH),
            FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop,
        };
        var icon = new TextureRect
        {
            Position = new Vector2((CellW - IconPx) / 2, 4), Size = new Vector2(IconPx, IconPx),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear, MouseFilter = MouseFilterEnum.Ignore,
        };
        var name = new Label
        {
            Position = new Vector2(4, IconPx + 8), Size = new Vector2(CellW - 8, 18),
            HorizontalAlignment = HorizontalAlignment.Left, ClipText = true, MouseFilter = MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", 13);
        b.AddChild(icon);
        b.AddChild(name);
        grid.AddChild(b);
        var cell = new Cell { Button = b, Icon = icon, Name = name, Col = col, Row = row };
        b.Pressed += () => Click(cell);
        b.MouseEntered += () => Hover(cell);
        return cell;
    }

    static Color FromRgb(uint rgb) => new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);

    // ---- filtering and the grid ---------------------------------------------------------------------------------------

    void Refilter(bool reset)
    {
        catalog.Filter(search.Text, tab, matches);
        Array.Clear(tabCounts);
        foreach (int i in matches) tabCounts[catalog.Entries[i].Tab]++;
        for (int t = 0; t < tabText.Length; t++)
            tabText[t] = tabCounts[t] > 0 ? $"{ItemCatalog.TabName(tables, t)} ({tabCounts[t]} items)" : null;
        int rows = (matches.Count + Columns - 1) / Columns;
        grid.CustomMinimumSize = new Vector2(Columns * CellW, rows * CellH);
        if (reset) { scroll.ScrollVertical = 0; firstRow = -1; shownTab = -1; foreach (var c in cells) c.AbsRow = -1; }   // the item at a row can change
        PlaceCells();
        status.Text = $"{matches.Count} of {catalog.Count} items";
    }

    /// <summary>The vertical scroll's pixel offset as the first shown row (the content's rows are CellH tall).</summary>
    int FirstRow()
    {
        int rows = (matches.Count + Columns - 1) / Columns;
        int scrollable = Math.Max(0, rows - PoolRows);
        return Math.Clamp((int)(scroll.ScrollVertical / CellH), 0, scrollable);
    }

    /// <summary>
    /// The scroll handler: only a row boundary changes anything (the ScrollContainer moves the content for the rows in
    /// between). It reuses the cell pool, loads an icon only for an assigned cell and allocates nothing itself after the
    /// first call, which only grows the icon cache; measured around the call.
    /// </summary>
    void OnScroll(double value)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        scrollEvents++;
        int first = FirstRow();
        if (first != firstRow) { firstRow = first; PlaceCells(); }
        scrollBytes += GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Assigns the pool's cells to the visible rows; a cell of an unchanged row keeps its item and its icon.</summary>
    void PlaceCells()
    {
        if (firstRow < 0) firstRow = FirstRow();
        foreach (var c in cells)
        {
            int absRow = firstRow + c.Row;
            if (absRow == c.AbsRow) continue;
            c.AbsRow = absRow;
            c.Button.Position = new Vector2(c.Col * CellW, absRow * CellH);
            int at = absRow * Columns + c.Col;
            Assign(c, at < matches.Count ? matches[at] : -1);
        }
        UpdateGroup();
    }

    /// <summary>
    /// The all-tabs view names the group on screen (the entries are sorted by bag tab, so every tab's items sit together):
    /// the label above the grid takes the tab most of the visible cells belong to, so a group boundary inside the top row
    /// does not label a screenful of the next group with the sliver above it. The text of each tab is built when the
    /// filter changes, so scrolling sets a label only when the shown group changes and never builds a string.
    /// </summary>
    void UpdateGroup()
    {
        if (tab >= 0 || matches.Count == 0) { group.Visible = false; shownTab = -1; return; }
        Array.Clear(visibleTabs);
        int start = Math.Max(0, firstRow) * Columns, end = Math.Min(matches.Count, (Math.Max(0, firstRow) + PoolRows) * Columns);
        for (int i = start; i < end; i++) visibleTabs[catalog.Entries[matches[i]].Tab]++;
        int best = -1;
        for (int t = 0; t < visibleTabs.Length; t++)
            if (visibleTabs[t] > 0 && (best < 0 || visibleTabs[t] > visibleTabs[best])) best = t;
        if (best < 0) { group.Visible = false; shownTab = -1; return; }
        group.Visible = true;
        if (best == shownTab) return;
        shownTab = best;
        group.Text = tabText[best] ?? "";
    }

    void Assign(Cell c, int entryIndex)
    {
        if (entryIndex < 0) { c.Entry = -1; c.Button.Visible = false; return; }
        var e = catalog.Entries[entryIndex];
        c.Entry = entryIndex;
        c.Button.Visible = true;
        c.Name.Text = e.Name.Length > 0 ? e.Name : "(no name)";
        if (c.Rank != e.Rank) { c.Rank = e.Rank; c.Name.AddThemeColorOverride(FontColor, rankColors[Math.Clamp(e.Rank, 0, rankColors.Length - 1)]); }
        if (!string.Equals(c.IconEntry, e.IconEntry, StringComparison.Ordinal)) { c.IconEntry = e.IconEntry; c.Icon.Texture = Icon(e.IconEntry); }
    }

    /// <summary>The item's tooltip text: its name, id, tab, rank and slot max (the client's tooltip is the movie's).</summary>
    string Tooltip(ItemCatalog.Entry e)
    {
        var sb = new StringBuilder(96);
        sb.Append(e.Name.Length > 0 ? e.Name : "(no name)");
        sb.Append("\nid ").Append(ItemCatalog.IdText(e.Id));
        sb.Append("  tab ").Append(ItemCatalog.TabName(tables, e.Tab));
        sb.Append("\nrank ").Append(e.Rank).Append("  max stack ").Append(e.SlotMax);
        return sb.ToString();
    }

    /// <summary>
    /// The icon of a Resource/Image entry, from a bounded first-in-first-out cache (256); a miss reads it through
    /// <see cref="UiAssets.ImageFileUncached"/> and keeps the texture, a missing one as null so it is not read again. Only
    /// cells that become visible ask, so the menu never loads all icons.
    /// </summary>
    Texture2D Icon(string entry)
    {
        if (icons.TryGetValue(entry, out var cached)) return cached;
        var tex = assets.ImageFileUncached(entry);
        icons[entry] = tex;
        iconOrder.Enqueue(entry);
        iconLoads++;
        while (icons.Count > IconCacheMax && iconOrder.Count > 0)
        {
            string old = iconOrder.Dequeue();
            if (!icons.Remove(old, out var evicted)) continue;
            if (evicted != null && !Shown(old)) evicted.Dispose();   // a cell still showing it keeps its own reference
        }
        return tex;
    }

    /// <summary>Whether a visible cell still draws the entry (its texture is not ours to free then).</summary>
    bool Shown(string entry)
    {
        foreach (var c in cells)
            if (c.Entry >= 0 && c.Button.Visible && string.Equals(c.IconEntry, entry, StringComparison.Ordinal)) return true;
        return false;
    }

    void Hover(Cell c)
    {
        if (c.Entry < 0) return;
        var e = catalog.Entries[c.Entry];
        c.Button.TooltipText = Tooltip(e);        // built on hover, so scrolling never builds one
        details.Text = $"{e.Name}  (id {ItemCatalog.IdText(e.Id)}, {ItemCatalog.TabName(tables, e.Tab)}, rank {e.Rank}, max stack {e.SlotMax})";
        // the amount box is left alone: it is what the next item's command sends, and clamping it to this item's slot max
        // would keep a non-stackable hover from reducing the amount a later potion spawns (ItemSpawnCommand caps a stack).
    }

    // ---- spawning -----------------------------------------------------------------------------------------------------

    /// <summary>The grid's click: spawn the cell's item, the amount field, the rarity picker; Shift = a full stack.</summary>
    void Click(Cell c)
    {
        if (c.Entry < 0) return;
        var e = catalog.Entries[c.Entry];
        bool full = Input.IsKeyPressed(Key.Shift);
        int amount = full ? Math.Max(1, e.SlotMax) : (int)amountBox.Value;
        Spawn(e, amount, SelectedRarity());
    }

    int SelectedRarity() => (int)rarityBox.GetItemId(rarityBox.Selected);

    /// <summary>
    /// Sends the spawn as the live session's normal chat line (UserChat 0x11, type 0, ChatType.Normal, as the chat window's
    /// normal line goes out). Every spawn re-checks the KMS gate, so an endpoint call on a menu left from another session's
    /// catalog cannot send.
    /// </summary>
    public string Spawn(ItemCatalog.Entry e, int amount, int rarity)
    {
        if (!Allowed()) return null;
        string line = ItemSpawnCommand.Line(e.Id, amount, rarity, (uint)e.SlotMax);
        lastLine = line;
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live?.GameSession == null)
        {
            ShowStatus("no live session: " + line, error: true);
            GD.Print($"creative: {line} not sent: no live session");
            return null;
        }
        live.SendChat(new Ms2.Net.Packets.UserChatRequest(0, line, "", 0));
        ShowStatus("sent " + line, error: false);
        GD.Print($"creative: sent {line}");
        return line;
    }

    /// <summary>The server's answer: the notice its command router sends (the unknown command, or the item's error).</summary>
    void OnNotice(Ms2.Net.Packets.NoticeMessage n)
    {
        if (!Visible) return;
        string text = n.Text.Formatted ? ClientFormat.Format(tables.Common(n.Text.Id), n.Text.Args) : n.Text.Text ?? "";
        text = System.Net.WebUtility.HtmlDecode(StripTags(text));   // the command output is html-encoded (&#39; for a quote)
        if (text.Length == 0) return;
        lastAnswer = text;
        ShowStatus("server: " + text, error: true);
    }

    /// <summary>The command output's tags (&lt;b&gt; around "Name:", &lt;br&gt;) removed for the one-line label.</summary>
    static string StripTags(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        var sb = new StringBuilder(s.Length);
        bool tag = false;
        foreach (char c in s)
        {
            if (c == '<') { tag = true; continue; }
            if (c == '>') { tag = false; continue; }
            if (!tag) sb.Append(c);
        }
        return sb.ToString();
    }

    void ShowStatus(string text, bool error)
    {
        status.Text = text;
        status.AddThemeColorOverride(FontColor, error ? ErrorColor : MutedColor);
    }

    // ---- the debug endpoint's view ------------------------------------------------------------------------------------

    public void SetSearch(string text) { search.Text = text ?? ""; Refilter(true); }

    /// <summary>The amount box's value, set by the endpoint for a scripted click (1..99999; the command clamps a stack).</summary>
    public void SetAmount(int amount) => amountBox.Value = Math.Clamp(amount, 1, 99999);

    /// <summary>Hovers the first visible cell, as a pointer over it does (details, tooltip; never the amount box).</summary>
    public void HoverFirst()
    {
        if (!Visible) Open();
        if (FirstVisible() is { } c) Hover(c);
    }

    /// <summary>Clicks the first visible cell: the same amount, rarity and shift path a real click takes.</summary>
    public string ClickFirst()
    {
        if (!Visible && !Open()) return null;
        if (FirstVisible() is not { } c) return null;
        Click(c);
        return lastLine;
    }

    Cell FirstVisible()
    {
        foreach (var c in cells) if (c.Button.Visible && c.Entry >= 0) return c;
        return null;
    }

    public void SetTab(int t) { tab = t; tabBox.Select(tabBox.GetItemIndex(t)); Refilter(true); }

    /// <summary>The endpoint's spawn by id, through the catalog's entry (so the slot max and rank are the item's own).</summary>
    public string SpawnId(int id, int amount, int rarity, bool fullStack)
    {
        if (catalog == null && !Open()) return null;
        for (int i = 0; i < catalog.Count; i++)
        {
            var e = catalog.Entries[i];
            if (e.Id != id) continue;
            if (fullStack) amount = Math.Max(1, e.SlotMax);
            return Spawn(e, amount, rarity);
        }
        ShowStatus($"no item {id} in the catalog", error: true);
        return null;
    }

    public System.Text.Json.Nodes.JsonObject StatusJson()
    {
        var o = new System.Text.Json.Nodes.JsonObject
        {
            ["open"] = Visible, ["count"] = catalog?.Count ?? 0, ["shown"] = matches.Count, ["firstRow"] = firstRow, ["amount"] = (int)amountBox.Value,
            ["tab"] = tab, ["search"] = search.Text, ["cells"] = cells.Count, ["icons"] = icons.Count, ["iconLoads"] = iconLoads,
            ["scrollEvents"] = scrollEvents, ["scrollBytes"] = scrollBytes, ["catalogMs"] = catalogMs,
            ["lastLine"] = lastLine, ["lastAnswer"] = lastAnswer, ["unfiltered"] = catalog?.Unfiltered ?? 0,
            ["removed"] = catalog == null ? 0 : catalog.Removed.Count, ["removedSummary"] = catalog?.RemovedSummary() ?? "",
        };
        return o;
    }
}
#endif
