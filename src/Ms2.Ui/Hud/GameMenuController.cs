using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIGameMenuDialog (client/ms2/uigamemenudialog.cpp; UI id 0x4e28), the C++ side of the game menu movie uigamemenudialog.
/// The movie builds the entries (SystemDropDownContainer.addEntity: the entry class, its icon, caption tag and drop-down),
/// the notify marks and the portrait loader; this sends it the menu table's entries and handles the movie's calls.
/// </summary>
public sealed class GameMenuController : UiController
{
    /// <summary>The ctor (@0x141935f20) and UIBuiltInDialog's (uibuiltindialog.cpp:19) have no SetTweenEnabled: m_c4 stays 0,
    /// no open / close tween.</summary>
    protected override bool TweenEnabled => false;

    public override string Movie => "uigamemenudialog";
    protected override string EntityMethod => "addMenuEntity";

    /// <summary>UIGameMenuDialog is a UIBuiltInDialog (uigamemenudialog.h:39): the ctor's flag 0 (vfunc_56).</summary>
    public override bool AlwaysOn => true;

    /// <summary>GetTypeName (@0x141936630, uigamemenudialog.cpp:809): the settings file's name and what the guide's
    /// dialog lookups match (CUIService::FindObjectByName compares it).</summary>
    public override string TypeName => "GameMenuDialog";

    /// <summary>CGameMenuTable (GetGameMenuTable).</summary>
    public GameMenuTable Table;

    /// <summary>
    /// GetGameMenuPathPosition's moved-entry step (uiguideeventarrow.cpp:912-929): whether a path element is an entry
    /// the menu table moved into a group (FindByName + IsChild), and the movie's getChildObjectPos(shown, original)
    /// answer for it — the shown entry's place and half width. Null when the element is no moved entry (the plain
    /// child walk applies); <see cref="GuideMenuMoved.Fail"/> when the movie gave no object (the client's whole
    /// lookup fails then, :920-921). The guide pane's walk asks it through its
    /// <see cref="GuideEventController.GameMenuChildPos"/>; the HUD passes its menu's table.
    /// </summary>
    public static Ms2.Ui.Guide.GuideMenuMoved? MenuChildPos(GameMenuTable table, string element, Ms2.Ui.Guide.GuideNode clip)
    {
        if (table == null || clip is not Ms2.Ui.Guide.GuideObjectNode node) return null;
        var def = table.FindByName(element);
        if (def == null || !table.IsChild(def.Id)) return null;
        string shown = table.Find(table.FindTopId(def.Id))?.Name ?? element;
        var v = node.Host.Invoke(node.Target, "getChildObjectPos", shown, element);
        if (v is not AsObject o) return new Ms2.Ui.Guide.GuideMenuMoved(Fail: true, 0, 0, 0);
        return new Ms2.Ui.Guide.GuideMenuMoved(Fail: false,
            Rt.ToNumber(node.Host.Get(o, "x")), Rt.ToNumber(node.Host.Get(o, "y")),
            Rt.ToNumber(node.Host.Get(o, "width")) / 2);
    }
    /// <summary>CGameMenuItem::IsVisible for an item with a filter (@0x140cf0bf0): a running game event by id or type, the
    /// attendance dialogs' active events, the current season. None of these exist here, so a filtered item is hidden.</summary>
    public Func<GameMenuItem, bool> FilterPasses = _ => false;
    /// <summary>The functions RegisterDefs binds to m_isEnabled (named by <see cref="GameMenuDef.EnabledBy"/>), e.g.
    /// UIMentoringDialog::IsAvailable (a mentoring event value is set). Without their state they answer no.</summary>
    public Func<GameMenuDef, bool> EnabledBy = _ => false;
    /// <summary>The function bound to m_hasNew (CMeratMarketMan::HasBuyableAdProduct): the entry's name gets "_event".</summary>
    public Func<GameMenuDef, bool> NewBy = _ => false;
    /// <summary>FUN_141938ae0's count for a dialog id (unread mail, claimable rewards, ...); 0 without the systems.</summary>
    public Func<int, int> NotifyCount = _ => 0;
    /// <summary>CCharacterAbilityTable and CCharacterAbilityManager, for the character ability notify mark
    /// (<see cref="UpdateCharacterAbilityNotify"/>) and its tooltip.</summary>
    public CharacterAbilityTable AbilityTable = new();
    public CharacterAbilityData Abilities = new();
    /// <summary>CQuestManagerCommon::IsQuestRewarded.</summary>
    public Func<int, bool> IsQuestRewarded = _ => false;

