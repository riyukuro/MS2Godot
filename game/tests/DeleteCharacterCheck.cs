using Godot;
using Ms2.Ui.Hud;
using Ms2Godot.Flow;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Live check of the character list's delete (res://tests/delete_character.tscn, with the launcher's --ip and --port after
/// `--`): log in (MS2_RETRY_ACCOUNT=user:password), then the comma-separated --steps in order, each on a character by name:
/// <list type="bullet">
/// <item>create:N: the list's Create, the job page's first job and the look page's Create with N.</item>
/// <item>enter:N: N's row selected and started; the game shows N.</item>
/// <item>back: the system menu's character select back to the list.</item>
/// <item>readmail: in the game, the mail list opened and its mails read one by one until none on the list is unread (the server's Read answer sets each one's read time).</item>
/// <item>refused:N:KEY: the list's Delete, N typed in the box with its checkbox, OK; the client's box with common string
/// KEY comes (ShowDeleteError) and N stays on the list and in the movie.</item>
/// <item>delete:N: the same, and N leaves the list and the movie with no box.</item>
/// <item>wait:N: the same for a character at or over CharacterDestroyDivisionLevel: N's row goes to the wait state with
/// s_char_delete_waiting and the time left; the selection leaves it and selecting it is refused.</item>
/// <item>cancel:N: the wait state's cancel button: N's row is back with no delete state.</item>
/// <item>final:N: N's row in the ready state (its wait over), the final delete button and its box's OK: N leaves the list.</item>
/// </list>
/// A shot after each step into MS2_SHOTS (step&lt;i&gt;_&lt;kind&gt;_&lt;N&gt;.png). Quits with 0 when every step did what it says, else 1.
/// </summary>
public partial class DeleteCharacterCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new DeleteCharacterDriver());
        GameFlow.Start(GetTree());
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene changes.</summary>
public partial class DeleteCharacterDriver : Node
{
    int failures, stepNo;

    async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async System.Threading.Tasks.Task<bool> Until(System.Func<bool> cond, int maxFrames)
    {
        for (int i = 0; i < maxFrames; i++) { if (cond()) return true; await Frames(1); }
        return cond();
    }
    GameFlow Flow => GetTree().Root.GetNodeOrNull<GameFlow>("GameFlow");
    CharacterListScene List => GetTree().CurrentScene as CharacterListScene;
    Ms2.Ui.CharacterListController Dialog => List?.Controller;

    static void Key(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    /// <summary>The list takes input with no box up; <paramref name="anyCharacter"/>: with a character on it (the return
    /// empties the list until the login server's list comes).</summary>
    async System.Threading.Tasks.Task<bool> ListReady(bool anyCharacter = true) =>
        await Until(() => List is { ListReady: true } && MessageBoxQueue.IsIdle && (!anyCharacter || Session.List().Count > 0), 3600)
        || Fail("character select never took input");

    static string Sn(string name) => Session.List().Find(c => c.User.Name == name)?.User.CharacterId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The movie's row of character <paramref name="sn"/> (its renderer's data.charSN), -1 without one.</summary>
    int MovieRow(string sn)
    {
        if (Dialog?.Host is not { } h) return -1;
        for (int i = 0; i < 8; i++)
            if (h.Invoke(h.Entity, "getCharacterRenderer", i) is { } r && Ms2.Avm2.Rt.ToString(h.Get(h.Get(r, "data"), "charSN")) == sn) return i;
        return -1;
    }

    object Renderer(int row) => Dialog?.Host is { } h ? h.Invoke(h.Entity, "getCharacterRenderer", row) : null;
    bool Visible(object r, string member) => Dialog.Host.Get(r, member) is { } m && Ms2.Avm2.Rt.ToBoolean(Dialog.Host.Get(m, "visible"));
    string DeleteLabel(object r) => Ms2.Avm2.Rt.ToString(Dialog.Host.Get(Dialog.Host.Get(Dialog.Host.Get(r, "m_delete"), "m_label"), "text"));

