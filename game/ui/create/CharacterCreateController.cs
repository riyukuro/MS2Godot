using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Ui;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// The customize page's C++ side (UICharacterCreateDialog, uicameraeditdialog.cpp:1243-2448) for uicharactercreatedialog,
/// which runs as the client's own movie. The lobby actor is <see cref="Look"/> (the create mode preview PC); the
/// Customize object's edit item is the item the open category last selected (Customize::SelectSlot / SelectId).
/// Not ported: the camera edit dialog's per-category camera (m_pCamera, FUN_1418de980), the actor's emotes (0x4d, 0x52,
/// 0x54), the 3D drag handles of the customize controllers (vfunc_1..3) and the mouse guide dialog (0x4ea7), which the
/// host gets as <see cref="GuideAsked"/>.
/// </summary>
public sealed class CharacterCreateController : UiController
{
    public override string Movie => "uicharactercreatedialog";
    protected override string EntityMethod => "onChangeGenderComplete";

    /// <summary>The constructor leaves the tween off (UIDialog m_c4 stays 0): no startOpen; the movie's popup opens it.</summary>
    protected override bool IsDialog => false;

    const int Categories = 7;
    // m_nState: 0 closed, 1 input, 3 request sent, 4 closing after the create
    int state;
    readonly bool[] loaded = new bool[Categories];                   // m_aLoaded
    readonly List<CreateLook.Row> rows = new();                       // m_aRows
    readonly Dictionary<int, (int Row, CustomValues Values)> items = new();   // m_items: the item clones kept by id
    int tab = -1, category = -1;                                      // m_nTab, m_nCategory
    bool currentBuilt, tabChanged, guideOpened, customize;            // m_bCurrentBuilt, m_bTabChanged, m_bGuideOpened, m_spCustomize
    int curItemId;                                                    // m_nCurItemId
    CreateLook backUp;                                                // m_spBackUp
    double openTime, elapsed;                                         // m_dOpenTime, m_dElapsed
    int changedMask;                                                  // m_nChangedMask
    // Customize's edit item (EditItems m_spData) and the slot it sits in (none for the skin colour item)
    int editItem;
    EquipSlot? editSlot;

    /// <summary>The lobby actor.</summary>
    public CreateLook Look;
    /// <summary>FUN_141900750's object (the server line).</summary>
    public LobbyServerInfo Server;
    /// <summary>UICharacterListDialog::GetCharacterCount() == 0: the mouse guide opens by itself.</summary>
    public bool NoCharacters;
    /// <summary>CMainSystem::GetGameTime in seconds.</summary>
    public Func<double> Clock = () => Environment.TickCount64 / 1000.0;

    /// <summary>The actor's look changed: draw it again.</summary>
    public event Action LookChanged;
    /// <summary>OnCreate: the name box (FUN_140d73e40(10, 1), input up to 12 characters); its OK goes to <see cref="SubmitName"/>.</summary>
    public event Action NameAsked;
    /// <summary>UIMessageBox::UpdateErrorMessage on the open name box.</summary>
    public event Action<string> NameError;
    /// <summary>FUN_1418e3f50: the create request (sent by the host).</summary>
    public event Action<CreateCharacter> CreateRequested;
    /// <summary>OnEndPopdown: PostMessage 0x4c8, back to the lobby's select scene.</summary>
    public event Action Back;
    /// <summary>OpenDialog(0x4ea7), the mouse guide.</summary>
    public event Action GuideAsked;
    /// <summary>Common strings by key (GetStringCommon), for the name check message.</summary>
    public Func<string, string> Common = _ => null;
    /// <summary>FUN_141276a30: the name check, null when the name passes, else its message.</summary>
    public Func<string, string> CheckName = _ => null;

    public int State => state;
    public int Category => category;
    public int EditItem => editItem;

    public override void OnLoadComplete()
    {
        customize = true;
        Array.Clear(loaded);
        changedMask |= 1 << Look.Gender;
        var init = Server is { } s ? Host.Obj(("name", s.Name ?? ""), ("iconPath", s.IconPath ?? "")) : Host.Obj();
        Host.Invoke(Host.Entity, "initData", Look.Gender, init);
        if (NoCharacters && !guideOpened)
        {
            GuideAsked?.Invoke();
            guideOpened = true;
        }
        openTime = Clock();
        state = 1;
    }

