using System.Globalization;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>The owner of the character window's equip slots (EquipSlot: FUN_14120d560 casts a drag slot to it).</summary>
public interface IEquipSlotOwner : IDragSlotOwner
{
    /// <summary>The name is one of the equip slots.</summary>
    bool IsEquipSlot(string name);
    /// <summary>The slot's item (vfunc_25), that item's equip slot (CItem m_68) and the bag's kind (m_nKind); null for an
    /// empty slot or another name.</summary>
    (Item Item, int Slot, int Kind)? WornSlot(string name);
    /// <summary>Unk_Inventory::RequestUnequipItem with its checks.</summary>
    void RequestUnequip(Item item);
}

/// <summary>
/// UIMyInfoDialog (client/ms2/uimyinfodialog.cpp; UI id 0x2711, the game menu's "characterinfo" entry and key action 400002)
/// over UIPCInfoDialog (uipcinfodialog.cpp), the C++ side of the character window movie uimyinfodialog
/// (GetTypeName "MyInfoDialog", @0x14132c030). It shows a <see cref="PcInfo"/> (the dialog's m_spInfo, which vfunc_65 builds
/// at open from MyPC::vfunc_125): the tabs, the profile (job, level, name, title list, address, architect score, guild,
/// prestige level, wedding mark, motto, profile image), the stat rows and the attribute points.
/// <para>Nothing here sends a packet. The motto change, title change, attribute add and reset, profile upload and screenshot
/// are raised as <see cref="Button"/>; the dialogs the buttons open as <see cref="ToggleDialog"/> / <see cref="OpenDialog"/>.</para>
/// <para>The parts whose data or code is not ported take it from a source that may be null; a null source leaves the
/// member unset (or the row's value and detail empty): the Lua scores and crit rate, the equipment abilities, the item
/// level, the shield, the achievement tally and the profile image path.</para>
/// </summary>
public sealed class MyInfoController : UiController, IEquipSlotOwner
{
    public override string Movie => "uimyinfodialog";
    protected override string EntityMethod => "setTodayMemo";
    public override string TypeName => "MyInfoDialog";

    /// <summary>UIMyInfoDialog's m_strGemBag / m_strEquipBag (its constructor, uimyinfodialog.cpp:203).</summary>
    public const string GemBag = "MyPcInfoDialog.Gem", EquipBag = "MyPcInfoDialog.Equip";

    /// <summary>MyPC::vfunc_125 at open (vfunc_65) and in RebuildPCInfo; null = no local player (the dialog does not open).</summary>
    public Func<PcInfo> Record;
    /// <summary>MyPC::GetStatPointManager (vfunc_87 reads it for the local record).</summary>
    public StatPointBook StatPoints;
    /// <summary>GetOffenseScore (@0x141334890): Lua either way (CalePcOffenseScore with feature 0xdf, else CalcPcDamage).</summary>
    public Func<PcInfo, long> OffenseScore;
    /// <summary>GetDefenseScore with feature 0xdf: Lua CalePcDefenseScore (without it, Stats2[0x14], ported).</summary>
    public Func<PcInfo, long> DefenseScoreLua;
    /// <summary>CLua::FUN_140674a70(value, 0, critical): the crit rate GetBasicStat(0x12) shows.</summary>
    public Func<long, float, long, float> CritRate;
    /// <summary>The record's m_3cc / m_3d0 (vfunc_126: the worn items' levels) where the port's own sum does not know them
    /// (Lua); null leaves the port's sum.</summary>
    public Func<PcInfo, (uint Total, uint Additional)?> ItemLevel;
    /// <summary>AdditionalCliList::GetTotalShieldValue of the local player.</summary>
    public Func<long> Shield;
    /// <summary>UIMyInfoDialog::vfunc_104 (@0x141a5ae90): CAchieveManager::GetTally()->Sum().</summary>
    public Func<uint> AchieveTally;
    /// <summary>The local PC's name tag symbol (PCBase::FUN_141661e00 = m_n10f8: the own FieldAddUser's, PC::FUN_141661d40).
    /// STILL-OPEN: CSceneStateGame::OnNametag (scenestategame.cpp:7314), which changes it later, is not handled.</summary>
    public Func<ushort> SymbolId = () => 0;
    /// <summary>The local player's GM grade (GetGmGrade), for the symbol's gm condition.</summary>
    public Func<int> GmGrade = () => 0;
    /// <summary>GetProfileImagePath (@0x141336c90): FUN_14016a730(FUN_141028be0(character id, picture, 1, "")).</summary>
    public Func<PcInfo, string> ProfileImagePath;

    /// <summary>CUIService::ToggleDialog of a button: onSymbolClick 0x4ed5, onWedding 0x4f8c.</summary>
    public event Action<int> ToggleDialog;
    /// <summary>CUIService::OpenDialog: OnConfig 0x4e75 (after DAT_1439f9284 = the gem item's slot).</summary>
    public event Action<int> OpenDialog;
    /// <summary>A button whose client path sends a request or opens a part not here, by its callback name.</summary>
    public event Action<string> Button;
    /// <summary>FUN_141275260(id, 1, 1) (unit_141274d10_uihelpers.cpp:267): ShowSystemNotice of a common string, by its key.</summary>
    public event Action<string> SystemNotice;
    /// <summary>The local player's inventory (g_143ab2a90): its bag-room check and the unequip request.</summary>
    public Ms2.Net.Packets.InventoryView Inventory;
    /// <summary>CInputKeyHandler::IsActionKeyDown(action, 0, 0): 0x49406, MS2DragSlot::vfunc_19's preview key.</summary>
    public Func<int, bool> ActionKeyDown = _ => false;

    PcInfo info;           // m_spInfo
    int tab;               // m_nTab
    bool dirty;            // m_208 (profile at the next vfunc_15)
    long statsDueMs = -1;  // m_218 (the stat lists 100 ms later)

    public PcInfo Info => info;
    public int Tab => tab;

    bool Feature(int id) => Model.IsFeatureEnabled(id);
    string Localize(string key) => Host.Player.Translate?.Invoke(key) ?? key;
    string Common(string key) => Model.Tables.Common(key) ?? "";
    object Child(string name) => Host.Child(name);

    /// <summary>
    /// vfunc_65 (@0x141a58310) then OnLoadComplete: the record from MyPC::vfunc_125, UIPCInfoDialog::OnLoadComplete
    /// (@0x14132dc20), then UIMyInfoDialog's (@0x141a58ca0): m_closet shown with feature 0xe3; then vfunc_46.
    /// </summary>
    public override void OnLoadComplete()
    {
        info = Record?.Invoke();
        base.OnLoadComplete();
        bool codi = Feature(0x8e);
        if (info != null && !info.IsLocal) codi = info.CodiActive;
        if (Host.Entity != null)
        {
            var tabs = new List<object>
            {
                TabRow(0, "$s_myInfo_game", "equip", "$s_tooltip_myinfo_tab_equip"),
                TabRow(1, "$s_myInfo_skin", "equip", "$s_tooltip_myinfo_tab_skin"),
            };
            if (codi) tabs.Add(TabRow(3, "$s_myInfo_codi", "equip", "$s_tooltip_myinfo_tab_codi"));
            tabs.Add(TabRow(2, "$s_myInfo_badge", "gem", "$s_tooltip_myinfo_tab_badge"));
            Host.Invoke("initData", Host.Obj(("dpTab", DataProvider(tabs))));
        }
        Profile();
        Memo();
        Stats();
        Bag(tab);
        SetProfileImage(ProfilePath());
        // m_myNews's click handler is OnClickMyNews (raised by the movie's click through OnCallback)
        if (Child("m_screenshot") is { } shot)
        {
            bool on = info != null && info.IsLocal && Feature(0xf7);
            Host.Set(shot, "enabled", on);
            Host.SetVisible(shot, on);
        }
        // m_webGuideButton (FUN_1419260d0) is the web guide's: not here
        if (Child("m_closet") is { } closet) Host.SetVisible(closet, Feature(0xe3));
        HideFeatureButtons();
        LoadRenderTarget();
    }

    /// <summary>
    /// The avatar the window shows (m_spAvatar: vfunc_65 makes a UIAvatarPC of the local player, its vfunc_1 sets the y
    /// turn limit), facing the camera (0, 0, 90). Null while closed or without a record.
    /// </summary>
    public AvatarControl Avatar { get; private set; }
    /// <summary>UIAvatarPC::vfunc_1's CLimitCharRotate; null: no limit.</summary>
    public AvatarRotateLimit RotateLimit;

    /// <summary>
    /// LoadRenderTarget (@0x141a5d5d0) from vfunc_17 (@0x141a5d410): the movie's loadRenderTarget loads
    /// "img://&lt;id&gt;:avatar/&lt;w&gt;/&lt;h&gt;" (the render view vfunc_25 @0x141a5d0c0 sets up) into m_renderTarget and
    /// takes the avatar's mouse calls once loaded. STILL-OPEN: vfunc_o178_1 (@0x141a5d450), run when the avatar's model has
    /// loaded, also passes the skin colour list and showActionList to initData; the action and pose lists are not ported.
    /// </summary>
    void LoadRenderTarget()
    {
        if (Host.Entity == null || info == null) return;
        Avatar = new AvatarControl(RotateLimit ?? new AvatarRotateLimit(0, 360), 0, 0, 90);
        Host.Invoke("loadRenderTarget");
    }

