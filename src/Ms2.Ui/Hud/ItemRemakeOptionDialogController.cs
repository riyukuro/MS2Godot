using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Wire;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The Change Attributes window, UIItemRemakeOptionDialog (unnamed/unit_14129cfa0_itemremakeoptiondialog.cpp, UI id 0x4eeb, type
/// name "ItemRemakeOptionDialog", movie uiitemenchantdialog through vfunc_7's "ItemEnchantDialog"), remake mode 2 (the random
/// options). The item bag holds the items that can be remade; a pick sends mode 4, the server's kind 4 fills the selected item, its
/// ingredients and options; a remake sends mode 0 (with an option to lock), kind 1 shows the old and the new options; the apply
/// sends mode 2 (the new) or 3 (keep the old).
/// <para>STILL-OPEN: the remake sale (CGameRule::FUN_140478cd0's GameEventSaleRemake rates, the reader skips them) is
/// <see cref="SaleRate"/>, 0 by default; the item event
/// listener (UIItemEventListener, FUN_1419cae20); the debug buttons and the kind 8 resend (onClickForceRemake,
/// onClickCompareEnchant, m_debugBtnFillIngredient*); what the type argument of FUN_1412752d0 changes.</para>
/// </summary>
public sealed class ItemRemakeOptionDialogController : UiController, IDragSlotOwner
{
    public override string Movie => "uiitemenchantdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemRemakeOptionDialog";
    /// <summary>UIItemModifyDialogBase's ctor has no SetTweenEnabled (uiitemenchantdialog.cpp:329): the close unloads at once.</summary>
    protected override bool TweenEnabled => false;

    public const int UiId = 0x4eeb;
    public const string Bag = "ItemRemakeOptionDialog";
    public const string SelectedSlot = Bag + ".selectedItem", ResultSlot = Bag + ".resultItem", IngredientPrefix = Bag + ".ingredient.";
    /// <summary>m_remakeMode: 2, the random options (the ctor and vfunc_67).</summary>
    public const int RemakeMode = 2;
    /// <summary>BindItemToSlot's CreateItemInfoSlot icon type (0x405) and tooltip flags (0x2020).</summary>
    public const int SlotFlags = 0x405, TipFlags = 0x2020;
    /// <summary>The fee per missing unit of a tag 0x60 ingredient (s_itemenchant_fee).</summary>
    public const int FeePerUnit = 1000;
    /// <summary>The meso tag (CItem::FUN_140526320: a stringTag of 0x60).</summary>
    public const int MesoTag = 0x60;

    /// <summary>The states (m_state): 0 loaded, 1 the open answer came, 2 an item is picked, 3 a remake is out or its result is
    /// shown, 4 applied.</summary>
    public int State { get; private set; }
    /// <summary>m_cur: the picked item; m_new: the remade copy.</summary>
    public Item Cur { get; private set; }
    public Item New { get; private set; }
    /// <summary>m_optionCount: the rows of the picked item's options.</summary>
    public int OptionCount { get; private set; }

    public Action<IOutPacket> Send = _ => { };
    /// <summary>FindItemByUid / FUN_1419be700: the inventory's item of the uid, else the worn one.</summary>
    public Func<long, Item> FindItem = _ => null;
    /// <summary>GetItemsInCategory(0) and CollectShownItemsInLayer(list, 0).</summary>
    public Func<int, IEnumerable<Item>> BagItems = _ => Array.Empty<Item>();
    public Func<IEnumerable<Item>> WornItems = () => Array.Empty<Item>();
    /// <summary>Unk_Inventory::FUN_140a5f7b0(tag, rarity): the items of a tag (and rarity, 0 any).</summary>
    public Func<int, int, int> CountByTag = (_, _) => 0;
    /// <summary>DAT_143ab2898: the meso.</summary>
    public Func<long> Meso = () => 0;
    /// <summary>The equipment's GetAbilityRate(0xa9); 0 without the HUD.</summary>
    public Func<float> AbilityRate = () => 0f;
    /// <summary>CGameRule::FUN_140478cd0: the remake sale of the item's kind (STILL-OPEN, 0).</summary>
    public Func<ItemData, uint> SaleRate = _ => 0;
    /// <summary>FUN_1412a1650: the item goes back into the inventory (ReplaceItem) or onto the worn slot.</summary>
    public Action<Item> Store = _ => { };
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>FUN_14151f460(1): the queue is dropped and every box closed with 1.</summary>
    public Action ClearBoxes = () => MessageBoxQueue.Clear(1);
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public event Action<string> NoticeText;
    /// <summary>FUN_1412751b0(text, type): a chat line.</summary>
    public event Action<string, int> ChatLine;
    public event Action<string> PlaySound;
    /// <summary>vfunc_85(type): NpcTalkModifyRequest of the type (100 picked, 0x66 the remake animation ended, 0x67 applied).</summary>
    public event Action<int> TalkModify;
    public event Action Closed;

    bool closing;
    readonly List<string> names198 = new(), names1b8 = new();
    readonly HashSet<string> bagSlots = new(StringComparer.Ordinal);
    object ani;
    /// <summary>m_enchantAni.</summary>
    object Ani => ani ??= Host?.Child("m_enchantAni");

    public static string BagName(long uid) => Bag + "." + uid.ToString(CultureInfo.InvariantCulture);

    string T(string key) => Model?.Tables.Common(key) ?? "";
    string Fmt(string key, params object[] a) => ClientFormat.Format(T(key), a);
    ItemTooltip Tip => new(Model, 0);
    void Notice(string key) => NoticeText?.Invoke(T(key));

    /// <summary>The NOTICE box of the file (:194): ShowMessageBoxWithCallback(text, 0, FUN_140b240b0()).</summary>
    void NoticeBox(string text) => ShowData(MessageBoxQueue.CreateWithCallback(text, 0, (_, _) => true));

    // ---- load / close -----------------------------------------------------------------------------------------------

    /// <summary>
    /// OnLoadComplete (@0x14129d2c0, :238). UIItemModifyDialogBase::OnLoadComplete (@0x1419bea10, uiitemenchantdialog.cpp:356)
    /// first: initData with the mode (2) and feature 0x86, then without m_enchantAni the window closes (and the rest still runs),
    /// else the debug children are hidden (m_forceEnchant shows only in debug mode). Then state 0, the bag, input off and mode 6
    /// [1] tells the server the window opened. The pet window (0x4f38) the client closes is the HUD's.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host?.Entity == null) return;
        Host.Invoke("initData", RemakeMode, Model.IsFeatureEnabled(0x86));                  // vfunc_84
        if (Ani == null) Close();
        else
            foreach (var n in new[] { "m_forceEnchant", "m_compareEnchant", "m_debugEnchantCount" })
                if (Host.Child(n, Ani) is { } c) Host.Set(c, "visible", false);
        State = 0;
        FillBag();
        SetInputEnabled(false);
        Send(ChangeAttributesRequest.Opened());
    }

    /// <summary>vfunc_67 (@0x14129d870, :302): the message boxes close (FUN_14151f460(1)), the bag and the slot keys go, the
    /// state is reset.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        ClearBoxes();
        DropBag();
        ReleaseSlotKeys();
        State = 0;
        OptionCount = 0;
        Cur = New = null;
        base.Close();
        ani = null;
        Closed?.Invoke();
    }

    /// <summary>FUN_1412761e0(m_30, enabled): the root's mouseChildren and mouseEnabled.</summary>
    void SetInputEnabled(bool enable)
    {
        var root = Host?.Entity;
        if (root == null) return;
        Host.Set(root, "mouseChildren", enable);
        Host.Set(root, "mouseEnabled", enable);
    }

    /// <summary>The movie takes no input (FUN_1412761e0(m_30, 0)) until an answer comes.</summary>
    public bool InputLocked => Host?.Entity is { } r && !Rt.ToBoolean(Host.Get(r, "mouseChildren"));

    /// <summary>vfunc_13 (@0x14129dbe0, :345): with a remake out (state 3) the action key 0x493e2 and Escape are swallowed.</summary>
    public bool SwallowsKeys => Ani != null && State == 3;

    // ---- the bag and the slots ---------------------------------------------------------------------------------------

    /// <summary>FUN_14129dd50 (:367): the category 0 items, then the shown worn items of layer 0, that CanRemakeOption(2) passes,
    /// fill the bag named by the type name.</summary>
    public void FillBag()
    {
        if (Host?.Entity == null || closing || Ani == null) return;
        var uids = new List<Item>();
        foreach (var i in BagItems(0)) if (i != null && ItemTooltip.CanRemakeOption(i, RemakeMode)) uids.Add(i);
        foreach (var i in WornItems()) if (i != null && ItemTooltip.CanRemakeOption(i, RemakeMode)) uids.Add(i);
        DropBag();                                                                // FUN_141414e50
        foreach (var i in uids)                                                   // FUN_141414d60
        {
            string n = BagName(i.Uid);
            Model.ItemSlots[n] = new ItemInfoSlot(n, i, 0, SlotFlags, 0);
            bagSlots.Add(n);
        }
        if (Host.Child("m_itemBag", Ani) is { } bag) Host.Set(bag, "bagId", Bag);
    }

    void DropBag()
    {
        if (Model != null) foreach (var n in bagSlots) Model.ItemSlots.Remove(n);
        bagSlots.Clear();
    }

    /// <summary>BindItemToSlot (@0x1412a0720, :743): the slot of the key is made again for the item and the key kept in the list.</summary>
    string BindItemToSlot(Item item, string key, List<string> names)
    {
        Model.ItemSlots.Remove(key);
        Model.ItemSlots[key] = new ItemInfoSlot(key, item, 0, SlotFlags, TipFlags);
        names.Add(key);
        return key;
    }

    /// <summary>ReleaseSlotKeys (@0x1412a0800, :753): the slots of both lists are destroyed.</summary>
    void ReleaseSlotKeys()
    {
        if (Model != null)
        {
            foreach (var n in names198) Model.ItemSlots.Remove(n);
            foreach (var n in names1b8) Model.ItemSlots.Remove(n);
        }
        names198.Clear();
        names1b8.Clear();
    }

    bool IsOwnSlot(string name) => name != null && (bagSlots.Contains(name) || names198.Contains(name) || names1b8.Contains(name));

    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == Bag ? new List<string>(bagSlots) : Array.Empty<string>();

    public override object GetBinding(string name)
    {
        if (IsOwnSlot(name) && Model.ItemSlots.TryGetValue(name, out var slot)) return slot.Value(Host, Model, Model.ItemSlotSources);
        return base.GetBinding(name);
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (IsOwnSlot(name) && Model.ItemSlots.TryGetValue(name, out var slot) && slot.Item != null)
        {
            var tip = new ItemTooltip(Model, 0x20) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
            return tip.Fill(slot.Item) ? tip.Data : new AsTooltip();
        }
        return base.GetSlotTooltip(name);
    }

    // ---- the statics (commonfiles/itemremakescrolldata.cpp) ----------------------------------------------------------

    /// <summary>FUN_140616b60 (@0x140616b60, :361): the bigger of the game rule's sale and <paramref name="rate"/>; 0 none, 1 the
    /// game rule's, 2 the rate's.</summary>
    public static int Sale(uint sale, uint rate, out uint discount)
    {
        discount = sale > rate ? sale : rate;
        if (discount == 0) return 0;
        return discount == sale ? 1 : 2;
    }

    /// <summary>GetDiscountedIngredientCount (@0x140616d40, :397): the discount capped by IngredientFeeReduceLimit
    /// (FUN_1406198a0(10000, limit)), the count scaled by FUN_140482050 (gamerule.cpp:360), at least 1.</summary>
    public static int DiscountedIngredientCount(int count, uint discount, float limit)
    {
        uint cap = (uint)(int)(10000 * (double)limit + 1e-06);
        if (discount >= cap) discount = cap;
        uint r = (uint)((uint)count * (1.0 - discount / 10000.0) + 1e-06);
        return r < 1 ? 1 : (int)r;
    }

    static bool IsArmor(ItemData d) => d.EquipSubtype is (>= 0xd and <= 0x11) or 0x16;                // CItemData::FUN_14053d600
    static bool IsAccessory(ItemData d) => d.EquipSubtype is 0xc or (>= 0x12 and <= 0x15);           // CItemData::FUN_14053d5c0

    /// <summary>GetLockItemId (@0x140616dd0, :407): the lock tag of the item's kind (armour 0x66, weapon 0x65, accessory 0x67,
    /// pet 0x68), 0 when the kind has none.</summary>
    public static int LockItemId(Item item, ItemModelTable models)
    {
        var d = item?.Data;
        if (d == null) return 0;
        if (IsArmor(d)) return 0x66;
        if (WornEquipment.IsWeapon(d, models)) return 0x65;                     // FUN_14053d2f0
        if (IsAccessory(d)) return 0x67;
        if (d.IsPetItem) return 0x68;
        return 0;
    }

    /// <summary>GetLockItemLevel (@0x140616f10, :426): with feature 0xa5 the item's rarity, a pet's 0.</summary>
    public static int LockItemLevel(Item item, Func<int, bool> feature)
    {
        if (item == null || !feature(0xa5)) return 0;
        if (item.Data.IsPetItem) return 0;
        return item.Rarity;
    }

    /// <summary>CanLockOptions (@0x140616fd0, :437): the feature of the item's kind (armour 0x8a, weapon 0x89, accessory 0x8b,
    /// pet 0x9b).</summary>
    public static bool CanLockOptions(Item item, ItemModelTable models, Func<int, bool> feature)
    {
        var d = item?.Data;
        if (d == null) return false;
        if (IsArmor(d)) return feature(0x8a);
        if (WornEquipment.IsWeapon(d, models)) return feature(0x89);
        if (IsAccessory(d)) return feature(0x8b);
        if (d.IsPetItem) return feature(0x9b);
        return false;
    }

    /// <summary>FUN_140619760 (@0x140619760, :918): the lock item's name (the tag's item, FUN_141811980(1, id)) in the colour of
    /// the level, or of the item's own rank (GetItemRank) for 0; "" for no lock.</summary>
    public string LockItemText(int lockId, int level)
    {
        if (lockId == 0) return "";
        int id = Model.Tables.Items.TagItemId(lockId);
        int rank = level != 0 ? level : ItemTooltip.ItemRank(Model.Tables.Items, id);
        return Colored(Model.Tables.ItemName(id), rank);
    }

    string Colored(string name, int rank) => $"<FONT color='#{Model.Tables.Items.TextColor(rank):x}'>{name}</FONT>";

    /// <summary>CItem::FUN_140528160 (item.cpp:2313): the item's name (FUN_140527b10) in its rarity's colour (FUN_14183e9c0).</summary>
    string ColoredName(Item item) => Colored(Tip.Name(item), item.Rarity);

    int LockId(Item item) => LockItemId(item, Model.Tables.ItemModels);
    int LockLevel(Item item) => LockItemLevel(item, Model.IsFeatureEnabled);

    /// <summary>FUN_1412a1c80 (:932): the have-count of an item's own stringTag (FUN_140a5f7b0(tag, 0)); 0 without a tag.</summary>
    int HaveOf(Item item)
    {
        if (item?.Data == null) return 0;
        int tag = ItemTags.ByName(item.Data.StringTag);
        return tag != 0 ? CountByTag(tag, 0) : 0;
    }

    /// <summary>The rate of the equipment's ability 0xa9 as the sale's second rate: (uint)(rate * 10000).</summary>
    uint AbilitySale() => (uint)(long)(AbilityRate() * 10000.0f);

    // ---- the price -------------------------------------------------------------------------------------------------

    /// <summary>
    /// CanPayRemakePrice (@0x1412a0940, :775): each ingredient slot's item (its count, discounted when there is a sale) against its
    /// tag's have-count; a short meso row (tag 0x60) sets the fee (assigned, not summed), any other short row fails with
    /// s_itemremake_error_server_lack_price; then the meso against the fee.
    /// </summary>
    public bool CanPayRemakePrice(Item item)
    {
        if (item == null) return false;
        int sale = Sale(SaleRate(item.Data), AbilitySale(), out uint discount);
        long fee = 0;
        foreach (var n in names1b8)
        {
            if (!Model.ItemSlots.TryGetValue(n, out var slot)) continue;                // FUN_1412a0900
            var si = slot.Item;
            if (si?.Data == null) continue;
            int need = (int)si.Count;
            int price = sale != 0 ? DiscountedIngredientCount(need, discount, Model.Tables.IngredientFeeReduceLimit) : need;
            int have = HaveOf(si);
            if (have < price)
            {
                if (ItemTags.ByName(si.Data.StringTag) == MesoTag)
                {
                    fee = (long)(price - have) * FeePerUnit;
                    continue;
                }
                Notice("s_itemremake_error_server_lack_price");                        // FUN_1412752d0(key, 1)
                return false;
            }
        }
        if (Meso() < fee)
        {
            Notice("s_itemremake_error_server_lack_price");
            return false;
        }
        return true;
    }

    /// <summary>GetIngredientCountText: have / need, the lack variant under.</summary>
    public string IngredientCountText(int have, int need) =>
        have < need ? Fmt("s_common_cur_max_ingredient_lack", have, need) : Fmt("s_common_cur_max_ingredient", have, need);

    /// <summary>
    /// FUN_1412a0e30 (:823): one entry per ingredient (its id, else the tag's item, at GetItemRank, InitWithCount with the
    /// undiscounted count): its slot "{type}.ingredient.{i}" and the have / need text; a short tag 0x60 row sets the fee. Then
    /// saleDesc (s_itemremake_sale_desc for the game rule's sale, s_itemremake_sa_sale_desc for the ability's, in percent) and,
    /// with a fee, remakeFee.
    /// </summary>
    void AddIngredients(List<(string, object)> m, Item item, List<EnchantIngredient> ingredients)
    {
        if (item?.Data == null) return;
        int sale = Sale(SaleRate(item.Data), AbilitySale(), out uint discount);
        long fee = 0;
        var list = new List<object>();
        for (int i = 0; i < (ingredients?.Count ?? 0); i++)
        {
            var g = ingredients[i];
            if (g.ItemId == 0 && g.Tag == 0) continue;                                // CIngredientInfo::IsEmpty
            int itemId = g.ItemId != 0 ? g.ItemId : Model.Tables.Items.TagItemId(g.Tag);
            var it = Item.Create(Model.Tables.Items, itemId, ItemTooltip.ItemRank(Model.Tables.Items, itemId));
            if (it == null) continue;                                                 // InitWithCount fails
            it.SetCountClamped(g.Count);
            int have = HaveOf(it);
            string key = IngredientPrefix + i.ToString(CultureInfo.InvariantCulture);
            list.Add(Host.Obj(("binding", BindItemToSlot(it, key, names1b8)), ("countText", IngredientCountText(have, g.Count))));
            if (g.Tag == MesoTag && g.Count > have) fee = (long)(g.Count - have) * FeePerUnit;
        }
        string saleDesc = sale == 1 ? Fmt("s_itemremake_sale_desc", discount * 0.01f)
            : sale == 2 ? Fmt("s_itemremake_sa_sale_desc", discount * 0.01f) : "";
        m.Add(("saleDesc", saleDesc));
        m.Add(("ingredient", list));
        if (fee > 0) m.Add(("remakeFee", Fmt("s_itemenchant_fee", AchieveTexts.Group(fee))));
    }

    // ---- the callbacks -----------------------------------------------------------------------------------------------

    /// <summary>FUN_140718350: the movie's uid string as the client parses it (_wcstoui64).</summary>
    static long ParseUid(object a) => unchecked((long)Ms2.Formats.World.ReservedWords.ParseUInt64(a?.ToString() ?? ""));

    /// <summary>vfunc_28 (@0x14129d4d0, :259). onClickForceRemake and onClickCompareEnchant are the debug buttons (not ported).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onselectitem":
                if (args.Length > 0) SendChangeAttributesMode4(ParseUid(args[0]));
                return true;
            case "onclickremake":
                if (args.Length > 0) OnClickRemake(ParseUid(args[0]), args.Length > 1 ? Rt.ToInt32(args[1]) : 0);
                return true;
            case "onendremakeani":
                if (Ani != null) TalkModify?.Invoke(0x66);                          // OnEndRemakeAni (@0x14129f6f0)
                return true;
            case "onclickapply":
                if (args.Length > 0) OnClickApply(Rt.ToInt32(args[0]));
                return true;
            case "getenablelockoption":
                if (args.Length > 0) GetEnableLockOption(args[0]);
                return false;
        }
        return false;
    }

    /// <summary>SendChangeAttributesMode4 (@0x14129f740, :602): before the open answer nothing; an item that is gone or can not
    /// be remade gives its notice (the latter also clears the slot); else mode 4 with the uid.</summary>
    public void SendChangeAttributesMode4(long uid)
    {
        if (State == 0) return;
        var item = FindItem(uid);
        if (item == null) { Notice("s_itemremake_error_server_not_in_inven"); return; }
        if (!ItemTooltip.CanRemakeOption(item, RemakeMode))
        {
            Notice("s_itemremake_error_server_impossible");
            ReleaseSlotKeys();
            if (Ani != null) Host.Invoke(Ani, "initEnchantSlot");
            return;
        }
        Send(ChangeAttributesRequest.Select(item.Uid));
    }

    /// <summary>
    /// OnClickRemake (@0x14129e950, :487): no option to lock starts the remake (StartRemake with the price test). With one, the
    /// item must be held, the price paid and the kind have a lock item; then s_itemremake_ask_lock_item_option asks, and a yes
    /// (OnLockOptionConfirm @0x1412a2a50) sends without the price test.
    /// </summary>
    public void OnClickRemake(long uid, int option)
    {
        if (option < 0) { StartRemake(uid, option, true); return; }
        var item = FindItem(uid);
        if (item?.Data == null) { Notice("s_itemremake_error_server_not_in_inven"); return; }
        if (!CanPayRemakePrice(item)) return;
        int lockId = LockId(item);
        if (lockId == 0) { Notice("s_itemremake_error_server_impossible"); return; }
        string ask = Fmt("s_itemremake_ask_lock_item_option", LockItemText(lockId, LockLevel(item)));
        var box = MessageBoxQueue.CreateWithCallback(ask, 1, (b, _) =>                   // ShowYesNoMessageBox
        {
            if (b == 0 && !closing) SendRemakeRequest(uid, option, false);
            return true;
        });
        box.Append(uid);
        box.Append(option);
        ShowData(box);
    }

    /// <summary>
    /// StartRemake (@0x1412a1d40, :944): the item must be held and remakeable, and with <paramref name="confirm"/> the price paid. An
    /// item of rarity 5 or more with a high roll (FUN_1409e4950) asks first in box 0x11 with the item, its high-roll rows and
    /// s_itemremake_ask_high_sa_item_option (buttons 0x475 / 0x476); a yes sends (FUN_1412a0650 @0x1412a0650), a no clears the
    /// pick. Else it sends at once.
    /// </summary>
    public void StartRemake(long uid, int option, bool confirm)
    {
        var item = FindItem(uid);
        if (item?.Data == null) { Notice("s_itemremake_error_server_not_in_inven"); return; }
        if (!ItemTooltip.CanRemakeOption(item, RemakeMode)) { Notice("s_itemremake_error_server_impossible"); return; }
        if (confirm && !CanPayRemakePrice(item)) return;
        string highSa = Tip.RemakeHighRollText(item, out bool best);
        if (item.Rarity >= 5 && best)
        {
            var box = MessageBoxQueue.Create(0x11, highSa + "\n" + T("s_itemremake_ask_high_sa_item_option"), null, "");
            box.Callback = (b, _) =>
            {
                if (closing) return true;
                if (b == 0) SendRemakeRequest(uid, option, confirm);
                else
                {
                    ResetSelection();
                    ReleaseSlotKeys();
                    if (Ani != null) Host.Invoke(Ani, "initEnchantSlot");
                }
                return true;
            };
            box.ItemUid = item.Uid;
            box.SetItemEntry(item, Tip.Name(item), 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
            box.SetButtonLabelIds(0x475, 0x476, 0);
            box.Append(uid);
            box.Append(option);                                                        // FUN_140f73320(box, bConfirm): the closure
            ShowData(box);
            return;
        }
        SendRemakeRequest(uid, option, confirm);
    }

    /// <summary>
    /// SendRemakeRequest (@0x1412a2380, :988): the item checks of StartRemake again; with an option to lock, more than one row, a
    /// lock item kind and one held (FUN_140a5f7b0(lock, level) &gt; 0) are needed, and the option's stat or special
    /// (Unk_RemakeStat::FUN_140619290) is locked. Then mode 0, input off, state 3.
    /// </summary>
    public void SendRemakeRequest(long uid, int option, bool confirm)
    {
        var item = FindItem(uid);
        if (item?.Data == null) { Notice("s_itemremake_error_server_not_in_inven"); return; }
        if (!ItemTooltip.CanRemakeOption(item, RemakeMode)) { Notice("s_itemremake_error_server_impossible"); return; }
        if (confirm && !CanPayRemakePrice(item)) return;
        var locks = new List<RemakeLock>();
        if (option >= 0)
        {
            if (OptionCount <= 1) { NoticeBox(T("s_itemremake_notice_disable_lock_item_option_error_count")); return; }
            int lockId = LockId(item);
            if (lockId == 0) { Notice("s_itemremake_error_server_impossible"); return; }
            int level = LockLevel(item);
            if (CountByTag(lockId, level) <= 0)
            {
                NoticeBox(Fmt("s_itemremake_notice_disable_lock_item_option_when_click_remake", LockItemText(lockId, level)));
                return;
            }
            if (Tip.RemakeOptionLock(item, RemakeMode, option, out int index, out int value))
                locks.Add(new RemakeLock((byte)index, (short)value));
        }
        Send(ChangeAttributesRequest.Remake(uid, locks.ToArray()));
        SetInputEnabled(false);
        State = 3;
    }

    /// <summary>
    /// OnClickApply (@0x14129ed90, :520): state 4; select -1 fails with its notice. The new item sends mode 2 with its uid, the old
    /// one mode 3 [1]; the movie's selectComplete gets the picked item's options and slot, and the client stores the picked item
    /// at once (FUN_1412a1650, before the server's kind 2), fills the bag again, talks 0x67 and keeps the pick only after mode 2.
    /// A null pick crashes the client (picked-&gt;GetItemUid); here it does nothing.
    /// </summary>
    public void OnClickApply(int select)
    {
        if (Ani == null) return;
        State = 4;
        if (select == -1) { Notice("s_itemremake_error_server_fail_apply_option"); return; }
        var picked = select == 0 ? Cur : New;
        if (picked == null) return;
        bool applied = false;
        if (ReferenceEquals(picked, New))
        {
            Send(ChangeAttributesRequest.Apply(picked.Uid));
            applied = true;
        }
        else Send(ChangeAttributesRequest.Keep());
        string final = Tip.RemakeOptionText("s_itemremake_final_random_option", picked, RemakeMode, true, out _);
        Host.Invoke(Ani, "selectComplete", Host.Obj(("remakeStatFinal", final), ("binding", BindItemToSlot(picked, ResultSlot, names198))));
        Store(picked);
        FillBag();
        TalkModify?.Invoke(0x67);
        PlaySound?.Invoke("Enchant_SuccessEffect");
        if (!applied) New = Cur = null;
    }

    /// <summary>GetEnableLockOption (@0x14129f360, :566): the movie's object gets value: the lock can be used (more than one row, a
    /// lock item kind and one held); else a notice box says why.</summary>
    void GetEnableLockOption(object result)
    {
        if (Ani == null || Cur == null) return;
        if (result == null || result is Undefined) return;
        bool enable = false;
        if (OptionCount <= 1) NoticeBox(T("s_itemremake_notice_disable_lock_item_option_error_count"));
        else
        {
            int lockId = LockId(Cur);
            if (lockId != 0)
            {
                int level = LockLevel(Cur);
                if (CountByTag(lockId, level) > 0) enable = true;
                else NoticeBox(Fmt("s_itemremake_notice_disable_lock_item_option", LockItemText(lockId, level)));
            }
        }
        Host.Set(result, "value", enable);
    }

    /// <summary>The lock test of the movie's lock button, as the movie asks it (GetEnableLockOption's value).</summary>
    public bool LockEnabled()
    {
        var o = Host.Obj(("value", false));
        GetEnableLockOption(o);
        return Rt.ToBoolean(Host.Get(o, "value"));
    }

    /// <summary>OnGameEventChanged (@0x14129dc60, :355): a SaleRemake change with an item picked (state 2) asks for it again.</summary>
    public void OnGameEventChanged(string eventName)
    {
        if (Ani == null || State != 2 || Cur == null) return;
        if (eventName == "SaleRemake") SendChangeAttributesMode4(Cur.Uid);
    }

    /// <summary>ResetSelection (@0x1412a2ac0, :1050).</summary>
    void ResetSelection()
    {
        Cur = New = null;
        OptionCount = 0;
    }

    // ---- the answers -------------------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_79 (@0x14129daa0, :322): without the movie nothing; else input on and the kind's handler. <paramref name="readBody"/>
    /// reads the item body that follows (CItem::ReadFromStream into a copy of the given record); kind 8 is the debug resend.
    /// </summary>
    public void OnPacket(ChangeAttributesPacket p, Func<EquipItem, EquipItem> readBody)
    {
        if (Host?.Entity == null || closing || Ani == null) return;
        SetInputEnabled(true);
        switch (p.Kind)
        {
            case 6: OnLastResult(p, readBody); break;
            case 4: OnItemAccepted(p); break;
            case 1: OnRemakeResult(p, readBody); break;
            case 2: OnApplied(p, readBody); break;
            case 5: OnRemakeError(p); break;
        }
    }

    /// <summary>A copy of <paramref name="item"/> (CItem::Clone) with the body read over its record.</summary>
    static Item Remade(Item item, Func<EquipItem, EquipItem> readBody)
    {
        var n = item.Clone();
        if (readBody != null && item.Record != null) n.Record = readBody(item.Record);
        return n;
    }

    /// <summary>FUN_1412a2c50 (:1069): the remake left from before the window opened. 0: none; 2, or the item gone: its notice; an
    /// item that can not be remade: impossible; else the item is picked, the movie told its uid and the old result shown.</summary>
    void OnLastResult(ChangeAttributesPacket p, Func<EquipItem, EquipItem> readBody)
    {
        if (p.Result == 0) { State = 1; return; }
        if (p.Result == 2) { ShowPrevOptionItemNotExist(p.ItemId); State = 1; return; }
        var item = FindItem(p.Uid);
        if (item == null) { ShowPrevOptionItemNotExist(p.ItemId); State = 1; return; }
        if (!ItemTooltip.CanRemakeOption(item, 2))
        {
            Notice("s_itemremake_error_server_impossible");
            State = 1;
            return;
        }
        State = 3;
        Cur = item;
        Host.Invoke(Ani, "setLastItemGUID", Host.Obj(("lastItemGUID", item.Uid.ToString(CultureInfo.InvariantCulture))));
        New = Remade(Cur, readBody);
        SetRemakeCount(Cur, New);
        ShowRemakeResult(Cur, New);
    }

    /// <summary>CItem::SetRemakeCount(next's): the held item's count follows the remade copy, on the held item itself (m_cur is the
    /// bag's CItem), after the server's answer.</summary>
    static void SetRemakeCount(Item cur, Item next)
    {
        if (cur.Record == null || next.Record == null || ReferenceEquals(cur.Record, next.Record)) return;
        cur.Record.RemakeCount = next.Record.RemakeCount;
    }

    /// <summary>ShowPrevOptionItemNotExist (@0x1412a2b40, :1060): a type 0 box and a chat line (0x14) naming the item.</summary>
    void ShowPrevOptionItemNotExist(int itemId)
    {
        string text = Fmt("s_itemremake_error_prev_option_item_not_exist", Model.Tables.ItemName(itemId));
        ShowData(MessageBoxQueue.Create(0, text, null, ""));
        ChatLine?.Invoke(text, 0x14);
    }

    /// <summary>
    /// FUN_14129f940 (:626): the server accepted the pick. The pick resets and state is 2; an item that is gone or can not be
    /// remade gives its notice; else it is picked and playEnchant gets its name, remake count, slot, ingredients, options and the
    /// lock flag; then talk 100 and the choice sound. The debug buttons stay hidden (not debug mode).
    /// </summary>
    void OnItemAccepted(ChangeAttributesPacket p)
    {
        ResetSelection();
        State = 2;
        var item = FindItem(p.Uid);
        if (item == null) { Notice("s_itemremake_error_server_not_in_inven"); return; }
        if (!ItemTooltip.CanRemakeOption(item, RemakeMode))
        {
            Notice("s_itemremake_error_server_impossible");
            ReleaseSlotKeys();
            Host.Invoke(Ani, "initEnchantSlot");
            return;
        }
        ReleaseSlotKeys();
        Cur = item;
        var m = new List<(string, object)>
        {
            ("itemName", ColoredName(item)),
            ("remakeCount", Fmt("s_itemremake_remake_count", item.Record?.RemakeCount ?? 0)),
            ("enchantItem", BindItemToSlot(item, SelectedSlot, names198)),            // FUN_1412a0cd0
        };
        AddIngredients(m, item, p.Ingredients);
        string option = Tip.RemakeOptionText("s_itemremake_cur_random_option", item, RemakeMode, true, out int count);
        bool enableLock = CanLockOptions(item, Model.Tables.ItemModels, Model.IsFeatureEnabled);
        OptionCount = count;
        m.Add(("option", option));
        m.Add(("optionCount", (uint)count));
        m.Add(("guid", p.Uid.ToString(CultureInfo.InvariantCulture)));                 // FUN_140719530
        m.Add(("enableLock", enableLock));
        Host.Invoke(Ani, "playEnchant", Host.Obj(m.ToArray()));
        TalkModify?.Invoke(100);
        PlaySound?.Invoke("Enchant_ItemChoice");
        if (Host.Child("remakeOptionInfo", Ani) is { } info)
            foreach (var n in new[] { "m_debugBtnFillIngredientUnit", "m_debugBtnFillIngredient" })
                if (Host.Child(n, info) is { } b) Host.Set(b, "visible", false);
    }

    /// <summary>FUN_1412a30f0 (:1110): the remade copy of the picked item; with the item still held the old and new options are
    /// shown. No pick crashes the client (m_cur-&gt;Clone); here it does nothing.</summary>
    void OnRemakeResult(ChangeAttributesPacket p, Func<EquipItem, EquipItem> readBody)
    {
        if (Cur == null) return;
        New = Remade(Cur, readBody);
        SetRemakeCount(Cur, New);
        if (FindItem(p.ApplyUid) == null) { Notice("s_itemremake_error_server_not_in_inven"); return; }
        ShowRemakeResult(Cur, New);
    }

    /// <summary>FUN_1412a3330 (:1131): the applied item, read over the new copy; when its uid is the new one's it is stored and the
    /// pick ends.</summary>
    void OnApplied(ChangeAttributesPacket p, Func<EquipItem, EquipItem> readBody)
    {
        if (New == null) return;
        var applied = Remade(New, readBody);
        New = applied;
        if (p.ApplyUid != New.Uid) return;
        Store(New);
        New = Cur = null;
    }

    /// <summary>ShowRemakeResult (@0x1412a1790, :897): the name, count, result slot and the old and new options (index 0
    /// selected), then the progress sound.</summary>
    void ShowRemakeResult(Item cur, Item next)
    {
        string stat0 = Tip.RemakeOptionText("s_itemremake_cur_random_option", cur, RemakeMode, true, out _);
        string stat1 = Tip.RemakeOptionText("s_itemremake_new_random_option", next, RemakeMode, true, out _);
        Host.Invoke(Ani, "setEnchantResult", Host.Obj(
            ("itemName", ColoredName(cur)),
            ("remakeCount", Fmt("s_itemremake_remake_count", cur.Record?.RemakeCount ?? 0)),
            ("binding", BindItemToSlot(cur, ResultSlot, names198)),
            ("remakeStat0", stat0),
            ("remakeStat1", stat1),
            ("selectedStatIndex", 0)));
        PlaySound?.Invoke("Enchant_Progress");
    }

    /// <summary>OnRemakeError (@0x1412a34f0, :1152): a remake out (state 3) with flag 0 goes back to the pick (2); the code's
    /// notice is formatted with the flag and the code.</summary>
    void OnRemakeError(ChangeAttributesPacket p)
    {
        if (State == 3 && p.Flag == 0) State = 2;
        string key = p.Code switch
        {
            1 => "s_itemremake_error_server_not_in_inven",
            2 => "s_itemremake_error_server_impossible",
            3 or 4 => "s_itemremake_error_server_null_status",
            5 => "s_itemremake_error_server_lack_price",
            6 or 7 => "s_itemremake_error_server_fail_apply_option",
            9 => "s_itemremake_error_server_fail_lack_lock_consume_item",
            10 => "s_content_shutdown_notice",
            _ => "s_itemremake_error_server_default",
        };
        NoticeText?.Invoke(Fmt(key, (int)p.Flag, p.Code));
    }

    // ---- IDragSlotOwner ----------------------------------------------------------------------------------------------

    public bool IsDragSlot(string name) => IsOwnSlot(name);
    public void RefreshSlot(string name) { if (IsDragSlot(name)) Host.Push(name); }
    public void SlotDragStarted(string name) { }
    public void SlotDropUnresolved(string name) => RefreshSlot(name);
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner) => sourceOwner?.RefreshSlot(source);
}
