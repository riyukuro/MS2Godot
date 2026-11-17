using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// scaleform.ms2.controls.dialog Window / Dialog with OpenCloseTween. Open: starts startOffsetY (25) lower at
/// startAlpha (0), tweens 160 ms to alpha 1 and topOffsetY (7) above its place, then 80 ms down to its place (both
/// Regular.easeOut). Close: 100 ms to alpha 0 and startOffsetY lower, then <see cref="EndCloseEventHandler"/> (OnEndClose).
/// A drag bar moves the window inside the stage (Window.onStartDrag's rectangle); a close button raises onCloseDialog.
/// <see cref="AlignTo"/> places the window on the stage as Align.alignment does (offsets in stage units).
/// </summary>
[GlobalClass]
public partial class NWindow : Control
{
    public enum Align { None, Center, BottomCenter, TopCenter, BottomRight, BottomLeft, TopRight, TopLeft }

    [Export] public Align AlignTo = Align.None;
    [Export] public Vector2 AlignOffset;
    [Export] public float StartAlpha = 0, StartOffsetY = 25, TopOffsetY = 7;
    [Export] public NodePath DragBar = "";
    [Export] public NodePath CloseButton = "";
    [Export] public bool OpenOnReady = true;

    [Signal] public delegate void EndCloseEventHandler();
    [Signal] public delegate void CloseDialogEventHandler();

    Tween tween;
    float endY;
    bool closing, dragging;
    Vector2 dragFrom;

    Viewport sizeSource;            // the viewport whose size_changed aligns the window, while in the tree

    public override void _EnterTree()
    {
        sizeSource = GetViewport();
        sizeSource.SizeChanged += Alignment;
    }

    public override void _ExitTree()
    {
        if (sizeSource != null) { sizeSource.SizeChanged -= Alignment; sizeSource = null; }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Pass;
        Alignment();
        if (!DragBar.IsEmpty && GetNodeOrNull<Control>(DragBar) is { } bar) bar.GuiInput += DragInput;
        if (!CloseButton.IsEmpty && GetNodeOrNull<NButton>(CloseButton) is { } close) close.Click += () => EmitSignal(SignalName.CloseDialog);
        if (OpenOnReady) StartOpen();
    }

    /// <summary>The window's bounds: its size when set, else the union of its children's rects (the movie's getBounds).</summary>
    public Rect2 Bounds()
    {
        if (Size != Vector2.Zero) return new Rect2(Vector2.Zero, Size);
        Rect2? r = null;
        foreach (var c in GetChildren())
            if (c is Control k && k.Visible)
            {
                var kr = k is NButton b ? new Rect2(k.Position + b.HitRect.Position, b.HitRect.Size) : new Rect2(k.Position, k.Size);
                if (k is NSkin s && s.Skin != null) kr = new Rect2(k.Position + s.Skin.Origin, k.Size);
                r = r == null ? kr : r.Value.Merge(kr);
            }
        return r ?? new Rect2(Vector2.Zero, Size);
    }

    /// <summary>Align.alignment on the stage (the visible canvas in stage units).</summary>
    public void Alignment()
    {
        if (AlignTo == Align.None) return;
        var stage = GetViewportRect().Size;
        var b = Bounds();
        b = new Rect2(b.Position * Scale, b.Size * Scale);
        float x = AlignTo switch
        {
            Align.Center or Align.BottomCenter or Align.TopCenter => (stage.X - b.Size.X) / 2,
            Align.BottomRight or Align.TopRight => stage.X - b.Size.X,
            _ => 0,
        };
        float y = AlignTo switch
        {
            Align.Center => (stage.Y - b.Size.Y) / 2,
            Align.BottomCenter or Align.BottomRight or Align.BottomLeft => stage.Y - b.Size.Y,
            _ => 0,
        };
        Position = new Vector2(x, y) - b.Position + AlignOffset;
        endY = Position.Y;
    }

    /// <summary>startOpenAni.</summary>
    public void StartOpen()
    {
        if (!Visible || tween?.IsRunning() == true && !closing) return;
        tween?.Kill(); closing = false;
        endY = Position.Y;
        Position = new Vector2(Position.X, endY + StartOffsetY);
        Modulate = new Color(1, 1, 1, StartAlpha);
        tween = NativeUi.EaseOut(this);
        tween.TweenProperty(this, "modulate:a", 1f, 0.160);
        tween.Parallel().TweenProperty(this, "position:y", endY - TopOffsetY, 0.160);
        tween.TweenProperty(this, "position:y", endY, 0.080);
    }

    /// <summary>startCloseAni: an invisible window reports OnEndClose at once.</summary>
    public void StartClose()
    {
        if (!Visible) { EmitSignal(SignalName.EndClose); return; }
        if (closing) return;
        if (tween?.IsRunning() == true) { tween.Kill(); Position = new Vector2(Position.X, endY); Modulate = Colors.White; }
        closing = true;
        endY = Position.Y;
        tween = NativeUi.EaseOut(this);
        tween.TweenProperty(this, "modulate:a", 0f, 0.100);
        tween.Parallel().TweenProperty(this, "position:y", endY + StartOffsetY, 0.100);
        tween.TweenCallback(Callable.From(() => { closing = false; Position = new Vector2(Position.X, endY); EmitSignal(SignalName.EndClose); }));
    }

    void DragInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) { dragging = mb.Pressed; dragFrom = GetGlobalMousePosition() - GlobalPosition; }
        else if (e is InputEventMouseMotion && dragging)
        {
            // onStartDrag: x within [-w/2, stage - w/2], y within [0, stage - h/2]
            var stage = GetViewportRect().Size; var b = Bounds();
            var p = GetGlobalMousePosition() - dragFrom;
            Position = new Vector2(Math.Clamp(p.X, -b.Size.X * 0.5f, stage.X - b.Size.X * 0.5f), Math.Clamp(p.Y, 0, stage.Y - b.Size.Y * 0.5f));
            endY = Position.Y;
        }
    }
}