    void Shot(string kind, string name)
    {
        var dir = System.Environment.GetEnvironmentVariable("MS2_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        string file = $"step{stepNo}_{kind}_{name}.png";
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, file));
        GD.Print($"delete character: shot {file}");
    }

    async System.Threading.Tasks.Task<bool> Select(string name)
    {
        if (!await ListReady()) return false;
        int row = Session.List().FindIndex(c => c.User.Name == name);
        if (row < 0) return Fail($"'{name}' is not on the list");
        // the dialog's character (MyPC, our Session.SelectedIndex) is what OnDeletePC and the start button take; it follows the
        // movie's OnSelectCharacter, so a movie index already on the row with another character previewed is moved off first
        if (List.ListIndex != row || Session.SelectedIndex != row)
        {
            if (List.ListIndex == row) { List.SelectRow(-1); await Frames(5); }
            List.SelectRow(row);   // the preview's map load
            await Frames(10);
            if (!await ListReady()) return false;
        }
        await Frames(30);
        return List.ListIndex == row && Session.SelectedIndex == row
            || Fail($"row {row} of '{name}' was not selected (movie row {List.ListIndex}, dialog row {Session.SelectedIndex})");
    }

    /// <summary>The list's Delete (OnDeletePC) on <paramref name="name"/>'s row, the name typed in the box with its checkbox,
    /// and OK. Returns the box's text.</summary>
    async System.Threading.Tasks.Task<string> AskDelete(string name)
    {
        if (!await Select(name)) return null;
        Dialog.OnCallback("OnDeletePC", System.Array.Empty<object>());
        if (!await Until(() => MessageBoxLayer.Instance is { Shown: true }, 600)) { Fail("no delete box"); return null; }
        await Frames(30);
        string ask = MessageBoxQueue.Current()?.Msg;
        GD.Print($"delete character: the delete box asks '{ask}'");
        MessageBoxLayer.Instance.Type(name);
        MessageBoxLayer.Instance.Tick(0);
        await Frames(10);
        Shot("ask", name);
        MessageBoxLayer.Instance.Click(0);
        return ask;
    }

    async System.Threading.Tasks.Task<bool> Step(string step)
    {
        stepNo++;
        var parts = step.Split(':');
        string kind = parts[0], name = parts.Length > 1 ? parts[1] : "";
        GD.Print($"delete character: step {stepNo} {step}");
        switch (kind)
        {
            case "create":
            {
                if (!await ListReady(false)) return false;
                Dialog.OnCallback("OnCreatePC", System.Array.Empty<object>());
                if (!await Until(() => Flow.Current == GameFlow.State.Create, 1800)) return Fail("the create page never opened");
                await Frames(180);
                Ui.Create.CharacterCreateScene.PickJobScripted();
                if (!await Until(() => Ui.Create.CharacterCreateScene.OnCreatePage, 1800)) return Fail("the look page never opened");
                await Frames(120);
                Ui.Create.CharacterCreateScene.ConfirmScripted(name);
                if (!await Until(() => Flow.Current == GameFlow.State.Lobby && Sn(name) != null, 1800)) return Fail($"'{name}' was not created");
                if (!await ListReady()) return false;
                Shot(kind, name);
                return true;
            }
            case "enter":
            {
                if (!await Select(name)) return false;
                long id = long.Parse(Sn(name));
                Key(Godot.Key.Enter);
                if (!await Until(() => Flow.Current == GameFlow.State.Game, 3600)) return Fail("the game state was never reached");
                var live = Net.LiveSession.Instance;
                if (!await Until(() => live.Me.User?.CharacterId == id && live.OwnPlayer != null, 1800)) return Fail($"'{name}' never came into the game");
                await Frames(240);
                GD.Print($"delete character: in the game as '{live.Me.User?.Name}', {live.Mails.Unread} unread mails");
                Shot(kind, name);
                return true;
            }
            case "back":
            {
                Net.LiveSession.Instance?.SendRequestQuit();
                if (!await Until(() => Flow.Current == GameFlow.State.Lobby, 1800)) return Fail("the return never reached character select");
                if (!await ListReady()) return false;
                Shot(kind, "list");
                return true;
            }
            case "readmail":
            {
                if (GetTree().CurrentScene is not HudScene hud) return Fail("not in the game");
                var live = Net.LiveSession.Instance;
                int unread = live.Mails.Unread;      // the menu count (Notify); the list's opening sets it to 0, as the client does
                hud.OpenMailScripted();
                if (!await Until(() => hud.MailList != null, 600))
                    return Fail(Ms2.Ui.Guide.GuideManager.RefusesDialogOpen("MailListDialog")
                        ? "the mail list never opened: the guide holds it shut (s_tutorial_dialog_limit)" : "the mail list never opened");
                await Frames(180);
                int Listed() => live.Mails.Mails.Count(m => !m.IsRead);
                int listed = Listed();
                for (int i = 0; i < 8 && Listed() > 0; i++)
                {
                    hud.ClickMail("m_tab", "tab:0");
                    await Frames(60);
                    if (!hud.ClickMail("m_list", "row:" + i)) break;
                    await Until(() => Listed() < listed, 300);
                    listed = Listed();
                }
                Shot(kind, "mail");
                GD.Print($"delete character: mail read, menu count {unread}, unread on the list {live.Mails.Mails.Count} mails -> {Listed()}");
                Key(Godot.Key.Escape);
                await Frames(60);
                return Listed() == 0 || Fail($"{Listed()} mails still unread");
            }
            case "refused":
            case "delete":
            case "wait":
            {
                string sn = Sn(name);
                int before = Session.List().Count;
                if (await AskDelete(name) == null) return false;
                if (kind == "refused")
                {
                    string want = MessageBoxData.CommonByKey(parts[2]) ?? "";
                    if (!await Until(() => MessageBoxQueue.Current()?.Msg == want, 900))
                        return Fail($"no '{parts[2]}' box (shown: '{MessageBoxQueue.Current()?.Msg}')");
                    await Frames(30);
                    Shot(kind, name);
                    GD.Print($"delete character: refused with '{want}'");
                    MessageBoxLayer.Instance.Click(0);
                    await Frames(30);
                    if (Session.List().Count != before || Sn(name) == null || MovieRow(sn) < 0) return Fail($"'{name}' left the list");
                    return true;
                }
                if (kind == "delete")
                {
                    if (!await Until(() => Sn(name) == null && MovieRow(sn) < 0, 900)) return Fail($"'{name}' is still on the list (box '{MessageBoxQueue.Current()?.Msg}')");
                    await Frames(120);
                    Shot(kind, name);
                    if (MessageBoxLayer.Instance?.Shown == true) return Fail($"a box after the delete: '{MessageBoxQueue.Current()?.Msg}'");
                    GD.Print($"delete character: '{name}' deleted, {Session.List().Count} characters, {Dialog.Characters.Count} rows");
                    return Dialog.Characters.Count == Session.List().Count || Fail("the dialog's rows and the list differ");
                }
                if (!await Until(() => Dialog.DeleteTimeOf(sn) > 0, 900)) return Fail($"'{name}' got no delete wait (box '{MessageBoxQueue.Current()?.Msg}')");
                await Frames(60);
                var r = Renderer(MovieRow(sn));
                GD.Print($"delete character: '{name}' waits {Dialog.DeleteSecondsLeft(sn)} s, the row says '{DeleteLabel(r)}', final delete button {(Visible(r, "m_finalDelete") ? "shown" : "hidden")}");
                Shot(kind, name);
                if (!Visible(r, "m_delete") || Visible(r, "m_finalDelete")) return Fail("the row is not in the wait state");
                string waiting = (MessageBoxData.CommonByKey("s_char_delete_waiting") ?? "").Split("{0}")[0];
                if (waiting.Length == 0 || !DeleteLabel(r).StartsWith(waiting)) return Fail($"the wait label is not s_char_delete_waiting ('{waiting}')");
                // the movie's requestDelete moves the selection off the waiting row (selectedNewValidIndex, its own
                // OnSelectCharacter) and its selectedIndex setter refuses a waiting row, so neither the start button nor a row
                // click reaches the waiting character; the dialog also refuses it in OnGameStart (:2259)
                int at = Session.List().FindIndex(c => c.User.Name == name);
                List.SelectRow(at);
                await Frames(30);
                string Picked() => Session.SelectedIndex >= 0 && Session.SelectedIndex < Session.List().Count ? Session.List()[Session.SelectedIndex].User.Name : "";
                GD.Print($"delete character: the waiting row {at} selected: movie row {List.ListIndex}, the dialog's character '{Picked()}'");
                return List.ListIndex != at && Session.SelectedIndex != at || Fail("the waiting row could be selected");
            }
            case "cancel":
            {
                if (!await ListReady()) return false;
                string sn = Sn(name);
                var r = Renderer(MovieRow(sn));
                Dialog.Host.Invoke(r, "onClickCancelDelete", (object)null);
                if (!await Until(() => Dialog.DeleteTimeOf(sn) == 0, 900)) return Fail("the wait was not cancelled");
                await Frames(60);
                Shot(kind, name);
                return !Visible(Renderer(MovieRow(sn)), "m_delete") || Fail("the row still shows its delete state");
            }
            case "final":
            {
                if (!await ListReady()) return false;
                string sn = Sn(name);
                var r = Renderer(MovieRow(sn));
                GD.Print($"delete character: '{name}' delete time {Dialog.DeleteTimeOf(sn)}, final delete button {(Visible(r, "m_finalDelete") ? "shown" : "hidden")}");
                Shot("ready", name);
                if (!Visible(r, "m_finalDelete")) return Fail("the row is not in the ready state");
                Dialog.Host.Invoke(r, "onClickFinalDelete", (object)null);
                if (!await Until(() => MessageBoxLayer.Instance is { Shown: true }, 600)) return Fail("no final delete box");
                await Frames(30);
                GD.Print($"delete character: the final delete box asks '{MessageBoxQueue.Current()?.Msg}'");
                Shot("finalask", name);
                MessageBoxLayer.Instance.Click(0);
                if (!await Until(() => Sn(name) == null && MovieRow(sn) < 0, 900)) return Fail($"'{name}' is still on the list (box '{MessageBoxQueue.Current()?.Msg}')");
                await Frames(120);
                Shot(kind, name);
                return true;
            }
        }
        return Fail("unknown step " + step);
    }

    public override async void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        string steps = "";
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--steps") steps = args[i + 1];
        var acct = (System.Environment.GetEnvironmentVariable("MS2_RETRY_ACCOUNT") ?? "").Split(':', 2);
        if (!await Until(() => Flow?.Current == GameFlow.State.Login && GetTree().CurrentScene is LoginScene s && s.Controller.Host?.Entity != null, 3000)) { Fail("no login dialog"); Done(); return; }
        await Frames(60);
        var c = ((LoginScene)GetTree().CurrentScene).Controller;
        foreach (var (field, text) in new[] { ("m_inputID", acct[0]), ("m_inputPW", acct.Length > 1 ? acct[1] : "") })
            if (c.Host.Get(c.Host.Entity, field) is { } input) c.Host.Set(input, "text", text);
        await Frames(5);
        Key(Godot.Key.Enter);
        if (!await Until(() => Flow.Current == GameFlow.State.Lobby, 1800)) { Fail("the login did not reach character select"); Done(); return; }
        await Frames(120);
        GD.Print($"delete character: {Session.List().Count} characters: {string.Join(", ", Session.List().Select(e => $"{e.User.Name} L{e.User.Level} del {e.DeleteTime}"))}");
        foreach (var step in steps.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
            if (!await Step(step)) break;
        Done();
    }

    bool Fail(string why)
    {
        failures++;
        GD.Print($"delete character: FAIL step {stepNo}: {why}");
        return false;
    }

    void Done()
    {
        GD.Print($"delete character: {(failures == 0 ? "ok" : "FAIL")}, {stepNo} steps, {failures} failures");
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
