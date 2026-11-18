using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The options movie (uigameoptiondialog) run by its own ActionScript with UIGameOptionDialog's C++ side
/// (GameOptionDialog) through GameOptionController (needs MS2_DATA).</summary>
public class GameOptionTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    /// <summary>The game around the dialog, recorded; the movie calls go to the controller.</summary>
    sealed class Host : IGameOptionDialogHost
    {
        public GameOptionController Movie;
        public GameOptionDialog Dialog;
        public readonly List<string> Log = new();
        public GameOption Opt = new(0) { SoundOption = { BgmSoundStep = 6, MainSoundStep = 8 } };
        public readonly SoundFlag Flag = new();
        public SoundVolumes Volumes;
        public IOptionTab Tab => Movie;
        public void Invoke(string m, params object[] a) => Movie.Invoke(m, a);
        public string InvokeString(string m) => Movie.InvokeString(m);
        public void SetApplyEnabled(bool e) { Log.Add("apply " + e); Movie.SetApplyEnabled(e); }
        public void CloseDialog() { Log.Add("close"); Movie.CloseMovie(); }
        public bool IsClosed => Movie == null || Movie.IsClosed;
        public void MessageBox(int id, string tag) => Log.Add($"box {id:x} {tag}");
        public void Notice(int id) => Log.Add($"notice {id:x}");
        public void ResetBox(string t, string a, string c, string x) => Log.Add($"reset {c}");
        public string Common(int id) => $"#{id:x}";
        public string CommonByKey(string k) => k;
        public string FormatStringId(int id, params object[] a) => $"#{id:x}:" + string.Join(",", a);
        public bool IsFeatureEnabled(int f) => false;
        public int BuildNumber => 0;
        public string Locale => "KR";
        public int SceneId => 0;
        public bool GuideRestricted(int n) => false;
        public bool QuickSlotsBound => true;
        public void SaveGameSettings() => Log.Add("save");
        public void SaveCharacterSettings() => Log.Add("savechar");
        public void SendBuddyMode20() { }
        public void OpenSecondaryPassword() { }
        public bool AgreementRedCircle => false;
        public GameOption Options => Opt;
        public ScreenModes Modes { get; } = new(new[] { new DisplayMode(1920, 1080, 32), new DisplayMode(1280, 720, 32), new DisplayMode(1024, 768, 32) });
        public string AdapterName => "Test GPU";
        public int BenchmarkLevel => 0;
        public bool IsHdrSupported(int l) => l >= 1;
        public bool IsShimmerSupported(int l) => l >= 3;
        public XDocument DefaultGameDocument => null;
        public (int Width, int Height) ScreenSize => (1920, 1080);
        public bool MessageBoxFree => true;
        public void ApplyWindow(WindowRequest r) => Log.Add($"window {r.Width}x{r.Height}");
        public void SetQuality(GameOption.Graphic s, int q) { s.Quality = q; Log.Add("quality " + q); }
        public void SetHdr(GameOption.Graphic s, bool on) { s.DisplayHdr = on; Log.Add("hdr " + on); }
        public void SetShimmer(GameOption.Graphic s, bool on) { s.DisplayShimmer = on; Log.Add("shimmer " + on); }
        public void SetEffectStep(int s) => Log.Add("effect " + s);
        public void ShowFrameRate(bool s) { }
        public void EnchantEffectChanged(bool on) { }
        public SoundFlag SoundFlag => Flag;
        public bool InstrumentPlaying => false;
        public bool AppActive => true;
        public void ApplySoundVolumes(SoundVolumes v) => Volumes = v;
        public void UpdateInstrumentVolume() { }
        public GameOption.AccountCommunity AccountCommunity { get; set; } = new();
        public XDocument DefaultAccountDocument => null;
        public GameOption.CharacterFlag MouseControlFlag { get; } = GameOption.CharacterFlag.MouseControl();
        public GameOption.CharacterFlag LearningQuestFlag { get; } = GameOption.CharacterFlag.LearningQuest();
        public bool MouseCameraMode => false;
        public IKeyTable Keys => keyTable ??= new KeyTable(packs, "en");
        static KeyTable keyTable;
        public int Region => 0;
        public GameOptionLocale.Row LocaleData =>
            GameOptionLocale.Parse(XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/gameoptionlocale.xml")).TrimStart('\ufeff'))).GetLocaleData(0, false);
        public int RunningVoice => 0;
        public bool FileExists(string path) => false;
        public void TextNotice(string text) => Log.Add("text " + text);
        public void NextMessageBox() => Log.Add("next box");
        public void PageMessageBox(int id, string tag, OptionPage listener) => Log.Add($"pagebox {id:x} {tag}");
        public void ApplyCamera(GameOption.Camera c) => Log.Add("camera");
        public void ApplyInterface(GameOption.Interface s) => Log.Add("interface");
        public void SetMouseControl(bool on) => Log.Add("mouse " + on);
        public void ResetUiLayout() => Log.Add("layout");
        public void UiScaleChanged(float s) { }
        public void ApplyStatus(GameOption.Status s) => Log.Add("status");
        public void SendRideMode(bool d) => Log.Add("ride " + d);
    }

    static (UiHost, Host) Load(int tab)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var host = new Host();
        host.Dialog = new GameOptionDialog(host);
        host.Movie = new GameOptionController { Dialog = host.Dialog };
        if (tab != 0) host.Dialog.SetTab(tab);
        var (rt, p) = src.Open(host.Movie.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1282; p.StageH = 722;
        var h = new UiHost(p, host.Movie, new UiModel(tables));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        TestContext.WriteLine($"calls [{string.Join(", ", h.Calls)}] unhandled [{string.Join(", ", h.Unhandled)}]");
        TestContext.WriteLine("log: " + string.Join(" | ", host.Log));
        foreach (var e in rt.Errors.Take(20)) TestContext.WriteLine("  error: " + e);
        return (h, host);
    }

    [Test]
    public void GraphicsPageShowsTheOptions()
    {
        var (h, host) = Load(6);
        var c = host.Movie;
        var g = host.Opt.GraphicOption;
        foreach (var n in new[] { "m_quality", "m_hdr", "m_shimmer", "m_shadow", "m_vsync", "m_resolution", "m_screenModeWindow", "m_devicename", "m_effectStep" })
            TestContext.WriteLine($"{n}: sel={c.IsSelected(n)} val={c.GetValue(n)} idx={c.GetSelectedIndex(n)} text={c.GetText(n)}");
        Assert.That(h.Calls, Does.Contain("OnTabInit"));
        Assert.That(c.GetValue("m_quality"), Is.EqualTo((float)g.Quality));
        Assert.That(c.GetValue("m_effectStep"), Is.EqualTo((float)g.EffectStep));
        Assert.That(c.IsSelected("m_vsync"), Is.EqualTo(g.Vsync));
        Assert.That(c.IsSelected("m_screenModeWindow"), Is.EqualTo(g.ScreenMode == 1));
        Assert.That(c.GetText("m_devicename"), Is.EqualTo("Test GPU"));
        var dp = h.Get(h.Invoke(h.Entity, "getTabChild", "m_resolution", -1), "dataProvider");
        Assert.That(Rt.ToInt32(h.Get(dp, "length")), Is.GreaterThan(0), "FillResolutionCombo's rows");
    }

    [Test]
    public void SoundPageAppliesAndCloses()
    {
        var (h, host) = Load(7);
        var c = host.Movie;
        var s = host.Opt.SoundOption;
        TestContext.WriteLine($"main {c.GetValue("m_mainSoundStep")} bgm {c.GetValue("m_bgmSoundStep")}");
        Assert.That(c.GetValue("m_bgmSoundStep"), Is.EqualTo((float)s.BgmSoundStep));
        c.SetValue("m_bgmSoundStep", 3);
        Assert.That(c.OnCallback("OnSave", Array.Empty<object>()), Is.True);       // m_apply's onApply
        Assert.That(host.Log, Does.Contain("save"), "OnSave saves when the quick slots are bound");
        Assert.That(host.Volumes, Is.Not.Null);
        host.Log.Clear();
        host.Dialog.Call("OnCloseDialog");
        Assert.That(host.Log, Does.Contain("close"), "nothing changed since the save: the dialog closes");
        Assert.That(host.Dialog.Tab, Is.EqualTo(1), "vfunc_67 resets the tab");
    }

    /// <summary>The client's shots go Game, Graphics, Sound by the tab buttons (m_option's BUTTON_SELECT, onOption). Apply
    /// is greyed on the Game page; the Graphics page's fill enables it through m_apply (ApplyButton's registered
    /// controls); nothing greys it on a tab change (updateTab only for CN), so it stays enabled on Sound.</summary>
    [Test]
    public void ApplyStaysEnabledAfterTheGraphicsPage()
    {
        var (h, host) = Load(0);
        var apply = h.Get(h.Entity, "m_apply");
        bool Enabled() => Rt.ToBoolean(h.Get(apply, "enabled"));
        void Select(int option)
        {
            h.Set(h.Get(h.Entity, "m_option"), "selectedIndex", option);
            h.Invoke(h.Entity, "onOption", (object)null);
            for (int i = 0; i < 5; i++) h.Player.Tick();
        }
        Assert.That(Enabled(), Is.False, "Game page: Apply greyed");
        Select(1);
        Assert.That(host.Dialog.Tab, Is.EqualTo(6));
        Assert.That(Enabled(), Is.True, "Graphics page: Apply enabled");
        Select(2);
        Assert.That(host.Dialog.Tab, Is.EqualTo(7));
        Assert.That(Enabled(), Is.True, "Sound page: Apply still enabled");
    }

    /// <summary>The Game tab's pages fill from the option state, not the movie's own control defaults: Camera's switches
    /// and steps, Battle's damage-form radios, Community's account switches; Interface's meter boxes follow m_b14/m_b15.</summary>
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void GamePagesShowTheOptions(int tab)
    {
        var (h, host) = Load(tab);
        var c = host.Movie;
        var o = host.Opt;
        switch (tab)
        {
            case 1:
                Assert.That(c.IsSelected("m_enableBgTransparency"), Is.EqualTo(o.CameraOption.BgTransparency));
                Assert.That(c.IsSelected("m_enableVibrateEffect"), Is.EqualTo(o.CameraOption.VibrateEffect));
                Assert.That(c.GetValue("m_cameraLooseStep"), Is.EqualTo((float)o.CameraOption.CameraLooseStep));
                Assert.That(c.GetValue("m_cameraFovStep"), Is.EqualTo((float)o.CameraOption.CameraFovStep));
                break;
            case 2:
                Assert.That(c.IsSelected("m_autoDesc"), Is.EqualTo(o.InterfaceOption.EnableAutoDesc));
                Assert.That(c.GetValue("m_sliderUI"), Is.EqualTo(200f - o.InterfaceOption.UIScale * 100f));
                Assert.That(c.GetValue("m_damageTextScaleStep"), Is.EqualTo((float)o.InterfaceOption.DamageTextScaleStep));
                break;
            case 3:
                Assert.That(c.IsSelected("m_displayMyName"), Is.EqualTo(o.StatusOption.DisplayMyName));
                Assert.That(c.IsSelected("m_displayPcIllust"), Is.EqualTo(o.StatusOption.DisplayPcIllust));
                Assert.That(c.GetSelectedIndex("m_bossMonsterNotify"), Is.EqualTo(o.StatusOption.DisplayBossMonsterNotify));
                break;
            case 4:
                Assert.That(o.BattleOption.GiveDamage, Is.EqualTo(7));
                Assert.That(c.IsSelected("m_giveDamageAll"), Is.True);
                Assert.That(c.IsSelected("m_takeDamageParty"), Is.True, "take 3 = party");
                Assert.That(c.IsSelected("m_diffSize"), Is.True);
                Assert.That(c.IsSelected("m_diffAlpha"), Is.True);
                break;
            case 5:
                Assert.That(c.IsSelected("m_displayMyChatBalloon"), Is.EqualTo(o.CommunityOption.DisplayMyChatBalloon));
                Assert.That(c.IsSelected("m_displayChatComfirmSuper"), Is.Null, "this movie has no Super box; the page skips it");
                Assert.That(c.IsSelected("m_displayChatComfirmWorld"), Is.EqualTo(host.AccountCommunity.DisplayChatComfirmWorld));
                Assert.That(c.IsSelected("m_disablePartyRequest"), Is.EqualTo(host.AccountCommunity.DisablePartyRequest));
                break;
        }
        Assert.That(host.Dialog.IsNoPageChanged(), Is.True, "reading the controls back leaves the page unchanged");
    }

    /// <summary>The KR row of gameoptionlocale.xml has one code per list: each dropdown gets one row, one menu row, and is
    /// disabled; the language and voice read back as 0.</summary>
    [Test]
    public void LanguagePageFillsTheLocaleRows()
    {
        var (h, host) = Load(12);   // the language page is type 12 without the game pad pages
        var c = host.Movie;
        foreach (var n in new[] { "m_languageSelect", "m_regionSelect", "m_voiceSelect" })
        {
            Assert.That(c.GetSelectedIndex(n), Is.EqualTo(0), n);
            Assert.That(c.GetSelectedItemIndex(n), Is.EqualTo(0), n);
        }
        Assert.That(host.Dialog.IsNoPageChanged(), Is.True);
    }

    /// <summary>Tab type 9 (page 10) lists keySetting.xml's category 10, feature-filtered, with the default keys (DefaultKey.xml).</summary>
    [Test]
    public void InputPageListsTheKeys()
    {
        var (h, host) = Load(9);
        var list = h.Invoke(h.Entity, "getTabChild", "m_keyList", 9);
        Assert.That(Rt.IsNullish(list), Is.False, "m_keyList");
        var dp = h.Get(list, "dataProvider");
        int n = Rt.ToInt32(h.Get(dp, "length"));
        var settings = host.Keys.GetSettingList(host.Dialog.GetPageByIndex(9).Id);   // tab type 9 is page 10
        Assert.That(n, Is.EqualTo(settings.Count));
        var first = h.Invoke(dp, "requestItemAt", 0);
        long action = settings[0].Action;
        Assert.That(Rt.ToString(h.Get(first, "label")), Is.EqualTo(host.Keys.ActionName(action)));
        Assert.That(Rt.ToString(h.Get(first, "primaryKey")), Is.EqualTo(host.Keys.GetKeyName(host.Keys.FindKeyByActionAndPriority(action, 0))));
        TestContext.WriteLine($"{n} rows; first {Rt.ToString(h.Get(first, "label"))} = {Rt.ToString(h.Get(first, "primaryKey"))}");
        Assert.That(Rt.ToString(h.Get(first, "primaryKey")), Is.Not.Empty, "the first action has a default key");
    }

    [Test]
    public void HdrAndShimmerImageTooltips()
    {
        var (h, _) = Load(0);
        foreach (var (name, kind, path) in new[] { ("m_hdr", "hdr", "HDR_PATH"), ("m_shimmer", "shimmer", "distortion_path") })
        {
            var d = h.Controller.GetUserData("getTooltip", new object[] { name });
            Assert.That(d["tooltip"], Is.EqualTo("export_common_tooltip_tooltipImageOption"));
            Assert.That(d["optionDesc"], Is.EqualTo("$s_gameoption_tooltip_" + kind));
            Assert.That(d["imgPath"], Is.EqualTo(tables.CommonResource(path)).And.StartsWith("./Data/Resource/Image/"));
            var shown = TooltipTests.Draw(d);
            TestContext.WriteLine(name + ": " + string.Join(" | ", shown));
            Assert.That(shown, Is.Not.Empty);
            Assert.That(shown, Has.None.StartsWith("$s_"), "the movie localizes the ids");
        }
        Assert.That(h.Controller.GetUserData("getTooltip", new object[] { "m_other" }), Is.Null);
    }
}
