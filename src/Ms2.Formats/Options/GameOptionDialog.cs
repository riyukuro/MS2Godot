using System;
using System.Collections.Generic;
using System.Linq;

namespace Ms2.Formats.Options;

// The C++ side of the client's options dialog (UIGameOptionDialog, client/ms2/uigameoptiondialog.cpp; the pages
// CBaseOption, client/ms2/baseoption.cpp, CGameOptionGraphicDefault, client/ms2/gameoption.cpp, CGameOptionSoundDefault,
// client/ms2/gameoptionstruct.cpp), without a UI: the movie's controls and the dialog's own calls go through
// IOptionTab and IGameOptionDialogHost. Method names follow the client's so the options movie binds to them.

/// <summary>The tab's controls as the pages reach them: Unk_GpDialog::GetTabCheckBox / GetTabSlider / GetTabRadioButton /
/// GetTabTextField / GetTabTextInput and the dropdown getter FUN_140cfcf10, all "getTabChild"(name, -1) on the movie. A
/// control the tab does not have reads as null and ignores writes, as the client skips a null pointer. A child of a clip
/// is named "clip.child" (m_playInstrumentStep's m_slider and m_isOffBgm).</summary>
public interface IOptionTab
{
    bool? IsSelected(string name);
    void SetSelected(string name, bool selected);
    float? GetValue(string name);
    void SetValue(string name, float value);
    int? GetSelectedIndex(string name);
    void SetSelectedIndex(string name, int index);
    string GetText(string name);
    void SetText(string name, string text);
    /// <summary>ASMovieClip::vfunc_3 on a clip found by getTabChild (show 1 / hide 0); false when there is no clip.</summary>
    bool SetClipShown(string name, bool shown);
    /// <summary>The resolution dropdown (FillResolutionCombo @0x14124d080): ClearItems, then SetScrollBar("export_scroll")
    /// for more than 6 rows or SetMenuRowCount(rows), AddItemRow({modeid, label}) per row, SetSelectedIndex, vfunc_24(true).
    /// False when the tab has no m_resolution.</summary>
    bool SetResolutionRows(string name, IReadOnlyList<ResolutionRow> rows, int selected);
    /// <summary>GetTabComponent(type)->Invoke("getResolution") (@0x14124bf20): the selected row's modeid, -1 for none.</summary>
    int GetResolution(int tabType);
}

/// <summary>What the dialog and its pages call outside the movie's tab.</summary>
public interface IGameOptionDialogHost
{
    IOptionTab Tab { get; }
    /// <summary>The movie root's Invoke (m_30->Invoke): "initData", "setTabComponent", "enableAgreementTabButtonRedCircle".</summary>
    void Invoke(string method, params object[] args);
    /// <summary>A root Invoke read as text (FUN_141277dd0): "getCurrentTabName", "getCurrentCategoryName".</summary>
    string InvokeString(string method);
    /// <summary>FUN_140d73bc0(id, 1, this, tag): a yes/no box of a common string; the answer comes back through
    /// <see cref="GameOptionDialog.OnMessage"/> with the tag.</summary>
    void MessageBox(int stringId, string tag);
    /// <summary>FUN_140d73bc0(id, 0, 0, ""): a notice box of a common string, no answer.</summary>
    void Notice(int stringId);
    /// <summary>CreateMessageBox(0x18) with three buttons, the title, and FUN_140d73c60 (@0x141943a30). The answer comes
    /// back through <see cref="GameOptionDialog.OnMessage"/> with an empty tag: 0 all pages, 1 this page, 2 cancel.</summary>
    void ResetBox(string title, string allPages, string currentPage, string cancel);
    /// <summary>GetStringCommon(id).</summary>
    string Common(int id);
    /// <summary>GetStringCommonByKey(key).</summary>
    string CommonByKey(string key);
    /// <summary>FormatStringId(id, args...): common string id formatted with the client's {n} / {n:spec} fields.</summary>
    string FormatStringId(int id, params object[] args);
    /// <summary>m_apply.SetEnabled (SetApplyEnabled @0x141943820).</summary>
    void SetApplyEnabled(bool enabled);
    /// <summary>UIDialog::vfunc_67: the dialog closes.</summary>
    void CloseDialog();
    /// <summary>UIDialog::vfunc_55, checked before the tab and key handlers (its meaning is not read here).</summary>
    bool Vfunc55 { get; }
    bool IsFeatureEnabled(int feature);
    /// <summary>DAT_143ab2914, compared with 0x32 for bNewBuild.</summary>
    int BuildNumber { get; }
    /// <summary>FUN_140651390, the locale passed to initData.</summary>
    string Locale { get; }
    /// <summary>GFxSceneService::GetSceneId.</summary>
    int SceneId { get; }
    /// <summary>CGuideManager::IsRestricted(n).</summary>
    bool GuideRestricted(int n);
    /// <summary>FUN_141944250's quick-slot test: true when every quick-slot action except 0x7a137..0x7a139 has a key.</summary>
    bool QuickSlotsBound { get; }
    /// <summary>CGameOption::SaveGameSettings.</summary>
    void SaveGameSettings();
    /// <summary>CGameOption::SaveCharacterSettings.</summary>
    void SaveCharacterSettings();
    /// <summary>CBuddyManager::SendBuddyMode20.</summary>
    void SendBuddyMode20();
    /// <summary>FUN_140d86260()->vfunc_24, the secondary password dialog.</summary>
    void OpenSecondaryPassword();
    /// <summary>The page id of m_bF1d0, the agreement tab's red circle (FUN_141943940).</summary>
    bool AgreementRedCircle { get; }

