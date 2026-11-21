using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The login dialog's movie (uilogindialog) with its fields typed into as the window's keys reach a movie (needs MS2_DATA).</summary>
public class LoginDialogTypingTests
{
    static MovieSource src;

    static MovieSource Source()
    {
        if (src == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            var packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return src;
    }

    const string Specials = "^!@#$%&*()_+-=[]{};:'\",.<>/?\\|`~";

    static (LoginDialogController, Player) Open()
    {
        var c = new LoginDialogController();
        var (rt, p) = Source().Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, new UiModel(null));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return (c, p);
    }

    /// <summary>Each character as a key without a Flash key code, the shape of the window's composed key (a dead key
    /// and the next key: Godot's X11 IME sends the composed character with no keycode).</summary>
    static void Type(Player p, string s)
    {
        foreach (var ch in s) { p.Key(0, ch, true, true); p.Key(0, 0, false, true); }
    }

    [Test]
    public void SpecialCharactersTypeIntoBothFields()
    {
        var (c, p) = Open();
        Assert.That(WindowIme.TakesText(p.Focus), Is.True, "initData focuses the id field: the window's IME goes on");
        Type(p, Specials);
        c.TabKey();
        for (int i = 0; i < 3; i++) p.Tick();
        Assert.That(WindowIme.TakesText(p.Focus), Is.True, "the password field takes the window's IME too (dead keys)");
        Type(p, Specials[..16]);
        Assert.That(c.Id, Is.EqualTo(Specials[..32]), "maxChars 32");
        Assert.That(c.Password, Is.EqualTo(Specials[..16]), "maxChars 16");
    }

    [Test]
    public void TheWindowImeFollowsTheLoginFieldsFocus()
    {
        var (c, p) = Open();
        var asked = new List<bool>();
        var ime = new WindowIme(asked.Add);
        bool typing = false;
        void Frame() => ime.Sync(WindowIme.TakesText(p.Focus), () => typing);

        Frame(); Frame();
        Assert.That(asked, Is.EqualTo(new[] { true }), "on once, with the id field focused");
        c.TabKey();
        for (int i = 0; i < 3; i++) p.Tick();
        Frame();
        Assert.That(asked, Is.EqualTo(new[] { true }), "still on in the password field");

        ime.WindowFocusChanged();               // the window lost the focus: the engine turned its IME off
        Frame();
        Assert.That(asked, Is.EqualTo(new[] { true, true }), "asked again");

        p.SetFocus(null, null);
        Frame();
        Assert.That(asked, Is.EqualTo(new[] { true, true, false }), "off with no field focused");

        ime.Sync(true, () => typing); typing = true; ime.Sync(false, () => typing);
        Assert.That(asked, Is.EqualTo(new[] { true, true, false, true }), "an engine text control with the focus keeps its own IME on");
    }

    /// <summary>The engine's X11 IME loses the input focus to the main window once after it is first mapped and on a
    /// mouse press (a click on the field): the window is asked again then, and not on other frames.</summary>
    [Test]
    public void TheWindowIsAskedAgainAfterASwitchOnAndAMousePress()
    {
        var asked = new List<bool>();
        var ime = new WindowIme(asked.Add);
        bool Never() => false;

        ime.Sync(true, Never);
        for (int i = 1; i < WindowIme.ConfirmFrames; i++) ime.Sync(true, Never);
        Assert.That(asked, Is.EqualTo(new[] { true }), "not before the confirm frame");
        ime.Sync(true, Never);
        Assert.That(asked, Is.EqualTo(new[] { true, true }), "once more after the switch on");
        for (int i = 0; i < 100; i++) ime.Sync(true, Never);
        Assert.That(asked, Has.Count.EqualTo(2), "then quiet");

        ime.Sync(true, Never, mousePressed: true);
        Assert.That(asked, Is.EqualTo(new[] { true, true, true }), "on the frame a mouse button went down");
        ime.Sync(false, Never, mousePressed: true);
        ime.Sync(false, Never, mousePressed: true);
        Assert.That(asked, Is.EqualTo(new[] { true, true, true, false }), "off: a press asks nothing");
        for (int i = 0; i < 100; i++) ime.Sync(false, Never);
        Assert.That(asked, Has.Count.EqualTo(4), "no confirm left after a switch off");
    }
}
