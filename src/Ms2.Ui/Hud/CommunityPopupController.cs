using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UICommunityPopup (ms2/client/ms2/uicommunitypopup.cpp, "CommunityPopup", UI id 0x4f5c, a UIHeadUpDisplay), the C++ side of
/// uicommunitypopup: a one-line text ticker. A line comes from GmCommand mode 1 (CSceneStateGame::FUN_14173c0f0, feature 0x88)
/// or the ShowPopupText user command (usercommandcli.cpp:2100), through <see cref="Show"/> (FUN_140bea390): the first line is
/// shown, the others queue. The text, padded with seven spaces and repeated to fill the 275 pixel lane, scrolls left at 50
/// pixels a second; after 120 server seconds the next queued line replaces it, or the movie unloads when none is left. A click
/// on the line opens its address in the browser popup (0x4f40) and ends the line at the next tick.
/// </summary>
public sealed class CommunityPopupController : UiController
{
    public override string Movie => "uicommunitypopup";     // GetSwfFileName: "UI" + GetTypeName
    /// <summary>Unused: the movie class (entity.CommunityPopup) adds no public method, see FindEntity.</summary>
    protected override string EntityMethod => "";
    const string EntityClass = "scaleform.ms2.entity.CommunityPopup";
    protected override bool IsDialog => false;              // a UIHeadUpDisplay
    public override string TypeName => "CommunityPopup";    // GetTypeName (@0x140be9b90)
    public const int UiId = 0x4f5c;

    /// <summary>The lane the text scrolls over and the gap the line starts past (FUN_140bea730's 275 and the 20 of width).</summary>
    public const int Lane = 275, WidthPad = 20;
    /// <summary>vfunc_15: a line is shown for this many server seconds.</summary>
    public const long LineSeconds = 120;
    /// <summary>FUN_140bea390: the padding after each copy of the text.</summary>
    public const string Padding = "       ";

    public override AsObject FindEntity()
    {
        static bool Is(AsClass k) { for (; k != null; k = k.Super) if (k.Name == EntityClass) return true; return false; }
        static Ms2.Avm2.Flash.AsDisplayObject Walk(Ms2.Avm2.Flash.AsDisplayObject o)
        {
            if (Is(o.Class)) return o;
            if (o is Ms2.Avm2.Flash.AsContainer c) foreach (var k in c.Children) if (Walk(k) is { } r) return r;
            return null;
        }
        return Host.Player.Root == null ? null : Walk(Host.Player.Root);
    }

    /// <summary>UIHeadUpDisplay::vfunc_2: the host loads the movie.</summary>
    public Action LoadMovie;
    /// <summary>UIHeadUpDisplay::vfunc_3: the host unloads the movie.</summary>
    public Action UnloadMovie;
    /// <summary>UIWebBrowserPopup (0x4f40) Navigate(url, 1); null when there is no browser popup.</summary>
    public Action<string> OpenUrl;
    /// <summary>GetServerTime, in seconds.</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    readonly Queue<(string Text, string Url)> queue = new();   // m_queue
    bool open;          // UIHeadUpDisplay m_68
    string text = "";   // m_text
    string url = "";    // m_url
    bool pending;       // m_pending
    const float Speed = 50f;    // m_speed
    float x;            // m_x
    int y;              // m_y, the text field's y at load
    int width;          // m_width, one copy's text width
    long time;          // m_time
    float lastDelta;    // GetLastGameTimeDelta, for the layout's own scroll step
    object movie, field;        // m_movie (m_popupText), m_field (its textField)
    object clickOn;
    Func<object[], object> click;

    public CommunityPopupController() => time = ServerTime();

    /// <summary>The shown line (m_text, m_url), the queued lines, the scroll x and one copy's width, for the tests.</summary>
    public (string Text, string Url) Current => (text, url);
    public int Queued => queue.Count;
    public float X => x;
    public int TextWidth => width;
    public bool IsOpen => open;

    bool Loaded => Host?.Entity != null;

    /// <summary>
    /// FUN_140bea390 (@0x140bea390): opens the popup if it is closed; with no line shown the text, padded, becomes the line
    /// and is laid out at once when nothing is scrolling; else the line queues.
    /// </summary>
    public void Show(string message, string address)
    {
        message ??= "";
        address ??= "";
        // the client opens first and its movie completes loading later; this host can load at once, so the line is
        // taken first and OnLoadComplete finds it (the order of the client's steps otherwise)
        bool load = !open;
        if (text.Length == 0)
        {
            text = message + Padding;
            url = address;
            pending = true;
            time = ServerTime();
            if (load) Open();
            if (width == 0) LayoutText();
        }
        else
        {
            if (load) Open();
            queue.Enqueue((message, address));
        }
    }

