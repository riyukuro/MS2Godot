using Ms2.Formats.Pack;

namespace Ms2.Ui.Hud;

/// <summary>
/// ItemPreviewCommon::CLimitCharRotate (uiugcitemeditdialog.cpp:2551): the range a preview avatar's y angle may turn in.
/// The range [start, end] is normalized; a span over 359.9 degrees (end before start counts around 360) sets no limit.
/// </summary>
public sealed class AvatarRotateLimit
{
    readonly float first, second;          // m_range
    readonly float limitFirst, limitSecond; // m_limit: (range.second, range.first)

    public bool Limited { get; }

    /// <summary>The constructor (@0x141b49c00).</summary>
    public AvatarRotateLimit(float start, float end)
    {
        first = AvatarControl.NormalizeAngle(start);
        second = AvatarControl.NormalizeAngle(end);
        float span = start <= end ? end - start : end + 360f - start;
        if (span > 359.9f) return;
        Limited = true;
        limitFirst = second;
        limitSecond = first;
    }

    /// <summary>
    /// UgcshopCharRotateStartDegreeY and UgcshopCharRotateEndDegreeY (UIAvatarPC::vfunc_1 @0x1418ba9f0), the &lt;v&gt; rows
    /// of Xml table/constants.xml FUN_14028faa0 keeps (one per key, by locale and feature). STILL-OPEN: the defaults
    /// compiled into the client for a missing row are not read; a missing row here gives 0 and 360 (no limit).
    /// </summary>
    public static AvatarRotateLimit FromConstants(PackSet packs)
    {
        float start = 0, end = 360;
        if (packs != null && packs.Exists("Xml", "table/constants.xml")
            && Formats.World.GameStrings.Parse(packs.Read("Xml", "table/constants.xml")).Root is { } root)
        {
            foreach (var v in FeatureElements.Collect(packs, root, "v", "key"))
            {
                var key = (string)v.Attribute("key");
                if (!float.TryParse((string)v.Attribute("value"), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float f)) continue;
                if (string.Equals(key, "UgcshopCharRotateStartDegreeY", StringComparison.OrdinalIgnoreCase)) start = f;
                else if (string.Equals(key, "UgcshopCharRotateEndDegreeY", StringComparison.OrdinalIgnoreCase)) end = (int)f;
            }
        }
        return new AvatarRotateLimit(start, end);
    }

    /// <summary>
    /// FUN_141b49d40: <paramref name="angle"/> (the turned angle) kept in the range. Out of the range: <paramref name="fallback"/>.
    /// A <paramref name="delta"/> larger than the room left from <paramref name="current"/> to the range's edge in its
    /// direction gives that edge.
    /// </summary>
    public float Apply(float angle, float delta, float current, float fallback)
    {
        if (!Limited) return angle;
        float a = AvatarControl.NormalizeAngle(angle);
        if (!InRange(a)) return fallback;
        float abs = Math.Abs(delta);
        bool over = abs >= 360f;
        if (!over)
        {
            float room = delta > 0
                ? (current <= limitFirst ? limitFirst - current : 360f - current + limitFirst)
                : (current >= limitSecond ? current - limitSecond : 360f - limitSecond + current);
            over = room < abs;
        }
        if (over) a = delta > 0 ? second : first;
        return a;
    }

    /// <summary>FUN_141b49f30: inside the range, both edges excluded (a range that wraps past 360 too).</summary>
    bool InRange(float a) => first <= second ? a > first && a < second : a < second || a > first;
}

/// <summary>
/// UIAvatarBase's mouse handling (uiavatarbase.cpp): the OnAvatarMouse* calls the window movie's AvatarController makes,
/// as UIAvatarBase::vfunc_27 (@0x1418b9390) takes them, and the avatar angles they turn (vfunc_14 @0x1418b8560,
/// vfunc_13 @0x1418b8750, vfunc_12 @0x1418b84c0). Angles are degrees about the client's x, y, z axes, in
/// FromEulerAnglesXYZ order. STILL-OPEN: the locked (m_nLocked) and wheel camera (m_bWheelCamera) modes the character
/// window never sets are not ported, so the left button (mode 2) does nothing and only the right button turns the avatar.
/// </summary>
public sealed class AvatarControl
{
    /// <summary>m_fMoveStep: degrees per pixel dragged.</summary>
    public const float MoveStep = 0.35f;

    readonly AvatarRotateLimit limit;
    float ax, ay, az;           // m_kAngle: the angle when the drag started
    float bx, by, bz;           // m_kAngleBase: the avatar's angle

    /// <summary>The avatar's angles to start from (the facing the copy of the player is created with).</summary>
    public AvatarControl(AvatarRotateLimit limit, float x, float y, float z)
    {
        this.limit = limit;
        bx = x; by = y; bz = z;
        ax = x; ay = y; az = z;
    }

    /// <summary>m_nMode: 0 none, 1 turning (the right button), 2 the left button's (moving, locked views only).</summary>
    public int Mode { get; private set; }

    /// <summary>m_kAngleBase, the avatar's current angles.</summary>
    public (float X, float Y, float Z) Angle => (bx, by, bz);

    /// <summary>Raised with true for vfunc_5 (zoom in: a wheel turned forward), false for vfunc_6.</summary>
    public event Action<bool> Zoom;

    /// <summary>Raised when the angle changes (vfunc_13).</summary>
    public event Action Turned;

