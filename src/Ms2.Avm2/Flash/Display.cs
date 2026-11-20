using System.Text.Json.Nodes;

namespace Ms2.Avm2.Flash;

[AsClass("flash.display.DisplayObject", "flash.events.EventDispatcher")]
public class AsDisplayObject : AsEventDispatcher
{
    public AsContainer Parent;
    public string InstanceName;
    Mat matrix = Mat.Identity;
    Cx color = Cx.Identity;
    bool isVisible = true;
    JsonObject symbol;
    SymbolScope scope;
    int depth = int.MinValue;
    int? clipDepth;
    AsDisplayObject maskObject;
    object scriptFilters;
    JsonArray placeFilters;
    string blend = "normal";

    // The state the renderer reads is set through these properties, which mark the object when the value changes
    // (see MarkChanged). A matrix, symbol or blend change also changes what the descendants are drawn through (their
    // screen scale, a scale-9 grid, the blend they inherit), so it marks the subtree as a whole.
    public Mat Matrix { get => matrix; set { if (!Same(matrix, value)) { matrix = value; MarkChanged(true); } } }
    public Cx Color { get => color; set { if (color != value) { color = value; MarkChanged(); } } }
    public bool IsVisible { get => isVisible; set { if (isVisible != value) { isVisible = value; MarkChanged(); } } }
    /// <summary>The symbol this object draws (shape, text, sprite timeline), with the scope its ids resolve in.</summary>
    public JsonObject Symbol { get => symbol; set { if (symbol != value) { symbol = value; MarkChanged(true); } } }
    public SymbolScope Scope { get => scope; set { if (scope != value) { scope = value; MarkChanged(); } } }
    /// <summary>Timeline placement that created it (null for objects made by code) and its depth.</summary>
    public JsonObject Placement;
    /// <summary>The depth; the parent's clip masks reach by depth, so the parent is marked too.</summary>
    public int Depth { get => depth; set { if (depth != value) { depth = value; MarkChanged(); Parent?.MarkChanged(); } } }
    public int? ClipDepth { get => clipDepth; set { if (clipDepth != value) { clipDepth = value; MarkChanged(); Parent?.MarkChanged(); } } }
    public bool MatrixLocked, ColorLocked;
    /// <summary>The script mask; the old and the new mask object are marked as well (a mask object is not drawn).</summary>
    public AsDisplayObject MaskObject
    {
        get => maskObject;
        set { if (maskObject == value) return; maskObject?.MarkChanged(); maskObject = value; value?.MarkChanged(); MarkChanged(true); }
    }
    /// <summary>The display object this one is the script mask of (DisplayObject::IsUsedAsMask).</summary>
    public AsDisplayObject MaskOwner;
    public RectD? ScrollRect;
    public object Filters { get => scriptFilters; set { if (scriptFilters != value) { scriptFilters = value; MarkChanged(); } } }
    /// <summary>The filters of the timeline placement that made the object (PlaceObject3's filter list), for the renderer.</summary>
    public JsonArray PlaceFilters { get => placeFilters; set { if (placeFilters != value) { placeFilters = value; MarkChanged(); } } }
    public string Blend { get => blend; set { if (blend != value) { blend = value; MarkChanged(true); } } }

    static bool Same(Mat a, Mat b) => a.A == b.A && a.B == b.B && a.C == b.C && a.D == b.D && a.Tx == b.Tx && a.Ty == b.Ty;

    // ---- change marks (the renderer's sync skips what is unmarked) ---------------------------------------------------
    // The port of the client's per-node change bits (Render::Change_*, render_constants.h:60): MovieImpl::Advance
    // (sfr_gfx_01i.cpp:3869) updates the render nodes every frame, and MovieImpl::Capture(onChangeOnly) (:4142) takes a
    // snapshot only when RenderContext.HasChanges(). Own stands for a node's own bits (Change_Matrix, Change_CxForm,
    // Change_Visible, Change_Shape, Change_IsMask; Change_ChildInsert / Change_ChildRemove on the parent). Deep stands for
    // the states a subtree inherits (Change_State_Scale9, Change_State_BlendMode, Change_State_MaskNode), and is also set
    // for a matrix, since text is laid out at the screen scale. Subtree marks the ancestors of a marked object, so the
    // sync reaches it from the stage. The sync redoes the whole of an Own object, so the bits are not split further.

    [Flags] enum Marks : byte { Own = 1, Subtree = 2, Deep = 4 }
    Marks marks = Marks.Own | Marks.Subtree | Marks.Deep;      // a new object is drawn in full the first time
    /// <summary>The object a mark goes up to when there is no parent: the button whose state this object is.</summary>
    internal AsDisplayObject MarkOwner;

    /// <summary>This object's own drawn state changed since the renderer last cleared the marks.</summary>
    public bool ChangedSelf => (marks & Marks.Own) != 0;
    /// <summary>This object or something under it changed (every ancestor of a marked object has this).</summary>
    public bool ChangedBelow => (marks & Marks.Subtree) != 0;
    /// <summary>Everything under this object is to be drawn again (its matrix, symbol or blend changed, or it was added).</summary>
    public bool ChangedDeep => (marks & Marks.Deep) != 0;

    /// <summary>
    /// Marks the object changed (and, with <paramref name="deep"/>, everything under it) and every ancestor as having a
    /// change below; the walk up stops at the first ancestor already marked, so a frame's marks cost O(depth) once.
    /// </summary>
    public void MarkChanged(bool deep = false)
    {
        Changes++;
        if (Class?.Runtime is { } rt) rt.DisplayMarks++;
        marks |= deep ? Marks.Own | Marks.Subtree | Marks.Deep : Marks.Own | Marks.Subtree;
        for (var p = Parent ?? MarkOwner; p != null && (p.marks & Marks.Subtree) == 0; p = p.Parent ?? p.MarkOwner) p.marks |= Marks.Subtree;
    }

    /// <summary>Counts the marks made on any display object (a renderer with nothing new since its last pass reads it unchanged).</summary>
    public static long Changes;

    /// <summary>The renderer has drawn this object's current state.</summary>
    public void ClearMarks() => marks = 0;
    /// <summary>Set to <see cref="Player"/>'s current advance epoch when this object or a descendant is a playing multi-frame clip.</summary>
    internal uint AdvanceEpoch;
    public bool CacheAsBitmapFlag;
    public bool HitTestDisabled;
    /// <summary>Free slot for the renderer (its node for this object).</summary>
    public object Tag;
    public int TopmostLevel;

    [AsCtor] void Ctor(object[] a) { }

    public void ScriptMoved() => MatrixLocked = true;
    public void ScriptColored() => ColorLocked = true;
    public Player P => RT.Player;

    [As] public string name { get => InstanceName ?? ""; set => InstanceName = value; }
    [As] public AsContainer parent => Parent;
    [As] public AsDisplayObject root
    {
        get
        {
            if (this is AsStage) return this;
            AsDisplayObject r = this;
            while (r.Parent != null && r.Parent is not AsStage && r.Parent is not AsLoader) r = r.Parent;
            return r.Parent != null || r == P?.Root ? r : null;
        }
    }
    [As] public AsStage stage { get { for (AsDisplayObject o = this; o != null; o = o.Parent) if (o is AsStage s) return s; return null; } }
    public bool OnStage => stage != null;

