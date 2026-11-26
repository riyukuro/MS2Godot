#if MS2_DEVTOOLS
using System.Collections.Generic;
using Godot;
using Ms2.Devtools;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using Ms2Godot.Ui;

namespace Ms2Godot;

/// <summary>
/// The item browser's enhancement panel and the real tooltip. The fields mirror what the server's item command can set
/// (enchant, limit break, sockets with gems, bonus stat/special lines), their limits come from the client's tables
/// (<see cref="ItemBrowserLimits"/>), and the hover shows the client's own item tooltip movie through
/// <see cref="UiHost.Tooltips"/> with the data <see cref="ItemTooltip"/> builds for the settings (the bag's flags 0x80).
/// The panel's options are enabled only where the live server's "/item --help" reply names them
/// (<see cref="ItemCommandSupport"/>); offline, or before the reply, the KMS fork's set is offered.
/// </summary>
public partial class CreativeMenu
{
    readonly ItemBrowserSettings settings = new();
    ItemCommandSupport support = ItemCommandSupport.Fork;
    ItemCommandSupport serverSupport;                  // the live server's reply; null until it comes
    readonly System.Text.StringBuilder helpText = new();
    bool awaitingHelp;

    SpinBox enchantBox, limitBox, socketBox;
    readonly LineEdit[] gemEdits = new LineEdit[ItemBrowserLimits.MaxSockets];
    Label gemNote;
    VBoxContainer bonusRows;
    Button addStat, addSpecial;
    Label enhanceNote, rangeNote;
    readonly List<OptionButton> bonusStatBoxes = new();
    readonly List<SpinBox> bonusValueBoxes = new();
    readonly List<(OptionButton Stat, SpinBox Value)> bonus = new();

    int tooltipId;
    Cell hoveredCell;
    int tooltipLayerLayer = -1;
    /// <summary>The tooltip builds since the window opened (the hover cost measurement: no per-frame rebuilds).</summary>
    int tooltipBuilds;
    /// <summary>The managed bytes the last build allocated on this thread (one build per hover; 0 per frame).</summary>
    long tooltipBuildBytes;

    /// <summary>A stable id per shown tooltip, so a rebuild destroys the old one.</summary>
    int NextTooltipId() => ++tooltipId;

    void BuildEnhance(VBoxContainer box)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        box.AddChild(row);
        row.AddChild(new Label { Text = "Enchant" });
        enchantBox = new SpinBox { MinValue = 0, MaxValue = ItemBrowserLimits.MaxEnchant, Step = 1, CustomMinimumSize = new Vector2(70, 0) };
        enchantBox.ValueChanged += _ => { settings.Enchant = (int)enchantBox.Value; SettingsChanged(); };
        row.AddChild(enchantBox);
        row.AddChild(new Label { Text = "Limit break" });
        limitBox = new SpinBox { MinValue = 0, MaxValue = ItemBrowserLimits.MaxLimitBreak, Step = 1, CustomMinimumSize = new Vector2(70, 0) };
        limitBox.ValueChanged += _ => { settings.LimitBreak = (int)limitBox.Value; SettingsChanged(); };
        row.AddChild(limitBox);
        row.AddChild(new Label { Text = "Sockets" });
        socketBox = new SpinBox { MinValue = 0, MaxValue = ItemBrowserLimits.MaxSockets, Step = 1, CustomMinimumSize = new Vector2(70, 0) };
        socketBox.ValueChanged += _ => { settings.Sockets = (int)socketBox.Value; SettingsChanged(); };
        row.AddChild(socketBox);

        var gemRow = new HBoxContainer();
        gemRow.AddThemeConstantOverride("separation", 6);
        box.AddChild(gemRow);
        gemRow.AddChild(new Label { Text = "Gem ids" });
        for (int i = 0; i < gemEdits.Length; i++)
        {
            int at = i;
            var gem = new LineEdit { PlaceholderText = "none", CustomMinimumSize = new Vector2(84, 0), TooltipText = $"socket {i + 1}'s gem item id" };
            gem.TextChanged += text => { SetGem(at, int.TryParse(text, out int id) ? id : 0); };
            gemEdits[i] = gem;
            gemRow.AddChild(gem);
        }
        gemNote = new Label { Text = "", ClipText = true, CustomMinimumSize = new Vector2(0, 16) };
        gemNote.AddThemeFontSizeOverride("font_size", 12);
        gemNote.AddThemeColorOverride(FontColor, MutedColor);
        box.AddChild(gemNote);

