using Ms2.Avm2;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIGameDictionaryDialog (UI id 0x4f0b, ms2/client/ms2/uigamedictionarydialog.cpp): the game menu's dictionary. Its list
/// holds the dictionary's npcs, monsters and items (GameDictionaryManager: Table/monsterWebfinder_Npc.xml,
/// monsterWebfinder_Mob.xml, itemWebfinder.xml) sorted by name, filtered by the tab and the search text; the selected
/// entry's detail is an npc's kind and field, a monster's grade, race and fields or an item's tooltip. The dialog object
/// is kept between opens (the tab and the search text carry over).
/// </summary>
/// <remarks>
/// STILL-OPEN: the search text goes through the platform's text filter (IClientPlatform vfunc_37, block list 0x4f0b,
/// "m_inputSearch"); offline it passes (result 1), so the async answer (vfunc_80) and its message box 0x8b7 are not
/// reached. A boss's spawn guarantee time (class 4: CFieldMetaTable::FUN_1406fc620 and s_spawnnpc_guarantee) is fed by
/// the server and not ported, so the boss's maps have no time. The "npcdictionary" and "itemdictionary" text links
/// (UIObject, uiobject.cpp:149-168) call <see cref="OpenTabWithSearch"/>; the link handler is not in this port yet.
/// </remarks>
public sealed class GameDictionaryController : UiController
{
    /// <summary>UIGameDictionaryDialog::Entry (@0x140f47d70): the list row of an npc (type 1), monster (2) or item (3).
    /// <see cref="LevelOrGrade"/> is a monster's level (GameDictionaryManager::SetMobLevel: the npc's class) or an item's grade.</summary>
    public sealed record Entry(string Name, int Id, int Type, int LevelOrGrade, int InvisibleLevel, int FieldId);

    public override string TypeName => "GameDictionaryDialog";      // GetTypeName (@0x140f44900)
    public override string Movie => "uigamedictionarydialog";
    protected override string EntityMethod => "searchResult";       // GameDictionaryDialog

    /// <summary>CReservedWordTable: the dictionary's tables (GameDictionaryManager), the field search flags and ReplaceBoldWords.</summary>
    public Ms2.Quest.ReservedWords Words;
    /// <summary>FindNpcData.</summary>
    public NpcTable Npcs;
    /// <summary>CFieldMetaTable's npc fields (FindNpcField, GetNpcMapSearchFields) and parent fields.</summary>
    public FieldMeta Meta;
    /// <summary>FUN_1406fbb50 for an (npc, field) pair with a time window: the server time is inside it.</summary>
    public Func<int, int, bool> NpcFieldOpen = (_, _) => false;
    /// <summary>WorldService::GetLocalPlayer as the item tooltip's owner (character id, name); null: no local player.</summary>
    public Func<(long Id, string Name)?> Owner = () => null;

    int tab;                                   // m_178 (ctor 0)
    int selectFirst;                           // m_17c (ctor 0)
    string search = "";                        // m_180 (ctor "")
    readonly List<Entry> entries = new();      // m_188

    /// <summary>The list rows (m_188) as CollectEntries made them.</summary>
    public IReadOnlyList<Entry> Entries => entries;