    /// <summary>DAT_1439f8ccc / DAT_1439f8cd0: the two bottom bars' heights (as <see cref="MainPlayerInfoController"/>).</summary>
    public int BottomBarHeight = 21, AdventureBarHeight = 21;

    /// <summary>An entry's m_onClick (the UIGameMenuDialog method <see cref="GameMenuDef.Action"/> names).</summary>
    public event Action<GameMenuDef> MenuAction;
    /// <summary>CUIService::ToggleDialog(id): an entry's m_nUiId, OnPetInfo (0x4f38), OnQuest (0x4f56).</summary>
    public event Action<int> ToggleDialog;
    /// <summary>The movie's own buttons: OnUgcUpload (OpenScreenshotDirectory), OnCamera (FUN_14193e760), OnPetAttack,
    /// OnContract (FUN_14193d450).</summary>
    public event Action<string> Button;

    readonly Dictionary<int, int> enabled = new();     // m_enabled (SetMenuEnable)
    List<int> order = new();                           // m_order: the ids the last build added
    bool redCircleHome, redCircleOption;               // m_bFlag259, m_bFlag25a

    bool Feature(int id) => Model.IsFeatureEnabled(id);

    /// <summary>vfunc_43 @0x141936d40: bottom right, 5 to the right, above the bottom bars: FUN_1418dd0c0 (the exp bar's
    /// height - 2), with feature 0xbd that - 6 + FUN_1418dd110 (the adventure bar's height - 2).</summary>
    public override void Layout(double stageW, double stageH)
    {
        int h = BottomBarHeight - 2;
        if (Feature(MainPlayerInfoController.AdventureLevelFeature)) h = h - 6 + (AdventureBarHeight - 2);
        Host.Align(Host.Entity, 8, 5, -h);
    }

