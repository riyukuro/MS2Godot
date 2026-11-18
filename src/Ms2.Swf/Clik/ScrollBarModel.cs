namespace Ms2.Swf.Clik;

/// <summary>
/// A vertical CLIK ScrollBar (scaleform.clik.controls.ScrollBar over ScrollIndicator and UIComponent) as its AS3 code lays
/// it out, without the drawing: the up arrow, the track, the down arrow and the thumb, in the bar's own units, and the scale
/// the bar is drawn at. The parts are given by the bar symbol's timeline (their y and height when the symbol is made).
/// <para>
/// Size: UIComponent.initialize keeps the placed size (_height = the placed height) and ScrollBar.initialize adds the
/// Constraints elements, each measured against the unscaled symbol (_originalHeight). The first validation runs configUI
/// (initSize: scale 1, setSize to the kept size) and then draw. ScrollIndicator.draw on a size change calls setActualSize,
/// which sets the display height when it differs: Scaleform's DisplayObjectBase::SetHeight scales the bar so that its
/// current content (the union of its parts, hidden ones too: DisplayList::GetBounds) gets that height, for the first draw
/// the 66 tall symbol. Then ScrollBar.drawLayout lays the parts out to the height: Constraints.update in REFLOW mode sets
/// the down arrow's y and the track's height and ends with scope.scaleX / scaleY = parentXAdjust / parentYAdjust (1, the
/// bar has no parent constraints), through UIComponent's scaleY setter (height = base.height: the laid out content, the
/// same height, so nothing is invalidated). So after each draw the bar is at scale 1, its parts laid out to the height;
/// the scale set by setActualSize lasts only inside the draw. updateThumb then sizes and places the thumb.
/// </para>
/// <para>Invalidations made between two validations are drawn by one draw (UIComponent.invalidate / validateNow).</para>
/// </summary>
public sealed class ScrollBarModel
{
    /// <summary>The bar's parts in the symbol: the up arrow at the top, the down arrow and the track (y, height).</summary>
    public readonly double OriginalHeight, UpHeight, DownHeight, TrackTop, TrackBottom, DownBottom;

    // ScrollIndicator's defaults
    public double OffsetTop = 0, OffsetBottom = 0, MinThumbSize = 10, PageScrollSize = 1;
    /// <summary>ScrollBar.trackScrollPageSize: a track press moves the position by this much.</summary>
    public double TrackScrollPageSize = 1;
    /// <summary>
    /// scaleform.ms2.controls.ScrollBarEx: drawLayout takes the size up to whole units (Math.ceil) and updateThumb caps the
    /// thumb at <see cref="MaxThumbSize"/> when that is above the minimum thumb size (0, the default, is no cap).
    /// </summary>
    public bool Ex;
    public double MaxThumbSize;
    double position = 5, minPosition = 0, maxPosition = 10, pageSize = double.NaN;

    /// <summary>UIComponent _width / _height: the size asked for.</summary>
    public double Width { get; private set; }
    public double Height { get; private set; }
    /// <summary>The display object's y scale (the drawn bar is <see cref="ContentHeight"/> times it): the placed scale until the first draw, then 1.</summary>
    public double ScaleY { get; private set; }
    public bool Enabled = true;

    // the parts' places in the bar's own units
    public double DownY { get; private set; }
    public double TrackY { get; private set; }
    public double TrackHeight { get; private set; }
    public double ThumbY { get; private set; }
    public double ThumbHeight { get; private set; }
    public bool ThumbVisible { get; private set; } = true;
    public bool UpEnabled { get; private set; } = true;
    public bool DownEnabled { get; private set; } = true;
    public bool TrackEnabled { get; private set; } = true;

    bool initialized, invalid, invalidSize, invalidData;
    /// <summary>The number of draws so far (a view redraws when it changes).</summary>
    public int Draws { get; private set; }

    /// <summary>Event.SCROLL: the position changed (ScrollIndicator.position).</summary>
    public event Action Scroll;

    /// <param name="symbolHeight">The bar symbol's height (_originalHeight: base.height / scaleY).</param>
    /// <param name="upHeight">The up arrow's height (it sits at y 0).</param>
    /// <param name="trackY">The track's y and height in the symbol.</param>
    /// <param name="downY">The down arrow's y and height in the symbol.</param>
    /// <param name="thumbY">The thumb's y and height in the symbol.</param>
    /// <param name="placedScaleY">The bar's placed y scale (its timeline matrix).</param>
    public ScrollBarModel(double symbolWidth, double symbolHeight, double upHeight, double trackY, double trackHeight,
        double downY, double downHeight, double thumbY, double thumbHeight, double placedScaleY)
    {
        OriginalHeight = symbolHeight;
        UpHeight = upHeight; DownHeight = downHeight;
        // Constraints.addElement: TOP keeps y, BOTTOM keeps _originalHeight - (y + height)
        DownBottom = symbolHeight - (downY + downHeight);
        TrackTop = trackY; TrackBottom = symbolHeight - (trackY + trackHeight);
        DownY = downY; TrackY = trackY; TrackHeight = trackHeight; ThumbY = thumbY; ThumbHeight = thumbHeight;
        ScaleY = placedScaleY;
        // UIComponent.initialize: _width / _height are the placed size; invalidate() marks everything
        Width = symbolWidth; Height = symbolHeight * placedScaleY;
        invalid = invalidSize = invalidData = true;
    }

