using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// How the client scales its UI with the window. In the game scenes the client sets one viewport scale on every movie
/// (GFxManager::SetMoviesScale with FUN_141276490(window width, UI scale option)): 1 at 1920 and wider, otherwise
/// 2.0 - w/1920 above 1366, 2.1 - w/1920 above 1279, 2.2 - w/1920 above 1024 and 2.32 - w/1920 at 1024. A movie's stage
/// is the window times that factor and is drawn back into the window, so movies are drawn at 1 / factor
/// (0.698 for a 1280 x 720 window, stage 1835 x 1032). The dialogs place themselves on that stage and do not scale themselves.
/// </summary>
public static class UiScale
{
    public const float DesignWidth = 1920f;

    /// <summary>The client's viewport factor for a window width and the UI scale option (1 by default).</summary>
    public static float ViewportFactor(float width, float option = 1f)
    {
        float s = 1f;
        if (width >= DesignWidth) s = 1f;
        else if (width > 1366f) s = 2.0f - width / DesignWidth;
        else if (width > 1279f) s = 2.1f - width / DesignWidth;
        else if (width > 1024f) s = 2.2f - width / DesignWidth;
        else if (Math.Abs(width - 1024f) < 0.5f) s = 2.32f - width / DesignWidth;
        return s * option;
    }

    /// <summary>Scale at which movies are drawn.</summary>
    public static float For(Vector2 window, float option = 1f) => 1f / ViewportFactor(window.X, option);

    /// <summary>Stage size in stage units, the space movies place themselves in.</summary>
    public static Vector2 Stage(Vector2 window, float option = 1f) => window / For(window, option);

    /// <summary>
    /// GFxMovie::vfunc_7 (gfxmovie.cpp:146) gives every movie a viewport one pixel larger than the window on every side:
    /// (-1, -1, w + 2, h + 2). A movie's stage origin is drawn at <see cref="MovieOrigin"/> in the window.
    /// </summary>
    public static readonly Vector2 MovieOrigin = new(-1, -1);

    /// <summary>
    /// The stage a movie sees in AS3 (Stage.stageWidth / stageHeight): the viewport (w + 2, h + 2) at the movie scale.
    /// Stage::stageWidthGet / stageHeightGet return the visible frame rect's size cast to SInt32, so it is truncated:
    /// 1837 x 1034 for a 1280 x 720 window.
    /// </summary>
    public static Vector2 MovieStage(Vector2 window, float option = 1f) =>
        ((window + new Vector2(2, 2)) / For(window, option)).Floor();
}

/// <summary>Input focus for the rest of the game: movement code should ignore the keyboard while a text box has focus.</summary>
public static class UiFocus
{
    /// <summary>True while a LineEdit or TextEdit owns the keyboard focus.</summary>
    public static bool IsTyping => (Engine.GetMainLoop() as SceneTree)?.Root.GuiGetFocusOwner() is LineEdit or TextEdit;
}