    static int Int(object[] a, int i) => i < a.Length ? (int)Rt.ToNumber(a[i]) : 0;

    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onscenelobbyselect": OnSceneLobbySelect(); return true;
            case "oncreate": OnCreate(); return true;
            case "onchangegender": if (args.Length > 0) OnChangeGender(Int(args, 0)); return true;
            case "onrandom": if (args.Length > 0) OnRandom(Int(args, 0)); return true;
            case "onchangetab": if (args.Length > 0) { tab = Int(args, 0); OnChangeTab(tab); } return true;
            case "onchangecategory":
                if (args.Length > 0) { tab = Int(args, 0); category = Int(args, 1); OnChangeCategory(tab, category); }
                return true;
            case "onchangeitem":
                // ParseInt of the string id
                if (args.Length > 0) OnChangeItem(int.TryParse(Rt.ToString(args[0]), out int id) ? id : 0);
                return true;
            case "onchangecolor": if (args.Length > 0) OnChangeColor(Int(args, 0)); return true;
            case "onchangedegree": if (args.Length > 0) OnChangeDegree(Int(args, 0)); return true;
            case "onchangeposition": if (args.Length > 0) OnChangePosition(Int(args, 0)); return true;
            case "onchangesize": if (args.Length > 0) OnChangeSize(Int(args, 0), args.Length > 1 ? (float)Rt.ToNumber(args[1]) : 0f); return true;
            case "onendpopdown": Back?.Invoke(); return true;
            case "onclickguide": GuideAsked?.Invoke(); return true;
        }
        return false;
    }

    /// <summary>vfunc_15, each frame: an item changed this frame is kept (m_nCurItemId).</summary>
    public void Update()
    {
        if (!customize) return;
        if (curItemId > 0) { Keep(curItemId); curItemId = 0; }
    }

    /// <summary>vfunc_13: Enter while the page takes input invokes onEnterKey (which calls onCreate).</summary>
    public bool OnEnterKey()
    {
        if (state != 1 || Host?.Entity == null) return false;
        Host.Invoke(Host.Entity, "onEnterKey");
        return true;
    }

    void OnSceneLobbySelect()
    {
        if (state != 1) return;
        Popdown();
    }

    void Popdown() { if (Host.Entity != null) Host.Invoke(Host.Entity, "popdown"); }

    void OnCreate()
    {
        if (state != 1) return;
        if (elapsed < 1.1920928955078125e-07) elapsed = Clock() - openTime;
        NameAsked?.Invoke();
    }

    /// <summary>
    /// The name box's OK (FUN_1418e7790: the text trimmed of ',', then FUN_1418e6430): an empty name gives common string
    /// 0xd and a refused one the check's message, both under the input; a good name commits the look and sends the request.
    /// Returns true when the request went out (the box closes).
    /// </summary>
    public bool SubmitName(string text)
    {
        if (state != 1) return false;
        var name = (text ?? "").Trim(',');
        if (name.Length == 0) { NameError?.Invoke(Common("s_char_err_input") ?? ""); return false; }
        if (CheckName(name) is { } error) { NameError?.Invoke(error); return false; }
        var packet = Look.Packet(name, (int)elapsed);
        CreateRequested?.Invoke(packet);
        state = 3;
        Host.Invoke(Host.Entity, "requestCreate");
        return true;
    }

    /// <summary>OnCreateCharacterAnswer after the message (the host shows it): failCreate, and the page takes input again.</summary>
    public void CreateRefused()
    {
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "failCreate");
        state = 1;
    }

    /// <summary>The 0xc answer with 1 (FUN_14151f200, then the page closes): state 4 and popdown.</summary>
    public void Created()
    {
        state = 4;
        Popdown();
    }

    void OnRandom(int gender)
    {
        if (!customize || gender is < 0 or >= 2 || state != 1) return;
        // CSceneStateMulti::FUN_14174ec40(gender, false): a new actor, every slot random
        Look.Create(gender, Look.JobCode, false);
        customize = false;
        ActorChanged();
    }

    void OnChangeGender(int gender)
    {
        if (!customize || gender is < 0 or >= 2 || state != 1) return;
        var old = backUp;
        backUp = Look.Snapshot();
        int bit = 1 << gender;
        if (old != null) Look.Restore(old);
        else Look.Create(gender, Look.JobCode, (changedMask & bit) == 0);
        curItemId = editItem;
        customize = false;
        changedMask |= bit;
        ActorChanged();
    }

    /// <summary>
    /// The new actor is in the field (vfunc_12): a new Customize, then vfunc_84: the loaded categories and kept items are
    /// dropped, buildCurrentItems, and onChangeGenderComplete (the movie reopens its category).
    /// </summary>
    void ActorChanged()
    {
        LookChanged?.Invoke();
        if (Host.Entity == null) return;
        customize = true;
        editItem = 0; editSlot = null;
        ResetLoaded();
        BuildCurrentItems();
        Host.Invoke(Host.Entity, "onChangeGenderComplete");
    }

    /// <summary>FUN_1418e4750.</summary>
    void ResetLoaded()
    {
        currentBuilt = false;
        Array.Clear(loaded);
        items.Clear();
    }

    void OnChangeTab(int t)
    {
        if (Host.Entity == null) return;
        if (!currentBuilt) BuildCurrentItems();
        tabChanged = true;
    }

    void OnChangeCategory(int t, int c)
    {
        if (Host.Entity == null || !customize || t == -1 || c == -1) return;
        if (c >= 0 && c < Categories && !loaded[c])
        {
            CollectRows(t, c);
            BuildItems(t, c);
            loaded[c] = true;
        }
        SelectSlot(SlotKind(c));
    }

    /// <summary>FUN_1418e46c0: the equip slot a category edits (0 for the skin: no item there).</summary>
    static int SlotKind(int c) => c switch { 0 => 1, 1 => 2, 2 => 3, 4 => 8, 5 => 9, 6 => 0xb, _ => 0 };

    /// <summary>Customize::EditItems::SelectSlot: the item worn in the slot becomes the edit item (none in an empty slot).</summary>
    void SelectSlot(int kind)
    {
        var slot = (EquipSlot)kind;
        if (kind != 0 && Look.Items.TryGetValue(slot, out int id)) { editItem = id; editSlot = slot; }
        else { editItem = 0; editSlot = null; }
    }

    /// <summary>GetItemTabAndCategory: the category of an item (-1 for none).</summary>
    int CategoryOf(int id)
    {
        var slot = Look.ModelSlot(id);
        int sub = Look.Subtype(id);
        if (slot == EquipSlot.HR) return 0;
        if (slot == EquipSlot.FA) return 1;
        if (slot == EquipSlot.FD) return 2;
        if (id == CreateLook.SkinItem) return 3;
        if (sub == 0xe || sub == 0x16) return 4;
        if (sub == 0xf) return 5;
        if (sub == 0x11) return 6;
        return -1;
    }

    /// <summary>FUN_1418e4380: the item belongs in the category (an overall in the top and the bottom).</summary>
    bool InCategory(int t, int c, int id)
    {
        if (t != 0) return false;
        var slot = Look.ModelSlot(id);
        int sub = Look.Subtype(id);
        return c switch
        {
            0 => slot == EquipSlot.HR,
            1 => slot == EquipSlot.FA,
            2 => slot == EquipSlot.FD,
            3 => id == CreateLook.SkinItem,
            4 => sub == 0xe || sub == 0x16,
            5 => sub == 0xf || sub == 0x16,
            6 => sub == 0x11,
            _ => false,
        };
    }

    /// <summary>FUN_1418e47e0: the category's rows (the skin: the avatar colour item; else the slot's defaults that fit).</summary>
    void CollectRows(int t, int c)
    {
        rows.Clear();
        if (t == -1 || c == -1) return;
        if (t == 0 && c == 3) { rows.Add(new CreateLook.Row(CreateLook.SkinItem, 0, false)); return; }
        foreach (var r in Look.SlotRows((EquipSlot)SlotKind(c)))
        {
            // FUN_1418e6950: the item's gender limit is the actor's or both, and it belongs in the category
            int g = Look.GenderLimit(r.Id);
            if ((g == Look.Gender || g == 2) && InCategory(t, c, r.Id)) rows.Add(r);
        }
    }

    /// <summary>BuildItems: buildItems(rows, the index of the worn item; the skin row 0, else -1).</summary>
    void BuildItems(int t, int c)
    {
        if (rows.Count == 0) return;
        int select = c == 3 ? 0 : -1;
        var list = new List<object>();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Id != CreateLook.SkinItem && Look.ModelSlot(r.Id) is { } slot && Look.Items.TryGetValue(slot, out int worn) && worn == r.Id)
                select = i;
            var row = ItemInfo(r.Id);
            if (Look.IsOverall(r.Id)) row = With(row, ("linkCategory", c == 4 ? 5 : 4));
            list.Add(Host.Obj(With(row, ("saleTag", r.SaleTag))));
        }
        Host.Invoke(Host.Entity, "buildItems", list, select);
    }

    /// <summary>WriteItemInfo(row, data, true) (charactercustomizepayloadwriters).</summary>
    (string, object)[] ItemInfo(int id)
    {
        var slot = Look.ModelSlot(id);
        return new (string, object)[]
        {
            ("itemname", Look.ItemName(id)),
            ("itemid", id.ToString()),
            ("iconpath", Look.IconPath(id)),
            ("editable", Look.Customize(id).Editable),
            ("isSkinBg", slot is EquipSlot.FA or EquipSlot.FD),
            ("isHideSlot", id == CreateLook.SkinItem),
            ("itemmovie", Look.MovieOf(id)),
        };
    }

    static (string, object)[] With((string, object)[] a, (string, object) b) => a.Append(b).ToArray();

    /// <summary>The colour a worn item shows (FUN_141424580 then FUN_1414248a0: the palette row's visibleColor).</summary>
    uint VisibleColor(int id, int row) => (uint)CreateLook.Argb(Look.Visible(Look.Palette(id).Palette, row));

    /// <summary>buildCurrentItems: each shown item's category line (name, id, colour), then the skin's.</summary>
    void BuildCurrentItems()
    {
        if (Host.Entity == null || !customize) return;
        var list = new List<object>();
        foreach (var (slot, id) in Look.Items.OrderBy(kv => (int)kv.Key))
        {
            int c = CategoryOf(id);
            if (c == -1) continue;
            items[id] = (Look.RowOf(slot), Look.ValuesOf(id).Clone());
            var row = new List<(string, object)> { ("itemname", Look.ItemName(id)), ("itemid", (double)id) };
            if (Look.Palette(id).Palette > 0) row.Add(("itemcolor", (double)VisibleColor(id, Look.RowOf(slot))));
            row.Add(("tab", 0)); row.Add(("category", c)); row.Add(("linkCategory", Look.IsOverall(id) ? 5 : -1));
            list.Add(Host.Obj(row.ToArray()));
        }
        list.Add(Host.Obj(("itemname", ""), ("itemid", CreateLook.SkinItem), ("itemcolor", (double)VisibleColor(CreateLook.SkinItem, Look.SkinRow)),
            ("tab", 0), ("category", 3)));
        Host.Invoke(Host.Entity, "buildCurrentItems", list);
        currentBuilt = true;
    }

    void OnChangeItem(int id)
    {
        if (!customize || id == 0) return;
        Keep(editItem);
        if (id == CreateLook.SkinItem)
        {
            // Customize::EquipItemId of the avatar colour item changes nothing; SelectId makes it the edit item
            editItem = id; editSlot = null;
        }
        else
        {
            var slot = items.TryGetValue(id, out var kept) ? Look.Equip(id, kept.Row, kept.Values) : Look.Equip(id);
            editItem = id; editSlot = slot;
            LookChanged?.Invoke();
        }
        BuildColors(id);
        BuildCustom(id);
        tabChanged = false;
        curItemId = id;
        UpdateCurrentItem(id);
    }

    /// <summary>FUN_1418e7380: a clone of the item worn in that item's slot is kept under the id.</summary>
    void Keep(int id)
    {
        if (id == 0 || id == CreateLook.SkinItem) return;
        if (Look.ModelSlot(id) is not { } slot || !Look.Items.TryGetValue(slot, out int worn)) return;
        items[id] = (Look.RowOf(slot), Look.ValuesOf(worn).Clone());
    }

    /// <summary>BuildColors: the palette's rows {realColor, visibleColor}, the worn colour selected (row 0 when none matches).</summary>
    void BuildColors(int id)
    {
        var list = new List<object>();
        int select = 0;
        var (pal, _) = Look.Palette(id);
        if (pal > 0)
        {
            int cur = 0;
            if (id == CreateLook.SkinItem) cur = Real(pal, Look.SkinRow);
            else if (Look.ModelSlot(id) is { } slot && Look.Items.TryGetValue(slot, out int worn) && worn == id) cur = Real(pal, Look.RowOf(slot));
            var palette = Look.PaletteRows(pal);
            for (int i = 0; i < palette.Count; i++)
            {
                int real = CreateLook.Argb(palette[i].c0);
                list.Add(Host.Obj(("realColor", (double)(uint)real), ("visibleColor", (double)(uint)CreateLook.Argb(Look.Visible(pal, i)))));
                if (real == cur) select = i;
            }
        }
        Host.Invoke(Host.Entity, "buildColors", list, select);
    }

    int Real(int pal, int row) => row >= 0 && row < Look.PaletteRows(pal).Count ? CreateLook.Argb(Look.PaletteRows(pal)[row].c0) : 0;

    /// <summary>BuildCustom: the edit item's sliders (sizedata in reverse, reversed ranges flipped), angle and positions.</summary>
    void BuildCustom(int id)
    {
        if (!customize || id == 0 || id == CreateLook.SkinItem) return;
        var c = Look.Customize(id);
        if (!c.HasController) return;
        int flags = c.EditFlags;
        if (flags <= 0) return;
        var v = Look.ValuesOf(id);
        var data = new List<(string, object)>();
        if ((flags & 3) != 0)
        {
            int n = c.SliderCount;
            if (n > 0)
            {
                var sizes = new List<object>();
                for (int i = n - 1; i >= 0; i--)
                {
                    var r = c.RangeAt(i);
                    float cur = r.Display(v.GetSize(c, i));
                    sizes.Add(Host.Obj(("id", i), ("cur", (double)cur), ("min", (double)r.Min), ("max", (double)r.Max)));
                }
                data.Add(("sizedata", sizes));
            }
        }
        if ((flags & 4) != 0) data.Add(("degreedata", Host.Obj(("cur", (double)v.Degree))));
        if ((flags & 8) != 0)
        {
            // a decal's <decal> customs, else the second asset's (ItemCustomize.Positions)
            var presets = c.Positions;
            var pos = new List<object>();
            for (int i = 0; i < presets.Count; i++)
            {
                var p = new List<(string, object)> { ("id", i), ("iconpath", presets[i].Icon) };
                if (i == v.Pick) p.Add(("selected", 1));
                pos.Add(Host.Obj(p.ToArray()));
            }
            data.Add(("posdata", pos));
        }
        Host.Invoke(Host.Entity, "buildCustom", flags, Host.Obj(data.ToArray()));
    }

    void OnChangeColor(int index)
    {
        if (!customize || index < 0) return;
        // Customize::EditItems::ApplyColor: the edit item's palette row (the skin row for the avatar colour item)
        if (editItem == CreateLook.SkinItem) { if (index < Look.PaletteRows(Look.Palette(CreateLook.SkinItem).Palette).Count) Look.SkinRow = index; }
        else if (editSlot is { } slot && Look.Palette(editItem).Palette > 0 && index < Look.PaletteRows(Look.Palette(editItem).Palette).Count) Look.Rows[slot] = index;
        else return;
        LookChanged?.Invoke();
        curItemId = editItem;
    }

    ItemCustomize EditPart => editItem != 0 && editItem != CreateLook.SkinItem && Look.Customize(editItem) is { HasController: true } c ? c : null;

    void OnChangePosition(int n)
    {
        if (!customize || EditPart is not { } c) return;
        Look.ValuesOf(editItem).SetPosition(c, n);
        LookChanged?.Invoke();
        curItemId = editItem;
    }

    void OnChangeSize(int index, float size)
    {
        if (!customize || EditPart is not { } c) return;
        var r = c.RangeAt(index);
        if (r.Reverse) size = r.Max - size + r.Min;
        Look.ValuesOf(editItem).SetSize(c, index, size);
        LookChanged?.Invoke();
        curItemId = editItem;
    }

    void OnChangeDegree(float degree)
    {
        if (!customize || EditPart is null) return;
        Look.ValuesOf(editItem).SetDegree(degree);
        LookChanged?.Invoke();
        curItemId = editItem;
    }

    /// <summary>
    /// updateCurrentItem: a top, bottom or overall changed while the other slot (bottom for the top category, else the top)
    /// is empty or holds an overall: that category's line shows the item (cleared when the slot is empty).
    /// </summary>
    void UpdateCurrentItem(int id)
    {
        if (Host.Entity == null) return;
        int sub = Look.Subtype(id);
        if (sub is not (0xe or 0xf or 0x16)) return;
        int other = category == 4 ? 5 : 4;
        var slot = category == 4 ? EquipSlot.PA : EquipSlot.CL;
        bool empty = !Look.Items.TryGetValue(slot, out int worn);
        if (empty || Look.IsOverall(worn))
            Host.Invoke(Host.Entity, "updateCurrentItem", Host.Obj(("category", other), ("itemId", id.ToString()), ("clearItemData", empty)));
    }
}