    /// <summary>OnLoadComplete (@0x140f449a0): the entries, the search text, the tab (its change fills the list), then the
    /// first row selected when an open asked for it.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        CollectEntries();
        if (Host.Child("m_inputSearch") is { } input) Host.Set(input, "text", search);      // SetTextW
        if (Host.Child("m_tab") is { } bar) Host.Set(bar, "selectedIndex", tab);
        SelectFirstRow();
    }

    /// <summary>vfunc_28 (@0x140f44c70), after UIDialog's: OnSearchItem, OnChangeTab, OnItemChange.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        object Arg(int i) => args != null && i < args.Length ? args[i] : null;
        bool any = args is { Length: > 0 };       // ASData::SetArgsIfAny
        if (name.Equals("OnSearchItem", StringComparison.OrdinalIgnoreCase))
        {
            if (any) Search(Rt.ToString(Arg(0)));
        }
        else if (name.Equals("OnChangeTab", StringComparison.OrdinalIgnoreCase))
        {
            if (any)
            {
                tab = Rt.ToInt32(Arg(0));          // PopInt
                search = Str(Arg(1));
                Fill();
            }
        }
        else if (name.Equals("OnItemChange", StringComparison.OrdinalIgnoreCase))
        {
            if (any) SetDetailInfo(Rt.ToInt32(Arg(0)), Rt.ToInt32(Arg(1)), Str(Arg(2)), Rt.ToInt32(Arg(3)));
        }
        else return false;
        return true;
    }

    /// <summary>
    /// vfunc_13 (@0x140f44f20), Enter (DIK 0x1c): only when this dialog is the top one (UISwfEntityManager FUN_1422d4e20:
    /// <see cref="UiModel.TopLayer"/> is this dialog's key); the focused search box runs the search. True = taken: Enter
    /// does nothing else then.
    /// </summary>
    public bool EnterKey()
    {
        if (Host?.Entity == null || IsClosed) return false;
        if (Model.TopLayer != Host.Id) return false;
        if (Host.Child("m_inputSearch") is { } input && Rt.ToBoolean(Host.Get(input, "focused")))
            Search(Rt.ToString(Host.Get(input, "text")));
        return true;
    }

    /// <summary>
    /// OpenTabWithSearch (@0x140f46ea0): the tab and search text for the next fill, the first row selected when
    /// <paramref name="selectFirstRow"/>. An open dialog refills now (the same tab) or switches tabs, which refills.
    /// </summary>
    public void OpenTabWithSearch(int newTab, string text, bool selectFirstRow)
    {
        selectFirst = selectFirstRow ? 1 : 0;
        search = text ?? "";
        if (Host?.Entity != null && Host.Child("m_inputSearch") is { } input) Host.Set(input, "text", search);
        if (Host?.Entity != null && !IsClosed)     // vfunc_55() == 0
        {
            if (Host.Child("m_tab") is { } bar)
            {
                if (tab == newTab) Fill();
                else Host.Set(bar, "selectedIndex", newTab);
            }
            SelectFirstRow();
        }
        tab = newTab;
    }

    /// <summary>The select-first block of OnLoadComplete and OpenTabWithSearch: with m_17c set and a list, the first row
    /// is selected when there is one (SetSelectedIndex(0), vfunc_24(1)); m_17c is cleared once the list is there.</summary>
    void SelectFirstRow()
    {
        if (selectFirst == 0 || Host.Child("m_items") is not { } list) return;
        var dp = Host.Get(list, "dataProvider");
        if (!Rt.IsNullish(dp) && Rt.ToInt32(Host.Get(dp, "length")) > 0)
        {
            Host.Set(list, "selectedIndex", 0);
            Host.Invoke(list, "invalidate");
            Host.Invoke(list, "validateNow");
        }
        selectFirst = 0;
    }

    /// <summary>
    /// CollectEntries (@0x140f452e0): GetAll(1), (2) and (3) in id order, an npc or monster named by FUN_1401aa6a0 (title and
    /// name), an item by string table 1; an entry without a name is left out. Then std::list::sort (stable) by
    /// Entry::operator&lt; (@0x140f48150), CStringW::Compare: ordinal.
    /// </summary>
    void CollectEntries()
    {
        entries.Clear();
        if (Words == null) return;
        var t = Model.Tables;
        foreach (var d in Words.DictionaryNpcs)
            if (t.NpcNameWithTitle(d.Id) is { Length: > 0 } name) entries.Add(new Entry(name, d.Id, 1, 0, 0, 0));
        foreach (var d in Words.DictionaryMobs)
            if (t.NpcNameWithTitle(d.Id) is { Length: > 0 } name)
                entries.Add(new Entry(name, d.Id, 2, MobLevel(d.Id), d.InvisibleLevel, d.FieldId));
        foreach (var d in Words.DictionaryItems)
            if (t.ItemName(d.Id) is { Length: > 0 } name) entries.Add(new Entry(name, d.Id, 3, d.Grade, 0, 0));
        var sorted = entries.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
        entries.Clear();
        entries.AddRange(sorted);
    }

    /// <summary>GameDictionaryData m_level: 0, set by SetMobLevel (@0x1403b7d30) to the npc's m_nClass when its npcdata loads
    /// with the GameDictionary feature on and its friendly is not above 0 (npcdata.cpp:1476-1485).</summary>
    int MobLevel(int id) => Npcs?.Find(id) is { } n && n.Friendly <= 0 ? n.Class : 0;

    /// <summary>GetTabTypeMask (@0x140f46e40): tab (or entry type) 0 all, 1 npc, 2 monster, 3 item; another value all.</summary>
    static int TabTypeMask(int n) => n switch { 0 => 7, 1 => 1, 2 => 2, 3 => 4, _ => 7 };

    /// <summary>
    /// FUN_140f45870 (@0x140f45870): the list emptied (cleanUp) and unselected, then every entry of the tab whose name holds
    /// the search text (FUN_14083fec0, unit_14083f3d0_stringutilities.cpp:177: both lowered, then Find), vfunc_24(1), and
    /// searchResult(found).
    /// </summary>
    void Fill()
    {
        if (Host.Child("m_items") is not { } list) return;
        var dp = Host.Get(list, "dataProvider");
        if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");       // ASCoreList FUN_1422dd1d0
        Host.Set(list, "selectedIndex", -1);
        int mask = TabTypeMask(tab);
        bool found = false;
        foreach (var e in entries)
        {
            if ((TabTypeMask(e.Type) & mask) == 0) continue;
            if (e.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!Rt.IsNullish(dp))
                Host.Invoke(dp, "push", Host.Obj(("label", e.Name), ("id", e.Id), ("itemGrade", e.LevelOrGrade), ("type", e.Type)));   // AddItem
            found = true;
        }
        Host.Invoke(list, "invalidate");       // vfunc_24(1)
        Host.Invoke(list, "validateNow");
        Host.Invoke(Host.Entity, "searchResult", found);
    }

    /// <summary>Search (@0x140f46c90): an empty text shows the whole tab; another text is filtered first (vfunc_37: offline
    /// result 1, see the remarks) and then becomes the search text.</summary>
    void Search(string text)
    {
        text ??= "";
        if (text.Length == 0)
        {
            search = "";
            Fill();
            return;
        }
        search = text;
        Fill();
    }

    /// <summary>
    /// SetDetailInfo (@0x140f45c20): the entry matching all four values, its name, id, itemGrade and type and per type: an
    /// npc's content (its field, the field's outdoor parent, its kind) and portrait; a monster's level and grade line,
    /// content (race and maps) and portrait; an item's tooltip and icon. Then setDetailInfo(detail).
    /// </summary>
    void SetDetailInfo(int id, int type, string name, int grade)
    {
        var e = entries.FirstOrDefault(x => x.Id == id && x.Type == type && x.Name == name && x.LevelOrGrade == grade);
        if (e == null || Host.Entity == null) return;
        var t = Model.Tables;
        var detail = new Dictionary<string, object> { ["name"] = e.Name, ["id"] = e.Id, ["itemGrade"] = e.LevelOrGrade, ["type"] = e.Type };
        switch (e.Type)
        {
            case 1 when Npcs?.Find(e.Id) is { } npc:
            {
                uint field = FindNpcField(e.Id);                                         // 0 when none (nField = 0)
                uint outdoor = Meta != null && Meta.ParentFields.TryGetValue((int)field, out int p) ? (uint)p : 0;   // GetParentField
                string maps = outdoor != 0 ? $"$map:{field}$ > $map:{outdoor}$" : $"$map:{field}$";
                string content = ClientFormat.Format(t.Common("s_gamedictionary_npc_content") ?? "", maps, NpcKindName(npc.Kind));
                detail["content"] = Content(content);
                detail["iconPath"] = npc.Portrait ?? "";
                break;
            }
            case 2 when Npcs?.Find(e.Id) is { } npc:
            {
                string gradeName = t.Common(grade is >= 0 and < 9 ? GradeNames[grade] : 0);
                detail["npcInfo"] = e.InvisibleLevel != 0
                    ? ClientFormat.Format(t.Common("s_gamedictionary_monster_mini_info_invisible_level") ?? "", gradeName)
                    : ClientFormat.Format(t.Common("s_gamedictionary_monster_mini_info") ?? "", (ushort)npc.Level, gradeName);
                string race = t.RaceName(npc.Race) ?? "";                                // string table 0x11
                string maps = "";
                if (e.FieldId != 0) maps = $"$map:{(uint)e.FieldId}$";
                else
                    foreach (uint f in NpcMapSearchFields(e.Id))
                    {
                        if (f == (uint)e.Id) continue;
                        maps = maps.Length == 0 ? $"$map:{f}$" : maps + $", $map:{f}$";
                    }
                string content = ClientFormat.Format(t.Common("s_gamedictionary_monster_content") ?? "", race,
                                                     maps.Length == 0 ? t.Common(0x573) : maps);
                detail["content"] = Content(content);
                detail["iconPath"] = npc.Portrait ?? "";
                break;
            }
            case 3:
            {
                var item = Item.Create(t.Items, e.Id, e.LevelOrGrade);                   // FUN_14052b130(id, grade, 0)
                if (item != null && Model.HasLocalPlayer)
                {
                    var tip = new ItemTooltip(Model, 0x800) { Owner = Owner(), Localize = Localize };
                    tip.Fill(item);                                                     // FUN_1409e0d40
                    detail["tooltipData"] = tip.Data;
                    detail["iconPath"] = item.IconPath ?? "";
                }
                break;
            }
        }
        Host.Invoke(Host.Entity, "setDetailInfo", Host.ToAs(detail));
    }

    /// <summary>The grade names by grade (aGradeNames, common string ids; the ninth is 0).</summary>
    static readonly int[] GradeNames = { 0x573, 0xaf, 0xb0, 0xb1, 0xb3, 0xb5, 0xb4, 0xb2, 0 };

    /// <summary>ReplaceBoldWords, then the link colour #5c2b00 made #a0a0a0.</summary>
    string Content(string text) => (Words?.ReplaceBoldWords(text) ?? text).Replace("#5c2b00", "#a0a0a0");

    /// <summary>CFieldMetaTable::FindNpcField (@0x1406fb700, fieldmetatable.cpp:505): the first of the npc's fields whose
    /// time window, if it has one, holds (FUN_1406fbb50); 0 when none.</summary>
    uint FindNpcField(int npc)
    {
        if (Meta == null || !Meta.NpcFields.TryGetValue(npc, out var fields)) return 0;
        foreach (int f in fields)
            if (!Meta.TimedNpcFields.Contains((npc, f)) || NpcFieldOpen(npc, f)) return (uint)f;
        return 0;
    }

    /// <summary>CFieldMetaTable::GetNpcMapSearchFields (@0x1406fb3a0, fieldmetatable.cpp:447): the npc's fields in order,
    /// each once, whose time window holds, that CFieldData::FindField finds with m_bMapSearch set.</summary>
    List<uint> NpcMapSearchFields(int npc)
    {
        var list = new List<uint>();
        if (Meta == null || !Meta.NpcFields.TryGetValue(npc, out var fields)) return list;
        foreach (int f in fields)
        {
            if (list.Contains((uint)f)) continue;
            if (Meta.TimedNpcFields.Contains((npc, f)) && !NpcFieldOpen(npc, f)) continue;
            if (Words == null || !Words.FieldMapSearch(f)) continue;
            list.Add((uint)f);
        }
        return list;
    }

    /// <summary>GetNpcKindName (@0x1406cd420, npcdata.cpp:1062): the common string of an npc kind (GetStringByKey,
    /// commonutil.cpp:2214: FUN_14028dfa0 with no arguments).</summary>
    string NpcKindName(int kind) => ClientFormat.Format(Model.Tables.Common(kind switch
    {
        2 => "s_gamedictionary_npc_type_bank",
        > 10 and < 30 => "s_gamedictionary_npc_type_shop",
        > 30 and < 40 => "s_gamedictionary_npc_type_beautyshop",
        0x50 => "s_gamedictionary_npc_type_job",
        0x51 => "s_gamedictionary_npc_type_doctor",
        0x52 => "s_gamedictionary_npc_type_transfer",
        0x57 => "s_gamedictionary_npc_type_enchant",
        _ => "s_gamedictionary_npc_type_normal",
    }) ?? "");

    string Localize(string key) => Host.Player.Translate?.Invoke(key) ?? key;

    static string Str(object v) => Rt.IsNullish(v) ? "" : Rt.ToString(v);
}