    // Graphic page applies (FUN_14124ce20 @0x14124ce20, vfunc_10 @0x14124d6a0)
    GameOption Options { get; }
    ScreenModes Modes { get; }
    /// <summary>The adapter name for m_devicename (GPUManager::GetAdapterName).</summary>
    string AdapterName { get; }
    /// <summary>CGameOption::GetBenchmarkLevel.</summary>
    int BenchmarkLevel { get; }
    /// <summary>CGraphicOptionTable::IsHdrSupported / IsShimmerSupported (graphicoptiontable.cpp:120-146): the level is at
    /// least the table's "hdr" / "shimmer" level (Table/graphicOption.xml), false when the table has none.</summary>
    bool IsHdrSupported(int level);
    bool IsShimmerSupported(int level);
    /// <summary>CGameOption::m_docGameDefault (FUN_1412442a0), DefaultGameOption.xml of the default option folder
    /// (GetOptionFilePath(0, true), gameoption.cpp:1693-1698); null when it could not be read.</summary>
    System.Xml.Linq.XDocument DefaultGameDocument { get; }
    /// <summary>GetSystemMetrics(SM_CXSCREEN / SM_CYSCREEN).</summary>
    (int Width, int Height) ScreenSize { get; }
    /// <summary>FUN_14151f6e0 (messageboxqueue.cpp:110): the message box layer takes a box.</summary>
    bool MessageBoxFree { get; }
    /// <summary>The window request of FUN_14125e910 (ScreenModes.Switch) to carry out.</summary>
    void ApplyWindow(WindowRequest request);
    /// <summary>SetQuality(state, quality, 0) on the page state (@0x14125e1f0).</summary>
    void SetQuality(GameOption.Graphic state, int quality);
    /// <summary>SetHdr (@0x14125e300).</summary>
    void SetHdr(GameOption.Graphic state, bool on);
    /// <summary>SetShimmer (@0x14125e3b0).</summary>
    void SetShimmer(GameOption.Graphic state, bool on);
    /// <summary>CEffectService::FUN_1411a8880(step).</summary>
    void SetEffectStep(int step);
    /// <summary>OpenDialog / CloseDialog of dialog 0x4e7d, the frame rate.</summary>
    void ShowFrameRate(bool shown);
    /// <summary>WorldService FUN_141bd41d0 (on) / FUN_141bd4260 (off).</summary>
    void EnchantEffectChanged(bool on);

    // Sound page applies (vfunc_10 @0x1412585a0)
    /// <summary>CGameOption::FUN_1411b1f30, the sound flag block (disableJobSound).</summary>
    SoundFlag SoundFlag { get; }
    /// <summary>The instrument manager has refs (FUN_14125fe20's bgm test).</summary>
    bool InstrumentPlaying { get; }
    /// <summary>g_bAppActive.</summary>
    bool AppActive { get; }
    /// <summary>The volumes of FUN_14125fe20 for CSoundEventMgr and the sound service.</summary>
    void ApplySoundVolumes(SoundVolumes volumes);
    /// <summary>CInstrumentManager::UpdateVolume(true).</summary>
    void UpdateInstrumentVolume();
}

/// <summary>Unk_SoundFlag: "sound"/"disableJobSound" (FUN_1412603d0 / FUN_141260410, gameoptionstruct.cpp:2823-2836).</summary>
public sealed class SoundFlag
{
    public bool DisableJobSound;
    public void Load(System.Xml.Linq.XDocument d) => GameOption.Xml.ReadBool(d, "sound", "disableJobSound", ref DisableJobSound);
    public void Save(System.Xml.Linq.XDocument d) => GameOption.Xml.WriteBool(d, "sound", "disableJobSound", DisableJobSound);
    public SoundFlag Clone() => (SoundFlag)MemberwiseClone();
}

/// <summary>One row of the resolution dropdown: the adapter mode index and the label (@0x14124d080).</summary>
public readonly record struct ResolutionRow(int ModeId, int Width, int Height, string Label);

/// <summary>The sound volumes FUN_14125fe20 (gameoptionstruct.cpp:2727-2752) sets: CSoundEventMgr::SetCategoryVolume for
/// categories 0..10, SetAllMute, CInstrumentManager::Mute, and the sound service's main volume.</summary>
public sealed record SoundVolumes(float[] Category, bool AllMute, bool MuteInstruments, float MainVolume)
{
    public static SoundVolumes From(GameOption.Sound p, bool instrumentPlaying, bool appActive)
    {
        float bgm = p.CategoryVolume(2);
        if (p.OffBgmWhilePlayInstrument && instrumentPlaying)
            bgm = 0f;
        var c = new float[11];
        c[0] = p.CategoryVolume(0);
        c[9] = p.CategoryVolume(0);
        c[1] = p.CategoryVolume(1);
        c[2] = bgm;
        c[3] = p.CategoryVolume(3);
        c[4] = p.CategoryVolume(4);
        c[7] = p.CategoryVolume(7);
        c[5] = p.CategoryVolume(5);
        c[8] = p.CategoryVolume(8);
        c[6] = p.CategoryVolume(6);
        c[10] = p.CategoryVolume(10);
        bool mute = p.MuteAll, muteInstruments = false;
        if (!appActive && !p.PlayAll)
        {
            mute = true;
            muteInstruments = true;
        }
        return new SoundVolumes(c, mute, muteInstruments, p.MainSoundStep * 10 / 100f);
    }
}