    [As] public double x { get => GetX(); set { if (double.IsNaN(value)) return; SetX(value); } }
    [As] public double y { get => GetY(); set { if (double.IsNaN(value)) return; SetY(value); } }
    // DisplayObject::xGet / xSet go through the virtual GetX / SetX (sfr_as3_07.cpp:963); a text field overrides them
    // (TextField::GetX, sfr_gfx_03e.cpp:760; TextField::SetX, sfr_gfx_03f.cpp:861).
    protected virtual double GetX() => Matrix.Tx;
    protected virtual double GetY() => Matrix.Ty;
    protected virtual void SetX(double v) { var m = Matrix; m.Tx = v; Matrix = m; ScriptMoved(); }
    protected virtual void SetY(double v) { var m = Matrix; m.Ty = v; Matrix = m; ScriptMoved(); }
    /// <summary>Runs after the scaleX, scaleY or rotation setter: a text field marks its position for recomputing
    /// (TextField::SetXScale / SetYScale / SetRotation set Flags_NeedUpdateGeomData, sfr_gfx_03f.cpp:882 / :911 / :422).</summary>
    protected virtual void ScaledOrRotated() { }
    // The 3x3 of the last matrix3D set, kept until a rotation, scale or z setter runs: those call UpdateTransform3D
    // (sfr_gfx_06.cpp:1903-1920), which rebuilds M34 from the properties and so replaces it. Is3D is the node flag
    // SetMatrix3D sets and Clear3D clears.
    internal double[,] Held3x3;
    internal bool Is3D;
    double zv, scaleZv = 1, rotXv, rotYv;
    // The four setters below make the node 3D even at the default value: UpdateTransform3D runs unconditionally (sfr_gfx_06.cpp:1220-1233).
    [As] public double z { get => zv; set { zv = value; Held3x3 = null; Is3D = true; } }
    [As] public double scaleX
    {
        get { var s = Math.Sqrt(Matrix.A * Matrix.A + Matrix.B * Matrix.B); return Matrix.A * Matrix.D - Matrix.B * Matrix.C < 0 && Matrix.A < 0 ? -s : s; }
        set { if (double.IsNaN(value)) return; double cur = Math.Sqrt(Matrix.A * Matrix.A + Matrix.B * Matrix.B); double rot = cur == 0 ? 0 : Math.Atan2(Matrix.B, Matrix.A); var m = Matrix; m.A = value * Math.Cos(rot); m.B = value * Math.Sin(rot); Matrix = m; Held3x3 = null; ScriptMoved(); ScaledOrRotated(); }
    }
    [As] public double scaleY
    {
        get => Math.Sqrt(Matrix.C * Matrix.C + Matrix.D * Matrix.D);
        set { if (double.IsNaN(value)) return; double cur = Math.Sqrt(Matrix.C * Matrix.C + Matrix.D * Matrix.D); double rot = cur == 0 ? 0 : Math.Atan2(-Matrix.C, Matrix.D); var m = Matrix; m.C = -value * Math.Sin(rot); m.D = value * Math.Cos(rot); Matrix = m; Held3x3 = null; ScriptMoved(); ScaledOrRotated(); }
    }
    [As] public double scaleZ { get => scaleZv; set { scaleZv = value; Held3x3 = null; Is3D = true; } }
    [As] public double rotation
    {
        get => Math.Atan2(Matrix.B, Matrix.A) * 180 / Math.PI;
        set
        {
            if (double.IsNaN(value)) return;
            double sx = scaleX, sy = scaleY, r = value * Math.PI / 180;
            var m = Matrix; m.A = sx * Math.Cos(r); m.B = sx * Math.Sin(r); m.C = -sy * Math.Sin(r); m.D = sy * Math.Cos(r); Matrix = m; Held3x3 = null; ScriptMoved(); ScaledOrRotated();
        }
    }
    [As] public double rotationX { get => rotXv; set { rotXv = value; Held3x3 = null; Is3D = true; } }
    [As] public double rotationY { get => rotYv; set { rotYv = value; Held3x3 = null; Is3D = true; } }
    [As] public double rotationZ { get; set; }
    [As] public double alpha { get => Color.Am; set { Color = Color with { Am = double.IsNaN(value) ? 0 : value }; ScriptColored(); } }
    /// <summary>
    /// DisplayObject::visibleSet -> Sprite::SetVisible -> SetVisibleFlag (sfr_as3_07.cpp:5333, sfr_gfx_05b.cpp:1065): unlike
    /// alpha, x, y, the scales and rotation (SetAcceptAnimMoves(false), sfr_gfx_06.cpp), it does not stop the timeline's moves.
    /// </summary>
    [As] public bool visible { get => IsVisible; set => IsVisible = value; }
    [As] public string blendMode { get => Blend; set => Blend = value ?? "normal"; }
    [As] public bool cacheAsBitmap { get => CacheAsBitmapFlag; set => CacheAsBitmapFlag = value; }
    [As] public object opaqueBackground { get; set; }
    [As] public object filters
    {
        get => Filters as AsArray ?? new AsArray(RT);
        set => Filters = value is AsArray a ? new AsArray(RT, a.Items) : null;
    }
    [As] public AsDisplayObject mask
    {
        get => MaskObject;
        set
        {
            if (MaskObject != null && MaskObject.MaskOwner == this) MaskObject.MaskOwner = null;
            MaskObject = value;
            if (value != null) value.MaskOwner = this;
        }
    }
    [As] public AsRectangle scrollRect
    {
        get => ScrollRect is { } r ? AsRectangle.New(RT, r) : null;
        set => ScrollRect = value == null ? null : value.R;
    }
    RectD scale9Own; bool scale9Set;
    /// <summary>DisplayObject::scale9GridGet (sfr_as3_07.cpp:1617): a new Rectangle of the node's grid, null without one. A
    /// symbol's DefineScale9Grid is that grid from the start, until a setter replaces it (scale9GridSet, sfr_as3_07.cpp:1833).</summary>
    [As] public AsRectangle scale9Grid
    {
        get
        {
            // a new Rectangle per get, null for an empty grid (sfr_as3_07.cpp:1617-1630)
            var g = scale9Set ? scale9Own : Symbol?["scale9"] is JsonObject o ? SymbolScope.Rect(o) : default;
            return g.W <= 0 || g.H <= 0 ? null : AsRectangle.New(RT, g);
        }
        // scale9GridSet copies the rectangle (sfr_as3_07.cpp:1833-1848); null clears
        set { scale9Own = value == null ? default : value.R; scale9Set = true; }
    }
    [As] public AsTransform transform
    {
        get { var t = (AsTransform)RT.GetClass("flash.geom.Transform").Construct(this); return t; }
        set { if (value?.Owner != null) { Matrix = value.Owner.Matrix; Color = value.Owner.Color; ScriptMoved(); ScriptColored(); } }
    }
    [As] public AsObject loaderInfo => P?.LoaderInfoFor(this);
    [As] public AsObject accessibilityProperties { get; set; }

    // ---- geometry -----------------------------------------------------------------------------------------

