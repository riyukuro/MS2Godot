using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The character list movie (uicharacterlistdialog) run by its own ActionScript (needs MS2_DATA).</summary>
public class CharacterListTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static (UiHost, CharacterListController) Load(params LobbyCharacterRow[] rows) => Load(null, rows);

    static (UiHost, CharacterListController) Load(Action<CharacterListController> setup, params LobbyCharacterRow[] rows)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var c = new CharacterListController();
        c.Characters.AddRange(rows);
        setup?.Invoke(c);
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 60; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    static LobbyCharacterRow Row(string name, string sn, string img) =>
        new(name, 1, 10, "Lv. 1", img, "2026/11/18", "Tria", sn);

    [Test]
    public void AnEmptyListReloadsTheSelectSceneOnce()
    {
        // vfunc_28 :2296: with no character the dialog's own OnSelectCharacter("") loads the select map while the scene's fade-in
        // (m_220) is still to come; the map's load is the fade-in, so the rebuilt dialog's call finds m_220 set and does nothing
        int first = 0, again = 0;
        var (_, a) = Load(c => c.SelectCharacter += _ => first++);
        Assert.That(first, Is.EqualTo(1), "the first entry loads the select map once");
        Load(c => { c.CarryFrom(a); c.Previewed(null); c.SelectCharacter += _ => again++; });
        Assert.That(again, Is.EqualTo(0), "the rebuilt dialog does not load it again");
    }

    [Test]
    public void LobbyButtonsFadeInWithThePopup()
    {
        // LobbyDialog.popup() returns false once the root sits on the "popup" label (frame 2): the buttons' tween only
        // starts when OnLoadComplete runs with the timeline still on frame 1
        var (h, c) = Load(Row("A", "11", "d.png"));
        Assert.That(h.Get(h.Child("m_btns"), "alpha"), Is.EqualTo(1.0).Within(0.01), "Quit / Options buttons shown");
        Assert.That(h.Get(h.Entity, "m_btnShowTween"), Is.Not.Null);
    }

    [Test]
    public void ANewPictureReachesItsRow()
    {
        // vfunc_ugc_1 (@0x1418ed310): updateProfile(index, GetProfileImagePath(key, 1)) on the dialog, which hands it to the
        // row's CharacterRenderer (CharacterListDialog.updateProfile -> m_charlist.getRendererAt(index).updateProfile).
        const string none = "default.png", pic = "./Custom/Download/Profile/x_1_2.png";
        var (h, c) = Load(Row("A", "11", none), Row("B", "12", none));
        string ImgPath(int i)
        {
            var r = h.Invoke(h.Entity, "getCharacterRenderer", i);
            return r == null ? "<no renderer>" : Rt.ToString(h.Get(h.Get(r, "m_profile"), "imgpath"));
        }
        TestContext.WriteLine($"before: {ImgPath(0)} | {ImgPath(1)}");
        c.OnProfileImage(12, pic);
        h.Player.Tick();
        Assert.That(c.Characters[1].ImgPath, Is.EqualTo(pic));
        Assert.That(ImgPath(1), Does.EndWith(pic));
        Assert.That(ImgPath(0), Does.Not.EndWith(pic));
    }
    [Test]
    public void DoubleClickOnACharacterStartsTheGame()
    {
        // CharacterListDialog listens to m_charlist's itemDoubleClick (onDoubleClickCharacter -> clickStart -> OnGameStart, as
        // the Start button), which CLIK's CoreList raises from its renderers' doubleClick (setupRenderer: doubleClickEnabled =
        // true). The client hands WM_LBUTTONDBLCLK to the movie as a plain mouse down (@0x1422da5a0) and GFx makes the second
        // release within 300 ms at the same pixel a doubleClick (sfr_as3_17.cpp:7131-7165).
        var (h, c) = Load(Row("A", "11", "d.png"), Row("B", "12", "d.png"));
        var started = new List<string>();
        c.GameStart += started.Add;
        var p = h.Player;
        p.Clock = () => p.RT.TimerMs;   // the double click's wall clock, kept with the frames here
        var r = h.Invoke(h.Entity, "getCharacterRenderer", 1) as AsDisplayObject;
        Assert.That(r, Is.Not.Null, "row 1's renderer");
        var b = r.GetBounds(null);
        double x = b.X + b.W / 2, y = b.Y + b.H / 2;
        void Press() { p.MouseMove(x, y); p.MouseButton(true); p.MouseButton(false); }
        void Wait(double ms) { for (double t = p.RT.TimerMs + ms; p.RT.TimerMs < t;) p.Tick(); }

        // the scene's fade-in turns the input on; a click on another row selects it and turns the input off until its
        // preview's fade-in (OnSelectCharacter, FadeInComplete), so the double click is on the selected row
        var selected = new List<string>();
        c.SelectCharacter += selected.Add;
        c.Previewed("11");      // the first row's preview from the list's load
        c.FadeInComplete();
        Press();
        p.Tick();
        Assert.That(selected, Is.EqualTo(new[] { "12" }), "a click selects the row");
        c.Previewed("12");
        c.FadeInComplete();
        Wait(400);

        // two clicks 400 ms apart are two clicks
        Press();
        Wait(400);
        Assert.That(started, Is.Empty, "one click selects only");
        Press();
        p.Tick();
        Assert.That(started, Is.Empty, "clicks 400 ms apart do not start");

        // then a click within 300 ms of the last one: a double click on that row; the enter request goes once the list has popped
        // down (FUN_1418f13f0 -> HidePopups, OnEndPopdown -> FUN_1418f1790)
        Wait(200);
        Press();
        p.Tick();
        Assert.That(c.Starting, Is.True, "the double click starts the game");
        Wait(2000);
        Assert.That(started, Is.EqualTo(new[] { "12" }), "the popdown's end sends the start with that row");
    }

    [Test]
    public void ARowClickAfterStartDoesNothing()
    {
        // OnGameStart -> FUN_1418f13f0 (uicharacterlistdialog.cpp:1470-1499): HidePopups (popdown, mouseChildren off) and m_1d4 =
        // 0x620000c; OnSelectCharacter returns while m_1d4 is set with a character chosen (:2271-2276). The enter request goes
        // only at the popdown's end (OnEndPopdown :1654 -> FUN_1418f1790 -> EnterGame), once.
        var (h, c) = Load(Row("A", "11", "d.png"), Row("B", "12", "d.png"));
        var started = new List<string>();
        var selected = new List<string>();
        c.GameStart += started.Add;
        c.SelectCharacter += selected.Add;
        var p = h.Player;
        c.Previewed("11");
        c.FadeInComplete();
        Assert.That(c.InputOn, Is.True, "the list takes the mouse before Start");

        h.Invoke(h.Entity, "onEnterKey");   // Enter: onEnterKey -> clickStart -> OnGameStart("11")
        p.Tick();
        Assert.That(c.Starting, Is.True, "Start took the selected row");
        Assert.That(c.InputOn, Is.False, "HidePopups turned the list's mouse off");
        Assert.That(started, Is.Empty, "nothing is sent before the popdown ends");

        // the movie's OnSelectCharacter for the other row (a click that reached the list) is refused while m_1d4 is set. The click
        // itself is kept from the movie by the scene (the view takes no mouse while the input is off): the runtime's hit test
        // still reaches a child under a root with mouseChildren and mouseEnabled off (as3-runtime-gaps.md)
        Assert.That(c.OnCallback("OnSelectCharacter", new object[] { "12" }), Is.True);
        Assert.That(selected, Is.Empty, "no selection after Start");

        for (int i = 0; i < 240 && started.Count == 0; i++) p.Tick();
        Assert.That(started, Is.EqualTo(new[] { "11" }), "the popdown's end sends the start once, with the started row");
        for (int i = 0; i < 60; i++) p.Tick();
        Assert.That(started, Has.Count.EqualTo(1));
        Assert.That(c.InputOn, Is.False, "the input stays off after the request (only a failed EnterGame shows the list again)");
    }

    [Test]
    public void AStartDuringASelectionGoesOnAfterThePreview()
    {
        // FUN_1418f13f0 under m_180 (a selection being handled): no HidePopups, m_1d4 = 0x620000c. The preview of the new select map
        // (PreviewCharacter -> FUN_1418f1d00) runs it again: the popdown, then the enter at its end. The host builds the dialog
        // again over the map load, so the game start is carried with it (CarryFrom).
        var (_, a) = Load(Row("A", "11", "d.png"), Row("B", "12", "d.png"));
        a.Previewed("11");
        a.FadeInComplete();
        Assert.That(a.OnCallback("OnSelectCharacter", new object[] { "12" }), Is.True);
        Assert.That(a.Selecting, Is.True);
        Assert.That(a.OnCallback("OnGameStart", new object[] { "12" }), Is.True);
        Assert.That(a.Starting, Is.True, "the start waits for the preview");
        var started = new List<string>();
        var (h, b) = Load(c => { c.CarryFrom(a); c.Previewed("12"); c.GameStart += started.Add; }, Row("A", "11", "d.png"), Row("B", "12", "d.png"));
        TestContext.WriteLine($"popdown state: label {Rt.ToString(h.Get(h.Entity, "currentLabel"))} frame {h.Get(h.Entity, "currentFrame")}");
        b.ResumeGameStart();
        Assert.That(b.InputOn, Is.False, "HidePopups after the preview");
        for (int i = 0; i < 240 && started.Count == 0; i++) h.Player.Tick();
        TestContext.WriteLine($"after: label {Rt.ToString(h.Get(h.Entity, "currentLabel"))} frame {h.Get(h.Entity, "currentFrame")}");
        Assert.That(started, Is.EqualTo(new[] { "12" }), "the popdown's end sends the start");
    }

    // ---- the character delete (UICharacterListDialog::OnCharacterListPacket modes 2, 5, 6; ShowDeleteError) ----------

    /// <summary>ShowDeleteError (@0x1418f47e0, :1959-1977): the client's string for each code; any other is s_char_err_destroy
    /// with the code (BlockFormat(0x17, err)).</summary>
    static readonly Dictionary<int, string> DeleteErrorKeys = new()
    {
        [1] = "s_char_err_already_destroy", [2] = "s_char_err_exist_ugc_map", [3] = "s_char_err_guild_master",
        [4] = "s_char_err_guild", [5] = "s_char_err_ugc_market", [6] = "s_char_err_black_market_count",
        [7] = "s_char_err_unread_mail", [8] = "s_char_err_no_destroy_wait", [9] = "s_char_err_meso_market_count",
        [11] = "s_char_err_wedding",
    };

    /// <summary>The codes run: the server's set (CharacterDeleteError 1-11 and 255), and codes the client has no text for.</summary>
    static readonly int[] DeleteCodes = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 255, -1 };

    static string Key(string k) => "<" + k + ">" + (k is "s_char_err_destroy" or "s_char_err_next_delete_char_date" or "s_char_delete_waiting"
        or "s_char_ask_revival_char" or "s_char_ask_delete_final" ? "{0}" : "");

    static string ExpectedError(int code) =>
        DeleteErrorKeys.TryGetValue(code, out var k) ? Key(k) : "<s_char_err_destroy>" + ((uint)code).ToString(System.Globalization.CultureInfo.InvariantCulture);

    sealed class DeleteRig
    {
        public UiHost H; public CharacterListController C;
        public long Now = 1_800_000_000, Tick = 1_000_000;
        public readonly List<(string Text, int Type, string Tag)> Boxes = new();
        public readonly List<string> Deletes = new(), Cancels = new(), Finals = new();
        public object Renderer(int i) => H.Invoke(H.Entity, "getCharacterRenderer", i);
        public bool DeleteShown(int i) => Rt.ToBoolean(H.Get(H.Get(Renderer(i), "m_delete"), "visible"));
        public string DeleteLabel(int i) => Rt.ToString(H.Get(H.Get(H.Get(Renderer(i), "m_delete"), "m_label"), "text"));
        public bool FinalShown(int i) => Rt.ToBoolean(H.Get(H.Get(Renderer(i), "m_finalDelete"), "visible"));
        public string Sn(int i) => Rt.ToString(H.Get(H.Get(Renderer(i), "data"), "charSN"));
        /// <summary>The movie list's rows that hold a character (the list fills up to maxRow with empty slots).</summary>
        public int Rows
        {
            get
            {
                var dp = H.Get(H.Get(H.Entity, "m_charlist"), "dataProvider");
                int n = (int)Rt.ToNumber(H.Get(dp, "length")), full = 0;
                for (int i = 0; i < n; i++)
                    if (H.Get(dp, i.ToString(System.Globalization.CultureInfo.InvariantCulture)) is { } row && Rt.ToString(H.Get(row, "charSN")) is { Length: > 0 } sn && sn != "undefined") full++;
                return full;
            }
        }
    }

    Func<string, string> savedCommon;
    [SetUp] public void CommonKeys() { savedCommon = Ms2.Ui.Hud.MessageBoxData.CommonByKey; Ms2.Ui.Hud.MessageBoxData.CommonByKey = Key; }
    [TearDown] public void RestoreCommon() => Ms2.Ui.Hud.MessageBoxData.CommonByKey = savedCommon;

    static DeleteRig LoadDelete(params LobbyCharacterRow[] rows)
    {
        var rig = new DeleteRig();
        var (h, c) = Load(c =>
        {
            c.ServerNow = () => rig.Now;
            c.ServerTick = () => rig.Tick;
            c.DateText = unix => "date" + unix;
            c.ShowBox = (text, type, tag) => rig.Boxes.Add((text, type, tag));
            c.DeleteRequest += rig.Deletes.Add;
            c.CancelDeleteRequest += rig.Cancels.Add;
            c.FinalDeleteRequest += rig.Finals.Add;
        }, rows);
        rig.H = h; rig.C = c;
        c.Previewed(rows.Length > 0 ? rows[0].CharSN : null);
        c.FadeInComplete();
        return rig;
    }

    [Test]
    public void EveryDeleteErrorShowsItsTextAndKeepsTheList()
    {
        // modes 2 (DeleteEntry), 5 (BeginDelete) and 6 (CancelDelete) with each code: one plain box (type 0, no tag) with the
        // code's text, the two characters still in the dialog and the movie, and the row in no delete state. Mode 5's code 10 is
        // s_char_err_next_delete_char_date with the time (FUN_14083bdc0); in modes 2 and 6 code 10 has no text of its own.
        var rig = LoadDelete(Row("A", "11", "d.png"), Row("B", "12", "d.png"));
        var c = rig.C;
        int runs = 0;
        foreach (int mode in new[] { 2, 5, 6 })
            foreach (int code in DeleteCodes)
            {
                rig.Boxes.Clear();
                if (mode == 2) Assert.That(c.OnDeleteEntry(code, "11"), Is.EqualTo(-1));
                else if (mode == 5) c.OnBeginDelete("11", code, rig.Now + 3600);
                else c.OnCancelDelete("11", code);
                string want = mode == 5 && code == 10 ? "<s_char_err_next_delete_char_date>date" + (rig.Now + 3600) : ExpectedError(code);
                Assert.That(rig.Boxes, Is.EqualTo(new[] { (want, 0, "") }), $"mode {mode} code {code}");
                Assert.That(c.Characters.Select(r => r.CharSN), Is.EqualTo(new[] { "11", "12" }), $"mode {mode} code {code}: the list");
                Assert.That(rig.Rows, Is.EqualTo(2), $"mode {mode} code {code}: the movie's rows");
                Assert.That(rig.DeleteShown(0), Is.False, $"mode {mode} code {code}: the row's delete state");
                Assert.That(c.DeleteTimeOf("11"), Is.Zero);
                runs++;
            }
        TestContext.WriteLine($"delete errors: {runs} answers checked ({DeleteCodes.Length} codes x 3 modes)");
    }

    [Test]
    public void ARefusedDeleteOfAWaitingCharacterStopsItsCountdown()
    {
        // mode 2 with an error for a character in its wait (:952-958): its seconds are zeroed (FUN_1418e9ed0(0)), the error
        // shown, and SetDeleteState takes it off the pending list and puts the row back to no delete state; the delete time stays
        var rig = LoadDelete(Row("A", "11", "d.png") with { DeleteTime = 1_800_000_000 + 7200 }, Row("B", "12", "d.png"));
        Assert.That(rig.DeleteShown(0), Is.True);
        Assert.That(rig.C.PendingDeletes, Is.EqualTo(new[] { "11" }));
        rig.C.OnDeleteEntry(7, "11");
        Assert.That(rig.Boxes, Is.EqualTo(new[] { (Key("s_char_err_unread_mail"), 0, "") }));
        Assert.That(rig.C.PendingDeletes, Is.Empty);
        Assert.That(rig.DeleteShown(0), Is.False);
        Assert.That(rig.C.DeleteSecondsLeft("11"), Is.Zero);
        Assert.That(rig.C.DeleteTimeOf("11"), Is.EqualTo(1_800_000_000 + 7200));
    }

    [Test]
    public void TheDeleteBoxNeedsTheCharactersName()
    {
        // OnMessage "deletePC,<typed>" (:745-762): MyPC's name, any case, sends CharacterManagement 2 once (m_1d8) until the answer;
        // another name, or none, shows s_char_err_delete_name; a cancel closes the box only
        var rig = LoadDelete(Row("Ara", "11", "d.png"), Row("B", "12", "d.png"));
        var c = rig.C;
        c.OnMessageBox(null, 1, "deletePC,Ara");
        Assert.That(rig.Deletes, Is.Empty, "cancel");
        c.OnMessageBox(null, 0, "deletePC,Bra");
        c.OnMessageBox(null, 0, "deletePC,");
        Assert.That(rig.Boxes, Is.EqualTo(new[] { (Key("s_char_err_delete_name"), 0, ""), (Key("s_char_err_delete_name"), 0, "") }));
        c.OnMessageBox(null, 0, "deletePC,aRA");
        c.OnMessageBox(null, 0, "deletePC,Ara");
        Assert.That(rig.Deletes, Is.EqualTo(new[] { "11" }), "sent once while unanswered");
        c.OnDeleteEntry(7, "11");
        c.OnMessageBox(null, 0, "deletePC,Ara");
        Assert.That(rig.Deletes, Is.EqualTo(new[] { "11", "11" }), "the answer lets the next one go");
    }

    [Test]
    public void ADeleteGoesFromTheListAndTheMovie()
    {
        // mode 2 error 0 (:944-950): EraseCharacter, then CharacterRemoved: the movie's deleteCharacter(index), the input on; no box
        var rig = LoadDelete(Row("A", "11", "d.png"), Row("B", "12", "d.png"));
        bool emptied = false;
        rig.C.ListEmptied += () => emptied = true;
        Assert.That(rig.C.OnDeleteEntry(0, "11"), Is.EqualTo(0));
        Assert.That(rig.Boxes, Is.Empty);
        Assert.That(rig.C.Characters.Select(r => r.CharSN), Is.EqualTo(new[] { "12" }));
        Assert.That(rig.Rows, Is.EqualTo(1));
        Assert.That(rig.Sn(0), Is.EqualTo("12"));
        Assert.That(rig.C.InputOn, Is.True);
        Assert.That(emptied, Is.False);
        rig.C.OnDeleteEntry(0, "12");
        Assert.That(rig.Rows, Is.Zero);
        Assert.That(emptied, Is.True, "the last one clears the preview");
    }

    [Test]
    public void TheDeleteWaitCountsDownThenOffersTheFinalDelete()
    {
        // mode 5 error 0 (:970-986): FUN_1418e9fa0(time) and requestDelete(index, seconds), the renderer in the wait state on the
        // pending list; vfunc_15 writes s_char_delete_waiting with the time left each frame and at 0 invokes readyDelete (the final
        // delete and restore buttons). OnGameStart refuses a character with a delete time (:2259).
        var rig = LoadDelete(Row("A", "11", "d.png"), Row("B", "12", "d.png"));
        var c = rig.C;
        Ms2.Ui.Hud.MessageBoxData.CommonByKey = k => k switch
        {
            "s_char_delete_waiting" => "wait {0}", "s_common_hour_min" => "{0}h{1}m", "s_common_min_sec" => "{0}m{1}s",
            "s_common_sec" => "{0}s", "s_common_min" => "{0}m", "s_common_hour" => "{0}h", _ => Key(k),
        };
        c.OnBeginDelete("11", 0, rig.Now + 90_060);
        Assert.That(rig.Boxes, Is.Empty);
        Assert.That(rig.DeleteShown(0), Is.True, "the wait state");
        Assert.That(rig.FinalShown(0), Is.False);
        Assert.That(c.PendingDeletes, Is.EqualTo(new[] { "11" }));
        c.Tick();
        Assert.That(rig.DeleteLabel(0), Is.EqualTo("wait 25h1m"));
        rig.Tick += 90_000_000;
        c.Tick();
        Assert.That(rig.DeleteLabel(0), Is.EqualTo("wait 1m0s").Or.EqualTo("wait 1m"));
        Assert.That(c.OnCallback("OnGameStart", new object[] { "11" }), Is.False, "no game start while deleting");
        Assert.That(c.Starting, Is.False);
        rig.Tick += 60_000;
        c.Tick();
        Assert.That(c.PendingDeletes, Is.Empty);
        Assert.That(rig.FinalShown(0), Is.True, "readyDelete: the final delete button");

        // the ready state's buttons ask first (type 1 boxes); the final delete's OK sends CharacterManagement 4 once
        rig.H.Invoke(rig.Renderer(0), "onClickFinalDelete", (object)null);
        Assert.That(rig.Boxes, Is.EqualTo(new[] { ("<s_char_ask_delete_final>A", 1, "finalDelete,11") }));
        c.OnMessageBox(null, 1, "finalDelete,11");
        Assert.That(rig.Finals, Is.Empty, "cancel");
        c.OnMessageBox(null, 0, "finalDelete,11");
        c.OnMessageBox(null, 0, "finalDelete,11");
        Assert.That(rig.Finals, Is.EqualTo(new[] { "11" }));
        rig.Boxes.Clear();
        rig.H.Invoke(rig.Renderer(0), "onClickRevivalChar", (object)null);
        Assert.That(rig.Boxes, Is.EqualTo(new[] { ("<s_char_ask_revival_char>A", 1, "revivalChar,11") }));
        c.OnMessageBox(null, 0, "revivalChar,11");
        Assert.That(rig.Cancels, Is.EqualTo(new[] { "11" }), "the restore's OK sends CharacterManagement 3");
        c.OnDeleteEntry(0, "11");
        Assert.That(c.Characters.Select(r => r.CharSN), Is.EqualTo(new[] { "12" }));
    }

    [Test]
    public void TheWaitsCancelButtonEndsTheWait()
    {
        // the wait state's cancel button (onClickCancelDelete :2314): CancelDelete zeroes the seconds and sends
        // CharacterManagement 3; mode 6 error 0 (:1006-1013) clears the delete time and puts the row back (SetDeleteState)
        var rig = LoadDelete(Row("A", "11", "d.png") with { DeleteTime = 1_800_000_000 + 600 }, Row("B", "12", "d.png"));
        var c = rig.C;
        Assert.That(rig.DeleteShown(0), Is.True, "a listed character with a delete time loads in its wait (waitDelTime)");
        Assert.That(rig.FinalShown(0), Is.False);
        rig.H.Invoke(rig.Renderer(0), "onClickCancelDelete", (object)null);
        Assert.That(rig.Cancels, Is.EqualTo(new[] { "11" }));
        Assert.That(c.DeleteSecondsLeft("11"), Is.Zero);
        c.OnCancelDelete("11", 0);
        Assert.That(rig.DeleteShown(0), Is.False);
        Assert.That(c.PendingDeletes, Is.Empty);
        Assert.That(c.DeleteTimeOf("11"), Is.Zero);
        Assert.That(c.OnCallback("OnGameStart", new object[] { "11" }), Is.True, "the game start is back");
    }

    [Test]
    public void ACharacterPastItsWaitLoadsReadyForTheFinalDelete()
    {
        // waitDelTime 0 (FUN_1418e9fa0 of a past time): the renderer's requestDelete(0) is the ready state; nothing pending
        var rig = LoadDelete(Row("A", "11", "d.png") with { DeleteTime = 1_800_000_000 - 5 });
        Assert.That(rig.DeleteShown(0), Is.True);
        Assert.That(rig.FinalShown(0), Is.True);
        Assert.That(rig.C.PendingDeletes, Is.Empty);
        Assert.That(rig.C.OnCallback("OnGameStart", new object[] { "11" }), Is.False);
    }

    [Test]
    public void EveryDeleteStringIsInTheData()
    {
        // the keys the delete flow shows, in this data's common strings. The GMS2 data's stringcommon has no s_char_err_wedding
        // (KMS2 and KMS2_DEV have every key): that code's box is empty there, as GetStringCommonByKey finds nothing
        Load();
        var common = new Ms2.Formats.World.GameStrings(packs, "en").Common();
        var keys = DeleteErrorKeys.Values.Concat(new[] { "s_char_err_destroy", "s_char_err_next_delete_char_date", "s_char_err_delete_name",
            "s_char_delete_waiting", "s_char_ask_delete_final", "s_char_ask_revival_char", "s_char_ask_delete", "s_char_ask_delete_with_wait",
            "s_char_ask_delete_confirm_checkbox" }).ToArray();
        var missing = keys.Where(k => string.IsNullOrEmpty(common(k))).ToArray();
        TestContext.WriteLine($"delete strings: {keys.Length} keys, {missing.Length} missing: {string.Join(", ", missing)}");
        Assert.That(missing, Is.SubsetOf(new[] { "s_char_err_wedding" }));
        if (common("s_char_ask_delete") is { } ask && ask.Contains("{0}") == false) Assert.Fail("s_char_ask_delete has no name argument");
    }
}
