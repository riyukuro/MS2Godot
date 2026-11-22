using Godot;
using Ms2.Formats.Options;
using Ms2.Ui.Hud;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Display check (res://tests/login_typing.tscn; args after `--`: --size WxH for a full window of that content size,
/// --typing-shots DIR for the picture after each field): the login screen as the game opens it, with the run's message box
/// layer and tooltip layer under the tree's root (<see cref="Flow.GameFlow"/>), takes typed keys into both fields. The
/// keys go through the engine's input as a keyboard's do (key events with their characters): an id into the id field
/// the dialog focuses, Tab to the password field, a password; then a message box over the dialog, the keypad's Enter, which
/// neither closes the box nor logs in, and Enter, which closes the box and sends no login (the client asks the top dialog first, UISwfEntityManager @0x1422d1c20). The idle message box layer, the root's last child, took
/// every key first, so nothing reached the login dialog. With the id typed, a script selection of its second to fourth
/// characters shows on the focused field (TextNode.SetSelection): those glyphs white over one black rectangle from the
/// second glyph's pen to the fourth one's end, the others in the field's colour; after Tab the id field has lost the
/// focus and shows none (TextField::OnFocus drops it). Quits with 0 when both fields hold what was typed and the
/// selection showed so, else 1.
/// </summary>
public partial class LoginTypingCheck : Node
{
    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        var driver = new LoginTypingDriver();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--typing-shots") driver.ShotDir = args[i + 1];
            if (args[i] == "--size" && args[i + 1].Split('x') is [var w, var h]) driver.Size = new Vector2I(int.Parse(w), int.Parse(h));
        }
        if (driver.Size is { } s) ScreenMode.ApplyRequest(new WindowRequest(false, true, s.X, s.Y));
        MessageBoxLayer.Ensure(this);   // as GameFlow._Ready: the run's layers, then the login scene
        TooltipLayer.Ensure(this);
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, driver);
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, Flow.GameFlow.LoginScene);
    }
}

/// <summary>The check's steps; under the tree's root, so it outlives the scene change to the login screen.</summary>
public partial class LoginTypingDriver : Node
{
    public string ShotDir;
    public Vector2I? Size;
    const string Id = "abc12", Password = "secret";
    int frames, connects;
    bool typed, keypadKeptBox;
    string selection, unselected;
    Ms2.Avm2.Flash.AsTextField idField;

    static void Press(Key key, char ch)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = ch, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    static void Type(string text)
    {
        foreach (var ch in text) Press(char.IsAsciiLetter(ch) ? (Key)char.ToUpperInvariant(ch) : (Key)ch, ch);
    }

    void Shot(string name)
    {
        if (ShotDir == null) return;
        DirAccess.MakeDirRecursiveAbsolute(ShotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(ShotDir, name));
    }

    /// <summary>
    /// What is wrong with the id field's selection as drawn, null when it is right: <paramref name="shown"/>, the
    /// characters 1 to 3 white over one black rectangle from glyph 1's pen to glyph 3's end and the rest in the field's colour;
    /// else every glyph in the field's colour and no rectangle. Read from the field's text node (the private members are read
    /// by name so the check also builds on code without the selection).
    /// </summary>
    static string Selection(Ms2.Avm2.Flash.AsTextField tf, bool shown)
    {
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        if (tf == null) return "no id field";
        var content = tf.Tag?.GetType().GetField("Content", Any)?.GetValue(tf.Tag);
        if (content?.GetType().GetField("node", Any)?.GetValue(content) is not TextNode node) return "no text node";
        var color = node.Label.LabelSettings.FontColor;
        var lines = node.ShownLines();
        if (lines.Count != 1 || lines[0].Shaped.Count < Id.Length) return $"{lines.Count} lines";
        var line = lines[0];
        var rects = node.GetType().GetMethod("SelectionRects", Any)?.Invoke(node, null) as System.Collections.Generic.List<Rect2>;
        var glyphs = new System.Text.StringBuilder();
        bool colours = true;
        foreach (var g in line.Shaped)
        {
            var c = node.GlyphColor(g.Index);
            bool selected = shown && g.Index >= 1 && g.Index < 4;
            colours &= c == (selected ? Colors.White : color);
            glyphs.Append($"{g.Index}:{c.ToHtml()} ");
        }
        string got = $"face by line {node.FaceByLine}, glyphs {glyphs}rects {(rects == null ? "none drawn" : string.Join(" ", rects))}";
        if (!shown) return colours && rects is not { Count: > 0 } ? null : got;
        var g1 = line.Shaped.Find(g => g.Index == 1);
        var g3 = line.Shaped.Find(g => g.Index == 3);
        float x0 = line.LabelX + g1.Pen + line.Dx, x1 = line.LabelX + g3.Pen + line.Dx + g3.Advance * g3.Repeat;
        bool rect = rects is { Count: 1 } && Mathf.IsEqualApprox(rects[0].Position.X, x0) && Mathf.IsEqualApprox(rects[0].End.X, x1)
            && rects[0].Size.Y > 0 && rects[0].Position.Y <= line.Baseline && rects[0].End.Y >= line.Baseline;
        return node.FaceByLine && colours && rect ? null : got + $" (want x {x0}..{x1})";
    }

    public override void _Process(double delta)
    {
        frames++;
        if (GetTree().CurrentScene is not LoginScene login)
        {
            if (frames > 600) { GD.Print("login typing: FAIL no login scene"); GetTree().Quit(1); }
            return;
        }
        if (frames == 120) Type(Id);
        if (frames == 140)
        {
            idField = login.Controller.Host.Get(login.Controller.Host.Entity, "m_inputID") is { } input
                && login.Controller.Host.Get(input, "textField") is Ms2.Avm2.Flash.AsTextField tf ? tf : null;
            idField?.setSelection(1, 4);
        }
        if (frames == 146) selection = Selection(idField, true);
        if (frames == 150) { Shot("login_id_typed.png"); Press(Key.Tab, '\t'); }
        if (frames == 153) unselected = Selection(idField, false);
        if (frames == 155) Type(Password);
        if (frames == 185)
        {
            Shot("login_password_typed.png");
            typed = login.Controller.Id == Id && login.Controller.Password.Length == Password.Length;
            // a box over the filled dialog (as the wrong-password answer's): Enter is the box's, not a new login
            login.Controller.Connect += () => connects++;
            MessageBoxQueue.ShowMessageBox(Id, 0, null, "");
        }
        // the keypad's Enter is not Return (DIK 0x9c; the box and the login dialog read DIK 0x1c): the box stays, no login
        if (frames == 200) Press(Key.KpEnter, '\r');
        if (frames == 210) keypadKeptBox = !MessageBoxQueue.IsIdle && connects == 0;
        if (frames == 215) Press(Key.Enter, '\r');
        if (frames == 230) Press(Key.KpEnter, '\r');   // and on the bare dialog: no login either
        if (frames < 245) return;
        bool boxClosed = MessageBoxQueue.IsIdle, ok = typed && keypadKeptBox && boxClosed && connects == 0 && selection == null && unselected == null;
        GD.Print($"login typing: window {GetTree().Root.Size}, content {GetTree().Root.GetVisibleRect().Size}, id \"{login.Controller.Id}\", password {login.Controller.Password.Length} chars, keypad Enter kept the box {keypadKeptBox}, Enter on a box: closed {boxClosed}, logins {connects}, selection on the focused id: {selection ?? "shown"}, after Tab: {unselected ?? "none"} -> {(ok ? "ok" : "FAIL")}");
        SetProcess(false);
        GetTree().Quit(ok ? 0 : 1);
    }
}
