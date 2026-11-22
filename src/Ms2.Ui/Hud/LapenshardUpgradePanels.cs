using System.Globalization;
using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.Items;
using Ms2.Ui.Shop;

namespace Ms2.Ui.Hud;

/// <summary>
/// The upgrade tab's three panels (uilapenshardsystemdialog.cpp), fed by the tab's vfunc_2 through <see cref="PanelEvent"/>:
/// LapenshardSourceItemSlotBagPanel (the lapenshards that can be upgraded, bag "LapenshardSourceItemSlotBag"),
/// LapenshardIngredientItemSlotBagPanel (the fodder of the source's group, bag "LapenshardIngredientItemSlotBag") and
/// LapenshardUpgradeSlotPanel (the body frames, the source, result and ingredient slots, the success rate and the result).
/// <para>STILL-OPEN: the names' auto size mode 2 (SetTextHTMLW); the debug key's OnFillIngredientDebug and m_fillIngredientDebug; the money icon's feature variant
/// (FUN_14036d6d0); BindToCharacter of a worn lapenshard whose transfer type is 2 (FUN_140c60920); the item sounds on select;
/// FUN_1422dd1d0 and vfunc_24(true) of the tile lists; the equip page's drag is not wired here.</para>
/// </summary>
public sealed partial class LapenshardUpgradeTabPage
{
    public const string SourceBag = "LapenshardSourceItemSlotBag", IngredientBag = "LapenshardIngredientItemSlotBag",
        SlotBag = "lapenshardUpgradeIngredientSlotBag";
    public const string SourceSlot = "lapenshardUpgradeSourceSlot", ResultSlot = "lapenshardUpgradeResultSlot",
        SuccessSlot = "lapenshardUpgradeSuccessSlotID", FailSlot = "lapenshardUpgradeFailSlotID";
    /// <summary>LapenshardSlot::vfunc_8 (lapenshardslot.cpp:348) for a bag item of kind 0: 0x49 | 0x24 | 0x400, less the 0x20 that
    /// only acts on equipment (FUN_14053cbc0 never passes for a lapenshard).</summary>
    public const int BagSlotFlags = 0x44d;
    /// <summary>The info slots' icon type and flags (UpdateSourceAndResultSlots) and the slot bag's (FUN_140c65540).</summary>
    public const int InfoSlotStyle = 0x40401, InfoSlotFlags = 0x2020, SlotBagStyle = 0x20403;

    readonly List<Item> sourceItems = new(), ingredientItems = new(), slotItems = new();
    readonly List<int> slotKinds = new();
    readonly Dictionary<string, ItemInfoSlot> infoSlots = new();
    readonly HashSet<string> pushed = new();
    /// <summary>The slot panel's own m_spSource (copied from the tab by its vfunc_3).</summary>
    Item panelSource;
    object body;
    string ingredientBagName = "";

    public IReadOnlyList<Item> SourceItems => sourceItems;
    public IReadOnlyList<Item> IngredientItems => ingredientItems;
    public IReadOnlyList<Item> SlotItems => slotItems;

    public LapenshardUpgradeTabPage() { PanelEvent += OnPanelEvent; }

    void OnPanelEvent(int ev)
    {
        // source panel vfunc_5
        if (ev is 2 or 8 or 0xc && State != 3) BuildSourceBag();
        // ingredient panel vfunc_5
        if (ev is 2 or 3 or 8 or 9 or 0xc) { if (State != 3) BuildIngredientBag(); }
        else if (ev is 6 or 7) ClearIngredientBag();
        // upgrade slot panel vfunc_5
        SlotPanelEvent(ev);
    }

    static string Idx(string bag, int i) => $"{bag}.{i}";

