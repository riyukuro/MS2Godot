using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>OnNotice's routing (NoticeController), the trigger server strings and GetLocalizedText.</summary>
public class NoticeTests
{
    sealed class Sinks
    {
        public readonly List<string> Calls = new();
        public NoticeController Controller()
        {
            return new NoticeController
            {
                ShowMessage = (t, type, o) => Calls.Add($"text {type} {o} {t}"),
                ChatLine = (t, k) => Calls.Add($"chat 0x{k:x} {t}"),
                MessageBox = (t, tag) => Calls.Add($"box '{tag}' {t}"),
                SystemSound = k => Calls.Add($"sound {k}"),
                ForcedQuit = () => Calls.Add("quit"),
                MaintenanceExit = () => Calls.Add("maintenance"),
            };
        }
    }

    static NoticeMessage Notice(byte kind, ushort flags, string text, ushort arg = 0, int top = 0) =>
        new(kind, flags, new StringIdText(false, 0, 0, Array.Empty<string>(), text), arg, top);

    static List<string> Route(NoticeMessage n, bool withSystemText = true)
    {
        var s = new Sinks();
        var c = s.Controller();
        if (!withSystemText) c.ShowMessage = null;
        c.OnNotice(n);
        return s.Calls;
    }

    [Test]
    public void FlagsPickThePlaces()
    {
        Assert.That(Route(Notice(4, 0, "a")), Is.EqualTo(new[] { "chat 0x14 a" }));
        Assert.That(Route(Notice(4, 2 | 1, "a")), Is.Empty);
        Assert.That(Route(Notice(4, 0x20 | 1, "bgm")), Is.EqualTo(new[] { "sound bgm" }));
        Assert.That(Route(Notice(4, 4 | 1, "a")), Is.EqualTo(new[] { "text 6 0 a", "chat 0x14 a" }));
        Assert.That(Route(Notice(4, 0x10, "a", 3000)), Is.EqualTo(new[] { "text 9 3000 a" }));
        Assert.That(Route(Notice(4, 0x400, "a")), Is.EqualTo(new[] { "text 2 0 a", "chat 0x14 a" }));
        Assert.That(Route(Notice(4, 0x100 | 1, "a")), Is.EqualTo(new[] { "chat 0x1d a" }));
        Assert.That(Route(Notice(4, 0x40, "a")), Is.EqualTo(new[] { "box '' a" }));
        Assert.That(Route(Notice(4, 0x80, "a")), Is.EqualTo(new[] { "box 'maintenanceExit' a", "maintenance" }));
        Assert.That(Route(Notice(5, 0x40, "bye")), Is.EqualTo(new[] { "quit", "box '' bye" }));
        Assert.That(Route(Notice(5, 0x41, "bye")), Is.Empty);
        Assert.That(Route(Notice(3, 0, "a")), Is.Empty);
    }

    [Test]
    public void TopLayerWithoutItsWindowShowsNothing() =>
        Assert.That(Route(Notice(4, 0x200, "a", 0, 5000)), Is.Empty);

    [Test]
    public void SystemNoticeNeedsTheSystemText() =>
        Assert.That(Route(Notice(4, 4 | 0x400, "a"), withSystemText: false), Is.Empty);

    [Test]
    public void TriggerKeyMissing() =>
        Assert.That(Route(Notice(4, 0, "$nope$")), Is.EqualTo(new[] { "chat 0x14 NO KEY : $nope$" }));

    [Test]
    public void TextKinds()
    {
        var c = new NoticeController();
        Assert.That(c.Text(new StringIdText(false, 0, 0, Array.Empty<string>(), "x")), Is.EqualTo("x"));
        Assert.That(c.Text(new StringIdText(false, 3, 0, Array.Empty<string>(), "x")), Is.EqualTo(""));
        Assert.That(c.Text(new StringIdText(true, 2, 5, new[] { "a" }, "")), Is.EqualTo(""));
    }

    [Test]
    public void LocalizedText()
    {
        const string xml = "<ms2><text KR=\"k\" EN=\"\" DE=\"d\"/></ms2>";
        Assert.That(ServerStringGroup.LocalizedText(xml, "KR", 0), Is.EqualTo("k"));
        Assert.That(ServerStringGroup.LocalizedText(xml, "EN", 2), Is.EqualTo("d"));     // EN empty: the region's EN, DE, PT
        Assert.That(ServerStringGroup.LocalizedText(xml, "JP", 1), Is.EqualTo(xml));     // no JP text: unchanged
        Assert.That(ServerStringGroup.LocalizedText("plain", "KR", 0), Is.EqualTo("plain"));
        Assert.That(ServerStringGroup.LocalizedText("<ms2><bad", "KR", 0), Is.EqualTo("<ms2><bad"));
    }

    [Test]
    public void TimePrefixNeedsFlag8()
    {
        var c = new NoticeController();
        Assert.That(c.TimePrefix(1, "a"), Is.EqualTo("a"));
    }

    [Test]
    public void TriggerStringsFromData()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var tables = new GameTables(new PackSet(d), "kr");
        var g = tables.TriggerStrings;
        Assert.That(g.Count, Is.GreaterThan(1000));
        string s = g.GetString("$02000313_BF__BOSSSPAWN__43$");
        Assert.That(s, Does.Not.StartWith("NO KEY"));
        Assert.That(s, Does.Not.Contain("$map"));                      // ReplaceObjectWords at load
        Assert.That(tables.Common(0xa9), Is.Not.Empty);
        var c = new NoticeController { Tables = tables, ServerTime = () => 0 };
        Assert.That(c.TimePrefix(8, "x"), Does.Contain("x").And.Not.EqualTo("x"));
    }
}
