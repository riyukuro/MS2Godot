using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The house visit question (CPersonalInfoAgreement FUN_141262180/830/b70) and the home option dialog's apply (UIHomeOptionDialog).</summary>
public class HouseOptionTests
{
    sealed class VisitHost : IHouseVisitHost
    {
        public bool HasTutorial, PersonalInfoFeature = true, HasLocalPlayer = true, HasNoHome, InMyHouse = true, HouseGroupAgreed;
        public string Password = "";
        public List<string> Log = new();
        bool IHouseVisitHost.HasTutorial => HasTutorial;
        bool IHouseVisitHost.PersonalInfoFeature => PersonalInfoFeature;
        bool IHouseVisitHost.HasLocalPlayer => HasLocalPlayer;
        bool IHouseVisitHost.HasNoHome => HasNoHome;
        bool IHouseVisitHost.InMyHouse => InMyHouse;
        bool IHouseVisitHost.HouseGroupAgreed => HouseGroupAgreed;
        public void SetHouseMenuRedCircle(bool show) => Log.Add("circle " + show);
        public void SendHouseSetting(int kind, string value, bool flag) => Log.Add($"send {kind} '{value}' {flag}");
        public string Text(string key) => key;
        public string CommonText(string key) => key;
        public string InputText(MessageBoxData box) => Password;
    }

    static (HouseVisitQuestion q, VisitHost h, List<MessageBoxData> shown, List<int> common) Make()
    {
        var h = new VisitHost(); var shown = new List<MessageBoxData>(); var common = new List<int>();
        var q = new HouseVisitQuestion(h) { Show = shown.Add, ShowCommon = common.Add, Next = () => h.Log.Add("next") };
        return (q, h, shown, common);
    }

    [Test] public void TheQuestionOpensInMyOwnHouseUntilAgreed()
    {
        var (q, h, shown, _) = Make();
        q.Ask();
        Assert.That(h.Log, Is.EqualTo(new[] { "circle True" }));
        Assert.That(shown, Has.Count.EqualTo(1));
        Assert.That(shown[0].Type, Is.EqualTo(4));
        Assert.That(shown[0].EnterAllowed, Is.False);
    }

    [Test] public void NoQuestionWithoutTheChecks()
    {
        foreach (var set in new Action<VisitHost>[] { x => x.HasTutorial = true, x => x.PersonalInfoFeature = false, x => x.HasLocalPlayer = false })
        {
            var (q, h, shown, _) = Make(); set(h); q.Ask();
            Assert.That(shown, Is.Empty); Assert.That(h.Log, Is.Empty);
        }
        { var (q, h, shown, _) = Make(); h.HasNoHome = true; q.Ask(); Assert.That(h.Log, Is.EqualTo(new[] { "circle False" })); Assert.That(shown, Is.Empty); }
        { var (q, h, shown, _) = Make(); h.InMyHouse = false; q.Ask(); Assert.That(h.Log, Is.EqualTo(new[] { "circle True" })); Assert.That(shown, Is.Empty); }
        { var (q, h, shown, _) = Make(); h.HouseGroupAgreed = true; q.Ask(); Assert.That(h.Log, Is.EqualTo(new[] { "circle False" })); Assert.That(shown, Is.Empty); }
    }

    [Test] public void PublicAndPrivateAnswersSendTheSettingAndClearTheCircle()
    {
        foreach (var (button, kind) in new[] { (0, 0), (2, 2) })
        {
            var (q, h, shown, _) = Make(); q.Ask(); h.Log.Clear();
            Assert.That(shown[0].Callback(button, shown[0]), Is.True);
            Assert.That(h.Log, Is.EqualTo(new[] { $"send {kind} '' True", "circle False" }));
            Assert.That(q.Answered, Is.True);
        }
    }

    [Test] public void TheMiddleButtonAsksForASixDigitPasswordAndSendsNothingYet()
    {
        var (q, h, shown, _) = Make(); q.Ask(); h.Log.Clear();
        Assert.That(shown[0].Callback(1, shown[0]), Is.True);
        Assert.That(h.Log, Is.Empty); Assert.That(q.Answered, Is.False);
        Assert.That(shown[1].Type, Is.EqualTo(10));
        Assert.That(shown[1].InputMaxChars, Is.EqualTo(6));
        Assert.That(shown[1].InputType, Is.EqualTo(2));
    }

    [Test] public void AGoodPasswordIsSentAsKindOneWithFlagFalse()
    {
        var (q, h, shown, _) = Make(); q.Ask(); shown[0].Callback(1, shown[0]); h.Log.Clear();
        h.Password = "123456";
        Assert.That(shown[1].Callback(0, shown[1]), Is.True);
        Assert.That(h.Log, Is.EqualTo(new[] { "send 1 '123456' False", "next" }));
        Assert.That(q.Answered, Is.True);
    }