/// <summary>CBaseOption (baseoption.cpp, @0x140a02bc0..): a page of the dialog. Id is the page map key, Type the order
/// it was created in (m_nType, the movie's tab index for GetTabComponent).</summary>
public class OptionPage
{
    public readonly int Id, Type;
    public bool Active { get; private set; }
    public bool Pending { get; private set; }
    public bool Enabled { get; set; }
    protected GameOptionDialog Dialog { get; private set; }
    protected IGameOptionDialogHost Host => Dialog.Host;
    protected IOptionTab Tab => Dialog.Host.Tab;

    public OptionPage(int id, int type) { Id = id; Type = type; }
    internal void Attach(GameOptionDialog dialog) => Dialog = dialog;

    /// <summary>OnTabInit: the tab's movie is ready; a pending fill runs now.</summary>
    public virtual void vfunc_2()
    {
        Active = true;
        if (Pending)
        {
            vfunc_6();
            Pending = false;
        }
    }

    /// <summary>The page opens (n 1) or the dialog refreshes (n 0): fill when active, else on OnTabInit.</summary>
    public virtual void vfunc_3(int n)
    {
        if (Active) vfunc_6();
        else Pending = true;
    }

    /// <summary>The page closes: n 0 deactivates, else reads the controls back.</summary>
    public virtual void vfunc_4(int n)
    {
        if (n == 0) Active = false;
        else vfunc_7();
    }

    /// <summary>A movie call the dialog did not handle; nonzero when handled.</summary>
    public virtual int vfunc_5(string method, object[] args) => 0;
    /// <summary>Fill the controls from the page state.</summary>
    public virtual void vfunc_6() { }
    /// <summary>Read the controls into the page state.</summary>
    public virtual void vfunc_7() { }
    /// <summary>The page takes the key (UIGameOptionDialog::vfunc_13).</summary>
    public virtual int vfunc_8() => 0;
    /// <summary>The changes are dropped (FUN_141943d80).</summary>
    public virtual void vfunc_9() { }
    /// <summary>Save: read the controls when active, then the page applies.</summary>
    public virtual void vfunc_10()
    {
        if (Active) vfunc_7();
    }
    /// <summary>After SaveGameSettings.</summary>
    public virtual void vfunc_11() { }
    /// <summary>Reset to the defaults.</summary>
    public virtual void vfunc_12() { }
    public virtual void vfunc_13() { }
    /// <summary>The page state differs from the stored option.</summary>
    public virtual int vfunc_14() => 0;
}

/// <summary>UIGameOptionDialog (uigameoptiondialog.cpp).</summary>
public sealed class GameOptionDialog
{
    public readonly IGameOptionDialogHost Host;
    readonly SortedDictionary<int, OptionPage> pages = new();
    public int Tab { get; private set; } = 1;
    public bool Simple { get; private set; }
    int resetScope;   // m_nF1cc

    /// <summary>The ctor (@0x141941b80): the pages by key, typed in creation order. Pages the dialog does not port yet
    /// are plain CBaseOption pages.</summary>
    public GameOptionDialog(IGameOptionDialogHost host)
    {
        Host = host;
        int order = 1;
        void Add(int key, Func<int, int, OptionPage> make) => Add2(make(key, order++));
        void Add2(OptionPage p) { p.Attach(this); pages[p.Id] = p; }
        OptionPage Plain(int k, int t) => new(k, t);
        Add(1, Plain);                                   // CGameOptionCamera
        Add(2, Plain);                                   // CGameOptionInterface
        Add(3, Plain);                                   // CGameOptionStatus
        Add(4, Plain);                                   // CGameOptionBattle
        Add(5, Plain);                                   // CGameOptionCommunity
        Add(6, (k, t) => new GraphicOptionPage(k, t));
        Add(7, (k, t) => new SoundOptionPage(k, t));
        bool gamePad = host.IsFeatureEnabled(0xb5);
        if (gamePad) Add(8, Plain);                      // CGameOptionGamePad
        Add(9, Plain);                                   // CGameOptionInputDefault x5
        Add(10, Plain);
        Add(11, Plain);
        if (gamePad) Add(12, Plain);
        Add(13, Plain);
        Add(14, Plain);                                  // CGameOptionLanguage
        Add(16, Plain);                                  // CGameOptionAccount
        if (host.IsFeatureEnabled(0x117)) Add(15, Plain); // CGameOptionPersonalInfoAgreement
    }

    public IEnumerable<OptionPage> Pages => pages.Values;
    public OptionPage Page(int key) => pages.TryGetValue(key, out var p) ? p : null;

    /// <summary>@0x141944460: the n-th page in map order, counted from 1. The map has 17 bins and keys 1..16, so its order
    /// is the key order.</summary>
    public OptionPage GetPageByIndex(int index) => index >= 1 && index <= pages.Count ? pages.Values.ElementAt(index - 1) : null;

