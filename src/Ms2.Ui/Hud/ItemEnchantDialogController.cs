using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The item enchant window, UIItemEnchantDialog (client/ms2/uiitemenchantdialog.cpp, UI id 0x4e92, type name
/// "ItemEnchantDialog", movie uiitemenchantdialog). The state is <see cref="ItemEnchantBook"/>; this controller binds the two
/// item bags, writes the movie's setEnchantInfo / setEnchantResult objects from it, handles the movie's callbacks and runs the
/// confirm box chain (refund, enchanted ingredient, binding, protector, payback).
/// <para>STILL-OPEN: GetEnchantableItemUids' limit and lock tests (FUN_14058e230, FUN_140525c60), the sale-rate text (saleDesc),
/// the event bonus of the rate text, GetEnchantRatio (the "before" stat uses 0), the exp divisor and the fail stack limit,
/// failDesc (the failure type is not in the Result event), the failure reward page (inventory 0x4e40), sounds, the protector
/// rank / count warnings, the over-enchanted ingredient test (FUN_1419c9490), the debug buttons (onForceEnchant).</para>
/// </summary>
public sealed class ItemEnchantDialogController : UiController, IDragSlotOwner
{
    public override string Movie => "uiitemenchantdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemEnchantDialog";

    public const int UiId = 0x4e92;
    public const string Bag = "ItemEnchantDialog", EquipBag = "ItemEnchantDialog.equip";
    public const string SelectedSlot = "ItemEnchantDialog.selectedItem", ResultSlot = "ItemEnchantDialog.resultItem",
        EquipIngredientSlot = "ItemEnchantDialog.equip_ingredient", IngredientPrefix = "ItemEnchantDialog.ingredient.";
    /// <summary>BindItemSlot's ItemInfoSlot icon type (0x405) and tooltip flags (0x20).</summary>
    public const int SlotFlags = 0x405;
    public const int ProtectorBoxType = 0x1a;
    /// <summary>The ingredient fee per missing unit of a tag 0x60 ingredient (s_itemenchant_fee).</summary>
    public const int FeePerUnit = 1000;

    public ItemEnchantBook Book;
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    public event Action<string> NoticeText;
    public event Action Closed;
    /// <summary>The server time (the lock test of the equip bag).</summary>
    public Func<long> Now = () => 0;
    /// <summary>The items of an inventory category (0 = equip tab bag, 3), then the worn equips.</summary>
    public Func<int, IEnumerable<Item>> BagItems = _ => Array.Empty<Item>();
    public Func<IEnumerable<Item>> WornItems = () => Array.Empty<Item>();
    /// <summary>The count of the inventory items of an item id and rank (the ingredient have-count).</summary>
    public Func<int, int, int> CountOf = (_, _) => 0;
    /// <summary>FUN_14058e230 &amp; the plus-level / lock tests of GetEnchantableItemUids; default: the book's CanEnchantItem.</summary>
    public Func<Item, bool> Enchantable;
    /// <summary>The feature value the movie's initData gets (feature 0x86).</summary>
    public Func<int> Feature86 = () => 0;
    /// <summary>The exp divisor (FUN_140528f90) of the peachy gauge; 0 hides the percent.</summary>
    public Func<Item, int> ExpDivisor = _ => 0;
    /// <summary>The fail stack limit; 0 = no limit known.</summary>
    public Func<int> FailStackLimit = () => 0;
    /// <summary>The NPC talk branch a finished result reports (state 1 success, 2 failure).</summary>
    public event Action<int> TalkBranch;

    bool closing;
    readonly HashSet<string> bound = new(StringComparer.Ordinal);
    readonly List<string> ingredientSlots = new();
    object bagChild, equipBagChild, ani;
    /// <summary>m_enchantAni: the clip that holds the bags, the debug buttons and the fill methods.</summary>
    object Ani => ani ??= Host.Child("m_enchantAni");

    public static string BagName(long uid) => Bag + "." + uid.ToString(CultureInfo.InvariantCulture);
    public static string EquipName(long uid) => EquipBag + "." + uid.ToString(CultureInfo.InvariantCulture);

    string T(string key) => Model?.Tables.Common(key) ?? "";
    string Fmt(string key, params object[] a) => ClientFormat.Format(T(key), a);

    // ---- load / close -----------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete (@0x1419bf410): initData, the hidden debug buttons, mode 0, the enchantable bag, state 0.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Book == null || Host?.Entity == null) return;
        Book.IsOpen = true;
        Book.Applied += OnApplied;
        Book.CloseRequested += OnCloseRequested;
        Book.Notice += OnNotice;
        Book.SlotReset += OnSlotReset;
        Book.Result += OnResult;
        Host.Invoke("initData", Book.Mode, Feature86());
        foreach (var n in new[] { "m_forceEnchant", "m_compareEnchant", "m_debugEnchantCount" })
            if (Host.Child(n, Ani) is { } c) Host.Set(c, "visible", false);
        Book.Load();
        BindEnchantable();
    }

    public override void Close()
    {
        if (closing) return;
        closing = true;
        if (Book != null)
        {
            Book.Applied -= OnApplied;
            Book.CloseRequested -= OnCloseRequested;
            Book.Notice -= OnNotice;
            Book.SlotReset -= OnSlotReset;
            Book.Result -= OnResult;
            Book.Restore();
            Book.IsOpen = false;
        }
        ClearSlots();
        base.Close();
        bagChild = equipBagChild = ani = null;
        Closed?.Invoke();
    }

    void OnCloseRequested() => Close();

    void OnNotice(int id, int arg)
    {
        string text = id == 0 ? T(ItemEnchantBook.RefundOkKey) : Model?.Tables.Common(id) ?? "";
        if (id != 0 && arg != 0 && text.Length > 0) text = ClientFormat.Format(text, arg);
        if (text.Length > 0) NoticeText?.Invoke(text);
    }

    void OnSlotReset()
    {
        if (Host?.Entity == null) return;
        Host.Invoke(Ani, "initEnchantSlot");
        ClearFill();
    }

    // ---- the bags ----------------------------------------------------------------------------------------------------

    /// <summary>GetEnchantableItemUids: category 0 and worn items that pass the limit test.</summary>
    public List<Item> EnchantableItems()
    {
        var test = Enchantable ?? Book.CanEnchantItem;
        var list = new List<Item>();
        foreach (var i in BagItems(0)) if (i.EnchantLevel <= 0 && test(i)) list.Add(i);
        foreach (var i in WornItems()) if (i.EnchantLevel <= 0 && test(i)) list.Add(i);
        return list;
    }

    /// <summary>FUN_1419bfd70: the bag named by the type name holds the enchantable items.</summary>
    public void BindEnchantable()
    {
        if (Host?.Entity == null || closing) return;
        DropBag(Bag + ".", EquipBag);
        foreach (var i in EnchantableItems()) Bind(BagName(i.Uid), i, SlotFlags);
        bagChild ??= Host.Child("m_itemBag", Ani);
        if (bagChild != null) Host.Set(bagChild, "bagId", Bag);
    }

    /// <summary>FUN_1419bff60: the equip bag holds the items of categories 0 and 3 that can be an ingredient (only when Ready).</summary>
    void BindIngredients()
    {
        if (Host?.Entity == null || closing) return;
        DropBag(EquipBag + ".", null);
        if (!Book.Ready) return;
        long now = Now();
        foreach (int cat in new[] { 0, 3 })
            foreach (var i in BagItems(cat))
                if (Book.IsIngredientCandidate(i, now)) Bind(EquipName(i.Uid), i, SlotFlags);
        equipBagChild ??= Host.Child("m_ingredientBag", Ani);
        if (equipBagChild != null) Host.Set(equipBagChild, "bagId", EquipBag);
    }

    void Bind(string name, Item item, int flags)
    {
        Model.ItemSlots[name] = new ItemInfoSlot(name, item, 0, flags, 0);
        bound.Add(name);
    }

    void DropBag(string prefix, string except)
    {
        var drop = new List<string>();
        foreach (var n in bound)
            if (n.StartsWith(prefix, StringComparison.Ordinal) && (except == null || !n.StartsWith(except, StringComparison.Ordinal))) drop.Add(n);
        foreach (var n in drop) { bound.Remove(n); Model.ItemSlots.Remove(n); }
    }

    /// <summary>FUN_1419c77a0: the fill slots (selected, ingredients, equip ingredient, result) are destroyed.</summary>
    void ClearFill()
    {
        foreach (var n in ingredientSlots) { bound.Remove(n); Model.ItemSlots.Remove(n); }
        ingredientSlots.Clear();
        foreach (var n in new[] { SelectedSlot, ResultSlot, EquipIngredientSlot })
            if (bound.Remove(n)) Model.ItemSlots.Remove(n);
    }

    void ClearSlots()
    {
        if (Model == null) return;
        foreach (var n in bound) Model.ItemSlots.Remove(n);
        bound.Clear();
        ingredientSlots.Clear();
    }

    public override IEnumerable<string> GetBagSlotBindings(string bag)
    {
        string prefix = bag == Bag ? Bag + "." : bag == EquipBag ? EquipBag + "." : null;
        if (prefix == null) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var n in bound)
            if (n.StartsWith(prefix, StringComparison.Ordinal) && (bag == EquipBag || !n.StartsWith(EquipBag, StringComparison.Ordinal))
                && long.TryParse(n.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out _)) list.Add(n);
        return list;
    }

    public override object GetBinding(string name)
    {
        if (name != null && Model != null && Model.ItemSlots.TryGetValue(name, out var slot) && bound.Contains(name))
            return slot.Value(Host, Model, Model.ItemSlotSources);
        return base.GetBinding(name);
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (name != null && Model != null && bound.Contains(name) && Model.ItemSlots.TryGetValue(name, out var slot) && slot.Item != null)
        {
            var tip = new ItemTooltip(Model, 0x20) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
            return tip.Fill(slot.Item) ? tip.Data : new AsTooltip();
        }
        return base.GetSlotTooltip(name);
    }

    // ---- the fill ----------------------------------------------------------------------------------------------------

    void OnApplied(ItemEnchantMessage m)
    {
        if (Host?.Entity == null || closing) return;
        switch (m)
        {
            case ItemEnchantMessage.Info:
                BindIngredients();
                UpdateEnchantInfo();
                break;
            case ItemEnchantMessage.ChargeChanged:
                ModifiedExp();
                BindEnchantable();
                BindIngredients();
                break;
            case ItemEnchantMessage.FailStackInfo:
            case ItemEnchantMessage.FailStackList:
                UpdateIngredient();
                break;
            case ItemEnchantMessage.IngredientCounts:
                UpdateIngredientCounts();
                BindIngredients();
                break;
            case ItemEnchantMessage.ItemUpdate:
                BindEnchantable();
                break;
        }
    }

    Item Selected => Book.Find(Book.SelectedUid);

    string Colored(int itemId, int rank) =>
        $"<FONT color='#{Model.Tables.Items.TextColor(rank):x}'>{Model.Tables.ItemName(itemId)}</FONT>";

    /// <summary>GetIngredientCountText: have / need, lack variant under, the overflow variant over.</summary>
    public string IngredientCountText(int have, int need)
    {
        if (have < need) return Fmt("s_common_cur_max_ingredient_lack", have, need);
        string t = Fmt("s_common_cur_max_ingredient", have, need);
        return t;
    }

    /// <summary>The fee text of a tag 0x60 ingredient that is short.</summary>
    public string FeeText(int have, int need) => Fmt("s_itemenchant_fee", (long)(need - have) * FeePerUnit);

    /// <summary>The chance the rate text shows: base plus the two bonus rates (m_23c = base + m_234 + m_238).</summary>
    public float Chance => Book.Rates.Base + Book.Rates.RateFourth + Book.Rates.RateFifth;

    /// <summary>FormatEnchantRateText: ophelia mode only.</summary>
    public string RateText()
    {
        if (Book.ExpMode != ItemEnchantBook.ExpOphelia) return "";
        string t = Fmt("s_itemenchant_option_prop", Chance);
        if (Book.Rates.RateFourth != 0 || Book.Rates.RateFifth != 0)
            t += Fmt("s_itemenchant_prop_detail", Book.Rates.Base, Book.Rates.RateFourth, Book.Rates.RateFifth);
        return t;
    }

    /// <summary>One stat text: the item's option 0 value scaled by (extra + rate/1000), as GetEnchantStatText.</summary>
    string StatText(Item item, int stat, float rate)
    {
        int b = item?.Record != null && item.Record.Options.Stat[0].TryGetValue(stat, out var v) ? v : 0;
        long scaled = (long)((float)(long)b * rate);
        if (scaled == 0) return ClientFormat.Format(Model.Tables.Common("s_word_stat_increase_near_zero"), Model.Tables.StatName(stat));
        return ClientFormat.Format(Model.Tables.ItemOptions.StatValueText(stat), (int)scaled);
    }

    /// <summary>The setEnchantInfo object (UpdateEnchantInfo @0x1419c81a0).</summary>
    public object BuildInfo()
    {
        var item = Selected;
        var m = new List<(string, object)>();
        if (item == null) return Host.Obj(m.ToArray());
        m.Add(("id", (uint)item.ItemId));
        m.Add(("itemName", Colored(item.ItemId, item.Rarity)));
        m.Add(("prop", RateText()));
        m.Add(("desc", ""));
        m.Add(("guid", item.Uid.ToString(CultureInfo.InvariantCulture)));
        m.Add(("frame", Frame()));
        var options = new List<object>();
        foreach (var kv in Book.Options)
            options.Add(Host.Obj(("statName", StatText(item, kv.Key, 0f)), ("afterStat", StatText(item, kv.Key, kv.Value / 1000f))));
        m.Add(("options", options));
        Bind(SelectedSlot, item, SlotFlags);
        m.Add(("enchantItem", SelectedSlot));
        AddIngredients(m);
        if (Book.ExpMode == ItemEnchantBook.ExpPeachy) AddExp(m, item);
        else AddFailStack(m);
        return Host.Obj(m.ToArray());
    }

    string Frame()
    {
        if (Book.ExpMode == ItemEnchantBook.ExpPeachy) return "EnchantExpGauge";
        return Book.Mode == 3 && Book.FailStackMode != 0 ? "EnchantFailStack" : "Enchant";
    }

    void AddIngredients(List<(string, object)> m)
    {
        foreach (var n in ingredientSlots) { bound.Remove(n); Model.ItemSlots.Remove(n); }
        ingredientSlots.Clear();
        var list = new List<object>();
        long fee = 0;
        for (int i = 0; i < Book.Ingredients.Count; i++)
        {
            var g = Book.Ingredients[i];
            var it = Item.Create(Model.Tables.Items, g.ItemId, 0);
            string name = IngredientPrefix + i.ToString(CultureInfo.InvariantCulture);
            if (it != null) { it.SetCountClamped(g.Count); Bind(name, it, SlotFlags); ingredientSlots.Add(name); }
            int have = CountOf(g.ItemId, 0);
            if (g.Tag == 0x60 && g.Count > have) fee += (long)(g.Count - have) * FeePerUnit;
            list.Add(Host.Obj(("binding", name), ("countText", IngredientCountText(have, g.Count))));
        }
        m.Add(("ingredient", list));
        m.Add(("enchantFee", fee > 0 ? Fmt("s_itemenchant_fee", fee) : ""));
        AddEquipIngredient(m);
    }

    void AddEquipIngredient(List<(string, object)> m)
    {
        var res = Item.Create(Model.Tables.Items, Book.ResultItemId, Book.ResultRank);
        if (res != null) { res.SetCountClamped(Book.ResultCount); Bind(EquipIngredientSlot, res, SlotFlags); }
        int have = CountOf(Book.ResultItemId, Book.ResultRank);
        m.Add(("equipIngredient", Host.Obj(("binding", EquipIngredientSlot), ("countText", IngredientCountText(have, Book.ResultCount)))));
        var uids = new List<object>();
        foreach (var n in bound)
            if (n.StartsWith(EquipBag + ".", StringComparison.Ordinal)) uids.Add(n.Substring(EquipBag.Length + 1));
        m.Add(("equipList", uids));
    }

    void AddFailStack(List<(string, object)> m)
    {
        if (Book.Mode != 3 || Book.FailStackMode == 0) return;
        int max = FailStackLimit();
        int cur = Book.FailStack;
        m.Add(("failStack", Host.Obj(("current", cur), ("maximum", max > 0 ? max : cur))));
    }

    void AddExp(List<(string, object)> m, Item item)
    {
        int cur = item.Record?.Enchant?.Charge ?? 0, max = ExpDivisor(item);
        m.Add(("expPercent", max > 0 ? Math.Min(100, cur * 100 / max) : 0));
        m.Add(("expText", cur.ToString(CultureInfo.InvariantCulture) + " / " + max.ToString(CultureInfo.InvariantCulture)));
    }

    void UpdateEnchantInfo()
    {
        Host.Invoke(Ani, "setEnchantInfo", BuildInfo());
        Host.Invoke(Ani, "playEnchant");
    }

    /// <summary>updateEnchantIngredient: prop, fail stack and equip ingredient.</summary>
    void UpdateIngredient()
    {
        var m = new List<(string, object)> { ("prop", RateText()) };
        AddFailStack(m);
        AddEquipIngredient(m);
        Host.Invoke(Ani, "updateEnchantIngredient", Host.Obj(m.ToArray()));
        BindIngredients();
    }

    /// <summary>onModifiedExp(obj, bUp) after a charge packet: ingredient list, exp info and equip ingredient.</summary>
    void ModifiedExp()
    {
        var item = Selected;
        var m = new List<(string, object)>();
        AddIngredients(m);
        if (item != null) AddExp(m, item);
        Host.Invoke(Ani, "onModifiedExp", Host.Obj(m.ToArray()), (item?.Record?.Enchant?.Charge ?? 0) > Book.PrevCharge);
    }

    /// <summary>UpdateIngredientCounts: the ingredient count texts again and the equip bag rebound.</summary>
    void UpdateIngredientCounts()
    {
        var m = new List<(string, object)>();
        AddIngredients(m);
        Host.Invoke(Ani, "updateEnchantIngredient", Host.Obj(m.ToArray()));
    }

    /// <summary>setEnchantResult: success flag, name, levels, the result slot, stats (success) or protect (failure).</summary>
    void OnResult(bool success, long uid, int prev, int cur)
    {
        if (Host?.Entity == null || closing) return;
        var item = Book.Shown != null ? Item.Create(Model.Tables.Items, Book.Shown.Id, Book.Shown.Rarity) : Selected;
        var m = new List<(string, object)> { ("success", success ? 1 : 0) };
        if (item != null)
        {
            if (Book.Shown != null) item.Record = Book.Shown;
            m.Add(("itemName", Colored(item.ItemId, item.Rarity)));
            Bind(ResultSlot, item, SlotFlags);
            m.Add(("binding", ResultSlot));
        }
        m.Add(("diff", cur - prev));
        m.Add(("prevLevel", Level(prev)));
        m.Add(("curLevel", Level(cur)));
        if (success)
        {
            var sel = Selected;
            var parts = new List<string>();
            foreach (var kv in Book.Options) parts.Add(StatText(sel, kv.Key, kv.Value / 1000f));
            m.Add(("enchantStat", string.Join(",", parts)));
        }
        else m.Add(("protect", Book.Protector0 != 0 || Book.Protector1 != 0 ? 1 : 0));
        Host.Invoke(Ani, "setEnchantResult", Host.Obj(m.ToArray()));
    }

    static string Level(int n) => $"<font color='{(n > 0 ? "#ffd200" : "#ffffff")}'>+{n}</font>";

    // ---- callbacks ---------------------------------------------------------------------------------------------------

    static long Uid(object a)
    {
        string s = a?.ToString() ?? "";
        int dot = s.LastIndexOf('.');
        return BlackMarketDialogController.ParseInt64(dot >= 0 ? s[(dot + 1)..] : s);
    }

    public override bool OnCallback(string name, object[] args)
    {
        if (Book == null) return false;
        switch (name.ToLowerInvariant())
        {
            case "onselectitem":
                if (args.Length == 0) return false;
                Step(Book.Select(Uid(args[0])));
                return true;
            case "onenchant":
                Step(Book.Enchant());
                return true;
            case "onretry":
                Book.Retry();
                ClearFill();
                return true;
            case "oncomplete":
                TalkBranch?.Invoke(Book.State);
                Book.Complete();
                BindEnchantable();
                return true;
            case "onselectequipingredient":
                if (args.Length == 0) return false;
                Step(Book.SelectIngredient(Uid(args[0]), args.Length > 1 && Rt.ToBoolean(args[1]) ? (byte)1 : (byte)0));
                return true;
            case "onclickfailstackup":
                Book.ChangeFailStack(1);
                return true;
            case "onclickfailstackdown":
                Book.ChangeFailStack(-1);
                return true;
        }
        return false;
    }

    /// <summary>Maps a step the book refused or deferred to its box.</summary>
    public void Step(EnchantStep step)
    {
        switch (step)
        {
            case EnchantStep.AskRefund: AskRefund(); break;
            case EnchantStep.AskEnchantedIngredient: Confirm("s_itemenchant_over_enchanted_item", Book.ConfirmEnchantedIngredient); break;
            case EnchantStep.AskBindingCharacter: Confirm("s_itemenchant_binding_character", Book.ConfirmBinding); break;
            case EnchantStep.AskProtector: AskProtector(); break;
            case EnchantStep.AskPayback: AskPayback(); break;
        }
    }

    void Confirm(string key, Action yes)
    {
        var box = MessageBoxQueue.CreateWithCallback(T(key), 1, (b, _) => { if (b == 0) yes(); return true; });
        ShowData(box);
    }

    void AskRefund() => Confirm("s_itemenchant_enchant_exp_refund", Book.ConfirmRefund);

    void AskPayback() => Confirm("s_itemenchant_exp_payback", Book.ConfirmPayback);

    /// <summary>The protector text: the level after, the success and failure chance, the protection rate (Common 0x95f).</summary>
    string ProtectorText()
    {
        int lv = (Selected?.EnchantLevel ?? 0) + 1;
        float chance = Chance;
        return ClientFormat.Format(Model.Tables.Common(0x95f), lv, chance, 100 - chance, Book.Rates.Protect);
    }

    /// <summary>OnEnchant with a protection rate: a protector item box (type 0x1a, checkbox) or, without a protector, a plain box.</summary>
    void AskProtector()
    {
        int v = Book.Protector0 != 0 ? 1 : Book.Protector1 != 0 ? 2 : 0;
        if (v == 0)
        {
            var plain = MessageBoxQueue.CreateWithCallback(ProtectorText(), 1, (b, _) => { if (b == 0) Step(Book.ConfirmProtector(0)); return true; });
            ShowData(plain);
            return;
        }
        var box = MessageBoxQueue.Create(ProtectorBoxType, ProtectorText(), null, "");
        var pItem = Book.Find(v == 1 ? Book.Protector0 : Book.Protector1);
        if (pItem != null) box.SetItemEntry(pItem, Colored(pItem.ItemId, pItem.Rarity), 0, 1, -1, -1, 1, Model.Tables.Items.TextColor);
        box.SetButtonPreset(1);
        box.SetCheckbox0(Model.Tables.Common(0x961), false);
        box.Callback = (b, bx) =>
        {
            if (b != 0) return true;
            bool use = bx.Check0Selected;
            string key = !use ? "s_itemenchant_protector_dont_use_warn"
                : v == 2 ? "s_itemenchant_super_protector_use_warn" : "s_itemenchant_protector_use_warn";
            Confirm(key, () => Step(Book.ConfirmProtector(use ? v : 0)));
            return true;
        };
        ShowData(box);
    }

    // ---- IDragSlotOwner ----------------------------------------------------------------------------------------------

    public bool IsDragSlot(string name) => name != null && bound.Contains(name);
    public void RefreshSlot(string name) { if (IsDragSlot(name)) Host.Push(name); }
    public void SlotDragStarted(string name) { }
    public void SlotDropUnresolved(string name) => RefreshSlot(name);
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner) => sourceOwner?.RefreshSlot(source);
}
