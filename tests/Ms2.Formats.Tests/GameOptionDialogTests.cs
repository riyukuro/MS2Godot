using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

public class GameOptionDialogTests
{
    sealed class FakeTab : IOptionTab
    {
        public readonly Dictionary<string, bool> Selected = new();
        public readonly Dictionary<string, float> Values = new();
        public readonly Dictionary<string, int> Index = new();
        public readonly Dictionary<string, string> Text = new();
        public readonly Dictionary<string, bool> Shown = new();
        public List<ResolutionRow> Rows;
        public int RowSelected, Resolution = -1;

        public bool? IsSelected(string n) => Selected.TryGetValue(n, out var v) ? v : null;
        public void SetSelected(string n, bool v) => Selected[n] = v;
        public float? GetValue(string n) => Values.TryGetValue(n, out var v) ? v : null;
        public void SetValue(string n, float v) => Values[n] = v;
        public int? GetSelectedIndex(string n) => Index.TryGetValue(n, out var v) ? v : null;
        public void SetSelectedIndex(string n, int v) => Index[n] = v;
        public string GetText(string n) => Text.TryGetValue(n, out var v) ? v : null;
        public void SetText(string n, string v) => Text[n] = v;
        public readonly Dictionary<string, bool> Enabled = new();
        public void SetEnabled(string n, bool v) => Enabled[n] = v;
        public readonly Dictionary<string, List<(string Label, int Index)>> Items = new();
        public readonly Dictionary<string, int> MenuRows = new();
        public bool SetItemRows(string n, IReadOnlyList<(string Label, int Index)> r) { Items[n] = r.ToList(); return true; }
        public void SetMenuRowCount(string n, int r) => MenuRows[n] = r;
        public int? GetSelectedItemIndex(string n) =>
            Items.TryGetValue(n, out var l) && Index.TryGetValue(n, out var i) && i >= 0 && i < l.Count ? l[i].Index : null;
        public bool SetClipShown(string n, bool v) { Shown[n] = v; return true; }
        public bool SetResolutionRows(string n, IReadOnlyList<ResolutionRow> rows, int sel) { Rows = rows.ToList(); RowSelected = sel; return true; }
        public int GetResolution(int type) => Resolution;
    }

    sealed class FakeHost : IGameOptionDialogHost
    {
        public readonly FakeTab T = new();
        public IOptionTab Tab => T;
        public readonly List<string> Log = new();
        public readonly HashSet<int> Features = new();
        public int Build = 0x32, Scene, Benchmark = 1;
        public bool Bound = true, Free = true, Playing, Active = true;
        public GameOption Opt = new(2);
        public ScreenModes M = new(new[]
        {
            new DisplayMode(1920, 1080, 32), new DisplayMode(1024, 768, 32), new DisplayMode(1280, 720, 32), new DisplayMode(800, 600, 32),
        });
        public readonly List<WindowRequest> Windows = new();
        public SoundVolumes Volumes;
        public readonly SoundFlag Flag = new();
        public XDocument Defaults;