    object TabRow(int index, string label, string frame, string tooltip) =>
        Host.Obj(("tabIndex", index), ("label", label), ("equipFrame", frame),
            ("tooltipData", Host.Obj(("className", "export_common_tooltip_tooltipText"), ("id", tooltip), ("align", "topLeft"))));

    /// <summary>UIPCInfoDialog::vfunc_46 (@0x14132da70): m_myNews hidden without feature 0x3b, m_wedding without 0x104.</summary>
    void HideFeatureButtons()
    {
        if (!Feature(0x3b) && Child("m_myNews") is { } news) Host.SetVisible(news, false);
        if (!Feature(0x104) && Child("m_wedding") is { } wedding) Host.SetVisible(wedding, false);
    }

    /// <summary>UIMyInfoDialog::vfunc_67 (@0x141a58630) and UIPCInfoDialog's (@0x14132d530): the bags dropped, the record
    /// and tab cleared, UIDialog's close.</summary>
    public override void Close()
    {
        if (Avatar != null && Host.Entity != null) Host.Invoke("unloadRenderTarget");   // vfunc_16 (@0x141a5d3e0)
        Avatar = null;
        info = null;
        tab = 0;
        dirty = false; statsDueMs = -1;
        base.Close();
    }

    /// <summary>FUN_141a5a560 (@0x141a5a560): the record changed; the profile is rebuilt at the next vfunc_15 and the stat
    /// lists 100 ms after (m_218.Start(100)).</summary>
    public void MarkChanged(long nowMs)
    {
        if (IsClosed) return;
        dirty = true;
        statsDueMs = nowMs + 100;
    }

    /// <summary>vfunc_15 (@0x141a593e0): RebuildPCInfo(1, 0, 0) when marked, RebuildPCInfo(0, 1, 0) when the timer ran out.</summary>
    public void Process(long nowMs)
    {
        if (IsClosed || Host.Entity == null) return;
        if (dirty) { Rebuild(true, false); dirty = false; }
        if (statsDueMs >= 0 && nowMs >= statsDueMs) { Rebuild(false, true); statsDueMs = -1; }
    }

    /// <summary>RebuildPCInfo (@0x141a5ab10): a new record from MyPC::vfunc_125, then vfunc_84 and / or vfunc_86.</summary>
    void Rebuild(bool profile, bool stats)
    {
        if (info == null) return;
        var r = Record?.Invoke();
        if (r == null) return;
        info = r;
        if (profile) Profile();
        if (stats) Stats();
    }

    // ---- vfunc_84: the profile -------------------------------------------------------------------------------------

    /// <summary>vfunc_84 (@0x14132fb40), then UIMyInfoDialog's override (@0x141a5ae40): <see cref="Symbol"/>.</summary>
    void Profile()
    {
        if (info == null) { ClearProfile(); return; }
        JobText(info.SubJob);
        if (Child("m_level") is { } level) Host.Set(level, "text", info.Level.ToString(CultureInfo.InvariantCulture));
        Host.Invoke(Host.Entity, "setName", info.Name);
        Trophy();
        Address(info.HomeAddress);
        if (Child("m_interior_point") is { } interior)            // vfunc_102(m_3d8, m_3dc): FormatStringId(0x25, nRank, nPoint)
        {
            Host.Set(interior, "enabled", true);
            Host.Set(interior, "text", Format(Common("s_char_info_home_commend"), info.HomeCommendMonthly, info.HomeCommendPoint));
        }
        Titles();
        if (Child("m_character") is { } ch)                       // vfunc_100: "%s_%d" of the gender and job group
            Host.Invoke(ch, "gotoAndStop", (info.Gender == 0 ? "MALE" : "FEMALE") + "_" + info.JobGroup.ToString(CultureInfo.InvariantCulture));
        Guild();
        Adventure(info.PrestigeLevel);
        Wedding();
        Symbol();
    }

    /// <summary>
    /// UIMyInfoDialog::SetSymbol (@0x141a5ac90) with the local PC's symbol: setSymbol(its name from string table 0x27, the
    /// ranked icon, CheckCondition == 0). The movie draws the icon disabled while the condition is not met. The trophy
    /// condition reads the achievement tally (none = 0).
    /// </summary>
    void Symbol()
    {
        ushort id = SymbolId?.Invoke() ?? 0;
        var table = Model.Tables.NametagSymbols;
        var player = new NametagSymbols.Player(info.Level, info.PrestigeLevel, GmGrade?.Invoke() ?? 0, info.Titles, AchieveTally?.Invoke() ?? 0);
        Host.Invoke("setSymbol", new object[] { Model.Tables.NametagSymbolTitle(id), table.IconRanked(id, player), table.CheckCondition(id, player) == 0 });
    }

    /// <summary>ClearProfile (@0x141333090): no name, trophy 0, m_title disabled.</summary>
    void ClearProfile()
    {
        Host.Invoke(Host.Entity, "setName", "");
        Host.Invoke("setTrophy", 0u);
        if (Child("m_title") is { } t) Host.Set(t, "enabled", false);
    }

    /// <summary>vfunc_94 (@0x141333180): m_jobImage to frame "_%d", m_job's text the job name (string table 6).</summary>
    void JobText(int job)
    {
        if (Child("m_jobImage") is { } img) Host.Invoke(img, "gotoAndStop", "_" + ((uint)job).ToString(CultureInfo.InvariantCulture));
        if (Child("m_job") is { } j) Host.Set(j, "text", Model.Tables.JobName(job));
    }

    /// <summary>UIMyInfoDialog::vfunc_104 (@0x141a5ae90): setTrophy(the achievement tally's sum).</summary>
    void Trophy()
    {
        if (AchieveTally != null) Host.Invoke("setTrophy", AchieveTally());
    }

    /// <summary>vfunc_101 (@0x141333dd0): enabled with an address; the local player's empty one is GetStringCommon(0x22).</summary>
    void Address(string address)
    {
        if (Child("m_address") is not { } a) return;
        Host.Set(a, "enabled", address.Length > 0);
        Host.Set(a, "text", info.IsLocal && address.Length == 0 ? Common("s_char_info_ugcmap") : address);
    }

    /// <summary>vfunc_103 (@0x141334120): FormatStringId(0x24, guild, grade); no guild: GetStringCommon(0x21) for the local
    /// player, else empty; enabled only with a guild.</summary>
    void Guild()
    {
        if (Child("m_guild") is not { } g) return;
        string text = "";
        if (info.GuildName.Length == 0) { if (info.IsLocal) text = Common("s_char_info_guild"); }
        else text = Format(Common("s_char_info_guild_name"), info.GuildName, info.GuildGrade);
        Host.Set(g, "enabled", info.GuildName.Length > 0);
        Host.Set(g, "text", text);
    }

    /// <summary>vfunc_105 (@0x141334390): m_adventureLevel's text, it and its icon hidden for 0.</summary>
    void Adventure(int level)
    {
        if (Child("m_adventureLevel") is { } l)
        {
            Host.Set(l, "text", level.ToString(CultureInfo.InvariantCulture));
            Host.SetVisible(l, level != 0);
        }
        if (Child("m_adventureLevelIcon") is { } icon) Host.SetVisible(icon, level != 0);
    }

    /// <summary>vfunc_106 (@0x141334550): the wedding state's frame (1 promise, 2 marriage, 3 / 4 coolingoff), shown; 0 and 5
    /// hide it. (Its tooltip, GetWeddingTooltip, is not here.)</summary>
    void Wedding()
    {
        if (Child("m_wedding") is not { } w) return;
        int s = info.Wedding;
        if (s == 0 || s == 5) { Host.SetVisible(w, false); return; }
        string frame = s switch { 1 => "promise", 2 => "marriage", 3 or 4 => "coolingoff", _ => null };
        if (frame != null) Host.Invoke(w, "gotoAndStop", frame);
        Host.SetVisible(w, true);
    }

    /// <summary>vfunc_96 (@0x141333420): the titles sorted by name (qsort CompareTitleNames, wcscmp); the local player's
    /// list is a "no title" row (id 0) and each title with a name, the shown title selected (its sorted position + 1),
    /// disabled without titles, min(n + 1, 5) menu rows; another player's is its one title, disabled.</summary>
    void Titles()
    {
        if (Child("m_title") is not { } menu) return;
        var sorted = info.Titles.OrderBy(id => Model.Tables.TitleName((int)id), StringComparer.Ordinal).ToList();
        var dp = Host.Get(menu, "dataProvider");
        Host.Invoke(dp, "cleanUp");                                         // ASDropdownMenu::ClearItems
        if (info.IsLocal)
        {
            Host.Invoke(dp, "push", Host.Obj(("label", Model.Tables.TitleName(0)), ("id", 0)));
            int selected = 0;
            for (int i = 0; i < sorted.Count; i++)
            {
                string label = Model.Tables.TitleName((int)sorted[i]);
                if (label.Length == 0) continue;
                Host.Invoke(dp, "push", Host.Obj(("label", label), ("id", sorted[i])));
                if (sorted[i] == (uint)info.Title) selected = i + 1;
            }
            Host.Set(menu, "selectedIndex", selected);
            if (sorted.Count == 0) Host.Set(menu, "enabled", false);
            Host.Set(menu, "menuRowCount", (uint)Math.Min(sorted.Count + 1, 5));
        }
        else
        {
            if ((uint)info.Title > 0)
            {
                Host.Invoke(dp, "push", Host.Obj(("label", Model.Tables.TitleName(info.Title))));
                Host.Set(menu, "selectedIndex", 0);
            }
            Host.Set(menu, "enabled", false);
        }
        Host.Invoke(menu, "invalidate");                                    // vfunc_24(true)
        Host.Invoke(menu, "validateNow");
    }