    public double Position
    {
        get => position;
        // ScrollBar.position: rounded; ScrollIndicator.position: clamped, a change raises SCROLL and invalidates the data
        set
        {
            double v = AsRound(value);
            if (v == position) return;
            v = Math.Max(minPosition, Math.Min(maxPosition, v));
            if (v == position) return;
            position = v;
            Scroll?.Invoke();
            invalid = invalidData = true;
        }
    }

    public double MinPosition => minPosition;
    public double MaxPosition => maxPosition;
    public double PageSize => pageSize;

    /// <summary>The bar's content height in its own units: the union of its parts (hidden ones too).</summary>
    public double ContentHeight
    {
        get
        {
            double top = Math.Min(0, Math.Min(TrackY, Math.Min(DownY, ThumbY)));
            double bottom = Math.Max(UpHeight, Math.Max(TrackY + TrackHeight, Math.Max(DownY + DownHeight, ThumbY + ThumbHeight)));
            return bottom - top;
        }
    }

    /// <summary>The drawn height (AS3 base.height): the content times the scale.</summary>
    public double DisplayHeight => ContentHeight * ScaleY;

    /// <summary>height = h (UIComponent.height: setSize(_width, h)).</summary>
    public void SetHeight(double h) => SetSize(Width, h);

    /// <summary>UIComponent.setSize: a new size invalidates the size.</summary>
    public void SetSize(double w, double h)
    {
        if (Width == w && Height == h) return;
        Width = w; Height = h;
        invalid = invalidSize = true;
    }

    /// <summary>ScrollIndicator.setScrollProperties(pageSize, min, max): invalidates the size.</summary>
    public void SetScrollProperties(double page, double min, double max)
    {
        pageSize = page; minPosition = min; maxPosition = max;
        invalid = invalidSize = true;
    }

    /// <summary>ScrollBar.scrollUp / scrollDown: the position by the page scroll size (the arrows' press).</summary>
    public void ScrollUp() => Position = position - PageScrollSize;
    public void ScrollDown() => Position = position + PageScrollSize;

    /// <summary>ScrollBar.availableHeight: the track less the thumb, with the offsets.</summary>
    public double AvailableHeight => TrackHeight - ThumbHeight + OffsetBottom + OffsetTop;

    /// <summary>ScrollIndicator._isDragging: the thumb is held (handleThumbPress until endDrag).</summary>
    public bool Dragging { get; private set; }
    double dragOffset;

    /// <summary>
    /// ScrollBar.handleThumbPress (the thumb's MOUSE_DOWN): not while dragging; else the drag starts, the offset the mouse's
    /// y in the bar less the thumb's y. The stage's MOUSE_MOVE then goes to <see cref="Drag"/> and MOUSE_UP to <see cref="EndDrag"/>.
    /// </summary>
    public void ThumbPress(double mouseY)
    {
        if (Dragging) return;
        Dragging = true;
        dragOffset = mouseY - ThumbY;
    }

    /// <summary>ScrollBar.doDrag: the position at the thumb's top's fraction of the available height (the setter rounds and clamps).</summary>
    public void Drag(double mouseY)
    {
        if (!Dragging) return;
        double frac = (mouseY - dragOffset - TrackY) / AvailableHeight;
        Position = minPosition + frac * (maxPosition - minPosition);
    }

    /// <summary>ScrollBar.endDrag: the stage's listeners removed, the drag over.</summary>
    public void EndDrag() => Dragging = false;

    /// <summary>
    /// ScrollBar.handleTrackPress (the track's MOUSE_DOWN) in the "scrollPage" track mode: with shift held the position goes
    /// to the cursor (the thumb centred on it, rounded) and the thumb is pressed, dragged with its middle under the mouse;
    /// then, unless dragging or at _trackScrollPosition (-1, never set), a press beside the thumb moves the position a track
    /// page towards the mouse and a press on the thumb does nothing. The track is a plain movie clip, not a Button, so it has
    /// no ButtonEvent.CLICK and no auto repeat.
    /// </summary>
    public void TrackPress(double mouseY, bool shift)
    {
        if (shift)
        {
            double frac = (mouseY - ThumbHeight / 2 - TrackY) / AvailableHeight;
            Position = AsRound(frac * (maxPosition - minPosition) + minPosition);
            // thumb.dispatchEvent(MOUSE_DOWN) runs handleThumbPress with the thumb where it was drawn; the call after it
            // returns at once; then the offset is set to the thumb's middle
            ThumbPress(mouseY);
            dragOffset = ThumbHeight / 2;
        }
        if (Dragging || position == -1) return;
        if (mouseY > ThumbY && mouseY < ThumbY + ThumbHeight) return;
        Position = position + (ThumbY < mouseY ? TrackScrollPageSize : -TrackScrollPageSize);
    }

