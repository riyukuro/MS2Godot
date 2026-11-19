using Ms2.Avm2;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIMasteryDialog (UI id 0x4f1e, ms2/client/ms2/uimasterydialog.cpp): the game menu's mastery dialog. The main tabs are
/// 0 gathering and 1 manufacturing (ported here), 2 housing and 3 magic scroll (not ported). Each main tab has sub-tabs, one
/// per mastery type; a sub-tab shows the level control (grade bar and the grade rewards) and the list of recipes, and a
/// click on a recipe the recipe page with its craft button. The craft box asks for the count, then each craft is a cast of the
/// casting bar that ends in a Mastery packet (mode 2).
/// </summary>
/// <remarks>
/// STILL-OPEN: main tab 2 (housing, furniture and the pet taming list) and main tab 3 (magic scroll): their sub-tabs, lists and
/// pages are not ported; those tabs show empty sub-tabs and their callbacks do nothing. The game menu's mastery count has no
/// housing part (FUN_141938ae0(0x4f1e) adds FUN_140930e10's housing grades, which need that tab).
/// STILL-OPEN: the equipment's ability rate (CEquipment::GetAbilityRate, types 3 to 6 adjust the reward count, types 7 to 10 the
/// mastery points shown) is not tracked, so it is 0 here and the numbers are the unadjusted ones.
/// STILL-OPEN: the home text of the production recipes of types 5 and 6 (the house service: "my home" or "other's home") needs
/// the house service, so only the success description shows.
/// STILL-OPEN: OnMastery mode 0's own-field refresh for types 5 and 6 (CFunctionCubeService::FUN_141232260, the housing
/// cubes, and WorldService::FUN_141bc5e20(6), the field's category 6 objects' state effects) and the mode 1/2 item lists for
/// the reward notice (URewardNotify 0x4e5d); the fishing, instrument and pet taming branches of mode 0 (their notices, the
/// fishing book 0x4ee6, the instrument dialog 0x4eec) are other systems'. The client's OnMastery returns early without a
/// local player; the live session raises these messages only with the own user block (LiveSession.MasteryReceived).
/// </remarks>
public sealed class MasteryController : UiController, IMessageBoxListener, ICastingEventListener
{
    public override string TypeName => "MasteryDialog";           // GetTypeName (@0x140eae470)
    public override string Movie => "uimasterydialog";
    protected override string EntityMethod => "setPage";

    /// <summary>The recipe list's row height (SetIntMember("rowHeight", 0x4c), FUN_140eb0da0:821).</summary>
    public const int ListRowHeight = 0x4c;

    /// <summary>Unk_MasteryCraft::m_nType: 0 a recipe craft (Mastery mode 2), 1 a magic scroll craft (not ported).</summary>
    public const int CraftRecipe = 0, CraftScroll = 1;

    /// <summary>Unk_MasteryResult (masterymanager.cpp): why a craft may not run. FUN_140b6d470's values: 1 the mastery is
    /// below the recipe's, 3 none of its required quests is in progress, 2 the mesos are short (DAT_143ab2898), 4 an
    /// ingredient is short, 5 no player.</summary>
    public enum CraftBlock { None = 0, Mastery = 1, Meso = 2, Quest = 3, Item = 4, NoPlayer = 5 }

    public MasteryRecipeTable Table = MasteryRecipeTable.Empty;
    public MasteryData Data = new();
    /// <summary>The casting bar (UI 0x4e72) the crafts run on.</summary>
    public CastingBarController CastingBar;
    /// <summary>CItemInventory counts: by item id (FUN_140a5f180) and by string tag (GetItemCountByTag).</summary>
    public Func<int, int> ItemCount = _ => 0;
    public Func<int, int> ItemCountByTag = _ => 0;
    /// <summary>FUN_140b6d330's quest test: the quest is in progress.</summary>
    public Func<uint, bool> IsQuestActive = _ => false;
    /// <summary>The habitat text's world map link (CMasteryReceipe::HabitatText).</summary>
    public Func<long, string, string> WorldMapLink = (_, name) => name;
    /// <summary>The debug mode flag (IsDebugMode) the page shows the debug button for.</summary>
    public Func<bool> IsDebugMode = () => false;
    /// <summary>ShowSystemNotice(text, 1, 1) (FUN_1412751f0): the craft result's notice, also a chat line.</summary>
    public Action<string> Notice;
    /// <summary>The count the box's text input holds (GetInputText @0x141a04da0); "" without the box's movie.</summary>
    public Func<MessageBoxData, string> CountInput = box => MessageBoxQueue.Layer?.InputText(box) ?? "";

    /// <summary>Unk_MasteryCraft's state (m_craft) for the tests: a craft is cast only once its total is set.</summary>
    public (int Type, int Total, int Done, bool Pending) CraftState => (craft.Type, craft.Total, craft.Done, craft.Pending != 0);