    /// <summary>vfunc_85 / vfunc_99 (@0x141333bb0): setTodayMemo(motto, local).</summary>
    void Memo()
    {
        if (info == null) return;
        Host.Invoke(Host.Entity, "setTodayMemo", info.Motto, info.IsLocal);
    }

    string ProfilePath() => info == null || ProfileImagePath == null ? "" : ProfileImagePath(info) ?? "";

    /// <summary>vfunc_98 (@0x141333980): setProfile(path) for a non-empty path, m_profile enabled with the "enable" label
    /// for the local player, else disabled with the "disable" label.</summary>
    void SetProfileImage(string path)
    {
        if (info == null || path.Length == 0) return;
        Host.Invoke(Host.Entity, "setProfile", path);
        if (Child("m_profile") is not { } p) return;
        Host.Set(p, "enabled", info.IsLocal);
        Host.Set(p, "label", info.IsLocal ? "$s_myInfo_menu_select_profile_enable" : "$s_myInfo_menu_select_profile_disable");
    }

    /// <summary>ShowProfileImage (@0x14132faf0): while open, vfunc_98 of <paramref name="path"/> (the upload dialog's
    /// preview of a picked file's thumbnail).</summary>
    public void ShowProfileImage(string path)
    {
        if (IsClosed) return;
        SetProfileImage(path ?? "");
    }

    /// <summary>RefreshProfileImage (@0x14132fa90): vfunc_98 of GetProfileImagePath, the stored picture again.</summary>
    public void RefreshProfileImage() => SetProfileImage(ProfilePath());

    /// <summary>
    /// UIPCInfoDialog's UIUGCEventDispatcher listener (uipcinfodialog.cpp:545): a picture for the shown character, while
    /// the window is open, becomes its picture and is shown again (vfunc_98 of the picture's path).
    /// </summary>
    public void OnProfileImage(long characterId, string key)
    {
        if (info == null || IsClosed || characterId == 0 || info.CharacterId != characterId) return;
        info.Picture = key ?? "";
        SetProfileImage(ProfilePath());
    }

    // ---- vfunc_86 / vfunc_87: the stats ----------------------------------------------------------------------------

    /// <summary>vfunc_86 (@0x14132ff30): the item level, attack and defense labels, m_statLabelManager.setData(rows), vfunc_87.</summary>
    void Stats()
    {
        if (info == null) return;
        bool score = Feature(0xdf);
        if (Child("m_itemLevelInfo") is AsDisplayObject il && (ItemLevel?.Invoke(info) ?? KnownItemLevel()) is var (total, additional))
        {
            Host.SetChildText("detail", Format("{0} + ({1})", total - additional, additional), il);
            Host.SetChildText("value", total.ToString(CultureInfo.InvariantCulture), il);
            Host.Invoke(il, "changeTooltipText", Localize("$s_tooltip_myinfo_stat_itemLevel_ohAdditional"));
        }
        ScoreLabel("m_attackInfo", OffenseScore?.Invoke(info), score, "attackScore");
        long? defense = score ? DefenseScoreLua?.Invoke(info) : info.Stats2[0x14];
        ScoreLabel("m_defenseInfo", defense, score, "defenseScore");
        if (Child("m_statLabelManager") is { } labels)
        {
            var rows = new List<object>();
            int g = 0, n = 0;
            foreach (int s in new[] { 4, 7, 10, 0, 1, 2, 3, 0xe }) rows.Add(BasicRow(s, g, n++));
            rows.Add(ShieldRow(g, n++));
            g++; n = 0;
            foreach (var (basic, s) in new[] { (true, 0x1b), (true, 0x1d), (true, 0x17), (true, 0x18), (true, 0x1f), (false, 0x1a), (false, 0x1b),
                (true, 0xf), (true, 0x11), (true, 0x12), (false, 0x18), (false, 0x19), (false, 0x13), (false, 0x12), (false, 0x17), (false, 0x16),
                (false, 0x14), (false, 0x15), (false, 0xc), (false, 7), (false, 0x10), (false, 0xce), (true, 0xd), (false, 0x23), (false, 0x36) })
                rows.Add(basic ? BasicRow(s, g, n++) : SpecialRow(s, g, n++));
            g++; n = 0;
            foreach (var (basic, s) in new[] { (true, 0x14), (true, 0x19), (true, 0x1a), (true, 0x10), (true, 0x13), (false, 0x25), (false, 0x26),
                (false, 0x1d), (false, 0x1c), (false, 0x21), (false, 0x20), (false, 0x1e), (false, 0x1f), (true, 0x15), (false, 0xcc), (false, 0x22),
                (false, 0x24), (true, 5), (false, 0x37) })
                rows.Add(basic ? BasicRow(s, g, n++) : SpecialRow(s, g, n++));
            var array = Host.RT.Construct(Host.RT.GetClass("Array"), Array.Empty<object>());
            foreach (var r in rows) Host.Invoke(array, "push", r);
            Host.Invoke(labels, "setData", array);
        }
        StatPointRows();
    }

    /// <summary>
    /// The record's m_3cc / m_3d0: PC::vfunc_125 (pc.cpp:4154) zeroes them and vfunc_126 adds the layer-0 items' levels
    /// (<see cref="ItemLevels.Worn"/>; layers 1 and 2 add nothing). PC::CanUseItem here is its first test,
    /// <see cref="ItemLevels.Usable"/>; its PC bang, VIP, wedding and owner tests are not (the shown player's worn items
    /// passed them when worn). Null, the label left, when a level needs Lua (a plus level, two one-handed
    /// weapons).
    /// </summary>
    (uint, uint)? KnownItemLevel()
    {
        var maps = EquipMaps(info);
        if (maps == null) return null;
        long now = ServerTime?.Invoke() ?? 0;
        return ItemLevels.Worn(maps[0], Model.Tables.BothHandGearScorePenaltyDiv, it => ItemLevels.Usable(it, now));
    }

    /// <summary>The attack / defense label: detail "", value "{0}" of the score; with feature 0xdf the html label text and
    /// ASLabel::SetTooltipData of an ASTooltip (class export_common_tooltip_tooltipText, text, onDelay 300, offDelay 5000,
    /// tooltipAlign 4). A score not here leaves the value.</summary>
    void ScoreLabel(string name, long? value, bool feature, string key)
    {
        if (Child(name) is not AsDisplayObject l) return;
        Host.SetChildText("detail", "", l);
        if (value is long v) Host.SetChildText("value", v.ToString(CultureInfo.InvariantCulture), l);
        if (!feature) return;
        if (Host.FindNamed("textField", l) is { } tf) Host.Set(tf, "htmlText", Localize("$s_myInfo_label_" + key));
        SetTooltipData(l, Host.Obj(("tooltip", "export_common_tooltip_tooltipText"), ("text", "$s_tooltip_myinfo_stat_" + key),
            ("onDelay", 300), ("offDelay", 5000), ("tooltipAlign", 4)));
    }

    /// <summary>ASUIComponent::SetTooltipData (asuicomponent.cpp:228): "_tooltipData", then RegisterTooltip (:247, once per
    /// component: FUN_1422c6380 = the movie's TooltipManager.registerMouseEvent).</summary>
    void SetTooltipData(object c, object data)
    {
        Host.Set(c, "_tooltipData", data);
        if (registered.Add(c)) RegisterMouseEvent(c);
    }
    readonly HashSet<object> registered = new(ReferenceEqualityComparer.Instance);

    /// <summary>ASTooltipManager::RegisterMouseEvent (astooltipmanager.cpp:26): TooltipManager.registerMouseEvent(obj).</summary>
    void RegisterMouseEvent(object c)
    {
        if (Host.RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm) Host.Invoke(tm, "registerMouseEvent", c);
    }

    /// <summary>ASMovieClip::SetExternalTooltipFunction (asmovieclip.cpp:285): the function member "externalTooltipFunction",
    /// which TooltipManager.showTooltipMouseEvent calls for the tooltip data.</summary>
    void SetExternalTooltipFunction(object c, Func<object> tooltip) =>
        Host.Set(c, "externalTooltipFunction", new AsPlainFunction(Host.RT, new NativeMethod("externalTooltipFunction", (_, _) => tooltip())));

    /// <summary>An ASTooltip(pc, 1, 2) with class export_common_tooltip_tooltipText, tooltipAlign 4 and the text.</summary>
    object TextTooltip(string text) =>
        Host.Obj(("tooltip", "export_common_tooltip_tooltipText"), ("tooltipAlign", 4), ("text", text));

    /// <summary>FUN_14132eed0 (@0x14132eed0): GetStringCommonByKey("s_char_info_stat_point_tooltip_" + GetStatName(stat)); none
    /// without a local player.</summary>
    object StatPointTooltip(int stat) =>
        Model.HasLocalPlayer ? TextTooltip(Common("s_char_info_stat_point_tooltip_" + Model.Tables.StatOption(stat))) : null;

