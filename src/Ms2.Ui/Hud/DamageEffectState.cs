namespace Ms2.Ui.Hud;

/// <summary>
/// The low-HP screen effect's state: UIDamageEffect's HP decision (uidamageeffect.cpp) and the uidamageeffect movie's
/// AS3 class scaleform.ms2.entity.DamageEffect, which owns the show and hide tweens and the alpha. The Godot node draws
/// <see cref="Visible"/> and <see cref="Alpha"/>; this class allocates nothing.
/// </summary>
public sealed class DamageEffectState
{
    /// <summary>The AS3 show and hide tweens' length: Tween(100, ...), linear (CLIK Tween with no ease).</summary>
    public const double TweenMs = 100;

    enum TweenKind : byte { None, Show, Hide }

    float lowPercent;                   // UIDamageEffect m_fLowHpPercent, the AS3 m_minHPPercent ("init"'s first argument)
    bool lowSent;                       // m_nLowHp
    TweenKind tween;                    // the AS3 m_showtween / m_hidetween; at most one runs
    double tweenFrom, tweenTo, tweenT;

    /// <summary>The AS3 m_maxAlpha: 1 from the ctor, then setAlpha's argument x 0.01.</summary>
    public double MaxAlpha { get; private set; } = 1;
    /// <summary>The entity's visible flag.</summary>
    public bool Visible { get; private set; }
    /// <summary>The entity's alpha.</summary>
    public double Alpha { get; private set; } = 1;
    /// <summary>A show or hide tween is running.</summary>
    public bool Tweening => tween != TweenKind.None;

    /// <summary>
    /// The movie's "init" (OnLoadComplete @0x141910980): the threshold (m_fLowHpPercent, the client constant
    /// DamageEffectMinHPPercent) and the alpha option (CGameOption's interface damageEffectAlpha, 0 to 100).
    /// The AS3 init sets m_minHPPercent, then calls setAlpha.
    /// </summary>
    public void Init(float lowHpPercent, int alphaOption)
    {
        lowPercent = lowHpPercent;
        SetAlpha(alphaOption);
    }

    /// <summary>
    /// The AS3 setAlpha (UIDamageEffect::SetAlpha @0x141910b30, sent from the interface options' save,
    /// CGameOptionInterface::vfunc_10, gameoptionstruct.cpp:388): m_maxAlpha = a x 0.01; the alpha itself changes now
    /// only when the entity is visible and neither tween runs. A running show tween keeps the target it was made with.
    /// </summary>
    public void SetAlpha(double a)
    {
        MaxAlpha = a * 0.01;
        if (!Visible || tween != TweenKind.None) return;
        Alpha = MaxAlpha;
    }

    /// <summary>
    /// UIStatusEventListener slot 3 (vfunc_oa0_3 @0x1419107d0) with the local player's HP: percent = cur x 100 / max as
    /// unsigned values (100 for max 0); "changeHP"(percent) is sent when the low flag changed or while it is low.
    /// </summary>
    public void Hp(long cur, long max)
    {
        float percent = max == 0 ? 100.0f : (float)(uint)cur * 100.0f / (float)(uint)max;
        bool low = percent <= lowPercent;
        if (low == lowSent && !low) return;
        lowSent = low;
        ChangeHp(percent);
    }

    /// <summary>
    /// The AS3 changeHP: when low differs from visible, low sets alpha 0 and shows the entity (the SHOW event runs onShow),
    /// not low runs onHide. A hide still running leaves the entity visible, so a low HP then does nothing.
    /// </summary>
    void ChangeHp(double percent)
    {
        bool low = percent <= lowPercent;
        if (low == Visible) return;
        if (low)
        {
            Alpha = 0;
            Visible = true;
            OnShow();
        }
        else OnHide();
    }

    /// <summary>The AS3 onShow: stop the hide tween; start the show tween to m_maxAlpha unless one runs.</summary>
    void OnShow()
    {
        if (tween == TweenKind.Hide) tween = TweenKind.None;
        if (tween == TweenKind.None) Start(TweenKind.Show, MaxAlpha);
    }

    /// <summary>The AS3 onHide: stop the show tween; start the hide tween to 0 unless one runs.</summary>
    void OnHide()
    {
        if (tween == TweenKind.Show) tween = TweenKind.None;
        if (tween == TweenKind.None) Start(TweenKind.Hide, 0);
    }

    void Start(TweenKind kind, double to)
    {
        tween = kind;
        tweenFrom = Alpha;
        tweenTo = to;
        tweenT = 0;
    }

    /// <summary>Run the tween by <paramref name="ms"/>. The hide's end (onHideComplete) hides the entity.</summary>
    public void Advance(double ms)
    {
        if (tween == TweenKind.None) return;
        tweenT += ms;
        double k = Math.Min(1, tweenT / TweenMs);
        Alpha = tweenFrom + (tweenTo - tweenFrom) * k;
        if (k < 1) return;
        if (tween == TweenKind.Hide) Visible = false;
        tween = TweenKind.None;
    }
}
