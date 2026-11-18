using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The message box movie (uimessagebox) run by its own ActionScript with UIMessageBox's C++ side and the message
/// box queue (needs MS2_DATA).</summary>
[NonParallelizable]
public class MessageBoxTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;
    static IEnumerable<object> errors;

    sealed class Listener : IMessageBoxListener
    {
        public readonly List<(int, string)> Answers = new();
        public bool Handled = true;
        public Action OnAnswer;
        public bool OnMessageBox(MessageBoxController sender, int button, string tag)
        {
            Answers.Add((button, tag));
            OnAnswer?.Invoke();
            return Handled;
        }
    }

    static (UiHost, MessageBoxController) Load()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var common = new GameStrings(packs, "en").Common();
        MessageBoxData.CommonByKey = common;
        var c = new MessageBoxController();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        errors = rt.Errors;
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        MessageBoxQueue.Clear(0);
        MessageBoxQueue.Layer = c;
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    [TearDown]
    public void Drop()
    {
        foreach (var e in errors ?? Array.Empty<object>()) TestContext.WriteLine("  error: " + e);
        MessageBoxQueue.Clear(0);
        MessageBoxQueue.Layer = null;
    }

    /// <summary>MessageBoxFactory.getCurMsgBox: the last of m_msgBoxArray.</summary>
    static object Box(UiHost h)
    {
        var a = h.Get(h.Entity, "m_msgBoxArray");
        int n = Rt.ToInt32(h.Get(a, "length"));
        return n == 0 ? null : h.Get(a, (n - 1).ToString());
    }

    static void Click(UiHost h, string button)
    {
        var b = ((AsDisplayObject)h.Child(button, Box(h))).GetBounds(null);
        h.Player.Click(b.X + b.W / 2, b.Y + b.H / 2);
        for (int i = 0; i < 5; i++) h.Player.Tick();
    }

    /// <summary>The imageYesNo box's text: m_contents' textField (MessageBoxImageYesNo.setBoxType).</summary>
    static string Text(UiHost h) => Rt.ToString(h.Get(h.Invoke(h.Child("m_contents", Box(h)), "getChildByName", "textField"), "text"));

    [Test]
    public void TypesTakeTheirButtonPresets()
    {
        MessageBoxData.CommonByKey = k => "#" + k;
        Assert.That(new MessageBoxData(1) is var y && (y.Btn0, y.Btn1) == ("#s_word_yes", "#s_word_no"));
        Assert.That(new MessageBoxData(0) is var o && (o.Btn0, o.Btn1, o.Sound) == ("#s_word_ok", "", "system_notice_error"));
        Assert.That(new MessageBoxData(0x17) is var x && (x.Btn0, x.Btn1, x.Sound) == ("#s_word_exit_yes", "#s_word_exit_no", "system_notice_confirm"));
        var obj = new MessageBoxData(0x18) { Extra = "t" }.FillObj(true);
        Assert.That(obj["boxType"], Is.EqualTo("vertical"));
        Assert.That(obj["extra"], Is.EqualTo("t"));
        Assert.That(obj.ContainsKey("textAlign"), Is.False, "align 4 sends none");
    }

    [Test]
    public void ExitBoxOpensAndYesQuits()
    {
        var (h, c) = Load();
        Assert.That(Rt.ToBoolean(h.Get(h.Entity, "visible")), Is.False, "configUI hides the layer");
        int quit = 0;
        c.GameExit += () => quit++;
        var box = MessageBoxQueue.ShowMessageBox("Exit the game?", 0x17, null, "gameexit");
        h.Player.Tick();
        Assert.That(c.CurrentBox, Is.SameAs(box));
        Assert.That(Rt.ToBoolean(h.Get(h.Entity, "visible")), Is.True);
        Assert.That(Box(h), Is.Not.Null, "the factory made msgbox_imageYesNo");
        TestContext.WriteLine("box: " + Box(h) + " msg: " + Text(h));
        Assert.That(Text(h), Does.Contain("Exit the game?"));
        Click(h, "m_button1");
        Assert.That(quit, Is.EqualTo(0), "no keeps playing");
        Assert.That(c.IsIdle, Is.True, "HandleGameexitButton(1) uses the box up and it closes");
        MessageBoxQueue.ShowMessageBox("Exit the game?", 0x17, null, "gameexit");
        Click(h, "m_button0");
        Assert.That(quit, Is.EqualTo(1));
    }

    [Test]
    public void ListenerAnswerClosesAndTheQueuedBoxReturns()
    {
        var (h, c) = Load();
        var first = new Listener();
        var second = new Listener();
        first.OnAnswer = MessageBoxQueue.Next;          // UIGameOptionDialog::OnMessage calls FUN_14151f200 first
        second.OnAnswer = MessageBoxQueue.Next;
        var a = MessageBoxQueue.ShowMessageBox("first", 1, first, "OnSave");
        var b = MessageBoxQueue.ShowMessageBox("second", 1, second, "OnClose");
        Assert.That(c.CurrentBox, Is.SameAs(b));
        Assert.That(MessageBoxQueue.Count, Is.EqualTo(1), "the replaced box waits");
        Click(h, "m_button1");
        Assert.That(second.Answers, Is.EqualTo(new[] { (1, "OnClose") }));
        Assert.That(c.CurrentBox, Is.SameAs(a), "FUN_14151f200 shows the queued box again");
        Click(h, "m_button0");
        Assert.That(first.Answers, Is.EqualTo(new[] { (0, "OnSave") }));
        Assert.That(c.IsIdle, Is.True);
        Assert.That(Rt.ToBoolean(h.Get(h.Entity, "visible")), Is.False);
    }

    [Test]
    public void SameTagIsNotOpenedTwice()
    {
        var (h, c) = Load();
        var a = MessageBoxQueue.ShowMessageBox("x", 1, null, "t");
        MessageBoxQueue.ShowMessageBox("y", 1, null, "t");
        Assert.That(c.CurrentBox, Is.SameAs(a));
        Assert.That(MessageBoxQueue.Count, Is.EqualTo(0));
    }

    [Test]
    public void ResetBoxHasThreeButtonsAndKeysReachTheMovie()
    {
        var (h, c) = Load();
        var l = new Listener { OnAnswer = MessageBoxQueue.Next };
        var box = MessageBoxQueue.Create(0x18, "", l, "");
        box.SetButtonLabels("All", "This page", "Cancel");
        box.Title = "Reset";
        MessageBoxQueue.Show(box);
        h.Player.Tick();
        foreach (var n in new[] { "m_button0", "m_button1", "m_button2" })
            TestContext.WriteLine(n + ": " + Rt.ToString(h.Get(h.Child(n, Box(h)), "label")));
        Assert.That(Rt.ToString(h.Get(h.Child("m_button2", Box(h)), "label")), Is.EqualTo("Cancel"));
        Assert.That(c.ProcessKey("escape"), Is.True);
        for (int i = 0; i < 5; i++) h.Player.Tick();
        TestContext.WriteLine("escape answers: " + string.Join(",", l.Answers));
        Assert.That(l.Answers, Has.Count.EqualTo(1), "onEscapeKey answers the box");
        Assert.That(c.IsIdle, Is.True);
    }

    /// <summary>UICharacterListDialog's delete box (uicharacterlistdialog.cpp:2239): CreateMessageBox(0xe, text, this,
    /// "deletePC"), SetInput(2, "", 0, 0, 0), SetCheckbox0(BlockFormat(7), 0). OnMessage @0x1418eb540 splits the answer's
    /// tag into "deletePC" and the typed name.</summary>
    [Test]
    public void DeleteConfirmAnswersWithTheTypedName()
    {
        var (h, c) = Load();
        var l = new Listener { OnAnswer = MessageBoxQueue.Next };
        var box = MessageBoxQueue.Create(0xe, "Delete the character Solo?", l, "deletePC");
        box.SetInput(2, "", 0, 0, 0);
        box.SetCheckbox0("Delete this character and their inventory", false);
        MessageBoxQueue.Show(box);
        h.Player.Tick();
        var mb = Box(h);
        TestContext.WriteLine("box: " + mb);
        h.Set(h.Get(mb, "m_textInput"), "text", "Solo");
        h.Set(h.Get(mb, "m_checkbox0"), "selected", true);
        for (int i = 0; i < 3; i++) h.Player.Tick();
        Click(h, "m_button0");
        TestContext.WriteLine("answers: " + string.Join(",", l.Answers));
        Assert.That(l.Answers, Is.EqualTo(new[] { (0, "deletePC,Solo") }));
        Assert.That(c.IsIdle, Is.True);
    }

    /// <summary>UICharacterCreateDialog's name box (uicameraeditdialog.cpp:2127): FUN_140d73e40(10, 1, 0, ""), SetInput(2,
    /// "", 12, 0, 0) and a callback; the callback reads the name with GetInputText, an empty name gets UpdateErrorMessage
    /// and keeps the box (the callback returns false).</summary>
    [Test]
    public void NameBoxCallbackReadsTheInput()
    {
        var (h, c) = Load();
        var names = new List<string>();
        var box = MessageBoxQueue.CreateWithCallback("Enter a name", 10, (n, d) =>
        {
            var name = c.InputText(d);
            names.Add(n + ":" + name);
            if (n == 0 && name.Length == 0) { c.UpdateErrorMessage("Enter a name."); return false; }
            return true;
        });
        box.SetInput(2, "", 12, 0, 0);
        MessageBoxQueue.Show(box);
        h.Player.Tick();
        Click(h, "m_button0");
        Assert.That(c.CurrentBox, Is.SameAs(box), "an empty name keeps the box");
        h.Set(h.Get(Box(h), "m_textInput"), "text", "Tester01");
        // the buttons' ButtonDelayTimer drops a click within 500 ms of the last one (MessageBoxBase, ButtonDelayTimer.onClickButton)
        for (int i = 0; i < h.Player.FrameRate; i++) h.Player.Tick();
        Click(h, "m_button0");
        Assert.That(names, Is.EqualTo(new[] { "0:", "0:Tester01" }));
        Assert.That(c.IsIdle, Is.True);
    }
}