    /// <summary>FUN_14132f020 (@0x14132f020): with feature 0xbd and the level at least AdventureLevelStartLevel (Constants
    /// m_9298) "s_char_info_stat_point_by_reason_with_adventure_level" of the total and the extras 2, 1, 3, 4; else
    /// "s_char_info_stat_point_by_reason" of the total and the extras 2, 1, 3.</summary>
    object RemainTooltip()
    {
        if (!Model.HasLocalPlayer || StatPoints == null || info == null) return null;
        var p = StatPoints;
        string text = Feature(0xbd) && info.Level >= Model.Tables.AdventureLevelStartLevel
            ? Format(Common("s_char_info_stat_point_by_reason_with_adventure_level"), (uint)p.Total, p.GetExtra(2), p.GetExtra(1), p.GetExtra(3), p.GetExtra(4))
            : Format(Common("s_char_info_stat_point_by_reason"), (uint)p.Total, p.GetExtra(2), p.GetExtra(1), p.GetExtra(3));
        return TextTooltip(text);
    }

    /// <summary>GetAverageStat (@0x141335780): (Stats2[0x1b] + Stats2[0x1c]) * 0.5f.</summary>
    long AverageStat() => (long)((float)(info.Stats2[0x1b] + info.Stats2[0x1c]) * 0.5f);

    /// <summary>GetSpecialStat (@0x141335d20): base 0, bonus = total = m_3b0[stat] (/ 10 when bTenth); null without the
    /// equipment abilities.</summary>
    (float Base, float Bonus, float Total)? Special(int stat, bool tenth)
    {
        if (info.Special == null) return null;
        int v = info.Special.TryGetValue(stat, out var x) ? x : 0;
        float f = tenth ? v / 10.0f : v;
        return (0f, f, 0f + f);
    }

    /// <summary>GetBasicStat (@0x141335940); null when a part it needs is not here.</summary>
    (float Base, float Bonus, float Total)? Basic(int stat)
    {
        float b = info.Stats1[stat], bonus = info.Stats0[stat] - b, t = info.Stats2[stat];
        if (stat is 4 or 10 or 7) t = info.Stats0[stat];
        else if (stat is 0x1b or 0x1c) { b = 0f; bonus = AverageStat(); t = bonus; }
        else if (stat is 0x1f or 0x15) { b /= 10.0f; bonus /= 10.0f; t /= 10.0f; }
        else if (stat == 0x12)
        {
            if (CritRate == null || Special(0xcb, false) is not var (_, crit, _)) return null;
            long critical = (long)crit;
            b = CritRate((long)b, 0f, 0) * 100.0f;
            bonus = (bonus + critical) * 0.1f;
            t = CritRate((long)t, 0f, critical) * 100.0f;
        }
        else if (stat == 0x11)
        {
            if (Special(0xca, false) is not var (_, acc, _)) return null;
            long accuracy = (long)acc;
            bonus += accuracy;
            t += accuracy;
        }
        return (b, bonus, t);
    }

    static string F1(float f) => f.ToString("F1", CultureInfo.InvariantCulture);
    static string I(float f) => ((long)f).ToString(CultureInfo.InvariantCulture);

    object Row(int container, int label, string name, string value, string detail) =>
        Host.Obj(("containerIndex", (uint)container), ("labelIndex", (uint)label), ("name", name), ("value", value), ("detail", detail));

    /// <summary>BuildBasicStatRow (@0x141335e00): rate stats (0xd, 0xe, 0x12, 0x15, 0x1f) as "{0:0.1f}" in s_common_percent,
    /// others "{0}"; detail "base+bonus" for a positive bonus, "basebonus" for a negative one, else "-".</summary>
    object BasicRow(int stat, int container, int label)
    {
        string name = Model.Tables.StatName(stat);
        if (Basic(stat) is not var (b, bonus, t)) return Row(container, label, name, "", "");
        bool rate = stat is 0xd or 0xe or 0x12 or 0x15 or 0x1f;
        string value = rate ? Format(Common("s_common_percent"), F1(t)) : I(t);
        string detail = bonus > 0f ? (rate ? F1(b) + "+" + F1(bonus) : "(" + I(b) + "+" + I(bonus) + ")")
            : bonus < 0f ? (rate ? F1(b) + F1(bonus) : "(" + I(b) + I(bonus) + ")")
            : "-";
        return Row(container, label, name, value, detail);
    }

    /// <summary>BuildSpecialStatRow (@0x1413363d0): tenths (not 0x36 / 0x37) as "{0:0.1f}" in s_common_percent; detail "-" for
    /// no bonus.</summary>
    object SpecialRow(int stat, int container, int label)
    {
        string name = Model.Tables.SpecialAbilityName(stat);
        bool tenth = stat is not (0x36 or 0x37);
        if (Special(stat, tenth) is not var (b, bonus, t)) return Row(container, label, name, "", "");
        string value = tenth ? Format(Common("s_common_percent"), F1(t)) : I(t);
        string detail = bonus == 0f ? "-"
            : bonus >= 0f ? (tenth ? F1(b) + "+" + F1(bonus) : "(" + I(b) + "+" + I(bonus) + ")")
            : (tenth ? F1(b) + F1(bonus) : "(" + I(b) + I(bonus) + ")");
        return Row(container, label, name, value, detail);
    }

    /// <summary>BuildShieldRow (@0x1413369a0): "s_pc_info_name_shield", the shield total, detail "-".</summary>
    object ShieldRow(int container, int label) =>
        Row(container, label, Common("s_pc_info_name_shield"), Shield == null ? "" : Shield().ToString(CultureInfo.InvariantCulture), "-");

    /// <summary>
    /// vfunc_87 (@0x1413322c0): per cost stat (GetCostIds) m_statPoint{i+1}: m_Label the stat name, m_point Stats0[4] for
    /// health else Stats2; for the local record m_point orange (0xffa200) with points put in, m_plus disabled with none
    /// remaining. m_resetStatPoint enabled with points used; m_remainPoint's m_remainStatPoint the remaining (orange when
    /// any) for the local record, else hidden. (The rows' tooltips, FUN_14132eed0 / FUN_14132f020, are not here.)
    /// </summary>
    void StatPointRows()
    {
        int remaining = 0, used = 0;
        if (info.IsLocal)
        {
            if (StatPoints == null) return;
            remaining = StatPoints.Remaining; used = StatPoints.Used;
        }
        var ids = StatPointBook.CostIds;
        for (int i = 0; i < ids.Length; i++)
        {
            if (Child("m_statPoint" + (i + 1).ToString(CultureInfo.InvariantCulture)) is not AsDisplayObject row) continue;
            int stat = ids[i];
            Host.SetChildText("m_Label", Model.Tables.StatName(stat), row);
            long v = stat == 4 ? info.Stats0[stat] : info.Stats2[stat];
            Host.SetChildText("m_point", v.ToString(CultureInfo.InvariantCulture), row);
            if (info.IsLocal && Host.FindNamed("m_point", row) is { } point)
                Host.Set(point, "textColor", (uint)StatPoints.GetStatPoints(stat) > 0 ? 0xffa200u : 0xffffffu);
            if (Host.Child("m_plus", row) is { } plus)
            {
                if ((uint)remaining == 0) Host.Set(plus, "enabled", false);
                else if (!Rt.ToBoolean(Host.Get(plus, "enabled"))) Host.Set(plus, "enabled", true);
            }
            SetExternalTooltipFunction(row, () => StatPointTooltip(stat));
            RegisterMouseEvent(row);
        }
        if (Child("m_resetStatPoint") is { } reset) Host.Set(reset, "enabled", (uint)used > 0);
        if (Child("m_remainPoint") is AsDisplayObject remain && Host.FindNamed("m_remainStatPoint", remain) is { } text)
        {
            Host.SetVisible(text, info.IsLocal);
            if (info.IsLocal)
            {
                Host.Set(text, "text", ((uint)remaining).ToString(CultureInfo.InvariantCulture));
                Host.Set(text, "textColor", (uint)remaining > 0 ? 0xffa200u : 0xffffffu);
            }
        }
        if (info.IsLocal && Child("m_remainPoint") is { } remainClip)
        {
            SetExternalTooltipFunction(remainClip, RemainTooltip);
            RegisterMouseEvent(remainClip);
        }
    }

    // ---- vfunc_88: the equipment bag -------------------------------------------------------------------------------

    /// <summary>vfunc_88 (@0x141332e00): tab 2 the gem bag (FUN_14126e2f0) over the record, else the equip bag of
    /// GetEquipBagKindForTab (0 -> 0, 1 -> 1, 3 -> 2, else none; CreateEquipSlotBag) over the record; equipBagID = the bag's
    /// name. A bag keeps the record it was made with (EquipSlotBag / GemSlotBag m_spPC).</summary>
    void Bag(int t)
    {
        if (Host.Entity == null || info == null) return;
        if (t == 2) gemBagRecord = info;
        else
        {
            int kind = t switch { 0 => 0, 1 => 1, 3 => 2, _ => 6 };
            if (kind != 6) { equipBagRecord = info; equipBagKind = kind; }
        }
        ShownBag = t == 2 ? GemBag : EquipBag;
        Host.Set(Host.Entity, "equipBagID", ShownBag);       // a setter only: it reads GameDataModel.getBagSlotBindings
    }