    /// <summary>Bounds of this object's own content and children, in its own coordinates.</summary>
    public virtual RectD LocalBounds()
    {
        var r = RectD.None;
        if (Symbol?["bounds"] is JsonObject b) r = SymbolScope.Rect(b);
        return r;
    }

    int boundsMemoAt;
    RectD boundsMemo;

    /// <summary>
    /// <see cref="LocalBounds"/>, kept per object while a <see cref="BoundsMemo"/> scope is open (a hit test, which changes
    /// nothing in the display list), so that a container's bounds, which hold all of its descendants', are each made once
    /// per test instead of once for every ancestor the test passes through. Outside a scope it is LocalBounds itself.
    /// </summary>
    internal RectD MemoBounds()
    {
        int at = BoundsMemo.Epoch;
        if (at == 0) return LocalBounds();
        if (boundsMemoAt != at) { boundsMemo = LocalBounds(); boundsMemoAt = at; }
        return boundsMemo;
    }

    public Mat ToGlobalMatrix()
    {
        var m = Matrix;
        for (var p = Parent; p != null; p = p.Parent) m = m.Then(p.Matrix);
        return m;
    }

    public (double X, double Y) LocalToGlobal(double x, double y) => ToGlobalMatrix().Apply(x, y);
    public (double X, double Y) GlobalToLocal(double x, double y) => ToGlobalMatrix().Inverse().Apply(x, y);

    public RectD GetBounds(AsDisplayObject space)
    {
        var lb = LocalBounds();
        if (lb.IsEmpty) return lb;
        var toGlobal = ToGlobalMatrix();
        var m = space == null ? toGlobal : toGlobal.Then(space.ToGlobalMatrix().Inverse());
        return m.Apply(lb);
    }

    [As] public AsRectangle getBounds(AsDisplayObject targetCoordinateSpace)
    {
        var r = GetBounds(targetCoordinateSpace ?? this);
        if (r.IsEmpty) { var (x0, y0) = (targetCoordinateSpace == null ? Mat.Identity : ToGlobalMatrix().Then(targetCoordinateSpace.ToGlobalMatrix().Inverse())).Apply(0, 0); return AsRectangle.New(RT, x0, y0, 0, 0); }
        return AsRectangle.New(RT, r);
    }
    [As] public AsRectangle getRect(AsDisplayObject targetCoordinateSpace) => getBounds(targetCoordinateSpace);

    RectD BoundsInParent() { var lb = LocalBounds(); return lb.IsEmpty ? lb : Matrix.Apply(lb); }

    [As] public double width
    {
        get { var r = BoundsInParent(); return r.IsEmpty ? 0 : r.W; }
        set
        {
            var lb = LocalBounds(); if (lb.IsEmpty || lb.W == 0 || double.IsNaN(value)) return;
            double rot = rotation; double sy = scaleY;
            scaleX = value / lb.W;
            if (rot != 0) { rotation = rot; }
            _ = sy;
        }
    }
    [As] public double height
    {
        get { var r = BoundsInParent(); return r.IsEmpty ? 0 : r.H; }
        set { var lb = LocalBounds(); if (lb.IsEmpty || lb.H == 0 || double.IsNaN(value)) return; scaleY = value / lb.H; }
    }

    [As] public AsPoint localToGlobal(AsPoint p) { var (x, y) = LocalToGlobal(p.x, p.y); return AsPoint.New(RT, x, y); }
    [As] public AsPoint globalToLocal(AsPoint p) { var (x, y) = GlobalToLocal(p.x, p.y); return AsPoint.New(RT, x, y); }
    [As] public double mouseX => GlobalToLocal(P?.MouseX ?? 0, P?.MouseY ?? 0).X;
    [As] public double mouseY => GlobalToLocal(P?.MouseX ?? 0, P?.MouseY ?? 0).Y;

    [As] public bool hitTestPoint(double x, double y, bool shapeFlag = false) => GetBounds(null).Contains(x, y);
    [As] public bool hitTestObject(AsDisplayObject o)
    {
        if (o == null) return false;
        var a = GetBounds(null); var b = o.GetBounds(null);
        return !a.IsEmpty && !b.IsEmpty && a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
    }

    /// <summary>Whether the point (stage coordinates) is on this object's own drawn content (not its children).</summary>
    public virtual bool HitsContent(double gx, double gy)
    {
        var lb = base_LocalContent();
        if (lb.IsEmpty) return false;
        var (lx, ly) = GlobalToLocal(gx, gy);
        return lb.Contains(lx, ly);
    }
    protected virtual RectD base_LocalContent() => Symbol?["bounds"] is JsonObject b ? SymbolScope.Rect(b) : RectD.None;

    public bool EffectivelyVisible { get { for (AsDisplayObject o = this; o != null; o = o.Parent) if (!o.IsVisible) return false; return true; } }

    public override string DefaultString() => "[object " + (Class?.LocalName ?? "DisplayObject") + "]";
}

[AsClass("flash.display.InteractiveObject", "flash.display.DisplayObject")]
public class AsInteractiveObject : AsDisplayObject
{
    [As] public bool mouseEnabled { get; set; } = true;
    [As] public bool doubleClickEnabled { get; set; }
    public bool? TabEnabledSet;
    [As] public bool tabEnabled { get => TabEnabledSet ?? DefaultTabEnabled; set => TabEnabledSet = value; }
    protected virtual bool DefaultTabEnabled => false;
    [As] public int tabIndex { get; set; } = -1;
    [As] public object focusRect { get; set; }
    [As] public AsObject contextMenu { get; set; }
    [As] public AsObject accessibilityImplementation { get; set; }
    [As] public bool needsSoftKeyboard { get; set; }
    [As] public AsObject softKeyboardInputAreaOfInterest { get; set; }
    [As] public bool requestSoftKeyboard() => false;
}

[AsClass("flash.display.DisplayObjectContainer", "flash.display.InteractiveObject")]
public class AsContainer : AsInteractiveObject
{
    /// <summary>The children in display order; every change to the list marks this container (and an added child in full).</summary>
    public readonly ChildList Children;
    public AsContainer() { Children = new ChildList(this); }
    [As] public bool mouseChildren { get; set; } = true;
    [As] public bool tabChildren { get; set; } = true;
    [As] public int numChildren => Children.Count;
    [As] public AsObject textSnapshot => null;

    public override RectD LocalBounds()
    {
        var r = base.LocalBounds();
        if (this is AsMovieClip || this is AsSprite) r = OwnGraphicsBounds();
        foreach (var c in Children)
        {
            var cb = c.MemoBounds();
            if (cb.IsEmpty) continue;
            r = r.Union(c.Matrix.Apply(cb));
        }
        return r;
    }

    protected virtual RectD OwnGraphicsBounds() => RectD.None;

    [As] public AsDisplayObject addChild(AsDisplayObject child) => addChildAt(child, child?.Parent == this ? Children.Count - 1 : Children.Count);

