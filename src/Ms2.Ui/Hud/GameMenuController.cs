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

    /// <summary>CGameMenuTable (GetGameMenuTable).</summary>
    public GameMenuTable Table;
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
    /// RefreshMenu @0x141939910: clearMenu, the entries (FUN_14193a140), then each notify mark. The marks whose state is not
    /// here (skill upgrades, character ability, event popups, the quest button, the pet buttons, the riding state) keep the
    /// movie's defaults, which is what the client sends without that state.
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

    /// <summary>FindMenuItem @0x14193b180: m_menus' entry of that name, its m_button (the SystemDropdown).</summary>
    object MenuItem(string name)
    {
        if (Host.Child("m_menus") is not AsObject menus) return null;
        var entry = Host.Invoke(menus, "getChildByName", name);
        return Rt.IsNullish(entry) ? null : Host.Invoke(entry, "getChildByName", "m_button") is { } b && !Rt.IsNullish(b) ? b : null;
    }

    /// <summary>FUN_141938ae0's tail: setNotifyCount(the entry's name, count) on its top entry's button.</summary>
    public void UpdateNotifyCount(int uiId)
    {
        int id = Table.FindIdByUiId(uiId);
        if (MenuItem(MenuName(Table.FindTopId(id))) is { } b) Host.Invoke(b, "setNotifyCount", MenuName(id), NotifyCount(uiId));
    }

    /// <summary>SetNotifyRedCircle16 @0x141939360 / SetNotifyRedCircle7 @0x1419394e0.</summary>
    void SetNotifyRedCircle(int id, bool on)
    {
        if (MenuItem(MenuName(Table.FindTopId(id))) is { } b) Host.Invoke(b, "setNotifyRedCircle", MenuName(id), on);
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

    /// <summary>GetCharacterAbilityTooltip @0x14193df10: s_tooltip_gamemenu_enable_characterability when the level opens
    /// the abilities (FUN_1401e0800) and the constant's quest (m_6dd0) is rewarded, else the disable text. Neither the
    /// level rule nor the quest state is here, so the disable text.</summary>
    AsTooltip CharacterAbilityTooltip() => AsTooltip.Text(Common("s_tooltip_gamemenu_disable_characterability"));

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