    static bool ParseIdx(string name, string bag, out int index)
    {
        index = -1;
        var prefix = bag + ".";
        return name != null && name.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    /// <summary>FUN_140528e20 (item.cpp): locked (LockState 1), or a timed lock (0) not yet expired.</summary>
    bool IsBound(Item i) => i.LockState == 1 || (i.LockState == 0 && Now() < i.UnlockTime);

    long Now() => new DateTimeOffset(Owner.Model.Now).ToUnixTimeSeconds();

    /// <summary>FUN_140c60920 (@0x140c60920): the lapenshards of category 0xd (inventory first, then the worn ones when
    /// <paramref name="withEquipped"/>), filtered in the client's order; an equipped item is added as is, any other a copy.</summary>
    List<Item> Collect(bool checkUse, bool withBound, bool withEquipped, bool tableOnly, int kind, long excludeUid)
    {
        var res = new List<Item>();
        var m = Owner?.Model;
        if (m == null || !m.HasLocalPlayer) return res;
        var table = m.Tables.Lapenshards;
        var all = new List<Item>(Owner.InventoryItems(0xd));
        if (withEquipped)
        {
            var seen = new HashSet<int>();
            for (int s = 1; s <= LapenshardEquipTabPage.Slots; s++)
            {
                int id = Owner.WornItemId(s);
                if (id == 0 || !seen.Add(id)) continue;
                if (tableOnly && !table.HasUpgrade(id)) continue;
                var w = Item.Create(m.Tables.Items, id, ItemTooltip.ItemRank(m.Tables.Items, id));
                if (w == null) continue;
                w.IsEquipped = true;                  // InitWithCount(id, grade, 0, 1) through the worn list
                all.Add(w);                           // STILL-OPEN: BindToCharacter for transfer type 2
            }
        }
        foreach (var it in all)
        {
            if (it?.Data == null) continue;
            if (checkUse)
            {
                if (!Owner.CanUseItem(it) || !it.Data.IsJobAllowed(m.Player.JobGroup) || !it.Data.IsLevelAllowed(m.Player.Level)) continue;
            }
            if (!it.Data.IsLapenshard) continue;
            if (!withBound && IsBound(it)) continue;
            if (tableOnly && !table.HasUpgrade(it.ItemId)) continue;
            if (kind != 0 && table.Group(it.ItemId) != kind) continue;
            if (excludeUid != 0 && it.Uid == excludeUid) continue;
            res.Add(it.IsEquipped ? it : it.Clone());
        }
        return res;
    }

    void PushBag(string bag, int count)
    {
        for (int i = 0; i < count; i++) { var n = Idx(bag, i); pushed.Add(n); host.Push(n); }
    }

    void DropBag(string bag)
    {
        var drop = pushed.Where(n => n.StartsWith(bag + ".", StringComparison.Ordinal)).ToList();
        foreach (var n in drop) pushed.Remove(n);
    }

    // ---- LapenshardSourceItemSlotBagPanel ---------------------------------------------------------------------

    /// <summary>vfunc_3: the bag's list (FUN_140c60920 with no use check, bound and worn ones, table rows only), the tile list's
    /// bagId and its slots.</summary>
    void BuildSourceBag()
    {
        if (host == null || root == null || Owner?.Model == null || !Owner.Model.HasLocalPlayer) return;
        var bag = host.Get(root, "m_sourceItemBag");
        if (bag == null) return;
        host.Invoke(bag, "resetRenderer");
        host.Set(bag, "selectedIndex", -1);
        DropBag(SourceBag);
        sourceItems.Clear();
        sourceItems.AddRange(Collect(false, true, true, true, 0, 0));
        host.Set(bag, "bagId", SourceBag);
        PushBag(SourceBag, sourceItems.Count);
    }

    /// <summary>SelectSourceItem (@0x140c63bb0).</summary>
    void SelectSourceItem(int index)
    {
        if (index < 0 || index >= sourceItems.Count) return;
        SendMode3(sourceItems[index]);
    }

    // ---- LapenshardIngredientItemSlotBagPanel -----------------------------------------------------------------

    /// <summary>vfunc_4 (@0x140c64160): the bag and its list go, the chosen ingredients clear, then event 0xd.</summary>
    void ClearIngredientBag()
    {
        if (ingredientBagName.Length > 0) { DropBag(ingredientBagName); ingredientBagName = ""; }
        ingredientItems.Clear();
        ClearIngredients();
        Notify(0xd);
    }

    /// <summary>vfunc_3 (@0x140c63d40).</summary>
    void BuildIngredientBag()
    {
        if (host == null || root == null) return;
        ClearIngredientBag();
        if (Source == null) return;
        var row = Owner.Model.Tables.Lapenshards.Get(Source.ItemId);
        if (row == null || row.MinCount <= 0) return;
        ingredientBagName = IngredientBag;
        var bag = host.Get(root, "m_ingredientItemBag");
        if (bag == null) return;
        host.Invoke(bag, "resetRenderer");
        host.Set(bag, "selectedIndex", -1);
        ingredientItems.AddRange(Collect(false, false, false, false, row.Group, Source.Uid));
        var copy = Source.Clone();
        if (!IsBound(copy))
        {
            uint left = unchecked(copy.Count - 1);
            if (left > 0)
            {
                copy.SetCountClamped(unchecked((int)left));
                ingredientItems.Add(copy);
            }
        }
        host.Set(bag, "bagId", ingredientBagName);
        PushBag(IngredientBag, ingredientItems.Count);
    }

    /// <summary>SelectIngredientItem (@0x140c64390).</summary>
    void SelectIngredientItem(int index)
    {
        if (index < 0 || index >= ingredientItems.Count || Source == null) return;
        var table = Owner.Model.Tables.Lapenshards;
        var srcRow = table.Get(Source.ItemId);
        if (srcRow == null) return;
        var item = ingredientItems[index];
        var itemRow = table.Get(item.ItemId);
        if (itemRow == null) return;
        if (item.HasFlags(2))
        {
            ToggleIngredient(index, GetIngredientCount(item.Uid));
            return;
        }
        int need = Math.Max(0, srcRow.MinCount - IngredientTotal);
        if (need == 0) return;
        if (itemRow.Level < 2)
        {
            ToggleIngredient(index, -(int)Math.Min(item.Count, (uint)need));
            return;
        }
        int srcId = Source.ItemId;
        Owner.ShowBox(MessageBoxQueue.CreateWithCallback(Owner.Model.Tables.Common("s_lapenshard_upgraded_item"), 1,
            (button, _) => { if (button == 0) UpgradedItemConfirmed(index, srcId, need); return true; }));
    }

    /// <summary>The message box's callback (FUN_140c64bc0, result 0): the count of the item at the index, up to the need.</summary>
    void UpgradedItemConfirmed(int index, int sourceId, int need)
    {
        if (index < 0 || index >= ingredientItems.Count) return;
        ToggleIngredient(index, -(int)Math.Min(ingredientItems[index].Count, (uint)need));
    }

    /// <summary>FUN_140c64960 (@0x140c64960): the tab's count moves by -n, the item's by +n, the selected mark toggles, the
    /// slot refreshes and the movie's toggleIngredientSelectItem runs.</summary>
    void ToggleIngredient(int index, int n)
    {
        var item = ingredientItems[index];
        ChangeIngredientCount(item.Uid, -n);
        item.SetCountClamped((int)item.Count + n);
        item.Flags ^= 2;
        host.Push(Idx(IngredientBag, index));
        host.Invoke(root, "toggleIngredientSelectItem", index);
    }

    // ---- LapenshardUpgradeSlotPanel ---------------------------------------------------------------------------

    /// <summary>The slot panel's events (vfunc_5).</summary>
    void SlotPanelEvent(int ev)
    {
        if (host == null || root == null) return;
        switch (ev)
        {
            case 3 or 8: BuildSlotPanel(); break;
            case 2: if (State is 0 or 1) BuildSlotPanel(); break;
            case 5:
                host.Invoke(root, "setSuccessProb", ClientFormat.Format(Owner.Model.Tables.Common("s_lapenshard_tabname_upgrade_success_prob"), SuccessRate / 100f));
                break;
            case 4 or 0xd: UpdateIngredientCount(); break;
            case 6: ShowUpgradeResult(true); break;
            case 7: ShowUpgradeResult(false); break;
            case 9: UpdateIngredientData(); break;
        }
    }

    /// <summary>vfunc_3: the body, the source from the tab, then its frame.</summary>
    void BuildSlotPanel()
    {
        if (host == null || Owner?.Model == null || !Owner.Model.HasLocalPlayer) return;
        body = host.Get(root, "m_body");
        if (body == null) return;
        panelSource = Source;
        if (State == 0) host.Invoke(body, "gotoAndStop", "start");
        else if (State == 1 && panelSource != null)
        {
            UpdateSaleRate();
            SelectFrame();
        }
    }

    /// <summary>FUN_140c671d0: the "select" frame, the source and result slots and the ingredient list.</summary>
    void SelectFrame()
    {
        host.Invoke(body, "gotoAndStop", "select");
        UpdateSourceAndResultSlots();
        var row = Owner.Model.Tables.Lapenshards.Get(panelSource.ItemId);
        if (row == null || row.Next == 0) return;
        var data = host.Obj(Array.Empty<(string, object)>());
        BuildIngredientList(row, data);
        host.Invoke(root, "selectItem", data);
    }

    /// <summary>The coloured name of a grade: FUN_14183e9c0.</summary>
    string RankColored(int itemId, int rank) =>
        $"<FONT color='#{Owner.Model.Tables.Items.TextColor(rank).ToString("x", CultureInfo.InvariantCulture)}'>{Owner.Model.Tables.ItemName(itemId)}</FONT>";

    int Rank(int id) => ItemTooltip.ItemRank(Owner.Model.Tables.Items, id);

    void SetInfoSlot(string name, Item item)
    {
        infoSlots[name] = new ItemInfoSlot(name, item, 0, InfoSlotStyle, InfoSlotFlags);
        pushed.Add(name);
    }

    /// <summary>UpdateSourceAndResultSlots: m_sourceSlot and m_resultSlot, and the two names.</summary>
    void UpdateSourceAndResultSlots()
    {
        var m = Owner.Model;
        var table = m.Tables.Lapenshards;
        Item src = null, res = null;
        if (panelSource != null)
        {
            src = Item.Create(m.Tables.Items, panelSource.ItemId, Rank(panelSource.ItemId));
            if (src != null) { src.Count = 0; src.Uid = panelSource.Uid; src.IsEquipped = src.Uid == 0; }
            if (table.Get(panelSource.ItemId) is { Next: not 0 } row) res = Item.Create(m.Tables.Items, row.Next, Rank(row.Next));
        }
        if (res != null) res.Count = 0;
        SetInfoSlot(SourceSlot, src);
        SetInfoSlot(ResultSlot, res);
        foreach (var (child, name) in new[] { ("m_sourceSlot", SourceSlot), ("m_resultSlot", ResultSlot) })
            if (host.Get(root, child) is { } c) host.Invoke(c, "setData", host.Obj(("bindID", name)));
        host.Push(SourceSlot);
        host.Push(ResultSlot);
        SetName("m_sourceItemName", src);
        SetName("m_resultItemName", res);
    }

    void SetName(string child, Item item)
    {
        if (host.Get(root, child) is not { } c || host.Get(c, "textField") is not { } tf) return;
        host.Set(tf, "htmlText", item == null ? "" : RankColored(item.ItemId, item.Rarity));
    }

    /// <summary>FUN_140c65540 (@0x140c65540): the item list (the group's first item for the minimum count, then the sale-rate
    /// scaled ingredients), its bag, and the rows out.ingredient, requireMeso.</summary>
    void BuildIngredientList(LapenshardTable.Row row, object data)
    {
        var m = Owner.Model;
        slotItems.Clear();
        slotKinds.Clear();
        if (row.MinCount > 0)
        {
            int id = m.Tables.Lapenshards.FirstItem(row.Group);
            var it = Item.Create(m.Tables.Items, id, Rank(id));
            if (it != null) { it.Count = (uint)row.MinCount; slotItems.Add(it); slotKinds.Add(0); }
        }
        foreach (var ing in LapenshardTable.Scaled(row, SaleRateRead))
        {
            if (ing == null || ing.ItemId == 0) continue;
            var it = Item.Create(m.Tables.Items, ing.ItemId, Rank(ing.ItemId));
            if (it == null) continue;
            it.Count = (uint)ing.Count;
            if (SaleRateRead != 0 && ing.Tag is 0x5d or 0x5e or 0x5f) it.IsEvent = true;
            slotItems.Add(it);
            slotKinds.Add(ing.Tag);
        }
        DropBag(SlotBag);
        for (int i = 0; i < slotItems.Count; i++) pushed.Add(Idx(SlotBag, i));
        host.Set(data, "ingredient", IngredientRows());
        if (row.Meso > 0) host.Set(data, "requireMeso", MoneyText(row.Meso));
    }

    List<object> IngredientRows()
    {
        var rows = new List<object>();
        for (int i = 0; i < slotItems.Count; i++)
        {
            var it = slotItems[i];
            int have = it.Data.IsLapenshard ? 0 : HaveOf(it, slotKinds[i]);
            rows.Add(host.Obj(("binding", Idx(SlotBag, i)), ("countText", GetIngredientCountText(have, (int)it.Count))));
        }
        return rows;
    }

    /// <summary>FUN_140c69670: the tag's count, else the item's at its grade.</summary>
    int HaveOf(Item it, int tag) => tag != 0 ? CountByTag(tag) : CountItems(it.ItemId, it.Rarity);

    /// <summary>GetIngredientCountText: s_common_cur_max_ingredient(_lack).</summary>
    string GetIngredientCountText(int have, int need) =>
        ClientFormat.Format(Owner.Model.Tables.Common(have < need ? "s_common_cur_max_ingredient_lack" : "s_common_cur_max_ingredient"), have, need);

    string MoneyText(long meso)
    {
        var (icon, vspace) = ShopController.CurrencyIcon(1, 0);
        return icon.Length > 0 ? ClientFormat.Format(Owner.Model.Tables.Common("s_common_payment"), meso.ToString("N0", CultureInfo.InvariantCulture), icon, vspace) : "";
    }

    /// <summary>UpdateIngredientCount: the lapenshard line of the list, in the select state.</summary>
    void UpdateIngredientCount()
    {
        if (panelSource == null || State is not (0 or 1) || slotItems.Count == 0) return;
        var row = Owner.Model.Tables.Lapenshards.Get(panelSource.ItemId);
        if (row == null || row.MinCount <= 0) return;
        host.Invoke(root, "setIngredientLapenshardCount", GetIngredientCountText(IngredientTotal, row.MinCount));
    }

    /// <summary>UpdateIngredientData: the rows again, then the count.</summary>
    void UpdateIngredientData()
    {
        if (slotItems.Count == 0 || slotKinds.Count == 0) return;
        host.Invoke(root, "setIngredientData", host.Obj(("ingredient", IngredientRows())));
        UpdateIngredientCount();
    }

    /// <summary>ShowUpgradeResult: the "result" frame and the movie's upgradeComplete, then the source is dropped.</summary>
    void ShowUpgradeResult(bool success)
    {
        if (body == null || panelSource == null) { return; }
        host.Invoke(body, "gotoAndStop", "result");
        var m = Owner.Model;
        var row = m.Tables.Lapenshards.Get(panelSource.ItemId);
        if (row == null || row.Next == 0) return;
        var members = new List<(string, object)> { ("success", success) };
        var next = Item.Create(m.Tables.Items, row.Next, Rank(row.Next));
        if (next != null)
        {
            SetInfoSlot(SuccessSlot, next);
            members.Add(("successSlotBindID", SuccessSlot));
            members.Add(("successItemName", RankColored(next.ItemId, next.Rarity)));
        }
        members.Add(("limitLevel", success && (m.Tables.Lapenshards.Get(row.Next)?.Next ?? 0) == 0 ? 1 : 0));
        var src = Item.Create(m.Tables.Items, panelSource.ItemId, Rank(panelSource.ItemId));
        if (src != null)
        {
            SetInfoSlot(FailSlot, src);
            members.Add(("failSlotBindID", FailSlot));
            members.Add(("failItemName", RankColored(src.ItemId, src.Rarity)));
        }
        host.Push(SuccessSlot);
        host.Push(FailSlot);
        host.Invoke(root, "upgradeComplete", host.Obj(members.ToArray()));
        panelSource = null;
        ClearSource();
    }

    // ---- bindings ---------------------------------------------------------------------------------------------

    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == SourceBag ? Enumerable.Range(0, sourceItems.Count).Select(i => Idx(SourceBag, i))
        : bag == IngredientBag ? Enumerable.Range(0, ingredientItems.Count).Select(i => Idx(IngredientBag, i))
        : bag == SlotBag ? Enumerable.Range(0, slotItems.Count).Select(i => Idx(SlotBag, i)) : Array.Empty<string>();

    /// <summary>The item a name binds, and the icon type its slot is built with.</summary>
    Item Resolve(string name, out int style, out int tipFlags)
    {
        style = BagSlotFlags; tipFlags = 0x20;
        if (ParseIdx(name, SourceBag, out int i)) return i < sourceItems.Count ? sourceItems[i] : null;
        if (ParseIdx(name, IngredientBag, out i)) return i < ingredientItems.Count ? ingredientItems[i] : null;
        if (ParseIdx(name, SlotBag, out i)) { style = SlotBagStyle; tipFlags = InfoSlotFlags; return i < slotItems.Count ? slotItems[i] : null; }
        if (infoSlots.TryGetValue(name, out var info)) { style = info.IconType; tipFlags = info.Flags; return info.Item; }
        return null;
    }

    public override object GetBinding(string name)
    {
        if (Owner?.Model == null) return null;
        var item = Resolve(name, out int style, out _);
        if (item == null) return null;
        var m = Owner.Model;
        var slot = new List<(string, object)>();
        if (!ItemSlot.Build(slot, item, m.HasLocalPlayer, style, m, m.ItemSlotSources)) return null;
        bool bag = style == BagSlotFlags;
        if (bag)
        {
            // LapenshardSlot::vfunc_8 (lapenshardslot.cpp:348): the count, disable, seal and equip marks of a bag item
            slot.RemoveAll(p => p.Item1 == "itemCount");
            slot.Add(("itemCount", item.Count));
            if (item.Count == 0) slot.Add(("disable", 1));
            if (item.HasFlags(2)) slot.Add(("itemSeal", 1));
            if (item.IsEquipped) slot.Add(("itemEquip", 1));
        }
        else if (infoSlots.ContainsKey(name) && item.IsEquipped) slot.Add(("itemEquip", 1));      // SlotFn: uid 0 is SetItemEquip(1)
        return host?.Obj(slot.ToArray());
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (Owner?.Model == null) return null;
        var item = Resolve(name, out _, out int flags);
        if (item == null) return ParseIdx(name, SourceBag, out _) || ParseIdx(name, IngredientBag, out _) || ParseIdx(name, SlotBag, out _) || infoSlots.ContainsKey(name) ? new AsTooltip() : null;
        var tip = new ItemTooltip(Owner.Model, flags) { Localize = k => host?.Player.Translate?.Invoke(k) ?? k };
        return tip.Fill(item) ? tip.Data : new AsTooltip();
    }

    /// <summary>The panels' vfunc_8 callbacks (the tab's vfunc_6 runs them before its own, ignoring what they return).</summary>
    void PanelCallback(string name, object[] args)
    {
        if (name.Equals("OnSelectSourceItem", StringComparison.OrdinalIgnoreCase) && State <= 1 && args is { Length: > 0 })
            SelectSourceItem((int)Rt.ToInt32(args[0]));
        else if (name.Equals("OnSelectIngredientItem", StringComparison.OrdinalIgnoreCase) && State == 1 && args is { Length: > 0 })
            SelectIngredientItem((int)Rt.ToInt32(args[0]));
    }
}
