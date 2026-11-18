using System.Xml.Linq;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The login screen's backdrop: a title animation movie behind the login dialog.
/// CUIService::LoadBackgroundMovie gives the login field (field 1) LoginBgBgmTable::GetBgDay() as a GFxMovieBackground
/// (GFxManager slot 4, the one movie SetMoviesScale leaves unscaled). That loads Background.gfx, whose
/// Background.loadBG(name) loads the movie the table names (clientPropertyBgDay, e.g. BG_Henesys_Login.swf, which is
/// bg_henesys_login.gfx in Gfx.m2d) and fits it to the window with onResizeNoScroll.
/// </summary>
public static class LoginBackground
{
    /// <summary>
    /// LoginBgBgmTable::SelectCurrent: the rows of table/loginbgbgm.xml for <paramref name="month"/> (1..12) with the
    /// highest priority, one of them at random. <paramref name="pick"/> chooses among them (the client: a random index).
    /// Null when the month has no row.
    /// </summary>
    public static string Select(XDocument table, int month, System.Func<int, int> pick)
    {
        if (table?.Root == null || month < 1 || month > 12) return null;
        var rows = new List<(string Bg, uint Priority)>();
        foreach (var e in table.Root.Elements("login"))
        {
            var months = ((string)e.Attribute("month") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!months.Contains(month.ToString())) continue;
            uint.TryParse((string)e.Attribute("priority"), out var p);
            rows.Add(((string)e.Attribute("clientPropertyBgDay") ?? "", p));
        }
        if (rows.Count == 0) return null;
        uint top = rows.Max(r => r.Priority);
        var candidates = rows.Where(r => r.Priority == top).ToList();
        return candidates[Math.Clamp(pick(candidates.Count), 0, candidates.Count - 1)].Bg;
    }

    /// <summary>The Gfx.m2d movie a loadBG url names: "BG_Henesys_Login.swf" is bg_henesys_login.gfx.</summary>
    public static string MovieName(string url) => System.IO.Path.GetFileNameWithoutExtension(url ?? "").ToLowerInvariant();

    /// <summary>
    /// The loaded movie's unscaled sizes onResizeNoScroll reads: a1.width / height (Flash counts m_size, hidden or not)
    /// and m_size's size in the title sprite. Taken once, before <see cref="Play"/> hides m_size.
    /// </summary>
    public readonly record struct Measure(Vector2 Raw, Vector2 MSize);

    public static Measure Measured(MovieClipNode content)
    {
        var ms = Title(content)?.ChildNamed("m_size")?.Node as MovieClipNode;
        var raw = content.ContentRect()?.Size ?? new Vector2(1920, 1200);
        var msSize = ms != null ? ms.Bounds.Size * new Vector2(ms.Transform.X.Length(), ms.Transform.Y.Length()) : raw;
        return new Measure(raw, msSize);
    }

    /// <summary>
    /// Background.onResizeNoScroll on a stage of <paramref name="window"/> pixels for the loaded movie
    /// <paramref name="content"/>: its first child's m_size (1920 x 1200) is scaled uniformly to cover the
    /// stage (by height when m_size is wider than the stage, else by width) and centred. It runs on load (loadComplete)
    /// and on every window resize (GFxMovieBackground::vfunc_16 invokes onResize), with the movie at its current scale.
    /// </summary>
    public static void Fit(MovieClipNode content, Vector2 window) => Fit(content, window, Measured(content));

    public static void Fit(MovieClipNode content, Vector2 window, Measure m)
    {
        uint l4 = (uint)window.Y, l5 = (uint)window.X;                  // stageHeight, stageWidth
        var s = content.Scale;                                          // a1.scaleX / scaleY
        var raw = m.Raw * s;                                            // a1.width / height
        // l9 = m_size: ceil(l9.width * a1.scaleX), ceil(l9.height * a1.scaleY)
        uint l2 = (uint)Math.Ceiling(m.MSize.X * s.X), l3 = (uint)Math.Ceiling(m.MSize.Y * s.Y);
        uint l11 = l2, l12 = l3;
        uint l13 = (uint)Math.Ceiling(raw.Y), l14 = (uint)Math.Ceiling(raw.X);
        if (l5 != l11 || l4 != l12)
        {
            double l16 = (double)l2 / l3, l17 = (double)l5 / l4;
            double l18 = Math.Ceiling(raw.X), l19 = Math.Ceiling(raw.Y);
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
        // a1.width = ceil(l14), a1.height = ceil(l13): the scale that gives those sizes
        var scale = new Vector2(l14 / m.Raw.X, l13 / m.Raw.Y);
        content.Scale = scale;
        // x / y from l9.getRect(this): m_size's size at that scale, centred on the stage
        content.Position = new Vector2((l5 - m.MSize.X * scale.X) * 0.5f, (l4 - m.MSize.Y * scale.Y) * 0.5f);
    }

    /// <summary>
    /// Plays the movie as Flash would. The title sprite hides m_size on its first frame and stops on its last
    /// (BG_*_Login_1 frame1 / frame320). The BG_Common intro clips over it (export_common_login_bg_white, a white cover
    /// fading out, and export_common_login_cloud) stop on their last, empty frame (frame182). Those clips with frame
    /// scripts play once to their end; the other animated clips loop. With <paramref name="settled"/> (shots) the
    /// once-only clips are put straight on their last frame and the looping ones stay on their first.
    /// </summary>
    public static void Play(MovieClipNode content, bool settled)
    {
        var title = Title(content);
        if (title?.ChildNamed("m_size") is { } ms) { ms.Hidden = true; ms.Node.Visible = false; }
        foreach (var c in content.AllClips().Skip(1))
        {
            if (c.FrameCount < 2) continue;
            if (c.FrameScripts) { if (settled) c.GotoFrame(c.FrameCount - 1); else c.PlayRange(0, c.FrameCount - 1); }
            else if (!settled) c.Loop();
        }
    }

    /// <summary>
    /// GFxMovieBackground::GotoAndPlay (gfxmoviebackground.cpp, @0x14127ab60): on a loaded background the movie's
    /// bgGotoAndPlay(label) is invoked and the call answers 1. The backdrop's AS is not run here, so the title clip goes to
    /// the label when it has one. STILL-OPEN: bgGotoAndPlay's body (the BG_*_Login movies carry no endTitleAni label).
    /// </summary>
    public static bool GotoAndPlay(MovieClipNode content, string label)
    {
        if (Title(content) is { } t && t.HasLabel(label)) t.GotoLabel(label, true);
        return true;
    }

    /// <summary>The title animation: the movie's first sprite, which stops on its last frame (endShowTitle).</summary>
    public static MovieClipNode Title(MovieClipNode content) =>
        content.Children.FirstOrDefault(c => c.Node is MovieClipNode)?.Node as MovieClipNode;
}
