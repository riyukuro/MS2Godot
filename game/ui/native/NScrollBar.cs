using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// CLIK ScrollBar (ScrollBarEx skins such as export_scroll): up and down arrow buttons, a track and a thumb taken from the
/// skin's named parts. Behaviour from scaleform.clik.controls.ScrollBar: thumb height = max(minThumbSize,
/// pageSize / (max - min + pageSize) * track), thumb y from the position, arrows and wheel step by pageScrollSize,
/// a track press jumps by trackScrollPageSize (trackMode scrollPage), the thumb drags.
/// </summary>
[GlobalClass]
public partial class NScrollBar : Control
{
    [Export] public string Movie = "component_v1";
    [Export] public string Skin = "export_scroll";
    [Export] public float MinPosition, MaxPosition = 10, PageSize = 1, PageScrollSize = 1, TrackScrollPageSize = 1, MinThumbSize = 10;

    [Signal] public delegate void ScrollEventHandler(float position);

    float position;
    NButton up, down, thumb;
    NSkin track;
    Rect2 trackRect;
    bool dragging; float dragOffset;

    public float Position_ { get => position; set => SetPosition(value); }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        if (NativeUi.Art == null) return;
        var parts = NativeUi.Art.Parts(Movie, Skin);
        var whole = NativeUi.Art.Bake(Movie, Skin, 0, skipComponents: true);
        if (Size == Vector2.Zero && whole != null) Size = whole.Size;
        foreach (var (name, kind, xf, id, cls) in parts)
        {
            string sym = Skin + "/" + name;
            switch (name)
            {
                case "upArrow": up = new NButton { Movie = Movie, Skin = sym, Position = xf.Origin }; AddChild(up); break;
                case "downArrow": down = new NButton { Movie = Movie, Skin = sym }; AddChild(down); break;
                case "thumb": thumb = new NButton { Movie = Movie, Skin = sym }; break;
                case "track":
                    var art = NativeUi.Art.Bake(Movie, sym);
                    track = new NSkin { Movie = Movie, Symbol = sym };
                    trackRect = new Rect2(xf.Origin, art == null ? Vector2.Zero : art.Size * xf.Scale);
                    AddChild(track);
                    break;
            }
        }
        if (thumb != null) AddChild(thumb);
        if (up != null) up.Click += () => SetPosition(position - PageScrollSize);
        if (down != null) down.Click += () => SetPosition(position + PageScrollSize);
        if (thumb != null) thumb.GuiInput += ThumbInput;
        Resized += Layout;
        Layout();
    }

    /// <summary>Constraints: arrows pinned top and bottom, the track between them.</summary>
    void Layout()
    {
        if (up == null || down == null) return;
        float top = up.Size.Y, bottom = Size.Y - down.Size.Y;
        down.Position = new Vector2(0, bottom);
        trackRect = new Rect2(trackRect.Position.X, top, Size.X, Math.Max(0, bottom - top));
        if (track != null) { track.Position = trackRect.Position; track.Size = trackRect.Size; }
        UpdateThumb();
    }

    public void SetScrollProperties(float pageSize, float min, float max, float pageScrollSize = 1)
    {
        PageSize = pageSize; MinPosition = min; MaxPosition = max; PageScrollSize = pageScrollSize;
        SetPosition(position);
    }

    public void SetPosition(float p, bool notify = true)
    {
        p = Math.Clamp(p, MinPosition, Math.Max(MinPosition, MaxPosition));
        bool changed = p != position;
        position = p;
        UpdateThumb();
        if (changed && notify) EmitSignal(SignalName.Scroll, position);
    }

    float AvailableHeight => trackRect.Size.Y - (thumb?.Size.Y ?? 0);

    void UpdateThumb()
    {
        if (thumb == null) return;
        float range = Math.Max(1, MaxPosition - MinPosition + PageSize);
        float h = Math.Max(MinThumbSize, Math.Min(trackRect.Size.Y, PageSize / range * trackRect.Size.Y));
        thumb.Size = new Vector2(thumb.SkinSize.X, h);
        thumb.Visible = MaxPosition > MinPosition;
        float t = MaxPosition > MinPosition ? (position - MinPosition) / (MaxPosition - MinPosition) : 0;
        float y = (float)Math.Round(t * AvailableHeight + trackRect.Position.Y);
        thumb.Position = new Vector2(0, Math.Max(trackRect.Position.Y, Math.Min(trackRect.End.Y - h, y)));
        thumb.QueueRedraw();
    }

    void ThumbInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
        {
            dragging = mb.Pressed;
            dragOffset = GetLocalMousePosition().Y - thumb.Position.Y;
        }
        else if (e is InputEventMouseMotion && dragging)
        {
            float t = (GetLocalMousePosition().Y - dragOffset - trackRect.Position.Y) / Math.Max(1, AvailableHeight);
            SetPosition(MinPosition + t * (MaxPosition - MinPosition));
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp) { SetPosition(position - PageScrollSize); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown) { SetPosition(position + PageScrollSize); AcceptEvent(); }
            else if (mb.ButtonIndex == MouseButton.Left && trackRect.HasPoint(mb.Position) && thumb != null)
            {
                // handleTrackPress, trackMode scrollPage
                SetPosition(position + (thumb.Position.Y < mb.Position.Y ? TrackScrollPageSize : -TrackScrollPageSize));
                AcceptEvent();
            }
        }
    }
}