    /// <summary>Mastery mode 1 with the reward id (<see cref="Ms2.Net.Packets.MasteryRewardRequest"/>).</summary>
    public event Action<int> SendReward;
    /// <summary>Mastery mode 2 with the recipe id (<see cref="Ms2.Net.Packets.MasteryCraftRequest"/>).</summary>
    public event Action<int> SendCraft;
    /// <summary>The chat input the debug button runs (UIMessengerBrowser::FUN_141a25770).</summary>
    public event Action<string> SendChatCommand;

    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    // ---- state ----------------------------------------------------------------------------------------------------

    const int Slots = 0xf;
    readonly int[] selectedIndex = new int[Slots];     // m_268: the selected list row per slot
    readonly int[] scroll = new int[Slots];            // m_scroll
    readonly int[] lastSelected = new int[4];          // m_lastSelectedIndex: the sub-tab per main tab
    readonly uint[] typeValue = new uint[12];          // m_310: the exp last shown per type
    int mainTab;                                       // m_nMainTab
    int selected;                                      // m_nSelected: the recipe on the page
    int pendingSub, pendingList;                       // m_nPendingSub, m_nPendingList
    bool restoring;                                    // m_304
    readonly Craft craft = new();

    ItemSlotBinder reward, require, mastery;

    /// <summary>Unk_MasteryCraft: the craft in progress. A craft has a total once the count was entered.</summary>
    sealed class Craft
    {
        public int Type = 2, Total, Done, Id, Pending;
        public void Reset() { Type = 2; Total = 0; Done = 0; Id = 0; Pending = 0; }
        public bool HasTotal => Total != 0;
    }

    Func<int, Items.ItemData> ItemData => id => Model.Tables.Items.Get(id);

    ItemSlotBinder Reward => reward ??= new ItemSlotBinder(Model, "MasteryDialog_Reward") { IconType = 0x11 };
    ItemSlotBinder Require => require ??= new ItemSlotBinder(Model, "MasteryDialog_Require") { IconType = 1 };
    ItemSlotBinder Mastery => mastery ??= new ItemSlotBinder(Model, "MasteryDialog_Mastery") { IconType = 0x11 };

    string Common(string key) => Model.Tables.Common(key) ?? "";

    // ---- lifecycle ------------------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_67 (@0x140eae520), the close: the cast bar cancelled while a craft has its total (FUN_1418e1390), the craft
    /// reset, the three binders cleared, the selection forgotten; then the movie's remembered sub-tabs (the
    /// m_lastSelectedIndex array, an int each, 0 for a non-int) and main tab read back for the next open.
    /// </summary>
    public override void Close()
    {
        if (!IsClosed && Host?.Entity != null)
        {
            if (craft.HasTotal) CastingBar?.Cancel();
            craft.Reset();
            reward?.Clear(); require?.Clear(); mastery?.Clear();
            selected = 0;
            if (Host.Get(Host.Entity, "m_lastSelectedIndex") is Ms2.Avm2.AsArray last)
                for (int i = 0; i < lastSelected.Length; i++)
                {
                    object v = i < last.Items.Count ? last.Items[i] : null;
                    lastSelected[i] = v is int n ? n : 0;            // Value::IsInt (FUN_140ebb1e0), else 0
                }
            if (Host.Child("m_mainTab") is { } tab) mainTab = Rt.ToInt32(Host.Get(tab, "selectedIndex"));
        }
        base.Close();
    }