        var bonusBox = new ScrollContainer { CustomMinimumSize = new Vector2(0, 62), VerticalScrollMode = ScrollContainer.ScrollMode.Auto, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(bonusBox);
        bonusRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bonusBox.AddChild(bonusRows);
        var buttons = new HBoxContainer();
        box.AddChild(buttons);
        addStat = new Button { Text = "Add stat" };
        addStat.Pressed += () => AddBonus(special: false);
        buttons.AddChild(addStat);
        addSpecial = new Button { Text = "Add special" };
        addSpecial.Pressed += () => AddBonus(special: true);
        buttons.AddChild(addSpecial);
        rangeNote = new Label { Text = "", ClipText = true, CustomMinimumSize = new Vector2(0, 18) };
        rangeNote.AddThemeFontSizeOverride("font_size", 12);
        rangeNote.AddThemeColorOverride(FontColor, MutedColor);
        buttons.AddChild(rangeNote);
        enhanceNote = new Label { Text = "", ClipText = true, CustomMinimumSize = new Vector2(0, 18) };
        enhanceNote.AddThemeFontSizeOverride("font_size", 12);
        enhanceNote.AddThemeColorOverride(FontColor, MutedColor);
        box.AddChild(enhanceNote);
    }

    /// <summary>The gem names of the ids written (CItemData type 0xd, the client's IsGemstone), so a hand-typed id is
    /// checked before the server sees it.</summary>
    void UpdateGemNote()
    {
        var names = new List<string>();
        for (int i = 0; i < settings.Gems.Length; i++)
        {
            int id = settings.Gems[i];
            if (id == 0) continue;
            var d = tables.Items.Get(id);
            names.Add(d is { IsGemstone: true } ? $"{i + 1}: {tables.ItemName(id)}" : $"{i + 1}: {id} is not a gem");
        }
        gemNote.Text = names.Count == 0 ? "" : string.Join(", ", names);
    }

    /// <summary>A bonus row: a stat or special from the client's tables (the option names, with the item's normal range),
    /// a value, and a remove button. The row's selection reports the range this item can roll normally for the stat.</summary>
    void AddBonus(bool special)
    {
        var row = new HBoxContainer();
        bonusRows.AddChild(row);
        var stat = new OptionButton { CustomMinimumSize = new Vector2(300, 0), ClipText = true };
        FillStatBox(stat, special);
        row.AddChild(stat);
        var value = new SpinBox { MinValue = 0, MaxValue = 1_000_000, Step = 1, CustomMinimumSize = new Vector2(100, 0) };
        row.AddChild(value);
        var remove = new Button { Text = "x", CustomMinimumSize = new Vector2(28, 0) };
        row.AddChild(remove);
        var entry = (stat, value);
        bonus.Add(entry);
        stat.ItemSelected += _ => { StoreBonus(entry); ShowRange(stat); };
        value.ValueChanged += _ => StoreBonus(entry);
        remove.Pressed += () =>
        {
            bonus.Remove(entry);
            row.QueueFree();
            StoreAllBonuses();
        };
        StoreBonus(entry);
        ShowRange(stat);
        SettingsChanged();
    }

    void FillStatBox(OptionButton box, bool special)
    {
        if (box.ItemCount > 0) return;
        if (special)
        {
            for (int id = 1; id < ItemBrowserLimits.SpecialCount; id++)
            {
                string name = tables.SpecialAbilityName(id);
                if (name.Length > 0) box.AddItem($"{name} (special {id})", 0x10000 | id);
            }
        }
        else
        {
            for (int id = 0; id < ItemBrowserLimits.ClientStatCount; id++)
            {
                string name = tables.StatName(id);
                if (name.Length > 0) box.AddItem($"{name} (stat {id})", id);
            }
        }
    }

    /// <summary>The selected stat's normal range at the hovered item's rarity (GetRandomOptionRange); (0, 0) when the item
    /// cannot roll it.</summary>
    void ShowRange(OptionButton box)
    {
        int itemId = hoveredCell != null && hoveredCell.Entry >= 0 ? catalog.Entries[hoveredCell.Entry].Id : 0;
        int id = (int)box.GetItemId(box.Selected);
        if ((id & 0x10000) != 0 || itemId == 0 || tables.Items.Get(itemId) is not { } d)
        {
            rangeNote.Text = "special lines have no item range";
            return;
        }
        var range = ItemBrowserLimits.StatRange(tables, d, EffectiveRarity(d), id);
        rangeNote.Text = range == (0, 0)
            ? "this item cannot roll it normally"
            : $"this item rolls it {range.First}..{range.Last} {tables.ItemOptions.StatValueText(id)}".TrimEnd();
    }

