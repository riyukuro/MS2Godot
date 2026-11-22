using System.Xml.Linq;
using Godot;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using Ms2.Formats.Text;

namespace Ms2Godot.Ui;

/// <summary>
/// The game around the options dialog (UIGameOptionDialog's calls outside its movie): the option store, the window, the
/// sound volumes, the strings and the message boxes. The movie calls go to the loaded <see cref="GameOptionController"/>.
/// </summary>
public sealed class GameOptionHost : IGameOptionDialogHost
{
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
        this.packs = packs;
        this.common = common;
        table = packs.Exists("Xml", "table/graphicoption.xml")
            ? GraphicOptionTable.Parse(PackXml.Parse(packs.Read("Xml", "table/graphicoption.xml")))
            : new GraphicOptionTable();
    }

    // ---- the movie --------------------------------------------------------------------------------------------------

    public IOptionTab Tab => (IOptionTab)Movie ?? NoTab.Instance;
    public void Invoke(string method, params object[] args) => Movie?.Invoke(method, args);
    public string InvokeString(string method) => Movie?.InvokeString(method) ?? "";
    public void SetApplyEnabled(bool enabled) => Movie?.SetApplyEnabled(enabled);
    public void CloseDialog() => Movie?.CloseMovie();
    /// <summary>UIDialog::vfunc_55: closed and unloaded (no movie counts as closed).</summary>
    public bool IsClosed => Movie == null || Movie.IsClosed;

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

    // ---- message boxes: UIMessageBox through the message box queue (Ms2.Ui.Hud.MessageBoxQueue) ---------------------

    /// <summary>An IMessageListener: UIMsg 0's button and tag go to <paramref name="answer"/>; the listener's return is
    /// UIGameOptionDialog::OnMessage's 1 or CGameOptionInterface::OnMessage's 0 (@0x141255b50).</summary>
    sealed class Listener(Action<string, int> answer, bool handled) : IMessageBoxListener
    {
        public bool OnMessageBox(MessageBoxController sender, int button, string tag)
        {
            answer(tag, button);
            return handled;
        }
    }

    Listener dialogListener;
    Listener DialogListener => dialogListener ??= new Listener((tag, n) => Dialog?.OnMessage(tag, n), true);

    /// <summary>FUN_140d73bc0(id, 1, this, tag).</summary>
    public void MessageBox(int stringId, string tag) => MessageBoxQueue.ShowMessageBox(Common(stringId), 1, DialogListener, tag);

    /// <summary>FUN_140d73bc0(id, 0, 0, "").</summary>
    public void Notice(int stringId) => MessageBoxQueue.ShowMessageBox(Common(stringId), 0, null, "");

    /// <summary>FUN_141943a30: CreateMessageBox(0x18, "", this, ""), SetButtonLabels, SetTitle(0x847), FUN_140d73c60.</summary>
    public void ResetBox(string title, string allPages, string currentPage, string cancel)
    {
        var box = MessageBoxQueue.Create(0x18, "", DialogListener, "");
        box.SetButtonLabels(allPages, currentPage, cancel);
        box.Title = title ?? "";
        MessageBoxQueue.Show(box);
    }

    /// <summary>FUN_14151f6e0.</summary>
    public bool MessageBoxFree => MessageBoxQueue.IsIdle;

    /// <summary>FUN_14151f200.</summary>
    public void NextMessageBox() => MessageBoxQueue.Next();

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

    /// <summary>CGuideManager::IsRestricted (@0x14138c900): a running guide event holds the limit; none offline (no guide).</summary>
    public bool GuideRestricted(int n) => n >= 0 && n < Ms2.Ui.Guide.GuideManager.LimitCount
        && Ms2.Ui.Guide.GuideManager.Current?.IsRestricted((Ms2.Ui.Guide.GuideLimit)n) == true;

    public void SaveGameSettings() => GameOptions.Save();
    public void SaveCharacterSettings() => GameOptions.SaveCharacterSettings();
    public void SendBuddyMode20() { }                         // no server offline
    public void OpenSecondaryPassword() => GD.Print("options: the secondary password dialog is not available");

    public GameOption.GamePad GamePad => GameOptions.GamePad;
    public PersonalInfoAgreement Agreement => GameOptions.Agreement;

    /// <summary>SetGamepadEnabled (@0x14125f670): the block's switch, the HUD followers and the character file.</summary>
    public void SetGamepadEnabled(GameOption.GamePad stored, bool use, bool notify) => GameOptions.SetGamepadEnabled(stored, use, notify);

    /// <summary>GetUGCMapInfo() != 0: the local home's map info is known (set by ResponseCube mode 0x14, <see cref="Net.LiveSession.Home"/>).
    /// Without it MyPC::SendHouseSetting returns at once, as the client does.</summary>
    public Func<bool> HasUgcMapInfo = () => Net.LiveSession.Instance?.Home.HasInfo == true;

    /// <summary>MyPC::SendHouseSetting (@0x141530720): the RequestCube 0x18 packet, then with feature 0x117 the agreement's group 1 takes the kind's flag and goes out as 0xC0.
    /// The callers are Ms2.Ui.Hud.HouseVisitQuestion (wired) and HomeOptionController (its host is not wired).</summary>
    public void SendHouseSetting(int kind, string value, bool flag)
    {
        if (HasUgcMapInfo == null || !HasUgcMapInfo()) return;
        Net.LiveSession.Instance?.SendHouseSetting((byte) kind, value, flag);
        if (!IsFeatureEnabled(0x117)) return;
        int group = HouseSettingAgreement.Apply(Agreement, kind);
        if (group != 0) SendAgreement(group);
    }

    public void SendAgreement(int group) => Net.LiveSession.Instance?.SendPersonalInfoAgreement(group, (int)Agreement.Flags);
    /// <summary>The reminder the game menu's option entry and the system menu's option entry show (SetNotifyRedCircle7Flag,
    /// UISystemMenuDialog::SetRedCircle).</summary>
    public event Action<bool> AgreementReminder;

    /// <summary>CPersonalInfoAgreement::FUN_141262090 (@0x141262090): with feature 0x117 on, the game menu's option entry, the
    /// system menu's option entry and the options dialog's agreement tab show a red circle while group 0 is not agreed.
    /// Returns first while WorldService::HasTutorial (the field has a tutorialType; HudScene sets it from the field table).
    /// The only caller the client has that is reached on the Korean service is the dialog's (gameoptionpersonalinfoagreement.cpp:163, :179);
    /// the scene-entry caller SetFlagsAndRefresh (scenestategame.cpp:11161-11168) needs DAT_143ab0248 == 3 (China), so it is not called here.</summary>
    public Func<bool> HasTutorial = () => false;
    public void RefreshAgreementReminders()
    {
        if (HasTutorial()) return;
        if (!IsFeatureEnabled(0x117)) return;
        bool show = !Agreement.HasGroupAgreement(0);
        AgreementReminder?.Invoke(show);
        Dialog?.SetAgreementRedCircle(show);
    }
    /// <summary>CGameOptionPersonalInfoAgreement::vfunc_5 "OnDetail" (@0x140a45e50): OpenLink of the PersonalInfoAgreementURL constant.</summary>
    public void OpenAgreementDetail() => WebBrowser.Navigate(Ms2.Ui.FeatureElements.Constants(packs).String("PersonalInfoAgreementURL", ""));

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
                defaults = PackXml.Parse(packs.Read("Xml", entry));
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

    /// <summary>SetHdr @0x14125e300 (gameoptionstruct.cpp:2081-2090): the switch, then the running field's post-processing
    /// switch and light scale (Field.ApplyHdr).</summary>
    public void SetHdr(GameOption.Graphic state, bool on)
    {
        state.DisplayHdr = on;
        Field.Active?.ApplyHdr(state);
    }

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
                defaultAccount = PackXml.Parse(packs.Read("Xml", entry));
            return defaultAccount;
        }
    }

    public GameOption.CharacterFlag MouseControlFlag => GameOptions.MouseControl;
    public GameOption.CharacterFlag LearningQuestFlag => GameOptions.LearningQuest;

    /// <summary>CMouseController::HasMode: the mouse camera mode is not ported, so the keyboard camera's fov step.</summary>
    public bool MouseCameraMode => false;

    public void PageMessageBox(int stringId, string tag, OptionPage listener) =>
        MessageBoxQueue.ShowMessageBox(Common(stringId), 1, new Listener(listener.OnMessage, false), tag);

    public void ApplyCamera(GameOption.Camera camera) => GD.Print("options: camera option stored (CameraMgr and BGTransparencyMgr are not ported)");
    /// <summary>The HUD's UIDamageEffect::SetAlpha, null without a HUD.</summary>
    public Action<int> DamageEffectAlpha;

    /// <summary>CGameOptionInterface::vfunc_10's hosted part (gameoptionstruct.cpp:388): the damage effect's SetAlpha with
    /// the option's damageEffectAlpha. STILL-OPEN: the damage meters (0x4f22 / 0x4f23) are not hosted.</summary>
    public void ApplyInterface(GameOption.Interface state) => DamageEffectAlpha?.Invoke(state.DamageEffectAlpha);
    public void SetMouseControl(bool on) => GD.Print($"options: mouse control {(on ? "on" : "off")} (CMouseController is not ported)");
    public void ResetUiLayout() => GD.Print("options: UI layout reset (UISwfEntityManager::ResetLayout is not ported)");
    public void UiScaleChanged(float scale) { }   // the UI scale is read from the option at layout (UiScale)
    public void ApplyStatus(GameOption.Status state) => GD.Print("options: status stored (the hp and exp bars read it when hosted)");
    public void SendRideMode(bool disableSingleRiding) { }   // no server offline

    /// <summary>The HUD's quick slots for the input pages (the main player info's); null without a HUD.</summary>
    public IQuickSlots QuickSlots { get; set; }

    /// <summary>NotifySystem.Post(NotifyChangeGameOptionInput(id)) (FUN_1412532f0): raised for the active input page.
    /// STILL-OPEN: its listener (UIHouseDemolishDialog::OnNotifyChangeGameOptionInput, unit_14134fc90.cpp:270) is not ported.</summary>
    public event Action<int> InputOptionChanged;
    public void NotifyChangeGameOptionInput(int optionId) => InputOptionChanged?.Invoke(optionId);

    // ---- the input pages --------------------------------------------------------------------------------------------

    IKeyTable keys;

    /// <summary>The HUD's key table when it is up, else one read from the data.</summary>
    public IKeyTable Keys
    {
        get => keys ??= new Ms2.Ui.KeyTable(packs, GameOptions.StringLanguage);
        set => keys = value;
    }

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
                    ? GameOptionLocale.Parse(PackXml.Parse(packs.Read("Xml", entry)))
                    : new GameOptionLocale();
            }
            return locale.GetLocaleData(Region, false);
        }
    }

    public int RunningVoice => GameOptions.Voice;
    public bool FileExists(string path) => System.IO.File.Exists(path);
    public void TextNotice(string text) => MessageBoxQueue.ShowMessageBox(text, 0, null, "");

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
        public bool SetKeyRows(int c, int t, IReadOnlyList<KeyRow> r) => false;
        public bool SetClipShown(string n, bool v) => false;
        public bool SetResolutionRows(string n, IReadOnlyList<ResolutionRow> r, int s) => false;
        public int GetResolution(int t) => -1;
    }
}