    /// <summary>@0x1419445c0.</summary>
    public void RemovePage(int key, int n)
    {
        if (n == 0) pages.Remove(key);
    }

    /// <summary>@0x1419428d0.</summary>
    public void OnLoadComplete()
    {
        int newBuild = Host.BuildNumber >= 0x32 ? 1 : 0;
        int gamePad = Host.IsFeatureEnabled(0xb5) ? 1 : 0;
        RemovePage(0x10, newBuild);   // FUN_141944590
        Host.Invoke("initData", Host.Locale, newBuild, gamePad);
        FUN_141943480();
        OpenTab(Tab);
    }

    /// <summary>@0x141943480: simple mode in scenes 0x620000d / 0x6200009 / 0x6200085 or under guide restriction 1, where
    /// only the pages of type 6, 7 and 0xe are enabled and the graphics tab opens.</summary>
    public void FUN_141943480()
    {
        int scene = Host.SceneId;
        Simple = scene == 0x620000d || scene == 0x6200009 || scene == 0x6200085 || Host.GuideRestricted(1);
        if (Simple) Tab = 6;
        foreach (var p in pages.Values)
        {
            p.Enabled = !Simple || p.Type == 6 || p.Type == 7 || p.Type == 0xe;
            p.vfunc_3(0);
        }
        Host.Invoke("setTabComponent", Tab, Simple, false);
        FUN_141943940();
    }

    /// <summary>@0x141943940.</summary>
    public void FUN_141943940()
    {
        if (pages.ContainsKey(0xf))
            Host.Invoke("enableAgreementTabButtonRedCircle", Host.AgreementRedCircle);
    }

    /// <summary>@0x1419436e0.</summary>
    public void OpenTab(int tab)
    {
        Tab = tab;
        if (Host.Vfunc55) return;
        GetPageByIndex(Tab)?.vfunc_3(1);
    }

    /// <summary>@0x141943790.</summary>
    public void OnCloseTab(int tab)
    {
        if (Host.Vfunc55) return;
        GetPageByIndex(tab)?.vfunc_4(1);
    }

    /// <summary>vfunc_28 @0x141942cc0: the movie's ExternalInterface calls, names compared case-insensitively. True when
    /// handled here (the client then returns 1; the rest go on to UIDialog::vfunc_28).</summary>
    public bool Call(string method, params object[] args)
    {
        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (Is(method, "OnCloseDialog")) { OnCloseDialog(); return true; }
        if (Is(method, "OnOpenTab")) { if (args.Length > 0) OpenTab(Convert.ToInt32(args[0])); return false; }
        if (Is(method, "OnCloseTab")) { OnCloseTab(Tab); return false; }
        if (Is(method, "OnUndo")) return true;
        if (Is(method, "OnInit")) { FUN_141943a30(); return true; }
        if (Is(method, "OnSave")) { OnSave(); return true; }
        if (Is(method, "OnTabInit")) { if (args.Length > 0) GetPageByIndex(Convert.ToInt32(args[0]))?.vfunc_2(); return false; }
        if (Is(method, "OnSecondaryPassword")) { Host.OpenSecondaryPassword(); return true; }
        var page = GetPageByIndex(Tab);
        return page != null && page.vfunc_5(method, args) != 0;
    }

    /// <summary>vfunc_13 @0x141942b10: a key while open. True when the page or the dialog took it; Esc closes.</summary>
    public bool OnKey(bool escape)
    {
        if (Host.Vfunc55) return false;
        var page = GetPageByIndex(Tab);
        if (page != null && page.vfunc_8() != 0) return true;
        if (!escape) return false;
        OnCloseDialog();
        return true;
    }

    /// <summary>@0x141943a30: the reset box. The category is the movie's, or s_gameoption_init_shoutcut on tabs 9..0xd.</summary>
    public void FUN_141943a30()
    {
        string tab = Host.InvokeString("getCurrentTabName");
        string category = Host.InvokeString("getCurrentCategoryName");
        if (Tab >= 9 && Tab <= 0xd)
            category = Host.CommonByKey("s_gameoption_init_shoutcut");
        Host.ResetBox(Host.Common(0x847), Host.Common(0x848), Host.FormatStringId(0x849, tab, category), Host.Common(0x476));
    }

    /// <summary>@0x141943e30: 0x84a is s_gameoption_quickslot_warning.</summary>
    public void OnSave()
    {
        if (!FUN_141944250()) Host.MessageBox(0x84a, "OnSave");
        else FUN_141943ec0();
    }

    /// <summary>@0x141943ec0: every page saves, the option file is written, then every page's vfunc_11.</summary>
    public void FUN_141943ec0()
    {
        foreach (var p in pages.Values.ToList()) p.vfunc_10();
        Host.SaveGameSettings();
        foreach (var p in pages.Values.ToList()) p.vfunc_11();
        Host.SendBuddyMode20();
    }

    /// <summary>@0x141943d80: every page drops its changes.</summary>
    public void FUN_141943d80()
    {
        foreach (var p in pages.Values.ToList()) p.vfunc_9();
    }

