using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class SmartPushTests
{
    const string Xml = "<ms2>" +
        "<push id='1' content='a' actionType='additionalEffect' actionValue='70000001' requireMerat='0' imgPath='x.png'/>" +
        "<push id='2' content='b' actionType='AUTOINTERACTION' actionValue='5' requireMerat='300' requireItem='20300001:tag,1,4'/>" +
        "<push id='3' content='c' actionType='other' requireItem='1,2'/>" +
        "<push id='4' content='d' actionType='additionalEffect' feature='Off'/>" +
        "<push id='1' content='again' actionType='additionalEffect' actionValue='70000002'/>" +
        "</ms2>";

    static SmartPushTable Table() => new(XDocument.Parse(Xml), e => (string)e.Attribute("feature") == null);

    [Test]
    public void TableParsesRowsLikeCSmartPushTableLoad()
    {
        var t = Table();
        Assert.That(t.Count, Is.EqualTo(2), "id 3 (two item parts) and id 4 (unavailable) are dropped");
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
        var strings = new Dictionary<string, string> { ["s_smartpush_active"] = "{0} active", ["s_err_lack_merat_ask"] = "Charge?" };
        return new SmartPushNotices(Table(), k => strings[k], (id, lv) => $"effect{id}L{lv}")
        {
            SystemNotice = n.Add, ShowBox = b.Add, SendAnswer = (i, c, a) => s.Add((i, c, a)),
        };
    }

    [Test]
    public void PositionalPushSaysItsEffectIsActiveAtLevelOne()
    {
        var p = Make(out var notices, out _, out _);
        p.OnPositional(1, 0);      // vfunc_4 (smartpush.cpp:309): type 1 -> s_smartpush_active with the effect's level 1 name
        p.OnPositional(2, 0);      // autoInteraction: nothing
        p.OnPositional(99, 0);     // no such push: nothing (FUN_140dd86a0 Lookup fails)
        Assert.That(notices, Is.EqualTo(new[] { "effect70000002L1 active" }));
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
