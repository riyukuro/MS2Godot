using Godot;
using Ms2.Formats.World;

namespace Ms2Godot.Ui;

/// <summary>
/// The red low-HP screen effect: UIDamageEffect ("DamageEffect", UI type 0x4e3c), the uidamageeffect movie's
/// dmgeff_entity_effect (AS3 scaleform.ms2.entity.DamageEffect).
/// <list type="bullet">
/// <item>Layer: UIDamageEffect takes UI layer 0 ("GC_Gfx HUD", FUN_141277f40); it is drawn over the 3D view and under every
/// other movie (name tags, HP bars, the HUD), so here it is the lowest canvas layer.</item>
/// <item>Threshold: m_fLowHpPercent, DamageEffectMinHPPercent from the client's constants (OnLoadComplete,
/// FUN_141910ab0): 30 in the shipped constants, 0 by the constants block's default.</item>
/// <item>HP (UIStatusEventListener slot 3, vfunc_oa0_3): percent = cur x 100 / max (100 for max 0); low = percent at or
/// below the threshold; "changeHP"(percent) is sent when low changes or while it is low.</item>
/// <item>changeHP: when low differs from visible, low makes the clip visible at alpha 0 and the show event tweens alpha to
/// m_maxAlpha over 100 ms; not low tweens it to 0 over 100 ms and hides it (onHide). CLIK's Tween with no ease is
/// linear.</item>
/// <item>m_maxAlpha: "init"'s second argument x 0.01, the game option interface/damageEffectAlpha (0 to 100, default
/// 100; the options dialog's m_damageEffectAlphaStep slider sends it again with "setAlpha"). There are no game options
/// here, so the default: 1.</item>
/// <item>Size (alignment): scaled by stage width / m_defaultSize and stage height / m_defaultSize, m_defaultSize = m_eff's
/// width (1200), and centred, so m_eff covers the movie's whole viewport, one pixel past the window on every side.</item>
/// <item>Art: plain timelines at the movie's 60 fps with no frame script, so every clip loops: the entity (70 frames,
/// m_eff 1 -> 0.80 -> 1), m_eff's two cross-fading vignettes (200 frames), m_backEff's drifting layers (490 frames) and
/// its additive (blend 8) pulse, 70 frames from alpha 0.398 down to 0.078 and back. One pulse is 70 frames, 1.17 s.</item>
/// </list>
/// The client hides it on the local player's death and shows it again on revival (StateObserver); there is no death here.
/// </summary>
public sealed partial class DamageEffect : CanvasLayer
{
    const float DefaultSize = 1200;      // m_eff.width
    const double TweenMs = 100;

    readonly MovieClipNode node;
    readonly float minPercent, maxAlpha = 1;
    bool low;                            // m_nLowHp, the flag last sent to the movie
    double tweenFrom, tweenTo, tweenT = -1;
    bool hideAfter;

    public DamageEffect(UiAssets ui, ClientConstants constants, Vector2 window)
    {
        Name = "DamageEffect";
        Layer = 1;                       // over the 3D view, under the name tags (5) and the HUD (10)
        MovieLayerHost.Bind(this, Ms2.Ui.CommonDepth.DamageEffect);   // UIDamageEffect's layer is 1 (m_2c, uidamageeffect.cpp:70)
        Offset = UiScale.MovieOrigin;
        minPercent = constants.Float("DamageEffectMinHPPercent", 0);   // the default is the client's constants block
        try { node = new UiMovie(ui, "uidamageeffect").BuildClass("dmgeff_entity_effect"); }
        catch (Exception e) { GD.PushWarning("damage effect movie: " + e.Message); }
        if (node == null) return;
        // alignment: setActualScale(stageWidth / m_defaultSize, stageHeight / m_defaultSize), x = stageWidth * 0.5,
        // y = stageHeight * 0.5, in stage units; the movie is drawn at the UI scale
        float u = UiScale.For(window);
        var stage = UiScale.MovieStage(window);
        node.Scale = new Vector2(stage.X / DefaultSize, stage.Y / DefaultSize) * u;
        node.Position = stage * 0.5f * u;
        foreach (var c in node.AllClips()) c.Loop();
        node.Visible = false;
        AddChild(node);
    }

    public DamageEffect() { }

    /// <summary>The local player's HP changed (vfunc_oa0_3).</summary>
    public void Hp(long cur, long max)
    {
        float percent = max == 0 ? 100f : cur * 100f / max;
        bool now = percent <= minPercent;
        if (now == low && !now) return;
        low = now;
        ChangeHp(percent);
    }

    void ChangeHp(float percent)
    {
        if (node == null) return;
        bool want = percent <= minPercent;
        if (want == node.Visible) return;
        if (want)
        {
            node.Visible = true;
            Tween(0, maxAlpha, false);   // onShow
        }
        else Tween(node.Modulate.A, 0, true);   // onHide
    }

    void Tween(double from, double to, bool hide)
    {
        tweenFrom = from; tweenTo = to; tweenT = 0; hideAfter = hide;
        node.Modulate = new Color(1, 1, 1, (float)from);
    }

    public override void _Process(double delta)
    {
        if (node == null || tweenT < 0) return;
        tweenT += delta * 1000;
        double k = Math.Min(1, tweenT / TweenMs);
        node.Modulate = new Color(1, 1, 1, (float)(tweenFrom + (tweenTo - tweenFrom) * k));
        if (k < 1) return;
        tweenT = -1;
        if (hideAfter) node.Visible = false;
    }
}
