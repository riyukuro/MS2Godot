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

    sealed class TimerListener : IMessageBoxListener, IMessageBoxTimerListener
    {
        public readonly List<string> Timed = new();
        public bool OnMessageBox(MessageBoxController sender, int button, string tag) => false;
        public void OnTimerComplete(MessageBoxController sender, string extra) => Timed.Add(extra);
    }

    [Test]
    public void ThePopupTimerCompleteGoesToATimerListenerWithTheExtraText()
    {
        var (h, c) = Load();
        var l = new TimerListener();
        var box = MessageBoxQueue.Create(0, "x", l, "leaveInField");
        MessageBoxQueue.Show(box);
        h.Player.Tick();
        c.OnCallback("OnPopupTimerComplete", System.Array.Empty<object>());
        Assert.That(l.Timed, Is.EqualTo(new[] { "leaveInField" }));
    }

    static (UiHost, MessageBoxController) Load(Func<string, string> translate = null)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        var common = new GameStrings(packs, "en").Common();
        MessageBoxData.CommonByKey = common;
        var c = new MessageBoxController();
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        errors = rt.Errors;
        p.StageW = 1280; p.StageH = 720;
        p.Translate = translate;
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

    /// <summary>UICutSceneMovie::vfunc_13 (@0x14190f510) and OnMessage (@0x14190f980): Esc asks box 0x88c while the exit
    /// button shows and no box is up; only its first button runs Exit (the Trigger request), and the button hides.</summary>
    [Test]
    public void EscAtACutSceneMovieAsksTheExitBoxAndOnlyYesSends()
    {
        var (h, c) = Load();
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        var movie = new CutSceneMovie(sent.Add);
        Assert.That(movie.AskExitOnEsc(), Is.False, "no movie, no box");
        movie.Start("Common/Common_Opening.usm", 7);
        Assert.That(movie.AskExitOnEsc(), Is.True);
        h.Player.Tick();
        Assert.That(c.IsIdle, Is.False, "the box is up");
        Assert.That(sent, Is.Empty, "nothing is sent before the answer");
        Assert.That(movie.AskExitOnEsc(), Is.False, "a box is up: the key is not taken");
        Click(h, "m_button1");
        Assert.That(sent, Is.Empty, "no: nothing is sent");
        Assert.That(movie.IsPlaying && movie.ExitShown, Is.True);
        Assert.That(c.IsIdle, Is.True);
        Assert.That(movie.AskExitOnEsc(), Is.True);
        h.Player.Tick();
        Click(h, "m_button0");
        Assert.That(sent.Count, Is.EqualTo(1), "yes: Exit sends the Trigger request");
        Assert.That(sent[0], Is.TypeOf<Ms2.Net.Packets.CutSceneExitTrigger>());
        Assert.That(movie.ExitShown, Is.False, "the button hides");
        Assert.That(c.IsIdle, Is.True);
        Assert.That(movie.IsPlaying, Is.True, "it plays on until the server's stop");
        Assert.That(movie.AskExitOnEsc(), Is.False, "the hidden button asks nothing");
    }

    [Test]
    public void PricedSmartPushBoxHasARealNoButton()
    {
        // CSmartPush::vfunc_3 (smartpush.cpp:296-301): yes / no; only yes answers (FUN_140dcf800)
        var (h, c) = Load();
        var (m, _, _, sent, boxes) = SmartPushTests.Mgr();
        m.OnPushClicked(2);                       // the AutoMining row costs 300 merat
        Assert.That(boxes.Count, Is.EqualTo(1));
        MessageBoxQueue.Show(boxes[0]);
        h.Player.Tick();
        Click(h, "m_button1");
        Assert.That(sent, Is.Empty, "no: nothing is sent, merat not spent");
        Assert.That(c.IsIdle, Is.True);
        MessageBoxQueue.Show(boxes[0]);
        Click(h, "m_button0");
        Assert.That(sent, Is.EqualTo(new[] { (2, 0, false) }), "yes answers");
    }

    [Test]
    public void WindowCloseOnTheLauncherQuitsAtOnce()
    {
        // the launcher is MS2Godot's own page before Logo: no confirm box there
        Assert.That(QuitFlow.ConfirmOnWindowClose(QuitFlow.LauncherSceneId), Is.False);
        int directQuits = 0;
        Assert.That(QuitFlow.HandleWindowClose(QuitFlow.LauncherSceneId, _ => "Quit now?", true, () => { }, () => directQuits++), Is.True);
        Assert.That(directQuits, Is.EqualTo(1));
    }

    [Test]
    public void WindowCloseConfirmsOutsideLoginAndQuitsOnYes()
    {
        // WindowService WM_CLOSE (windowservice.cpp:772-784): Login and Logo quit directly; Lobby/Create/Game confirm
        Assert.That(QuitFlow.ConfirmOnWindowClose(QuitFlow.LoginSceneId), Is.False);
        Assert.That(QuitFlow.ConfirmOnWindowClose(QuitFlow.LogoSceneId), Is.False);
        Assert.That(QuitFlow.ConfirmOnWindowClose(QuitFlow.LobbySceneId), Is.True);
        Assert.That(QuitFlow.ConfirmOnWindowClose(QuitFlow.CreateSceneId), Is.True);
        Assert.That(QuitFlow.ConfirmOnWindowClose(QuitFlow.GameSceneId), Is.True);

        var (h, c) = Load();
        int restores = 0, directQuits = 0, boxQuits = 0;
        c.GameExit += () => boxQuits++;

        // In-game WM_CLOSE restores display mode and opens the "gameexit" confirm box instead of quitting immediately
        bool closed = QuitFlow.HandleWindowClose(QuitFlow.GameSceneId, _ => "Quit now?", true, () => restores++, () => directQuits++);
        Assert.That(closed, Is.False);
        Assert.That(restores, Is.EqualTo(1));
        Assert.That(directQuits, Is.EqualTo(0));
        Assert.That(c.CurrentBox?.Extra, Is.EqualTo(QuitFlow.ExitTag));
        Assert.That(c.CurrentBox?.Type, Is.EqualTo(QuitFlow.ExitBoxType));

        // Repeated WM_CLOSE while the exit box is already shown does not stack a second exit box
        Assert.That(QuitFlow.HandleWindowClose(QuitFlow.GameSceneId, _ => "Quit now?", true, () => restores++, () => directQuits++), Is.False);
        Assert.That(MessageBoxQueue.Count, Is.EqualTo(0));

        // Clicking No closes the box and stays in game
        Click(h, "m_button1");
        Assert.That(boxQuits, Is.EqualTo(0));
        Assert.That(c.IsIdle, Is.True);

        // WM_CLOSE again, then clicking Yes fires GameExit (SendRequestQuitAndDisconnect + exit)
        Assert.That(QuitFlow.HandleWindowClose(QuitFlow.GameSceneId, _ => "Quit now?", true, () => restores++, () => directQuits++), Is.False);
        Click(h, "m_button0");
        Assert.That(boxQuits, Is.EqualTo(1));

        // Login scene WM_CLOSE restores display mode and quits directly without a confirm box
        bool loginClosed = QuitFlow.HandleWindowClose(QuitFlow.LoginSceneId, _ => "Quit now?", true, () => restores++, () => directQuits++);
        Assert.That(loginClosed, Is.True);
        Assert.That(directQuits, Is.EqualTo(1));

        // Outside Login without a message box layer the client has no quit path: nothing runs
        Assert.That(QuitFlow.HandleWindowClose(QuitFlow.LobbySceneId, _ => "Quit now?", false, () => restores++, () => directQuits++), Is.False);
        Assert.That(directQuits, Is.EqualTo(1));
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

    /// <summary>
    /// The AlreadyLogin box (CNetworkService::OnLoginResult 4 or 5, networkservice.cpp:1033-1036): type 0, tag alreadyReconnect, no
    /// listener. Its one button and Escape both reach HandleAlreadyReconnectButton (uimessagebox.cpp:1572-1577); the box can only
    /// be answered with the button the handler takes as the OK, so the login again always follows.
    /// </summary>
    [Test]
    public void TheAlreadyLoginBoxAnswersItsOkToEveryKeyAndClick()
    {
        foreach (var how in new[] { "escape", "enter", "click" })
        {
            var (h, c) = Load();
            var answered = new List<int>();
            c.AlreadyReconnect += () => answered.Add(0);
            var box = LoginRefusalBox.Build(4, 0, "x");
            MessageBoxQueue.Show(box);
            for (int i = 0; i < 3; i++) h.Player.Tick();
            if (how == "click") Click(h, "m_button0");
            else c.ProcessKey(how);
            for (int i = 0; i < 5; i++) h.Player.Tick();
            TestContext.WriteLine($"{how}: reconnects {answered.Count}, idle {c.IsIdle}");
            Assert.That(c.IsIdle, Is.True, how + " closes the box");
            Assert.That(answered, Has.Count.EqualTo(1), how + " answers the OK: the login again");
            MessageBoxQueue.Clear(0);
        }
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

    /// <summary>The forced rename box (FUN_1418f13f0, OnLoginToGame 0x31): no listener, so its "changeInvalidCharName,<typed>"
    /// answer goes to HandleChangeInvalidCharNameButton (uimessagebox.cpp:2119-2156): an empty name gets common string 0xd
    /// under the input and keeps the box, a name the checker refuses its message, a good one the request with the box's
    /// character id; any other button cancels.</summary>
    [Test]
    public void TheRenameBoxChecksTheNameThenSendsIt()
    {
        var (h, c) = Load();
        var sent = new List<(long, string)>();
        int cancels = 0;
        c.CharacterNameError = n => n == "Bad" ? "This name cannot be used." : null;
        c.RenameInvalidName += (id, n) => sent.Add((id, n));
        c.RenameInvalidNameCancelled += () => cancels++;
        var box = InvalidNameRename.Create(20000000042, MessageBoxData.CommonByKey);
        Assert.That(box.Msg, Is.Not.Empty, "s_char_rename_input");
        MessageBoxQueue.Show(box);
        h.Player.Tick();
        void Wait() { for (int i = 0; i < h.Player.FrameRate; i++) h.Player.Tick(); }

        Click(h, "m_button0");
        Assert.That(c.CurrentBox, Is.SameAs(box), "an empty name keeps the box");
        TestContext.WriteLine($"empty: '{SafeError(h)}'");
        h.Set(h.Get(Box(h), "m_textInput"), "text", "Bad");
        Wait();
        Click(h, "m_button0");
        Assert.That(c.CurrentBox, Is.SameAs(box), "a refused name keeps the box");
        Assert.That(sent, Is.Empty);
        h.Set(h.Get(Box(h), "m_textInput"), "text", "Neu");
        Wait();
        Click(h, "m_button0");
        Assert.That(sent, Is.EqualTo(new[] { (20000000042L, "Neu") }));
        Assert.That(cancels, Is.Zero);
        Assert.That(c.IsIdle, Is.True);

        var again = InvalidNameRename.Create(7, MessageBoxData.CommonByKey);
        MessageBoxQueue.Show(again);
        h.Player.Tick();
        Wait();
        Click(h, "m_button1");
        Assert.That(cancels, Is.EqualTo(1));
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(c.IsIdle, Is.True);
    }

    static string SafeError(UiHost h)
    {
        try { return Rt.ToString(h.Get(h.Get(Box(h), "m_errorMsg"), "text")); } catch (Exception e) { return e.GetType().Name; }
    }

    /// <summary>The name box's field takes any character: CommonTextField.setInpuCharName gives it the restrict set
    /// CommonTextField.init built, and the client runs that init (Main.addedToStage, CommonInit.init) after Common.init
    /// cleared m_getFormatFunction and before main registers it (gfxmoviemain.cpp:106-114), so the translator's
    /// "$s_common_text_restrict" is not read and the set is null. The name box's maxChars of 12 still holds.</summary>
    [Test]
    public void NameBoxTakesAnyCharacter()
    {
        var (h, _) = Load(k => k == "$s_common_text_restrict" ? "0-9a-zA-Z" : k);
        var box = MessageBoxQueue.CreateWithCallback("Enter a name", 10, (n, d) => true);
        box.SetInput(2, "", 12, 0, 0);
        MessageBoxQueue.Show(box);
        h.Player.Tick();
        var input = h.Get(Box(h), "m_textInput");
        var tf = (AsTextField)h.Get(input, "textField");
        for (int i = 0; i < h.Player.FrameRate; i++) h.Player.Tick();
        h.Player.TypeInto(tf, "ab1_C d");
        h.Player.TypeInto(tf, "0123456789");
        // GetInputText, what the create callback reads, is m_textInput's text
        Assert.That((tf.restrict, tf.text, h.Get(input, "text")), Is.EqualTo(((string)null, "ab1_C d01234", "ab1_C d01234")));
    }

    [Test]
    public void PartyInviteRefusesOrShowsItsBoxAndAnswers()
    {
        var answers = new List<int>();
        var invite = new Ms2.Net.Packets.PartyInvite("Inviter", 77);
        Assert.That(PartyInviteBox.OnInvite(invite, true, false, answers.Add), Is.Null);
        Assert.That(PartyInviteBox.OnInvite(invite, false, true, answers.Add), Is.Null);
        Assert.That(answers, Is.EqualTo(new[] { 0x14, 0x15 }), "the refuse option, then a PvP point");
        answers.Clear();

        var (h, c) = Load();
        c.AnswerPartyInvite = answers.Add;
        var box = PartyInviteBox.OnInvite(invite, false, false, answers.Add);
        h.Player.Tick();
        Assert.That(c.CurrentBox, Is.SameAs(box));
        Assert.That((box.Type, box.Extra, box.Listener), Is.EqualTo((1, "partyinvite_77", (IMessageBoxListener)null)));
        Assert.That((box.Modaless, box.NoInputBlock, box.EnterAllowed), Is.EqualTo((true, true, false)));
        Assert.That(box.Msg, Does.Contain("Inviter"), "s_msg_party_invite with the inviter's name");
        TestContext.WriteLine("invite: " + Box(h) + " / " + h.Child("m_contents", Box(h)) + " / " + box.Msg);
        Click(h, "m_button1");
        Assert.That(answers, Is.EqualTo(new[] { 9 }), "no declines");
        Assert.That(c.IsIdle, Is.True);
        PartyInviteBox.OnInvite(invite, false, false, answers.Add);
        Click(h, "m_button0");
        Assert.That(answers, Is.EqualTo(new[] { 9, 1 }), "yes accepts");
        Assert.That(c.IsIdle, Is.True);
        // the 20 s countdown: a button the timer pressed (OnPopupTimerComplete) answers 12
        box = PartyInviteBox.OnInvite(invite, false, false, answers.Add);
        Assert.That((box.HasTimer, box.Timer.Sec, box.Timer.ShowFormat), Is.EqualTo((true, 0x14, 1)));
        h.Player.Tick();
        Assert.That(c.OnCallback("OnPopupTimerComplete", System.Array.Empty<object>()), Is.False, "no listener: falls through");
        Assert.That(box.Timer.Button, Is.EqualTo(1));
        Click(h, "m_button1");
        Assert.That(answers, Is.EqualTo(new[] { 9, 1, 12 }), "the timer's decline");
    }

    [Test]
    public void GuildInviteShowsItsBoxAndAnswers()
    {
        var answers = new List<bool>();
        var invite = new Ms2.Net.Packets.GuildInviteInfo { GuildId = 9, GuildName = "Guildy", Inviter = "Inviter", Invitee = "Me" };
        var (h, c) = Load();
        c.AnswerGuildInvite = answers.Add;
        var box = GuildInviteBox.Show(invite);
        h.Player.Tick();
        Assert.That(c.CurrentBox, Is.SameAs(box));
        Assert.That((box.Type, box.Extra, box.Listener), Is.EqualTo((1, "guildinvite", (IMessageBoxListener)null)));
        Assert.That((box.Modaless, box.NoInputBlock, box.EnterAllowed), Is.EqualTo((true, true, false)));
        Assert.That((box.HasTimer, box.Timer.Sec, box.Timer.ShowFormat), Is.EqualTo((true, 0x14, 1)));
        Assert.That(box.Msg, Does.Contain("Inviter").And.Contain("Guildy"), "common 0x38c with the inviter and the guild");
        TestContext.WriteLine("guild invite: " + box.Msg);
        Click(h, "m_button1");
        Assert.That(answers, Is.EqualTo(new[] { false }), "no declines");
        Assert.That(c.IsIdle, Is.True);
        GuildInviteBox.Show(invite);
        Click(h, "m_button0");
        Assert.That(answers, Is.EqualTo(new[] { false, true }), "yes accepts");
    }

    [Test]
    public void CoupleEmotionInviteShowsItsBoxAndAnswers()
    {
        // OnBuddyEmote mode 0 (networkservice.cpp:1124-1127) and HandleCoupleEmotionButton (uimessagebox.cpp:2273)
        var answers = new List<(int, uint, long)>();
        var (h, c) = Load();
        c.AnswerCoupleEmotion = (b, e, id) => answers.Add((b, e, id));
        var box = CoupleEmotionInviteBox.Show(90220001, 4002, "Asker", "Hug", 20);
        h.Player.Tick();
        Assert.That(c.CurrentBox, Is.SameAs(box));
        Assert.That((box.Type, box.Extra, box.Listener), Is.EqualTo((1, "coupleEmotion", (IMessageBoxListener)null)));
        Assert.That((box.Modaless, box.NoInputBlock, box.EnterAllowed), Is.EqualTo((true, true, false)));
        Assert.That((box.HasTimer, box.Timer.Sec, box.Timer.ShowFormat), Is.EqualTo((true, 20, 1)));
        Assert.That(box.Args, Is.EqualTo(new[] { "90220001", "4002", "Asker" }));
        Assert.That(box.ArgString(2, null), Is.EqualTo("Asker"));
        Assert.That(box.Msg, Does.Contain("Asker").And.Contain("Hug"), "common 0xce5 with the asker and the emotion");
        TestContext.WriteLine("couple invite: " + box.Msg);
        Click(h, "m_button1");
        Assert.That(answers, Is.EqualTo(new[] { (1, 90220001u, 4002L) }));
        Assert.That(c.IsIdle, Is.True);
        CoupleEmotionInviteBox.Show(90220001, 4002, "Asker", "Hug", 20);
        Click(h, "m_button0");
        Assert.That(answers[1], Is.EqualTo((0, 90220001u, 4002L)), "yes is button 0");
    }

    [Test]
    public void RecallInviteShowsItsBoxAndAnswers()
    {
        // OnRecallUser (scenestategame.cpp:4733): mode 4 opens the confirm box of common 0x902 once; a second invite while one is
        // pending is answered with SendRecallUserText (the name, flag 4); the box's yes / no is SendRecallUserReply (u8 4, id, accept)
        var sent = new List<Ms2.Net.Wire.IOutPacket>();
        var notices = new List<string>();
        var recall = new RecallUserBox { Send = sent.Add, Notice = notices.Add };
        var (h, c) = Load();
        c.AnswerRecallUser = recall.Answer;
        var box = recall.Apply(new Ms2.Net.Packets.RecallUserPacket.Invite(1, "Caller", 77));
        h.Player.Tick();
        Assert.That(recall.Pending, Is.EqualTo(77));
        Assert.That(c.CurrentBox, Is.SameAs(box));
        Assert.That((box.Type, box.Extra, box.Listener), Is.EqualTo((1, "recallconfirm", (IMessageBoxListener)null)));
        Assert.That((box.Modaless, box.NoInputBlock, box.EnterAllowed), Is.EqualTo((true, true, false)));
        Assert.That((box.HasTimer, box.Timer.Sec, box.Timer.ShowFormat), Is.EqualTo((true, 0x14, 1)));
        Assert.That(box.Msg, Does.Contain("Caller"), "common 0x902 with the caller's name");
        Assert.That(recall.Apply(new Ms2.Net.Packets.RecallUserPacket.Invite(1, "Other", 78)), Is.Null);
        Assert.That(sent, Is.EqualTo(new Ms2.Net.Wire.IOutPacket[] { new Ms2.Net.Packets.RecallUserText("Other", 4) }), "the second invite is refused by text");
        Assert.That(recall.Pending, Is.EqualTo(77));
        Click(h, "m_button0");
        Assert.That(sent[^1], Is.EqualTo(new Ms2.Net.Packets.RecallUserReply(77, true)), "yes accepts the pending invite");
        Assert.That((recall.Pending, c.IsIdle), Is.EqualTo((0, true)));
        recall.Answer(true);
        Assert.That(sent, Has.Count.EqualTo(2), "no invite pending: nothing is sent");
        recall.Apply(new Ms2.Net.Packets.RecallUserPacket.Invite(1, "Caller", 79));
        Click(h, "m_button1");
        Assert.That(sent[^1], Is.EqualTo(new Ms2.Net.Packets.RecallUserReply(79, false)), "no declines");
        // the chat lines of modes 5 to 7
        recall.Apply(new Ms2.Net.Packets.RecallUserPacket.Progress("A", 0));
        recall.Apply(new Ms2.Net.Packets.RecallUserPacket.Progress("A", 9));
        recall.Apply(new Ms2.Net.Packets.RecallUserPacket.Recalled("B"));
        recall.Apply(new Ms2.Net.Packets.RecallUserPacket.Failure("C", 4));
        Assert.That(notices, Has.Count.EqualTo(3), "result 9 of mode 5 shows nothing");
        Assert.That(notices[0], Does.Contain("A")); Assert.That(notices[1], Does.Contain("B")); Assert.That(notices[2], Does.Contain("C"));
        TestContext.WriteLine("recall: " + box.Msg + " | " + string.Join(" | ", notices));
    }

    [Test]
    public void TheStickerPackItemBoxYesUsesItsItem()
    {
        // ChatEmoticonAdd @0x140d1fba0: box 0x11 (itemNotify) with the item entry, tag use_function_item, yes / no 0x477 / 0x478;
        // its yes reaches UIMessageBox::FUN_1419fc810 with the box's item uid
        var (h, c) = Load();
        var used = new List<long>();
        c.UseFunctionItem = used.Add;
        var box = MessageBoxQueue.Create(0x11, "pack", null, Ms2.Ui.Hud.InventoryController.UseFunctionItemTag);
        box.ItemUid = 4242;
        var item = Ms2.Ui.Items.Item.Create(tables.Items, 20000001, 1);
        if (item != null) box.SetItemEntry(item, "item", 1, 0, -1, -1, 1, tables.Items.TextColor);
        box.SetButtonLabelIds(0x477, 0x478, 0);
        MessageBoxQueue.Show(box);
        for (int i = 0; i < 5; i++) h.Player.Tick();
        Assert.That(c.CurrentBox, Is.SameAs(box));
        Click(h, "m_button0");
        Assert.That(used, Is.EqualTo(new long[] { 4242 }), "the yes uses the box's item");
        Assert.That(c.IsIdle, Is.True);
    }

    [Test]
    public void TheBoxCountdownKeepsItsSecondsLeft()
    {
        long now = 1000;
        var clock = MessageBoxTimer.Clock;
        MessageBoxTimer.Clock = () => now;
        try
        {
            var box = new MessageBoxData(1) { Msg = "m" };
            box.SetTimer(20, 1, "<{0}>");
            var o = box.FillObj(false);
            Assert.That((o["time"], o["msg"]), Is.EqualTo((20, "m\n<20>")));
            Assert.That(box.IsLive, Is.True, "a timed box with seconds left");
            string line = null;
            box.OnChangeTime(true, 15, t => line = t);
            Assert.That((line, box.Timer.Armed), Is.EqualTo(("m\n<15>", 15)));
            box.OnChangeTime(false, 14, t => line = t);
            Assert.That(box.Timer.Armed, Is.EqualTo(15), "not shown: ignored");
            box.ReArm();                    // closed mid-countdown: the 15 s go on running
            now += 6000;
            Assert.That(box.IsLive, Is.True);
            o = box.FillObj(false);
            Assert.That((o["time"], box.Timer.Armed), Is.EqualTo((9, -1)), "shown again with the seconds left");
            box.OnChangeTime(true, 9, _ => { });
            box.ReArm();
            now += 9000;
            Assert.That(box.IsLive, Is.False, "the countdown ran out while queued");
            Assert.That(new MessageBoxData(1).IsLive, Is.True, "no timer");
        }
        finally { MessageBoxTimer.Clock = clock; }
    }
}
