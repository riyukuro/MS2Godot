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
/// <item>changeHP, the show and hide tweens and setAlpha: <see cref="Ms2.Ui.Hud.DamageEffectState"/>.</item>
/// <item>m_maxAlpha: "init"'s second argument x 0.01, the game option interface/damageEffectAlpha (0 to 100, default
/// 100), read at the load as OnLoadComplete does; the options dialog's save sends it again (<see cref="SetAlpha"/>,
/// CGameOptionInterface::vfunc_10, gameoptionstruct.cpp:388).</item>
/// <item>Size (alignment): scaled by stage width / m_defaultSize and stage height / m_defaultSize, m_defaultSize = m_eff's
/// width (1200), and centred, so m_eff covers the movie's whole viewport, one pixel past the window on every side.</item>
/// <item>Art: plain timelines at the movie's 60 fps with no frame script, so every clip loops: the entity (70 frames,
/// m_eff 1 -> 0.80 -> 1), m_eff's two cross-fading vignettes (200 frames), m_backEff's drifting layers (490 frames) and
/// its additive (blend 8) pulse, 70 frames from alpha 0.398 down to 0.078 and back. One pulse is 70 frames, 1.17 s.</item>
/// </list>
/// Death does not hide it (HP 0 is under the threshold). The client hides it (FUN_141910660, "onHide") and shows it
/// again (FUN_141910700) only around spectating: StateObserver (state 0x3f, stateobserver.cpp:406, 426) and MyPC's
/// StartObserving / StopObserving (mypc.cpp:3692, 3712, 3867). STILL-OPEN: spectating is not ported, so neither runs.
/// </summary>
public sealed partial class DamageEffect : CanvasLayer
{
    const float DefaultSize = 1200;      // m_eff.width

    readonly MovieClipNode node;
    readonly Ms2.Ui.Hud.DamageEffectState state = new();

    public DamageEffect(UiAssets ui, ClientConstants constants, Vector2 window, int alphaOption)
    {
        Name = "DamageEffect";
        Layer = 1;                       // over the 3D view, under the name tags (5) and the HUD (10)
        MovieLayerHost.Bind(this, Ms2.Ui.CommonDepth.DamageEffect);   // UIDamageEffect's layer is 1 (m_2c, uidamageeffect.cpp:70)
        Offset = UiScale.MovieOrigin;
        state.Init(constants.Float("DamageEffectMinHPPercent", 0), alphaOption);   // the default is the client's constants block
        try { node = new UiMovie(ui, "uidamageeffect").BuildClass("dmgeff_entity_effect"); }
        catch (Exception e) { GD.PushWarning("damage effect movie: " + e.Message); }
        if (node == null) return;
        Align(window);
        foreach (var c in node.AllClips()) c.Loop();
        node.Visible = false;
        AddChild(node);
    }

    public DamageEffect() { }

    /// <summary>
    /// The movie's alignment for the window: setActualScale(stageWidth / m_defaultSize, stageHeight / m_defaultSize),
    /// x = stageWidth * 0.5, y = stageHeight * 0.5, in stage units; the movie is drawn at the UI scale. Run at the load
    /// and again when the window is resized (the entity's layout after the device restore, UISwfEntityManager::vfunc_1,
    /// uiswfentitymanager.cpp:413), so the effect is kept with its state.
    /// </summary>
    public void Align(Vector2 window)
    {
        if (node == null) return;
        float u = UiScale.For(window);
        var stage = UiScale.MovieStage(window);
        node.Scale = new Vector2(stage.X / DefaultSize, stage.Y / DefaultSize) * u;
        node.Position = stage * 0.5f * u;
    }

    /// <summary>The local player's HP changed (vfunc_oa0_3).</summary>
    public void Hp(long cur, long max)
    {
        if (node == null) return;        // m_30 null: nothing
        state.Hp(cur, max);
        Show();
    }

    /// <summary>UIDamageEffect::SetAlpha (@0x141910b30) with the interface option damageEffectAlpha.</summary>
    public void SetAlpha(int alphaOption)
    {
        if (node == null) return;
        state.SetAlpha(alphaOption);
        Show();
    }

    void Show()
    {
        node.Visible = state.Visible;
        node.Modulate = new Color(1, 1, 1, (float)state.Alpha);
    }

    public override void _Process(double delta)
    {
        if (node == null || !state.Tweening) return;
        state.Advance(delta * 1000);
        Show();
    }
}