    /// <summary>@0x141944030: reset this page (n != 0) or all pages.</summary>
    public void FUN_141944030(int n)
    {
        resetScope = n;
        if (n != 0) GetPageByIndex(Tab)?.vfunc_12();
        else foreach (var p in pages.Values.ToList()) p.vfunc_12();
    }

    /// <summary>@0x141944150: 0x84b is s_gameoption_modified_warning.</summary>
    public void OnCloseDialog()
    {
        if (!IsNoPageChanged()) Host.MessageBox(0x84b, "OnSaveAndCheck");
        else if (!FUN_141944250()) Host.MessageBox(0x84a, "OnSaveAndCheck");
        else Close();
    }

    /// <summary>@0x141944250: simple mode skips the quick-slot test.</summary>
    public bool FUN_141944250() => Simple || Host.QuickSlotsBound;

    /// <summary>@0x141944330: the open page reads its controls back, then any page that differs counts.</summary>
    public bool IsNoPageChanged()
    {
        GetPageByIndex(Tab)?.vfunc_7();
        return !pages.Values.Any(p => p.vfunc_14() != 0);
    }

    /// <summary>OnMessage @0x141943010: a message box answer (0 yes / first button, 1 no / second, 2 cancel, -1 none).</summary>
    public void OnMessage(string tag, int answer)
    {
        if (tag == "OnSave")
        {
            if (answer == 0)
            {
                FUN_141943ec0();
                Host.SetApplyEnabled(false);
            }
        }
        else if (tag == "OnSaveAndCheck")
        {
            if (answer == 0) FUN_141943ec0();
            else FUN_141943d80();
            Close();
        }
        else if (tag == "OnClose")
        {
            if (answer == 0) Close();
        }
        else
        {
            if (answer == 0) FUN_141944030(0);
            else if (answer == 1) FUN_141944030(1);
            if (answer != 2 && answer != -1) Host.SetApplyEnabled(true);
        }
    }

    /// <summary>@0x141943820.</summary>
    public void SetApplyEnabled(bool enabled) => Host.SetApplyEnabled(enabled);

    /// <summary>vfunc_67 @0x141942a10: every page deactivates, the dialog closes, simple mode and the tab reset.</summary>
    public void Close()
    {
        foreach (var p in pages.Values.ToList()) p.vfunc_4(0);
        Host.CloseDialog();
        Simple = false;
        Tab = 1;
    }
}

/// <summary>CGameOptionGraphicDefault (gameoption.cpp). The page state is a copy of the graphic and optimizer options
/// (m_state, m_state2), stored back on save.</summary>
public sealed class GraphicOptionPage : OptionPage
{
    public GameOption.Graphic State = new();
    public GameOption.Optimizer State2 = new();
    bool filled;   // m_n74: onDisplayQuality is ignored until the controls are filled

    public GraphicOptionPage(int id, int type) : base(id, type) { }

    GameOption.Graphic Global => Host.Options.GraphicOption;
    GameOption.Optimizer Global2 => Host.Options.OptimizerOption;

    /// <summary>@0x14124abe0: n 0 copies the stored options into the page state.</summary>
    public override void vfunc_3(int n)
    {
        if (n == 0)
        {
            State = Global.Clone();
            State2 = Global2.Clone();
            filled = false;
        }
        base.vfunc_3(n);
    }

    /// <summary>@0x14124ac90: onDisplayQuality(level) from the quality slider. The hdr box, the shadow index and the
    /// effect step follow the level; a level above the benchmark warns once (0x84d s_gameoption_quality_warning).</summary>
    public override int vfunc_5(string method, object[] args)
    {
        if (!string.Equals(method, "onDisplayQuality", StringComparison.OrdinalIgnoreCase) || !filled || args.Length == 0)
            return 0;
        int level = Convert.ToInt32(args[0]);
        Tab.SetSelected("m_hdr", Host.IsHdrSupported(level));
        if (level >= 3) Tab.SetSelectedIndex("m_shadow", 2);
        else if (level >= 2) Tab.SetSelectedIndex("m_shadow", 1);
        else if (level >= 0) Tab.SetSelectedIndex("m_shadow", 0);
        Tab.SetValue("m_effectStep", Math.Max(level, 1));
        if (level > Host.BenchmarkLevel && level != State.Quality && Host.MessageBoxFree)
            Host.Notice(0x84d);
        return 0;
    }