    [As] public AsDisplayObject addChildAt(AsDisplayObject child, int index)
    {
        if (child == null) throw RT.TypeError("Error #2007: Parameter child must be non-null.");
        if (child == this) throw RT.ArgumentError("Error #2024: An object cannot be added as a child of itself.");
        for (var p = Parent; p != null; p = p.Parent) if (p == child) throw RT.ArgumentError("Error #2150: An object cannot be added as a child to one of it's children.");
        if (index < 0 || index > Children.Count) throw RT.RangeError("Error #2006: The supplied index is out of bounds.");
        if (child.Parent == this)
        {
            Children.Remove(child);
            Children.Insert(Math.Min(index, Children.Count), child);
            return child;
        }
        child.Parent?.RemoveChildInternal(child, true);
        Children.Insert(index, child);
        child.Parent = this;
        child.Send("flash.events.Event", "added", true);
        if (OnStage) P?.AddedToStage(child);
        return child;
    }

    /// <summary>Puts a timeline child in depth order among the other timeline children.</summary>
    public void InsertByDepth(AsDisplayObject child)
    {
        int idx = Children.Count;
        for (int i = 0; i < Children.Count; i++)
            if (Children[i].Depth != int.MinValue && Children[i].Depth > child.Depth) { idx = i; break; }
        Children.Insert(idx, child);
        child.Parent = this;
    }

    internal void RemoveChildInternal(AsDisplayObject child, bool events)
    {
        if (events)
        {
            child.Send("flash.events.Event", "removed", true);
            if (OnStage) P?.RemovedFromStage(child);
        }
        Children.Remove(child);
        child.Parent = null;
        if (P?.Focus == child || (P?.Focus is AsDisplayObject f && IsAncestorOf(child, f))) P?.ClearFocusSilently();
    }

    static bool IsAncestorOf(AsDisplayObject a, AsDisplayObject d) { for (var p = d.Parent; p != null; p = p.Parent) if (p == a) return true; return false; }

    /// <summary>
    /// GFx DisplayObjectContainer::removeChild @0x142451dc0: a null child is TypeError 2007; a child of another container
    /// is returned unchanged with no error (AvmDisplayObjContainer::RemoveChild @0x142522570 finds no index and returns),
    /// where Flash Player throws ArgumentError 2025. Movies rely on it (MessageBox.popDown removes the box from both popup
    /// canvases).
    /// </summary>
    [As] public AsDisplayObject removeChild(AsDisplayObject child)
    {
        if (child == null) throw RT.TypeError("Error #2007: Parameter child must be non-null.");
        if (child.Parent == this) RemoveChildInternal(child, true);
        return child;
    }

    [As] public AsDisplayObject removeChildAt(int index)
    {
        if (index < 0 || index >= Children.Count) throw RT.RangeError("Error #2006: The supplied index is out of bounds.");
        var c = Children[index]; RemoveChildInternal(c, true); return c;
    }

    [As] public void removeChildren(int beginIndex = 0, int endIndex = int.MaxValue)
    {
        endIndex = Math.Min(endIndex, Children.Count - 1);
        for (int i = endIndex; i >= beginIndex; i--) RemoveChildInternal(Children[i], true);
    }

    [As] public AsDisplayObject getChildAt(int index)
    {
        if (index < 0 || index >= Children.Count) throw RT.RangeError("Error #2006: The supplied index is out of bounds.");
        return Children[index];
    }
    [As] public AsDisplayObject getChildByName(string name) => Children.FirstOrDefault(c => c.InstanceName == name);
    [As] public int getChildIndex(AsDisplayObject child)
    {
        int i = Children.IndexOf(child);
        if (i < 0) throw RT.ArgumentError("Error #2025: The supplied DisplayObject must be a child of the caller.");
        return i;
    }
    /// <summary>
    /// GFx DisplayObjectContainer::setChildIndex @0x142452d20 (sfr_as3_07.cpp:4408): a negative index is 0, then
    /// AvmDisplayObjContainer::SetChildIndex (sfr_as3_19.cpp:3017) moves the child only when the index is below the child
    /// count and the child is in this container's list; else nothing, where Flash Player throws ArgumentError 2025 or
    /// RangeError 2006. ShopPriceButton.updatePrice calls it on its strike line with the line itself as the child. A null
    /// child keeps the error it had (the thunk's null handling is not read).
    /// </summary>
    [As] public void setChildIndex(AsDisplayObject child, int index)
    {
        if (child == null) throw RT.ArgumentError("Error #2025: The supplied DisplayObject must be a child of the caller.");
        if (index < 0) index = 0;
        if (index >= Children.Count || child.Parent != this) return;
        Children.Remove(child); Children.Insert(index, child);
    }
    [As] public void swapChildren(AsDisplayObject a, AsDisplayObject b)
    {
        int i = getChildIndex(a), j = getChildIndex(b);
        Children[i] = b; Children[j] = a;
    }
    [As] public void swapChildrenAt(int i, int j) { var a = getChildAt(i); var b = getChildAt(j); Children[i] = b; Children[j] = a; }
    [As] public bool contains(AsDisplayObject child) { for (var o = child; o != null; o = o.Parent) if (o == this) return true; return false; }
    [As] public AsArray getObjectsUnderPoint(AsPoint p) => new(RT, P?.ObjectsUnder(this, p.x, p.y) ?? new List<object>());
    [As] public bool areInaccessibleObjectsUnderPoint(AsPoint p) => false;
    [As] public void stopAllMovieClips() { }
}

[AsClass("flash.display.Sprite", "flash.display.DisplayObjectContainer")]
public class AsSprite : AsContainer
{
    AsGraphics graphicsObj;
    [As] public AsGraphics graphics => graphicsObj ??= AsGraphics.For(RT, this);
    public AsGraphics GraphicsOrNull => graphicsObj;
    [As] public bool buttonMode { get; set; }
    [As] public bool useHandCursor { get; set; } = true;
    public AsSprite HitAreaObject;
    [As] public AsSprite hitArea { get => HitAreaObject; set { if (HitAreaObject != null) HitAreaObject.IsHitAreaOf = null; HitAreaObject = value; if (value != null) value.IsHitAreaOf = this; } }
    public AsSprite IsHitAreaOf;
    /// <summary>
    /// Sprite::dropTargetGet (sfr_as3_07.cpp:3619): the topmost entity under the last mouse position with every object
    /// tested and this sprite ignored (MovieImpl::GetTopMostEntity(mouse, 0, true, this)), or null. A movie on a shared
    /// stage (<see cref="Player.DropStage"/>) asks that stage, which searches every movie on it.
    /// </summary>
    [As] public AsDisplayObject dropTarget => P == null ? null
        : P.DropStage != null ? P.DropStage.DropTarget(P, this) : P.GetTopMostEntity(P.MouseX, P.MouseY, true, this);
    [As] public AsObject soundTransform { get; set; }
    [As] public void startDrag(bool lockCenter = false, AsRectangle bounds = null) => P?.StartDrag(this, lockCenter, bounds?.R);
    [As] public void stopDrag() => P?.StopDrag(this);
    [As] public void startTouchDrag(int id, bool lockCenter = false, AsRectangle bounds = null) { }
    [As] public void stopTouchDrag(int id) { }
    protected override bool DefaultTabEnabled => buttonMode;
    protected override RectD OwnGraphicsBounds() => graphicsObj?.Bounds ?? RectD.None;
    public override bool HitsContent(double gx, double gy)
    {
        if (graphicsObj != null && !graphicsObj.Bounds.IsEmpty)
        {
            var (lx, ly) = GlobalToLocal(gx, gy);
            if (graphicsObj.Bounds.Contains(lx, ly)) return true;
        }
        return false;
    }
}

