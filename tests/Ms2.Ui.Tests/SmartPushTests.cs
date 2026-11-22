using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class SmartPushTests
{
    const string Xml = "<ms2>" +
        "<push id='1' content='SafeRiding' actionType='additionalEffect' actionValue='70000001' requireMerat='0' imgPath='x.png'/>" +
        "<push id='2' content='AutoMining' actionType='AUTOINTERACTION' actionValue='5' requireMerat='300' requireItem='20300001:tag,1,4'/>" +
        "<push id='3' content='c' actionType='other' requireItem='1,2'/>" +
        "<push id='4' content='d' actionType='additionalEffect' feature='Off'/>" +
        "<push id='5' content='SafeWaterRidingToggle' actionType='additionalEffect' actionValue='70000005'/>" +
        "<push id='6' content='AutoFishing' actionType='additionalEffect' actionValue='70000006'/>" +
        "<push id='7' content='NoSuchClass' actionType='additionalEffect' actionValue='70000007'/>" +
        "<push id='8' content='AutoInteractObject' actionType='AUTOINTERACTION' actionValue='1'/>" +
        "<push id='9' content='AutoFunctionCubeObject' actionType='AUTOINTERACTION' actionValue='1'/>" +
        "<push id='10' content='AutoInteractObjectBuffItem' actionType='AUTOINTERACTION' actionValue='1'/>" +
        "<push id='11' content='AutoFunctionCubeObjectBuffItem' actionType='AUTOINTERACTION' actionValue='1'/>" +
        "<push id='1' content='AutoPlayInstrument' actionType='additionalEffect' actionValue='70000002'/>" +
        "</ms2>";

    static SmartPushTable Table() => new(XDocument.Parse(Xml), e => (string)e.Attribute("feature") == null);

    [Test]
    public void TableParsesRowsLikeCSmartPushTableLoad()
    {
        var t = Table();
        Assert.That(t.Count, Is.EqualTo(9), "id 3 (two item parts) and id 4 (unavailable) are dropped");
        Assert.That(t.Get(1).ActionValue, Is.EqualTo(70000002), "a later row replaces an earlier one (SetAt)");
        var p = t.Get(2);
        Assert.That((p.ActionType, p.ActionValue, p.RequireMerat), Is.EqualTo((2, 5, 300)), "Parse_140219650 is case-insensitive");
        Assert.That((p.RequireItemId, p.RequireItemTag, p.RequireItemCount), Is.EqualTo((20300001u, "tag", 4u)));
        Assert.That(t.Get(99), Is.Null);
    }

    static SmartPushNotices Make(out List<string> notices, out List<MessageBoxData> boxes, out List<(int, int, bool)> sent)
    {
        var n = new List<string>(); var b = new List<MessageBoxData>(); var s = new List<(int, int, bool)>();
        notices = n; boxes = b; sent = s;
        var strings = new Dictionary<string, string> { ["s_smartpush_active"] = "{0} active", ["s_mastery_autointeraction_result"] = "gain {0} {1}", ["s_err_lack_merat_ask"] = "Charge?" };
        return new SmartPushNotices(Table(), k => strings[k], (id, lv) => $"effect{id}L{lv}")
        {
            SystemNotice = n.Add, ShowBox = b.Add, SendAnswer = (i, c, a) => s.Add((i, c, a)),
        };
    }

    [Test]
    public void PositionalPushIsDispatchedOnItsContentClass()
    {
        var p = Make(out var notices, out _, out _);
        p.OnPositional(1, 0);      // AutoPlayInstrument: CSmartPush::vfunc_4 (smartpush.cpp:309), s_smartpush_active with the level 1 name
        p.OnPositional(5, 0);      // SafeWaterRidingToggle::vfunc_4 (:1089) is empty
        p.OnPositional(7, 0);      // content with no class: CreatePushes (smartpushmanager.cpp:79) makes no push
        p.OnPositional(99, 0);     // no such push (FUN_140dd86a0 Lookup fails)
        Assert.That(notices, Is.EqualTo(new[] { "effect70000002L1 active" }));
    }

    [Test]
    public void AutoFishingRunsTheBaseNoticeThenItsResult()
    {
        var p = Make(out var notices, out _, out _);
        int ran = 0;
        p.AutoFishingResult = () => ran++;
        p.OnPositional(6, 0);      // :492 CSmartPush::vfunc_4 first, then mode 9 and the option save
        Assert.That(notices, Is.EqualTo(new[] { "effect70000006L1 active" }));
        Assert.That(ran, Is.EqualTo(1));
    }

    [Test]
    public void AutoInteractionSaysTheResultAndAddsToTheGatherCount()
    {
        var p = Make(out var notices, out _, out _);
        var mastery = new List<string>();
        p.OnPositional(2, 3);                                  // no recipe source: silent
        Assert.That(p.GatherCounts, Is.Empty);
        p.RecipeOf = content => content == "AutoMining" ? 11 : 0;
        p.RecipeName = r => "recipe" + r;
        p.MasteryNotice = mastery.Add;
        p.OnPositional(2, 3);                                  // :1096: s_mastery_autointeraction_result(value, name), count += value
        p.OnPositional(2, 4);
        Assert.That(mastery, Is.EqualTo(new[] { "gain 3 recipe11", "gain 4 recipe11" }));
        Assert.That(p.GatherCounts[11], Is.EqualTo(7));
        Assert.That(notices, Is.Empty, "not the system notice kind 1");
    }

    [Test]
    public void TheFourUnregisteredAutoInteractionBasesShowNothing()
    {
        var p = Make(out var notices, out _, out _);
        var mastery = new List<string>();
        p.RecipeOf = _ => 21;
        p.RecipeName = r => "recipe" + r;
        p.MasteryNotice = mastery.Add;
        foreach (var id in new[] { 8, 9, 10, 11 })
            p.OnPositional(id, 2);                          // smartpush.cpp:251-263: no registrar, so no push
        Assert.That(mastery, Is.Empty);
        Assert.That(notices, Is.Empty);
        Assert.That(p.GatherCounts, Is.Empty);
    }

    [Test]
    public void LackMeratAskIsATypeOneBoxAndYesRequestsTheChargePage()
    {
        var p = Make(out _, out var boxes, out _);
        var asked = new List<bool>();
        p.ChargeRequested = asked.Add;
        var box = p.ShowLackMeratAsk();
        Assert.That(boxes, Is.EqualTo(new[] { box }));
        Assert.That((box.Type, box.Msg), Is.EqualTo((1, "Charge?")));
        Assert.That(box.Callback(1, box), Is.True, "no: the box closes");
        Assert.That(asked, Is.Empty);
        Assert.That(box.Callback(0, box), Is.False, "yes with feature 0xc2 off (FUN_141add940): returns false");
        p.ChargeFeature = () => true;
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(asked, Is.EqualTo(new[] { false, true }));
    }

    [Test]
    public void AnswerWritesIdN1cZeroAccept()
    {
        var p = Make(out _, out _, out var sent);
        p.Answer(7, 3, true);
        Assert.That(sent, Is.EqualTo(new[] { (7, 3, true) }));
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        new Ms2.Net.Packets.SmartPushAnswer(7, 3, true).Write(w);
        Assert.That(w.ToArray(), Is.EqualTo(new byte[] { 7, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 1 }));
        Assert.That(new Ms2.Net.Packets.SmartPushAnswer(7, 3, true).Op, Is.EqualTo(Ms2.Net.Outbound.SmartPush));
    }

    // ---- the manager (smartpushmanager.cpp) ----

    internal sealed class Fake
    {
        public bool Player = true, Riding, Reached, Fishing, Hide;
        public int Ridee, Instrument;
        public HashSet<int> Buffs = new();
        public List<string> Log = new();
    }

    internal sealed class FakeStrip : ISmartPushStrip
    {
        public bool Ready { get; set; } = true;
        public bool CanAdd = true;
        public List<string> Log = new();
        public bool AddPush(SmartPushView v) { Log.Add($"add {v.Id} '{v.Text}' toggle={v.IsToggle} active={v.Active} movie={v.ActiveMovie}"); return CanAdd; }
        public bool RemovePush(int id) { Log.Add("remove " + id); return true; }
    }

    internal static (SmartPushManager, FakeStrip, Fake, List<(int, int, bool)>, List<MessageBoxData>) Mgr()
    {
        var f = new Fake();
        var strings = new Dictionary<string, string>
        {
            ["s_smartpush_saferiding"] = "ride", ["s_smartpush_saferiding_active"] = "ride on",
            ["s_smartpush_saferiding_desc"] = "ride {0}", ["s_smartpush_saferiding_rbmerat_desc"] = "red ride {0}",
            ["s_smartpush_autofishing"] = "fish", ["s_smartpush_autofishing_active"] = "fish on",
            ["s_smartpush_saferiding_water"] = "water", ["s_smartpush_saferiding_water_active"] = "water on",
            ["s_mastery_automining_button"] = "mine",
        };
        var n = Make(out _, out var boxes, out var sent);
        var field = new SmartPushField
        {
            HasPlayer = () => f.Player, IsRiding = () => f.Riding, RideeType = () => f.Ridee,
            EventBuff = t => f.Buffs.Contains(t), AdventureReached = () => f.Reached, FishingAllowed = () => f.Fishing,
            Instrument = () => f.Instrument,
        };
        var m = new SmartPushManager(Table(), n, k => strings.TryGetValue(k, out var v) ? v : "", field) { Suppressed = () => f.Hide, Strip = new FakeStrip() };
        return (m, m.Strip as FakeStrip, f, sent, boxes);
    }

    [Test]
    public void ManagerMakesOnlyThePushesThatHaveAClass()
    {
        var (m, _, _, _, _) = Mgr();
        // CreatePushes (smartpushmanager.cpp:79): rows 1 PlayInstrument, 2 AutoMining, 5 Toggle, 6 AutoFishing; 7-11 have no class
        Assert.That(m.Count, Is.EqualTo(4));
        Assert.That(m.Get(7), Is.Null);
        Assert.That(m.Get(8), Is.Null);
    }

    [Test]
    public void WantedFollowsEachClassesVfunc6()
    {
        var (m, _, f, _, _) = Mgr();
        Assert.That(m.Wanted(m.Get(6)), Is.EqualTo(0), "AutoFishing: FUN_140954150 is 0");
        f.Fishing = true;
        Assert.That(m.Wanted(m.Get(6)), Is.EqualTo(1));
        f.Buffs.Add(1);
        Assert.That(m.Wanted(m.Get(6)), Is.EqualTo(2), "an enabled event buff of type 1");
        f.Player = false;
        Assert.That(m.Wanted(m.Get(6)), Is.EqualTo(0), "no local player");
        f.Player = true;
        Assert.That(m.Wanted(m.Get(5)), Is.EqualTo(0), "Toggle: not riding");
        f.Riding = true; f.Ridee = 1;
        Assert.That(m.Wanted(m.Get(5)), Is.EqualTo(0), "ridee type 1 and 2 are not wanted");
        f.Ridee = 3;
        Assert.That(m.Wanted(m.Get(5)), Is.EqualTo(0), "the toggle needs the adventure level reached");
        f.Reached = true;
        Assert.That(m.Wanted(m.Get(5)), Is.EqualTo(1));
        f.Buffs.Add(3);
        Assert.That(m.Wanted(m.Get(5)), Is.EqualTo(2));
        f.Instrument = 2;
        Assert.That(m.Wanted(m.Get(1)), Is.EqualTo(2), "PlayInstrument takes the instrument manager's answer");
        Assert.That(m.Wanted(m.Get(2)), Is.EqualTo(0), "AutoInteraction: STILL-OPEN, nothing nearby is asked");
    }

    [Test]
    public void ClickAnswersAFreePushAndAsksForAPricedOne()
    {
        var (m, _, _, sent, boxes) = Mgr();
        m.Get(6).N1c = 4;
        m.OnPushClicked(6);                      // RequireMerat 0: vfunc_5(false) (smartpush.cpp:290)
        Assert.That(sent, Is.EqualTo(new[] { (6, 4, false) }));
        m.OnPushClicked(5);                      // the toggle (:2a80) answers whatever it costs
        Assert.That(sent.Count, Is.EqualTo(2));
        m.OnPushClicked(2);                      // AutoMining row costs 300 merat: STILL-OPEN class box, base path used
        Assert.That(sent.Count, Is.EqualTo(2));
        Assert.That(boxes.Count, Is.EqualTo(1));
        Assert.That(boxes[0].Type, Is.EqualTo(0x20));
        Assert.That((boxes[0].Btn0, boxes[0].Btn1), Is.EqualTo((MessageBoxData.CommonByKey("s_word_yes"), MessageBoxData.CommonByKey("s_word_no"))), "SetButtonPreset(1): yes / no (:296)");
        Assert.That(boxes[0].EnterAllowed, Is.False, "FUN_141a024a0(0) (:301)");
        boxes[0].Callback(1, boxes[0]);          // no
        Assert.That(sent.Count, Is.EqualTo(2));
        boxes[0].Callback(0, boxes[0]);          // yes: FUN_140dcf800
        Assert.That(sent[2], Is.EqualTo((2, 0, false)));
        m.OnPushClicked(99);                     // no such push
        Assert.That(sent.Count, Is.EqualTo(3));
    }

    [Test]
    public void UpdateAddsRemovesAndRetriesLikeUpdatePushes()
    {
        var (m, strip, f, _, _) = Mgr();
        f.Fishing = true;
        m.Update(1000);                       // smartpushmanager.cpp:92-121
        Assert.That(strip.Log, Is.EqualTo(new[] { "add 6 'fish' toggle=False active=False movie=UISmartPush_autofish" }));
        m.Update(1050);                       // under 100 ms since the last poll: nothing
        f.Buffs.Add(1);
        m.Update(1099);
        Assert.That(strip.Log.Count, Is.EqualTo(1));
        m.Update(1100);                       // state 1 -> 2: added again with the active text
        Assert.That(strip.Log[1], Is.EqualTo("add 6 'fish on' toggle=False active=True movie=UISmartPush_autofish"));
        f.Fishing = false;
        m.Update(1200);
        Assert.That(strip.Log[2], Is.EqualTo("remove 6"));
        m.Update(1300);
        Assert.That(strip.Log.Count, Is.EqualTo(3), "state 0 and wanted 0: nothing");
        strip.CanAdd = false;
        f.Fishing = true;
        m.Update(1400);                       // AddPush fails: the old state (0) is kept ...
        m.Update(1500);                       // ... so it is tried again
        Assert.That(strip.Log.Count, Is.EqualTo(5));
        Assert.That(m.Get(6).State, Is.EqualTo(0));
    }

    [Test]
    public void SuppressionRemovesEveryPushAndResetReAddsThem()
    {
        var (m, strip, f, _, _) = Mgr();
        f.Fishing = true; f.Player = true; f.Riding = true; f.Ridee = 3; f.Reached = true;
        m.Update(1000);
        Assert.That(strip.Log, Is.EqualTo(new[]
        {
            "add 5 'water' toggle=True active=False movie=",
            "add 6 'fish' toggle=False active=False movie=UISmartPush_autofish",
        }));
        f.Hide = true;                        // DisplayHideSmartPush: nWanted = 0 (:103)
        m.Update(1100);
        Assert.That(strip.Log.GetRange(2, 2), Is.EqualTo(new[] { "remove 5", "remove 6" }));
        f.Hide = false;
        m.Update(1200);
        Assert.That(strip.Log.Count, Is.EqualTo(6));
        m.ResetPushStates();                  // UISmartPush::vfunc_3: every state forgotten, the next poll adds them again
        m.Update(1300);
        Assert.That(strip.Log.Count, Is.EqualTo(8));
        strip.Ready = false;
        m.ResetPushStates();
        m.Update(1400);                       // FindStripUI is null: return
        Assert.That(strip.Log.Count, Is.EqualTo(8));
    }

    [Test]
    public void UpdateAllocatesNothingWhenNoPushIsWanted()
    {
        var (m, strip, f, _, _) = Mgr();
        uint t = 1000;
        for (int i = 0; i < 300; i++) m.Update(t += 40);      // warm up: the first polls run every probe and the JIT settles
        f.Hide = true; for (int i = 0; i < 300; i++) m.Update(t += 40); f.Hide = false;
        long Measure(int n, bool hide)
        {
            f.Hide = hide;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < n; i++) m.Update(t += 40);      // every third call passes the 100 ms throttle
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        // A Release run reads a constant 24 B however many ticks run (one-off, not per tick: JIT or tiering), so the claim is
        // that ten times the ticks cost no more bytes than the short run.
        long shortRun = Measure(500, false), longRun = Measure(5000, false);
        Assert.That(longRun, Is.LessThanOrEqualTo(shortRun), "per tick: 0 B");
        long shortHidden = Measure(500, true), longHidden = Measure(5000, true);
        Assert.That(longHidden, Is.LessThanOrEqualTo(shortHidden), "the suppressed path: 0 B per tick");
        f.Hide = false;
        Assert.That(strip.Log, Is.Empty);
    }

    [Test]
    public void TheClientsOwnTableMakesPushes()
    {
        // smartpushtable.cpp:105 reads Table/smartPush.xml; the packs hold it only under table/kr/ (locale folder first)
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        if (!packs.Exists("Xml", "table/kr/smartpush.xml") && !packs.Exists("Xml", "table/smartpush.xml")) Assert.Ignore("no smartpush.xml in this data");
        var tables = new GameTables(packs, "en");
        Assert.That(tables.SmartPush.Count, Is.GreaterThan(0));
    }
}