    PcInfo equipBagRecord, gemBagRecord;
    int equipBagKind;
    PcInfo mapsRecord;
    Dictionary<int, Item>[] maps;

    /// <summary>EquipSlotBag's slots (equipslot.cpp:426): index -> Unk_InventoryEquipSlot, kind 1 / 2 and kind 0.</summary>
    static readonly int[] SkinSlots = { 6, 0xe, 0xd, 0xc, 8, 10, 9, 0x10, 7, 0xb, 5, 4 };
    static readonly int[] GearSlots = { 6, 0xe, 0xf, 0x11, 8, 10, 9, 0x10, 7, 0xb, 5, 4 };
    /// <summary>GemSlotBag's slots (gemslot.cpp): index -> GemType.</summary>
    static readonly int[] GemTypes = { 1, 5, 3, 2, 4, 6, 9, 8, 7, 10, 11, 12 };

    static int[] SlotsOf(int kind) => kind == 1 || kind == 2 ? SkinSlots : GearSlots;

    /// <summary>EquipSlotBag::vfunc_15 (equipslot.cpp:478): StringFormat("{0}.{1}", bag, the equip slot); GemSlotBag "%s.%d" of
    /// the index. (The equip slot is written as its number here; the movie only passes the names back.)</summary>
    public override IEnumerable<string> GetBagSlotBindings(string bag) =>
        bag == EquipBag && equipBagRecord != null ? SlotsOf(equipBagKind).Select(s => EquipBag + "." + s.ToString(CultureInfo.InvariantCulture))
        : bag == GemBag && gemBagRecord != null ? Enumerable.Range(0, GemTypes.Length).Select(i => GemBag + "." + i.ToString(CultureInfo.InvariantCulture))
        : Array.Empty<string>();

    /// <summary>The record's equipment layer maps (<see cref="WornEquipment.Layers"/>), kept for the record.</summary>
    Dictionary<int, Item>[] EquipMaps(PcInfo r)
    {
        if (r?.Equips == null) return null;
        if (ReferenceEquals(r, mapsRecord)) return maps;
        var m = WornEquipment.Layers(r, Model.Tables.Items);
        mapsRecord = r; maps = m;
        return m;
    }

    /// <summary>PCInfo::GetEquipItemByKind (pcinfo.cpp:155): kind 0 / 1 / 2 the layer map, else none.</summary>
    Item EquipItemByKind(PcInfo r, int kind, int slot) =>
        kind is >= 0 and <= 2 && EquipMaps(r) is { } m && m[kind].TryGetValue(slot, out var it) ? it : null;

    /// <summary>CItemData::FUN_14053d460 (<see cref="WornEquipment.IsTwoHanded"/>).</summary>
    bool TwoHanded(ItemData d) => WornEquipment.IsTwoHanded(d, Model.Tables.ItemModels);
    /// <summary>CItemData::FUN_14053d2b0 (itemdata.cpp:848): equip subtype 0x16 (a suit over the pants).</summary>
    static bool Suit(ItemData d) => d.EquipSubtype == 0x16;

    /// <summary>EquipSlot::FUN_14120ce90 (equipslot.cpp:294): the slot's item; an empty LH shows a two-handed RH item, an empty
    /// PA a suit CL item.</summary>
    Item EquipSlotItem(PcInfo r, int kind, int slot)
    {
        var it = EquipItemByKind(r, kind, slot);
        if (it != null) return it;
        if (slot == 4) { it = EquipItemByKind(r, kind, 5); return it != null && TwoHanded(it.Data) ? it : null; }
        if (slot == 9) { it = EquipItemByKind(r, kind, 8); return it != null && Suit(it.Data) ? it : null; }
        return null;
    }

    /// <summary>
    /// EquipSlot::vfunc_9 (@0x14120cd40) / GemSlot::vfunc_9 (@0x14126e080): the bound slot's m_strSlotType is its equip slot's
    /// name (FUN_1405451e0) / gem type's name, which EquipSlot.setSlotType / setSlotBg read for the empty slot's frame and
    /// silhouette (UIMyInfoDialog_slotbg_&lt;type&gt;); without it the slot keeps its instance name and shows neither.
    /// </summary>
    public override void OnBindData(string name, object data)
    {
        string type = null;
        if (name.StartsWith(EquipBag + ".", StringComparison.Ordinal) && int.TryParse(name.AsSpan(EquipBag.Length + 1), out int slot))
            type = ItemModelTable.SlotName(slot);
        else if (name.StartsWith(GemBag + ".", StringComparison.Ordinal) && int.TryParse(name.AsSpan(GemBag.Length + 1), out int index)
                 && index >= 0 && index < GemTypes.Length)
            type = GemTypeName(GemTypes[index]);
        if (type != null) Host.Set(data, "m_strSlotType", type);
    }

    /// <summary>
    /// EquipSlot::vfunc_8 (@0x14120cae0) / GemSlot::vfunc_8 (gemslot.cpp:264) for the bags' names; other names go to the base.
    /// </summary>
    public override object GetBinding(string name)
    {
        if (name.StartsWith(EquipBag + ".", StringComparison.Ordinal) && int.TryParse(name.AsSpan(EquipBag.Length + 1), out int slot))
            return EquipBinding(slot);
        if (name.StartsWith(GemBag + ".", StringComparison.Ordinal) && int.TryParse(name.AsSpan(GemBag.Length + 1), out int index))
            return GemBinding(index);
        return base.GetBinding(name);
    }

    /// <summary>
    /// EquipSlot::vfunc_8: null without a record, a local player or an item; else ASSlot::SetEquipItem with flags 0x49 (| 4
    /// for the local record): FUN_1409c3860, then itemBothEquip (asitemslot.cpp:1386: a skin item with a layer-0 item at its
    /// slot, or another item with a layer-1 one; read from the record's maps here, the local player's equipment in the
    /// client); grayColor for a two-handed item in LH or a suit in PA; disable for the local record's layer-2 slots while the
    /// title layer is not active.
    /// </summary>
    object EquipBinding(int slot)
    {
        var r = equipBagRecord;
        if (r == null || Model == null || !Model.HasLocalPlayer) return null;
        var item = EquipSlotItem(r, equipBagKind, slot);
        if (item == null) return null;
        int flags = 0x49 | (r.IsLocal ? 4 : 0);
        var o = new List<(string, object)>();
        if (!ItemSlot.Build(o, item, true, flags, Model, Model.ItemSlotSources)) return null;
        var m = EquipMaps(r);
        bool both = item.Data.HasSkinType ? m[0].ContainsKey(slot) : m[1].ContainsKey(slot);
        o.Add(("itemBothEquip", both ? 1 : 0));
        if (slot == 4 && TwoHanded(item.Data)) o.Add(("grayColor", 1));
        else if (slot == 9 && Suit(item.Data)) o.Add(("grayColor", 1));
        if (r.IsLocal && equipBagKind == 2 && !CodiLayerActive(r)) o.Add(("disable", 1));
        return Host.Obj(o.ToArray());
    }

    /// <summary>CEquipmentSlotLayerInfo::IsActive (m_bActive = !FUN_1411df700, equipment.cpp:401): not active while the layer is
    /// featured (kind 2, feature 0x8e) and never started or ended.</summary>
    bool CodiLayerActive(PcInfo r)
    {
        if (!Feature(0x8e)) return true;
        if (r.CodiStart == 0) return false;
        return !(r.CodiEnd > 0 && (ServerTime?.Invoke() ?? 0) >= r.CodiEnd);
    }

    /// <summary>IsValidGemType (unit_1402226b0_featureenumhelpers.cpp:219): 1, 3, 4 always, the others by their feature.</summary>
    bool IsValidGemType(int type) => type switch
    {
        1 or 3 or 4 => true,
        5 => Feature(0x67), 6 => Feature(0x68), 2 => Feature(0x66), 7 => Feature(0x69), 8 => Feature(0x6a),
        9 => Feature(0x9d), 10 => Feature(0x6b), 11 => Feature(0x6c), 12 => Feature(0x6d),
        _ => false,
    };

    /// <summary>
    /// GemSlot::vfunc_8: null without a record or a local player, or for a valid gem type without an item; else "lock" (not a
    /// valid gem type) and, for a gem item (PCInfo::FindItemByGemType, pcinfo.cpp:113: the first with that gem type),
    /// FUN_1409c3860 with flags 0x49 (| 4 for the local record), "config" (the local record and subtype 1) and "gemSystem".
    /// </summary>
    object GemBinding(int index)
    {
        var r = gemBagRecord;
        if (r == null || Model == null || !Model.HasLocalPlayer || (uint)index >= GemTypes.Length) return null;
        int type = GemTypes[index];
        bool locked = !IsValidGemType(type);
        var e = r.Gems?.FirstOrDefault(g => g.GemType == type);
        var item = e != null ? InventoryController.ToItem(e, Model.Tables.Items) : null;
        if (!locked && item == null) return null;
        var o = new List<(string, object)> { ("lock", locked) };
        if (item != null && item.Data.IsGemItem)
        {
            int flags = 0x49 | (r.IsLocal ? 4 : 0);
            if (ItemSlot.Build(o, item, true, flags, Model, Model.ItemSlotSources))
            {
                o.Add(("config", r.IsLocal && item.Data.Subtype == 1));
                o.Add(("gemSystem", (int)e.GemType));
            }
        }
        return Host.Obj(o.ToArray());
    }