/// <summary>A movie clip: a sprite with a timeline from the movie's layout (frames, labels, placements, frame scripts).</summary>
[AsClass("flash.display.MovieClip", "flash.display.Sprite", Dynamic = true)]
public class AsMovieClip : AsSprite
{
    Timeline tl;
    bool playing = true;
    internal bool TrackedPlaying;
    /// <summary>True when this clip's timeline advances each frame (MovieImpl::AdvanceFrame's optimized play list criterion).</summary>
    internal bool CanAdvance => playing && tl != null && tl.Frames > 1;
    void TrackPlaying() { if (CanAdvance) P?.RegisterPlaying(this); }

    public Timeline TL { get => tl; set { tl = value; TrackPlaying(); } }
    public int Frame;                       // 0-based
    public bool Playing { get => playing; set { playing = value; TrackPlaying(); } }
    public readonly Dictionary<int, AsFunction> FrameScripts = new();
    Dictionary<int, string> scriptLabels;
    /// <summary>The error label of frame <paramref name="frame"/>'s script ("Class@frame", 1-based), made once.</summary>
    internal string ScriptLabel(int frame)
    {
        scriptLabels ??= new();
        if (!scriptLabels.TryGetValue(frame, out var s)) scriptLabels[frame] = s = $"{Class.Name}@{frame + 1}";
        return s;
    }
    public readonly Dictionary<JsonObject, AsDisplayObject> Live = new(ReferenceEqualityComparer.Instance);
    public bool TimelineBuilt;
    public int LastScriptFrame = -1;
    internal bool ScriptQueued;             // in the player's scriptQueue (Flag_NeedExecuteFrame set)

    [As] public int currentFrame => Frame + 1;
    [As] public int totalFrames => Math.Max(1, TL?.Frames ?? 1);
    [As] public int framesLoaded => totalFrames;
    [As] public string currentLabel { get { string l = null; if (TL != null) foreach (var (f, n) in TL.Labels) if (f <= Frame) l = n; return l; } }
    [As] public string currentFrameLabel { get { if (TL != null) foreach (var (f, n) in TL.Labels) if (f == Frame) return n; return null; } }
    [As] public AsArray currentLabels => new(RT, (TL?.Labels ?? new()).Select(l => (object)AsFrameLabel.New(RT, l.Name, l.Frame + 1)));
    [As] public AsObject currentScene => AsScene.New(RT, "Scene 1", currentLabels, totalFrames);
    [As] public AsArray scenes => new(RT, new object[] { currentScene });
    [As] public bool enabled { get; set; } = true;
    [As] public bool trackAsMenu { get; set; }
    [As] public bool isPlaying => Playing;

    [As] public void play() => Playing = true;
    [As] public void stop() => Playing = false;
    // MovieClip::gotoAndStop/gotoAndPlay (@0x14244e1d0/@0x14244e0e0, GotoFrameAndRun sfr_as3_07.cpp:1789): when the frame
    // differs, the clip's pending AL_Frame entries are dropped (FUN_142522490) and the frame changes; then the frame
    // actions are queued (FUN_142520080, FrameCounter::QueueFrameActions) and run at once, even for the current frame,
    // so the target frame's script runs between frameConstructed and exitFrame. A label that is not found returns first.
    [As] public void gotoAndStop(object frame, string scene = null) { if (Goto(frame)) { Playing = false; P.RunFrameActions(); } }
    [As] public void gotoAndPlay(object frame, string scene = null) { if (Goto(frame)) { Playing = true; P.RunFrameActions(); } }
    // MovieClip::nextFrame/prevFrame (sfr_as3_07.cpp:3339/3225): Sprite::GotoFrame and the stopped state only; the new
    // frame's script runs with the next frame actions. STILL-OPEN: nextFrame tests frame < GetLoadingFrame(), not the count.
    [As] public void nextFrame() { Playing = false; if (Frame + 1 < totalFrames) P.GotoFrame(this, Frame + 1); }
    [As] public void prevFrame() { Playing = false; if (Frame > 0) P.GotoFrame(this, Frame - 1); }
    [As] public void nextScene() { }
    [As] public void prevScene() { }

    bool Goto(object frame)
    {
        int f;
        if (frame is string s && !double.TryParse(s, out _))
        {
            var hit = TL?.Labels.FirstOrDefault(l => l.Name == s);
            if (hit == null || hit.Value.Name == null)
            {
                // Flash throws #2109 here; Scaleform (what the game ran) logs it and leaves the clip where it is
                P?.Warn($"frame label {s} not found in {Class.Name}");
                return false;
            }
            f = hit.Value.Frame;
        }
        // STILL-OPEN: the client returns when the frame does not convert (Convert2UInt32); here it is clamped
        else f = Rt.ToInt32(frame) - 1;
        f = Math.Clamp(f, 0, totalFrames - 1);
        if (f != Frame) { P.DropFrameActions(this); P.GotoFrame(this, f); }
        return true;
    }

    /// <summary>addFrameScript(frame, fn, frame, fn, ...): frames are 0-based.</summary>
    [As] public void addFrameScript(params object[] a)
    {
        for (int i = 0; i + 1 < a.Length; i += 2)
        {
            int f = Rt.ToInt32(a[i]);
            if (a[i + 1] is AsFunction fn) FrameScripts[f] = fn; else FrameScripts.Remove(f);
        }
    }
}

/// <summary>A sprite symbol's timeline from the layout JSON.</summary>
public sealed class Timeline
{
    public int Frames;
    public List<(int Frame, string Name)> Labels = new();
    public List<JsonObject> Placements = new();
    public SymbolScope Scope;

    public static Timeline From(JsonObject tl, SymbolScope scope)
    {
        var t = new Timeline { Scope = scope, Frames = J.I(tl?["frames"]) ?? 1 };
        if (tl?["labels"] is JsonArray la) foreach (var l in la) t.Labels.Add((J.Iv(l["frame"]), (string)l["name"]));
        if (tl?["children"] is JsonArray ch) foreach (var c in ch) t.Placements.Add((JsonObject)c);
        t.Tracks = tl == null ? Array.Empty<Track>() : TrackCache.GetValue(tl, Compile);
        return t;
    }

    /// <summary>Per placement (in <see cref="Placements"/> order): its frame span and its moves, read once per layout timeline.</summary>
    public Track[] Tracks = Array.Empty<Track>();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<JsonObject, Track[]> TrackCache = new();

    static Track[] Compile(JsonObject tl)
    {
        if (tl["children"] is not JsonArray ch) return Array.Empty<Track>();
        var r = new Track[ch.Count];
        for (int i = 0; i < r.Length; i++) r[i] = new Track((JsonObject)ch[i]);
        return r;
    }

