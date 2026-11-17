using Godot;
using Ms2Godot.Ui.Contracts;
using C = Ms2Godot.Ui.Contracts.UICharacterCreateDialogContract;

namespace Ms2Godot.Ui.Create;

/// <summary>
/// The item option panel of a create category (CharacterCreateContent.m_movie, its m_custom CustomOption), ported from the
/// movie's AS3 (CustomOption.buildCustom / buildSliders / buildDegreeSlider / buildPosBtns) and the C++ dialog
/// (UICharacterCreateDialog::BuildCustom, OnChangeSize / OnChangeDegree / OnChangePosition).
/// It pops up when an editable item is picked (onChangeItem -> playMovie): the item's name, a size slider per range
/// (export_custom_mc_slider, listed from the last range down), the angle slider (0-359) and the position presets
/// (export_btn_custompos, the last one the disabled "custom" button). The guide movie (.usm) is not played.
/// </summary>
public sealed class CustomOptionPanel
{
    // export_slider_gauge: the track starts at x 1.8 and is 10 x 24.441 wide; the 22 wide thumb centres on the value
    // m_statusBar is a scale-9 sprite placed at (3, 7), 24.2 wide: it is built at that width, so it is scaled against it
    const float TrackX = 1.8f, TrackW = 244.41f, ThumbW = 22, BarX = 3, BarY = 7, BarW = 242f;
    static readonly string[] Labels = { "$s_common_custom_size", "$s_common_custom_length", "$s_common_custom_front_hair", "$s_common_custom_back_hair" };

    sealed class Slider
    {
        public MovieClipNode Row, Gauge;
        public float Min, Max, Value;
        public bool Whole;
        public Action<float> Changed;
    }

    readonly UiMovie movie;
    readonly Func<string, Texture2D> icon;
    readonly List<Node> added = new();
    readonly List<Slider> sliders = new();
    readonly List<(ButtonWidget Button, int Index)> posButtons = new();
    MovieClipNode shown, shownContent;
    Slider dragging;

    /// <summary>A value changed (the preview and the request read the item's values).</summary>
    public event Action Changed;

    public CustomOptionPanel(UiMovie movie, Func<string, Texture2D> icon) { this.movie = movie; this.icon = icon; }

    public bool Visible => shown != null && shown.IsVisibleInTree();

    /// <summary>stopMovie: the panel goes away.</summary>
    public void Hide()
    {
        Clear();
        shownContent?.SetNamedVisible("m_movie", false);
        shown = shownContent = null;
    }

    void Clear()
    {
        foreach (var n in added) { n.GetParent()?.RemoveChild(n); n.QueueFree(); }
        added.Clear(); sliders.Clear(); posButtons.Clear(); dragging = null;
    }

