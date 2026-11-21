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
        "<push id='1' content='AutoPlayInstrument' actionType='additionalEffect' actionValue='70000002'/>" +
        "</ms2>";

    static SmartPushTable Table() => new(XDocument.Parse(Xml), e => (string)e.Attribute("feature") == null);

    [Test]
    public void TableParsesRowsLikeCSmartPushTableLoad()
    {
        var t = Table();
        Assert.That(t.Count, Is.EqualTo(5), "id 3 (two item parts) and id 4 (unavailable) are dropped");
        Assert.That(t.Get(1).ActionValue, Is.EqualTo(70000002), "a later row replaces an earlier one (SetAt)");
        var p = t.Get(2);
        Assert.That((p.ActionType, p.ActionValue, p.RequireMerat), Is.EqualTo((2, 5, 300)), "Parse_140219650 is case-insensitive");
        Assert.That((p.RequireItemId, p.RequireItemTag, p.RequireItemCount), Is.EqualTo((20300001u, "tag", 4u)));
        Assert.That(t.Get(9), Is.Null);
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
}
