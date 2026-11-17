using Godot;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// The loading screen (uiloadingprogress, UILoadingProgress in the client), ported by hand from the movie's AS3 (LoadingProgress)
/// for its frame mode: a clear screen (loadingprogress_whitekbg), the loading picture fitted to the stage, the top and bottom
/// bars stretched to the stage width (the bottom one holds the tip and the percent text) and, for low levels, the keyboard guide
/// centred above the bottom bar. With no picture the frame shows a screenshot of the game, stretched to the stage.
/// Without the frame (startProgress showFrame false: UILoadingProgress::vfunc_52 is false on the login and character select
/// scenes, and UILoadingProgressLobby, the lobby's loading screen, always passes false) the picture or the screenshot is
/// stretched to the stage (loadCompleteScreenShot / setScreenShotContent), with no clear screen, bars or guide, and the small
/// loading badge (loadingprogress_loading, a Progressbar) sits in the stage's bottom right corner with the percent.
/// <see cref="StopProgress"/> fades it out over 500 ms, then raises OnFadeInComplete.
/// The movie is drawn at the UI scale like every other movie (<see cref="UiScale"/>: CUIService::OnDeviceReset sets it for all
/// movies with GFxManager::SetMoviesScale), so its stage is <see cref="UiScale.MovieStage"/> and the guide (1208 wide in the
/// movie) takes about 843 pixels of a 1280 window, as the client's does. That scale and the stage come from the window's own
/// pixel size, with whatever content scale the root window applies at the time taken back out (<see cref="Fit"/>): the screen
/// lives at the tree root across a field change, while the HUD scene turns the root's canvas_items stretch off and on as it
/// enters and leaves the tree, so a size read from the visible rect is right on some frames and 1.46 times too large on
/// others (a 1920 x 1050 window over the 1280 x 720 design size).
/// </summary>
public sealed partial class LoadingProgressLogic
{
    public sealed class Start
    {
        public Texture2D Picture;          // filePath; null: the screenshot
        public Texture2D ScreenShot;
        public string Tip = "";
        public bool ShowGuide;             // showGuide with showGuideType "keyboard"
        public bool ShowMove;              // the move-channel message instead of the tip
        public bool ShowFrame = true;      // showFrame (UILoadingProgress::vfunc_52); false: the picture stretched and the badge
    }

    /// <summary>Everything the movie draws; the host adds it to a CanvasLayer.</summary>
    public readonly Node2D Layer;
    readonly MovieClipNode clear, top, bottom, guide, badge;
    readonly Vector2 topSize, bottomSize;
    readonly Sprite2D picture = new() { Centered = false };
    bool screenShot, showFrame = true;
    double fadeT = -1;
    Vector2 stage;
    public bool Active { get; private set; }

    public LoadingProgressLogic(UiAssets assets)
    {
        Layer = new StageLayer(Fit) { Name = "LoadingProgress" };
        var movie = new UiMovie(assets, UILoadingProgressContract.Movie);
        clear = movie.BuildClass("loadingprogress_whitekbg");
        top = movie.BuildClass("loadingprogress_bg_top");
        bottom = movie.BuildClass("loadingprogress_bg_bottom");
        guide = movie.BuildClass("export_keyboard_guide");
        badge = movie.BuildClass("loadingprogress_loading");       // the constructor's m_progressBar
        // the guide's key labels (export_keyboard_guide_label, a CLIK Label) get their text from the symbol's inspector values
        // (__setProp___idN__export_keyboard_guide_Layer1_0 in component_v1): $s_keyboard_guide_z ... _period
        if (guide != null)
        {
            UiWidgets.ApplyInspector(guide, UILoadingProgressContract.Library.Export_keyboard_guide);
            // export_keyboard_guide_label.frame1: TextFieldEx.setVerticalAlign(textField, VALIGN_CENTER)
            foreach (var clip in guide.AllClips())
                if (clip.Widget is LabelWidget)
                    foreach (var c in clip.Children)
                        if (c.Name == "textField" && c.Node is TextNode tf) tf.AlignCenter();
        }
        topSize = top?.Bounds.Size ?? Vector2.One;
        bottomSize = bottom?.Bounds.Size ?? Vector2.One;
        foreach (var n in new Node2D[] { clear, picture, top, bottom, guide, badge }) if (n != null) { Layer.AddChild(n); n.Visible = false; }
    }

