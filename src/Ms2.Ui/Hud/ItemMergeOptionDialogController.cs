using System.Globalization;
using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The item merge window, UIItemMergeOptionDialog (client/ms2/uiitemmergeoptiondialog.cpp, UI id 0x4e68, type name
/// "ItemMergeOptionDialog", movie uiitemmergeoptiondialog). The item to merge and a material are picked from two bags
/// (<c>&lt;TypeName&gt;_MergeSlotBag</c>, <c>&lt;TypeName&gt;_MaterialSlotBag</c>), the server answers each pick (RecvOp 0x74
/// modes 0, 1, 2, 4) and the merge itself is answered by modes 5 / 6, which the scene reads and hands to
/// <see cref="ShowMergeResult"/> / <see cref="ShowMergeError"/>. Phases (m_nPhase): 0 empty, 1 ready, 2 item picked, 3 material
/// picked, 4 waiting for the option answer, 5 merge sent, 6 result shown.
/// <para>STILL-OPEN: the exact option text of GetCurrentOption (FUN_140631600 / FUN_140631830: here the option name and value),
/// the dock beside the NPC dialog 0x4e40 (vfunc_43), the NpcTalk end after the result (m_nTalkId is set by the talk), the
/// item-bag sort is stable where the client's qsort is not, the slot names of the two bags, the revert dialog 0x4f3d.</para>
/// </summary>
public sealed class ItemMergeOptionDialogController : UiController
{
    public override string Movie => "uiitemmergeoptiondialog";
    /// <summary>The CommonDepth layer of the movie: UIItemMergeOptionDialog's ctor sets m_2c to 0x1b (uiitemmergeoptiondialog.cpp:317).</summary>
    public override int LoadLayer => CommonDepth.CinematicDialog;
    protected override string EntityMethod => "alignment";
    public override string TypeName => "ItemMergeOptionDialog";
    /// <summary>UIItemMergeOptionDialog's ctor (@0x1419ccd00, uiitemmergeoptiondialog.cpp:314) has no SetTweenEnabled: m_c4 stays 0, so CloseDialog
    /// unloads at once (FUN_141924330, uidialog.cpp:393) and a following OpenDialog loads it again.</summary>
    protected override bool TweenEnabled => false;

    public const int UiId = 0x4e68;
    public string MergeBag => TypeName + "_MergeSlotBag";
    public string MaterialBag => TypeName + "_MaterialSlotBag";
    /// <summary>The merge and material slots (ASSlot flags), as the other item bags: 0x1401f, tooltip 0x20.</summary>
    public const int SlotFlags = 0x1401f, TooltipFlags = 0x20;
    /// <summary>The warn box of a merge over merged options (common key, box type 1); the missing-item box type.</summary>
    public const int WarnBoxType = 1;
    /// <summary>The chat kind of the missing-item line.</summary>
    public const int ChatKind = 0x14;
    /// <summary>onDebug's smallest debug level (RequestMaterial).</summary>
    public const int DebugLevel = 50;

    /// <summary>CheckMerge's answers.</summary>
    public const int CheckOk = 0, CheckLackMaterial = 1, CheckLackMeso = 2, CheckNoItem = 3, CheckNoMaterial = 4;

    // ---- the outside world (the client's globals and singletons) ----

    /// <summary>FUN_140710590: the packet is sent.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>FindMergeItem / Unk_Inventory::FindItemByUid: an inventory or equipped item by uid.</summary>
    public Func<long, Item> Find = _ => null;
    /// <summary>Unk_Inventory::FindItemByUid: the inventory only (CheckMerge's material). Null: Find.</summary>
    public Func<long, Item> FindBag;
    /// <summary>The inventory's category 0 items (the merge candidates).</summary>
    public Func<IEnumerable<Item>> BagItems = () => Array.Empty<Item>();
    /// <summary>The equipped items shown on layer 0.</summary>
    public Func<IEnumerable<Item>> EquippedItems = () => Array.Empty<Item>();
    /// <summary>DAT_143ab2898: the player's meso.</summary>
    public Func<long> Meso = () => 0;
    /// <summary>How many of an item id / of an item tag the inventory holds.</summary>
    public Func<int, int> CountById = _ => 0, CountByTag = _ => 0;
    /// <summary>ShowSystemNotice (kind 1).</summary>
    public Action<string> Notice = _ => { };
    /// <summary>ShowMessageBox(text, 0, 0, "") and FUN_1412751b0(text, 0x14): the missing-item box and chat line.</summary>
    public Action<string> MissingBox = _ => { }, ChatLine = _ => { };
    /// <summary>The box is shown (FUN_140d73c60).</summary>
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>UITalkManager::FUN_141b213c0 / GetTalkNpcId and the client constant m_talk of RequestResultTalk.</summary>
    public Func<bool> TalkActive = () => false;
    public Func<int> TalkNpcId = () => 0;
    public int TalkConstant;
    /// <summary>The debug level of onDebug.</summary>
    public int DebugLevelNow;
    /// <summary>The common string of a key ("" without one) and an item's name by id.</summary>
    public Func<string, string> Text;
    public Func<int, string> ItemName;

    public event Action Closed;

    // ---- the state (members of the client's dialog) ----

    /// <summary>m_nPhase.</summary>
    public int Phase { get; private set; }
    /// <summary>m_nItemUid / m_nMaterialUid.</summary>
    public long ItemUid { get; private set; }
    public long MaterialUid { get; private set; }
    /// <summary>m_bSpecial: the picked item already holds merged (group 4) options.</summary>
    public bool Special { get; private set; }
    /// <summary>m_nTalkId: the talk the result goes on with (0 none).</summary>
    public int TalkId;
    /// <summary>m_spProxy: the option proxy the last mode 2 answer carried.</summary>
    public MergeOptionProxy Proxy { get; private set; } = new(0, Array.Empty<MergeIngredient>(), Array.Empty<int>(), Array.Empty<MergeOptionRange>(), Array.Empty<MergeOptionRange>());
    /// <summary>m_items190: the material uids of the mode 1 answer.</summary>
    public IReadOnlyList<long> MaterialUids => materials;
    /// <summary>The merge / material bags' items, in their sorted order.</summary>
    public IReadOnlyList<Item> MergeItems => mergeItems;
    public IReadOnlyList<Item> MaterialItems => materialItems;

    readonly List<long> materials = new();
    readonly List<Item> mergeItems = new(), materialItems = new();
    bool closing;

    string T(string key) => Text != null ? Text(key) ?? key : Model?.Tables.Common(key) ?? key;
    string Loc(string key) => Host.Player.Translate?.Invoke("$" + key) ?? key;

    /// <summary>HasGroup4Options (CItemStatus): the item holds options in the merge group (4).</summary>
    public static bool HasMergedOptions(Item item) =>
        item?.Record?.Options is { } o && (o.Stat[4].Count > 0 || o.Special[4].Count > 0);

    static bool Fusionable(Item item) => item?.Data != null && item.Data.Fusionable != 0;

    // ---- load / close ------------------------------------------------------------------------------------------------

    /// <summary>OnLoadComplete: SetPhase(0), the bag lists, the price label, RequestInfo (mode 0).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        SetPhase(0);
        ResetMergeItems();
        ShowMergePrice();
        Send(ItemMergeRequest.Info());
    }

    /// <summary>vfunc_13: Escape or the action key may close the dialog except while waiting for the answer (4) or merging (5).</summary>
    public bool CanClose => Phase != 4 && Phase != 5;
    /// <summary>vfunc_13 (uiitemmergeoptiondialog.cpp:430) has no UIDialog call: Esc (or action 0x493e2) is taken while it waits
    /// for the answer or merges (phase 4/5); otherwise it passes the dialog without closing it.</summary>
    public override bool EscapeKey() => !IsClosed && !CanClose;

    /// <summary>vfunc_67: both bags released, the picks cleared, phase 0.</summary>
    public override void Close()
    {
        if (closing) return;
        closing = true;
        ItemUid = 0; MaterialUid = 0; Special = false;
        mergeItems.Clear(); materialItems.Clear(); materials.Clear();
        Phase = 0;
        base.Close();
        Closed?.Invoke();
    }

    void SetPhase(int phase) => Phase = phase;

    // ---- the packet (CSceneStateGame::OnItemFusion) ------------------------------------------------------------------

    /// <summary>RecvOp 0x74 modes 0, 1, 2 and 4 (the dialog's readers); 5 and 6 are <see cref="ShowMergeResult"/>.</summary>
    public void OnPacket(ItemMergePacket p)
    {
        if (closing) return;
        switch (p.Mode)
        {
            case 0: OnInfo(p); break;
            case 1:
                materials.Clear();
                if (p.Materials != null) materials.AddRange(p.Materials);
                RefreshMaterialBag();
                if (Host?.Entity != null) Host.Invoke(Host.Entity, "setSelectItem", Host.Obj(("uid", UidText(ItemUid)), ("special", Special)));
                break;
            case 2: OnPreview(p.Proxy); break;
            case 4: OnOptionChoice(p.Normal, p.Special, p.Value); break;
        }
    }

    /// <summary>Mode 0: sub 0 is phase 1; otherwise the previous option's item is looked up: missing is the missing-item
    /// notice and phase 1; found is the picked item with the previous option (setPrevMergeOption).</summary>
    void OnInfo(ItemMergePacket p)
    {
        if (p.Sub == 0) { SetPhase(1); return; }
        if (p.Sub == 2 || Find(p.Uid) == null)
        {
            ShowMissingItem(p.ItemId);
            SetPhase(1);
            return;
        }
        ItemUid = p.Uid;
        Special = true;
        if (Host?.Entity != null)
            Host.Invoke(Host.Entity, "setPrevMergeOption", Host.Obj(("normal", p.Normal), ("special", p.Special), ("value", p.Value)));
        OnOptionChoice(p.Normal, p.Special, p.Value);
    }

    /// <summary>ShowMissingItem: the item name in the text, as a box and a chat line.</summary>
    public void ShowMissingItem(int itemId)
    {
        string text = ClientFormat.Format(T("s_item_merge_option_error_prev_option_item_not_exist"), ItemName?.Invoke(itemId) ?? "");
        MissingBox(text);
        ChatLine(text);
    }

    /// <summary>FUN_1419d12f0 (mode 2): the new proxy; with the picked item still there the ingredient, the static
    /// ingredients, the option preview and the price are shown.</summary>
    void OnPreview(MergeOptionProxy proxy)
    {
        if (proxy != null) Proxy = proxy;
        if (Find(ItemUid) is not { } item) return;
        RefreshSelectedIngredient();
        ApplyMergeItem();
        ShowOptionPreview();
        ShowMergePrice();
    }

    /// <summary>FUN_1419d1490 (mode 4 and the previous-option path): the old option text next to the new one, then onMerge; phase 4.</summary>
    void OnOptionChoice(int normal, int special, int value)
    {
        var item = Find(ItemUid);
        if (item == null) return;                         // FindMergeItem(m_nItemUid) == 0: back before the phase
        if (Host?.Entity != null)
        {
            string oldText = CurrentOptionText(item);
            string newText = normal != -1 ? StatOptionText(normal, value) : special != 0 ? SpecialOptionText(special, value) : "";
            Host.Invoke(Host.Entity, "selectOption", Host.Obj(
                ("oldOption", ClientFormat.Format(T("s_item_merge_option_old_option"), oldText)),
                ("newOption", ClientFormat.Format(T("s_item_merge_option_new_option"), newText)),
                ("iconPath", item?.IconPath ?? ""),
                ("itemGrade", item?.Rarity ?? 0),
                ("itemName", item == null ? "" : ItemName?.Invoke(item.ItemId) ?? "")));
            Host.Invoke(Host.Entity, "onMerge");
        }
        SetPhase(4);
    }

    /// <summary>ShowMergeResult (scene packet mode 5 with the item read): setResult, and onMerge unless the item was already
    /// merged; m_nTalkId = 0; phase 6.</summary>
    public void ShowMergeResult(Item item)
    {
        if (closing || item == null) return;
        if (Host?.Entity != null)
        {
            Host.Invoke(Host.Entity, "setResult", Host.Obj(
                ("iconPath", item.IconPath ?? ""),
                ("itemGrade", item.Rarity),
                ("itemName", ItemName?.Invoke(item.ItemId) ?? ""),
                ("option", CurrentOptionText(item))));
            if (!Special) Host.Invoke(Host.Entity, "onMerge");
        }
        TalkId = 0;
        SetPhase(6);
    }

    /// <summary>ShowMergeError: the notice of the code (an unknown code shows nothing).</summary>
    public void ShowMergeError(int error)
    {
        string key = error switch
        {
            3 => "s_item_merge_option_error_invalid_mergeitem",
            4 => "s_item_merge_option_error_invalid_material",
            1 => "s_item_merge_option_error_lack_material",
            9 => "s_item_merge_option_error_unknown",
            5 => "s_item_merge_option_error_invalid_merge_scroll",
            6 => "s_item_merge_option_error_invalid_revert_rank",
            7 => "s_item_merge_option_error_invalid_revert_option",
            2 => "s_item_merge_option_error_lack_meso",
            _ => null,
        };
        if (key != null) Notice(T(key));
    }

    // ---- the movie's commands (vfunc_28) -----------------------------------------------------------------------------

    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onmergeitemselect": SelectItem(ParseUid(args)); return true;
            case "onmaterialitemselect": SelectMaterial(ParseUid(args)); return true;
            case "onmerge": ClickMerge(); return true;
            case "onshowresult": Send(ResultTalk()); return true;
            case "onresetdialog": ResetDialog(); return true;
            case "ondebug":
                if (DebugLevelNow >= DebugLevel) Send(ItemMergeRequest.Material(ItemUid, MaterialUid));
                return true;
            case "onmergeselectoption":
                Send(ItemMergeRequest.ChoiceOf((byte)(args.Length > 0 && Rt.ToBoolean(args[0]) ? 1 : 0)));
                return true;
        }
        return false;
    }

    static long ParseUid(object[] args)
    {
        if (args == null || args.Length == 0) return 0;
        object a = args[0];
        if (a is string s) return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out long v) ? v : 0;
        return a == null ? 0 : (long)Rt.ToNumber(a);
    }

    /// <summary>onMergeItemSelect: ignored while empty (phase 0); the item and whether it already holds merged options are
    /// kept, phase 2, RequestSelection (mode 1).</summary>
    public void SelectItem(long uid)
    {
        if (Phase == 0) return;
        ItemUid = uid;
        if (Find(uid) is { } found) Special = HasMergedOptions(found);     // not found: m_bSpecial is kept
        SetPhase(2);
        Send(ItemMergeRequest.Selection(uid));
    }

    /// <summary>onMaterialItemSelect: the material is kept, phase 3, RequestPreview (mode 2).</summary>
    public void SelectMaterial(long uid)
    {
        MaterialUid = uid;
        SetPhase(3);
        Send(ItemMergeRequest.Preview(ItemUid, uid));
    }

    /// <summary>CheckMerge: 3 no item, 4 no material, 2 meso short, 1 an ingredient short (counted by tag when it has one, else
    /// by item id), else 0.</summary>
    public int CheckMerge()
    {
        if (Find(ItemUid) == null) return CheckNoItem;
        if ((FindBag ?? Find)(MaterialUid) == null) return CheckNoMaterial;
        if (Meso() < Proxy.Meso) return CheckLackMeso;
        foreach (var ing in Proxy.Ingredients)
            if (Have(ing) < ing.C) return CheckLackMaterial;
        return CheckOk;
    }

    int Have(MergeIngredient ing) => ing.B != 0 ? CountByTag(ing.B) : CountById(ing.A);

    /// <summary>onMerge: a refused check is its notice; an item that already holds merged options asks first (the warn box,
    /// phase 4; its first button merges), else the merge is sent (mode 3) and the phase is 5.</summary>
    public void ClickMerge()
    {
        int err = CheckMerge();
        if (err != CheckOk) { ShowMergeError(err); return; }
        if (HasMergedOptions(Find(ItemUid)))
        {
            var box = MessageBoxQueue.Create(WarnBoxType, T("s_item_merge_option_warn"), null, "");
            box.Callback = ConfirmMerge;
            ShowData(box);
            SetPhase(4);
            return;
        }
        RequestMerge();
    }

    bool ConfirmMerge(int button, MessageBoxData box)
    {
        if (button == 0 && !closing) RequestMerge();
        return true;
    }

    void RequestMerge()
    {
        Send(ItemMergeRequest.Merge(ItemUid, MaterialUid));
        SetPhase(5);
    }

    /// <summary>RequestResultTalk.</summary>
    public ItemMergeResultTalkRequest ResultTalk() => new(TalkActive(), TalkNpcId(), TalkConstant, (short)TalkId);

    /// <summary>onResetDialog: the bags rebuilt, the price cleared, phase 1.</summary>
    public void ResetDialog()
    {
        ResetMergeItems();
        Proxy = new(0, Array.Empty<MergeIngredient>(), Array.Empty<int>(), Array.Empty<MergeOptionRange>(), Array.Empty<MergeOptionRange>());
        ShowMergePrice();
        SetPhase(1);
    }

    // ---- the bags ----------------------------------------------------------------------------------------------------

    /// <summary>CompareMergeItems' rank: equipped (not in the bag) 16, merged options 8, plus the rarity; higher first.</summary>
    static int SortRank(Item item, bool equipped) => (equipped ? 16 : 0) | (HasMergedOptions(item) ? 8 : 0) | item.Rarity;

    /// <summary>ResetMergeItems: the fusionable bag items and the equipped ones, sorted by rank, bound under the merge bag.</summary>
    public void ResetMergeItems()
    {
        var all = new List<(Item item, int rank)>();
        foreach (var i in BagItems()) if (Fusionable(i)) all.Add((i, SortRank(i, false)));
        foreach (var i in EquippedItems()) if (Fusionable(i)) all.Add((i, SortRank(i, true)));
        mergeItems.Clear();
        mergeItems.AddRange(all.OrderByDescending(a => a.rank).Select(a => a.item));
        BindBag(MergeBag, "m_itemBag", mergeItems.Count);
    }

    /// <summary>RefreshMaterialBag: the same over the material uids of mode 1.</summary>
    public void RefreshMaterialBag()
    {
        var all = new List<(Item item, int rank)>();
        foreach (long uid in materials)
            if (Find(uid) is { } i && Fusionable(i)) all.Add((i, SortRank(i, false)));
        materialItems.Clear();
        materialItems.AddRange(all.OrderByDescending(a => a.rank).Select(a => a.item));
        BindBag(MaterialBag, "m_ingredientBag", materialItems.Count);
    }

    void BindBag(string bag, string child, int count)
    {
        if (Host?.Entity == null || closing) return;
        if (Host.Child(child) is { } c) Host.Set(c, "bagId", bag);
        for (int i = 0; i < count; i++) Host.Push(SlotName(bag, i));
    }

    public static string SlotName(string bag, int i) => bag + "." + i.ToString(CultureInfo.InvariantCulture);

    bool ParseSlot(string name, out List<Item> list, out int index)
    {
        list = null; index = -1;
        foreach (var (bag, l) in new[] { (MergeBag, mergeItems), (MaterialBag, materialItems) })
        {
            if (!name.StartsWith(bag + ".", StringComparison.Ordinal)) continue;
            list = l;
            return int.TryParse(name.AsSpan(bag.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index) && index < l.Count;
        }
        return false;
    }

    public override IEnumerable<string> GetBagSlotBindings(string bag)
    {
        if (bag == MergeBag) return Enumerable.Range(0, mergeItems.Count).Select(i => SlotName(bag, i));
        if (bag == MaterialBag) return Enumerable.Range(0, materialItems.Count).Select(i => SlotName(bag, i));
        return Array.Empty<string>();
    }

    public override object GetBinding(string name)
    {
        if (!ParseSlot(name, out var list, out int i) || Model == null) return base.GetBinding(name);
        var slot = new List<(string, object)>();
        return ItemSlot.Build(slot, list[i], Model.HasLocalPlayer, SlotFlags, Model, Model.ItemSlotSources) ? Host.Obj(slot.ToArray()) : null;
    }

    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (!ParseSlot(name, out var list, out int i)) return base.GetSlotTooltip(name);
        var tip = new ItemTooltip(Model, TooltipFlags) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        return tip.Fill(list[i]) ? tip.Data : new AsTooltip();
    }

    /// <summary>
    /// vfunc_45 (uiitemmergeoptiondialog.cpp:448), after UIObject's: "getTooltipByGUID" (the uid text, FUN_141b71960) is
    /// FUN_1409e0d40 of FindMergeItem(uid), "getTooltipByID" (id, rank) is FUN_1409e1770, both ASTooltip(player, 0x20, 2).
    /// </summary>
    public override IDictionary<string, object> GetUserData(string name, object[] args)
    {
        if (string.Equals(name, "getTooltipByGUID", StringComparison.OrdinalIgnoreCase))
        {
            long uid = args is { Length: > 0 } && long.TryParse(Rt.ToString(args[0]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : 0;
            var tip = new ItemTooltip(Model, TooltipFlags) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
            return Find(uid) is { } item && tip.Fill(item) ? tip.Data : null;
        }
        if (string.Equals(name, "getTooltipByID", StringComparison.OrdinalIgnoreCase))
        {
            int id = args is { Length: > 0 } ? (int)Rt.ToUint32(args[0]) : 0;
            int rank = args is { Length: > 1 } ? (int)Rt.ToUint32(args[1]) : 0;
            return ItemTooltip.ById(Model, TooltipFlags, id, rank);
        }
        return base.GetUserData(name, args);
    }

    /// <summary>vfunc_45's "isMergeable": true.</summary>
    public override object GetUserValue(string name, object[] args)
        => string.Equals(name, "isMergeable", StringComparison.OrdinalIgnoreCase) ? true : base.GetUserValue(name, args);

    // ---- the shown pieces --------------------------------------------------------------------------------------------

    static string UidText(long uid) => uid.ToString(CultureInfo.InvariantCulture);

    /// <summary>RefreshSelectedIngredient: setIngredient {iconPath, itemGrade, index (in the material uids), itemGUID}.</summary>
    void RefreshSelectedIngredient()
    {
        if (Host?.Entity == null || Find(MaterialUid) is not { } mat) return;
        Host.Invoke(Host.Entity, "setIngredient", Host.Obj(("iconPath", mat.IconPath ?? ""), ("itemGrade", mat.Rarity),
            ("index", materials.IndexOf(MaterialUid)), ("itemGUID", UidText(MaterialUid))));
    }

    /// <summary>The rank of an item id (the table's rank).</summary>
    int RankOfId(int id) => Model?.Tables.Items.Get(id)?.Rank ?? 0;

    /// <summary>ApplyMergeItem: a row per proxy ingredient (icon, grade, "have / need" in the enable or disable text, id), the
    /// list "ingredient&lt;n&gt;" and "ready" (false when any is short); setStaticIngredient.</summary>
    public void ApplyMergeItem()
    {
        if (Host?.Entity == null) return;
        bool ready = true;
        var args = new List<(string, object)>();
        for (int i = 0; i < Proxy.Ingredients.Length; i++)
        {
            var ing = Proxy.Ingredients[i];
            int have = Have(ing);
            bool enough = have >= ing.C;
            if (!enough) ready = false;
            string text = ClientFormat.Format(T(enough ? "s_item_merge_option_material_count_enable" : "s_item_merge_option_material_count_disable"), have, ing.C);
            var data = Model?.Tables.Items.Get(ing.A);
            args.Add(("ingredient" + (i + 1).ToString(CultureInfo.InvariantCulture),
                Host.Obj(("iconPath", data?.IconPath ?? ""), ("itemGrade", RankOfId(ing.A)), ("itemCount", text), ("itemID", ing.A))));
        }
        args.Add(("ready", ready));
        Host.Invoke(Host.Entity, "setStaticIngredient", Host.Obj(args.ToArray()));
    }

    /// <summary>The fixed order of the option preview, the constructor's table (uiitemmergeoptiondialog.cpp:319-367) as
    /// (stat type, special) pairs; -1 stat is a special option.</summary>
    static readonly (int stat, int special)[] OrderTable =
    {
        (4,0),(7,0),(10,0),(0,0),(1,0),(2,0),(3,0),(14,0),(27,0),(29,0),(23,0),(24,0),(31,0),(-1,26),(-1,27),(15,0),(17,0),(18,0),
        (-1,24),(-1,25),(-1,19),(-1,18),(-1,23),(-1,22),(-1,20),(-1,21),(-1,12),(-1,7),(-1,16),(13,0),(-1,35),(-1,54),(20,0),(25,0),
        (26,0),(16,0),(19,0),(-1,37),(-1,38),(-1,29),(-1,28),(-1,33),(-1,32),(-1,30),(-1,31),(-1,34),(-1,36),(5,0),(-1,55),
    };
    static readonly Dictionary<(int, int), int> OptionOrder = BuildOrder();

    static Dictionary<(int, int), int> BuildOrder()
    {
        var d = new Dictionary<(int, int), int>();
        for (int i = 0; i < OrderTable.Length; i++) d[OrderTable[i]] = i;
        return d;
    }

    /// <summary>OptionLess: by the fixed order, an option not in it last; two of those by their names.</summary>
    public static int CompareOptions((int stat, int special) a, (int stat, int special) b, Func<(int stat, int special), string> name)
    {
        int la = OptionOrder.TryGetValue(a, out int x) ? x : int.MaxValue;
        int lb = OptionOrder.TryGetValue(b, out int y) ? y : int.MaxValue;
        if (la == int.MaxValue && lb == int.MaxValue) return string.CompareOrdinal(name(a), name(b));
        return la.CompareTo(lb);
    }

    /// <summary>OptionLess::GetName: the stat's common text, else the special's name.</summary>
    string OptionName((int stat, int special) o) =>
        o.stat != -1 ? Model.Tables.StatName(o.stat) : o.special != 0 ? Model.Tables.SpecialAbilityName(o.special) : "";

    static string RatePercent(float rate) => ClientFormat.Format("{0:0.1f}", rate * 100.0f) + "%";

    string StatValue(int stat, int v) => Model.Tables.ItemOptions.IsStatRate(stat) ? RatePercent(v / 1000.0f) : v.ToString(CultureInfo.InvariantCulture);
    string SpecialValue(int sp, int v) => Model.Tables.ItemOptions.IsSpecialRate(sp) ? RatePercent(v / 1000.0f) : v.ToString(CultureInfo.InvariantCulture);

    string StatOptionText(int stat, int value) => (Model.Tables.StatName(stat) + " " + StatValue(stat, value)).Trim();
    string SpecialOptionText(int sp, int value) => (Model.Tables.SpecialAbilityName(sp) + " " + SpecialValue(sp, value)).Trim();

    /// <summary>GetCurrentOption: the item's first valued group 4 stat, else its first valued special; "" without one.</summary>
    string CurrentOptionText(Item item)
    {
        var o = item?.Record?.Options;
        if (o == null || Model == null) return "";
        foreach (var (stat, v) in o.Stat[4]) return StatOptionText(stat, v);
        foreach (var (sp, v) in o.Special[4]) return SpecialOptionText(sp, v);
        return "";
    }

    /// <summary>ShowOptionPreview: the normal and the special ranges sorted by <see cref="CompareOptions"/>, one line each as
    /// "name min~max" in the current-option wrapper, joined with newlines; setOptionPreview {title, options}.</summary>
    public void ShowOptionPreview()
    {
        if (Host?.Entity == null || Model == null) return;
        string Lines(MergeOptionRange[] ranges, bool special)
        {
            var rows = ranges.Select(r => (key: special ? (-1, (int)r.Key) : ((int)r.Key, 0), r)).ToList();
            rows.Sort((a, b) => CompareOptions(a.key, b.key, OptionName));
            return string.Join("\n", rows.Select(row =>
            {
                string min = special ? SpecialValue(row.key.Item2, row.r.Min) : StatValue(row.key.Item1, row.r.Min);
                string max = special ? SpecialValue(row.key.Item2, row.r.Max) : StatValue(row.key.Item1, row.r.Max);
                string line = ClientFormat.Format("{0} {1}~{2}", OptionName(row.key), min, max);
                return ClientFormat.Format(T("s_item_merge_option_current"), line);
            }));
        }
        string normal = Lines(Proxy.Normal, false), spec = Lines(Proxy.Special, true);
        string options = normal.Length > 0 && spec.Length > 0 ? normal + "\n" + spec : normal + spec;
        Host.Invoke(Host.Entity, "setOptionPreview", Host.Obj(("title", T("s_item_merge_option_grade_current")), ("options", options)));
    }

    /// <summary>ShowMergePrice: the merge button's label is the meso cost text.</summary>
    public void ShowMergePrice()
    {
        if (Host?.Entity == null) return;
        if (Host.Child("m_merge") is { } b)
            Host.Invoke(b, "setLabel", ClientFormat.Format(T("s_item_merge_option_consume_meso"), Proxy.Meso.ToString("N0", CultureInfo.InvariantCulture)));
    }
}
