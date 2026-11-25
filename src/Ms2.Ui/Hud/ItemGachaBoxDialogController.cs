using System.Globalization;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIItemGachaBoxDialog (client/ms2/uiitemgachaboxdialog.cpp, UI id 0x4f8b, type name "ItemGachaBoxDialog", movie
/// uiitemgachaboxdialog): the capsule machine of an OpenGachaBox item. OpenGachaBox (@0x140d25e10) gives it the item id
/// (<see cref="SetBoxItemId"/>) and opens it; the machine lists the gacha's drop box, its open buttons ask
/// <see cref="CanOpenBox"/> and send the item's use with "single" or "multi" (<see cref="SendOpenBox"/>), and the server's
/// ItemScript mode 5 (<see cref="OnResult"/>) shows what came out.
/// STILL-OPEN: the result items' SetGachaId (the tooltip's gacha line), the probability button (onOpenProbability does
/// nothing in the client either), the web page of FUN_140c42050 (no caller here). std::sort of the listed drops is not
/// stable in the client: rows of the same rank keep their table order here, the client's order of such rows past 32 rows
/// (MSVC's introsort) is not reproduced.
/// </summary>
public sealed class ItemGachaBoxDialogController : UiController
{
    public override string Movie => "uiitemgachaboxdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemGachaBoxDialog";   // GetTypeName (@0x140c40150)

    public const int UiId = 0x4f8b;
    /// <summary>The machine's slots (FUN_140c40ad0: nSlots 0x23) and its bags (nBags 3).</summary>
    public const int SlotCount = 0x23, BagCount = 3;
    /// <summary>The slots' icon type and flags (ItemSlotBag::CreateParam and CreateItemInfoSlot: 0x415, 0x800).</summary>
    public const int IconType = 0x415, SlotFlags = 0x800;
    /// <summary>CFeatureManager id 0x10a: the capsule machine item is needed too.</summary>
    public const int CapsuleMachineFeature = 0x10a;
    /// <summary>The UI the shop link box's yes opens (FUN_140c42fd0: OpenDialog(0x4ec1)).</summary>
    public const int ShopLinkDialogId = 0x4ec1;
    /// <summary>The dialog's system sounds (s_sndClick, s_sndNormal, s_sndRolling, s_sndSpecial).</summary>
    public const string ClickSound = "System_Gotcha_Click_01", NormalSound = "System_Gotcha_Normal_01",
        RollingSound = "System_Gotcha_Rolling_01", SpecialSound = "System_Gotcha_Special_01";
    public const string EndTimeKey = "s_itemgacha_dialog_gacha_endtime", NotEnoughItemKey = "s_itemgacha_dialog_not_enough_item_shop_link",
        NotEnoughMachineKey = "s_itemgacha_dialog_not_enough_capsulemachine", UnknownErrorKey = "s_itemgacha_dialog_err_unknown";

    int boxItemId, machineId, uiType = 1, season;   // m_boxItemId, m_machineId, m_uiType (1 in the ctor), m_season
    string title = "";                             // m_title
    bool multi;                                    // m_multi
    /// <summary>m_sent: the use request went out and its result has not come back.</summary>
    public bool Sent { get; private set; }
    readonly List<string> slotBinds = new();       // m_slotBinds
    readonly List<string> bagIds = new();          // m_bagIds
    readonly Dictionary<string, List<string>> bagSlots = new(StringComparer.Ordinal);
    readonly List<ItemScriptBoxItem> result = new();       // m_result
    readonly HashSet<(int, int, int)> special = new();     // m_special (Unk_GachaLess: id, rank, count)
    object resultItem;                             // m_resultItem (m_machine.m_resultItem)

    /// <summary>The bag's first item of an id (g_143ab2a90.FUN_140a5e2e0(id)), null when it holds none.</summary>
    public Func<int, Item> FindItemById = _ => null;
    /// <summary>The bag's count of an item id (FUN_140a5f180(id, 0)).</summary>
    public Func<int, int> HaveCount = _ => 0;
    /// <summary>FUN_140a63880(item, mode): the use request with the argument; false when it was not sent.</summary>
    public Func<Item, string, bool> RequestUse = (_, _) => false;
    /// <summary>The local player's job group and gender (GetLocalPlayer()->GetJobGroupId / GetGender); null without one.</summary>
    public Func<(int JobGroup, int Gender)?> Self = () => null;
    /// <summary>The coin shop request (SendOp SystemShop 10, 1, the coin item).</summary>
    public Action<SystemShopCoinRequest> Send;
    /// <summary>PlaySystemSound(key, ""): the sound's handle (0 when none).</summary>
    public Func<string, long> PlaySound = _ => 0;
    /// <summary>CEffectService::FUN_1411a6060: stops a sound by its handle.</summary>
    public Action<long> StopSound = _ => { };
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public event Action<string> NoticeText;
    /// <summary>FUN_140d73c60: shows a message box.</summary>
    public Action<MessageBoxData> ShowBox = _ => { };
    /// <summary>CUIService::OpenDialog(id).</summary>
    public Action<int> OpenDialog = _ => { };

    long rolling;                                  // m_sndRolling

    public int BoxItemId => boxItemId;
    public IReadOnlyList<string> SlotBinds => slotBinds;

    /// <summary>
    /// SetBoxItemId @0x140c41c30: the bag's item of the id, its gacha (argument 0 of its function, ItemGachaBoxTable::GetBox);
    /// nothing when either is missing. Else the box id, the machine item (argument 1), the gacha's ui and season, and the item's
    /// name as the title. It runs before the load, so the HUD's model is passed in.
    /// </summary>
    public void SetBoxItemId(UiModel model, int itemId)
    {
        if (FindItemById(itemId) is not { } box || box.Data == null) return;
        int gachaId = box.Data.FunctionArgInt(0, 0);
        if (!model.Tables.GachaBoxes.HasBox(gachaId)) return;
        boxItemId = itemId;
        machineId = box.Data.FunctionArgInt(1, 0);
        uiType = model.Tables.GachaBoxes.Ui(gachaId);
        season = ItemData.Atoi(model.Tables.GachaBoxes.SeasonText(gachaId) ?? "");
        title = new ItemTooltip(model, 0).Name(box);
    }

    /// <summary>The gacha of the box item (GetGachaBoxData @0x140c43110): argument 0 of the bag item's function; 0 without the
    /// item or a box of that id.</summary>
    int GachaId()
    {
        if (FindItemById(boxItemId) is not { } box || box.Data == null) return 0;
        int id = box.Data.FunctionArgInt(0, 0);
        return Model.Tables.GachaBoxes.HasBox(id) ? id : 0;
    }

    /// <summary>vfunc_67 @0x140c401d0: UIDialog's close, then the box id and title cleared and the result slots released.</summary>
    public override void Close()
    {
        base.Close();
        boxItemId = 0;
        title = "";
        ReleaseSlotBinds();
        resultItem = null;
    }

    /// <summary>vfunc_26 @0x140c403a0 (the unload): the bags released, the rolling sound stopped.</summary>
    public void Unload()
    {
        ReleaseBagBinds();
        ReleaseSlotBinds();
        StopSound(rolling);
        rolling = 0;
    }

    /// <summary>OnLoadComplete @0x140c40240: no data for the box item closes it; else m_machine's m_resultItem, the rolling
    /// sound handle reset, then the machine's data (<see cref="InitData"/>).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Model.Tables.Items.Get(boxItemId) == null) { Close(); return; }
        if (Host.Child("m_machine") is { } machine) resultItem = Host.Child("m_resultItem", machine);
        rolling = 0;
        InitData();
    }

    /// <summary>
    /// FUN_140c40ad0 @0x140c40ad0 (:268): without the local player, the gacha or the box item, nothing. The machine's object:
    /// uiType, title, sellingTime (the sale end in s_itemgacha_dialog_gacha_endtime, "" without an end), coinshop (the gacha
    /// has a coin item). The gacha's drop box (GetBoxItems(id, out)) keeps the rows shown on the gacha UI whose item exists,
    /// suits the player's job group and allows the gender; none: nothing. Sorted by rank, highest first, the 35 slots take
    /// them in turn; bag 0 is slots 13..0, bag 1 slots 27..14, bag 2 slots 6..0 then 34..28. Then initData.
    /// </summary>
    public void InitData()
    {
        if (Self() is not var (job, gender)) return;
        int gachaId = GachaId();
        if (gachaId == 0) return;
        if (FindItemById(boxItemId) == null) return;
        var t = Model.Tables;
        string selling = "";
        if (t.GachaBoxes.SaleEnd(gachaId) is { } end)
            selling = ClientFormat.Format(t.Common(EndTimeKey) ?? "", end[0], end[1], end[2], end[3], end[4], end[5]);
        bool coinshop = t.GachaBoxes.Coin(gachaId) is { ItemId: > 0 };
        var sorted = Drops(t, gachaId, job, gender);
        if (sorted.Count == 0) return;
        var items = new List<Item>();
        for (int k = 0; k < SlotCount; k++)
        {
            var d = sorted[k % sorted.Count];
            if (Item.Create(t.Items, d.ItemId, d.Rank) is not { } item) continue;
            item.SetCountClamped(d.Count);
            items.Add(item);
        }
        if (items.Count < SlotCount) return;   // the client indexes all 35 (every row's item exists, checked in Drops)
        var bags = new List<Item>[BagCount];
        for (int k = 0; k < BagCount; k++) bags[k] = BagSlotOrder[k].Select(i => items[i]).ToList();
        ReleaseBagBinds();
        var obj = new List<(string, object)>
        {
            ("uiType", uiType), ("title", title), ("sellingTime", selling), ("coinshop", coinshop),
        };
        for (int k = 0; k < BagCount; k++)
        {
            string bag = "ItemGachaBoxBag" + k.ToString(CultureInfo.InvariantCulture);
            var names = new List<string>();
            for (int i = 0; i < bags[k].Count; i++)
            {
                string name = bag + "." + i.ToString(CultureInfo.InvariantCulture);
                Model.ItemSlots[name] = new ItemInfoSlot(name, bags[k][i], i, IconType, SlotFlags);
                names.Add(name);
            }
            bagSlots[bag] = names;
            obj.Add(("bagId" + k.ToString(CultureInfo.InvariantCulture), bag));
            bagIds.Add(bag);
        }
        Host.Invoke(Host.Entity, "initData", Host.Obj(obj.ToArray()));
    }

    /// <summary>The machine's bags as FUN_140c40ad0 fills them from the 35 slots: bag 0 slots 13..0, bag 1 slots 27..14, bag 2
    /// slots 6..0 (added in the first loop) then 34..28.</summary>
    public static readonly int[][] BagSlotOrder =
    {
        Enumerable.Range(0, 14).Reverse().ToArray(),
        Enumerable.Range(14, 14).Reverse().ToArray(),
        Enumerable.Range(0, 7).Reverse().Concat(Enumerable.Range(28, 7).Reverse()).ToArray(),
    };

    /// <summary>
    /// FUN_140c40ad0's list: the gacha's drop box rows (IndividualItemDropTable::GetBoxItems(id, out), itemdroptable.cpp:1171)
    /// that show on the gacha UI, whose item has data, that suit the job group (IsJobRecommended) and allow the gender
    /// (IsGenderAllowed), sorted by rank, highest first (std::sort with FUN_140c43260). Rows of the same rank keep their table
    /// order (see the class notes).
    /// </summary>
    public static List<ItemDropEntry> Drops(GameTables t, int gachaId, int job, int gender)
    {
        var drops = new List<(ItemDropEntry Entry, bool ShowIconOnGachaUI)>();
        t.IndividualDrops.GetBoxItems(t.GachaBoxes.IndividualDropBoxId(gachaId), drops);
        var filtered = new List<ItemDropEntry>();
        foreach (var (d, show) in drops)
        {
            if (!show) continue;
            if (t.Items.Get(d.ItemId) is not { } data) continue;
            if (!data.IsJobRecommended(job)) continue;
            if (!data.IsGenderAllowed(gender)) continue;
            filtered.Add(d);
        }
        return filtered.OrderByDescending(d => d.Rank).ToList();
    }

    public override IEnumerable<string> GetBagSlotBindings(string bag)
        => bagSlots.TryGetValue(bag, out var names) ? names : Array.Empty<string>();

    /// <summary>
    /// FUN_140c41700 @0x140c41700: 0 when the box can be opened once (10 times for multi). 1: the bag holds no box item or fewer
    /// than needed; 3: no data or not an OpenGachaBox item; 2: with feature 0x10a, the machine item (argument 1) is set and the
    /// bag holds fewer than needed.
    /// </summary>
    public int CanOpenBox()
    {
        int need = multi ? 10 : 1;
        if (FindItemById(boxItemId) is not { } box) return 1;
        if (box.Data == null) return 3;
        if (box.Data.FunctionName != "OpenGachaBox") return 3;
        if (need > HaveCount(boxItemId)) return 1;
        if (Model.IsFeatureEnabled(CapsuleMachineFeature))
        {
            int machine = box.Data.FunctionArgInt(1, 0);
            if (machine != 0 && need > HaveCount(machine)) return 2;
        }
        return 0;
    }

    /// <summary>vfunc_28 @0x140c40860 after UIDialog's.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onsendopenbox":
                if (args is { Length: > 0 }) SendOpenBox();
                return false;
            case "oncanopenbox":
                if (args is { Length: > 0 })
                {
                    multi = Avm2.Rt.ToBoolean(args[0]);
                    int r = CanOpenBox();
                    if (r == 0) Host.Invoke(Host.Entity, "onOpenBox");
                    else ShowOpenBoxError(r);
                }
                return false;
            case "onopencoinshop":
                OnOpenCoinShop();
                return false;
            case "onopenprobability":
                return false;
            case "onchangestate":
                if (args is { Length: > 0 }) OnChangeState(Avm2.Rt.ToString(args[0]));
                return false;
        }
        return false;
    }

    /// <summary>ShowOpenBoxError @0x140c41900: 1 the shop link box (type 1, the title in the text; its yes opens 0x4ec1); 2 the
    /// notice naming the machine item; 3 the unknown error notice.</summary>
    public void ShowOpenBoxError(int error)
    {
        var t = Model.Tables;
        if (error == 1)
        {
            var box = MessageBoxQueue.Create(1, ClientFormat.Format(t.Common(NotEnoughItemKey) ?? "", title), null, "");
            box.Callback = (button, _) => { if (button == 0) OpenDialog(ShopLinkDialogId); return true; };   // FUN_140c42fd0
            ShowBox(box);
        }
        else if (error == 2) NoticeText?.Invoke(ClientFormat.Format(t.Common(NotEnoughMachineKey) ?? "", t.ItemName(machineId)));
        else if (error == 3) NoticeText?.Invoke(t.Common(UnknownErrorKey) ?? "");
    }

    /// <summary>SendOpenBox @0x140c41e10: the box item in the bag and <see cref="CanOpenBox"/> 0: its use with "single" or
    /// "multi"; not sent: the dialog closes (vfunc_67); sent: m_sent.</summary>
    public void SendOpenBox()
    {
        string mode = multi ? "multi" : "single";
        if (FindItemById(boxItemId) is not { } box) return;
        if (CanOpenBox() != 0) return;
        if (!RequestUse(box, mode)) Close();
        else Sent = true;
    }

    /// <summary>OnOpenCoinShop @0x140c41f20: the gacha's coin item to the system shop (SystemShop 10, 1, the coin).</summary>
    public void OnOpenCoinShop()
    {
        if (Model.Tables.Items.Get(boxItemId) is not { } data) return;
        int gachaId = data.FunctionArgInt(0, 0);
        if (Model.Tables.GachaBoxes.Coin(gachaId) is not { } coin) return;
        Send?.Invoke(new SystemShopCoinRequest(coin.ItemId));
    }

    /// <summary>OnChangeState @0x140c42ee0: "idle" restarts the rolling sound; "open_sound" plays the click.</summary>
    public void OnChangeState(string state)
    {
        if (string.Equals(state, "idle", StringComparison.OrdinalIgnoreCase))
        {
            StopSound(rolling);
            rolling = PlaySound(RollingSound);
        }
        else if (string.Equals(state, "open_sound", StringComparison.OrdinalIgnoreCase)) PlaySound(ClickSound);
    }

    /// <summary>
    /// vfunc_79 @0x140c403e0 for ItemScript: loaded only; m_sent cleared; mode 5 keeps the items and the special ones, binds
    /// them (<see cref="BindResult"/>), plays the normal sound and the special one when any is special, then onResult with
    /// showEffect (any special) and isMulti (more than one).
    /// </summary>
    public void OnResult(ItemScriptBoxResult r)
    {
        if (Host?.Entity == null || IsClosed) return;
        Sent = false;
        if (r == null || r.Mode != ItemScriptBoxResult.GachaMode) return;
        result.Clear();
        special.Clear();
        bool hasSpecial = false;
        foreach (var it in r.Items)
        {
            result.Add(it);
            if (it.Special) { hasSpecial = true; special.Add((it.ItemId, it.Rank, it.Count)); }
        }
        BindResult();
        PlaySound(NormalSound);
        if (special.Count > 0) PlaySound(SpecialSound);
        Host.Invoke(Host.Entity, "onResult", Host.Obj(("showEffect", hasSpecial), ("isMulti", r.Items.Count > 1)));
    }

    /// <summary>
    /// FUN_140c42160 @0x140c42160: one item fills m_singleItem (its slot bound as ItemGacha_ItemGachaBoxDialog_1, the name
    /// "{0} X {1}" with the count, m_uncommonEffect shown when special); more fill m_multi1..10 (slot i bound as
    /// ItemGacha_ItemGachaBoxDialog_i, the effect shown and played from frame 1 when special); slots past the result get "".
    /// </summary>
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
            if (Host.Child("m_uncommonEffect", single) is { } unc) Host.SetVisible(unc, special.Contains((e.ItemId, e.Rank, e.Count)));
            item.SetCountClamped(e.Count);
            string key = $"ItemGacha_{TypeName}_1";
            Model.ItemSlots[key] = new ItemInfoSlot(key, item, 0, IconType, SlotFlags);
            Host.Set(slot, "binding", key);
            Host.Set(label, "text", ClientFormat.Format("{0} X {1}", new ItemTooltip(Model, 0).Name(item), item.Count));
            slotBinds.Add(key);
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
                key = $"ItemGacha_{TypeName}_{i + 1}";
                Model.ItemSlots[key] = new ItemInfoSlot(key, item, i, IconType, SlotFlags);
                slotBinds.Add(key);
                if (Host.Child("m_uncommonEffect", m) is { } unc)
                {
                    bool sp = special.Contains((e.ItemId, e.Rank, e.Count));
                    Host.SetVisible(unc, sp);
                    if (sp) Host.Invoke(unc, "gotoAndPlay", 1);
                }
            }
            Host.Set(slot, "binding", key);
        }
    }

    /// <summary>ReleaseSlotBinds @0x140c43010.</summary>
    void ReleaseSlotBinds()
    {
        foreach (var n in slotBinds) Model?.ItemSlots.Remove(n);
        slotBinds.Clear();
    }

    /// <summary>ReleaseBagBinds @0x140c43090.</summary>
    void ReleaseBagBinds()
    {
        foreach (var bag in bagIds)
            if (bagSlots.Remove(bag, out var names)) foreach (var n in names) Model?.ItemSlots.Remove(n);
        bagIds.Clear();
    }
}