    /// <summary>@0x14124aff0.</summary>
    public override void vfunc_6()
    {
        FillResolutionCombo();
        Tab.SetSelected("m_screenModeWindow", State.ScreenMode == 1);
        Tab.SetSelected("m_screenModeFullWindow", State.ScreenMode == 2);
        Tab.SetSelected("m_showFrameRate", State.ShowFrameRate);
        Tab.SetValue("m_effectStep", State.EffectStep);
        Tab.SetValue("m_quality", State.Quality);
        Tab.SetSelected("m_hdr", State.DisplayHdr);
        Tab.SetSelected("m_shimmer", State.DisplayShimmer);
        Tab.SetSelectedIndex("m_multisampling", State.MultisamplingIndex);
        Tab.SetSelected("m_vsync", State.Vsync);
        Tab.SetSelected("m_enchantEffect", State.ShowEnchantEffect);
        int shadow = State.ShadowLevel;
        if (shadow >= 3) Tab.SetSelectedIndex("m_shadow", 2);
        else if (shadow >= 2) Tab.SetSelectedIndex("m_shadow", 1);
        else if (shadow >= 0) Tab.SetSelectedIndex("m_shadow", 0);
        Tab.SetText("m_devicename", Host.AdapterName);
        Tab.SetSelectedIndex("m_optimalHide", State2.OptimalHide);
        Tab.SetSelected("m_hideAlways", State2.HideAlways);
        Tab.SetSelected("m_hideBossBattle", State2.HideBossBattle);
        Tab.SetSelected("m_showParty", State2.ShowParty);
        Tab.SetSelected("m_showGuild", State2.ShowGuild);
        Tab.SetSelected("m_showCustom", State2.ShowCustom);
        Tab.SetText("m_customInput", State2.CustomCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Tab.SetSelected("m_showPhysx", State2.ShowPhysx);
        filled = true;
    }

    /// <summary>@0x14124bf20.</summary>
    public override void vfunc_7()
    {
        int res = Tab.GetResolution(Type);
        if (res != -1)
        {
            State.ResolutionIndex = res;
            GetModeSize(res, ref State.Width, ref State.Height);
        }
        if (Tab.IsSelected("m_screenModeWindow") == true) State.ScreenMode = 1;
        if (Tab.IsSelected("m_screenModeFullWindow") == true) State.ScreenMode = 2;
        if (Tab.IsSelected("m_showFrameRate") is bool fr) State.ShowFrameRate = fr;
        if (Tab.GetValue("m_effectStep") is float es) State.EffectStep = (int)es;
        if (Tab.GetValue("m_quality") is float q) State.Quality = (int)q;
        if (Tab.IsSelected("m_hdr") is bool hdr) State.DisplayHdr = hdr;
        if (Tab.IsSelected("m_shimmer") is bool sh) State.DisplayShimmer = sh;
        if (Tab.GetSelectedIndex("m_shadow") is int sd)
        {
            if (sd == 0) State.ShadowLevel = 0;
            else if (sd == 1) State.ShadowLevel = 2;
            else if (sd == 2) State.ShadowLevel = 3;
        }
        if (Tab.IsSelected("m_vsync") is bool vs) State.Vsync = vs;
        if (Tab.IsSelected("m_enchantEffect") is bool en) State.ShowEnchantEffect = en;
        if (Tab.GetSelectedIndex("m_multisampling") is int ms) State.MultisamplingIndex = ms;
        if (Tab.GetSelectedIndex("m_optimalHide") is int oh) State2.OptimalHide = oh;
        if (Tab.IsSelected("m_hideAlways") is bool ha) State2.HideAlways = ha;
        if (Tab.IsSelected("m_hideBossBattle") is bool hb) State2.HideBossBattle = hb;
        if (Tab.IsSelected("m_showParty") is bool sp) State2.ShowParty = sp;
        if (Tab.IsSelected("m_showGuild") is bool sg) State2.ShowGuild = sg;
        if (Tab.IsSelected("m_showCustom") is bool sc) State2.ShowCustom = sc;
        if (Tab.GetText("m_customInput") is string ci) State2.CustomCount = Wtoi(ci);
        if (Tab.IsSelected("m_showPhysx") is bool ph) State2.ShowPhysx = ph;
    }

    /// <summary>FUN_1407181a0, _wtoi: leading blanks, a sign, then digits; 0 when there are none.</summary>
    public static int Wtoi(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9' && v <= int.MaxValue) v = v * 10 + (s[i++] - '0');
        if (v > int.MaxValue) return neg ? int.MinValue : int.MaxValue;
        return (int)(neg ? -v : v);
    }

    /// <summary>FUN_14124ce20 @0x14124ce20: carry out what changed against the stored option. A changed screen mode or row
    /// switches through FUN_14125e910 on the stored option and takes its size back. Multisampling and vsync only raise
    /// the notice 0x84e: their setters FUN_14125e4b0 / FUN_14125e5b0 have empty bodies (gameoptionstruct.cpp:2112,2136).</summary>
    public void FUN_14124ce20()
    {
        var g = Global;
        bool notify = false;
        if (State.ScreenMode != g.ScreenMode || State.ResolutionIndex != g.ResolutionIndex)
        {
            var req = Host.Modes.Switch(g, State.ScreenMode, State.ResolutionIndex, out _);
            if (req != null) Host.ApplyWindow(req.Value);
            State.Width = g.Width;
            State.Height = g.Height;
        }
        if (State.Quality != g.Quality) Host.SetQuality(State, State.Quality);
        if (State.DisplayHdr != g.DisplayHdr) Host.SetHdr(State, State.DisplayHdr);
        if (State.DisplayShimmer != g.DisplayShimmer) Host.SetShimmer(State, State.DisplayShimmer);
        if (State.EffectStep != g.EffectStep) Host.SetEffectStep(State.EffectStep);
        if (State.MultisamplingIndex != g.MultisamplingIndex) notify = true;
        if (State.Vsync != g.Vsync) notify = true;
        Host.ShowFrameRate(State.ShowFrameRate);
        if (notify) Host.Notice(0x84e);
    }

    /// <summary>@0x14124d6a0: apply, then the page state becomes the stored option (FUN_14125dff0 / FUN_14125ef60).
    /// FUN_14125ef90(&amp;m_state2) runs on the page copy after it is stored.</summary>
    public override void vfunc_10()
    {
        base.vfunc_10();
        bool oldEnchant = Global.ShowEnchantEffect;
        FUN_14124ce20();
        Host.Options.GraphicOption = State.Clone();
        Host.Options.OptimizerOption = State2.Clone();
        if (State.ShowEnchantEffect != oldEnchant)
            Host.EnchantEffectChanged(State.ShowEnchantEffect);
    }

    /// <summary>@0x14124d7a0: the defaults (FUN_14125d8c0, then LoadGameOptionGraphic of the default document in its
    /// default form; FUN_14125ec50, then LoadGameOptionOptimizer of the default document), then refill.</summary>
    public override void vfunc_12()
    {
        base.vfunc_12();
        var doc = Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument();
        var (w, h) = Host.ScreenSize;
        State = new GameOption.Graphic();
        State.LoadDefault(doc, Host.BenchmarkLevel, Host.IsHdrSupported, Host.IsShimmerSupported, Host.Modes, w, h);
        State2 = new GameOption.Optimizer();
        State2.Load(doc);
        vfunc_6();
    }

    /// <summary>@0x14124d890: either block differs from the stored option.</summary>
    public override int vfunc_14() => GameOption.SameFields(State, Global) && GameOption.SameFields(State2, Global2) ? 0 : 1;

    /// <summary>@0x14124d080.</summary>
    public void FillResolutionCombo()
    {
        var rows = ResolutionRows(Host.Modes, Host.FormatStringId);
        if (rows.Count == 0) return;
        int selected = 0;
        for (int j = 0; j < rows.Count; j++)
            if (rows[j].Width == State.Width && rows[j].Height == State.Height)
                selected = j;
        Tab.SetResolutionRows("m_resolution", rows, selected);
    }

    /// <summary>The rows of FillResolutionCombo: the usable modes (FUN_14125e7e0) by mode index, sorted by width then height
    /// (Unk_GoResModeLess @0x14124fdc0, a stable order is not promised by std::sort but equal sizes cannot occur in the
    /// unique mode list), labelled with 0x310 s_option_resolution_wide when the ratio is at least 1.59, else 0x30f
    /// s_option_resolution, both formatted with (width, height, ratio a, ratio b).</summary>
    public static List<ResolutionRow> ResolutionRows(ScreenModes modes, Func<int, object[], string> format)
    {
        var rows = new List<ResolutionRow>();
        for (int i = 0; i < modes.Modes.Count; i++)
            if (ScreenModes.Usable(modes.Modes[i]))
                rows.Add(new ResolutionRow(i, modes.Modes[i].Width, modes.Modes[i].Height, null));
        rows.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
        for (int j = 0; j < rows.Count; j++)
        {
            var (a, b) = GetAspectRatio(rows[j].Width, rows[j].Height);
            int id = (float)a / b >= 1.59f ? 0x310 : 0x30f;
            rows[j] = rows[j] with { Label = format(id, new object[] { rows[j].Width, rows[j].Height, a, b }) };
        }
        return rows;
    }

    /// <summary>@0x14124d5f0: the size of an adapter mode; unchanged when there is no such mode.</summary>
    void GetModeSize(int index, ref int width, ref int height)
    {
        var m = Host.Modes;
        if (index < 0 || index >= m.Modes.Count) return;
        width = m.Modes[index].Width;
        height = m.Modes[index].Height;
    }

    /// <summary>Gcd @0x14124d900.</summary>
    public static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int r = a % b;
            a = b;
            b = r;
        }
        return Math.Abs(a);
    }

    /// <summary>RoundHalfUp @0x14124d950: the integer part, plus one when the fraction is at least 0.5.</summary>
    public static float RoundHalfUp(float f)
    {
        float i = (int)f;
        return 0.5f <= f - i ? MathF.Ceiling(i + 1f) : i;
    }

    /// <summary>GetAspectRatio @0x14124d9d0 (gameoption.cpp:1499-1556): the reduced ratio; ratios in [1.6, 1.67) are
    /// scaled to a 16-wide form, other large ones are rounded to a tenth (under 1.57) or to x:9.</summary>
    public static (int A, int B) GetAspectRatio(int width, int height)
    {
        int g = Gcd(width, height);
        int a = width, b = height;
        if (1 < g)
        {
            a /= g;
            b /= g;
        }
        bool scale = 0x10 < a;
        if (!scale)
        {
            float r = (float)a / b;
            if (r < 1.6f || 1.67f <= r)
                scale = 5 < a;
            else
            {
                int mul = (int)(16.0f / a);
                a *= mul;
                b *= mul;
                if (a < 0x10 && (g = Gcd(a, b)) > 1)
                {
                    a /= g;
                    b /= g;
                }
            }
        }
        if (scale)
        {
            float r = (float)a / b;
            float scaled = r < 1.6f ? RoundHalfUp(r * 10.0f) : r * 10.0f;
            if (r < 1.57f)
            {
                int n = (int)scaled;
                float mul = (float)n / a;
                a = n;
                b = (int)RoundHalfUp(b * mul);
                float r2 = (float)a / b;
                if ((r2 < 1.6f || 1.67f <= r2) && (g = Gcd(a, b)) > 1)
                {
                    a /= g;
                    b /= g;
                }
            }
            else
            {
                float mul = 9.0f / b;
                a = (int)RoundHalfUp(a * mul);
                b = 9;
            }
        }
        return (a, b);
    }
}