    /// <summary>GetModeForButton (@0x1418ba1a0): left 2, right 1, others 0.</summary>
    static int ModeForButton(int button) => button == 0 ? 2 : button == 1 ? 1 : 0;

    /// <summary>NormalizeAngle (@0x1418bf270): one turn added below 0 or taken above 360.</summary>
    public static float NormalizeAngle(float a) => a < 0f ? a + 360f : 360f < a ? a - 360f : a;

    /// <summary>
    /// vfunc_27 (@0x1418b9390) for one call of the window movie; false for a name it does not take ("OnAvatarMouseUp"
    /// among them).
    /// </summary>
    public bool OnCallback(string name, IReadOnlyList<object> args)
    {
        switch (name.ToLowerInvariant())
        {
            case "onavatarmousedown":
                if (args.Count > 0) MouseDown(Int(args[0]));
                return true;
            case "onavatarmousewheel":
                if (args.Count > 0) Zoom?.Invoke(Int(args[0]) > 0);
                return true;
            case "onavatarmousemove":
                if (args.Count > 0) MouseMove(Int(args[0]), args.Count > 1 ? Int(args[1]) : 0, args.Count > 2 ? Int(args[2]) : 0);
                return true;
        }
        return false;
    }

    static int Int(object o) => o switch
    {
        int i => i, uint u => (int)u, double d => (int)d, float f => (int)f, long l => (int)l, bool b => b ? 1 : 0,
        string s when int.TryParse(s, out int n) => n, _ => 0,
    };

    /// <summary>OnAvatarMouseDown: SetModeFromButton; a left press (mode 2) on an unlocked view clears the mode, any
    /// other mode starts a drag at the current angle (FUN_1418b9c10).</summary>
    public void MouseDown(int button)
    {
        Mode = ModeForButton(button);
        if (Mode == 0) return;
        if (Mode == 2) { Mode = 0; return; }
        ax = bx; ay = by; az = bz;
    }

    /// <summary>OnAvatarMouseMove: the drag since the press, in pixels; turns the avatar in mode 1 once it is more than 2
    /// pixels on either axis (vfunc_14(0, dy, dx)).</summary>
    public void MouseMove(int dx, int dy, int button)
    {
        if (ModeForButton(button) != Mode) return;
        if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2) return;
        if (Mode == 1) Turn(0, dy, dx);
    }

    /// <summary>vfunc_14: the drag times -<see cref="MoveStep"/> added to the start angle (the z turn reversed while the
    /// start angle's y is between 90 and 270), y limited (vfunc_12), then set (vfunc_13).</summary>
    void Turn(int dx, int dy, int dz)
    {
        float fx = -(dx * MoveStep), fy = -(dy * MoveStep), fz = -(dz * MoveStep);
        if (90f < ay && ay < 270f) fz = -fz;
        float rx = fx + ax, ry = fy + ay, rz = fz + az;
        if (ry != 0f) ry = limit.Apply(ry, fy, ay, by);
        bx = NormalizeAngle(rx); by = NormalizeAngle(ry); bz = NormalizeAngle(rz);
        Turned?.Invoke();
    }
}

/// <summary>
/// UIModelView's zoom (uimodelview.cpp): the camera moves along its line to the target between the camera's far
/// distance (the closest) and its min distance (the farthest). vfunc_13 (@0x140e67710) sets the range from the camera
/// numbers and the step (the range over the steps); vfunc_5 / vfunc_6 start a zoom in / out when none runs, and
/// UpdateZoomAnimation (uimodelview.cpp:331, run each frame from vfunc_2 @0x140e66a30) moves the camera until a range end or one step.
/// </summary>
public sealed class AvatarZoom
{
    readonly float near, far, speed, step;  // m_fMinDistance (farthest), m_fMaxDistance (closest)
    int state;                              // m_nZoomState: 0 none, 1 in, 2 out
    float start;                            // m_kZoomStart, as a distance

    /// <summary>The camera's distance from the target.</summary>
    public float Distance { get; private set; }

    /// <summary>vfunc_13 with the camera at <paramref name="distance"/>: the farthest the greater of
    /// <paramref name="minDistance"/> and it, the closest the smaller of <paramref name="maxDistance"/> and it.</summary>
    public AvatarZoom(float distance, float minDistance, float maxDistance, float speed, int steps)
    {
        Distance = distance;
        near = Math.Max(minDistance, distance);
        far = Math.Min(maxDistance, distance);
        this.speed = speed;
        step = (near - far) / Math.Max(steps, 1);
    }

    /// <summary>vfunc_5 (true) / vfunc_6 (false).</summary>
    public void Start(bool zoomIn)
    {
        if (state != 0) return;
        state = zoomIn ? 1 : 2;
        start = Distance;
    }

    /// <summary>UpdateZoomAnimation for a frame of <paramref name="dt"/> seconds; true when the distance changed.</summary>
    public bool Update(float dt)
    {
        if (state == 0) return false;
        float d = Distance;
        if (state == 1)
        {
            if (d > far)
            {
                d -= MathF.Ceiling(Math.Min(d, speed) * dt);
                if (d <= far) { d = far; state = 0; }
            }
            else { d = far; state = 0; }
        }
        else
        {
            if (d < near)
            {
                d += MathF.Ceiling(Math.Min(d, speed) * dt);
                if (d >= near) { d = near; state = 0; }
            }
            else { d = near; state = 0; }
        }
        if (state != 0 && Math.Abs(start - d) >= step) state = 0;
        bool moved = d != Distance;
        Distance = d;
        return moved;
    }
}
