namespace Ms2.Formats.World;

/// <summary>
/// The field's backdrop placement, as the client's background movie does it. CUIService::LoadBackgroundMovie
/// (uiservice.cpp:504, @0x141ad9650) makes a GFxMovieBackground(bgDay pick, fielddata &lt;ui vscroll&gt;, &lt;ui hscroll&gt;); it loads
/// Background.gfx, whose initializeClass calls Background.loadBG(BG/&lt;name&gt;, vscroll) (GFxMovieBackground::vfunc_4,
/// gfxmoviebackground.cpp:424). loadBG keeps the second argument as m_scroll; on loadComplete and on every window resize
/// Background.onResize sizes the picture with onResizeScroll (m_scroll) or onResizeNoScroll. Each frame
/// GFxMovieBackground::vfunc_8 (@0x14127a070, gfxmoviebackground.cpp:486) then calls setCameraDistance(pi/3, pi/2, angle)
/// when vscroll is set and updatePos(player's minimap screen x) when hscroll is set; both only once the field has a
/// minimap (m_n98 = MinimapViewport::InitFromFieldMinimap, minimapviewport.cpp:42).
/// The AS bodies below are Background's (Background.gfx, class Background); CommonMath.clamp(lo, hi, v) = max(min(v, hi), lo)
/// and Background.interpolation(a, b, t) = a + clamp(0, 1, t) * (b - a).
/// </summary>
public static class FieldBackground
{
    /// <summary>The angles GFxMovieBackground::vfunc_8 passes: 1.0471976 (pi/3) and 1.5707964 (pi/2).</summary>
    public const double NearAngle = 1.0471976, FarAngle = 1.5707964;

    static double Clamp01(double v) => Math.Max(Math.Min(v, 1), 0);
    static double Interpolation(double a, double b, double t) => a + Clamp01(t) * (b - a);

    /// <summary>
    /// Background.onResizeScroll: the picture is 2320:2400 and twice the stage high (its width from that ratio), or as wide
    /// as the stage once the stage is wider than 2320. The position is left to setCameraDistance / updatePos.
    /// </summary>
    public static (double Width, double Height) ResizeScroll(uint stageWidth, uint stageHeight)
    {
        const uint l2 = 2320, l3 = 2400;
        uint l4 = stageHeight * 2;
        uint l5 = (uint)((double)(stageHeight * 2) / l3 * l2);    // uint / uint is a Number division in AS3
        if (stageWidth > l2) return (stageWidth, stageWidth * ((double)l2 / l3));
        return (l5, l4);
    }

    /// <summary>
    /// Background.onResizeNoScroll for a picture (a Bitmap has no m_size child): scaled to cover the stage, keeping its
    /// ratio, and centred. <paramref name="width"/> and <paramref name="height"/> are the bitmap's current width and
    /// height (a1.width / a1.height), which onResizeNoScroll updates on each resize.
    /// </summary>
    public static (double X, double Y, double Width, double Height) ResizeNoScroll(double width, double height, uint stageWidth, uint stageHeight)
    {
        uint l4 = stageHeight, l5 = stageWidth;
        uint l2 = (uint)width, l3 = (uint)height;
        uint l11 = l2, l12 = l3;
        uint l13 = (uint)Math.Ceiling(height), l14 = (uint)Math.Ceiling(width);
        if (l5 != l11 || l4 != l12)
        {
            double l16 = (double)l2 / l3, l17 = (double)l5 / l4;
            double l18 = Math.Ceiling(width), l19 = Math.Ceiling(height);
            if (l16 > l17)
            {
                double l7 = (double)l4 / l12;
                l13 = (uint)(l19 * l7);
                l14 = (uint)((uint)(l19 * l7) * l18 / l19);
            }
            else
            {
                double l6 = (double)l5 / l11;
                l14 = (uint)(l18 * l6);
                l13 = (uint)((uint)(l18 * l6) * l19 / l18);
            }
        }
        return ((l5 - (double)l14) * 0.5, (l4 - (double)l13) * 0.5, l14, l13);
    }

    /// <summary>
    /// The angle vfunc_8 hands setCameraDistance: CCapRotationController::SafeAcos (@0x140b02b90) of the unit camera world
    /// up vector (CameraMgr::FUN_140af0340, camera.cpp:1477) dotted with UNIT_X; <paramref name="upX"/> is that dot.
    /// </summary>
    public static double CameraAngle(double upX) => upX > -1 ? (upX < 1 ? Math.Acos((float)upX) : 0) : 3.1415927;

    /// <summary>
    /// Background.setCameraDistance(a1 = pi/3, a2 = pi/2, a3 = angle): a picture at least as high as the stage slides from its
    /// top (angle pi/2) to its bottom edge on the stage's bottom (angle pi/3); a shorter one sits at y 0.
    /// </summary>
    public static double CameraY(double contentHeight, uint stageHeight, double angle)
    {
        if (contentHeight < stageHeight) return 0;
        return Interpolation(0, stageHeight - contentHeight, (FarAngle - angle) / (FarAngle - NearAngle));
    }

    /// <summary>
    /// Background.updatePos(a1 = screen x): a picture at least as wide as the stage slides from its left edge (x 0) to its
    /// right edge on the stage's right as the player's minimap screen x goes from 0 to 300; a narrower one sits at x 0.
    /// </summary>
    public static double PosX(double contentWidth, uint stageWidth, double screenX)
    {
        if (contentWidth < stageWidth) return 0;
        return Interpolation(0, stageWidth - contentWidth, Clamp01(screenX / 300));
    }
}