    /// <summary>OnLoadComplete @0x141936a20: UIDialog's, LoadRenderTarget, the listeners, RefreshMenu.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Host.Invoke("loadRenderTarget");
        RefreshMenu();
    }

    /// <summary>
    /// RefreshMenu @0x141939910: clearMenu, the entries (FUN_14193a140), then each notify mark (the character ability's
    /// count with 0), the character ability's mark (<see cref="UpdateCharacterAbilityNotify"/>). The marks whose state is
    /// not here (skill upgrades, event popups, the quest button, the pet buttons, the riding state) keep the movie's
    /// defaults, which is what the client sends without that state.
    /// </summary>
    public void RefreshMenu()
    {
        if (Host?.Entity == null || Table == null) return;
        Host.Invoke("clearMenu");
        AddEntries();
        foreach (int ui in new[] { 0x4e4d, 0x4e49, 0x4e73, 0x4f2d, 0x4efa, 0x4f53, 0x4f1e, 0x4f0c, 0x4f18, 0x4f2f, 0x4e4f, 0x4f1c,
                                   0x4f43, 0x4eb8, 0x4eb9, 0x4f4b, Feature(0xfb) ? 0x4f67 : 0x4f87, 0x4f33, 0x4f59 })
            UpdateNotifyCount(ui);
        SetNotifyRedCircle(0x16, redCircleHome);
        SetNotifyRedCircle(7, redCircleOption);
        UpdateCharacterAbilityNotify();
    }

    /// <summary>FUN_14193a140: per visible top item {row, col} and its fields (FUN_14193a7b0); the visible children go into
    /// a CLIK DataProvider as "child" (ASDataProvider @0x1422deec0); then addMenuEntity. Ids not in the previous build get
    /// setNotifyNew.</summary>
    void AddEntries()
    {
        var added = new List<int>();
        foreach (var item in Table.TopItems())
        {
            var obj = new List<(string, object)> { ("row", item.Row), ("col", item.Col) };
            if (!IsVisible(item) || !Fields(item.Id, obj)) continue;
            added.Add(item.Id);
            if (item.Children.Count > 0)
            {
                var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
                foreach (var child in item.Children)
                {
                    var co = new List<(string, object)>();
                    if (!IsVisible(child) || !Fields(child.Id, co)) continue;
                    Host.Invoke(dp, "push", Host.Obj(co.ToArray()));
                    added.Add(child.Id);
                }
                obj.Add(("child", dp));
            }
            Host.Invoke("addMenuEntity", Host.Obj(obj.ToArray()));
        }
        if (order.Count > 0)
            foreach (int id in added)
                if (!order.Contains(id) && MenuItem(MenuName(Table.FindTopId(id))) is { } b)
                    Host.Invoke(b, "setNotifyNew", MenuName(id), 1);
        order = added;
    }

    bool IsVisible(GameMenuItem item) => item.FilterType.Length == 0 || FilterPasses(item);

    /// <summary>FUN_14193a7b0: an entry's fields; false when it has no definition or its m_isEnabled says no. The tooltip
    /// name is the localized caption, a child's with " [key]" of its key command; with feature 0xc4 off a top entry's is
    /// empty.</summary>
    bool Fields(int id, List<(string, object)> obj)
    {
        var def = Table.Find(id);
        if (def == null) return false;
        if (def.EnabledBy != null && !EnabledBy(def)) return false;
        string tip = Host.Player.Translate?.Invoke(def.Label) ?? def.Label;
        bool child = Table.IsChild(id);
        if (child && def.KeyBind != 0)
        {
            string key = Model.Keys.GetKeyName(Model.Keys.FindKeyByActionAndPriority(def.KeyBind, 0));
            if (key.Length > 0) tip += " [" + key + "]";
        }
        string name = MenuName(id);
        obj.Add(("name", name));
        obj.Add(("layerName", name));
        obj.Add(("isBig", 0));                         // m_28: RegisterDefs sets it on no entry
        if (enabled.TryGetValue(id, out int en)) obj.Add(("enabled", en));
        obj.Add(("tooltipName", !Feature(0xc4) && !child ? "" : tip));
        return true;
    }

    /// <summary>FUN_14193afc0: the entry's name, with "_event" while its m_hasNew says so.</summary>
    string MenuName(int id)
    {
        var def = Table.Find(id);
        if (def == null) return "";
        return def.NewBy != null && NewBy(def) ? def.Name + "_event" : def.Name;
    }

    /// <summary>IsMenuExpanded @0x141938a00 (uigamemenudialog.cpp:1304): the entry's m_button isExpand(); false
    /// without the movie or the entry. The guide's MenuExpand asks it.</summary>
    public bool IsMenuExpanded(string name) =>
        Host?.Entity != null && MenuItem(name) is { } b && Rt.ToBoolean(Host.Invoke(b, "isExpand"));

    /// <summary>FindMenuItem @0x14193b180: m_menus' entry of that name, its m_button (the SystemDropdown).</summary>
    object MenuItem(string name)
    {
        if (Host.Child("m_menus") is not AsObject menus) return null;
        var entry = Host.Invoke(menus, "getChildByName", name);
        return Rt.IsNullish(entry) ? null : Host.Invoke(entry, "getChildByName", "m_button") is { } b && !Rt.IsNullish(b) ? b : null;
    }

    /// <summary>FUN_141938ae0 (@0x141938ae0): nothing without a local player; else setNotifyCount(the entry's name, count) on
    /// its top entry's button. The character ability's (0x4f0c) count is <paramref name="value"/> itself (it is not kept:
    /// RefreshMenu sends 0).</summary>
    public void UpdateNotifyCount(int uiId, int value = 0)
    {
        if (!Model.HasLocalPlayer) return;              // pPc == NULL (uigamemenudialog.cpp:1317-1319)
        if (Host?.Entity == null || Table == null) return;
        int count = uiId == CharacterAbilityUiId ? value : NotifyCount(uiId);
        int id = Table.FindIdByUiId(uiId);
        if (MenuItem(MenuName(Table.FindTopId(id))) is { } b) Host.Invoke(b, "setNotifyCount", MenuName(id), count);
    }

    /// <summary>The character ability dialog's UI id.</summary>
    public const int CharacterAbilityUiId = 0x4f0c;

    /// <summary>SetMenuNotifyUp @0x141938350: setNotifyUp(the entry's name, up) on its top entry's button.</summary>
    void SetMenuNotifyUp(int id, bool up)
    {
        if (MenuItem(MenuName(Table.FindTopId(id))) is { } b) Host.Invoke(b, "setNotifyUp", MenuName(id), up ? 1 : 0);
    }

    /// <summary>
    /// FUN_141938850 (@0x141938850): with a local player and the constant's quest (CharacterAbilityOpenQuestID) rewarded,
    /// entry 0x14's up mark: points left (FUN_140f29e00 above 0, as an unsigned) and an ability of the table (GetAbilities'
    /// order) at or under the level that is not learned. Run by RefreshMenu and after the CharacterAbility load and learn.
    /// </summary>
    public void UpdateCharacterAbilityNotify()
    {
        if (Host?.Entity == null || Table == null || !Model.HasLocalPlayer) return;
        if (!IsQuestRewarded((int)Model.Tables.CharacterAbilityOpenQuestID)) return;
        bool found = false;
        ushort level = (ushort)Host.Model.Player.Level;
        if ((uint)Abilities.Remain(level, Model.Tables.CharacterAbilityDefaultPoint) > 0u)
            foreach (var a in AbilityTable.Abilities)
                if (a.RequireLevel <= level && !Abilities.IsLearned(a.Id)) { found = true; break; }
        SetMenuNotifyUp(0x14, found);
    }

    /// <summary>FUN_141939320 (uigamemenudialog.cpp:1417): m_bFlag259 (RefreshMenu sends it again) and the home entry's red circle
    /// (SetNotifyRedCircle16, id 0x16; the house visit question's, CPersonalInfoAgreement::FUN_141262180). Before the movie is built only the flag is kept.</summary>
    public void SetNotifyRedCircle16Flag(bool on)
    {
        redCircleHome = on;
        if (Host?.Entity != null) SetNotifyRedCircle(0x16, on);
    }

    /// <summary>SetNotifyRedCircle7Flag @0x1419394a0: m_bFlag25a (RefreshMenu sends it again) and the option entry's red circle
    /// (the agreement reminder, CPersonalInfoAgreement::FUN_141262090). Before the movie is built only the flag is kept.</summary>
    public void SetNotifyRedCircle7Flag(bool on)
    {
        redCircleOption = on;
        if (Host?.Entity != null) SetNotifyRedCircle(7, on);
    }

    /// <summary>SetNotifyRedCircle16 @0x141939360 / SetNotifyRedCircle7 @0x1419394e0.</summary>
    void SetNotifyRedCircle(int id, bool on)
    {
        if (MenuItem(MenuName(Table.FindTopId(id))) is { } b && Host.Has(b, "setNotifyRedCircle")) Host.Invoke(b, "setNotifyRedCircle", MenuName(id), on);
    }

    /// <summary>vfunc_oa0_9 @0x141937d70 (the level change): the character ability mark, the learning quest count.
    /// STILL-OPEN: its UpdateQuestMenu (the quest button) is not ported.</summary>
    public void OnChangeMyLevel()
    {
        UpdateCharacterAbilityNotify();
        UpdateNotifyCount(0x4f18);
    }

    /// <summary>vfunc_o178_2 @0x1419376a0 (uigamemenudialog.cpp:996-1004), a rewarded quest (the quest event dispatcher's
    /// vfunc_2, OnQuest mode 4, for a quest whose type has no slot of its own): the learning quest count and, for the
    /// constant's quest (CharacterAbilityOpenQuestID), the character ability mark.
    /// STILL-OPEN: its UpdateQuestMenu (the quest button) and FUN_141938100 are not ported.</summary>
    public void OnQuestRewarded(int questId)
    {
        UpdateNotifyCount(0x4f18);
        if (questId == (int)Model.Tables.CharacterAbilityOpenQuestID) UpdateCharacterAbilityNotify();
    }

    /// <summary>The character ability state the mark reads, set after the menu was built (the HUD makes the game menu
    /// first): RefreshMenu ran FUN_141938850 without it, so the mark is redone with it.</summary>
    public void SetCharacterAbility(CharacterAbilityTable table, CharacterAbilityData abilities, Func<int, bool> isQuestRewarded)
    {
        AbilityTable = table;
        Abilities = abilities;
        IsQuestRewarded = isQuestRewarded;
        UpdateCharacterAbilityNotify();
    }

    /// <summary>SetMenuEnable @0x141939770.</summary>
    public void SetMenuEnable(int id, bool on)
    {
        if (MenuItem(MenuName(Table.FindTopId(id))) is { } b) Host.Invoke(b, "setMenuEnable", MenuName(id), on);
        enabled[id] = on ? 1 : 0;
    }

    /// <summary>
    /// vfunc_49 @0x141937a30 (uigamemenudialog.cpp:1082): the entry's name (its "_event" cut off): "characterability" gets
    /// GetCharacterAbilityTooltip, a name with "mushking" FUN_14193e0e0; any other the definition's m_strTip, or
    /// m_strTipOff while its top entry's button is disabled, through FUN_14193dd90.
    /// </summary>
    public override IDictionary<string, object> GetTooltip(object[] args)
    {
        string name = Rt.ToString(args.ElementAtOrDefault(0));
        int cut = name.IndexOf("_event", StringComparison.Ordinal);
        if (cut >= 0) name = name[..cut];
        if (name == "characterability") return CharacterAbilityTooltip();
        if (name.Contains("mushking", StringComparison.Ordinal)) return MushkingTooltip();
        var def = Table?.FindByName(name);
        if (def == null) return null;
        string tip = def.Tooltip;
        if (MenuItem(name) is { } b && !Rt.ToBoolean(Host.Get(b, "enabled"))) tip = def.DisabledTooltip;   // ASDisplayObject::IsEnabled
        return KeyTooltip(def.Id, tip, def.KeyBind);
    }

    /// <summary>FUN_14193dd90 @0x14193dd90: the common string <paramref name="key"/> formatted with "[key]" of the key
    /// command (CKeyTable::GetActionKeyName(action, 0); nothing without a key), as a text tooltip, aligned 3 for a child
    /// entry (IsChildMenu).</summary>
    AsTooltip KeyTooltip(int id, string key, int action)
    {
        string keyName = Model.Keys.GetKeyName(Model.Keys.FindKeyByActionAndPriority(action, 0));
        string bracket = keyName.Length > 0 ? "[" + keyName + "]" : "";
        var tip = AsTooltip.Text(Common(key, bracket));
        if (tip != null && Table.IsChild(id)) tip.SetTooltipAlign(3);
        return tip;
    }

    /// <summary>FUN_14028dfa0: the common string by key formatted with the arguments ("" when there is none).</summary>
    string Common(string key, params object[] args) => string.IsNullOrEmpty(key) ? "" : Ms2.Quest.ClientFormat.Format(Model.Tables.Common(key), args);

    /// <summary>GetCharacterAbilityTooltip @0x14193df10: none without a local player; s_tooltip_gamemenu_enable_characterability
    /// when the level opens the abilities (FUN_1401e0800) and the constant's quest (m_6dd0) is rewarded, else the disable
    /// text.</summary>
    AsTooltip CharacterAbilityTooltip()
    {
        if (!Model.HasLocalPlayer) return null;
        bool done = IsQuestRewarded((int)Model.Tables.CharacterAbilityOpenQuestID);
        bool open = CharacterAbilityData.IsOpen(Feature(Ms2.Ui.Feature.CharacterAbility), (ushort)Host.Model.Player.Level);
        return AsTooltip.Text(Common(open && done ? "s_tooltip_gamemenu_enable_characterability" : "s_tooltip_gamemenu_disable_characterability"));
    }

    /// <summary>FUN_14193e0e0 @0x14193e0e0: s_tooltip_gamemenu_mushking_royal, plus a kill and a time exp line while the
    /// game rule's event buffs are not 0 (none here).</summary>
    AsTooltip MushkingTooltip() => AsTooltip.Text(Common("s_tooltip_gamemenu_mushking_royal"));

    /// <summary>
    /// vfunc_28 @0x141936e30: the movie's buttons, else an entry's name (its "_event" cut off): the definition's m_onClick
    /// or, without one, ToggleDialog(m_nUiId). (Entries below 0x41 also send guide trigger 0x1f; the guide is not here.)
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onugcupload": Button?.Invoke("OnUgcUpload"); return true;
            case "oncamera": Button?.Invoke("OnCamera"); return true;
            case "onpetinfo": ToggleDialog?.Invoke(0x4f38); return true;
            case "onpetattack": Button?.Invoke("OnPetAttack"); return true;
            case "onquest": ToggleDialog?.Invoke(0x4f56); return true;
            case "oncontract": Button?.Invoke("OnContract"); return true;
        }
        int cut = name.IndexOf("_event", StringComparison.Ordinal);
        var def = Table?.FindByName(cut >= 0 ? name[..cut] : name);
        if (def == null) return false;
        if (def.Action != null) MenuAction?.Invoke(def);
        else if (def.UiId != 0) ToggleDialog?.Invoke(def.UiId);
        return true;
    }
}