/// <summary>CGameOptionSoundDefault (gameoptionstruct.cpp:440-680).</summary>
public sealed class SoundOptionPage : OptionPage
{
    public GameOption.Sound State = new();
    public SoundFlag Flag = new();

    public SoundOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x141256dd0.</summary>
    public override void vfunc_3(int n)
    {
        if (n == 0)
        {
            State = Host.Options.SoundOption.Clone();
            Flag = Host.SoundFlag.Clone();
        }
        base.vfunc_3(n);
    }

    static readonly (string Name, Func<GameOption.Sound, int> Get, Action<GameOption.Sound, int> Set)[] Steps =
    {
        ("m_mainSoundStep", s => s.MainSoundStep, (s, v) => s.MainSoundStep = v),
        ("m_effectSoundStep", s => s.EffectSoundStep, (s, v) => s.EffectSoundStep = v),
        ("m_enviromentSoundStep", s => s.EnviromentSoundStep, (s, v) => s.EnviromentSoundStep = v),
        ("m_uiSoundStep", s => s.UiSoundStep, (s, v) => s.UiSoundStep = v),
        ("m_npcSoundStep", s => s.NpcSoundStep, (s, v) => s.NpcSoundStep = v),
        ("m_bossSoundStep", s => s.BossSoundStep, (s, v) => s.BossSoundStep = v),
        ("m_bgmSoundStep", s => s.BgmSoundStep, (s, v) => s.BgmSoundStep = v),
        ("m_systemSoundStep", s => s.SystemSoundStep, (s, v) => s.SystemSoundStep = v),
        ("m_movieSoundStep", s => s.MovieSoundStep, (s, v) => s.MovieSoundStep = v),
    };

