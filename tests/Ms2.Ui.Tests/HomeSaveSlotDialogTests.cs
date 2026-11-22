using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIHomeSaveSlotDialog (unit_140fa9eb0_uihomesaveslotdialog.cpp): the slots shown, the boxes, the sends.</summary>
public class HomeSaveSlotDialogTests
{
    sealed class Host : IHomeSaveSlotHost
    {
        public bool HasService { get; set; } = true;
        public int Plot;
        public bool PlotModeIs(int mode) => Plot == mode;
        public List<UgcMapLayout> DesignList = new(), BlueprintList = new();
        public IReadOnlyList<UgcMapLayout> Designs => DesignList;
        public IReadOnlyList<UgcMapLayout> Blueprints => BlueprintList;
        public int GmLevel { get; set; }
        public bool InHomeField { get; set; }
        public bool PlotAdminOnly { get; set; }
        public int Language { get; set; } = 1;
        public List<string> Log = new();
        public void SendSaveDesign(int slot, string name) => Log.Add($"save {slot} {name}");
        public void SendLoadDesignBlocked(int slot) => Log.Add($"blocked {slot}");
        public void SendLoadDesign(int slot) => Log.Add($"load {slot}");
        public void SendSaveBlueprint(int slot, string name) => Log.Add($"bpsave {slot} {name}");
        public void SendLoadBlueprint(int slot) => Log.Add($"bpload {slot}");
    }

    static (HomeSaveSlotDialogController c, Host h, List<MessageBoxData> boxes, List<(string, object[])> calls, List<(string, int)> ints) Make()
    {
        var h = new Host(); var boxes = new List<MessageBoxData>(); var calls = new List<(string, object[])>(); var ints = new List<(string, int)>();
        var c = new HomeSaveSlotDialogController(h)
        {
            Str = k => k, ShowBox = boxes.Add, CallMovie = (n, a) => calls.Add((n, a)), SetIntMember = (n, v) => ints.Add((n, v)),
        };
        return (c, h, boxes, calls, ints);
    }

    [Test] public void SaveModeIsTheDefault() => Assert.That(Make().c.SaveMode, Is.EqualTo(1));

    [Test] public void ModeIsPutIntoTheMovieWhenLoaded()
    {
        var (c, _, _, _, ints) = Make();
        c.SetLoadMode();
        Assert.That(c.SaveMode, Is.EqualTo(0));
        Assert.That(ints, Is.EqualTo(new[] { ("saveMode", 0) }));
        c.SetSaveMode();
        Assert.That(ints[^1], Is.EqualTo(("saveMode", 1)));
    }

    [Test] public void ModeOpensTheDialogWhenTheMovieIsNotLoaded()
    {
        var (c, _, _, _, ints) = Make();
        int opened = 0;
        c.MovieLoaded = () => false; c.Open = () => opened++;
        c.SetLoadMode();
        Assert.That(opened, Is.EqualTo(1));
        Assert.That(ints, Is.Empty, "the load puts the mode itself");
        Assert.That(c.SaveMode, Is.EqualTo(0));
    }

    [Test] public void OnlyExistingSlotsAreShownUpToFive()
    {
        var (c, h, _, calls, _) = Make();
        for (int i = 0; i < 7; i++) h.DesignList.Add(new UgcMapLayout(i, "n" + i, 0));
        c.UpdateSlots();
        Assert.That(calls.Select(x => x.Item1), Is.All.EqualTo("setSlot"));
        Assert.That(calls.Count, Is.EqualTo(5));
        Assert.That(calls[4].Item2[0], Is.EqualTo(4));
        Assert.That(calls[0].Item2[2], Is.EqualTo(""), "time 0 has no text");
        calls.Clear(); h.DesignList.Clear(); h.DesignList.Add(new UgcMapLayout(2, "x", 0));
        c.UpdateSlots();
        Assert.That(calls.Count, Is.EqualTo(1));
    }

    [Test] public void BlueprintModeShowsTheBlueprintList()
    {
        var (c, h, _, calls, _) = Make();
        h.DesignList.Add(new UgcMapLayout(0, "design", 0)); h.BlueprintList.Add(new UgcMapLayout(1, "plan", 0));
        h.Plot = 3;
        c.UpdateSlots();
        Assert.That(calls.Single().Item2[1], Is.EqualTo("plan"));
    }

    [TestCase(1, "{0:yyyy}/{0:MM}/{0:dd}")] [TestCase(5, "{0:dd}.{0:MM}.{0:yyyy}")] [TestCase(2, "{0:MM}/{0:dd}/{0:yyyy}")] [TestCase(4, "{0:dd}/{0:MM}/{0:yyyy}")]
    public void TimeTextFollowsTheLanguage(int language, string dateFormat)
    {
        var (c, h, _, _, _) = Make();
        h.Language = language;
        long t = 1795352709;
        var tm = Ms2.Net.MsTime.LocalTm(t);
        Assert.That(c.TimeText(t), Is.EqualTo(string.Format(System.Globalization.CultureInfo.InvariantCulture, dateFormat + " {0:HH}:{0:mm}:{0:ss}", tm)));
    }