    /// <summary>startProgress(info) (useFadeOut true).</summary>
    public void StartProgress(Start s)
    {
        Fit();
        fadeT = -1;
        Layer.Modulate = Colors.White;
        showFrame = s.ShowFrame;
        screenShot = s.Picture == null;
        picture.Texture = s.Picture ?? s.ScreenShot;
        if (!showFrame)
        {
            // the else branch: the picture goes through loadCompleteScreenShot either way; m_progressBar.textField.text = "0%",
            // addChild(m_progressBar), m_progressBar.alignment(). (showMove's centre message, loadingprogress_bg_center, is
            // not shown: no caller here passes showMove.)
            foreach (var n in new Node2D[] { clear, top, bottom, guide }) if (n != null) n.Visible = false;
            picture.Visible = true;
            if (badge != null)
            {
                badge.Visible = true;
                badge.SetNamedText("textField", "0%");
                LoopTimelines(badge);
            }
            Layout();
            Active = true;
            return;
        }
        if (badge != null) badge.Visible = false;
        if (bottom != null) LoopTimelines(bottom);
        foreach (var n in new Node2D[] { clear, picture, top, bottom }) if (n != null) n.Visible = true;
        if (guide != null) guide.Visible = s.ShowGuide;
        // startProgress: m_progressInfoText.htmlText = the move-channel message or the tip
        bottom?.SetNamedHtml("infoText", s.ShowMove ? "$s_loading_movechannel_message" : s.Tip ?? "");
        bottom?.SetNamedText("percentText", "0%");
        Layout();
        Active = true;
    }

    /// <summary>
    /// The movie's place on the window, every frame and whenever the root viewport changes size: the UI scale and the stage
    /// (UiScale) for the window's pixel size, drawn through the inverse of the root's final transform (its canvas_items
    /// stretch, Viewport.get_final_transform) so the screen keeps that size whether the stretch is on or off. Only a new stage
    /// size lays the screen out again (onStageResize; the badge's Progressbar.alignment runs on Event.RESIZE, so it keeps its
    /// place while its fill animates).
    /// </summary>
    void Fit()
    {
        var root = (Engine.GetMainLoop() as SceneTree)?.Root;
        if (root == null) return;
        Vector2 window = root.Size;
        float k = UiScale.For(window);
        Layer.Transform = root.GetFinalTransform().AffineInverse() * new Transform2D(0, new Vector2(k, k), 0, UiScale.MovieOrigin);
        var before = stage;
        stage = UiScale.MovieStage(window);
        if (Active && stage != before) Layout();
    }

    /// <summary>The movie's root node: it fits the movie to the window each frame and on the root viewport's size_changed.</summary>
    sealed partial class StageLayer : Node2D
    {
        readonly Action fit;
        public StageLayer(Action fit) { this.fit = fit; }
        public StageLayer() { }
        public override void _EnterTree() { GetTree().Root.SizeChanged += OnSize; fit?.Invoke(); }
        public override void _ExitTree() => GetTree().Root.SizeChanged -= OnSize;
        void OnSize() => fit?.Invoke();
        public override void _Process(double delta) => fit?.Invoke();
    }

    /// <summary>
    /// The hexagon's running shine: the badge (loadingprogress_loading, sprite 11) and the bottom bar (loadingprogress_bg_bottom,
    /// sprite 30 at depth 4) each hold one nested sprite with a 120-frame timeline, the gold glint (sprite 8) turning once
    /// around under a mask of the hexagon's rim (shape 5 / 27, clip depth 4 / 5). The sprite has no stop() and no frame
    /// labels, so Flash plays it from its creation and wraps from its last frame to its first for as long as it is on the
    /// stage. Nothing drives it from the load: progress() only writes the percent text (Progressbar.progress,
    /// LoadingProgress.progress), so it runs the same at 0% and at 100%.
    /// </summary>
    static void LoopTimelines(MovieClipNode clip)
    {
        foreach (var c in clip.Children)
            if (c.Node is MovieClipNode mc && mc.FrameCount > 1 && !mc.FrameScripts) mc.Loop();
    }

