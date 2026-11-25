using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// A refused login and the next try on the login dialog's movie (uilogindialog, needs MS2_DATA): the login (Enter ->
/// onEnterKey -> OnConnect) pops the dialog down (UISceneLogin::FUN_141acfad0 -> FUN_1419e28a0); the refusal ends in
/// LoginConnectFailure.AfterRefusal (CNetworkService::OnLoginResult -> UISceneLogin::FUN_141acf470), after which the same
/// dialog sends OnConnect again with the typed id and password.
/// </summary>
public class LoginRetryTests
{
    static MovieSource src;

    static MovieSource Source()
    {
        if (src == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            var packs = new PackSet(d);
            src = TestData.Movies(packs);
        }
        return src;
    }

    static (LoginDialogController, Player) Open()
    {
        var c = new LoginDialogController();
        var (rt, p) = Source().Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, new UiModel(null));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        Ticks(p, 30);
        return (c, p);
    }

    static void Ticks(Player p, int n) { for (int i = 0; i < n; i++) p.Tick(); }

    static void Type(Player p, string s)
    {
        foreach (var ch in s) { p.Key(0, ch, true, true); p.Key(0, 0, false, true); }
    }

    /// <summary>The id and password typed, then Enter: OnConnect; the scene pops the dialog down, as the host does on it.</summary>
    static void TypeAndLogin(LoginDialogController c, Player p)
    {
        Type(p, "user");
        c.TabKey(); Ticks(p, 3);
        Type(p, "pw");
        c.EnterKey(); Ticks(p, 3);
    }

    [Test]
    public void ARefusedLoginCanBeTriedAgain()
    {
        var (c, p) = Open();
        int connects = 0;
        c.Connect += () => { connects++; c.Popdown(); };
        TypeAndLogin(c, p);
        Assert.That(connects, Is.EqualTo(1), "the first try");
        Ticks(p, 60);   // the popdown plays out while the server answers
        int boxes = 0;
        LoginConnectFailure.AfterRefusal(() => boxes++, c.ShowConnectFailure, null);
        Ticks(p, 60);
        Assert.That(boxes, Is.EqualTo(1), "the refusal's box");
        Assert.That(c.Id, Is.EqualTo("user"), "the id stays typed");
        c.EnterKey(); Ticks(p, 3);
        Assert.That(connects, Is.EqualTo(2), "the second try sends OnConnect again");
    }

    [Test]
    public void WithoutTheFailureStateThePoppedDownDialogTakesNoSecondTry()
    {
        var (c, p) = Open();
        int connects = 0;
        c.Connect += () => { connects++; c.Popdown(); };
        TypeAndLogin(c, p);
        Ticks(p, 60);
        // the box alone, as the flow did before: the dialog stays popped down
        c.EnterKey(); Ticks(p, 3);
        Assert.That(connects, Is.EqualTo(1), "no second OnConnect from a popped-down dialog");
    }
}
