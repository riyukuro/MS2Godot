using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// Where the lobby dialogs (login, character list, job pick, character create) go on the window. They are movies like
/// any other, so they are drawn at the UI scale (<see cref="UiScale"/>: CUIService::OnDeviceReset ->
/// GFxManager::SetMoviesScale scales every movie but the background picture; the lobby dialogs live on the main movie,
/// UISwfEntity's default slot 2), on the stage <see cref="UiScale.MovieStage"/> drawn from <see cref="UiScale.MovieOrigin"/>.
/// Then each dialog aligns itself in AS3, with Main.gfx having set CommonPath.flash = false (scaleform.ms2.Main.addedToStage),
/// so the non-flash branches below apply.
/// </summary>
public static class LobbyLayout
{
    /// <summary>
    /// The scenes run in window pixels, as the HUD does: the project's canvas stretch is off while they run, so the movies
    /// can use the client's own scale. Restored when the scene leaves the tree.
    /// </summary>
    public static void UseWindowPixels(Node scene)
    {
        var w = scene.GetWindow();
        var outer = w.ContentScaleMode;
        w.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        scene.TreeExiting += () => w.ContentScaleMode = outer;
    }

    /// <summary>Common.getLobbyRatioY with CommonPath.flash false: a stage taller than 1200 scales by its height over 1200.</summary>
    public static float LobbyRatio(Vector2 stage) => AsRound(stage.Y) > 1200 ? stage.Y / 1200f : 1f;

    /// <summary>Common.getUIScale: the stage width over 1920, at most 1.</summary>
    public static float AsUiScale(Vector2 stage) => stage.X >= 1920 ? 1f : stage.X / 1920f;

    /// <summary>AS3 Math.round (half up).</summary>
    public static float AsRound(double v) => (float)Math.Floor(v + 0.5);

    /// <summary>
    /// The dialog of a lobby movie: the root is the main timeline and the dialog (login_dialog, charlist_dialog, ...: the
    /// LobbyDialog subclass whose alignment() runs) is its one sprite child, placed by the timeline at its authored position.
    /// </summary>
    public static MovieClipNode.Child Dialog(MovieClipNode root) => root.Children.First(c => c.Node is MovieClipNode);

    /// <summary>
    /// Draws a lobby movie (root built at the UI scale <paramref name="u"/>) at the UI scale from the movie origin, with its
    /// dialog's origin at <paramref name="at"/> on the stage (the x / y setters) and the dialog scaled by <paramref name="r"/>
    /// (setActualScale).
    /// </summary>
    public static void Place(MovieClipNode root, Vector2 at, float u, float r = 1)
    {
        root.Scale = new Vector2(u, u);
        root.Position = UiScale.MovieOrigin;
        Dialog(root).Override = new Transform2D(0, new Vector2(r, r), 0, at);
        root.GotoFrame(root.Frame);
    }

    /// <summary>
    /// The size of <paramref name="clip"/> in the dialog's own units (AS3 width / height in its parent; the dialogs' content
    /// clips are unscaled), measured with the dialog root at scale <paramref name="rootScale"/>.
    /// </summary>
    public static Vector2 SizeInDialog(MovieClipNode clip, float rootScale) => clip.GlobalBounds().Size / rootScale;

    /// <summary>
    /// AS3 height of an unscaled clip in its parent: what it shows at its current frame. The lobby buttons
    /// (component_v1 export_icon_btn_lobby) are 84 high on "up"; their symbol bounds (-29, -32, 140 x 140) also hold the
    /// over / down glow.
    /// </summary>
    public static float AsHeight(MovieClipNode clip) => clip.ContentRect()?.Size.Y ?? clip.Bounds.Size.Y;

    /// <summary>
    /// CharacterListDialog.alignment / CharacterCreateDialog.alignment: applyScale(m_bg) (LobbyDialog.applyScale,
    /// getLobbyRatioY), then the dialog at ALIGN_CENTERRIGHT of the stage for m_bg's size times that scale.
    /// Returns the dialog's position on the stage and its scale.
    /// </summary>
    public static (Vector2 At, float R) CenterRightOfBg(MovieClipNode root, MovieClipNode bg, Vector2 stage, float u)
    {
        float r = LobbyRatio(stage);
        var size = bg != null ? SizeInDialog(bg, root.Scale.X) * r : Vector2.Zero;
        var at = new Vector2(stage.X - size.X, (stage.Y - size.Y) * 0.5f);   // Align.computePosition(ALIGN_CENTERRIGHT)
        Place(root, at, u, r);
        return (at, r);
    }

    /// <summary>
    /// btnsAlignment(btn, getUIScale, height, x, y) with (x, y) the stage's bottom left in the dialog's units: the button's
    /// origin 18 * scale in from the left and its <paramref name="height"/> plus 12 * scale up from the bottom.
    /// </summary>
    public static Vector2 BtnsAlignment(Vector2 bottomLeft, float height, Vector2 stage)
    {
        float g = AsUiScale(stage);
        return new Vector2(bottomLeft.X + AsRound(18 * g), bottomLeft.Y - height - AsRound(12 * g));
    }

    /// <summary>
    /// CharacterListDialog.alignment for a list root built at the UI scale <paramref name="u"/>: the dialog at the centre right
    /// for m_bg (m_content.m_bg), then btnsAlignment(m_btns, getUIScale, m_exit.height, 0, 0, stage) puts the exit / option
    /// buttons at the stage's bottom left. The region and event extras it also aligns are not shown here.
    /// </summary>
    public static void AlignCharacterList(MovieClipNode root, Vector2 stage, float u)
    {
        float exitH = root.FindClip("m_btns.m_exit") is { } exit ? AsHeight(exit) : 0;
        var (at, r) = CenterRightOfBg(root, root.FindClip("m_content.m_bg"), stage, u);
        if (root.ChildNamed("m_btns") is { } btns)
        {
            btns.Override = new Transform2D(0, BtnsAlignment(StageBottomLeftInDialog(at, r, stage), exitH, stage));
            root.GotoFrame(root.Frame);
        }
    }

    /// <summary>globalToLocal(new Point(0, stage.stageHeight)) for a dialog at <paramref name="at"/> with scale <paramref name="r"/>.</summary>
    public static Vector2 StageBottomLeftInDialog(Vector2 at, float r, Vector2 stage) => (new Vector2(0, stage.Y) - at) / r;
}