    /// <summary>
    /// The slots' ITooltip::vfunc_0 through NotifyDragSlot::GetSlotTooltip: null for a name no slot of the bags has; an
    /// empty object when the slot gives none (GetSlotTooltip makes the object before asking the slot).
    /// </summary>
    public override IDictionary<string, object> GetSlotTooltip(string name)
    {
        if (name.StartsWith(EquipBag + ".", StringComparison.Ordinal) && int.TryParse(name.AsSpan(EquipBag.Length + 1), out int slot)
            && equipBagRecord != null && SlotsOf(equipBagKind).Contains(slot))
            return EquipTooltip(slot) ?? new AsTooltip();
        if (name.StartsWith(GemBag + ".", StringComparison.Ordinal) && int.TryParse(name.AsSpan(GemBag.Length + 1), out int index)
            && gemBagRecord != null && (uint)index < GemTypes.Length)
            return GemTooltip(index) ?? new AsTooltip();
        return null;
    }

    /// <summary>ASTooltip(the local player, 0x200 for the own record (PCInfo m_3c0) else 0x100, 2). The owner's character
    /// id and name are the record's when it is the local player's; otherwise not known here (the binding's own name).</summary>
    ItemTooltip NewItemTooltip(PcInfo r) => new(Model, r.IsLocal ? 0x200 : 0x100) { Owner = r.IsLocal ? (r.CharacterId, r.Name) : null, Localize = Localize };

    /// <summary>
    /// EquipSlot::vfunc_0 (equipslot.cpp:208): none without a record or a local player; ASTooltip(player, 0x200 for the own
    /// record (PCInfo m_3c0) else 0x100, 2) over FUN_14120ce90's item; when it fills, SetEnablePuton(GetStringCommon(0x752)).
    /// Then, the slot being enabled (MS2DragSlot::IsEnabled; EquipSlot's ctor sets it, equipslot.cpp:112), SetEnablePreview.
    /// </summary>
    AsTooltip EquipTooltip(int slot)
    {
        var r = equipBagRecord;
        if (r == null || !Model.HasLocalPlayer) return null;
        var tip = NewItemTooltip(r);
        var item = EquipSlotItem(r, equipBagKind, slot);
        if (!tip.Fill(item)) return null;
        tip.SetEnablePuton(Common("s_msg_rclick_putoff_equip"));                  // 0x752
        tip.SetEnablePreview(item);
        return tip.Data;
    }

    /// <summary>
    /// GemSlot::vfunc_0 (gemslot.cpp:227): none without a record, a local player or for a gem type that is not valid; a
    /// gem item: ASTooltip(player, 0x200 own / 0x100, 2) and, when it fills, SetEnablePuton(vfunc_22), the base
    /// DragSlot::vfunc_22's "" (dragslot.cpp:142); no item: the text tooltip "$s_tooltip_myinfo_gem_" + Name_140219b10(type) (commonenum.cpp:625),
    /// align 3.
    /// </summary>
    AsTooltip GemTooltip(int index)
    {
        var r = gemBagRecord;
        if (r == null || !Model.HasLocalPlayer) return null;
        int type = GemTypes[index];
        if (!IsValidGemType(type)) return null;
        var e = r.Gems?.FirstOrDefault(g => g.GemType == type);
        var item = e != null ? InventoryController.ToItem(e, Model.Tables.Items) : null;
        if (item != null)
        {
            var tip = NewItemTooltip(r);
            if (!tip.Fill(item)) return null;
            tip.SetEnablePuton("");
            return tip.Data;
        }
        var text = AsTooltip.Text("$s_tooltip_myinfo_gem_" + GemTypeName(type));
        text?.SetTooltipAlign(3);
        return text;
    }

    /// <summary>Name_140219b10 (commonenum.cpp:625): the gem type's name.</summary>
    static string GemTypeName(int type) => type switch
    {
        1 => "trans", 2 => "dmgFont", 3 => "chatBalloon", 4 => "nameTag", 5 => "tomb", 6 => "swim", 7 => "charEffect",
        8 => "fishing", 9 => "autoMastery", 10 => "effect", 0xb => "petSkin", 0xc => "meta", _ => "",
    };

    /// <summary>The bag name last given to equipBagID.</summary>
    public string ShownBag { get; private set; }

    /// <summary>UIMyInfoDialog::vfunc_90 (@0x141a5b3b0): the base's (m_nTab, vfunc_88); the codi tab (3) checks its period
    /// (FUN_141a5c410) and shows it (FUN_141a5b6c0); another tab empties m_equip.m_period.</summary>
    void ChangeTab(int t)
    {
        int old = tab;
        tab = t;
        Bag(tab);
        if (t == 3) { CodiTabCheck(old, t); CodiPeriod(); }
        else PeriodText("");
    }

    /// <summary>The message box key of FUN_141a5c410 (GetStringCommonByKey).</summary>
    public const string CodiTabNotActiveKey = "s_char_info_changetab_not_active_codi_tab";

    /// <summary>FUN_141a5c410 (@0x141a5c410): the title layer's period state 0 or 4 shows a type-0 box (FUN_140d73f20, shown
    /// by FUN_140d73c60) answered by OnCodiTabChangeAnswer with the tab before.</summary>
    void CodiTabCheck(int before, int t)
    {
        if (t != 3 || info == null) return;
        int state = info.GetCodiPeriodState(Feature(0x8e), ServerTime?.Invoke() ?? 0);
        if (state != 0 && state != 4) return;
        var box = MessageBoxQueue.CreateWithCallback(MessageBoxData.CommonByKey(CodiTabNotActiveKey) ?? "", 0, (button, _) => OnCodiTabChangeAnswer(button, before));
        ShowBox(box);
    }

    /// <summary>OnCodiTabChangeAnswer (@0x141a5c640): OK (0) puts m_tab back on the tab before (its value as the button
    /// index, as the client does) when the record has no layer-2 items (m_mapEquip2 empty).</summary>
    bool OnCodiTabChangeAnswer(int button, int before)
    {
        if (button != 0) return true;
        if (Host.Entity != null && before != tab && info != null && info.CodiItems == 0 && Child("m_tab") is { } bar)
            Host.Set(bar, "selectedIndex", before);
        return true;
    }

    /// <summary>FUN_141a5b6c0 (@0x141a5b6c0) for the codi tab: state 1 "s_char_info_codi_tab_period_fmt" of the time left
    /// (days / hours / minutes, or "s_item_word_s_shortly"), state 3 "s_char_info_codi_tab_expired", else empty, as
    /// m_equip.m_period's html text. (The refresh timer m_268 / m_274 is not here.)</summary>
    void CodiPeriod()
    {
        if (Host.Entity == null || tab != 3 || info == null) return;
        long now = ServerTime?.Invoke() ?? 0;
        string text = "";
        switch (info.GetCodiPeriodState(Feature(0x8e), now))
        {
            case 1:
                if (now < info.CodiEnd)
                {
                    var left = TimeSpan.FromSeconds(info.CodiEnd - now);
                    string remain = left.Days > 0 ? Format(Common("s_item_word_d"), left.Days)
                        : left.Hours > 0 ? Format(Common("s_item_word_h"), left.Hours)
                        : left.Minutes > 0 ? Format(Common("s_item_word_m"), left.Minutes)
                        : Common("s_item_word_s_shortly");
                    text = Format(Common("s_char_info_codi_tab_period_fmt"), remain);
                }
                break;
            case 3: text = Common("s_char_info_codi_tab_expired"); break;
        }
        PeriodText(text, true);
    }

    /// <summary>m_258: m_equip's m_period (SetTextHTMLW for the period, SetTextW("") on another tab).</summary>
    void PeriodText(string text, bool html = false)
    {
        if (Host.Entity == null || Child("m_equip") is not AsDisplayObject equip || Host.FindNamed("m_period", equip) is not { } period) return;
        Host.Set(period, html ? "htmlText" : "text", text);
    }

    /// <summary>FUN_140d73c60: the box goes to the message box queue.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>GetServerTime (seconds).</summary>
    public Func<long> ServerTime;

    // ---- vfunc_28: the movie's calls -------------------------------------------------------------------------------