    void Layout()
    {
        if (!showFrame)
        {
            if (picture.Texture != null) { picture.Scale = stage / picture.Texture.GetSize(); picture.Position = ScreenShotPosition(picture.Texture.GetSize(), stage); }
            if (badge != null)
            {
                // Progressbar.alignment: x = stageWidth - width - m_marginX, y = stageHeight - height - m_marginY (10 each);
                // (the badge's origin is placed; width / height are its bounds on the current frame)
                var size = (badge.ContentRect() ?? badge.Bounds).Size;
                badge.Position = new Vector2(stage.X - size.X - 10, stage.Y - size.Y - 10);
            }
            return;
        }
        // setClearScreen: the clear screen fills the stage, the bars take the stage width and keep their aspect
        if (clear != null) Fit(clear, clear.Bounds, new Rect2(Vector2.Zero, stage));
        if (top != null)
        {
            float h = topSize.Y * (stage.X / topSize.X);
            Fit(top, top.Bounds, new Rect2(0, 0, stage.X, h));
        }
        float bottomY = stage.Y;
        if (bottom != null)
        {
            float h = bottomSize.Y * (stage.X / bottomSize.X);
            bottomY = stage.Y - h;                                           // Align.alignment(ALIGN_BOTTOMCENTER): the y setter
            Fit(bottom, bottom.Bounds, new Rect2(0, bottomY, stage.X, h));
        }
        if (picture.Texture != null)
        {
            var sz = picture.Texture.GetSize();
            if (screenShot) picture.Scale = stage / sz;                        // setScreenShotContent
            else
            {
                var r = LocalImageRect(sz, stage);
                picture.Scale = r.Size / sz;
                picture.Position = r.Position;
            }
            if (screenShot) picture.Position = ScreenShotPosition(sz, stage);
        }
        if (guide != null && guide.Visible)
        {
            // setInputGuide: Align.alignment(ALIGN_CENTER) puts the guide's origin at ((stage - size) / 2); then
            // l3 = int(m_progressBgBottom.y - int(y + height)) and, if l3 < 0, y += l3 + 20: the guide is raised by its overlap
            // with the bottom bar less 20, so it still covers the bar's top 20 (as on the client: the panel's bottom edge
            // passes the bar's dark band)
            var size = guide.Bounds.Size;
            var pos = (stage - size) * 0.5f;
            int l3 = (int)(bottomY - (int)(pos.Y + size.Y));
            if (l3 < 0) pos.Y += l3 + 20;
            guide.Position = pos;
        }
    }

    /// <summary>
    /// setScreenShotContent: the picture is stretched to the stage (stageWidth x stageHeight), then placed at
    /// (getContentsPosition(width, stageWidth), getContentsPosition(height, stageHeight)) with its size before the stretch.
    /// getContentsPosition(a, b) returns b / a, or 1 when they are equal, so the picture sits at the scale ratio (about
    /// (1.4, 1.4) for a 1280 x 720 shot on a 1837 x 1034 stage), not at the stage origin.
    /// </summary>
    public static Vector2 ScreenShotPosition(Vector2 picture, Vector2 stage) =>
        new(picture.X != stage.X ? stage.X / picture.X : 1, picture.Y != stage.Y ? stage.Y / picture.Y : 1);

    /// <summary>
    /// setLocalImageContent: where the loading picture goes on the stage. A narrow stage (width / height under 1.3) fits 90% of
    /// its height, a wider one its width (so the picture is cropped, never letterboxed); centred across, 30% of the spare
    /// height above it.
    /// </summary>
    public static Rect2 LocalImageRect(Vector2 picture, Vector2 stage)
    {
        float w, h;
        if (stage.X / stage.Y < 1.3f) { h = stage.Y * 0.9f; w = picture.X * (h / picture.Y); }
        else { w = stage.X; h = picture.Y * (w / picture.X); }
        return new Rect2((stage.X - w) * 0.5f, (stage.Y - h) * 0.3f, w, h);
    }

    /// <summary>Places a clip so its bounds fill <paramref name="to"/> (the AS3 width / height setters).</summary>
    static void Fit(Node2D n, Rect2 bounds, Rect2 to)
    {
        var s = new Vector2(bounds.Size.X > 0 ? to.Size.X / bounds.Size.X : 1, bounds.Size.Y > 0 ? to.Size.Y / bounds.Size.Y : 1);
        n.Scale = s;
        n.Position = to.Position - bounds.Position * s;
    }

    /// <summary>progress(message, count, total): the percent text.</summary>
    public void Progress(double count, double total)
    {
        if (!Active || total <= 0) return;
        string text = $"{(uint)(count / total * 100)}%";
        if (showFrame) bottom?.SetNamedText("percentText", text);
        else badge?.SetNamedText("textField", text);
    }

    /// <summary>stopProgress(fade): a 500 ms fade (alphaTween(500, 0)) or at once.</summary>
    public void StopProgress(bool fade)
    {
        if (!Active) return;
        if (fade) fadeT = 0;
        else Done();
    }

    public void Process(double delta)
    {
        if (fadeT < 0) return;
        fadeT += delta;
        float a = 1 - (float)Math.Min(1, fadeT / 0.5);
        Layer.Modulate = new Color(1, 1, 1, a);
        if (a <= 0) Done();
    }

    /// <summary>onCompleteFadeOut / processFadeInComplete: everything off, OnFadeInComplete to the host.</summary>
    void Done()
    {
        fadeT = -1;
        Active = false;
        foreach (var n in new Node2D[] { clear, picture, top, bottom, guide, badge }) if (n != null) n.Visible = false;
        badge?.SetNamedText("textField", "0%");                           // onCompleteFadeOut: m_progressBar.release()
        picture.Texture = null;
        Layer.Modulate = Colors.White;
        UiBridge.Raise(UILoadingProgressContract.Movie, UILoadingProgressContract.ToHost.OnFadeInComplete);
    }
}