    /// <summary>UIHeadUpDisplay::vfunc_2: m_68 = 1 and the movie loads.</summary>
    void Open()
    {
        open = true;
        LoadMovie?.Invoke();
    }

    /// <summary>UIHeadUpDisplay::vfunc_3: m_68 = 0 and the movie unloads.</summary>
    public void Close()
    {
        open = false;
        UnloadMovie?.Invoke();
        field = movie = null;   // vfunc_26
    }

    /// <summary>
    /// OnLoadComplete (@0x140be9be0): m_popupText and its textField are found, the field's y kept, the clip's click opens the
    /// address (FUN_140bea960), the text is laid out, and an empty popup closes again.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        movie = Host.Child("m_popupText");
        field = movie == null ? null : Host.Child("textField", movie);
        if (field == null) return;      // the client throws GfxExceptionSwfLoad
        y = (int)Num(field, "y");
        if (!ReferenceEquals(clickOn, movie))
        {
            clickOn = movie;
            Host.Invoke(movie, "addEventListener", "click", click ??= _ => { OnClick(); return null; }, false, 0, false);
        }
        LayoutText();
        if (text.Length == 0) Close();
    }

    /// <summary>FUN_140bea960 (@0x140bea960): the browser popup opens the line's address and the line ends at the next tick.</summary>
    public void OnClick()
    {
        if (OpenUrl == null) return;
        OpenUrl(url);
        time = 0;
    }

    /// <summary>
    /// vfunc_15 (@0x140bea0e0), each frame: past 120 server seconds the line is cleared and the next queued one shown, or the
    /// popup closes when none is left; else the text scrolls by <paramref name="delta"/> seconds (GetLastGameTimeDelta).
    /// </summary>
    public void Tick(double delta)
    {
        lastDelta = (float)delta;
        long now = ServerTime();
        if (now - time > LineSeconds)
        {
            Reset();
            time = now;
            if (queue.Count == 0) Close();
            else
            {
                var (t, u) = queue.Dequeue();
                Show(t, u);
            }
        }
        else Scroll(lastDelta);
    }

    /// <summary>
    /// FUN_140bea540 (@0x140bea540): x moves left by speed times the frame time; a laid out line wraps back to 0 once one copy
    /// has passed, and a pending one is laid out once the old text has left the lane.
    /// </summary>
    void Scroll(float delta)
    {
        if (field == null || width == 0) return;
        x -= Speed * delta;
        int ix = (int)x;
        if (!pending)
        {
            if (ix < -width) { x = 0; ix = 0; }
            SetPos(ix);
        }
        else if (ix > -(int)Math.Ceiling(Num(field, "width"))) SetPos(ix);
        else LayoutText();
    }

    /// <summary>FUN_140bea6b0 (@0x140bea6b0): no line.</summary>
    void Reset()
    {
        text = "";
        url = "";
        x = 0;
        width = 0;
        pending = false;
    }

    /// <summary>
    /// FUN_140bea730 (@0x140bea730): with no text the clip hides; else it shows, one copy's width is measured, the text is
    /// repeated (width + 275) / width + 1 times, the field is made 20 wider than that, and the line starts at x 275.
    /// </summary>
    void LayoutText()
    {
        if (field == null) return;
        if (text.Length == 0)
        {
            width = 0;
            Host.Set(movie, "visible", false);   // m_movie->vfunc_3(0), SetVisible
            return;
        }
        Host.Set(movie, "visible", true);
        Host.Set(field, "htmlText", text);
        width = (int)Num(field, "textWidth");
        if (width <= 0) { width = 0; return; }      // the client divides by it; a line that measures nothing never scrolls
        int repeat = (width + Lane) / width + 1;
        var copies = new System.Text.StringBuilder(text.Length * repeat);   // once per shown line, not per frame
        for (int i = 0; i < repeat; i++) copies.Append(text);
        Host.Set(field, "htmlText", copies.ToString());
        Host.Set(field, "width", (double)(int)(Num(field, "textWidth") + WidthPad));
        x = Lane;
        pending = false;
        Scroll(lastDelta);
    }

    void SetPos(int px)
    {
        Host.Set(field, "x", (double)px);
        Host.Set(field, "y", (double)y);
    }

    double Num(object o, string member) => o == null ? 0 : Rt.ToNumber(Host.Get(o, member));

    /// <summary>vfunc_43 (@0x140bea2b0): ASGameEntity::vfunc_2(3, 0, -200).</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (!Loaded) return;
        Host.Align(Host.Entity, 3, 0, -200);
    }
}