    /// <summary>
    /// UIMyInfoDialog::vfunc_28 (@0x141a595a0) after UIPCInfoDialog's (@0x14132e890). The callbacks that return 1 there do here;
    /// those that fall through to the avatar's vfunc_27 return false.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        int Arg(int i) => args.Length > i ? (int)Rt.ToNumber(args[i]) : 0;
        switch (name.ToLowerInvariant())
        {
            case "onremoveslot": return true;                                       // vfunc_89: the bags dropped
            case "onchangetab":                                                     // not returned as handled
                if (args.Length > 0) ChangeTab(Arg(0));
                return false;
            case "ondetailinfo": Stats(); return true;
            case "onaddstatpoint":                                                  // vfunc_91 -> RequestAdd: a request
            case "onresetstatpoint":                                                // the reset box, then RequestReset: a request
            case "onchangeprofile":                                                 // CUgcManager::OpenProfileUpload
            case "onchangetodaymemo":                                               // the input box, then SendOp::MyInfo
                Button?.Invoke(name); return true;
            case "onconfig": OpenDialog?.Invoke(0x4e75); Button?.Invoke(name); return false;
            case "ontitlechange":                                                   // SendOp::RequestUserEnv mode 1
            case "onscreenshot": case "onaddstatpointrepeat": case "onaddstatpointstop":
                Button?.Invoke(name); return false;
            case "onsymbolclick": ToggleDialog?.Invoke(0x4ed5); return false;
            case "onwedding": ToggleDialog?.Invoke(0x4f8c); return false;
            case "onclickmynews": Button?.Invoke(name); return true;
            case "notifyslotuse": return SlotUse(args);
        }
        return Avatar?.OnCallback(name, args) == true;                          // m_spAvatar->vfunc_27
    }

    /// <summary>
    /// NotifyDragSlot::SlotUse (notifydragslot.cpp:33) for a slot of the equip bag: the button is the second argument (0
    /// without one). Button 0 is EquipSlot::vfunc_13 (equipslot.cpp:116), nothing. Button 1: MS2DragSlot::vfunc_19 (the slot
    /// is enabled, EquipSlot's ctor) with action 0x49406 held is the preview (vfunc_16, not ported: raised), else
    /// <see cref="Unequip"/>. A gem bag slot's use is not ported: raised. The client guards none against a repeat.
    /// </summary>
    bool SlotUse(object[] args)
    {
        int button = args.Length > 1 ? InventoryController.ArgInt(args[1]) : 0;
        if (args.Length > 0 && args[0] is string n && n.StartsWith(EquipBag + ".", StringComparison.Ordinal)
            && int.TryParse(n.AsSpan(EquipBag.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
        {
            if (button != 1) return true;
            if (ActionKeyDown(0x49406)) { Button?.Invoke("notifySlotUse"); return true; }
            Unequip(slot);
            return true;
        }
        Button?.Invoke("notifySlotUse");
        return true;
    }

    /// <summary>The common string ids the unequip shows (commonstringtable.cpp:1684, :1824).</summary>
    public const string PutOffErrorKey = "s_item_err_putoff", InventoryFullKey = "s_err_inventory";

    /// <summary>g_ItemFunctionFlags (0x142d568c0, read by CItem::HasAnyEquipSlotOf, item.cpp:1373): the model group types of a
    /// worn part that cannot be taken off.</summary>
    static readonly int[] FixedPartGroups = { 1, 2, 3, 18 };

    /// <summary>
    /// EquipSlot::vfunc_14 (equipslot.cpp:121): the slot's item (vfunc_25, <see cref="EquipSlotItem"/>), none: nothing; else
    /// Unk_Inventory::RequestUnequipItem (inventory.cpp:1278): no local player, nothing; the local player's worn item of the uid
    /// (PC::FUN_141655b80), none (a record not the local player's): nothing; CItem::HasAnyEquipSlotOf (a model group of
    /// <see cref="FixedPartGroups"/>): box 207 (FUN_140d73bc0: ShowMessageBox(GetStringCommon(207), 0)); FUN_140a655b0
    /// (:1563): no empty cell in the item's tab (<see cref="Ms2.Net.Packets.InventoryView.HasEmptyCell"/>): system notice 67.
    /// Then the unequip request: nothing is taken off here, the server's equip packets do. A dead local player (IsDead, :1282)
    /// gets box 66 (<see cref="DeadKey"/>) before the item is looked up; FUN_140a655b0's own dead check (:1568) is then never
    /// reached.
    /// STILL-OPEN: the guide restriction (IsRestricted(5), notice 2185) and the sounds (the warning DAT_143ab78e8, the item's
    /// material sound).
    /// </summary>
    void Unequip(int slot)
    {
        var r = equipBagRecord;
        if (r == null || Model == null) return;
        if (EquipSlotItem(r, equipBagKind, slot) is { } item) RequestUnequip(item);
    }

    /// <summary>Unk_Inventory::RequestUnequipItem's part after the slot (see <see cref="Unequip"/>) for a worn item of the shown
    /// equip bag's record.</summary>
    public void RequestUnequip(Item item)
    {
        var r = equipBagRecord;
        if (item == null || r == null || Model == null || !Model.HasLocalPlayer) return;
        if (LocalPlayerDead()) { ShowCommonBox(DeadKey); return; }       // inventory.cpp:1282
        if (!r.IsLocal || Inventory == null) return;
        var model = Model.Tables.ItemModels.Get(item.Data.ItemPreset);
        if (model != null && FixedPartGroups.Any(model.HasSlotType))
        {
            ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(PutOffErrorKey) ?? "", null, ""));
            return;
        }
        if (!Inventory.HasEmptyCell(Inventory.TabOfItem(item.Data.Id))) { SystemNotice?.Invoke(InventoryFullKey); return; }
        Inventory.RequestUnequip?.Invoke(item.Uid);
    }

    /// <summary>The equip bag slot of a binding name (EquipSlot, FUN_14120d560): its item (vfunc_25), that item's equip slot
    /// (CItem m_68, the record's slot) and the bag's kind (m_nKind); null for another name or an empty slot.</summary>
    public (Item Item, int Slot, int Kind)? WornSlot(string name)
    {
        if (equipBagRecord == null || Model == null || !name.StartsWith(EquipBag + ".", StringComparison.Ordinal)
            || !int.TryParse(name.AsSpan(EquipBag.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)) return null;
        var item = EquipSlotItem(equipBagRecord, equipBagKind, slot);
        return item == null ? null : (item, (int)(item.Record?.Slot ?? 0), equipBagKind);
    }

    // ---- the drag slots (EquipSlot, GemSlot), through NotifyDragSlot -----------------------------------------------

    /// <summary>A slot of the shown equip bag (an EquipSlot).</summary>
    public bool IsEquipSlot(string name) => GetBagSlotBindings(EquipBag).Contains(name);

    /// <summary>The equip bag's slots (EquipSlot) and the gem bag's (GemSlot).</summary>
    public bool IsDragSlot(string name) => IsEquipSlot(name) || GetBagSlotBindings(GemBag).Contains(name);

    /// <summary>DragSlot::vfunc_10: the slot pushed again.</summary>
    public void RefreshSlot(string name) { if (IsDragSlot(name)) Host.Push(name); }

    /// <summary>
    /// The destination's vfunc_18. An equip slot: <see cref="DroppedOnEquipSlot"/>; a gem slot: <see cref="DroppedOnGemSlot"/>.
    /// Both end with the source's vfunc_10 (pushed again: nothing is worn until the server's equip packets say so), except a
    /// gem slot without a local player. Their sounds (vfunc_20 / vfunc_21) are not here.
    /// </summary>
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner)
    {
        if (IsEquipSlot(name)) DroppedOnEquipSlot(int.Parse(name.AsSpan(EquipBag.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture), source, sourceOwner);
        else if (GetBagSlotBindings(GemBag).Contains(name))
        {
            if (!DroppedOnGemSlot(int.Parse(name.AsSpan(GemBag.Length + 1), NumberStyles.Integer, CultureInfo.InvariantCulture), source, sourceOwner)) return;
        }
        sourceOwner.RefreshSlot(source);
    }

    /// <summary>
    /// EquipSlot::vfunc_18 (equipslot.cpp:151). A bag slot (InvenSlot, <see cref="IInvenSlotOwner"/>): FindItemByUid of its
    /// item uid; found and <see cref="CanPutOn"/>: RequestEquipItem(item, m_nSlot, m_nKind, 0). Another equip slot
    /// (FUN_14120d560): its item (vfunc_25) and this slot's; when <see cref="CanSwapEquippedItems"/> over this bag's record
    /// (m_spPC): the swap request (ItemEquip mode 2: the dragged item's uid, this slot). Any other source: nothing.
    /// </summary>
    void DroppedOnEquipSlot(int slot, string source, IDragSlotOwner sourceOwner)
    {
        var r = equipBagRecord;
        if (r == null || Model == null) return;
        if (sourceOwner is IInvenSlotOwner bag && bag.InvenSlotUid(source) is long uid)
        {
            if (bag.FindItem(uid) is { } item && CanPutOn(item, slot, bag)) bag.RequestEquipItem(item, slot, equipBagKind);
        }
        else if (sourceOwner is IEquipSlotOwner equips && equips.IsEquipSlot(source))
        {
            var from = equips.WornSlot(source)?.Item;
            var mine = EquipSlotItem(r, equipBagKind, slot);
            if (CanSwapEquippedItems(from, mine, r)) Inventory?.RequestSwapEquip?.Invoke(from.Uid, (byte)slot);
        }
    }

    /// <summary>
    /// EquipSlot::FUN_14120d2e0 (equipslot.cpp:358): not the local record (FUN_14120d1f0: m_spPC and PCInfo m_3c0): false. Kind
    /// 1 or 2 and the item has no skin type (CItem::HasSkin, item.cpp:850): kind 2 box s_item_err_Invalid_slot_codi, else box
    /// 0xd3 (211); false. The item's model (GetModelOrEmpty) without this slot's group type: box 0xd3; false. Then
    /// CanEquipItem(item, m_nSlot, m_nKind) (<see cref="IInvenSlotOwner.CanEquipItem"/>).
    /// </summary>
    bool CanPutOn(Item item, int slot, IInvenSlotOwner bag)
    {
        if (equipBagRecord is not { IsLocal: true }) return false;
        if (equipBagKind is 1 or 2 && !item.Data.HasSkinType)
        {
            ShowCommonBox(equipBagKind == 2 ? InventoryController.InvalidCodiSlotKey : InventoryController.InvalidSlotKey);
            return false;
        }
        if (Model.Tables.ItemModels.Get(item.Data.ItemPreset)?.HasSlotType(slot) != true)
        {
            ShowCommonBox(InventoryController.InvalidSlotKey);
            return false;
        }
        return bag.CanEquipItem(item, slot, equipBagKind);
    }

    /// <summary>The box GetStringCommon(0xd5) (commonstringtable.cpp:1830) shows.</summary>
    public const string SwapHandsKey = "s_item_err_cash_putoff";

    /// <summary>
    /// CanSwapEquippedItems @0x141421230 (unit_1414210f0.cpp:49): no first item, or one that is not equip subtype 0x1f / 0x22
    /// (CItemData::FUN_14053d510, itemdata.cpp:925): false. The record's kind-1 items (GetEquipItemSet1, m_mapEquip1: the
    /// skin layer, PCInfo::GetEquipItemByKind kind 1; exe 0x141421318 / 0x141421339 call 0x140dba790) at slots 4 and 5:
    /// both: each, and the second item when there is one, must be of those subtypes; neither: the second item, when there is
    /// one, must be; only one: box 0xd5; false. Else true.
    /// </summary>
    bool CanSwapEquippedItems(Item first, Item second, PcInfo r)
    {
        static bool Swappable(Item i) => i.Data.EquipSubtype is 0x1f or 0x22;
        if (first == null || !Swappable(first)) return false;
        var four = EquipItemByKind(r, 1, 4);
        var five = EquipItemByKind(r, 1, 5);
        if (four != null && five != null)
        {
            if (!Swappable(four) || !Swappable(five)) return false;
            if (second != null && !Swappable(second)) return false;
        }
        else if (four == null && five == null)
        {
            if (second != null && !Swappable(second)) return false;
        }
        else
        {
            ShowCommonBox(SwapHandsKey);
            return false;
        }
        return true;
    }

    /// <summary>
    /// GemSlot::vfunc_18 (gemslot.cpp:117). The local record (m_spPC, FUN_140dba1c0): no local player: false (no vfunc_10);
    /// a bag slot: FindItemByUid of its item uid; FUN_14126d880 (:194: a gem item, type 7, the local record) and its gem type
    /// (GetGem()->vfunc_1()) this slot's: the worn gem of that type, from the local actor's PCGemEquip
    /// (<see cref="LocalGems"/>, GetLocalPlayer()->GetGemEquip()->GetItemByGemType, :134), gives the slot (its GetSlot,
    /// else -1) and <see cref="EquipGem"/>(item, slot, 0). Else nothing. True: the caller pushes the source again.
    /// </summary>
    bool DroppedOnGemSlot(int index, string source, IDragSlotOwner sourceOwner)
    {
        var r = gemBagRecord;
        if (r == null || !r.IsLocal || (uint)index >= GemTypes.Length) return true;
        if (Model == null || !Model.HasLocalPlayer) return false;
        if (sourceOwner is not IInvenSlotOwner bag || bag.InvenSlotUid(source) is not long uid) return true;
        var item = bag.FindItem(uid);
        if (item == null || item.Data.Type != 7) return true;
        int type = GemTypes[index];
        if ((item.Record?.GemType ?? 0) != type) return true;
        int slot = (LocalGems != null ? LocalGems() : r.Gems)?.FirstOrDefault(g => g.GemType == type) is { } worn ? worn.BadgeSlot : -1;
        EquipGem(item, slot, false);
        return true;
    }

    /// <summary>The local actor's live PCGemEquip (MyPC::GetGemEquip), which OnBadgeEquip (inbound 0x7f) keeps current;
    /// unset: the record's gems.</summary>
    public Func<IReadOnlyList<EquipItem>> LocalGems;

    /// <summary>
    /// OnBadgeEquip mode 4 (scenestategame.cpp:11075): the i16 error's system notice (FUN_141275260(id, 1, 1)):
    /// 0 s_gem_error_inventory_full (0xaa9), 1 s_gem_error_InvalidSlot (0xaaa), 2 s_gem_error_InvalidItem (0xaab),
    /// 3 s_gem_error_Expired (0xaac), 4 s_gem_error_PCBang (0xaad), 5 with feature 0x4a s_gem_error_VIP
    /// (GetStringCommonByKey), any other s_gem_error_unknown (0xaae) (commonstringtable.cpp:4337-4342).
    /// </summary>
    public static string GemEquipErrorKey(int error, bool vipFeature) => error switch
    {
        0 => "s_gem_error_inventory_full",
        1 => "s_gem_error_InvalidSlot",
        2 => "s_gem_error_InvalidItem",
        3 => "s_gem_error_Expired",
        4 => "s_gem_error_PCBang",
        5 when vipFeature => "s_gem_error_VIP",
        _ => "s_gem_error_unknown",
    };

    /// <summary>PCBase::IsDead of the local player (the live MyCharacter.Dead); unset: false.</summary>
    public Func<bool> LocalPlayerDead = () => false;

    /// <summary>The gem bind box's tag (s_useBindGemEquipItem, gemslot_2.cpp:31), which HandlePlainTag answers
    /// (uimessagebox.cpp:1062).</summary>
    public const string BindOnGemEquipBox = "useBindGemEquipItem";

    /// <summary>The box GetStringCommon(0x42) (commonstringtable.cpp:1683) shows.</summary>
    public const string DeadKey = "s_err_unable";

    /// <summary>
    /// EquipGem @0x141271960 (gemslot_2.cpp:441): no item or no local player: false. Dead: box 0x42; false. Unless
    /// <paramref name="skipBinding"/>, an item that binds on equip and is not bound (HasBindingRule, no HasBinding) asks first:
    /// box 17 with s_msg_transfer_bind_when_equip and the player's name (vfunc_77), tag <see cref="BindOnGemEquipBox"/>, the
    /// slot appended (Append&lt;int&gt;), the item uid and entry (FUN_140527b10 the name, 1, 0, -1, -1, 1), buttons 1143 / 1144,
    /// shown; true. Else the gem equip request (BadgeEquip mode 0: the uid, the slot as an unsigned char); true. Nothing is
    /// put on here: the server's badge packets do.
    /// STILL-OPEN: the item's material sound (CEffectService::FUN_1411a5110, "ms2sound/sound/item").
    /// </summary>
    public bool EquipGem(Item item, int slot, bool skipBinding)
    {
        if (item == null || Model == null || !Model.HasLocalPlayer) return false;
        if (LocalPlayerDead()) { ShowCommonBox(DeadKey); return false; }
        if (!skipBinding && InventoryController.NeedsBindConfirm(item))
        {
            string text = Ms2.Quest.ClientFormat.Format(Model.Tables.Common(InventoryController.BindOnEquipKey) ?? "", Pc.Name);
            var box = MessageBoxQueue.Create(17, text, null, BindOnGemEquipBox);
            box.Append(slot);
            box.ItemUid = item.Uid;
            box.SetItemEntry(item, new ItemTooltip(Model, 0).Name(item) ?? "", 1, 0, -1, -1, 1, Model.Tables.Items.TextColor);
            box.SetButtonLabelIds(1143, 1144, 0);
            ShowBox(box);
            return true;
        }
        Inventory?.RequestEquipGem?.Invoke(item.Uid, (byte)slot);
        return true;
    }

    /// <summary>HandleUseBindGemEquipItemButton @0x1419fe110 (uimessagebox.cpp:1907), the yes: FindItemByUid of the box's item
    /// uid, then EquipGem(item, arg 0, 1). False when the inventory no longer has the item (the handler's 0).</summary>
    public bool ConfirmBindGemEquip(long uid, int slot)
    {
        if (Inventory?.FindByUid(uid) is not { } e || Model == null) return false;
        var item = InventoryController.ToItem(e, Model.Tables.Items);
        if (item == null) return false;
        EquipGem(item, slot, true);
        return true;
    }

    void ShowCommonBox(string key) => ShowBox(MessageBoxQueue.Create(0, Model.Tables.Common(key) ?? "", null, ""));

    /// <summary>
    /// The open FUN_1422aeed0(2) branch picks vfunc_12 or vfunc_17; for both slot kinds both are vfunc_10
    /// (DragSlot::vfunc_12, EquipSlot::vfunc_17 equipslot.cpp:145, GemSlot::vfunc_17 gemslot.cpp:111): the slot pushed again.
    /// </summary>
    public void SlotDropUnresolved(string name) => RefreshSlot(name);

    /// <summary>ASDataProvider: a scaleform.clik.data.DataProvider with the values pushed.</summary>
    object DataProvider(IEnumerable<object> values)
    {
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        foreach (var v in values) Host.Invoke(dp, "push", v);
        return dp;
    }

    /// <summary>StringFormat: "{n}" replaced by the n-th argument.</summary>
    static string Format(string f, params object[] args)
    {
        for (int i = 0; i < args.Length; i++) f = f.Replace("{" + i + "}", Convert.ToString(args[i], CultureInfo.InvariantCulture));
        return f;
    }
}