    /// <summary>
    /// A placement's frame span and its timeline moves (the PlaceObject moves of later frames), with the move frames as
    /// ints and each move's matrix and colour transform parsed on first use, so a playing clip's frame does not walk the
    /// layout JSON.
    /// </summary>
    public sealed class Track
    {
        public readonly int First, Last;            // Last = int.MaxValue: shown to the end
        public readonly JsonObject[] Moves;         // null: the placement has no moves list
        readonly int[] frames;
        readonly bool sorted;
        readonly Mat[] mats;
        readonly Cx[] colors;
        readonly byte[] state;                      // 1 parsed, 2 sets the matrix, 4 sets the colour

        public Track(JsonObject pl)
        {
            First = J.Iv(pl["first"]);
            Last = pl["last"] is JsonNode l ? J.Iv(l) : int.MaxValue;
            if (pl["moves"] is not JsonArray mv) return;
            Moves = new JsonObject[mv.Count]; frames = new int[mv.Count]; sorted = true;
            for (int i = 0; i < Moves.Length; i++)
            {
                Moves[i] = (JsonObject)mv[i]; frames[i] = J.Iv(Moves[i]["frame"]);
                if (i > 0 && frames[i] < frames[i - 1]) sorted = false;
            }
            mats = new Mat[Moves.Length]; colors = new Cx[Moves.Length]; state = new byte[Moves.Length];
        }

        public bool AliveAt(int f) => First <= f && f <= Last;

        /// <summary>The last move (in list order) whose frame is at or before <paramref name="f"/>; -1 when none.</summary>
        public int MoveAt(int f)
        {
            if (sorted)
            {
                int lo = 0, hi = frames.Length;             // first index with frame > f
                while (lo < hi) { int m = (lo + hi) >> 1; if (frames[m] <= f) lo = m + 1; else hi = m; }
                return lo - 1;
            }
            int k = -1;
            for (int i = 0; i < frames.Length; i++) if (frames[i] <= f) k = i;
            return k;
        }

        /// <summary>Move <paramref name="k"/>'s matrix (null when it keeps the matrix) and colour transform (null when it keeps it).</summary>
        public void Get(int k, out Mat? m, out Cx? c)
        {
            int s = System.Threading.Volatile.Read(ref state[k]);
            if (s == 0)
            {
                var mv = Moves[k];
                s = 1;
                // PlaceObject2 with PlaceFlagHasMatrix: the layout writes an identity matrix as null (LayoutJson Mx), so null = identity
                if ((bool?)mv["hasMatrix"] != false) { mats[k] = SymbolScope.Matrix(mv["matrix"]); s |= 2; }
                if (mv["color"] is JsonObject cj) { colors[k] = SymbolScope.Color(cj); s |= 4; }
                System.Threading.Volatile.Write(ref state[k], (byte)s);
            }
            m = (s & 2) != 0 ? mats[k] : null;
            c = (s & 4) != 0 ? colors[k] : null;
        }
    }
}

[AsClass("flash.display.Shape", "flash.display.DisplayObject")]
public class AsShape : AsDisplayObject
{
    AsGraphics graphicsObj;
    [As] public AsGraphics graphics => graphicsObj ??= AsGraphics.For(RT, this);
    public AsGraphics GraphicsOrNull => graphicsObj;
    public override RectD LocalBounds() => base.LocalBounds().Union(graphicsObj?.Bounds ?? RectD.None);
    public override bool HitsContent(double gx, double gy)
    {
        var (lx, ly) = GlobalToLocal(gx, gy);
        return LocalBounds().Contains(lx, ly);
    }
}

/// <summary>Static text and morph shapes: drawn from their symbol, no API of their own.</summary>
[AsClass("flash.text.StaticText", "flash.display.DisplayObject")]
public class AsStaticText : AsDisplayObject { [As] public string text => ""; }

[AsClass("flash.display.MorphShape", "flash.display.DisplayObject")]
public class AsMorphShape : AsDisplayObject { }

[AsClass("flash.display.SimpleButton", "flash.display.InteractiveObject")]
public class AsSimpleButton : AsInteractiveObject
{
    [As] public bool enabled { get; set; } = true;
    [As] public bool useHandCursor { get; set; } = true;
    [As] public bool trackAsMenu { get; set; }
    // the states are drawn under the button without being its children: their marks go up to it (MarkOwner)
    AsDisplayObject up, over, down, hit;
    AsDisplayObject SetState(AsDisplayObject old, AsDisplayObject value)
    {
        if (old == value) return old;
        if (old != null && old.MarkOwner == this) old.MarkOwner = null;
        if (value != null) { value.MarkOwner = this; value.MarkChanged(true); }
        MarkChanged();
        return value;
    }
    [As] public AsDisplayObject upState { get => up; set => up = SetState(up, value); }
    [As] public AsDisplayObject overState { get => over; set => over = SetState(over, value); }
    [As] public AsDisplayObject downState { get => down; set => down = SetState(down, value); }
    [As] public AsDisplayObject hitTestState { get => hit; set => hit = SetState(hit, value); }
    [As] public AsObject soundTransform { get; set; }
    string state = "up";
    public string State { get => state; set { if (state != value) { state = value; MarkChanged(); } } }
    protected override bool DefaultTabEnabled => true;
    AsDisplayObject Current => State == "down" ? downState ?? upState : State == "over" ? overState ?? upState : upState;
    public IEnumerable<AsDisplayObject> Drawn => Current == null ? Array.Empty<AsDisplayObject>() : new[] { Current };
    public override RectD LocalBounds() { var c = hitTestState ?? upState; return c == null ? RectD.None : c.Matrix.Apply(c.LocalBounds()); }
    public override bool HitsContent(double gx, double gy)
    {
        var (lx, ly) = GlobalToLocal(gx, gy);
        return LocalBounds().Contains(lx, ly);
    }
}

[AsClass("flash.display.Stage", "flash.display.DisplayObjectContainer")]
public class AsStage : AsContainer
{
    [As] public int stageWidth { get => (int)(P?.StageW ?? 0); set { } }
    [As] public int stageHeight { get => (int)(P?.StageH ?? 0); set { } }
    [As] public AsInteractiveObject focus { get => P?.Focus as AsInteractiveObject; set => P?.SetFocus(value, null); }
    [As] public string scaleMode { get; set; } = "noScale";
    [As] public string align { get; set; } = "";
    [As] public string quality { get; set; } = "HIGH";
    [As] public double frameRate { get => P?.FrameRate ?? 30; set { } }
    [As] public string displayState { get; set; } = "normal";
    [As] public bool stageFocusRect { get; set; }
    [As] public bool showDefaultContextMenu { get; set; }
    [As] public double fullScreenWidth => P?.StageW ?? 0;
    [As] public double fullScreenHeight => P?.StageH ?? 0;
    [As] public AsRectangle fullScreenSourceRect { get; set; }
    [As] public string colorCorrection { get; set; } = "default";
    [As] public uint color { get; set; }
    [As] public void invalidate() { if (P != null) P.RenderInvalidated = true; }
    [As] public bool isFocusInaccessible() => false;
}

