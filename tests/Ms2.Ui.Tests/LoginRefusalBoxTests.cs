using System;
using System.Collections.Generic;
using System.Linq;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every login result's box against CNetworkService::OnLoginResult @0x14153c670 (networkservice.cpp:991-1066), result 1 to 255:
/// the box type, the text's common string, the title and the tag of each branch; and the tag handlers the boxes reach
/// (UIMessageBox::HandleMaintenanceExitButton, HandleAlreadyReconnectButton, HandleExternalBlockButton).
/// </summary>
[NonParallelizable]
public class LoginRefusalBoxTests
{
    Func<string, string> saved;
    [SetUp] public void Keys() { saved = MessageBoxData.CommonByKey; MessageBoxData.CommonByKey = k => "<" + k + ">"; }
    [TearDown] public void Restore() => MessageBoxData.CommonByKey = saved;

    static string Key(int id) => "<" + Ms2.Formats.Text.CommonStringIds.Key(id) + ">";

    /// <summary>The client's branches, written from networkservice.cpp (not from LoginRefusalBox): type, text, title, tag.</summary>
    static (int type, string text, string title, string tag) Client(int result, string message)
    {
        const string exit = "maintenanceExit";
        string restrict = Key(0x219);
        return result switch
        {
            11 => (0, message, Key(0x8c6), ""),                                              // :1004-1008
            12 => (0, message, Key(0x8c6), exit),
            0x18 => (0, Key(0x221), restrict, exit),                                         // :1009-1012
            0x12 => (0, Key(0x21b), restrict, exit),                                         // :1013-1016
            0x15 => (0, Key(0x21e), restrict, exit),                                         // :1017-1020
            0xe => (0, Key(0x21f), restrict, "externalBlock"),                               // :1021-1024
            0xf => (0, Key(0x220), restrict, exit),                                          // :1025-1028
            0x14 => (0, Key(0x223), restrict, "autoExternalBlock"),                          // :1029-1032
            4 or 5 => (0, "<s_alreadyloginuser_response_kick>", "", "alreadyReconnect"),     // :1033-1036, FUN_140cc1fb0 0xace
            0x13 => (0, Key(0x222), restrict, exit),                                         // :1037-1040
            0x19 => (0, "<s_login_err_nxa_ticket>", restrict, exit),                         // :1041-1043
            0x1a => (1, "<s_founderspack_need>", "", ""),                                    // :1044-1051
            _ => (0, Key(result switch { 1 or 2 => 0x210, 3 => 0x211, 6 => 0x213, 8 => 0x216, 10 => 0x215, 16 => 0x218, 22 => 0x224, _ => 0x217 }), "", ""),   // :1052-1060
        };
    }

    [Test]
    public void EveryRefusedResultGetsTheClientsBox()
    {
        var wrong = new List<string>();
        int checkedResults = 0;
        for (int result = 1; result <= 255; result++)
        {
            if (result == 7) continue;   // its text is formatted: the next test
            string message = "server text";
            var box = LoginRefusalBox.Build(result, 0, message);
            var want = Client(result, message);
            if (result == 16) want.text = Ms2.Quest.ClientFormat.Format(want.text, 0);
            var got = (box.Type, box.Msg, box.Title, box.Extra);
            if (got != want) wrong.Add($"{result}: {got} want {want}");
            checkedResults++;
        }
        Assert.That(wrong, Is.Empty, $"{wrong.Count} of {checkedResults} results differ");
        Assert.That(checkedResults, Is.EqualTo(254));
    }

    [Test]
    public void TheMaintenanceTextTurnsItsWrittenLineBreaksIntoLines()
    {
        Assert.That(LoginRefusalBox.Build(11, 0, @"a\nb").Msg, Is.EqualTo("a\nb"));
    }

    [Test]
    public void ARestrictionShowsItsStartEndAndReasonInThePacketsZone()
    {
        MessageBoxData.CommonByKey = k => k == "s_login_err_restrict" ? "{0}-{1}-{2} {3}:{4}:{5} / {6}-{7}-{8} {9}:{10}:{11} {12}" : "<" + k + ">";
        long start = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).ToUnixTimeSeconds();
        long end = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero).ToUnixTimeSeconds();
        var box = LoginRefusalBox.Build(7, 0, "why", start, end, 9 * 3600);
        Assert.That((box.Type, box.Msg, box.Title, box.Extra), Is.EqualTo((0x10, "2026-1-2 12:4:5 / 2026-2-3 13:5:6 why", "<s_login_err_restrict_title>", "")));
    }

    [Test]
    public void TheFoundersBoxBuysOrClosesAndEnterDoesNotAnswer()
    {
        int pressed = -1;
        var box = LoginRefusalBox.Build(0x1a, 0, "", founders: (b, _) => { pressed = b; return true; });
        Assert.That((box.Btn0, box.Btn1, box.EnterAllowed, box.HasCallback), Is.EqualTo(("<s_word_buy>", "<s_gamemode_rpg_close>", false, true)));
        box.Callback(0, box);
        Assert.That(pressed, Is.EqualTo(0));
    }

    [Test]
    public void TheExternalBlockBoxesCarryTypeZero()
    {
        Assert.That(LoginRefusalBox.Build(0xe, 0, "").ArgInt(0, -1), Is.EqualTo(0));
        Assert.That(LoginRefusalBox.Build(0x14, 0, "").ArgInt(0, -1), Is.EqualTo(0));
    }

    [Test]
    public void TheRefusalTagsReachTheirHandlers()
    {
        var c = new MessageBoxController();
        var got = new List<string>();
        c.MaintenanceExit += () => got.Add("exit");
        c.AlreadyReconnect += () => got.Add("reconnect");
        c.ExternalBlock += (url, quit) => got.Add($"{url} {quit}");
        c.data = LoginRefusalBox.Build(4, 0, "");
        Assert.That(c.HandlePlainTag(1, "alreadyReconnect"), Is.True);
        Assert.That(c.HandlePlainTag(0, "alreadyReconnect"), Is.True);
        c.data = LoginRefusalBox.Build(12, 0, "");
        Assert.That(c.HandlePlainTag(0, "maintenanceExit"), Is.True);
        c.data = LoginRefusalBox.Build(0xe, 0, "");
        Assert.That(c.HandlePlainTag(0, "externalBlock"), Is.True);
        c.data = LoginRefusalBox.Build(0x14, 0, "");
        Assert.That(c.HandlePlainTag(1, "autoExternalBlock"), Is.True);
        Assert.That(got, Is.EqualTo(new[] { "reconnect", "exit", "http://maplestory2.nexon.com/Auth/Certify True" }));
    }
}
