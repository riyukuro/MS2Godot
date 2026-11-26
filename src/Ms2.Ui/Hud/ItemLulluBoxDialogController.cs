using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIItemLulluBoxDialog (client/ms2/uiitemlulluboxdialog.cpp, UI id 0x4f2b, type name "ItemLulluBoxDialog", movie
/// uiitemlulluboxdialog): the Lullu box of an OpenItemBoxLullu / OpenItemBoxLulluSimple item (@0x140d22390 gives it the item id
/// and opens it). The item's function xml names the key and box item tags, the box price, the drop boxes and the theme; the
/// open buttons check the keys (and boxes, or the mesos of the auto pay) and send the key item's use with "single" / "multi"
/// (plus "autoPay"); the server's ItemScript mode 0 shows the result.
/// STILL-OPEN: PC::CanUseItem's skip in the tag count and find (as in the bag count); the web guide button (FUN_1419260d0).
/// </summary>
public sealed class ItemLulluBoxDialogController : UiController
{
    public override string Movie => "uiitemlulluboxdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemLulluBoxDialog";   // GetTypeName (@0x140e9ac70)

    public const int UiId = 0x4f2b;
    public const string SimpleFunction = "OpenItemBoxLulluSimple", BoxFunction = "OpenItemBoxLullu";
    /// <summary>The bags' icon type and flags (CreateParam and CreateItemInfoSlot: 0x415, 0x800).</summary>
    public const int IconType = 0x415, SlotFlags = 0x800;
    /// <summary>The item break dialog (OnOpenItemBreak: OpenDialog(0x4e8d)) and the shop link box's dialog (FUN_140e9c260: 0x4ec1).</summary>
    public const int ItemBreakDialogId = 0x4e8d, ShopLinkDialogId = 0x4ec1;
    /// <summary>The style shop tab (OnOpenStyleShop: ToggleShopTab(9)).</summary>
    public const int StyleShopTab = 9;
    public const string OpenSound = "LulluOpenBox_Open", SpecialSound = "LulluOpenBox_SpecialItem_Get", TenSound = "LulluOpenBox_10Items_Open";
    public const string NotEnoughMoneyKey = "s_lullu_key_not_enough_money", NotEnoughBoxKey = "s_lullu_key_not_enough_box",
        ShopLinkKey = "s_lullu_key_not_enough_item_shop_link", NotEnoughItemKey = "s_lullu_key_not_enough_item",
        SingleTooltipKey = "s_lullu_key_single_open_tooltip", MultiTooltipKey = "s_lullu_key_multi_open_tooltip";

    /// <summary>m_bagId: the preview bag's id. The ctor (@0x140e9a970) default-constructs it and nothing sets it, so it is "".</summary>
    public const string BagId = "";

    int boxItemId;                                 // m_boxItemId
    bool multi, simple;                            // m_multi, m_simple
    /// <summary>m_sent: the use went out and its result has not come back.</summary>
    public bool Sent { get; private set; }
    public bool Simple => simple;
    readonly List<ItemScriptBoxItem> result = new();                    // m_result
    readonly List<string> slotBinds = new();                            // m_slotBinds
    readonly HashSet<(int, int, int)> special1 = new(), special2 = new();   // m_special1, m_special2 (Unk_GachaLess: id, rank, count)
    readonly List<string> bagSlots = new();
    object title, singleOpen, multiOpen, itemBreak, styleShop, skipEffect, autoPay, itemBox, resultItem;

    /// <summary>The bag's count of the items with a string tag (Unk_Inventory::FUN_140a5f7b0(tag, 0)).</summary>
    public Func<int, int> TagCount = _ => 0;
    /// <summary>Unk_Inventory::FindItemByTag.</summary>
    public Func<int, Item> FindByTag = _ => null;
    /// <summary>FUN_140a63880(item, mode): the use request with the argument; false when it was not sent.</summary>
    public Func<Item, string, bool> RequestUse = (_, _) => false;
    /// <summary>The local player's job group and gender; null without one.</summary>
    public Func<(int JobGroup, int Gender)?> Self = () => null;
    /// <summary>DAT_143ab2898: the mesos.</summary>
    public Func<long> Mesos = () => 0;
    /// <summary>PlaySystemSound(key, "").</summary>
    public Action<string> PlaySound = _ => { };
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public event Action<string> NoticeText;
    /// <summary>FUN_140d73c60 / ShowMessageBox: shows a box.</summary>
    public Action<MessageBoxData> ShowBox = _ => { };
    /// <summary>CUIService::OpenDialog(id).</summary>
    public Action<int> OpenDialog = _ => { };
    /// <summary>ToggleShopTab(tab).</summary>
    public Action<int> ToggleShopTab = _ => { };
    /// <summary>CReservedWordTable::ReplaceObjectAndValueWords.</summary>
    public Func<string, string> ReplaceWords = s => s;

    public int BoxItemId => boxItemId;
    public IReadOnlyList<string> SlotBinds => slotBinds;
    public IReadOnlyList<string> PreviewSlots => bagSlots;

    /// <summary>SetBoxItemId @0x140e9c240.</summary>
    public void SetBoxItemId(int id) => boxItemId = id;

    ItemData Data() => Model?.Tables.Items.Get(boxItemId);

    /// <summary>The function xml GetIntArray (@0x1409e8620 -> FUN_140719090): the value split at ',' as ints, empty when missing.</summary>
    static List<int> XmlInts(ItemData d, string key) => d.HasFunctionXmlKey(key)
        ? d.FunctionXmlString(key).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ItemData.Atoi).ToList() : new List<int>();

    /// <summary>The function xml GetBool (@0x1409e8780 -> ParseBoolW @0x1407183f0: not empty and holding one of "1oOtT").</summary>
    static bool XmlBool(ItemData d, string key, bool fallback)
    {
        if (!d.HasFunctionXmlKey(key)) return fallback;
        string s = d.FunctionXmlString(key);
        return s.Length > 0 && s.IndexOfAny("1oOtT".ToCharArray()) >= 0;
    }

    /// <summary>The function xml GetInt(key, 0).</summary>
    static int XmlInt(ItemData d, string key) => d.HasFunctionXmlKey(key) ? d.FunctionXmlInt(key) : 0;

    static int Tag(ItemData d, string key) => ItemTags.ByName(d.FunctionXmlString(key));

    /// <summary>vfunc_67 @0x140e9acf0: UIDialog's close, the children dropped, the preview bag destroyed and the result slots released.</summary>
    public override void Close()
    {
        base.Close();
        title = singleOpen = multiOpen = itemBreak = styleShop = itemBox = resultItem = skipEffect = autoPay = null;
        DestroyBag();
        ReleaseSlotBinds();
    }

    /// <summary>
    /// OnLoadComplete @0x140e9adf0 (:163): no data for the box item closes it. The open buttons get OnSelectMode (single 0, multi
    /// 1); the item box group goes to the frame of the xml's boxTheme ("default" when missing, and again "default" when that frame
    /// has no m_itemBox), then setBoxTheme. The simple function makes the simple box; then the title, and the simple box's
    /// buttons or the auto pay.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Data() is not { } data) { Close(); return; }
        multi = Sent = simple = false;
        BindCloseButton("m_closeButton");   // UIDialog::BindCloseButton (@0x141925ec0): the X closes the dialog (vfunc_67); the web guide button (FUN_1419260d0) is STILL-OPEN (WebGuideTable)
        string theme = data.HasFunctionXmlKey("boxTheme") ? data.FunctionXmlString("boxTheme") : "default";
        title = Host.Child("m_title");
        if ((singleOpen = Host.Child("m_singleOpenButton")) != null) Click(singleOpen, () => OnSelectMode(0));
        if ((multiOpen = Host.Child("m_multiOpenButton")) != null) Click(multiOpen, () => OnSelectMode(1));
        if (Host.Child("m_itemBoxGroup") is { } group)
        {
            Host.Invoke(group, "gotoAndStop", theme);
            itemBox = Host.Child("m_itemBox", group);
            if (itemBox == null)
            {
                theme = "default";
                Host.Invoke(group, "gotoAndStop", theme);
                itemBox = Host.Child("m_itemBox", group);
            }
            Host.Invoke(Host.Entity, "setBoxTheme", theme);
        }
        resultItem = Host.Child("m_resultItem");
        skipEffect = Host.Child("m_isSkipEffect");
        autoPay = Host.Child("m_autoPayBox");
        if (data.FunctionName == SimpleFunction) simple = true;
        UpdateTitle();
        if (simple) InitSimpleBox();
        else UpdateAutoPay();
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the button's click handler is the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("lullu box dialog: " + name); return; }
        Click(b, Close);
    }

    void Click(object button, Action a)
    {
        Func<object[], object> f = _ => { a(); return null; };
        Host.Invoke(button, "addEventListener", "buttonClick", f, false, 0, false);   // ASButton::AddClickHandler
    }

    /// <summary>OnSelectMode @0x140e9c150: 0 single, 1 multi, then the open (<see cref="TryOpen"/>).</summary>
    public void OnSelectMode(int mode)
    {
        if (Host?.Entity == null) return;
        if (mode is not (0 or 1)) return;
        multi = mode == 1;
        TryOpen();
    }

    /// <summary>UpdateTitle @0x140e9c340: the box item's name.</summary>
    void UpdateTitle()
    {
        if (title != null) Host.Set(title, "text", Model.Tables.ItemName(boxItemId));
    }

    /// <summary>UpdateAutoPay @0x140e9c3e0: only OpenItemBoxLullu; the auto pay box shown when boxPrice is not negative; then the
    /// preview (<see cref="InitItemData"/>).</summary>
    void UpdateAutoPay()
    {
        if (Data() is not { } data || data.FunctionName != BoxFunction) return;
        if (autoPay != null) Host.SetVisible(autoPay, XmlInt(data, "boxPrice") >= 0);   // vfunc_3: the enabled/visible state
        InitItemData();
    }

    /// <summary>InitSimpleBox @0x140e9c550: the auto pay hidden; the item break and style shop buttons (itemBreakButton,
    /// styleShopButton, true when missing) shown with their handlers; the preview scroll shown; the window title and text from
    /// showWindowTitle / showWindowDesc (common string keys); then the preview.</summary>
    void InitSimpleBox()
    {
        if (autoPay != null) Host.SetVisible(autoPay, false);
        if (Data() is not { } data) return;
        bool breakOn = XmlBool(data, "itemBreakButton", true), styleOn = XmlBool(data, "styleShopButton", true);
        if (breakOn && (itemBreak = Host.Child("m_openItemBreak")) != null)
        {
            Host.SetVisible(itemBreak, true);
            Click(itemBreak, () => OpenDialog(ItemBreakDialogId));   // OnOpenItemBreak @0x140e9c1e0
        }
        if (styleOn && (styleShop = Host.Child("m_openStyleShop")) != null)
        {
            Host.SetVisible(styleShop, true);
            Click(styleShop, () => ToggleShopTab(StyleShopTab));      // OnOpenStyleShop @0x140e9c210
        }
        if (Host.Child("m_previewBagScroll") is { } scroll) Host.SetVisible(scroll, true);
        if (data.HasFunctionXmlKey("showWindowTitle") && Host.Child("m_showWindowTitle") is { } t)
            Host.Set(t, "text", Model.Tables.Common(data.FunctionXmlString("showWindowTitle")) ?? "");
        if (data.HasFunctionXmlKey("showWindowDesc") && Host.Child("m_showWindowDesc") is { } d)
            Host.Set(d, "text", Model.Tables.Common(data.FunctionXmlString("showWindowDesc")) ?? "");
        InitItemData();
    }

    /// <summary>
    /// The preview list of FUN_140e9d2b0 (@0x140e9d2b0, :463): the rows of the unCommonBoxId boxes, then (simple only) of the
    /// commonBoxId boxes (GetBoxItems(id, out) each), keeping the rows whose item has data, suits the job group and allows the
    /// gender. Not simple: each kept row is special (m_special1) as (id, rank, count).
    /// </summary>
    public static List<ItemDropEntry> PreviewDrops(GameTables t, ItemData data, int job, int gender)
    {
        var drops = new List<(ItemDropEntry Entry, bool ShowIconOnGachaUI)>();
        foreach (int id in XmlInts(data, "unCommonBoxId")) t.IndividualDrops.GetBoxItems(id, drops);
        if (data.FunctionName == SimpleFunction)
            foreach (int id in XmlInts(data, "commonBoxId")) t.IndividualDrops.GetBoxItems(id, drops);
        var kept = new List<ItemDropEntry>();
        foreach (var (d, _) in drops)
        {
            if (t.Items.Get(d.ItemId) is not { } idata) continue;
            if (!idata.IsJobRecommended(job)) continue;
            if (!idata.IsGenderAllowed(gender)) continue;
            kept.Add(d);
        }
        return kept;
    }

    /// <summary>FUN_140e9d2b0: no player or data, nothing. The preview's items (CItem::FUN_14052b130(id, rank) with the count)
    /// into the bag <see cref="BagId"/>, initItemData with its bagId, then the layout (<see cref="Layout"/>).</summary>
    bool InitItemData()
    {
        if (Self() is not var (job, gender)) return false;
        if (Data() is not { } data) return false;
        var kept = PreviewDrops(Model.Tables, data, job, gender);
        var items = new List<Item>();
        special1.Clear();
        foreach (var d in kept)
        {
            if (Item.Create(Model.Tables.Items, d.ItemId, d.Rank) is { } item)
            {
                item.SetCountClamped(d.Count);
                items.Add(item);
            }
            if (data.FunctionName != SimpleFunction) special1.Add((d.ItemId, d.Rank, d.Count));
        }
        DestroyBag();
        for (int i = 0; i < items.Count; i++)
        {
            string name = BagId + "." + i.ToString(CultureInfo.InvariantCulture);
            Model.ItemSlots[name] = new ItemInfoSlot(name, items[i], i, IconType, SlotFlags);
            bagSlots.Add(name);
        }
        Host.Invoke(Host.Entity, "initItemData", Host.Obj(("bagId", BagId)));
        Layout(items.Count);
        return true;
    }

    public override IEnumerable<string> GetBagSlotBindings(string bag) => bag == BagId ? bagSlots : Array.Empty<string>();

    void DestroyBag()
    {
        foreach (var n in bagSlots) Model?.ItemSlots.Remove(n);
        bagSlots.Clear();
    }

    /// <summary>
    /// FUN_140e9cde0 @0x140e9cde0 (:418): rows of 8 (2 to 3 shown), the preview bag 60 per row plus 10 between; the skip and auto
    /// pay boxes 6 below it, the open buttons 10 below the skip box, the background 0x10d taller than the bag; more than 3 rows
    /// moves the bag 6 left; 3 rows moves the dialog up by the height past 0x8c.
    /// </summary>
    void Layout(int count)
    {
        int rows = count / 8 + (count % 8 > 0 ? 1 : 0);
        bool tall = rows > 3;
        rows = Math.Clamp(rows, 2, 3);
        int h = rows * 0x3c + (rows - 1) * 0xa;
        if (Host.Child("m_previewItemBag") is not { } bag) return;
        Host.Set(bag, "height", (double)h);
        double y = Num(bag, "y") + Math.Ceiling(Num(bag, "height")) + 6.0;
        if (skipEffect != null) Host.Set(skipEffect, "y", y);
        if (autoPay != null) Host.Set(autoPay, "y", y);
        double y2 = skipEffect != null ? Num(skipEffect, "y") + Math.Ceiling(Num(skipEffect, "height")) + 10.0 : y;
        if (singleOpen != null) Host.Set(singleOpen, "y", y2);
        if (multiOpen != null) Host.Set(multiOpen, "y", y2);
        if (Host.Child("m_boxBG") is { } bg) Host.Set(bg, "height", (double)(h + 0x10d));
        if (tall) Host.Set(bag, "x", Num(bag, "x") - 6.0);
        if (rows > 2) Host.Set(Host.Entity, "y", Num(Host.Entity, "y") - (h - 0x8c));
    }

    double Num(object o, string member) => (float)Rt.ToNumber(Host.Get(o, member));

    /// <summary>
    /// FUN_140e9e8f0 @0x140e9e8f0 (:596): 4 no data or no key tag; 3 fewer keys than needed (10 for multi); not simple: 4 no box
    /// tag, and fewer boxes than needed: with the auto pay checked and a price not negative, 1 when the mesos do not cover the
    /// missing boxes, else 2. 0 when it can be opened.
    /// </summary>
    public int CanOpen()
    {
        if (Data() is not { } data) return 4;
        int keyTag = Tag(data, "keyItemTag"), boxTag = Tag(data, "boxItemTag");
        int price = XmlInt(data, "boxPrice");
        int need = multi ? 10 : 1;
        if (keyTag == 0) return 4;
        if (TagCount(keyTag) < need) return 3;
        if (!simple)
        {
            if (boxTag == 0) return 4;
            int haveBox = TagCount(boxTag);
            if (haveBox < need)
            {
                if (AutoPaySelected() && price >= 0)
                {
                    if (Mesos() < price * (need - haveBox)) return 1;
                }
                else return 2;
            }
        }
        return 0;
    }

    bool AutoPaySelected() => autoPay != null && Rt.ToBoolean(Host.Get(autoPay, "selected"));
    bool AutoPayVisible() => autoPay != null && Rt.ToBoolean(Host.Get(autoPay, "visible"));

    /// <summary>
    /// FUN_140e9ece0 @0x140e9ece0 (:632): 4 nothing; 1 the not enough money box; 3 the key's lack (<see cref="NotEnough"/>); 2
    /// with a price the box asking to buy the missing boxes (its yes checks the auto pay and tries again), without one the box's
    /// lack. 0: enableComponent(false), then with the skip effect checked the send, else the result hidden and the box's "open".
    /// </summary>
    public void TryOpen()
    {
        if (Host?.Entity == null) return;
        int r = CanOpen();
        var t = Model.Tables;
        if (r == 4) return;
        if (r == 1) { ShowBox(MessageBoxQueue.Create(0, t.Common(NotEnoughMoneyKey) ?? "", null, "")); return; }
        if (r == 3)
        {
            if (Data() is not { } d) return;
            NotEnough("key", t.Items.TagItemId(Tag(d, "keyItemTag")));
            return;
        }
        if (r == 2)
        {
            if (Data() is not { } d) return;
            int keyItem = t.Items.TagItemId(Tag(d, "keyItemTag"));
            int price = XmlInt(d, "boxPrice");
            int boxTag = Tag(d, "boxItemTag");
            int boxItem = t.Items.TagItemId(boxTag);
            int have = TagCount(boxTag), need = multi ? 10 : 1;
            if (have < need)
            {
                if (price >= 0)
                {
                    string msg = ReplaceWords(ClientFormat.Format(t.Common(NotEnoughBoxKey) ?? "", NumberText.Group(((long)price * (need - have)).ToString(CultureInfo.InvariantCulture)), keyItem, boxItem));
                    var box = MessageBoxQueue.Create(1, msg, null, "");
                    box.Callback = (button, _) =>
                    {
                        // FUN_140e9c2a0 @0x140e9c2a0: yes checks the shown auto pay box and tries again
                        if (button == 0)
                        {
                            if (autoPay != null && AutoPayVisible()) Host.Set(autoPay, "selected", true);
                            TryOpen();
                        }
                        return true;
                    };
                    ShowBox(box);
                }
                else NotEnough("box", boxItem);
            }
            return;
        }
        Host.Invoke(Host.Entity, "enableComponent", false);
        if (skipEffect != null && Rt.ToBoolean(Host.Get(skipEffect, "selected"))) SendOpenBox();
        else
        {
            if (resultItem != null) Host.SetVisible(resultItem, false);
            if (itemBox != null) Host.Invoke(itemBox, "gotoAndPlay", "open");
        }
    }

    /// <summary>FUN_140e9f870 @0x140e9f870: the xml's meratMarketPopup naming the kind ("key" / "box") shows the shop link box (its
    /// yes opens 0x4ec1); else the not enough item notice. Both name the item through the reserved words.</summary>
    void NotEnough(string kind, int itemId)
    {
        if (Data() is not { } d) return;
        var t = Model.Tables;
        if (d.FunctionXmlString("meratMarketPopup") == kind)
        {
            var box = MessageBoxQueue.Create(1, ReplaceWords(ClientFormat.Format(t.Common(ShopLinkKey) ?? "", itemId)), null, "");
            box.Callback = (button, _) => { if (button == 0) OpenDialog(ShopLinkDialogId); return true; };   // FUN_140e9c260
            ShowBox(box);
        }
        else NoticeText?.Invoke(ReplaceWords(ClientFormat.Format(t.Common(NotEnoughItemKey) ?? "", itemId)));
    }

    /// <summary>SendOpenBox @0x140e9f540: once until the result; <see cref="CanOpen"/> not 0 hides the result and stops the box on
    /// "idle"; else "single" / "multi" (plus "autoPay" when not simple and the shown auto pay is checked) as the use of the first
    /// key item (FindItemByTag); a use not sent closes the dialog.</summary>
    public void SendOpenBox()
    {
        if (Sent) return;
        string mode = multi ? "multi" : "single";
        if (CanOpen() != 0)
        {
            if (resultItem != null) Host.SetVisible(resultItem, false);
            if (itemBox != null) Host.Invoke(itemBox, "gotoAndStop", "idle");
            return;
        }
        if (!simple && AutoPayVisible() && AutoPaySelected()) mode += "autoPay";
        if (Data() is not { } data) return;
        if (FindByTag(Tag(data, "keyItemTag")) is not { } key) return;
        if (!RequestUse(key, mode)) Close();
        else Sent = true;
    }

    /// <summary>vfunc_28 @0x140e9bfd0 after UIDialog's: onSendOpenBox sends when it can be opened, else the result hidden and the
    /// box on "idle".</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onSendOpenBox", StringComparison.OrdinalIgnoreCase)) return false;
        if (CanOpen() == 0) SendOpenBox();
        else
        {
            if (resultItem != null) Host.SetVisible(resultItem, false);
            if (itemBox != null) Host.Invoke(itemBox, "gotoAndStop", "idle");
        }
        return false;
    }

    /// <summary>vfunc_45 @0x140e9bb90 getTooltip: not simple, the open buttons' text tooltips (align 5) naming the key and the box.</summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (simple) return null;
        string which = args is { Length: > 0 } ? Rt.ToString(args[0]) : "";
        if (Data() is not { } data) return null;
        var t = Model.Tables;
        string keyName = t.ItemName(boxItemId), boxName = t.ItemName(t.Items.TagItemId(Tag(data, "boxItemTag")));
        string key = which == "m_singleOpenButton" ? SingleTooltipKey : which == "m_multiOpenButton" ? MultiTooltipKey : null;
        if (key == null) return null;
        var tip = new AsTooltip();
        tip.SetTooltipAlign(5);
        return tip.ApplyTextTooltip(ClientFormat.Format(t.Common(key) ?? "", keyName, boxName)) ? tip : null;
    }

    /// <summary>IsSpecialItem @0x140e9d1e0: the item is in either special set (id, rank, count).</summary>
    bool IsSpecial(ItemScriptBoxItem e) => special1.Contains((e.ItemId, e.Rank, e.Count)) || special2.Contains((e.ItemId, e.Rank, e.Count));

    /// <summary>
    /// vfunc_79 @0x140e9b670 for ItemScript: loaded only; m_sent cleared; mode 0 keeps the items (the packet's special ones in
    /// m_special2), binds them, plays "result" on the box, the open sound (and the ten sound for more than one), and the firework
    /// shown and played with the special sound when any item is special.
    /// </summary>
    public void OnResult(ItemScriptBoxResult r)
    {
        if (Host?.Entity == null || IsClosed) return;
        Sent = false;
        if (r == null || r.Mode != ItemScriptBoxResult.LulluMode) return;
        result.Clear();
        special2.Clear();
        bool hasSpecial = false;
        foreach (var it in r.Items)
        {
            result.Add(it);
            if (it.Special) special2.Add((it.ItemId, it.Rank, it.Count));
            if (IsSpecial(it)) hasSpecial = true;
        }
        BindResult();
        if (itemBox != null) Host.Invoke(itemBox, "gotoAndPlay", "result");
        PlaySound(OpenSound);
        if (result.Count > 1) PlaySound(TenSound);
        if (itemBox != null && Host.Child("m_fireworkEffect", itemBox) is { } fw)
        {
            Host.SetVisible(fw, hasSpecial);
            if (hasSpecial)
            {
                Host.Invoke(fw, "gotoAndPlay", 1);
                PlaySound(SpecialSound);
            }
        }
    }

    /// <summary>FUN_140e9dc30 @0x140e9dc30: one item in m_singleItem (ItemLullu_ItemLulluBoxDialog_1, "{0} X {1}", the uncommon effect
    /// for a special one), SetMultiType(false); more in m_multi1..10 (ItemLullu_ItemLulluBoxDialog_i, the effect shown and played
    /// for a special one; slots past the result get ""), SetMultiType(true).</summary>
    void BindResult()
    {
        if (result.Count == 0 || resultItem == null) return;
        ReleaseSlotBinds();
        var t = Model.Tables;
        if (result.Count == 1)
        {
            if (Host.Child("m_singleItem", resultItem) is not { } single) return;
            if (Host.Child("m_itemSlot", single) is not { } slot) return;
            if (Host.Child("m_itemName", single) is not { } label) return;
            var e = result[0];
            if (Item.Create(t.Items, e.ItemId, e.Rank) is not { } item) return;
            if (Host.Child("m_uncommonEffect", single) is { } unc) Host.SetVisible(unc, IsSpecial(e));
            item.SetCountClamped(e.Count);
            string key = $"ItemLullu_{TypeName}_1";
            Model.ItemSlots[key] = new ItemInfoSlot(key, item, 0, IconType, SlotFlags);
            Host.Set(slot, "binding", key);
            Host.Set(label, "text", ClientFormat.Format("{0} X {1}", new ItemTooltip(Model, 0).Name(item), item.Count));
            slotBinds.Add(key);
            Host.Invoke(Host.Entity, "SetMultiType", false);
            return;
        }
        for (int i = 0; i < 10; i++)
        {
            if (Host.Child("m_multi" + (i + 1).ToString(CultureInfo.InvariantCulture), resultItem) is not { } m) continue;
            if (Host.Child("m_itemSlot", m) is not { } slot) continue;
            string key = "";
            if (i < result.Count)
            {
                var e = result[i];
                if (Item.Create(t.Items, e.ItemId, e.Rank) is not { } item) continue;
                item.SetCountClamped(e.Count);
                key = $"ItemLullu_{TypeName}_{i + 1}";
                Model.ItemSlots[key] = new ItemInfoSlot(key, item, i, IconType, SlotFlags);
                slotBinds.Add(key);
                if (Host.Child("m_uncommonEffect", m) is { } unc)
                {
                    bool sp = IsSpecial(e);
                    Host.SetVisible(unc, sp);
                    if (sp) Host.Invoke(unc, "gotoAndPlay", 1);
                }
            }
            Host.Set(slot, "binding", key);
        }
        Host.Invoke(Host.Entity, "SetMultiType", true);
    }

    /// <summary>ReleaseSlotBinds @0x140e9c0d0.</summary>
    void ReleaseSlotBinds()
    {
        foreach (var n in slotBinds) Model?.ItemSlots.Remove(n);
        slotBinds.Clear();
    }
}