    [Test] public void AWrongLengthShowsTheErrorBoxThenAsksAgain()
    {
        var (q, h, shown, _) = Make(); q.Ask(); shown[0].Callback(1, shown[0]); h.Log.Clear();
        h.Password = "12345";
        Assert.That(shown[1].Callback(0, shown[1]), Is.False);
        Assert.That(h.Log, Is.Empty);
        Assert.That(shown, Has.Count.EqualTo(3));
        Assert.That(shown[2].Callback(0, shown[2]), Is.True);   // FUN_141262f10
        Assert.That(shown, Has.Count.EqualTo(4));                // the question again
    }

    [Test] public void ANonDigitPasswordShowsCommonBoxB0aAndAsksAgain()
    {
        var (q, h, shown, common) = Make(); q.Ask(); shown[0].Callback(1, shown[0]); h.Log.Clear();
        h.Password = "12345a";
        Assert.That(shown[1].Callback(0, shown[1]), Is.False);
        Assert.That(common, Is.EqualTo(new[] { 0xb0a }));
        Assert.That(h.Log, Is.EqualTo(new[] { "circle True" }));
        Assert.That(shown, Has.Count.EqualTo(3));
    }

    [Test] public void ACancelledPasswordAsksAgain()
    {
        var (q, h, shown, _) = Make(); q.Ask(); shown[0].Callback(1, shown[0]);
        Assert.That(shown[1].Callback(1, shown[1]), Is.True);
        Assert.That(shown, Has.Count.EqualTo(3));
    }

    sealed class HomeHost : IHomeOptionHost
    {
        public bool HasService = true, CanChange = true;
        public int? OpenType = 1;
        public int[] Levels = new int[9]; public bool[] On = new bool[9];
        public List<string> Log = new();
        bool IHomeOptionHost.HasService => HasService;
        bool IHomeOptionHost.CanChange => CanChange;
        int? IHomeOptionHost.OpenType => OpenType;
        public bool RowOn(int row) => On[row];
        public int RowLevel(int row) => Levels[row];
        public bool SendHouseSetting(int kind, string value, bool flag) { Log.Add($"setting {kind} '{value}' {flag}"); return true; }
        public void SendRow(int row, bool on, int level) => Log.Add($"row {row} {on} {level}");
    }

    [Test] public void SetDataIsZeroWhenOffElseLevelPlusOne()
    {
        var h = new HomeHost(); h.On[2] = true; h.Levels[2] = 3; h.Levels[4] = 5;
        var d = new HomeOptionController(h).SetData().Value;
        Assert.That(d.OpenType, Is.EqualTo(1));
        Assert.That(d.Rows, Is.EqualTo(new[] { 0, 0, 4, 0, 0, 0, 0, 0, 0 }));
        h.HasService = false;
        Assert.That(new HomeOptionController(h).SetData(), Is.Null);
    }

    [Test] public void ApplySendsTheOpenTypeThenNineRows()
    {
        var h = new HomeHost(); var common = new List<int>();
        var c = new HomeOptionController(h) { ShowCommon = common.Add };
        Assert.That(c.Apply(1, " 123456 ", new[] { 0, 1, 2, 3, 0, 0, 0, 0, 5 }), Is.True);
        Assert.That(h.Log[0], Is.EqualTo("setting 1 '123456' True"));
        Assert.That(h.Log.Skip(1), Is.EqualTo(new[] { "row 0 False 0", "row 1 True 0", "row 2 True 1", "row 3 True 2", "row 4 False 0", "row 5 False 0", "row 6 False 0", "row 7 False 0", "row 8 True 4" }));
    }

    [Test] public void TheOpenTypeClearsThePasswordUnlessItIsOne()
    {
        var h = new HomeHost();
        new HomeOptionController(h).Apply(2, "123456", new int[9]);
        Assert.That(h.Log[0], Is.EqualTo("setting 2 '' True"));
    }

    [Test] public void ApplyRefusesWhenLockedOrThePasswordIsNotSixLong()
    {
        var h = new HomeHost { CanChange = false }; var common = new List<int>();
        var c = new HomeOptionController(h) { ShowCommon = common.Add };
        Assert.That(c.Apply(1, "123456", new int[9]), Is.False);
        Assert.That(common, Is.EqualTo(new[] { 0x6e1 }));
        h.CanChange = true;
        Assert.That(c.Apply(0, "123", new int[9]), Is.False);
        Assert.That(common, Is.EqualTo(new[] { 0x6e1, 0xb0a }));
        Assert.That(h.Log, Is.Empty);
    }
}
