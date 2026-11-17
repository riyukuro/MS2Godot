using Godot;
using Ms2Godot.Ui.Contracts;

namespace Ms2Godot.Ui;

/// <summary>
/// The loading screen (uiloadingprogress, UILoadingProgress in the client), ported by hand from the movie's AS3 (LoadingProgress)
/// for its frame mode: a clear screen (loadingprogress_whitekbg), the loading picture fitted to the stage, the top and bottom
/// bars stretched to the stage width (the bottom one holds the tip and the percent text) and, for low levels, the keyboard guide
/// centred above the bottom bar. With no picture the frame shows a screenshot of the game, stretched to the stage.
/// <see cref="StopProgress"/> fades it out over 500 ms, then raises OnFadeInComplete.
/// The movie is drawn at the UI scale like every other movie (<see cref="UiScale"/>: CUIService::OnDeviceReset sets it for all
/// movies with GFxManager::SetMoviesScale), so its stage is <see cref="UiScale.MovieStage"/> and the guide (1208 wide in the
/// movie) takes about 843 pixels of a 1280 window, as the client's does.
/// </summary>
public sealed class LoadingProgressLogic
{
    public sealed class Start
    {
        public Texture2D Picture;          // filePath; null: the screenshot
        public Texture2D ScreenShot;
        public string Tip = "";
        public bool ShowGuide;             // showGuide with showGuideType "keyboard"
        public bool ShowMove;              // the move-channel message instead of the tip
    }

    /// <summary>Everything the movie draws; the host adds it to a CanvasLayer.</summary>
    public readonly Node2D Layer = new() { Name = "LoadingProgress" };
    readonly MovieClipNode clear, top, bottom, guide;
    readonly Vector2 topSize, bottomSize;
    readonly Sprite2D picture = new() { Centered = false };
    bool screenShot;
    double fadeT = -1;
    Vector2 stage;
    public bool Active { get; private set; }

    public LoadingProgressLogic(UiAssets assets)
    {
        var movie = new UiMovie(assets, UILoadingProgressContract.Movie);
        clear = movie.BuildClass("loadingprogress_whitekbg");
        top = movie.BuildClass("loadingprogress_bg_top");
        bottom = movie.BuildClass("loadingprogress_bg_bottom");
        guide = movie.BuildClass("export_keyboard_guide");
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
        foreach (var n in new Node2D[] { clear, picture, top, bottom, guide }) if (n != null) { Layer.AddChild(n); n.Visible = false; }
    }

    /// <summary>startProgress(info) in frame mode (showFrame true, useFadeOut true), for a window of <paramref name="window"/> pixels.</summary>
    public void StartProgress(Start s, Vector2 window)
    {
        SetWindow(window);
        fadeT = -1;
        Layer.Modulate = Colors.White;
        screenShot = s.Picture == null;
        picture.Texture = s.Picture ?? s.ScreenShot;
        foreach (var n in new Node2D[] { clear, picture, top, bottom }) if (n != null) n.Visible = true;
        if (guide != null) guide.Visible = s.ShowGuide;
        bottom?.SetNamedText("infoText", s.ShowMove ? "$s_loading_movechannel_message" : s.Tip ?? "");
        bottom?.SetNamedText("percentText", "0%");
        Layout();
        Active = true;
    }

    /// <summary>onStageResize, for a window of <paramref name="window"/> pixels.</summary>
    public void SetStageSize(Vector2 window) { SetWindow(window); if (Active) Layout(); }

    void SetWindow(Vector2 window)
    {
        float k = UiScale.For(window);
        Layer.Scale = new Vector2(k, k);
        Layer.Position = UiScale.MovieOrigin;
        stage = UiScale.MovieStage(window);
    }

    void Layout()
    {
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
                // setLocalImageContent: a narrow stage fits 90% of its height, a wide one its width; centred, 30% down
                float w, h;
                if (stage.X / stage.Y < 1.3f) { h = stage.Y * 0.9f; w = sz.X * (h / sz.Y); }
                else { w = stage.X; h = sz.Y * (w / sz.X); }
                picture.Scale = new Vector2(w / sz.X, h / sz.Y);
                picture.Position = new Vector2((stage.X - w) * 0.5f, (stage.Y - h) * 0.3f);
            }
            if (screenShot) picture.Position = Vector2.Zero;
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
        bottom?.SetNamedText("percentText", $"{(uint)(count / total * 100)}%");
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
        foreach (var n in new Node2D[] { clear, picture, top, bottom, guide }) if (n != null) n.Visible = false;
        picture.Texture = null;
        Layer.Modulate = Colors.White;
        UiBridge.Raise(UILoadingProgressContract.Movie, UILoadingProgressContract.ToHost.OnFadeInComplete);
    }
}
