using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// The EXP bar (uiexpbar's expbar_entity_body) without Flash. ExpBar's Constraints (REFLOW) stretch m_bg, m_restBg and
/// m_bar with the stage width (left and right margins kept) and keep m_point centred; setSize gives the status bar
/// m_fullwidth = m_bg.width - 6; StatusBar.setValue sets the mask width to fullwidth * cur / max (at least 1); makeSplit
/// puts expbar_status_split at 10 % .. 90 % of fullwidth (y 2). Rest exp: m_restBar / m_restPin / m_restBg show only with
/// rest exp, none here. Not ported: the m_eff particle that plays on a gain (export_effect_particle1).
/// The node is anchored to the bottom of the stage over its full width; its height is m_bg's (21).
/// </summary>
public partial class NExpBar : Control
{
    const string Movie = "uiexpbar";
    const float DesignWidth = 1920;
    double cur, max = 100; string pointText = "";
    NSkin bg, fill; Control mask; Label point; NSkin pointSkin;
    readonly List<NSkin> splits = new();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        bg = new NSkin { Movie = Movie, Symbol = "expbar_entity_body/m_bg" };
        AddChild(bg);
        // m_bar at (4,0); its fill m_bg at (0,5) shows through m_mask (x -2)
        mask = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(mask);
        fill = new NSkin { Movie = Movie, Symbol = "expbar_status_bar/m_bg" };
        mask.AddChild(fill);
        for (int i = 1; i <= 9; i++) { var s = new NSkin { Movie = Movie, Symbol = "expbar_status_split" }; splits.Add(s); AddChild(s); }
        pointSkin = new NSkin { Movie = Movie, Symbol = "expbar_entity_body/m_point", ShowTexts = true, Alpha = 0.699f };
        AddChild(pointSkin);
        Resized += Layout;
        Layout();
    }

    float FullWidth => Size.X - 6;                               // m_bg.width - 6 (m_bg spans the stage width)

    void Layout()
    {
        if (bg == null) return;
        float w = Size.X, grow = w - DesignWidth;
        bg.Position = Vector2.Zero; bg.Size = new Vector2(w, 21);
        // m_bar (x 4) keeps its right margin; m_mask sits at m_bar x - 2 and m_bar.m_bg at y 5
        fill.Size = new Vector2(1912 + grow, 12);
        float mw = (float)Math.Max(1, max < 1 ? 0 : FullWidth * cur / max);
        mask.Position = new Vector2(4 - 2, 0); mask.Size = new Vector2(mw, 18);
        fill.Position = new Vector2(2, 5);
        for (int i = 0; i < splits.Count; i++) splits[i].Position = new Vector2(FullWidth * (i + 1) / 10f, 2);
        pointSkin.Position = new Vector2(855 + grow / 2, 1);              // CENTER_H
        pointSkin.Modulate = new Color(1, 1, 1, 0.699f);
        if (pointSkin.Labels.Count > 0) pointSkin.Labels.Values.First().Text = pointText;
    }

    /// <summary>setExp(cur, rest, text): the bar to cur / 100 and the m_point text.</summary>
    public void SetExp(double percent, string text)
    {
        cur = percent; max = 100; pointText = text;
        if (IsInsideTree()) Layout();
    }
}
