using System.Xml.Linq;
using Godot;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The game around the options dialog (UIGameOptionDialog's calls outside its movie): the option store, the window, the
/// sound volumes, the strings and the message boxes. The movie calls go to the loaded <see cref="GameOptionController"/>.
/// </summary>
public sealed class GameOptionHost : IGameOptionDialogHost
{
    readonly Node owner;
    readonly PackSet packs;
    readonly Func<string, string> common;
    readonly GraphicOptionTable table;
    XDocument defaults;
    bool defaultsRead;

    /// <summary>The loaded movie, null while the dialog is closed.</summary>
    public GameOptionController Movie;
    public GameOptionDialog Dialog;

    public GameOptionHost(Node owner, PackSet packs, Func<string, string> common)
    {
        this.owner = owner;
        this.packs = packs;
        this.common = common;
        table = packs.Exists("Xml", "table/graphicoption.xml")
            ? GraphicOptionTable.Parse(XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/graphicoption.xml")).TrimStart('﻿')))
            : new GraphicOptionTable();
    }

    // ---- the movie --------------------------------------------------------------------------------------------------

    public IOptionTab Tab => (IOptionTab)Movie ?? NoTab.Instance;
    public void Invoke(string method, params object[] args) => Movie?.Invoke(method, args);
    public string InvokeString(string method) => Movie?.InvokeString(method) ?? "";
    public void SetApplyEnabled(bool enabled) => Movie?.SetApplyEnabled(enabled);
    public void CloseDialog() => Movie?.CloseMovie();
    /// <summary>UIDialog::vfunc_55: closed and unloaded (no movie counts as closed).</summary>
    public bool Vfunc55 => Movie == null || Movie.IsClosed;

    // ---- strings ----------------------------------------------------------------------------------------------------

    /// <summary>CommonString's numeric ids used by the dialog and its pages (commonstringtable.cpp).</summary>
    static readonly Dictionary<int, string> CommonKeys = new()
    {
        [0x30f] = "s_option_resolution",              // 783
        [0x310] = "s_option_resolution_wide",         // 784
        [0x476] = "s_word_cancel",                    // 1142
        [0x847] = "s_gameoption_init",                // 2119
        [0x848] = "s_gameoption_init_all",            // 2120
        [0x849] = "s_gameoption_init_current",        // 2121
        [0x84a] = "s_gameoption_quickslot_warning",   // 2122
        [0x84b] = "s_gameoption_modified_warning",    // 2123
        [0x84d] = "s_gameoption_quality_warning",     // 2125
        [0x84e] = "s_gameoption_restart_warning",     // 2126
        [0x852] = "s_gameoption_confirm_reset_layout", // 2130
    };

    public string Common(int id)
    {
        if (CommonKeys.TryGetValue(id, out var key)) return common(key) ?? "";
        GD.PushWarning($"options: common string 0x{id:x} has no key here");
        return "";
    }

    public string CommonByKey(string key) => common(key) ?? "";
    public string FormatStringId(int id, params object[] args) => Ms2.Quest.ClientFormat.Format(Common(id), args);

    // ---- message boxes (stand-ins: the client's message box movie is not hosted yet) -------------------------------

    public void MessageBox(int stringId, string tag) =>
        Box(Common(stringId), Common(0x476), answer => Dialog?.OnMessage(tag, answer), null);

    public void Notice(int stringId) => Box(Common(stringId), null, null, null);

    public void ResetBox(string title, string allPages, string currentPage, string cancel) =>
        Box(title, cancel, answer => Dialog?.OnMessage("", answer), (allPages, currentPage));

    /// <summary>A Godot box in place of the client's: OK answers 0, the extra button 1, Cancel 2 (the reset box) or 1 (a
    /// yes/no box).</summary>
    void Box(string text, string cancel, Action<int> answer, (string First, string Second)? three)
    {
        GD.Print("options: box: " + text);
        var box = cancel == null ? new AcceptDialog() : new ConfirmationDialog();
        box.DialogText = three == null ? text : "";
        box.Title = three == null ? "" : text;
        box.Exclusive = true;
        bool answered = false;
        void Answer(int n)
        {
            if (answered) return;
            answered = true;
            answer?.Invoke(n);
            box.QueueFree();
        }
        if (three is var (first, second))
        {
            box.OkButtonText = first;
            box.AddButton(second, true, "second");
            box.CustomAction += a => { if (a == "second") { box.Hide(); Answer(1); } };
        }
        if (box is ConfirmationDialog cd)
        {
            cd.CancelButtonText = cancel;
            cd.Canceled += () => Answer(three == null ? 1 : 2);
        }
        box.Confirmed += () => Answer(0);
        box.CloseRequested += () => Answer(-1);
        owner.AddChild(box);
        box.PopupCentered();
    }

    public bool MessageBoxFree => true;

    // ---- the client's state ----------------------------------------------------------------------------------------

    /// <summary>CFeatureManager by id (the feature table's names).</summary>
    public bool IsFeatureEnabled(int feature) => feature >= 1 && feature <= Ms2.Ui.FeatureTable.Names.Length
                                                 && Ms2.Ui.LobbyFeatures.IsEnabledByName(packs, Ms2.Ui.FeatureTable.Names[feature - 1]);

    /// <summary>DAT_143ab2914, the account's GM level (asitemslot.cpp:1518, fishingmanager.cpp:215): 0 for a player, so
    /// initData's second argument is 0 (no account page, no debug input category).</summary>
    public int BuildNumber => 0;

    /// <summary>FUN_140651390 (localepath.cpp:309), the locale code.</summary>
    public string Locale => Ms2.Ui.LobbyFeatures.LocaleName;

    /// <summary>GFxSceneService::GetSceneId: the field scene, none of the simple-mode scenes (0x620000d, 0x6200009,
    /// 0x6200085).</summary>
    public int SceneId => 0;

    /// <summary>CGuideManager::IsRestricted: no guide restriction offline.</summary>
    public bool GuideRestricted(int n) => false;

    /// <summary>FUN_141944250's test: the key settings are not changed here, so the default key table's quick-slot keys
    /// stand.</summary>
    public bool QuickSlotsBound => true;

    public void SaveGameSettings() => GameOptions.Save();
    public void SaveCharacterSettings() => GameOptions.SaveCharacterSettings();
    public void SendBuddyMode20() { }                         // no server offline
    public void OpenSecondaryPassword() => GD.Print("options: the secondary password dialog is not available");
    public bool AgreementRedCircle => false;

    public GameOption Options => GameOptions.Current;
    public ScreenModes Modes => ScreenMode.Modes;
    public string AdapterName => RenderingServer.GetVideoAdapterName();

    /// <summary>CGameOption::GetBenchmarkLevel (gameoption.cpp:1943): video memory of 0x800 MB or less is level 0; the
    /// rest needs the adapter's video memory and vendor, which are not read here, so 0.</summary>
    public int BenchmarkLevel => 0;

    public bool IsHdrSupported(int level) => table.IsHdrSupported(level);
    public bool IsShimmerSupported(int level) => table.IsShimmerSupported(level);

    public XDocument DefaultGameDocument
    {
        get
        {
            if (defaultsRead) return defaults;
            defaultsRead = true;
            const string entry = "table/default/defaultgameoption.xml";
            if (packs.Exists("Xml", entry))
                defaults = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'));
            return defaults;
        }
    }

    public (int Width, int Height) ScreenSize
    {
        get { var s = DisplayServer.ScreenGetSize(); return (s.X, s.Y); }
    }

    public void ApplyWindow(WindowRequest request) => ScreenMode.ApplyRequest(request);

    /// <summary>SetQuality(state, quality, 0) @0x14125e1f0: the quality (the mirror flag m_b1f is not kept here).</summary>
    public void SetQuality(GameOption.Graphic state, int quality) => state.Quality = quality;

    /// <summary>SetHdr @0x14125e300: the switch (the soft particles and the light scale are the renderer's, not ported).</summary>
    public void SetHdr(GameOption.Graphic state, bool on) => state.DisplayHdr = on;

    /// <summary>SetShimmer @0x14125e3b0: the switch (post-process click 6 is not ported).</summary>
    public void SetShimmer(GameOption.Graphic state, bool on) => state.DisplayShimmer = on;

    public void SetEffectStep(int step) { }                   // CEffectService: not ported
    public void ShowFrameRate(bool shown) => GD.Print($"options: frame rate dialog {(shown ? "on" : "off")} (dialog 0x4e7d is not hosted)");
    public void EnchantEffectChanged(bool on) { }             // WorldService: not ported

    public SoundFlag SoundFlag => GameOptions.SoundFlag;
    public bool InstrumentPlaying => false;
    public bool AppActive => true;
    public void ApplySoundVolumes(SoundVolumes volumes) => FieldAudio.Instance?.ApplyVolumes(volumes);
    public void UpdateInstrumentVolume() { }

    // ---- the Game pages --------------------------------------------------------------------------------------------

    public GameOption.AccountCommunity AccountCommunity
    {
        get => GameOptions.AccountCommunity;
        set => GameOptions.AccountCommunity = value;
    }

    XDocument defaultAccount;
    bool defaultAccountRead;

    public XDocument DefaultAccountDocument
    {
        get
        {
            if (defaultAccountRead) return defaultAccount;
            defaultAccountRead = true;
            const string entry = "table/default/defaultaccountoption.xml";
            if (packs.Exists("Xml", entry))
                defaultAccount = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('\ufeff'));
            return defaultAccount;
        }
    }

    public GameOption.CharacterFlag MouseControlFlag => GameOptions.MouseControl;
    public GameOption.CharacterFlag LearningQuestFlag => GameOptions.LearningQuest;

    /// <summary>CMouseController::HasMode: the mouse camera mode is not ported, so the keyboard camera's fov step.</summary>
    public bool MouseCameraMode => false;

    public void PageMessageBox(int stringId, string tag, OptionPage listener) =>
        Box(Common(stringId), Common(0x476), answer => listener.OnMessage(tag, answer), null);

    public void ApplyCamera(GameOption.Camera camera) => GD.Print("options: camera option stored (CameraMgr and BGTransparencyMgr are not ported)");
    public void ApplyInterface(GameOption.Interface state) => GD.Print("options: interface stored (the damage meters and the damage effect are not hosted)");
    public void SetMouseControl(bool on) => GD.Print($"options: mouse control {(on ? "on" : "off")} (CMouseController is not ported)");
    public void ResetUiLayout() => GD.Print("options: UI layout reset (UISwfEntityManager::ResetLayout is not ported)");
    public void UiScaleChanged(float scale) { }   // the UI scale is read from the option at layout (UiScale)
    public void ApplyStatus(GameOption.Status state) => GD.Print("options: status stored (the hp and exp bars read it when hosted)");
    public void SendRideMode(bool disableSingleRiding) { }   // no server offline

    // ---- the Language page ------------------------------------------------------------------------------------------

    public int Region => GameOptions.RegionIndex;

    GameOptionLocale locale;

    /// <summary>GetLocaleData(0): not in debug mode (IsDebugMode is the GM build's), so the region's row.</summary>
    public GameOptionLocale.Row LocaleData
    {
        get
        {
            if (locale == null)
            {
                const string entry = "table/gameoptionlocale.xml";
                locale = packs.Exists("Xml", entry)
                    ? GameOptionLocale.Parse(XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('\ufeff')))
                    : new GameOptionLocale();
            }
            return locale.GetLocaleData(Region, false);
        }
    }

    public int RunningVoice => GameOptions.Voice;
    public bool FileExists(string path) => System.IO.File.Exists(path);
    public void TextNotice(string text) => Box(text, null, null, null);

    /// <summary>A tab with no controls (no movie loaded): reads null, ignores writes.</summary>
    sealed class NoTab : IOptionTab
    {
        public static readonly NoTab Instance = new();
        public bool? IsSelected(string n) => null;
        public void SetSelected(string n, bool v) { }
        public float? GetValue(string n) => null;
        public void SetValue(string n, float v) { }
        public int? GetSelectedIndex(string n) => null;
        public void SetSelectedIndex(string n, int v) { }
        public string GetText(string n) => null;
        public void SetText(string n, string v) { }
        public void SetEnabled(string n, bool v) { }
        public bool SetItemRows(string n, IReadOnlyList<(string Label, int Index)> r) => false;
        public void SetMenuRowCount(string n, int r) { }
        public int? GetSelectedItemIndex(string n) => null;
        public bool SetClipShown(string n, bool v) => false;
        public bool SetResolutionRows(string n, IReadOnlyList<ResolutionRow> r, int s) => false;
        public int GetResolution(int t) => -1;
    }
}