    /// <summary>
    /// OnLoadComplete (@0x140eae8a0): UIDialog's, the web guide button hidden, the remembered sub-tabs handed to the movie
    /// (m_lastSelectedIndex), the main tab selected, the magic scroll tab hidden without feature 0x5a, the tab marks. The
    /// close button is the movie's own (m_close's click runs its onCloseDialog, which ends in OnEndClose); the client
    /// never binds it here.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host.Child("m_webGuideButton") is { } web) Host.Set(web, "visible", false);
        Host.Set(Host.Entity, "m_lastSelectedIndex", lastSelected.Select(i => (object)i).ToList());
        if (Host.Child("m_mainTab") is { } tab) Host.Invoke(tab, "setSelectedButtonByIndex", mainTab);
        if (!Model.IsFeatureEnabled(0x5a) && Host.Child("m_magicScroll") is { } scrollTab) Host.Set(scrollTab, "visible", false);
        UpdateMainTabRewards();
    }

    /// <summary>SetMainTab (@0x140eafde0 neighbour): the tab the next open selects.</summary>
    public void SetMainTab(int tab) => mainTab = tab;

    // ---- the tabs -------------------------------------------------------------------------------------------------

    int MainTabIndex() =>
        Host?.Entity == null || Host.Child("m_mainTab") is not { } t ? -1 : Rt.ToInt32(Host.Get(t, "selectedIndex"));

    int SubTabIndex() =>
        Host?.Entity == null || Host.Child("m_subTab") is not { } t ? -1 : Rt.ToInt32(Host.Get(t, "selectedIndex"));

    /// <summary>FUN_140eb86b0 (@0x140eb86b0): the slot of the selected sub-tab; 0xe for none.</summary>
    int CurrentSlot()
    {
        int tab = MainTabIndex(), sub = SubTabIndex();
        if (sub < 0) return 0xe;
        var slots = new List<int>();
        switch (tab)
        {
            case 0: slots.Add(0); slots.Add(1); if (Model.IsFeatureEnabled(0x59)) { slots.Add(2); slots.Add(3); } break;
            case 1: slots.Add(5); slots.Add(4); if (Model.IsFeatureEnabled(0x59)) { slots.Add(6); slots.Add(7); } break;
            case 2: slots.Add(8); if (Model.IsFeatureEnabled(0x8d)) slots.Add(9); break;
            case 3:
                if (Model.IsFeatureEnabled(0x5a))
                {
                    if (Model.IsFeatureEnabled(0x7c)) slots.Add(10);
                    if (Model.IsFeatureEnabled(0x7d)) slots.Add(0xb);
                    if (Model.IsFeatureEnabled(0x7e)) slots.Add(0xc);
                    if (Model.IsFeatureEnabled(0x7f)) slots.Add(0xd);
                }
                break;
        }
        return sub < slots.Count ? slots[sub] : 0xe;
    }

    /// <summary>GetTypeForSlot (@0x140eb89e0).</summary>
    public static int TypeForSlot(int slot) => slot switch
    {
        0 => 3, 1 => 5, 2 => 4, 3 => 6, 4 => 8, 5 => 7, 6 => 9, 7 => 10, 9 => 0xb, _ => 0,
    };

    /// <summary>FUN_140eb8a90: the type of the selected sub-tab.</summary>
    int CurrentType() => TypeForSlot(CurrentSlot());

    /// <summary>GetMasteryTabName (@0x140eb7c50): the type's name, with the claimable reward count when there is one.</summary>
    string MasteryTabName(int type)
    {
        string name = MasteryRecipeTable.TypeName(type, Common);
        int n = ClaimableRewardLevels(type);
        return n != 0 ? ClientFormat.Format(Common("s_mastery_subtab_mastery_name_and_reward"), name, n) : name;
    }

    /// <summary>CMasteryManager::HasRewards: the grade has rewards for the player's job group.</summary>
    bool HasRewards(int type, int level) =>
        Table.Rewards(MasteryRecipeTable.MakeID(type, level), Pc.JobGroup, ItemData).Count > 0;

    /// <summary>CMasteryManager::CountClaimableRewardLevels: the grades reached whose rewards were not received.</summary>
    public int ClaimableRewardLevels(int type)
    {
        int n = 0;
        uint exp = Data.Get(type);
        for (int level = 1; level <= Table.MaxGrade(type); level++)
        {
            if (exp < Table.Value(type, level)) continue;
            if (Data.Received.TryGetValue((uint)MasteryRecipeTable.MakeID(type, level), out var got) && got != 0) continue;
            if (HasRewards(type, level)) n++;
        }
        return n;
    }

    /// <summary>CMasteryManager::GetTotalClaimableRewardLevels (masterymanager.cpp:78): the claimable count of all types,
    /// the game menu's mastery count (FUN_141938ae0(0x4f1e)) without the housing grades (FUN_140930e10, STILL-OPEN).</summary>
    public int TotalClaimableRewardLevels()
    {
        int n = 0;
        for (int type = 0; type < 12; type++) n += ClaimableRewardLevels(type);
        return n;
    }

    /// <summary>
    /// UpdateSubTabs (@0x140eafde0): once the movie is loaded, the sub-tab bar emptied and one label per mastery type of the
    /// main tab (the second pair with feature 0x59). Tabs 2 and 3 are not ported: their bar stays empty.
    /// </summary>
    public void UpdateSubTabs()
    {
        if (Host?.Entity == null || Host.Child("m_subTab") is not { } bar) return;
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        void Add(string label) => Host.Invoke(dp, "push", Host.Obj(("label", label)));
        bool more = Model.IsFeatureEnabled(0x59);
        switch (MainTabIndex())
        {
            case 0:
                Add(MasteryTabName(3)); Add(MasteryTabName(5));
                if (more) { Add(MasteryTabName(4)); Add(MasteryTabName(6)); }
                break;
            case 1:
                Add(MasteryTabName(7)); Add(MasteryTabName(8));
                if (more) { Add(MasteryTabName(9)); Add(MasteryTabName(10)); }
                break;
        }
        Host.Set(bar, "dataProvider", dp);
        Host.Invoke(bar, "validateNow");
    }

    /// <summary>FUN_140eb7e30 (@0x140eb7e30): the red dot of each main tab (a reward to claim).</summary>
    public void UpdateMainTabRewards()
    {
        if (Host?.Entity == null) return;
        bool more = Model.IsFeatureEnabled(0x59);
        int a = ClaimableRewardLevels(3) + ClaimableRewardLevels(5) + (more ? ClaimableRewardLevels(4) + ClaimableRewardLevels(6) : 0);
        Host.Invoke("setMainTabReward", 0, a > 0);
        int b = ClaimableRewardLevels(7) + ClaimableRewardLevels(8) + (more ? ClaimableRewardLevels(9) + ClaimableRewardLevels(10) : 0);
        Host.Invoke("setMainTabReward", 1, b > 0);
        Host.Invoke("setMainTabReward", 2, false);      // housing + taming: not ported
        Host.Invoke("setMainTabReward", 3, false);
    }

    /// <summary>FUN_140eb4c10 (@0x140eb4c10): a mastery changed (OnMastery): the tabs redone on request, the page refreshed in the next frame.</summary>
    public void OnMasteryChanged(int type, bool refreshTabs)
    {
        if (Host?.Entity != null && refreshTabs)
        {
            UpdateSubTabs();
            UpdateMainTabRewards();
        }
        if (type == CurrentType()) SetPendingSub(type);
    }

    /// <summary>FUN_140eb4be0 (@0x140eb4be0): the sub-tabs and the main tab marks redone, no pending refresh (a reward was
    /// received, OnMastery mode 1).</summary>
    public void UpdateSubTabsAndRewards()
    {
        if (Host?.Entity == null) return;
        UpdateSubTabs();
        UpdateMainTabRewards();
    }

    /// <summary>SetPendingSub (@0x140eb5260): ignored while closed or for another sub-tab's type.</summary>
    public void SetPendingSub(int type)
    {
        if (IsClosed || CurrentType() != type) return;
        pendingSub = type;
    }

    /// <summary>SetPendingList (@0x140eb52c0).</summary>
    public void SetPendingList(int type)
    {
        if (IsClosed || CurrentType() != type) return;
        pendingList = type;
    }

    /// <summary>FUN_140eb6660 (the item-event listener): a changed item the tab-1 recipes use refreshes the list in the next frame (-1: an unknown item).</summary>
    public void OnItemChanged(int itemId)
    {
        if (MainTabIndex() != 1) return;
        int type = CurrentType();
        if (itemId < 0 || Table.HasTypeIngredient(type, itemId, ItemData)) SetPendingList(type);   // < 0: the item is gone, unknown
    }

    /// <summary>OnDamagedMyPC (@0x140eb5320): a hit (negative total damage) cancels the craft.</summary>
    public void OnDamaged(long totalDamage)
    {
        if (totalDamage < 0) CancelCraft();
    }

    // ---- the movie's calls ----------------------------------------------------------------------------------------

    /// <summary>
    /// vfunc_28 (@0x140eaf090), after UIDialog's: onSelectCategory (sub-tabs), onSelectMastery (type), onSelectList (recipe),
    /// onReceiveItem (tabs 0 and 1: Mastery mode 1), onCraftOk (tab 1: the craft box), onDebug (the chat command),
    /// onScrollPosition.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        bool Is(string n) => name.Equals(n, StringComparison.OrdinalIgnoreCase);
        bool any = args is { Length: > 0 };
        if (Is("onSelectCategory")) { if (any) UpdateSubTabs(); return true; }
        if (Is("onSelectMastery"))
        {
            if (any && MainTabIndex() is 0 or 1) SelectMastery(CurrentType(), false);
            return true;
        }
        if (Is("onSelectList")) { if (any) OnSelectList(Rt.ToInt32(args[0])); return true; }
        if (Is("onReceiveItem"))
        {
            if (any && MainTabIndex() is 0 or 1) SendReward?.Invoke((int)Rt.ToUint32(args[0]));
            return true;
        }
        if (Is("onCraftOk"))
        {
            if (any && MainTabIndex() == 1) OpenCraftBox(Rt.ToInt32(args[0]));
            return true;
        }
        if (Is("onDebug"))
        {
            if (any && MainTabIndex() == 1) SendChatCommand?.Invoke($"/ManufacturingMastery {Rt.ToInt32(args[0])} ");
            return false;
        }
        if (Is("onScrollPosition"))
        {
            if (restoring) return true;
            if (any) scroll[CurrentSlot()] = Rt.ToInt32(args[0]);
            return true;
        }
        return false;
    }

    /// <summary>vfunc_49 (@0x140eaed30), after UIObject's: "masteryRecipe" on main tab 1: the recipe's first reward item as a tooltip.</summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        if (args is not { Length: > 1 } || Rt.ToString(args[0]) != "masteryRecipe") return null;
        if (MainTabIndex() != 1 || !Model.HasLocalPlayer) return null;
        if (Table.FirstItem(Rt.ToInt32(args[1])) is not { } first) return null;
        var item = Item.Create(Model.Tables.Items, first.ItemId, first.Rank);
        if (item == null) return null;
        var tip = new ItemTooltip(Model, 0x800);
        if (!tip.Fill(item)) return null;
        tip.Data.SetTooltipAlign(7);
        return tip.Data;
    }

    // ---- level control, list and page -----------------------------------------------------------------------------

    /// <summary>FUN_140eb05b0: the level control and the list of the selected sub-tab's type, when it is <paramref name="type"/>.</summary>
    void SelectMastery(int type, bool keep)
    {
        if (CurrentType() != type) return;
        UpdateLevelControl(type, keep);
        UpdateList(type, keep);
    }

    /// <summary>FUN_140eb0540: the page's binders cleared and the page removed.</summary>
    void ClearPage()
    {
        Reward.Clear(); Require.Clear();
        Host.Invoke("removePage");
    }

    /// <summary>FUN_140eb0610 (@0x140eb0610): the grade bar (setPcGrade, setPC) and, when the grade changed or not kept, the grade rewards.</summary>
    void UpdateLevelControl(int type, bool keep)
    {
        if (!Model.HasLocalPlayer || Host.Child("m_levelControl") is not { } level) return;
        uint old = typeValue[type], now = Data.Get(type);
        int grade = Table.Grade(type, now);
        bool max = grade == Table.MaxGrade(type);
        uint baseValue = Table.Value(type, grade), next = Table.Value(type, grade + 1);
        bool changed = now != old, gradeChanged = false;
        if (changed)
        {
            typeValue[type] = now;
            gradeChanged = grade != Table.Grade(type, old);
        }
        if (!keep || changed)
        {
            string text = ClientFormat.Format("{0} {1}", MasteryRecipeTable.TypeName(type, Common), MasteryRecipeTable.GradeName(grade, Common));
            Host.Invoke(level, "setPcGrade", (int)(now - baseValue), (int)(next - baseValue), text, max);
            if (Host.Get(level, "m_guideBar") is { } guide && !Rt.IsNullish(guide)) Host.Invoke(guide, "setPC", now);
        }
        if (!keep || gradeChanged)
        {
            Mastery.Clear();
            Host.Invoke(level, "removeGradeAll");
            var arr = new List<object>();
            for (int i = 1; i <= Table.MaxGrade(type); i++) arr.Add(GradeObject(type, i));
            Host.Invoke(level, "setGrade", arr, now);
        }
    }

    /// <summary>FUN_140eb6920 (@0x140eb6920): one grade of the level control.</summary>
    object GradeObject(int type, int index)
    {
        uint need = Table.Value(type, index);
        int id = MasteryRecipeTable.MakeID(type, index);
        bool got = Data.Received.TryGetValue((uint)id, out var flag) && flag != 0;
        string state = got || !HasRewards(type, index) ? "inactive" : Data.Get(type) >= need ? "active" : "lock";
        var fields = new List<(string, object)>
        {
            ("id", (uint)id), ("level", need), ("levelStr", MasteryRecipeTable.GradeName(index, Common)), ("state", state),
        };
        var items = Table.Rewards(id, Pc.JobGroup, ItemData);
        if (items.Count > 0) fields.Add(("binding", Mastery.BindNewItem(items[0].ItemId, items[0].Rank, items[0].Count)));
        return Host.Obj(fields.ToArray());
    }

    /// <summary>AddLevelGuide (@0x140eb0bf0): a grade pushed into the guide bar (a reward was received).</summary>
    public void AddLevelGuide(int type, int index)
    {
        if (!Model.HasLocalPlayer || Host?.Entity == null || Host.Child("m_levelControl") is not { } level) return;
        if (Host.Get(level, "m_guideBar") is { } guide && !Rt.IsNullish(guide)) Host.Invoke(guide, "addLevelGuide", GradeObject(type, index));
    }

    /// <summary>FUN_140b6d330: a recipe shows with no required quest or with one of them in progress.</summary>
    bool RecipeVisible(MasteryRecipeTable.Recipe r)
    {
        if (r.RequireQuests.Count == 0) return true;
        foreach (var q in r.RequireQuests) if (IsQuestActive(q)) return true;
        return false;
    }

    object RecipeRow(MasteryRecipeTable.Recipe r, int type, uint mastery)
    {
        var fields = new List<(string, object)>
        {
            ("id", r.Id), ("name", Model.Tables.StringTableText(0x2f, r.Id)), ("state", RecipeStateText(r)),
            ("active", mastery >= r.RequireMastery && HasRequiredLevel(r)), ("icon", r.IconPath),
        };
        if (type == 0xb && r.Rewards.Count > 0) fields.Add(("grade", r.Rewards[0].Rank));
        return Host.Obj(fields.ToArray());
    }

    /// <summary>FUN_140eb0da0 (@0x140eb0da0): the recipe list of a type; kept, the rows are redone in place.</summary>
    void UpdateList(int type, bool keep)
    {
        if (!Model.HasLocalPlayer) return;
        uint mastery = Data.Get(type);
        if (Host.Child("m_objectList") is not { } list)
        {
            if (!keep) ClearPage();
            return;
        }
        var recipes = Table.Recipes(type);
        if (keep)
        {
            int selectedRow = Rt.ToInt32(Host.Get(list, "selectedIndex"));
            if (selectedRow < recipes.Count)
            {
                int first = Rt.ToInt32(Host.Get(list, "scrollPosition"));
                var dp = Host.Get(list, "dataProvider");
                int row = 0;
                foreach (var r in recipes)
                {
                    if (!RecipeVisible(r)) continue;
                    var o = RecipeRow(r, type, mastery);
                    Host.Set(dp, row.ToString(System.Globalization.CultureInfo.InvariantCulture), o);
                    if (row >= first && row <= first + 5) Host.Invoke(list, "setRendererData", row, o);
                    row++;
                }
            }
            Host.Invoke(list, "invalidate");
            return;
        }
        ClearPage();
        var data = Host.Get(list, "dataProvider");
        Host.Invoke(data, "cleanUp");
        Host.Set(list, "itemRendererName", "UIMasteryDialog_renderer_mastery");
        Host.Set(list, "rowHeight", ListRowHeight);
        foreach (var r in recipes)
        {
            if (!RecipeVisible(r)) continue;
            Host.Invoke(data, "push", RecipeRow(r, type, mastery));
        }
        RestoreListSelection();
    }

    /// <summary>RestoreListSelection (@0x140eb8b20): the slot's selected row and scroll position back; the scroll event is ignored meanwhile.</summary>
    void RestoreListSelection()
    {
        if (Host.Child("m_objectList") is not { } list) return;
        restoring = true;
        int slot = CurrentSlot();
        Host.Set(list, "selectedIndex", -1);
        Host.Invoke(list, "validateNow");
        Host.Set(list, "selectedIndex", selectedIndex[slot]);
        Host.Invoke(list, "validateNow");
        Host.Set(list, "scrollPosition", scroll[slot]);
        Host.Invoke(list, "validateNow");
        restoring = false;
    }

    /// <summary>OnSelectList (@0x140eb5450): the recipe's page (tabs 0 and 1), the selection kept for the slot.</summary>
    void OnSelectList(int id)
    {
        if (MainTabIndex() is 0 or 1) UpdateRecipePage(id, false);
        selected = id;
        if (Host.Child("m_objectList") is { } list) selectedIndex[CurrentSlot()] = Rt.ToInt32(Host.Get(list, "selectedIndex"));
    }

    bool HasRequiredLevel(MasteryRecipeTable.Recipe r) =>
        Model.HasLocalPlayer && (r.Level == 0 || (ushort)Pc.Level >= r.Level);

    uint MyMastery(int type) => Model.HasLocalPlayer ? Data.Get(type) : 0;

    uint IngredientNeed(MasteryRecipeTable.Recipe r, MasteryRecipeTable.Ingredient ing) =>
        MasteryRecipeTable.IsProductionType(r.Type) ? (uint)ing.Count
            : MasteryRecipeTable.LuaCraftRequireItemCount((uint)ing.Count, r.RequireMastery, MyMastery(r.Type));

    int Have(MasteryRecipeTable.Ingredient ing) => ing.Tag != 0 ? ItemCountByTag(ing.Tag) : ItemCount(ing.ItemId);

    static string IngredientCountText(Func<string, string> common, int have, uint need) =>
        ClientFormat.Format(common(have < need ? "s_common_cur_max_ingredient_lack" : "s_common_cur_max_ingredient"), (uint)have, need);

    /// <summary>CalcAdjustedItemCount (@0x140eb79b0): the reward count; the equipment ability rate is 0 (STILL-OPEN).</summary>
    int AdjustedCount(MasteryRecipeTable.Recipe r, MasteryRecipeTable.Reward rew) =>
        MasteryRecipeTable.IsProductionType(r.Type)
            ? (int)MasteryRecipeTable.LuaGatheringRewardItemCount((uint)rew.Count, r.RequireMastery, MyMastery(r.Type))
            : rew.Count;

    /// <summary>FUN_140eb8120 (@0x140eb8120): the mastery points a craft gives; the equipment bonus is 0 (STILL-OPEN).</summary>
    int RewardMasteryPoints(MasteryRecipeTable.Recipe r) => Model.HasLocalPlayer ? Table.Factor(r.Id, Data.Get(r.Type)) : 0;

    /// <summary>UpdateRecipePage (@0x140eb1770): the page of a recipe; kept, only the ingredient counts are refreshed.</summary>
    void UpdateRecipePage(int id, bool keep)
    {
        if (Host?.Entity == null || !Model.HasLocalPlayer) return;
        if (keep)
        {
            if (Table.Find(id) is not { } rec) return;
            var fields = new List<(string, object)>();
            for (int i = 0; i < rec.Ingredients.Count; i++)
            {
                uint need = IngredientNeed(rec, rec.Ingredients[i]);
                fields.Add(($"require{i}", Host.Obj(("itemCnt", IngredientCountText(Common, Have(rec.Ingredients[i]), need)))));
            }
            Host.Invoke("refreshPage", Host.Obj(fields.ToArray()));
            return;
        }
        ClearPage();
        if (Table.Find(id) is not { } r) return;
        var page = new List<(string, object)>();
        bool canCraft = MyMastery(r.Type) >= r.RequireMastery && !craft.HasTotal;
        for (int i = 0; i < r.Rewards.Count; i++)
            page.Add(($"binding{i}", Reward.BindNewItem(r.Rewards[i].ItemId, r.Rewards[i].Rank, AdjustedCount(r, r.Rewards[i]))));
        bool enough = true;
        for (int i = 0; i < r.Ingredients.Count; i++)
        {
            var ing = r.Ingredients[i];
            uint need = IngredientNeed(r, ing);
            int have = Have(ing);
            if (have < need) enough = false;
            int rank = Model.Tables.Items.Get(ing.ItemId)?.Rank ?? 0;
            page.Add(($"require{i}", Host.Obj(("binding", Require.BindNewItem(ing.ItemId, rank, ing.Count)),
                ("itemCnt", IngredientCountText(Common, have, need)))));
        }
        page.Add(("id", id));
        page.Add(("name", Model.Tables.StringTableText(0x2f, id)));
        page.Add(("mastery", ClientFormat.Format(Common("s_mastery_reward_mastery"), (uint)RewardMasteryPoints(r))));
        page.Add(("desc", Model.Tables.StringTableText(0x30, id)));
        page.Add(("place", r.HabitatText(WorldMapLink)));
        page.Add(("icon", r.IconPath));
        page.Add(("meso", r.RequireMeso));
        page.Add(("enabled", canCraft && enough && HasRequiredLevel(r)));
        page.Add(("debug", IsDebugMode()));
        if (r.Type == 0xb && r.Rewards.Count > 0) page.Add(("grade", r.Rewards[0].Rank));
        Host.Invoke("setPage", Host.Obj(page.ToArray()));
    }

    /// <summary>GetRecipeStateText (@0x140eb71a0): why the recipe is locked, else its success text or the craftable count.</summary>
    string RecipeStateText(MasteryRecipeTable.Recipe r)
    {
        string err = HasRequiredLevel(r) ? "" : ClientFormat.Format(Common("s_mastery_need_level"), (uint)r.Level);
        if (MyMastery(r.Type) < r.RequireMastery)
        {
            string grade = MasteryRecipeTable.GradeName(Table.Grade(r.Type, r.RequireMastery), Common);
            string need = ClientFormat.Format(Common("s_mastery_need_mastery"), grade);
            return err.Length != 0 ? ClientFormat.Format("{0}, {1}", err, need) : need;
        }
        if (err.Length > 0) return err;
        if (MasteryRecipeTable.IsProduction(r)) return Table.SuccessDescription(r.Type, r.Id, true, Data.Gathering, Common);
        int count = MaxCraftCount(r.Id);
        return ClientFormat.Format(Common(count == 1000 ? "s_mastery_manufacture_count_max" : "s_mastery_manufacture_count"), count);
    }

    /// <summary>GetMaxCraftCount (@0x140eb6d40) for a recipe craft: 1000 at most, less by the mesos and by each ingredient.</summary>
    public int MaxCraftCount(int id)
    {
        int max = 1000;
        if (Table.Find(id) is not { } r) return 0;
        if (r.RequireMeso != 0)
        {
            long byMeso = Pc.Mesos / r.RequireMeso;
            if (byMeso < max) max = (int)byMeso;
        }
        foreach (var ing in r.Ingredients)
        {
            uint need = IngredientNeed(r, ing);
            // the client divides without a zero test (GetMaxCraftCount:1559, nCan = nHave / nNeed): a need of 0 cannot
            // happen for a real row (LuaCraftRequireItemCount returns its first argument, a recipe count >= 1), so this
            // skip only keeps C# from dividing by zero.
            if (need == 0) continue;
            max = Math.Min((int)(Have(ing) / need), max);
        }
        return max;
    }

    // ---- crafting -------------------------------------------------------------------------------------------------

    /// <summary>
    /// CMasteryManager::FUN_140b6d470 (masterymanager.cpp:139): the craft precheck, in the client's order: no player (5),
    /// the mastery below the recipe's (1), no required quest in progress (3), the mesos short (2), an ingredient short
    /// (4). 0 when the craft may run (also for a recipe the table lacks; the caller returns before that).
    /// </summary>
    public CraftBlock CraftBlocked(int id)
    {
        if (!Model.HasLocalPlayer) return CraftBlock.NoPlayer;
        if (Table.Find(id) is not { } r) return CraftBlock.None;
        if (r.RequireMastery != 0 && Data.Get(r.Type) < r.RequireMastery) return CraftBlock.Mastery;
        if (!RecipeVisible(r)) return CraftBlock.Quest;
        if (r.RequireMeso != 0 && Pc.Mesos < r.RequireMeso) return CraftBlock.Meso;
        foreach (var ing in r.Ingredients)
            if (Have(ing) < ing.Count) return CraftBlock.Item;      // the raw m_c, not CalcIngredientCount
        return CraftBlock.None;
    }

    /// <summary>
    /// CMasteryManager::FUN_140b6d710 (masterymanager.cpp:176): the notice of a craft result. 1 to 4 the lack kinds, 0xc
    /// the anti-addiction text, 7 the invalid level with the value, 5 the unknown with the value; anything else an empty
    /// notice (the client shows it, too). The precheck's answer and Mastery mode 3's result (scenestategame.cpp:4047).
    /// </summary>
    public void ShowCraftResult(int result)
    {
        string text = result switch
        {
            (int) CraftBlock.Mastery => Common("s_mastery_error_lack_mastery"),
            (int) CraftBlock.Meso => Common("s_mastery_error_lack_meso"),
            (int) CraftBlock.Quest => Common("s_mastery_error_lack_quest"),
            (int) CraftBlock.Item => Common("s_mastery_error_lack_item"),
            0xc => Common("s_anti_addiction_cannot_receive"),
            7 => ClientFormat.Format(Common("s_mastery_error_invalid_level"), (uint)result),
            (int) CraftBlock.NoPlayer => ClientFormat.Format(Common("s_mastery_error_unknown"), (uint)result),
            _ => "",
        };
        Notice?.Invoke(text);
    }

    /// <summary>
    /// FUN_140eb55e0 (@0x140eb55e0): the craft button. The precheck first (FUN_140b6d470): a nonzero result shows its
    /// notice (FUN_140b6d710) and no box. The box type 0x23 shows the item, the ingredients and a count input up to the
    /// craftable count; its answer is <see cref="OnCraftCountInput"/>.
    /// </summary>
    public void OpenCraftBox(int id)
    {
        if (Table.Find(id) is not { } r) return;
        if (CraftBlocked(id) is { } block && block != CraftBlock.None)
        {
            ShowCraftResult((int)block);
            return;
        }
        if (r.Rewards.Count == 0) return;
        var first = r.Rewards[0];
        var item = Item.Create(Model.Tables.Items, first.ItemId, first.Rank);
        if (item == null) return;
        var ingredients = r.Ingredients.Select(ing =>
            (ing.ItemId, Model.Tables.Items.Get(ing.ItemId)?.Rank ?? 0, (int)IngredientNeed(r, ing))).ToList();
        var box = MessageBoxQueue.Create(0x23, Common("s_craft_messagebox_desc"), null, "");
        box.Append(r.Id);
        box.Title = Common("s_craft_messagebox_title");
        box.SetButtonLabels(Common("s_word_craft"), Common("s_word_cancel"), "");
        box.SetItemEntry(item, new ItemTooltip(Model, 0).Name(item) ?? "", 1, 1, MaxCraftCount(r.Id), (int)r.RequireMeso, 1,
            Model.Tables.Items.TextColor);
        box.EnterAllowed = false;
        box.SetRequiredItems(ingredients);
        box.Callback = OnCraftCountInput;
        ShowBox(box);
    }

    /// <summary>OnRecipeCraftCountInput (@0x140eb4c70): on the craft button the count typed (commas removed) starts the craft.</summary>
    bool OnCraftCountInput(int result, MessageBoxData box)
    {
        if (result != 0) return true;
        CancelCraft();
        string text = (CountInput(box) ?? "").Replace(",", "");
        craft.Type = CraftRecipe;
        craft.Total = int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0;
        craft.Id = box.ArgInt(0, 0);
        craft.Done = 1;
        craft.Pending = 1;
        return true;
    }

    bool IMessageBoxListener.OnMessageBox(MessageBoxController sender, int button, string tag) => false;

    /// <summary>CancelCraft (@0x140eb6c80): the cast cancelled, the craft reset, the page redone when it is the craft's recipe.</summary>
    public void CancelCraft()
    {
        if (!craft.HasTotal) return;
        CastingBar?.Cancel();
        bool same = selected == craft.Id;
        craft.Reset();
        if (same) OnSelectList(selected);
    }

    /// <summary>OnCastingFinished (@0x140eaf840): the craft is sent; the next one is queued while the materials last.</summary>
    public void OnCastingFinished()
    {
        if (craft.Type == CraftRecipe) SendCraft?.Invoke(craft.Id);
        else return;                                // the magic scroll craft: not ported
        craft.Done++;
        if (craft.Done <= craft.Total && MaxCraftCount(craft.Id) != 0) craft.Pending = 1;
        else craft.Reset();
    }

    /// <summary>OnCastingCancelled (@0x140eafa10).</summary>
    public void OnCastingCancelled()
    {
        craft.Reset();
        if (Host?.Entity != null) RestoreListSelection();
    }

    /// <summary>
    /// vfunc_72 (@0x140eafa50), every frame: a pending craft starts its cast (2 s, or the recipe's gathering time) with the
    /// progress text; then a pending sub-tab or list refresh runs.
    /// </summary>
    public void Process(ulong nowMs)
    {
        if (IsClosed || Host?.Entity == null) return;
        if (craft.HasTotal && craft.Pending != 0)
        {
            int time = 2000;
            if (craft.Type == CraftRecipe && Table.Find(craft.Id) is { } r) time = (int)r.GatheringTime;
            if (CastingBar != null)
            {
                string key = craft.Type == CraftRecipe ? "s_mastery_manufacture_progress" : "s_mastery_magicscroll_progress";
                CastingBar.StartCastingText(this, ClientFormat.Format(Common(key), craft.Done, craft.Total), time, false, true, "");
            }
            craft.Pending = 0;
        }
        if (pendingSub != 0)
        {
            SelectMastery(pendingSub, true);
            if (CurrentType() == pendingList) UpdateRecipePage(selected, true);
            pendingSub = 0; pendingList = 0;
        }
        else if (pendingList != 0)
        {
            if (CurrentType() == pendingList)
            {
                UpdateList(pendingList, true);
                UpdateRecipePage(selected, true);
            }
            pendingList = 0;
        }
    }
}