    /// <summary>@0x141256e90. The instrument row shows only with feature 10.</summary>
    public override void vfunc_6()
    {
        Tab.SetSelected("m_bMuteAll", State.MuteAll);
        Tab.SetSelected("m_bPlayAll", State.PlayAll);
        Tab.SetSelected("m_disableJobSound", Flag.DisableJobSound);
        foreach (var s in Steps) Tab.SetValue(s.Name, s.Get(State));
        if (Host.IsFeatureEnabled(10))
        {
            if (Tab.SetClipShown("m_playInstrumentStep", true))
            {
                Tab.SetValue("m_playInstrumentStep.m_slider", State.InstrumentSoundStep);
                Tab.SetSelected("m_playInstrumentStep.m_isOffBgm", State.OffBgmWhilePlayInstrument);
            }
        }
        else Tab.SetClipShown("m_playInstrumentStep", false);
        Tab.SetValue("m_charSoundStep", State.CharSoundStep);
    }

    /// <summary>@0x141257a90.</summary>
    public override void vfunc_7()
    {
        if (Tab.IsSelected("m_bMuteAll") is bool m) State.MuteAll = m;
        if (Tab.IsSelected("m_bPlayAll") is bool p) State.PlayAll = p;
        if (Tab.IsSelected("m_disableJobSound") is bool j) Flag.DisableJobSound = j;
        foreach (var s in Steps)
            if (Tab.GetValue(s.Name) is float v) s.Set(State, (int)v);
        if (Host.IsFeatureEnabled(10))
        {
            if (Tab.GetValue("m_playInstrumentStep.m_slider") is float iv) State.InstrumentSoundStep = (int)iv;
            if (Tab.IsSelected("m_playInstrumentStep.m_isOffBgm") is bool off) State.OffBgmWhilePlayInstrument = off;
        }
        if (Tab.GetValue("m_charSoundStep") is float c) State.CharSoundStep = (int)c;
    }

    /// <summary>@0x1412585a0: the page's volumes are set (FUN_14125fe20 on the page state), then stored, the instruments
    /// follow, and the character settings are written.</summary>
    public override void vfunc_10()
    {
        base.vfunc_10();
        Host.ApplySoundVolumes(SoundVolumes.From(State, Host.InstrumentPlaying, Host.AppActive));
        Host.Options.SoundOption = State.Clone();
        Host.SoundFlag.DisableJobSound = Flag.DisableJobSound;
        Host.UpdateInstrumentVolume();
        Host.SaveCharacterSettings();
    }

    /// <summary>@0x141258650: the defaults (FUN_14125f740 + LoadGameOptionSound of the default document, FUN_1412603b0 +
    /// FUN_1412603d0), then refill.</summary>
    public override void vfunc_12()
    {
        base.vfunc_12();
        var doc = Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument();
        State = new GameOption.Sound();
        State.Load(doc);
        Flag = new SoundFlag();
        Flag.Load(doc);
        vfunc_6();
    }

    /// <summary>@0x141258730: as the client has it, the page counts as changed only when both the state and the flag
    /// differ.</summary>
    public override int vfunc_14()
    {
        if (GameOption.SameFields(State, Host.Options.SoundOption)) return 0;
        if (Flag.DisableJobSound == Host.SoundFlag.DisableJobSound) return 0;
        return 1;
    }
}