        public void Invoke(string m, params object[] a) => Log.Add(m + "(" + string.Join(",", a) + ")");
        public string InvokeString(string m) => m == "getCurrentTabName" ? "Tab" : "Category";
        public void MessageBox(int id, string tag) => Log.Add($"box {id:x} {tag}");
        public void Notice(int id) => Log.Add($"notice {id:x}");
        public void ResetBox(string t, string a, string c, string x) => Log.Add($"reset {t}|{a}|{c}|{x}");
        public string Common(int id) => $"#{id:x}";
        public string CommonByKey(string k) => k;
        public string FormatStringId(int id, params object[] a) => $"#{id:x}:" + string.Join(",", a);
        public void SetApplyEnabled(bool e) => Log.Add("apply " + e);
        public void CloseDialog() => Log.Add("close");
        public bool Vfunc55 => false;
        public bool IsFeatureEnabled(int f) => Features.Contains(f);
        public int BuildNumber => Build;
        public string Locale => "en";
        public int SceneId => Scene;
        public bool GuideRestricted(int n) => false;
        public bool QuickSlotsBound => Bound;
        public void SaveGameSettings() => Log.Add("save");
        public void SaveCharacterSettings() => Log.Add("savechar");
        public void SendBuddyMode20() => Log.Add("buddy");
        public void OpenSecondaryPassword() => Log.Add("password");
        public bool AgreementRedCircle => false;
        public GameOption Options => Opt;
        public ScreenModes Modes => M;
        public string AdapterName => "GPU";
        public int BenchmarkLevel => Benchmark;
        public bool IsHdrSupported(int l) => l >= 3;
        public bool IsShimmerSupported(int l) => l >= 2;
        public XDocument DefaultGameDocument => Defaults;
        public (int Width, int Height) ScreenSize => (1920, 1080);
        public bool MessageBoxFree => Free;
        public void ApplyWindow(WindowRequest r) => Windows.Add(r);
        public void SetQuality(GameOption.Graphic s, int q) => Log.Add("quality " + q);
        public void SetHdr(GameOption.Graphic s, bool on) => Log.Add("hdr " + on);
        public void SetShimmer(GameOption.Graphic s, bool on) => Log.Add("shimmer " + on);
        public void SetEffectStep(int s) => Log.Add("effect " + s);
        public void ShowFrameRate(bool s) => Log.Add("fps " + s);
        public void EnchantEffectChanged(bool on) => Log.Add("enchant " + on);
        public SoundFlag SoundFlag => Flag;
        public bool InstrumentPlaying => Playing;
        public bool AppActive => Active;
        public void ApplySoundVolumes(SoundVolumes v) => Volumes = v;
        public void UpdateInstrumentVolume() => Log.Add("instrument");
        public GameOption.AccountCommunity AccountCommunity { get; set; } = new();
        public System.Xml.Linq.XDocument DefaultAccountDocument => null;
        public GameOption.CharacterFlag MouseControlFlag { get; } = GameOption.CharacterFlag.MouseControl();
        public GameOption.CharacterFlag LearningQuestFlag { get; } = GameOption.CharacterFlag.LearningQuest();
        public bool MouseCameraMode { get; set; }
        public int Region { get; set; }
        public GameOptionLocale.Row LocaleData { get; set; }
        public int RunningVoice { get; set; }
        public readonly HashSet<string> Files = new();
        public bool FileExists(string path) => Files.Contains(path);
        public void TextNotice(string text) => Log.Add("text " + text);
        public void PageMessageBox(int id, string tag, OptionPage listener) => Log.Add($"pagebox {id:x} {tag}");
        public void ApplyCamera(GameOption.Camera c) => Log.Add("camera");
        public void ApplyInterface(GameOption.Interface s) => Log.Add("interface");
        public void SetMouseControl(bool on) => Log.Add("mouse " + on);
        public void ResetUiLayout() => Log.Add("layout");
        public void UiScaleChanged(float s) => Log.Add("uiscale " + s);
        public void ApplyStatus(GameOption.Status s) => Log.Add("status");
        public void SendRideMode(bool d) => Log.Add("ride " + d);
    }

    static (GameOptionDialog, FakeHost) Open(FakeHost h = null)
    {
        h ??= new FakeHost();
        var d = new GameOptionDialog(h);
        d.OnLoadComplete();
        return (d, h);
    }

    static GraphicOptionPage ShowGraphics(GameOptionDialog d)
    {
        d.Call("OnOpenTab", 6);
        d.Call("OnTabInit", 6);
        return (GraphicOptionPage)d.GetPageByIndex(6);
    }

    static readonly GameOptionLocale.Row NaRow = new("NA", new[] { "en", "de", "pt" }, new[] { "na" }, new[] { "en" });
    static readonly GameOptionLocale.Row CnRow = new("CN", new[] { "cn" }, new[] { "cn" }, new[] { "cn", "kr" });

    static LanguageOptionPage ShowLanguage(GameOptionDialog d)
    {
        d.Call("OnOpenTab", 12);
        d.Call("OnTabInit", 12);
        return (LanguageOptionPage)d.Page(14);
    }

    [Test]
    public void LanguagePageFillsFromTheLocaleRow()
    {
        var h = new FakeHost { Region = 2, LocaleData = NaRow };
        h.Opt.ConnectOption.Region = "";
        h.Opt.LanguageOption.LanguageIndex = 5;   // DE
        var (d, _) = Open(h);
        ShowLanguage(d);
        Assert.That(h.T.Items["m_languageSelect"], Is.EqualTo(new[]
        {
            ("s_gameoption_language_en", 2), ("s_gameoption_language_de", 5), ("s_gameoption_language_pt", 6),
        }));
        Assert.That(h.T.MenuRows["m_languageSelect"], Is.EqualTo(3));
        Assert.That(h.T.Enabled.ContainsKey("m_languageSelect"), Is.False, "three rows stay enabled");
        Assert.That(h.T.Index["m_languageSelect"], Is.EqualTo(1), "DE selected");
        Assert.That(h.T.Items["m_regionSelect"], Is.EqualTo(new[] { ("s_gameoption_region_stage", 0) }), "NA: the connect region, stage when empty");
        Assert.That(h.T.Enabled["m_regionSelect"], Is.False);
        Assert.That(h.T.Enabled["m_voiceSelect"], Is.False, "one voice row disables the list");
        Assert.That(d.IsNoPageChanged(), Is.True);
    }