[AsClass("flash.display.Graphics")]
public class AsGraphics : AsObject
{
    public AsDisplayObject Owner;
    /// <summary>Drawing commands kept for the renderer: ("rect", x, y, w, h, colour, alpha) and similar.</summary>
    public readonly List<(string Op, double[] V, uint Color, double Alpha)> Ops = new();
    public RectD Bounds = RectD.None;
    uint fill; double fillAlpha; bool filling;
    public static AsGraphics For(Runtime rt, AsDisplayObject owner) { var g = (AsGraphics)rt.GetClass("flash.display.Graphics").Construct(); g.Owner = owner; return g; }
    void Grow(double x, double y, double w, double h) => Bounds = Bounds.Union(new RectD(x, y, w, h));
    void Drawn(string op, double[] v) { Ops.Add((op, v, fill, filling ? fillAlpha : 0)); Grow(v[0], v[1], v[2], v[3]); Owner?.MarkChanged(); }
    [As] public void clear() { Ops.Clear(); Bounds = RectD.None; filling = false; Owner?.MarkChanged(); }
    [As] public void beginFill(uint color, double alpha = 1) { fill = color; fillAlpha = alpha; filling = true; }
    [As] public void beginGradientFill(params object[] a) { fill = 0x808080; fillAlpha = 1; filling = true; }
    [As] public void beginBitmapFill(params object[] a) { fill = 0xffffff; fillAlpha = 1; filling = true; }
    [As] public void endFill() => filling = false;
    [As] public void lineStyle(params object[] a) { }
    [As] public void lineGradientStyle(params object[] a) { }
    [As] public void moveTo(double x, double y) => Grow(x, y, 0, 0);
    [As] public void lineTo(double x, double y) => Grow(x, y, 0, 0);
    [As] public void curveTo(double cx, double cy, double x, double y) => Grow(x, y, 0, 0);
    [As] public void drawRect(double x, double y, double w, double h) => Drawn("rect", new[] { x, y, w, h });
    [As] public void drawRoundRect(double x, double y, double w, double h, double ew, double eh = double.NaN) => Drawn("rect", new[] { x, y, w, h });
    [As] public void drawCircle(double x, double y, double r) => Drawn("ellipse", new[] { x - r, y - r, 2 * r, 2 * r });
    [As] public void drawEllipse(double x, double y, double w, double h) => Drawn("ellipse", new[] { x, y, w, h });
    [As] public void drawPath(params object[] a) { }
    [As] public void drawTriangles(params object[] a) { }
    [As] public void drawGraphicsData(params object[] a) { }
    [As] public void copyFrom(AsGraphics g) { Ops.Clear(); Ops.AddRange(g.Ops); Bounds = g.Bounds; Owner?.MarkChanged(); }
}

[AsClass("flash.display.BitmapData")]
public class AsBitmapData : AsObject
{
    [As] public int width { get; set; }
    [As] public int height { get; set; }
    [As] public bool transparent { get; set; } = true;
    public string Source;    // image entry when known
    /// <summary>An image resource of a movie placed on a timeline (see Player.Instantiate): its scope and character id.</summary>
    public SymbolScope ImageScope;
    public int ImageId;
    [AsCtor] void Ctor(object[] a) { width = a.Length > 0 ? Rt.ToInt32(a[0]) : 0; height = a.Length > 1 ? Rt.ToInt32(a[1]) : 0; transparent = a.Length < 3 || Rt.ToBoolean(a[2]); }
    [As] public AsRectangle rect => AsRectangle.New(Class.Runtime, 0, 0, width, height);
    [As] public void dispose() { }
    [As] public void draw(params object[] a) { }
    [As] public void fillRect(params object[] a) { }
    [As] public void copyPixels(params object[] a) { }
    [As] public void applyFilter(params object[] a) { }
    [As] public void colorTransform(params object[] a) { }
    [As] public void @lock() { }
    [As] public void unlock(params object[] a) { }
    [As] public uint getPixel(int x, int y) => 0;
    [As] public uint getPixel32(int x, int y) => 0;
    [As] public void setPixel(int x, int y, uint c) { }
    [As] public void setPixel32(int x, int y, uint c) { }
    [As] public AsBitmapData clone() { var b = (AsBitmapData)Class.Construct(Rt.Box(width), Rt.Box(height)); b.Source = Source; b.ImageScope = ImageScope; b.ImageId = ImageId; return b; }
}

[AsClass("flash.display.Bitmap", "flash.display.DisplayObject")]
public class AsBitmap : AsDisplayObject
{
    AsBitmapData data;
    [As] public AsBitmapData bitmapData { get => data; set { if (data != value) { data = value; MarkChanged(); } } }
    [As] public bool smoothing { get; set; }
    [As] public string pixelSnapping { get; set; } = "auto";
    [AsCtor] void Ctor(object[] a) { bitmapData = a.Length > 0 ? a[0] as AsBitmapData : null; }
    public override RectD LocalBounds() => bitmapData == null ? RectD.None : new RectD(0, 0, bitmapData.width, bitmapData.height);
}

[AsClass("flash.display.FrameLabel", "flash.events.EventDispatcher")]
public class AsFrameLabel : AsEventDispatcher
{
    [As] public string name { get; set; }
    [As] public int frame { get; set; }
    [AsCtor] void Ctor(object[] a) { name = a.Length > 0 ? Rt.ToString(a[0]) : null; frame = a.Length > 1 ? Rt.ToInt32(a[1]) : 0; }
    public static AsFrameLabel New(Runtime rt, string n, int f) => (AsFrameLabel)rt.GetClass("flash.display.FrameLabel").Construct(n, Rt.Box(f));
}

[AsClass("flash.display.Scene")]
public class AsScene : AsObject
{
    [As] public string name { get; set; }
    [As] public AsArray labels { get; set; }
    [As] public int numFrames { get; set; }
    public static AsScene New(Runtime rt, string n, AsArray l, int f) { var s = (AsScene)rt.GetClass("flash.display.Scene").Construct(); s.name = n; s.labels = l; s.numFrames = f; return s; }
}

[AsClass("flash.display.LoaderInfo", "flash.events.EventDispatcher")]
public class AsLoaderInfo : AsEventDispatcher
{
    [As] public string url { get; set; } = "";
    [As] public string loaderURL => url;
    [As] public AsObject parameters { get; set; }
    [As] public double bytesLoaded { get; set; }
    [As] public double bytesTotal { get; set; }
    [As] public AsDisplayObject content { get; set; }
    [As] public AsObject loader { get; set; }
    [As] public double frameRate => P?.FrameRate ?? 30;
    [As] public double width => P?.StageW ?? 0;
    [As] public double height => P?.StageH ?? 0;
    [As] public int swfVersion => 10;
    [As] public int actionScriptVersion => 3;
    [As] public string contentType => "application/x-shockwave-flash";
    [As] public AsObject applicationDomain => (AsObject)Class.Runtime.GetClass("flash.system.ApplicationDomain")?.GetPublic("currentDomain");
    [As] public AsObject sharedEvents => Class.Runtime.GetClass("flash.events.EventDispatcher").Construct();
    [As] public AsObject uncaughtErrorEvents => Class.Runtime.GetClass("flash.events.EventDispatcher").Construct();
    Player P => Class.Runtime.Player;
}