    /// <summary>ScrollBar.handleMouseWheel: up a page scroll per notch up, down per notch down.</summary>
    public void MouseWheel(int delta) => Position = position - (delta > 0 ? 1 : -1) * PageScrollSize;

    /// <summary>
    /// UIComponent.validateNow: the first one runs configUI (ScrollIndicator.configUI -> initSize: scale 1, setSize to the
    /// kept size), then a pending invalidation is drawn.
    /// </summary>
    public void Validate()
    {
        if (!initialized)
        {
            initialized = true;
            ScaleY = 1;
            SetSize(Width, Height == 0 ? DisplayHeight : Height);
        }
        if (!invalid) return;
        Draw();
        invalid = invalidSize = invalidData = false;
    }

    /// <summary>ScrollIndicator.draw: a size change sets the actual size, lays out and sizes the thumb; else a data change places the thumb.</summary>
    void Draw()
    {
        Draws++;
        if (invalidSize)
        {
            // setActualSize(_width, _height): the display height is set when it or _height differs
            // (SetHeight: a NaN or -Infinity height is not set, +Infinity is 0, a content without height scales to 0)
            if (DisplayHeight != Height && !double.IsNaN(Height) && !double.IsNegativeInfinity(Height))
            {
                double content = ContentHeight;
                ScaleY = Math.Abs(content) > 1e-6 ? (double.IsPositiveInfinity(Height) ? 0 : Height) / content : 0;
            }
            DrawLayout();
            UpdateThumb();
        }
        else if (invalidData) UpdateThumbPosition();
    }

    /// <summary>ScrollBar.drawLayout: the thumb to the track's top, then Constraints.update(_width, _height) in REFLOW mode.</summary>
    void DrawLayout()
    {
        if (Ex) { Width = Math.Ceiling(Width); Height = Math.Ceiling(Height); }
        ThumbY = TrackY - OffsetTop;
        DownY = Height - DownHeight - DownBottom;                 // BOTTOM: y = height - element height - bottom
        TrackHeight = Height - TrackTop - TrackBottom;            // TOP | BOTTOM: height = height - top - bottom
        // REFLOW: scope.scaleX = scope.scaleY = 1; UIComponent.scaleY: base.scaleY, then height = base.height (setSize; an
        // invalidation made inside the draw is cleared when validateNow ends)
        ScaleY = 1;
        Height = ContentHeight;
    }

    /// <summary>
    /// ScrollBar.updateThumb: the thumb's share of the track (the page over the range plus the page), at least the minimum
    /// thumb size; the thumb, a Button, takes the height when it validates (a NaN height is not set).
    /// </summary>
    void UpdateThumb()
    {
        double range = Math.Max(1, maxPosition - minPosition + pageSize);
        double track = TrackHeight + OffsetTop + OffsetBottom;
        double h = AsMax(MinThumbSize, AsMin(track, pageSize / range * track));
        if (Ex && MaxThumbSize > MinThumbSize) h = AsMin(h, MaxThumbSize);
        if (!double.IsNaN(h)) ThumbHeight = h;
        UpdateThumbPosition();
    }

    /// <summary>
    /// ScrollBar.updateThumbPosition: the thumb along the track by the position's fraction of the range, rounded, kept inside
    /// the track (a NaN place is not set); it shows while the fraction and the page size are numbers and the maximum is
    /// above 0 and finite. The arrows and the track are enabled while the thumb shows and the bar is enabled, the up arrow
    /// above the minimum and the down arrow below the maximum.
    /// </summary>
    void UpdateThumbPosition()
    {
        double frac = (position - minPosition) / (maxPosition - minPosition);
        double top = TrackY - OffsetTop;
        double available = TrackHeight - ThumbHeight + OffsetBottom + OffsetTop;
        double at = AsRound(frac * available + top);
        double y = AsMax(top, AsMin(TrackY + TrackHeight - ThumbHeight + OffsetBottom, at));
        if (!double.IsNaN(y)) ThumbY = y;
        ThumbVisible = !(double.IsNaN(frac) || double.IsNaN(pageSize) || maxPosition <= 0 || double.IsPositiveInfinity(maxPosition));
        bool on = ThumbVisible && Enabled;
        UpEnabled = on && position > minPosition;
        DownEnabled = on && position < maxPosition;
        TrackEnabled = on;
    }

    // AS3 Math.round: halves up
    static double AsRound(double v) => Math.Floor(v + 0.5);

    // AS3 Math.max / Math.min: NaN when either is NaN
    static double AsMax(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : Math.Max(a, b);
    static double AsMin(double a, double b) => double.IsNaN(a) || double.IsNaN(b) ? double.NaN : Math.Min(a, b);
}