    int EffectiveRarity(ItemData d) => ItemBrowserModel.EffectiveRarity(d, settings.Rarity);

    void StoreBonus((OptionButton Stat, SpinBox Value) row)
    {
        int id = (int)row.Stat.GetItemId(row.Stat.Selected);
        bool special = (id & 0x10000) != 0;
        id &= 0xffff;
        settings.BonusLines.RemoveAll(b => b.Special == special && b.Id == id);
        if (row.Value.Value > 0) settings.BonusLines.Add(new ItemBrowserSettings.Bonus(special, id, (int)row.Value.Value));
        SettingsChanged();
    }

    void StoreAllBonuses()
    {
        settings.BonusLines.Clear();
        foreach (var row in bonus) StoreBonus(row);
    }

    /// <summary>A field set by the debug endpoint (for a scripted run): enchant, limit, socket or rarity.</summary>
    public void SetField(string field, int value)
    {
        switch (field)
        {
            case "enchant": enchantBox.Value = Math.Clamp(value, 0, (int)enchantBox.MaxValue); settings.Enchant = (int)enchantBox.Value; break;
            case "limit": limitBox.Value = Math.Clamp(value, 0, (int)limitBox.MaxValue); settings.LimitBreak = (int)limitBox.Value; break;
            case "socket": socketBox.Value = Math.Clamp(value, 0, (int)socketBox.MaxValue); settings.Sockets = (int)socketBox.Value; break;
            case "rarity":
                int r = Math.Clamp(value, 0, ItemSpawnCommand.MaxRarity);
                rarityBox.Select(rarityBox.GetItemIndex(r));
                settings.Rarity = r;
                break;
            default: throw new System.ArgumentException("unknown enhancement field " + field);
        }
        SettingsChanged();
    }

    /// <summary>A gem of a socket set by the debug endpoint (-1 index: every open socket).</summary>
    public void SetGem(int index, int gemId)
    {
        for (int i = 0; i < gemEdits.Length; i++)
        {
            if (index >= 0 && i != index) continue;
            settings.Gems[i] = gemId;
            string text = gemId == 0 ? "" : gemId.ToString();
            if (gemEdits[i].Text != text) gemEdits[i].Text = text;
        }
        UpdateGemNote();
        SettingsChanged();
    }

    /// <summary>A bonus line added by the debug endpoint (a new row, selected).</summary>
    public void AddBonusLine(bool special, int id, int value)
    {
        AddBonus(special);
        var row = bonus[^1];
        int coded = special ? 0x10000 | id : id;
        int at = row.Stat.GetItemIndex(coded);
        if (at >= 0) row.Stat.Select(at);
        row.Value.Value = Math.Clamp(value, 0, 1_000_000);
        StoreBonus(row);
        ShowRange(row.Stat);
        SettingsChanged();
    }

    /// <summary>The settings changed: the hovered cell's tooltip is rebuilt (it is the item as it would spawn now).</summary>
    void SettingsChanged()
    {
        settings.Rarity = (int)rarityBox.GetItemId(rarityBox.Selected);
        if (hoveredCell != null && hoveredCell.Entry >= 0) ShowTooltip(hoveredCell);
    }

    /// <summary>
    /// The hover: the bottom line keeps only the id (the tooltip has the rest), the panel's limits follow the item, and the
    /// real tooltip is built from the settings. The tooltip's server-made blocks (the enchant and limit break options) come
    /// from the bag item the same settings already spawned, when there is one.
    /// </summary>
    void EnhanceHover(Cell c)
    {
        if (c.Entry < 0) return;
        var e = catalog.Entries[c.Entry];
        var d = tables.Items.Get(e.Id);
        hoveredCell = c;
        details.Text = $"id {ItemCatalog.IdText(e.Id)}";
        UpdateLimits(d);
        ShowTooltip(c);
    }

    void EnhanceLeave(Cell c)
    {
        if (hoveredCell == c) hoveredCell = null;
        HideTooltip();
    }