/// <summary>Loader: the movies load images and other movies (icons, portraits) by URL; the player's resolver decides what that gives.</summary>
[AsClass("flash.display.Loader", "flash.display.DisplayObjectContainer")]
public class AsLoader : AsContainer
{
    AsLoaderInfo info;
    [As] public AsLoaderInfo contentLoaderInfo => info ??= NewInfo();
    AsLoaderInfo NewInfo() { var i = (AsLoaderInfo)RT.GetClass("flash.display.LoaderInfo").Construct(); i.loader = this; return i; }
    [As] public AsDisplayObject content => Children.FirstOrDefault();
    [As] public void load(AsObject request, AsObject context = null) => P?.LoadInto(this, request?.GetPublic("url") is string u ? u : null);
    [As] public void loadBytes(params object[] a) { }
    internal AsLoaderInfo InfoIfAny => info;
    /// <summary>Loader::unload: MovieRoot::UnloadMovie with dropAll false (see <see cref="Player.UnloadMovie"/>).</summary>
    [As] public void unload() { if (P is { } p) p.UnloadMovie(this, false); else foreach (var c in Children.ToList()) RemoveChildInternal(c, true); }
    /// <summary>
    /// Loader::unloadAndStop (sfr_as3_07.cpp:5160 -> MovieRoot::UnloadMovie(dropAll: true), sfr_as3_19.cpp:8453): the content
    /// hears "unload", leaves the stage and is stopped (its listeners and queued frame actions: ForceShutdown). Only when the
    /// content is a movie whose definition loses its last user do that definition's timers go (ShutdownTimersForMovieDef):
    /// an image loader's unload stops no timer. See <see cref="Player.UnloadMovie"/> for what stays STILL-OPEN.
    /// </summary>
    [As] public void unloadAndStop(bool gc = true) { if (P is { } p) p.UnloadMovie(this, true); else unload(); }
    [As] public void close() { }
}

[AsClass("flash.display.BlendMode")]
public class AsBlendMode : AsObject
{
    [As] public const string ADD = "add", ALPHA = "alpha", DARKEN = "darken", DIFFERENCE = "difference", ERASE = "erase", HARDLIGHT = "hardlight",
        INVERT = "invert", LAYER = "layer", LIGHTEN = "lighten", MULTIPLY = "multiply", NORMAL = "normal", OVERLAY = "overlay", SCREEN = "screen",
        SHADER = "shader", SUBTRACT = "subtract";
}

[AsClass("flash.display.StageAlign")]
public class AsStageAlign : AsObject { [As] public const string BOTTOM = "B", BOTTOM_LEFT = "BL", BOTTOM_RIGHT = "BR", LEFT = "L", RIGHT = "R", TOP = "T", TOP_LEFT = "TL", TOP_RIGHT = "TR"; }

[AsClass("flash.display.StageScaleMode")]
public class AsStageScaleMode : AsObject { [As] public const string EXACT_FIT = "exactFit", NO_BORDER = "noBorder", NO_SCALE = "noScale", SHOW_ALL = "showAll"; }

[AsClass("flash.display.StageQuality")]
public class AsStageQuality : AsObject { [As] public const string BEST = "best", HIGH = "high", LOW = "low", MEDIUM = "medium"; }

[AsClass("flash.display.StageDisplayState")]
public class AsStageDisplayState : AsObject { [As] public const string FULL_SCREEN = "fullScreen", FULL_SCREEN_INTERACTIVE = "fullScreenInteractive", NORMAL = "normal"; }

[AsClass("flash.display.PixelSnapping")]
public class AsPixelSnapping : AsObject { [As] public const string ALWAYS = "always", AUTO = "auto", NEVER = "never"; }

[AsClass("flash.display.GradientType")]
public class AsGradientType : AsObject { [As] public const string LINEAR = "linear", RADIAL = "radial"; }

[AsClass("flash.display.SpreadMethod")]
public class AsSpreadMethod : AsObject { [As] public const string PAD = "pad", REFLECT = "reflect", REPEAT = "repeat"; }

[AsClass("flash.display.LineScaleMode")]
public class AsLineScaleMode : AsObject { [As] public const string HORIZONTAL = "horizontal", NONE = "none", NORMAL = "normal", VERTICAL = "vertical"; }

[AsClass("flash.display.CapsStyle")]
public class AsCapsStyle : AsObject { [As] public const string NONE = "none", ROUND = "round", SQUARE = "square"; }

[AsClass("flash.display.JointStyle")]
public class AsJointStyle : AsObject { [As] public const string BEVEL = "bevel", MITER = "miter", ROUND = "round"; }

[AsClass("flash.display.InterpolationMethod")]
public class AsInterpolationMethod : AsObject { [As] public const string LINEAR_RGB = "linearRGB", RGB = "rgb"; }

/// <summary>
/// The scope of a <see cref="AsDisplayObject.MemoBounds"/> memo: each scope has its own epoch, so bounds kept in an earlier
/// scope are never reused. Scopes are per thread and do not nest (an inner scope keeps the outer one's epoch).
/// </summary>
internal static class BoundsMemo
{
    [ThreadStatic] internal static int Epoch;
    [ThreadStatic] static int last;
    /// <summary>Tests only: no memo, for the reference answers.</summary>
    internal static bool Off;

    internal readonly struct Scope : IDisposable
    {
        readonly bool opened;
        Scope(bool o) => opened = o;
        public static Scope Open()
        {
            if (Off || Epoch != 0) return default;
            if (++last == 0) last = 1;
            Epoch = last;
            return new Scope(true);
        }
        public void Dispose() { if (opened) Epoch = 0; }
    }
}

/// <summary>
/// A container's children: a list whose changes mark the container (its child order or membership changed) and a child
/// that comes in (everything under it is drawn again where it now is). Enumerates without allocating.
/// </summary>
public sealed class ChildList : IList<AsDisplayObject>, IReadOnlyList<AsDisplayObject>
{
    readonly List<AsDisplayObject> items = new();
    readonly AsContainer owner;
    internal ChildList(AsContainer owner) { this.owner = owner; }

    public int Count => items.Count;
    public bool IsReadOnly => false;
    public AsDisplayObject this[int index]
    {
        get => items[index];
        set { if (items[index] == value) return; items[index] = value; owner.MarkChanged(); value?.MarkChanged(true); }
    }
    public void Add(AsDisplayObject item) { items.Add(item); owner.MarkChanged(); item?.MarkChanged(true); }
    public void Insert(int index, AsDisplayObject item) { items.Insert(index, item); owner.MarkChanged(); item?.MarkChanged(true); }
    public bool Remove(AsDisplayObject item) { if (!items.Remove(item)) return false; owner.MarkChanged(); return true; }
    public void RemoveAt(int index) { items.RemoveAt(index); owner.MarkChanged(); }
    public void Clear() { if (items.Count == 0) return; items.Clear(); owner.MarkChanged(); }
    public int IndexOf(AsDisplayObject item) => items.IndexOf(item);
    public bool Contains(AsDisplayObject item) => items.Contains(item);
    public void CopyTo(AsDisplayObject[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);
    public List<AsDisplayObject>.Enumerator GetEnumerator() => items.GetEnumerator();
    IEnumerator<AsDisplayObject> IEnumerable<AsDisplayObject>.GetEnumerator() => items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => items.GetEnumerator();
}