    [Test]
    public void LanguageChangeAsksForARestart()
    {
        var h = new FakeHost { Region = 2, LocaleData = NaRow, RunningVoice = 2 };
        var (d, _) = Open(h);
        var p = ShowLanguage(d);
        h.T.Index["m_languageSelect"] = 2;   // PT
        p.vfunc_7();
        Assert.That(p.vfunc_14(), Is.EqualTo(1));
        p.vfunc_10();
        Assert.That(h.Log, Does.Contain("text s_gameoption_voice_select_restart"));
        Assert.That(h.Opt.LanguageOption.LanguageIndex, Is.EqualTo(6));
    }

    [Test]
    public void ChinaKoreanVoiceNeedsItsSoundFiles()
    {
        var h = new FakeHost { Region = 3, LocaleData = CnRow, RunningVoice = 3 };
        h.Opt.LanguageOption.LanguageIndex = 3;
        h.Opt.LanguageOption.VoiceIndex = 3;
        var (d, _) = Open(h);
        var p = ShowLanguage(d);
        h.T.Index["m_voiceSelect"] = 1;   // kr
        p.vfunc_7();
        p.vfunc_10();
        Assert.That(h.Log, Does.Contain("text s_gameoption_voice_select_warning"));
        Assert.That(h.Log, Does.Not.Contain("text s_gameoption_voice_select_restart"));
        Assert.That(h.Opt.LanguageOption.VoiceIndex, Is.EqualTo(3), "the missing voice goes back");
        Assert.That(h.T.Index["m_voiceSelect"], Is.EqualTo(3), "the client selects the voice enum as the row index");
    }