    /// <summary>
    /// The hovered item's limits in the note (the enchant's grade, the limit break's, the most sockets); the values are
    /// left alone, so a setting made for one item is not lost when the pointer crosses another (the amount box's lesson).
    /// The spawn checks them against the clicked item and says why when one does not fit.
    /// </summary>
    void UpdateLimits(ItemData d)
    {
        if (d == null) return;
        int enchantMax = support.Enchant ? ItemBrowserLimits.EnchantMax(tables, d) : 0;
        int limitMax = support.LimitBreak ? ItemBrowserLimits.LimitBreakMax(d) : 0;
        int socketMax = support.Socket ? ItemBrowserLimits.SocketMax(tables, model.IsFeatureEnabled, d, EffectiveRarity(d)) : 0;
        if (settings.LimitBreak > 0) enchantMax = 0;    // a limit-broken item has no enchant (ItemEnchantRules.CanEnchant)
        string ranges = $"this item: enchant 0..{enchantMax}, limit break 0..{limitMax}, sockets 0..{socketMax}";
        var reasons = new List<string>();
        if (!support.Enchant) reasons.Add("--enchant");
        else if (ItemBrowserLimits.EnchantMax(tables, d) == 0) reasons.Add("the item cannot be enchanted");
        if (!support.LimitBreak) reasons.Add("--limit-break");
        else if (ItemBrowserLimits.LimitBreakMax(d) == 0) reasons.Add("the item has no limit break");
        if (!support.Socket) reasons.Add("--socket/--gem");
        else if (ItemBrowserLimits.SocketMax(tables, model.IsFeatureEnabled, d, EffectiveRarity(d)) == 0) reasons.Add("the item has no sockets");
        if (!support.Stat) reasons.Add("--stat");
        if (!support.Special) reasons.Add("--special");
        enhanceNote.Text = reasons.Count == 0 ? ranges : ranges + "; not offered here: " + string.Join(", ", reasons);
        bool gems = support.Gem;
        for (int i = 0; i < gemEdits.Length; i++) gemEdits[i].Editable = gems;
    }

    /// <summary>The reason the settings cannot spawn the item, null when they fit: the enchant's and limit break's table
    /// maxima, the socket count, and the enchant/limit-break mutual exclusion (the server's own checks).</summary>
    public string ValidateFor(ItemData d)
    {
        if (d == null) return "no item";
        if (settings.Enchant > 0 && settings.LimitBreak > 0) return "an enchant and a limit break cannot be set together";
        if (settings.Enchant > 0 && !support.Enchant) return "this server has no --enchant";
        if (settings.LimitBreak > 0 && !support.LimitBreak) return "this server has no --limit-break";
        if (settings.Sockets > 0 && !support.Socket) return "this server has no --socket";
        if (settings.Gems.Any(g => g != 0) && !support.Gem) return "this server has no --gem";
        if (settings.BonusLines.Any(b => b.Special ? !support.Special : !support.Stat)) return "this server has no --stat/--special";
        if (settings.Enchant > 0)
        {
            int max = ItemBrowserLimits.EnchantMax(tables, d);
            if (settings.Enchant > max) return $"this item takes no enchant {settings.Enchant} (0..{max})";
        }
        if (settings.LimitBreak > 0)
        {
            int max = ItemBrowserLimits.LimitBreakMax(d);
            if (settings.LimitBreak > max) return $"this item takes no limit break {settings.LimitBreak} (0..{max})";
        }
        if (settings.Sockets > 0)
        {
            int max = ItemBrowserLimits.SocketMax(tables, model.IsFeatureEnabled, d, EffectiveRarity(d));
            if (settings.Sockets > max) return $"this item has no socket {settings.Sockets} (0..{max})";
        }
        return null;
    }

    /// <summary>The tooltip of the settings for a cell: the client's item tooltip data, placed at the cell's stage rect.
    /// The data is built once per hover or settings change, never per frame.</summary>
    void ShowTooltip(Cell c)
    {
        if (c.Entry < 0 || catalog == null) return;
        long before = GC.GetAllocatedBytesForCurrentThread();
        tooltipBuilds++;
        HideTooltip();
        var e = catalog.Entries[c.Entry];
        var live = Ms2Godot.Net.LiveSession.Instance;
        var server = live != null ? ItemBrowserModel.Spawned(live.Items, model, e.Id, settings) : null;
        if (ItemBrowserModel.Build(model, e.Id, settings, server) is not { } built) return;
        if (UiHost.Tooltips == null) return;
        var view = GetViewport();
        float u = UiScale.For(view.GetVisibleRect().Size);
        var rect = new Dictionary<string, object>
        {
            ["x"] = (c.Button.GlobalPosition.X - UiScale.MovieOrigin.X) / u,
            ["y"] = (c.Button.GlobalPosition.Y - UiScale.MovieOrigin.Y) / u,
            ["width"] = CellW / u,
            ["height"] = CellH / u,
        };
        UiHost.Tooltips.Call("createTooltip", new object[] { NextTooltipId(), built.Tooltip, rect });
        tooltipBuildBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    }

