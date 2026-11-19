using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>vfunc_84's routing of a user's chat line (MessengerBrowser.OnUserChat), TextToHtml's helpers and the
/// report log.</summary>
public class ChatTests
{
    static ChatMessage Msg(int type, string text = "hi", long chr = 22, int commonId = 0, byte img = 0) =>
        new(11, chr, "name", false, commonId, text, type, false, 0, "win", 0, 0, img);

    sealed class Sinks
    {
        public readonly List<string> Calls = new();
        public MessengerBrowser Browser(bool wedding = false) => new()
        {
            ShowSystemNotice = (t, type, log) => Calls.Add($"notice {type} {log} {t}"),
            SystemSound = s => Calls.Add($"sound {s}"),
            WhisperNotify = (c, t, w) => Calls.Add($"whisper {c} {t} {w}"),
            UserCommand = t => Calls.Add($"command {t}"),
            FieldStringNotice = () => Calls.Add("field"),
            ChatBalloon = (n, t, type) => Calls.Add($"balloon {type}"),
            FeatureEnabled = id => wedding && id == MessengerBrowser.WeddingFeature,
            ServerTime = () => 100,
        };
    }

    [Test]
    public void Routing()
    {
        var s = new Sinks();
        var b = s.Browser();
        b.OnUserChat(Msg(3));
        Assert.That(s.Calls, Is.EqualTo(new[] { "whisper 22 hi win", $"sound {MessengerBrowser.WhisperSound}", "balloon 3" }));
        Assert.That(b.Logs.Single().Kind, Is.EqualTo(4));

        s.Calls.Clear();
        b.OnUserChat(Msg(0x11));
        Assert.That(s.Calls, Is.EqualTo(new[] { "notice 1 True hi", "balloon 17" }));

        s.Calls.Clear();
        b.OnUserChat(Msg(0xd, commonId: 0x35));
        Assert.That(s.Calls[0], Is.EqualTo("field"));

        s.Calls.Clear();
        b.OnUserChat(Msg(0x12));
        Assert.That(s.Calls, Is.EqualTo(new[] { "balloon 18", $"notice {SystemTextController.GuildMegaphone} False ", $"sound {MessengerBrowser.GuildMegaphoneSound}" }));
        Assert.That(b.Logs.Last().Kind, Is.EqualTo(0x18));

        s.Calls.Clear();
        int logs = b.Logs.Count;
        b.OnUserChat(Msg(10, "/cmd"));
        Assert.That(s.Calls, Is.EqualTo(new[] { "command /cmd" }));
        b.OnUserChat(Msg(0xf));                        // the announcement is not ported: dropped
        b.OnUserChat(Msg(0x19));                       // no wedding feature: dropped
        b.OnUserChat(Msg(0, "<FONT size='12'></FONT>"));   // empty once the tags are gone
        Assert.That(b.Logs.Count, Is.EqualTo(logs));
    }

    [Test]
    public void BlocksEndIt()
    {
        var s = new Sinks();
        var b = s.Browser();
        b.IsBlinded = n => n == "name";
        b.OnUserChat(Msg(0));
        b.IsBlinded = _ => false;
        b.IsBlockedBuddy = (n, c) => c == 22;
        b.OnUserChat(Msg(0));
        Assert.That(b.Logs, Is.Empty);
        b.OnUserChat(Msg(4));                          // a whisper sent is not checked against the block list
        Assert.That(b.Logs.Single().Kind, Is.EqualTo(4));
        b.WhisperBlockedReply = n => s.Calls.Add($"reply {n}");
        b.IsBlockedBuddy = (_, _) => false;
        s.Calls.Clear();
        b.OnUserChat(Msg(3));
        Assert.That(s.Calls, Is.EqualTo(new[] { "reply name" }));
    }

    [Test]
    public void WeddingLineNeedsTheFeature()
    {
        var b = new Sinks().Browser(wedding: true);
        bool read = false;
        b.OnUserChat(Msg(0x19), () => { read = true; return null; });
        Assert.That(read, Is.True);
        Assert.That(b.Logs.Single().Kind, Is.EqualTo(0x17));
    }

    [Test]
    public void RemoveHtmlTag()
    {
        Assert.That(MessengerBrowser.RemoveHtmlTag("a<FONT size='12'>b</font>c", "FONT"), Is.EqualTo("abc"));
        Assert.That(MessengerBrowser.RemoveHtmlTag("a<p>b", "P"), Is.EqualTo(""));            // no close
        Assert.That(MessengerBrowser.RemoveHtmlTag("a<p b", "P"), Is.EqualTo(""));            // no '>'
        Assert.That(MessengerBrowser.RemoveHtmlTag("plain", "P"), Is.EqualTo("plain"));
        Assert.That(MessengerBrowser.RemoveChatTags("<P><FONT>x</FONT></P><BR></BR>"), Is.EqualTo("x"));
    }

    [Test]
    public void ReportLine()
    {
        var b = new MessengerBrowser { ServerTime = () => 5 };
        Assert.That(b.ReportLine("n", "h", 0x14), Is.False);
        Assert.That(b.ReportLine("n", "h", 1), Is.True);
        Assert.That(b.TimedLogs.Single(), Is.EqualTo((5L, "[Normal][n] h")));
        for (int i = 0; i < 60; i++) b.ReportLine("n", "h" + i, 0x1a);
        Assert.That(b.TimedLogs.Count, Is.EqualTo(0x33));
        Assert.That(b.Reports.Count, Is.EqualTo(61));
    }

    [Test]
    public void HtmlHelpers()
    {
        Assert.That(ChatHtml.Kind(0x10), Is.EqualTo(0x17));
        Assert.That(ChatHtml.Kind(0xc), Is.EqualTo(0x16));
        Assert.That(ChatHtml.Kind(7), Is.EqualTo(2));
        Assert.That(ChatHtml.Kind(0), Is.EqualTo(1));
        Assert.That(ChatHtml.FixColor("<FONT COLOR=\"#FFFFFE\">", 0xabc), Is.EqualTo("<FONT COLOR='#ABC'>"));
        Assert.That(ChatHtml.FixColor("color=\"#fffffe\"", 0xabc), Is.EqualTo("color=\"#fffffe\""));
        Assert.That(ChatHtml.ReplaceFontSize(20, "size='12' SIZE=\"18\" size='19'"), Is.EqualTo("size='20' SIZE=\"20\" size='19'"));
        Assert.That(ChatHtml.Wcstoi64Hex(" 0xff"), Is.EqualTo(255));
        Assert.That(ChatHtml.Wcstoi64Hex("-10"), Is.EqualTo(-16));
        Assert.That(ChatHtml.Wcstoi64Hex("zz"), Is.EqualTo(0));
        Assert.That(ChatHtml.Wcstoi64Hex("ffffffffffffffffff"), Is.EqualTo(long.MaxValue));
        var t = GameTables.Empty;
        Assert.That(ChatHtml.OwnColor(t, false, false, 7), Is.EqualTo(7));
        Assert.That(ChatHtml.OwnColor(t, true, true, 7), Is.EqualTo(7));
        Assert.That(ChatHtml.HasImage(t, Msg(0, img: 1)), Is.EqualTo(t.MentoringShowIcon));
        Assert.That(ChatHtml.HasImage(t, Msg(5, img: 1)), Is.False);
        Assert.That(ChatHtml.HasImage(t, Msg(0, img: 0)), Is.False);
        Assert.That(ChatHtml.Line(new ChatHtml.Context(), Msg(0x15), "x", 12), Is.EqualTo(""));
    }
}
