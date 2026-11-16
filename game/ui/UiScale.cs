using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// How the client scales its UI with the window. The client gives every movie a stage as large as the window divided by the
/// viewport scale (GFxMovie::SetViewportScale, the UI scale option, 1 by default) and the movies scale themselves with
/// Common.getUIScale(stage): stage width / 1920, at most 1. The scale a movie is drawn with is therefore
/// min(1, windowWidth / 1920) at the default option, and its stage is window / that scale (1920 x 1080 for a 1280 x 720 window).
/// </summary>
public static class UiScale
{
    public const float DesignWidth = 1920f;

    /// <summary>Scale at which movies are drawn.</summary>
    public static float For(Vector2 window, float viewportScale = 1f)
    {
        float stageWidth = window.X / viewportScale;
        return Math.Min(1f, stageWidth / DesignWidth) * viewportScale;
    }

    /// <summary>Stage size in stage units, the space movies place themselves in.</summary>
    public static Vector2 Stage(Vector2 window, float viewportScale = 1f) => window / For(window, viewportScale);
}

/// <summary>Input focus for the rest of the game: movement code should ignore the keyboard while a text box has focus.</summary>
public static class UiFocus
{
    /// <summary>True while a LineEdit or TextEdit owns the keyboard focus.</summary>
    public static bool IsTyping => (Engine.GetMainLoop() as SceneTree)?.Root.GuiGetFocusOwner() is LineEdit or TextEdit;
}
