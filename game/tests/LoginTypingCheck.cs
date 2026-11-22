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
/// focus and shows none (TextField::OnFocus drops it). A script text change under the kept range redraws the rectangle
/// over the new glyphs (the label's relayout, TextNode.AlignLine); with an outline on the field's label and a dark text
/// colour the selected glyphs are still drawn white over the rectangle (the client draws the outline in its own colour
/// and the text in the highlighter's, TextLayer_Shadow). Quits with 0 when both fields hold what was typed and the
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
    string selection, unselected, relaid, outlined;
    System.Collections.Generic.List<Rect2> before;
    LabelSettings saved;
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
    const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    static TextNode NodeOf(Ms2.Avm2.Flash.AsTextField tf)
    {
        var content = tf?.Tag?.GetType().GetField("Content", Any)?.GetValue(tf.Tag);
        return content?.GetType().GetField("node", Any)?.GetValue(content) as TextNode;
    }

    static System.Collections.Generic.List<Rect2> Rects(TextNode node) =>
        node?.GetType().GetMethod("SelectionRects", Any)?.Invoke(node, null) is System.Collections.Generic.List<Rect2> r ? new(r) : null;

    /// <summary>
    /// What is wrong with the highlight after the id's text changed under the kept range, null when it is right: the rects
    /// it last drew are the field's current ones, and they moved from <paramref name="old"/>.
    /// </summary>
    static string Relaid(TextNode node, System.Collections.Generic.List<Rect2> old)
    {
        if (node == null) return "no text node";
        var now = Rects(node);
        var highlight = node.Label.GetNodeOrNull("Selection");
        var drawn = highlight?.GetType().GetProperty("Drawn")?.GetValue(highlight) as System.Collections.Generic.IReadOnlyList<Rect2>;
        string got = $"drawn {(drawn == null ? "not recorded" : string.Join(" ", drawn))}, now {(now == null ? "none" : string.Join(" ", now))}, before {(old == null ? "none" : string.Join(" ", old))}";
        if (drawn == null || now == null || old == null || now.Count == 0) return got;
        bool same = drawn.Count == now.Count, moved = now.Count != old.Count;
        for (int i = 0; same && i < now.Count; i++) same = drawn[i].IsEqualApprox(now[i]);
        for (int i = 0; !moved && i < now.Count; i++) moved = !now[i].IsEqualApprox(old[i]);
        return same && moved ? null : got;
    }

    /// <summary>
    /// What is wrong with the selected glyphs on the outlined label, null when they are right: the rendered picture inside
    /// the selection's rect (2 pixels in from its sides, past the caret and the neighbours' outlines) holds white glyph pixels.
    /// A headless run draws no picture: there the label must keep its face with the selection drawn over it
    /// (TextNode.SelectionOverFace, read by name).
    /// </summary>
    static string Outlined(TextNode node)
    {
        if (node == null) return "no text node";
        if (DisplayServer.GetName() == "headless")
            return node.GetType().GetProperty("SelectionOverFace")?.GetValue(node) is true && !node.FaceByLine
                ? null : $"headless: selection over the face {node.GetType().GetProperty("SelectionOverFace")?.GetValue(node) ?? "unknown"}, face by line {node.FaceByLine}";
        var rects = Rects(node);
        if (rects is not { Count: 1 }) return $"rects {(rects == null ? "none" : string.Join(" ", rects))}";
        var vp = node.Label.GetViewport();
        var image = vp.GetTexture().GetImage();
        var r = vp.GetFinalTransform() * node.Label.GetGlobalTransformWithCanvas() * rects[0];
        int x0 = Mathf.CeilToInt(r.Position.X) + 2, x1 = Mathf.FloorToInt(r.End.X) - 2;
        int y0 = Mathf.CeilToInt(r.Position.Y), y1 = Mathf.FloorToInt(r.End.Y);
        int white = 0, dark = 0;
        for (int y = System.Math.Max(y0, 0); y < System.Math.Min(y1, image.GetHeight()); y++)
            for (int x = System.Math.Max(x0, 0); x < System.Math.Min(x1, image.GetWidth()); x++)
            {
                var c = image.GetPixel(x, y);
                if (c.R > 0.85f && c.G > 0.85f && c.B > 0.85f) white++;
                else if (c.R < 0.15f && c.G < 0.15f && c.B < 0.15f) dark++;
            }
        string got = $"rect {r} in the picture: {white} white, {dark} black pixels";
        return white >= 10 && dark > white ? null : got;
    }

    static string Selection(Ms2.Avm2.Flash.AsTextField tf, bool shown)
    {
        if (tf == null) return "no id field";
        if (NodeOf(tf) is not TextNode node) return "no text node";
        var color = node.Label.LabelSettings.FontColor;
        var lines = node.ShownLines();
        if (lines.Count != 1 || lines[0].Shaped.Count < Id.Length) return $"{lines.Count} lines";
        var line = lines[0];
        var rects = Rects(node);
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
        if (frames == 146)
        {
            selection = Selection(idField, true);
            before = Rects(NodeOf(idField));
            if (idField != null) idField.text = "WWW12";   // a script text change: the range stays 1..4
        }
        if (frames == 152)
        {
            relaid = Relaid(NodeOf(idField), before);
            if (idField != null) idField.text = Id;
        }
        if (frames == 154 && NodeOf(idField) is TextNode outline)
        {
            // an outline of the label's own and a dark text colour, as an outlined field's
            saved = outline.Label.LabelSettings;
            var ls = (LabelSettings)saved.Duplicate();
            ls.FontColor = new Color(0.1f, 0.1f, 0.1f);
            ls.OutlineSize = 2;
            ls.OutlineColor = new Color(0.8f, 0.1f, 0.1f);
            outline.Label.LabelSettings = ls;
        }
        if (frames == 162)
        {
            var node = NodeOf(idField);
            outlined = Outlined(node);
            Shot("login_id_outlined.png");
            if (node != null && saved != null) node.Label.LabelSettings = saved;
        }
        if (frames == 170) { Shot("login_id_typed.png"); Press(Key.Tab, '\t'); }
        if (frames == 173) unselected = Selection(idField, false);
        if (frames == 175) Type(Password);
        if (frames == 205)
        {
            Shot("login_password_typed.png");
            typed = login.Controller.Id == Id && login.Controller.Password.Length == Password.Length;
            // a box over the filled dialog (as the wrong-password answer's): Enter is the box's, not a new login
            login.Controller.Connect += () => connects++;
            MessageBoxQueue.ShowMessageBox(Id, 0, null, "");
        }
        // the keypad's Enter is not Return (DIK 0x9c; the box and the login dialog read DIK 0x1c): the box stays, no login
        if (frames == 220) Press(Key.KpEnter, '\r');
        if (frames == 230) keypadKeptBox = !MessageBoxQueue.IsIdle && connects == 0;
        if (frames == 235) Press(Key.Enter, '\r');
        if (frames == 250) Press(Key.KpEnter, '\r');   // and on the bare dialog: no login either
        if (frames < 265) return;
        bool boxClosed = MessageBoxQueue.IsIdle, ok = typed && keypadKeptBox && boxClosed && connects == 0 && selection == null && unselected == null && relaid == null && outlined == null;
        GD.Print($"login typing: window {GetTree().Root.Size}, content {GetTree().Root.GetVisibleRect().Size}, id \"{login.Controller.Id}\", password {login.Controller.Password.Length} chars, keypad Enter kept the box {keypadKeptBox}, Enter on a box: closed {boxClosed}, logins {connects}, selection on the focused id: {selection ?? "shown"}, after a text change: {relaid ?? "redrawn"}, outlined: {outlined ?? "white glyphs"}, after Tab: {unselected ?? "none"} -> {(ok ? "ok" : "FAIL")}");
        SetProcess(false);
        GetTree().Quit(ok ? 0 : 1);
    }
}