    [Test] public void CallsNeedAnArgumentAndTheService()
    {
        var (c, h, boxes, _, _) = Make();
        Assert.That(c.OnCallback("OnSaveSlot", null), Is.False);
        Assert.That(c.OnCallback("onloadslot", Array.Empty<object>()), Is.False);
        h.HasService = false;
        c.OnCallback("OnSaveSlot", new object[] { 1 });
        Assert.That(boxes, Is.Empty);
        Assert.That(c.OnCallback("other", new object[] { 1 }), Is.False);
    }

    [Test] public void SaveAsksForAName()
    {
        var (c, _, boxes, _, _) = Make();
        c.OnCallback("onsaveslot", new object[] { 2 });
        var b = boxes.Single();
        Assert.That(b.Type, Is.EqualTo(5));
        Assert.That(b.ArgInt(0, -1), Is.EqualTo(2));
    }

    [Test] public void SlotsAboveFiveAskNothing()
    {
        var (c, _, boxes, _, _) = Make();
        c.OnCallback("OnSaveSlot", new object[] { 6 });
        c.OnCallback("OnLoadSlot", new object[] { -1 });
        Assert.That(boxes, Is.Empty);
    }

    static void Answer(MessageBoxData b, int result, string name = null)
    {
        if (name != null) b.ClickedTag = name;
        b.Callback(result, b);
    }

    [Test] public void FreeSlotSavesTheEnteredNameAtOnce()
    {
        var (c, h, boxes, _, _) = Make();
        c.OnCallback("OnSaveSlot", new object[] { 1 });
        Answer(boxes[0], 0, "home");
        Assert.That(h.Log, Is.EqualTo(new[] { "save 1 home" }));
        Assert.That(boxes.Count, Is.EqualTo(1));
    }

    [Test] public void UsedSlotAsksBeforeOverwriting()
    {
        var (c, h, boxes, _, _) = Make();
        h.DesignList.Add(new UgcMapLayout(1, "old", 5));
        c.OnCallback("OnSaveSlot", new object[] { 1 });
        Answer(boxes[0], 0, "new");
        Assert.That(h.Log, Is.Empty);
        Assert.That(boxes.Count, Is.EqualTo(2));
        Answer(boxes[1], 1);
        Assert.That(h.Log, Is.Empty, "no sends on no");
        Answer(boxes[1], 0);
        Assert.That(h.Log, Is.EqualTo(new[] { "save 1 new" }));
    }

    [Test] public void CancelledNameBoxSendsNothing()
    {
        var (c, h, boxes, _, _) = Make();
        c.OnCallback("OnSaveSlot", new object[] { 1 });
        Answer(boxes[0], 1, "x");
        Assert.That(h.Log, Is.Empty);
    }

    [Test] public void BlueprintSaveUsesTheBlueprintListAndMode0x40()
    {
        var (c, h, boxes, _, _) = Make();
        h.Plot = 3; h.DesignList.Add(new UgcMapLayout(1, "d", 5));
        c.OnCallback("OnSaveSlot", new object[] { 1 });
        Answer(boxes[0], 0, "bp");
        Assert.That(h.Log, Is.EqualTo(new[] { "bpsave 1 bp" }), "the design list does not count");
        h.BlueprintList.Add(new UgcMapLayout(2, "p", 9));
        c.OnCallback("OnSaveSlot", new object[] { 2 });
        Answer(boxes[1], 0, "q");
        Assert.That(boxes.Count, Is.EqualTo(3));
        Answer(boxes[2], 0);
        Assert.That(h.Log[^1], Is.EqualTo("bpsave 2 q"));
    }

    [Test] public void DesignLoadIsBlockedInDesignModeElseMode35()
    {
        var (c, h, boxes, _, _) = Make();
        h.Plot = 1;
        c.OnCallback("OnLoadSlot", new object[] { 3 });
        Answer(boxes[0], 0);
        h.Plot = 0;
        c.OnCallback("OnLoadSlot", new object[] { 3 });
        Answer(boxes[1], 0);
        h.GmLevel = 50; h.InHomeField = true; h.PlotAdminOnly = true;
        c.OnCallback("OnLoadSlot", new object[] { 4 });
        Answer(boxes[2], 0);
        c.OnCallback("OnLoadSlot", new object[] { 4 });
        Answer(boxes[3], 1);
        Assert.That(h.Log, Is.EqualTo(new[] { "blocked 3", "load 3", "blocked 4" }));
    }

    [Test] public void BlueprintLoadNeedsBlueprintMode()
    {
        var (c, h, boxes, _, _) = Make();
        h.Plot = 3;
        c.OnCallback("OnLoadSlot", new object[] { 2 });
        h.Plot = 0;
        Answer(boxes[0], 0);
        Assert.That(h.Log, Is.Empty);
        h.Plot = 3;
        Answer(boxes[0], 0);
        Assert.That(h.Log, Is.EqualTo(new[] { "bpload 2" }));
    }
}
