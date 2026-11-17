using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// A game menu button (scaleform.ms2 SystemDropdown on a UIGameMenuDialog_dropdown_* clip). The clip's own frames only
/// move two copies of the menu icon: m_layerBtn (scaled up on over) and m_layerEffect (the same icon, blend add, faded in
/// on over). The icon is commonResource's export_common_menu_[big_]&lt;layerName&gt;. With <see cref="Items"/> the button
/// opens a list on roll-over (showDropdown: UIGameMenuDialog_list + UIGameMenuDialog_renderer rows) and shows the last
/// row's icon while it is open.
/// </summary>
public partial class NMenuButton : Control
{
    public string Movie = "uigamemenudialog";
    public string Clip = "UIGameMenuDialog_dropdown_menu";
    public bool Big;
    public string LayerName = "";
    public List<(string Name, string Caption)> Items = new();
    public Func<string> TooltipText;

    [Signal] public delegate void MenuEventHandler(string name);

    Dictionary<string, int> labels; int frames; float fps = 30;
    List<(Transform2D Xf, float Alpha)> btnTrack, fxTrack;
    NativeSkin icon;
    string shownLayer;
    int frame, target; double acc;
    readonly CanvasItemMaterial addMat = new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    Control effect;
    Control list; Tween fade;

    public override void _Ready()
    {
        var art = NativeUi.Art;
        (labels, frames) = art.Timeline(Movie, Clip);
        fps = (float)art.Fps(Movie, Clip);
        btnTrack = art.Track(Movie, Clip, "m_layerBtn", 0, frames - 1);
        fxTrack = art.Track(Movie, Clip, "m_layerEffect", 0, frames - 1);
        Size = new Vector2(37, 44);                               // m_hitArea
        MouseFilter = MouseFilterEnum.Stop;
        // the effect copy draws with blend add, so it lives in its own canvas item
        effect = new Control { MouseFilter = MouseFilterEnum.Ignore, Material = addMat };
        effect.Draw += () => DrawLayer(effect, fxTrack);
        AddChild(effect);
        SetLayer(LayerName);
        MouseEntered += () => { SetState("over"); if (Items.Count > 0) ShowList(); };
        MouseExited += () => { SetState("up"); if (list != null && !list.GetGlobalRect().HasPoint(GetGlobalMousePosition())) HideList(); };
        if (TooltipText != null) NTooltip.Attach(this, TooltipText);
    }

    void SetLayer(string name)
    {
        shownLayer = name;
        icon = NativeUi.Art.Bake("commonresource", (Big ? "export_common_menu_big_" : "export_common_menu_") + name);
        QueueRedraw(); effect?.QueueRedraw();
    }

    void DrawLayer(CanvasItem ci, List<(Transform2D Xf, float Alpha)> track)
    {
        if (icon == null || track.Count == 0) return;
        var (xf, a) = track[Math.Clamp(frame, 0, track.Count - 1)];
        if (a <= 0.001f) return;
        ci.DrawSetTransformMatrix(xf);
        ci.DrawTextureRect(icon.Texture, new Rect2(icon.Origin, icon.Size), false, new Color(1, 1, 1, a));
        ci.DrawSetTransformMatrix(Transform2D.Identity);
    }

    public override void _Draw() => DrawLayer(this, btnTrack);

    public void SetState(string s)
    {
        if (!labels.TryGetValue(s, out int f)) return;
        frame = f; target = NativeArt.RestFrame(labels, frames, s); acc = 0;
        QueueRedraw(); effect.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (frame >= target) return;
        acc += delta;
        while (acc >= 1 / fps && frame < target) { acc -= 1 / fps; frame++; }
        QueueRedraw(); effect.QueueRedraw();
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
        if (mb.Pressed) SetState("down");
        else { SetState("over"); if (Items.Count == 0) EmitSignal(SignalName.Menu, LayerName); }
        AcceptEvent();
    }

    // ---- the dropdown list ------------------------------------------------------------------------------------------

    /// <summary>
    /// The renderer's parts its setData leaves hidden with no notifications or events (the notify badges, the event tag),
    /// and m_icon, which gets the row's own icon below.
    /// </summary>
    const string RendererHidden = "m_notifyCount,m_notifyRedCircle,m_notifyNew,m_notifyUp,m_event,m_icon";
    const float MenuWidth = 178, MenuHeight = 51, Margin = 2, PadTop = 1, PadBottom = 1;
    static readonly Vector2 Offset = new(37 / 2f - 178 / 2f, -6);   // menuOffset left / bottom

    public void ShowList()
    {
        if (list != null) return;
        list = new Control { MouseFilter = MouseFilterEnum.Stop, ZIndex = 5 };
        float h = MenuHeight * Items.Count + Margin * 2 + PadTop + PadBottom;
        var bg = new NSkin { Movie = Movie, Symbol = "UIGameMenuDialog_list/bg", Size = new Vector2(MenuWidth, h) };
        list.AddChild(bg);
        list.Size = new Vector2(MenuWidth, h);
        for (int i = 0; i < Items.Count; i++)
        {
            var (name, caption) = Items[i];
            // setData: m_splitLine shows only on the last row, and the last row (enabled) is selected
            bool isEnd = i == Items.Count - 1;
            var row = new NButton { Movie = Movie, Skin = "UIGameMenuDialog_renderer", Text = caption, Hide = isEnd ? RendererHidden : RendererHidden + ",m_splitLine", Selected = isEnd, Position = new Vector2(Margin, Margin + PadTop + MenuHeight * i) };
            list.AddChild(row);
            row.HitArea = new Rect2(0, 0, MenuWidth - 2 * Margin, MenuHeight);   // one row of the list
            var ic = new NSkin { Movie = "commonresource", Symbol = "export_common_menu_" + name, Position = new Vector2(12, 1), MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(ic);
            string n = name;
            row.Click += () => { HideList(); EmitSignal(SignalName.Menu, n); };
        }
        list.MouseExited += () => { if (!GetGlobalRect().HasPoint(GetGlobalMousePosition()) && !list.GetGlobalRect().HasPoint(GetGlobalMousePosition())) HideList(); };
        // PopUpManager.show at (x + offset.left, y - bg.height + offset.bottom) in the entry's space; it goes on top of
        // the whole menu container so the neighbouring entries do not take its mouse events
        var entry = GetParent<Control>();
        entry.GetParent().AddChild(list);
        var at = entry.Position + Position + new Vector2(Offset.X, -h + Offset.Y);
        list.Position = at + new Vector2(0, 50);
        list.Modulate = new Color(1, 1, 1, 0);
        SetLayer(Items[^1].Name);                                    // setData: layerName = the last row's name
        fade = list.CreateTween().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out).SetParallel();
        fade.TweenProperty(list, "modulate:a", 1f, 0.2);
        fade.TweenProperty(list, "position:y", at.Y, 0.2);
    }

    public void HideList()
    {
        if (list == null) return;
        fade?.Kill();
        list.QueueFree(); list = null;
        SetLayer(LayerName);                                         // hideDropdown: layerName = m_layerNameBackup
    }

    public bool ListOpen => list != null;
}