    [Test]
    public void PagesAreTypedInCreationOrder()
    {
        var d = new GameOptionDialog(new FakeHost());
        Assert.That(d.Pages.Select(p => p.Id), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 13, 14, 16 }));
        Assert.That(d.Page(14).Type, Is.EqualTo(12), "no game pad pages: the language page is the 12th");
        Assert.That(d.GetPageByIndex(8).Id, Is.EqualTo(9));
        var h = new FakeHost();
        h.Features.UnionWith(new[] { 0xb5, 0x117 });
        d = new GameOptionDialog(h);
        Assert.That(d.Pages.Select(p => p.Id), Is.EqualTo(Enumerable.Range(1, 16)), "the map order is the key order");
        Assert.That(d.Page(15).Type, Is.EqualTo(16), "created last");
    }

    [Test]
    public void LoadCompleteInitialisesTheMovie()
    {
        var h = new FakeHost { Build = 0x31 };
        var (d, _) = Open(h);
        Assert.That(d.Page(16), Is.Null, "an old build has no account page");
        Assert.That(h.Log, Is.EqualTo(new[] { "initData(en,0,0)", "setTabComponent(1,False,False)" }));
        Assert.That(d.Tab, Is.EqualTo(1));
        Assert.That(d.Pages.All(p => p.Enabled && p.Pending && !p.Active), Is.True);
    }

    [Test]
    public void SimpleModeOpensGraphics()
    {
        var h = new FakeHost { Scene = 0x6200009 };
        var (d, _) = Open(h);
        Assert.That(d.Simple, Is.True);
        Assert.That(d.Tab, Is.EqualTo(6));
        Assert.That(h.Log, Does.Contain("setTabComponent(6,True,False)"));
        Assert.That(d.Pages.Where(p => p.Enabled).Select(p => p.Type), Is.EqualTo(new[] { 6, 7 }), "type 0xe does not exist here");
        d.Close();
        Assert.That((d.Simple, d.Tab), Is.EqualTo((false, 1)));
    }

    [Test]
    public void TheGraphicsTabFillsOnTabInit()
    {
        var (d, h) = Open();
        h.Opt.GraphicOption.Width = 1280;
        h.Opt.GraphicOption.Height = 720;
        d.FUN_141943480();   // the open refresh copies the stored option again
        d.Call("OnOpenTab", 6);
        Assert.That(h.T.Rows, Is.Null, "not filled before the tab's movie says it is ready");
        var g = ShowGraphics(d);
        Assert.That(g.Active, Is.True);
        Assert.That(h.T.Rows.Select(r => (r.ModeId, r.Label)), Is.EqualTo(new[]
        {
            (1, "#30f:1024,768,4,3"), (2, "#310:1280,720,16,9"), (0, "#310:1920,1080,16,9"),
        }), "usable modes sorted by size, the mode index as modeid");
        Assert.That(h.T.RowSelected, Is.EqualTo(1));
        Assert.That(h.T.Selected["m_screenModeWindow"], Is.True);
        Assert.That(h.T.Selected["m_screenModeFullWindow"], Is.False);
        Assert.That(h.T.Index["m_shadow"], Is.EqualTo(1), "shadow level 2 is the middle row");
        Assert.That(h.T.Text["m_devicename"], Is.EqualTo("GPU"));
    }

    [Test]
    public void DisplayQualityFollowsOnlyAfterTheFill()
    {
        var (d, h) = Open();
        var g = (GraphicOptionPage)d.GetPageByIndex(6);
        d.OpenTab(6);
        d.Call("onDisplayQuality", 3);
        Assert.That(h.T.Selected.ContainsKey("m_hdr"), Is.False, "ignored until the page is filled");
        d.Call("OnTabInit", 6);
        d.Call("onDisplayQuality", 0);
        Assert.That((h.T.Selected["m_hdr"], h.T.Index["m_shadow"], h.T.Values["m_effectStep"]), Is.EqualTo((false, 0, 1f)));
        Assert.That(h.Log, Does.Not.Contain("notice 84d"));
        d.Call("onDisplayQuality", 2);
        Assert.That(h.Log, Does.Contain("notice 84d"), "above the benchmark level 1");
        h.Log.Clear();
        d.Call("onDisplayQuality", 3);
        Assert.That(h.Log, Does.Not.Contain("notice 84d"), "the stored quality 3 does not warn");
        Assert.That((h.T.Selected["m_hdr"], h.T.Index["m_shadow"]), Is.EqualTo((true, 2)));
    }

    [Test]
    public void SaveSwitchesTheWindowAndStores()
    {
        var (d, h) = Open();
        ShowGraphics(d);
        h.T.Resolution = 0;                         // 1920 x 1080
        h.T.Selected["m_screenModeWindow"] = false;
        h.T.Selected["m_screenModeFullWindow"] = true;
        h.T.Selected["m_vsync"] = false;
        h.T.Index["m_shadow"] = 2;
        d.Call("OnSave");
        Assert.That(h.Windows, Is.EqualTo(new[] { new WindowRequest(false, true, 1920, 1080) }));
        var g = h.Opt.GraphicOption;
        Assert.That((g.ScreenMode, g.ResolutionIndex, g.Width, g.Height, g.Vsync, g.ShadowLevel), Is.EqualTo((2, 0, 1920, 1080, false, 3)));
        Assert.That(h.Log.SkipWhile(l => !l.StartsWith("fps")), Is.EqualTo(new[] { "fps False", "notice 84e", "instrument", "savechar", "save", "uiscale 1", "buddy" }),
            "vsync only notifies; the file is written after every page saved");
        h.Windows.Clear();
        d.OnSave();
        Assert.That(h.Windows, Is.Empty, "unchanged mode and row do not switch");
    }

    [Test]
    public void SaveWaitsForUnboundQuickSlots()
    {
        var (d, h) = Open();
        h.Bound = false;
        d.Call("OnSave");
        Assert.That(h.Log.Last(), Is.EqualTo("box 84a OnSave"));
        Assert.That(h.Log, Does.Not.Contain("save"));
        d.OnMessage("OnSave", 0);
        Assert.That(h.Log.TakeLast(4), Is.EqualTo(new[] { "save", "uiscale 1", "buddy", "apply False" }));   // the Interface page's vfunc_11 after the save
    }

    [Test]
    public void CloseAsksWhenAPageChanged()
    {
        var (d, h) = Open();
        ShowGraphics(d);
        d.Call("OnCloseDialog");
        Assert.That(h.Log.Last(), Is.EqualTo("close"), "nothing changed");
        (d, h) = Open();
        ShowGraphics(d);
        h.T.Selected["m_vsync"] = false;
        d.Call("OnCloseDialog");
        Assert.That(h.Log.Last(), Is.EqualTo("box 84b OnSaveAndCheck"));
        d.OnMessage("OnSaveAndCheck", 1);
        Assert.That(h.Log.Last(), Is.EqualTo("close"));
        Assert.That(h.Opt.GraphicOption.Vsync, Is.True, "no keeps the stored option");
        Assert.That(d.Pages.Any(p => p.Active), Is.False);
    }

    [Test]
    public void ResetBoxAndItsAnswers()
    {
        var (d, h) = Open();
        d.Call("OnInit");
        Assert.That(h.Log.Last(), Is.EqualTo("reset #847|#848|#849:Tab,Category|#476"));
        d.OpenTab(9);
        d.Call("OnInit");
        Assert.That(h.Log.Last(), Is.EqualTo("reset #847|#848|#849:Tab,s_gameoption_init_shoutcut|#476"));
        d.OpenTab(6);
        d.Call("OnTabInit", 6);
        var g = (GraphicOptionPage)d.GetPageByIndex(6);
        h.Defaults = XDocument.Parse("<ms2><graphic fullScreen=\"2\" vsync=\"false\"/></ms2>");
        d.OnMessage("", 1);
        Assert.That((g.State.Quality, g.State.EffectStep, g.State.ShadowLevel, g.State.DisplayShimmer, g.State.DisplayHdr), Is.EqualTo((1, 1, 1, false, false)),
            "the default form follows the benchmark level");
        Assert.That((g.State.ResolutionIndex, g.State.Width, g.State.ScreenMode, g.State.Vsync), Is.EqualTo((0, 1920, 2, false)));
        Assert.That(h.Log.Last(), Is.EqualTo("apply True"));
        h.Log.Clear();
        d.OnMessage("", 2);
        Assert.That(h.Log, Is.Empty, "cancel");
    }

    [Test]
    public void SoundSaveSetsTheVolumes()
    {
        var (d, h) = Open();
        h.Features.Add(10);
        d.OpenTab(7);
        d.Call("OnTabInit", 7);
        Assert.That(h.T.Shown["m_playInstrumentStep"], Is.True);
        h.T.Values["m_mainSoundStep"] = 8;
        h.T.Values["m_bgmSoundStep"] = 5;
        h.T.Selected["m_disableJobSound"] = true;
        d.OnSave();
        Assert.That(h.Opt.SoundOption.BgmSoundStep, Is.EqualTo(5));
        Assert.That(h.Flag.DisableJobSound, Is.True);
        Assert.That(h.Volumes.Category[2], Is.EqualTo(0.4f));
        Assert.That(h.Volumes.MainVolume, Is.EqualTo(0.8f));
    }

    [Test]
    public void SoundVolumesFromTheOption()
    {
        var s = new GameOption.Sound { MainSoundStep = 10, BgmSoundStep = 7, EffectSoundStep = 3, NpcSoundStep = 4 };
        var v = SoundVolumes.From(s, instrumentPlaying: true, appActive: true);
        Assert.That(v.Category[2], Is.EqualTo(0f), "the bgm is off while an instrument plays");
        Assert.That((v.Category[0], v.Category[9], v.Category[10]), Is.EqualTo((0.3f, 0.3f, 0.4f)));
        Assert.That((v.AllMute, v.MuteInstruments), Is.EqualTo((false, false)));
        s.OffBgmWhilePlayInstrument = false;
        v = SoundVolumes.From(s, true, appActive: false);
        Assert.That(v.Category[2], Is.EqualTo(0.7f));
        Assert.That((v.AllMute, v.MuteInstruments), Is.EqualTo((true, true)), "in the background unless play-all");
        s.PlayAll = true;
        Assert.That(SoundVolumes.From(s, false, false).AllMute, Is.False);
    }

    [Test]
    public void SoundChangeNeedsBothBlocks()
    {
        var (d, h) = Open();
        var p = (SoundOptionPage)d.Page(7);
        p.State.BgmSoundStep = 9;
        Assert.That(p.vfunc_14(), Is.EqualTo(0), "the client's test: a state change alone is not a change");
        p.Flag.DisableJobSound = true;
        Assert.That(p.vfunc_14(), Is.EqualTo(1));
    }

    [TestCase(1920, 1080, 16, 9)]
    [TestCase(1600, 900, 16, 9)]
    [TestCase(1366, 768, 16, 9)]
    [TestCase(1280, 800, 16, 10)]
    [TestCase(1680, 1050, 16, 10)]
    [TestCase(1024, 768, 4, 3)]
    [TestCase(1280, 1024, 5, 4)]
    [TestCase(2560, 1080, 21, 9)]
    public void AspectRatio(int w, int h, int a, int b)
    {
        Assert.That(GraphicOptionPage.GetAspectRatio(w, h), Is.EqualTo((a, b)));
    }

    [Test]
    public void Wtoi()
    {
        Assert.That(GraphicOptionPage.Wtoi(" 12x"), Is.EqualTo(12));
        Assert.That(GraphicOptionPage.Wtoi("-7"), Is.EqualTo(-7));
        Assert.That(GraphicOptionPage.Wtoi("abc"), Is.EqualTo(0));
    }
}
