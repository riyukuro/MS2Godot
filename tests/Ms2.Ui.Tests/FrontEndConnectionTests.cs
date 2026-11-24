using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The front end's connection errors (window messages 0x465 and 0x467) and a refused game start (OnLoginToGame).</summary>
public class FrontEndConnectionTests
{
    /// <summary>The client's reason switch, code by code (windowservice.cpp:487-531).</summary>
    static readonly (int Code, int Id)[] ClientReasons =
    {
        (0x2714, 0xd29), (0x271d, 0xd2a), (0x271e, 0xd2b), (0x2726, 0xd2c), (0x2728, 0xd2d), (0x2733, 0xd2e), (0x2734, 0xd2f),
        (0x2735, 0xd30), (0x2736, 0xd28), (0x2737, 0xd31), (0x2738, 0xd32), (0x2739, 0xd28), (0x273a, 0xd28), (0x273b, 0xd28),
        (0x273c, 0xd28), (0x273d, 0xd28), (0x273e, 0xd28), (0x273f, 0xd28), (0x2740, 0xd33), (0x2741, 0xd28), (0x2742, 0xd34),
        (0x2743, 0xd35), (0x2744, 0xd36), (0x2745, 0xd37), (0x2746, 0xd38), (0x2747, 0xd39), (0x2748, 0xd3a), (0x2749, 0xd28),
        (0x274a, 0xd3b), (0x274c, 0xd3c), (0x274d, 0xd3d), (0x2750, 0xd3e), (0x2751, 0xd3f), (0x2753, 0xd40), (0x276b, 0xd28),
        (0x276c, 0xd28), (0x276d, 0xd28), (0x2775, 0xd41), (0x277d, 0xd28), (0x2af9, 0xd42), (0x2afa, 0xd43), (0x2afb, 0xd28),
        (0x2afc, 0xd28),
    };

    [Test]
    public void EverySocketErrorHasTheClientsReason()
    {
        Assert.That(FrontEndConnection.ReasonCodes, Is.EqualTo(ClientReasons.Select(r => r.Code).ToArray()), "the whole list, in the switch's order");
        var wrong = ClientReasons.Where(r => FrontEndConnection.ReasonId(r.Code) != r.Id).ToArray();
        TestContext.WriteLine($"socket error reasons: {ClientReasons.Length} codes, {wrong.Length} differ");
        Assert.That(wrong, Is.Empty);
        // a code the switch does not name keeps nMsgId 0 (s_empty_string): 0, the clean close, and codes between the named ones
        foreach (int code in new[] { 0, 1, 0x274b, 0x274e, 0x2752, 0x2afd, -1 })
            Assert.That(FrontEndConnection.ReasonId(code), Is.EqualTo(0), $"code {code}");
    }

    [Test]
    public void TheDisconnectBoxShowsTheReasonThenTheCode()
    {
        // windowservice.cpp:532-541: the reason, " : " only when it is not empty, then the code in decimal, into 0x20f's {0}
        string Common(int id) => id switch { 0x20f => "Disconnected ({0})", 0xd38 => "reset by peer", _ => "" };
        Assert.That(FrontEndConnection.Disconnected(10054, Common), Is.EqualTo("Disconnected (reset by peer : 10054)"));
        Assert.That(FrontEndConnection.Disconnected(12345, Common), Is.EqualTo("Disconnected (12345)"), "no reason: the code alone");
        Assert.That(FrontEndConnection.ConnectFailed(10061, id => id == 0x20e ? "No connection [{0}]" : ""), Is.EqualTo("No connection [10061]"));
    }

    [Test]
    public void TheCodeIsTheSocketErrorAndACleanCloseIsZero()
    {
        // CGameSocket::OnClose: FD_CLOSE's error; a clean close by the peer is 0, which shows nothing (gamesocket.cpp:226)
        var reset = new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset);
        Assert.That(FrontEndConnection.CodeOf(reset), Is.EqualTo(10054));
        Assert.That(FrontEndConnection.CodeOf(new IOException("read failed", reset)), Is.EqualTo(10054), "a socket error under an IO error");
        Assert.That(FrontEndConnection.CodeOf(new IOException("closed", new EndOfStreamException("connection closed"))), Is.EqualTo(0));
        Assert.That(FrontEndConnection.CodeOf(new EndOfStreamException("connection closed")), Is.EqualTo(0));
        Assert.That(FrontEndConnection.CodeOf(new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)), Is.EqualTo(10061));
    }

    [Test]
    public void ARefusedStartShowsTheClientsBox()
    {
        // OnLoginToGame (networkservice.cpp:910-925): 0x1a the text titled 0x8c6 with the maintenanceExit tag, 0x19 the same
        // without a tag, "\n" pairs made line breaks; any other result GetFieldMoveErrorMessage's text; 0 no box
        string ByKey(string k) => k == FieldMoveError.Key(3) ? "move error three" : k == FieldMoveError.DefaultKey ? "move error {0}" : null;
        Assert.That(StartRefusal.Build(0, "x", ByKey), Is.Null);
        var m = StartRefusal.Build(StartRefusal.Maintenance, "down\\nnow", ByKey);
        Assert.That(m.Msg, Is.EqualTo("down\nnow"));
        Assert.That(m.Extra, Is.EqualTo(LoginRefusalBox.MaintenanceExitTag));
        Assert.That(m.Title, Is.EqualTo(MessageBoxData.Common(0x8c6)));
        Assert.That(m.Type, Is.EqualTo(0));
        var n = StartRefusal.Build(StartRefusal.Notice, "a\\nb", ByKey);
        Assert.That(n.Msg, Is.EqualTo("a\nb"));
        Assert.That(n.Extra, Is.EqualTo(""));
        Assert.That(n.Title, Is.EqualTo(MessageBoxData.Common(0x8c6)));
        Assert.That(StartRefusal.Build(3, "server text", ByKey).Msg, Is.EqualTo(FieldMoveError.Text(3, ByKey)), "other results: the move error's text, not the packet's");
    }

    [Test]
    public void EveryConnectionStringIsInTheData()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        foreach (var (label, dir) in new[] { ("MS2_DATA", d), ("MS2_GMS2_DATA", Environment.GetEnvironmentVariable("MS2_GMS2_DATA")) })
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { TestContext.WriteLine($"{label}: not set"); continue; }
            var common = new Ms2.Formats.World.GameStrings(new PackSet(dir), "en").Common();
            var ids = new[] { FrontEndConnection.ConnectFailedId, FrontEndConnection.DisconnectedId }
                .Concat(ClientReasons.Select(r => r.Id)).Distinct().ToArray();
            var keys = ids.Select(Ms2.Formats.Text.CommonStringIds.Key).ToArray();
            Assert.That(keys, Has.None.Null, "every id has a key");
            var missing = keys.Where(k => string.IsNullOrEmpty(common(k))).ToArray();
            TestContext.WriteLine($"{label}: {keys.Length} connection strings, {missing.Length} missing: {string.Join(", ", missing)}");
            Assert.That(missing, Is.Empty, label);
            Assert.That(common("s_login_err_connect"), Does.Contain("{0}"), label + ": the connect box takes the code");
            Assert.That(common("s_login_err_disconnected"), Does.Contain("{0}"), label + ": the disconnect box takes reason and code");
        }
    }
}