    /// <summary>onChangeItem with an editable item: playMovie (popup with the name) and the host's buildCustom(flags, data).</summary>
    public void Show(MovieClipNode content, string itemName, ItemCustomize c, CustomValues v, bool animate = true)
    {
        var holder = content?.FindClip("m_movie");
        var custom = holder?.FindClip("m_custom");
        var opts = custom?.FindClip("m_opts");
        if (holder == null || custom == null || opts == null) return;
        if (shown != holder) { Hide(); shown = holder; shownContent = content; }
        Clear();
        content.SetNamedVisible("m_movie", true);
        holder.GotoLabel("popup", animate);
        custom.SetNamedText("m_name", itemName ?? "");
        custom.SetNamedVisible("m_moviedummy", false);

        int flags = c.EditFlags;
        float y = 0, grow = 0, rowH = 0;
        int sliderRows = 0;
        if ((flags & 3) != 0)
        {
            int n = c.SliderCount;
            int first = (flags & 2) != 0 ? (n > 1 ? 2 : 1) : 0;
            if (first + n <= Labels.Length)
                for (int row = 0; row < n; row++)
                {
                    int id = n - 1 - row;               // sizedata runs from the last range down
                    var r = c.RangeAt(id);
                    var s = AddSlider(opts, Labels[first + row], ref y, ref rowH);
                    if (s == null) break;
                    s.Min = Math.Min(r.Min, r.Max); s.Max = Math.Max(r.Min, r.Max);
                    s.Changed = shownValue =>
                    {
                        UiBridge.Raise(C.Movie, C.ToHost.OnChangeSize, id, shownValue);
                        v.SetSize(c, id, r.Display(shownValue));
                    };
                    SetValue(s, r.Display(v.GetSize(c, id)), false);
                    grow += rowH;
                    sliderRows++;
                }
        }
        if ((flags & 4) != 0)
        {
            y = sliderRows * (rowH + 2);
            var s = AddSlider(opts, "$s_common_custom_degree", ref y, ref rowH);
            if (s != null)
            {
                s.Min = 0; s.Max = 359; s.Whole = true;
                s.Changed = deg => { UiBridge.Raise(C.Movie, C.ToHost.OnChangeDegree, (int)deg); v.SetDegree((int)deg); };
                SetValue(s, (int)v.Degree, false);
                grow += rowH;
            }
        }
        var presets = c.Positions;
        if ((flags & 8) != 0 && presets is { Count: > 0 })
            grow += AddPositions(opts, presets, v, c, sliders.Count * (rowH + 2));

        // m_bg grows by what was added (CustomOption.m_bg.height += ...)
        if (custom.ChildNamed("m_bg") is { } bg && bg.Node is MovieClipNode bgClip && bgClip.Bounds.Size.Y > 0)
        {
            // a scale-9 m_bg is built at its placed height (scale 1 left); a plain one keeps its 0.952 placement scale
            float baseH = bg.Static9 ? bgClip.Bounds.Size.Y : bgClip.Bounds.Size.Y * 0.952f;
            float sy = (float)Math.Ceiling(baseH + grow) / (bg.Static9 ? baseH : bgClip.Bounds.Size.Y);
            bg.Override = new Transform2D(new Vector2(1, 0), new Vector2(0, sy), Vector2.Zero);
            custom.GotoFrame(custom.Frame);
        }
    }

    Slider AddSlider(MovieClipNode opts, string label, ref float y, ref float rowH)
    {
        var row = movie.BuildClass("export_custom_mc_slider", opts.sx, opts.sy);
        if (row == null) return null;
        foreach (var clip in row.AllClips()) UiWidgets.Attach(clip);
        row.Position = new Vector2(0, y);
        opts.AddChild(row);
        added.Add(row);
        (row.FindClip("text")?.Widget as LabelWidget)?.Set("text", label);
        var gauge = row.FindClip("slider");
        gauge?.SetNamedVisible("m_tooltip", false);       // useTooltip = false
        if (rowH == 0) rowH = row.Bounds.Size.Y > 0 ? row.Bounds.Size.Y : 54.2f;
        y += rowH + 2;
        var s = new Slider { Row = row, Gauge = gauge };
        sliders.Add(s);
        return s;
    }

    float AddPositions(MovieClipNode opts, List<ItemCustomize.Preset> presets, CustomValues v, ItemCustomize c, float y)
    {
        var box = movie.BuildClass("export_custom_mc_pos", opts.sx, opts.sy);
        if (box == null) return 0;
        foreach (var clip in box.AllClips()) UiWidgets.Attach(clip);
        box.Position = new Vector2(0, y);
        opts.AddChild(box);
        added.Add(box);
        var text = box.FindClip("text");
        (text?.Widget as LabelWidget)?.Set("text", "$s_common_custom_position");
        float bx = (text?.Position.X ?? 0) + 16, by = (text?.Position.Y ?? 0) + (text?.Bounds.Size.Y ?? 22) + 2;
        int selected = v.Pick >= 0 && v.Pick < presets.Count ? v.Pick : presets.Count;
        float height = 0;
        for (int i = 0; i <= presets.Count; i++)
        {
            var btn = movie.BuildClass("export_btn_custompos", opts.sx, opts.sy);
            if (btn == null) break;
            UiWidgets.Attach(btn);
            var w = btn.Widget as ButtonWidget;
            float bw = btn.Bounds.Size.X > 0 ? btn.Bounds.Size.X : 54;
            btn.Position = new Vector2(bx + i * bw, by);
            box.AddChild(btn);
            height = Math.Max(height, by + btn.Bounds.Size.Y);
            if (i == presets.Count)
            {
                // the custom position button: a label, enabled only while it is the selection
                w?.Set("label", "$s_common_custom_btn_custom");
                if (w != null) w.Enabled = selected == presets.Count;
            }
            else
            {
                btn.SetText("");
                if (icon(presets[i].Icon) is { } tex)
                    btn.AddChild(new TextureRect
                    {
                        Texture = tex, Position = new Vector2(3, 3), Size = new Vector2(bw - 6, bw - 6),
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                    });
            }
            if (w != null)
            {
                w.Toggle = true;
                int index = i;
                w.SetSelected(i == selected, false);
                w.Refresh();
                if (i < presets.Count) w.Click += _ => SelectPosition(index, c, v);
                posButtons.Add((w, i));
            }
        }
        return height;
    }