    void HideTooltip()
    {
        if (tooltipId == 0 || UiHost.Tooltips == null) return;
        UiHost.Tooltips.Call("destroyTooltip", new object[] { tooltipId });
        tooltipId = 0;
    }

    /// <summary>The tooltip layer sits above the window while it is open (CommonDepth.Tooltip 37 is over the dialogs in the
    /// client; the window is a native layer 16), and back where the HUD put it on close.</summary>
    void RaiseTooltipLayer()
    {
        var layer = TooltipLayer.Ensure(this);
        if (layer == null) return;
        tooltipLayerLayer = layer.Layer;
        layer.Layer = CanvasLayerIndex + 1;
    }

    void RestoreTooltipLayer()
    {
        HideTooltip();
        if (tooltipLayerLayer < 0) return;
        if (TooltipLayer.Instance is { } layer) layer.Layer = tooltipLayerLayer;
        tooltipLayerLayer = -1;
    }

    /// <summary>
    /// The options the live server's "/item --help" reply names: asked once on open, the reply's notices gathered and
    /// parsed. Until it comes (and offline) the fork's set is offered, and a field the server does not name is disabled
    /// with the reason.
    /// </summary>
    void AskServerSupport()
    {
        serverSupport = null;
        helpText.Clear();
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live?.GameSession == null) { ApplySupport(ItemCommandSupport.Fork); return; }
        awaitingHelp = true;
        helpText.Clear();
        live.SendChat(new Ms2.Net.Packets.UserChatRequest(0, "/item --help", "", 0));
        // The reply is a run of console notices (one per help line); wait for the whole run before parsing, as the first
        // lines are the usage alone.
        GetTree().CreateTimer(1.2).Timeout += FinishHelp;
    }

    /// <summary>One notice while the help is awaited: the reply comes as console notices, possibly several, and is parsed
    /// once <see cref="FinishHelp"/> has them all.</summary>
    bool TakeHelp(Ms2.Net.Packets.NoticeMessage n)
    {
        if (!awaitingHelp) return false;
        string text = n.Text.Formatted ? ClientFormat.Format(tables.Common(n.Text.Id), n.Text.Args) : n.Text.Text ?? "";
        helpText.Append(System.Net.WebUtility.HtmlDecode(StripTags(text))).Append('\n');
        return true;
    }

    void FinishHelp()
    {
        if (!awaitingHelp || !Visible) return;
        awaitingHelp = false;
        var parsed = ItemCommandSupport.Parse(helpText.ToString());
        serverSupport = parsed;
        ApplySupport(parsed);
        status.Text = parsed.Any ? "server: the item command takes the enhancement options" : "server: the item command takes no enhancement options";
    }

    void ApplySupport(ItemCommandSupport s)
    {
        support = s;
        enchantBox.Editable = s.Enchant;
        limitBox.Editable = s.LimitBreak;
        socketBox.Editable = s.Socket;
        addStat.Disabled = !s.Stat;
        addSpecial.Disabled = !s.Special;
        // A field the server does not take is dimmed as well as locked: its spin box refuses input and its maximum is 0
        // (UpdateLimits), and the reason line names the option.
        var dim = new Color(1, 1, 1, 0.45f);
        var on = Colors.White;
        enchantBox.Modulate = s.Enchant ? on : dim;
        limitBox.Modulate = s.LimitBreak ? on : dim;
        socketBox.Modulate = s.Socket ? on : dim;
        addStat.Modulate = s.Stat ? on : dim;
        addSpecial.Modulate = s.Special ? on : dim;
        foreach (var gem in gemEdits) gem.Modulate = s.Gem ? on : dim;
        if (hoveredCell != null && hoveredCell.Entry >= 0) UpdateLimits(tables.Items.Get(catalog.Entries[hoveredCell.Entry].Id));
    }
}
#endif
