using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The enchant scroll window, UIEnchantScrollDialog (client/ms2/uienchantscrolldialog.cpp, UI id 0x4ee0, type name
/// "EnchantScrollDialog", movie uienchantscrolldialog) with its one EnchantScrollSlot (enchantscrollslot.cpp), bound by the
/// movie's m_slot.enchantItem under the setData's bindID. The state is <see cref="EnchantScrollBook"/>; this controller
/// writes the setData objects (UpdateScrollInfo @0x140f3cb10 and FUN_140f3bd10), handles onEnchant and the slot's
/// NotifyDragSlot calls, and builds the result and refund boxes.
/// <para>STILL-OPEN: GMS2 item records keep each enchant option as a rate and a value (upstream ItemEnchant.WriteTo); the
/// GMS2 client's GetEnchantRatio is not decompiled, so here a GMS2 ratio is the rate plus value / 1000 (the KMS rule when
/// the rate is 0).</para>
/// </summary>
public sealed class EnchantScrollDialogController : UiController, IDragSlotOwner
{
    public override string Movie => "uienchantscrolldialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => EnchantScrollBook.TypeName;

    public const int UiId = EnchantScrollBook.UiId;
    /// <summary>The slot's binding name (GetTypeName, registered by FUN_140cd73b0).</summary>
    public const string Slot = EnchantScrollBook.TypeName;
    /// <summary>EnchantScrollSlot::vfunc_8: ASSlot(2) through FUN_1409c3860 with flags 1; vfunc_0: ASTooltip flags 0x20.</summary>
    public const int SlotFlags = 1, TooltipFlags = 0x20;
    /// <summary>FUN_140c3c6c0's box: type 0x11, text Common 0xb71, tag "enchant_ok", the button label Common 0x475.</summary>
    public const int ResultBoxType = 0x11, ResultText = 0xb71, ResultButton = 0x475;
    public const string ResultTag = "enchant_ok";

    public EnchantScrollBook Book;
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>The inventory item of a uid as the UI item (the slot, the tooltip and the result box); null when unknown.</summary>
    public Func<long, Item> FindItem = _ => null;
    /// <summary>Shift held (DragSlot::vfunc_13's check).</summary>
    public Func<bool> ShiftHeld = () => false;
    /// <summary>FUN_141275260(id, 1, 1): the notice text.</summary>
    public event Action<string> NoticeText;
    public event Action Closed;
    /// <summary>FUN_140f3c6c0's inventory refresh (FUN_141999e00 on 0x4e2d) of the enchanted item, before the box.</summary>
    public event Action<long> InventorySlotRefresh;
    /// <summary>CReservedWordTable::ReplaceValueWords on the scroll description.</summary>
    public Func<string, string> ReplaceValueWords = t => t;

    bool closing, bookClosed;

    string T(int id) => Model?.Tables.Common(id) ?? "";
    string T(string key) => Model?.Tables.Common(key) ?? "";

    // ---- load / close -----------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete (@0x140f3b650): UpdateScrollInfo.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Book == null) return;
        Book.IsOpen = true;
        Book.ScrollInfoChanged += ShowScrollInfo;
        Book.Previewed += ShowPreview;
        Book.Cleared += OnCleared;
        Book.Notice += OnNotice;
        Book.Succeeded += OnSucceeded;
        Book.CloseRequested += OnBookClosed;
        Book.SlotChanged += OnSlotChanged;
        Book.UpdateScrollInfo();
    }

    /// <summary>vfunc_67 (@0x140f3b5b0, :136): FUN_140f3d210, the slot unregistered, UIDialog's close.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        if (Book != null)
        {
            Book.ScrollInfoChanged -= ShowScrollInfo;
            Book.Previewed -= ShowPreview;
            Book.Cleared -= OnCleared;
            Book.Notice -= OnNotice;
            Book.Succeeded -= OnSucceeded;
            Book.CloseRequested -= OnBookClosed;
            Book.SlotChanged -= OnSlotChanged;
            Book.IsOpen = false;
            if (!bookClosed) Book.Close();
        }
        base.Close();
        Closed?.Invoke();
    }

    /// <summary>The book's own close (the result box's OK, code 1): its reset and unregister already ran.</summary>
    void OnBookClosed() { bookClosed = true; Close(); }

    void OnCleared() { if (Host?.Entity != null && !closing) Host.Invoke("clear"); }
    void OnSlotChanged() { if (Host?.Entity != null && !closing) Host.Push(Slot); }

    void OnNotice(int id)
    {
        string text = T(id);
        if (!string.IsNullOrEmpty(text)) NoticeText?.Invoke(text);
    }

    void OnSucceeded(long uid)
    {
        InventorySlotRefresh?.Invoke(uid);
        if (Model == null || FindItem(uid) is not { } item) return;
        ShowData(ResultBox(Model, item, () => Book?.Close()));
    }

    void ShowScrollInfo() { if (Host?.Entity != null && !closing && ScrollInfoData() is { } d) Host.Invoke("setData", Host.Obj(d.ToArray())); }
    void ShowPreview(EnchantScrollMessage.Preview p) { if (Host?.Entity != null && !closing && PreviewData(p) is { } d) Host.Invoke("setData", Host.Obj(d.ToArray())); }

    // ---- the setData objects ----------------------------------------------------------------------------------------

    /// <summary>
    /// UpdateScrollInfo's object (@0x140f3cb10, :307): null when the scroll is not in the inventory. The level "{min}" or
    /// "{min}~{max}", the coloured rank names and the slot names (string table 8) each joined by ", " (TrimRight of ", ").
    /// </summary>
    public List<(string, object)> ScrollInfoData()
    {
        if (Book == null || Model == null || Book.Find(Book.ScrollUid) is not { } scroll) return null;
        string level = Book.LevelMin == Book.LevelMax
            ? ClientFormat.Format("{0}", Book.LevelMin)
            : ClientFormat.Format("{0}~{1}", Book.LevelMin, Book.LevelMax);
        string ranks = string.Join(", ", Book.Ranks.Select(r => ColoredRank((uint)r))).TrimEnd(',', ' ');
        string types = string.Join(", ", Book.EquipTypes.Select(t => Model.Tables.SlotName(t))).TrimEnd(',', ' ');
        return new List<(string, object)>
        {
            ("itemGUID", "0"),
            ("scrollType", Book.Type),
            ("bindID", Slot),
            ("ready", 0),
            ("desc", ScrollDescription(scroll)),
            ("limit", ClientFormat.Format(T(0xb6d), level, ranks, types)),
            ("failDesc", FailDescription()),
        };
    }

    /// <summary>
    /// FUN_140f3bd10's object (:227): null when the preview was not shown. itemName is the item at its level
    /// (FUN_140527030, the stable flag, is always 0: CItemEnchantInfo::FUN_14058c8a0); the switch on the packet's type adds the
    /// rate text (0xb70: fail, 100 - fail), resultItemName and, from the rows read, the options.
    /// </summary>
    public List<(string, object)> PreviewData(EnchantScrollMessage.Preview p)
    {
        if (Book == null || Model == null || p == null || !p.Shown) return null;
        if (Book.Find(p.ItemUid) is not { } item || Book.Find(Book.ScrollUid) is not { } scroll) return null;
        var m = new List<(string, object)>
        {
            ("scrollType", Book.Type),
            ("itemGUID", p.ItemUid.ToString(CultureInfo.InvariantCulture)),
            ("bindID", Slot),
            ("ready", 1),
            ("itemName", NameAtLevel(item.Enchant.Level, item, false, false)),
            ("desc", ScrollDescription(scroll)),
        };
        switch (p.Type)
        {
            case 1:
            case 2:
                m.Add(("prop", ClientFormat.Format(T(0xb70), Book.FailRate, 100u - Book.FailRate)));
                m.Add(("resultItemName", NameAtLevel(Book.TargetLevel, item, false, false)));
                if (p.RowsRead) m.Add(("options", LevelRows(item, p.Rows)));
                break;
            case 5:
                m.Add(("prop", ClientFormat.Format(T(0xb70), Book.FailRate, 100u - Book.FailRate)));
                m.Add(("resultItemName", NameAtLevel(item.Enchant.Level, item, false, true)));
                break;
            case 3:
                m.Add(("resultItemName", ClientFormat.Format(T(0xb6e), TextColorString(item.Rarity), LevelText(), Model.Tables.ItemName(item.Id))));
                m.Add(("options", RandomRows(item, p.Rows, p.High)));
                break;
        }
        return m;
    }

    /// <summary>One options row: statName, prevStat, afterStat.</summary>
    public readonly record struct OptionRow(string StatName, string PrevStat, string AfterStat);

    /// <summary>
    /// FUN_140f3d5b0 (:370): the rows kept in a CAtlMap(17, 0.75, 0.25, 2.25) (a repeated key keeps its place and takes the
    /// later value), walked in the map's order; each row with a value: the group 0 stat (GetStatValue(key, 0, 0)) scaled by
    /// the item's enchant ratio before, and by the ratio plus the row's rate after.
    /// </summary>
    public List<OptionRow> LevelRowValues(EquipItem item, IReadOnlyList<EnchantScrollRow> rows)
    {
        var last = new Dictionary<int, EnchantScrollRow>();
        foreach (var r in rows) last[r.Key] = r;
        var l = new List<OptionRow>();
        foreach (int key in FieldMeta.AtlMapOrder(rows.Select(r => r.Key)))
        {
            var r = last[key];
            if (!r.HasValue) continue;
            long b = BaseStat(item, key);
            float extra = EnchantRatio(item, key);
            l.Add(new OptionRow(StatName(key), Num(ItemTooltip.ScaleValue(b, extra)), Num(ItemTooltip.ScaleValue(b, extra + r.Rate))));
        }
        return l;
    }

    /// <summary>
    /// FUN_140f3db20 (:400): the keys in first-seen order over the low rows then the high rows; a key shows only when both
    /// maps hold it with a value (the later value of a repeated key), afterStat "{low}~{high}".
    /// </summary>
    public List<OptionRow> RandomRowValues(EquipItem item, IReadOnlyList<EnchantScrollRow> low, IReadOnlyList<EnchantScrollRow> high)
    {
        var lo = new Dictionary<int, EnchantScrollRow>();
        var hi = new Dictionary<int, EnchantScrollRow>();
        var keys = new List<int>();
        foreach (var r in low) { lo[r.Key] = r; if (!keys.Contains(r.Key)) keys.Add(r.Key); }
        foreach (var r in high) { hi[r.Key] = r; if (!keys.Contains(r.Key)) keys.Add(r.Key); }
        var l = new List<OptionRow>();
        foreach (int key in keys)
        {
            if (!lo.TryGetValue(key, out var a) || !hi.TryGetValue(key, out var z)) continue;
            if (!a.HasValue || !z.HasValue) continue;
            long b = BaseStat(item, key);
            float extra = EnchantRatio(item, key);
            l.Add(new OptionRow(StatName(key), Num(ItemTooltip.ScaleValue(b, extra)),
                ClientFormat.Format("{0}~{1}", ItemTooltip.ScaleValue(b, extra + a.Rate), ItemTooltip.ScaleValue(b, extra + z.Rate))));
        }
        return l;
    }

    List<object> LevelRows(EquipItem item, IReadOnlyList<EnchantScrollRow> rows) => AsRows(LevelRowValues(item, rows));
    List<object> RandomRows(EquipItem item, IReadOnlyList<EnchantScrollRow> low, IReadOnlyList<EnchantScrollRow> high) =>
        AsRows(RandomRowValues(item, low, high));

    List<object> AsRows(List<OptionRow> rows)
    {
        var l = new List<object>(rows.Count);
        foreach (var r in rows) l.Add(Host.Obj(("statName", r.StatName), ("prevStat", r.PrevStat), ("afterStat", r.AfterStat)));
        return l;
    }

    static string Num(long v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>GetStatValueText(key) with "{0}" removed.</summary>
    string StatName(int key) => Model.Tables.ItemOptions.StatValueText(key).Replace("{0}", "");

    /// <summary>CItemStatus::GetStatValue(key, 0, 0): option group 0's value.</summary>
    static long BaseStat(EquipItem item, int key) => item.Options.Stat[0].TryGetValue(key, out var v) ? v : 0;

    /// <summary>CItemStatus::GetEnchantRatio: a ratio stat's enchant option as a rate (value / 1000), else 0.</summary>
    public static float EnchantRatio(EquipItem item, int key)
    {
        if (!ItemTooltip.IsRatioStat(key)) return 0;
        float rate = item.Enchant.OptionRates.TryGetValue(key, out var f) ? f : 0;
        return item.Enchant.Options.TryGetValue(key, out var v) ? rate + v / 1000.0f : rate;
    }

    // ---- the texts --------------------------------------------------------------------------------------------------

    /// <summary>CItemDataTable::GetTextColorString (itemdatatable.cpp:408): "#" and the colour in hex.</summary>
    string TextColorString(int rarity) => "#" + Model.Tables.Items.TextColor(rarity).ToString("x", CultureInfo.InvariantCulture);

    /// <summary>GetItemNameAtLevel (@0x140f3d320): Common 0xb6e with the colour, the level and the item's name; the stable
    /// flag appends Common 0xb6f, else the random flag s_enchantscroll_stable.</summary>
    string NameAtLevel(int level, EquipItem item, bool stable, bool random)
    {
        string s = ClientFormat.Format(T(0xb6e), TextColorString(item.Rarity), level, Model.Tables.ItemName(item.Id));
        if (stable) s += " " + T(0xb6f);
        else if (random) s += " " + T("s_enchantscroll_stable");
        return s;
    }

    /// <summary>FUN_140f3e390 (:458): types 1/2 the target level, 3 the random range, 5 the level of the item put on; else "".</summary>
    public string LevelText() => Book.Type switch
    {
        1 or 2 => ClientFormat.Format("{0}", Book.TargetLevel),
        3 => ClientFormat.Format("{0}~{1}", Book.RandomMin, Book.RandomMax),
        5 => Book.Find(Book.ItemUid) is { } i ? ClientFormat.Format("{0}", i.Enchant.Level) : "",
        _ => "",
    };

    /// <summary>GetScrollDescription (@0x140f3e550): Common 0xb69 / 0xb6a / 0xb6c / 0xb6b by type with the coloured scroll name
    /// (FUN_140528160) and <see cref="LevelText"/>; disableBreak appends s_enchantscroll_desc_disable_break; then
    /// ReplaceValueWords.</summary>
    string ScrollDescription(EquipItem scroll)
    {
        int id = Book.Type switch { 1 => 0xb69, 2 => 0xb6a, 3 => 0xb6c, 5 => 0xb6b, _ => 0 };
        string name = FindItem(scroll.Uid) is { } ui ? new ItemTooltip(Model, 0).Name(ui) : Model.Tables.ItemName(scroll.Id);
        string colored = $"<FONT color='#{Model.Tables.Items.TextColor(scroll.Rarity):x}'>{name}</FONT>";
        string s = ClientFormat.Format(id == 0 ? "" : T(id), colored, LevelText());
        if (Book.DisableBreak) s += T("s_enchantscroll_desc_disable_break");
        return ReplaceValueWords(s);
    }

    /// <summary>FUN_140f3e730 (:510): the fail text of the type; type 3 formats the random range (as unsigned).</summary>
    string FailDescription() => Book.Type switch
    {
        1 => T("s_enchant_fail_desc_enchant"),
        2 => T("s_enchant_fail_desc_restore"),
        3 => ClientFormat.Format(T("s_enchant_fail_desc_random"), (uint)Book.RandomMin, (uint)Book.RandomMax),
        5 => T("s_enchant_fail_desc_repair"),
        _ => "",
    };

    /// <summary>FUN_14183e9c0(GetRankName(rank), rank): the rank name (Common 0x4f9 for 0, else s_word_rank_NN) in the rank's
    /// text colour.</summary>
    string ColoredRank(uint rank)
    {
        string name = rank == 0 ? T(0x4f9) : T("s_word_rank_" + rank.ToString("00", CultureInfo.InvariantCulture));
        return $"<FONT color='#{Model.Tables.Items.TextColor((int)rank):x}'>{name}</FONT>";
    }

    // ---- the boxes --------------------------------------------------------------------------------------------------

    /// <summary>
    /// FUN_140f3c6c0's success box (:280-287): CreateMessageBox(0x11, Common 0xb71, the dialog, "enchant_ok"), SetItemUid,
    /// FUN_141a024a0(0) (Enter does not answer), SetItemEntry(item, its name, 1, 0, -1, -1, 1), the button Common 0x475.
    /// OnMessage (:184): button 0 with that tag closes the dialog (vfunc_67).
    /// </summary>
    public static MessageBoxData ResultBox(UiModel model, Item item, Action onOk)
    {
        var box = MessageBoxQueue.Create(ResultBoxType, model.Tables.Common(ResultText) ?? "", null, ResultTag);
        box.ItemUid = item.Uid;
        box.EnterAllowed = false;
        box.SetItemEntry(item, new ItemTooltip(model, 0).Name(item), 1, 0, -1, -1, 1, model.Tables.Items.TextColor);
        box.SetButtonLabelIds(ResultButton, 0, 0);
        box.Callback = (button, _) => { if (button == 0) onOk?.Invoke(); return true; };
        return box;
    }

    /// <summary>FUN_141902660 (uicommon.cpp:2009): CreateMessageBox(1, s_itemenchant_enchant_exp_refund, none, ""), the uid
    /// appended, Enter does not answer; SendRequestItemEnchant (button 0) sends the refund.</summary>
    public static MessageBoxData RefundBox(UiModel model, long uid, Action<long> refund)
    {
        var box = MessageBoxQueue.Create(1, model.Tables.Common("s_itemenchant_enchant_exp_refund") ?? "", null, "");
        box.ItemUid = uid;
        box.EnterAllowed = false;
        box.Callback = (button, _) => { if (button == 0) refund?.Invoke(uid); return true; };
        return box;
    }

    // ---- callbacks --------------------------------------------------------------------------------------------------

    /// <summary>vfunc_28 (@0x140f3b680, :157): onEnchant with the movie's item guid; the slot's notifySlotUse and
    /// notifySlotShiftClick (NotifyDragSlot).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Book == null) return false;
        switch (name.ToLowerInvariant())
        {
            case "onenchant":
                if (args.Length == 0) return false;           // SetArgsIfAny: no args, not handled
                if (ulong.TryParse(Rt.ToString(args[0]), NumberStyles.None, CultureInfo.InvariantCulture, out ulong uid))
                    Book.Enchant((long)uid);
                else Book.Enchant(0);
                // The client's vfunc_28 returns 0 after the send (uienchantscrolldialog.cpp:175); nothing reads the value for
                // this movie, and true here only keeps the name off UiHost's Unhandled list.
                return true;
            case "notifyslotuse": return SlotUse(args);
            case "notifyslotshiftclick":
                if (args.Length < 1 || args[0] as string != Slot) return false;
                Host.Push(Slot);                                // DragSlot::vfunc_15
                return true;
        }
        return false;
    }

    /// <summary>NotifyDragSlot::SlotUse (notifydragslot.cpp:33): button 0 without Shift pushes the slot again (vfunc_13);
    /// button 1, the slot not enabled (vfunc_19 is 0), vfunc_14 -> vfunc_17: the item taken off and UpdateScrollInfo.</summary>
    bool SlotUse(object[] args)
    {
        if (args.Length < 1 || args[0] as string != Slot) return false;
        int button = args.Length > 1 ? InventoryController.ArgInt(args[1]) : 0;
        if (button == 0) { if (!ShiftHeld()) Host.Push(Slot); }
        else if (button == 1) Book.RemoveSlotItem();
        return true;
    }

    /// <summary>An inventory item dropped on the slot (EnchantScrollSlot::vfunc_18): the refund box, the put request, or
    /// nothing; the source slot is pushed either way.</summary>
    public EnchantScrollPut Drop(long uid)
    {
        var r = Book?.Put(uid) ?? EnchantScrollPut.None;
        if (r == EnchantScrollPut.AskRefund && Model != null) ShowData(RefundBox(Model, uid, Book.ConfirmRefund));
        return r;
    }

    // ---- the slot (EnchantScrollSlot) -------------------------------------------------------------------------------

    Item SlotItem => Book != null && Book.SlotRegistered && Book.SlotItemUid != 0 ? FindItem(Book.SlotItemUid) : null;

    public override object GetBinding(string name)
    {
        if (name != Slot) return base.GetBinding(name);
        var item = SlotItem;
        if (item == null || Model == null || !Model.HasLocalPlayer) return null;      // vfunc_8: no item or no local player
        var slot = new List<(string, object)>();
        return ItemSlot.Build(slot, item, Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources) ? Host.Obj(slot.ToArray()) : null;
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (name != Slot) return base.GetSlotTooltip(name);
        var item = SlotItem;
        if (Model == null || !Model.HasLocalPlayer || item == null) return new AsTooltip();
        var tip = new ItemTooltip(Model, TooltipFlags) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        return tip.Fill(item) ? tip.Data : new AsTooltip();             // the slot is not enabled: no SetEnablePreview
    }

    // ---- IDragSlotOwner ---------------------------------------------------------------------------------------------

    public bool IsDragSlot(string name) => name == Slot && Book != null && Book.SlotRegistered;
    public void RefreshSlot(string name) { if (name == Slot) Host.Push(Slot); }
    public void SlotDragStarted(string name) { }
    public void SlotDropUnresolved(string name) => RefreshSlot(name);

    /// <summary>EnchantScrollSlot::vfunc_18 (enchantscrollslot.cpp:95): only an inventory slot's item counts; the source slot is
    /// pushed (vfunc_10) either way.</summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        if (name == Slot && sourceOwner is IInvenSlotOwner inv && inv.InvenSlotUid(source) is long uid) Drop(uid);
        sourceOwner?.RefreshSlot(source);
    }
}