    /// <summary>onChangePosition: the preset button is the selection, the custom button goes disabled; OnChangePosition(id).</summary>
    void SelectPosition(int index, ItemCustomize c, CustomValues v)
    {
        foreach (var (b, i) in posButtons)
        {
            b.SetSelected(i == index, false);
            if (i == posButtons.Count - 1) { b.Enabled = false; b.Refresh(); }
        }
        UiBridge.Raise(C.Movie, C.ToHost.OnChangePosition, index);
        v.SetPosition(c, index);
        Changed?.Invoke();
    }

    void SetValue(Slider s, float value, bool raise)
    {
        value = Math.Clamp(value, s.Min, s.Max);
        if (s.Whole) value = (float)Math.Round(value);
        bool changed = value != s.Value;
        s.Value = value;
        if (s.Gauge is { } g)
        {
            float frac = s.Max > s.Min ? (value - s.Min) / (s.Max - s.Min) : 0;
            float cx = TrackX + frac * TrackW;
            if (g.ChildNamed("thumb") is { } thumb) thumb.Override = new Transform2D(0, new Vector2(cx - ThumbW / 2, 0));
            if (g.ChildNamed("m_statusBar") is { } bar)
                bar.Override = new Transform2D(new Vector2(Math.Max(0.001f, (cx - BarX) / BarW), 0), new Vector2(0, 1), new Vector2(BarX, BarY));
            g.GotoFrame(g.Frame);
        }
        if (raise && changed) { s.Changed?.Invoke(value); Changed?.Invoke(); }
    }

    /// <summary>Slider drags (SliderEvent.VALUE_CHANGE while the thumb or the track is held).</summary>
    public bool OnMouse(InputEvent e)
    {
        if (!Visible) return false;
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            if (!mb.Pressed) { bool was = dragging != null; dragging = null; return was; }
            foreach (var s in sliders)
                if (s.Gauge != null && s.Gauge.IsVisibleInTree() && s.Gauge.GlobalBounds().HasPoint(mb.Position))
                {
                    dragging = s;
                    Drag(s, mb.Position);
                    return true;
                }
        }
        else if (e is InputEventMouseMotion mm && dragging != null)
        {
            Drag(dragging, mm.Position);
            return true;
        }
        return false;
    }

    void Drag(Slider s, Vector2 screen)
    {
        var local = s.Gauge.GetGlobalTransform().AffineInverse() * screen;
        float frac = Math.Clamp((local.X - TrackX) / TrackW, 0, 1);
        SetValue(s, s.Min + frac * (s.Max - s.Min), true);
    }

    /// <summary>Scripted runs: set slider <paramref name="row"/> (in panel order) to <paramref name="value"/>.</summary>
    public void SetSlider(int row, float value) { if (row >= 0 && row < sliders.Count) SetValue(sliders[row], value, true); }

    /// <summary>Scripted runs: pick position preset <paramref name="index"/>.</summary>
    public void ClickPosition(int index)
    {
        foreach (var (b, i) in posButtons) if (i == index) { b.MouseOver(true); b.MouseDown(true); b.MouseDown(false); b.MouseOver(false); }
    }
}
