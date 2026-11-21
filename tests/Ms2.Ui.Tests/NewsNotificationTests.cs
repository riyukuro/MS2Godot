using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CSceneStateGame::OnNewsNotification (scenestategame.cpp:3789) and the notice dialog's outbound request
/// (CWebBrowserService::SendNewsNotification, webbrowserservice.cpp:359). The browser pages are not drawn.</summary>
public class NewsNotificationTests
{
    static NewsNotification Msg(string url, bool inGame, int type, byte mode = 0) => new(mode, url, inGame, type);

    [Test]
    public void InGameAddressGoesToTheDialogOfItsType()
    {
        var r = new NewsNotificationRouter();
        var got = new List<string>();
        r.Register(NewsNotificationRouter.Notice, got.Add);
        r.Handle(Msg("http://a", true, 2));
        r.Handle(Msg("http://b", true, 1));          // the visitors book has no dialog here: only the state
        Assert.That(got, Is.EqualTo(new[] { "http://a" }));
        Assert.That(r.UrlOf(2), Is.EqualTo("http://a"));
        Assert.That(r.UrlOf(1), Is.EqualTo("http://b"));
        Assert.That(r.UrlOf(0), Is.Null);
    }

    [Test]
    public void ClearInGameFlagTakesTheTokenPathAndOtherTypesDoNothing()
    {
        var r = new NewsNotificationRouter();
        var tok = new List<(int, string)>();
        var opened = 0;
        r.TokenRequested += (t, u) => tok.Add((t, u));
        r.Register(2, _ => opened++);
        r.Handle(Msg("u", false, 2));
        r.Handle(Msg("v", true, 3));                 // no case for type 3
        r.Handle(Msg("w", true, 2, mode: 1));        // only mode 0
        Assert.That(tok, Is.EqualTo(new[] { (2, "u") }));
        Assert.That(opened, Is.EqualTo(0));
        Assert.That(r.UrlOf(3), Is.Null);
        Assert.That(r.UrlOf(2), Is.Null);
    }

    [Test]
    public void RequestWritesZeroFlagAndBrowserId()
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        new NewsNotificationRequest(true, 2).Write(w);
        Assert.That(w.ToArray(), Is.EqualTo(new byte[] { 0, 1, 2, 0, 0, 0 }));
        Assert.That(new NewsNotificationRequest(true, 2).Op, Is.EqualTo(Ms2.Net.Outbound.NewsNotification));
    }
}
