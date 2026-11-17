using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The target frame at the top of the screen (UIUpperHpBar, movie uiupperhpbar), its normal-monster slot
/// (upperhp_entity_normal). The other slots (accumulated damage, boss, elite) are not built; <see cref="Hit"/> ignores
/// victims that would go to them.
/// <list type="bullet">
/// <item>Place (vfunc_43, root vfunc_2(ALIGN_TOPCENTER, 0, 0)): the UpperHpBar dialog, about 1 x 90 (its only child,
/// m_dummy, is 600 x 90 at x scale 0.002), centred at the top of the stage; createHpBar adds the entity at (0, posY 55).</item>
/// <item>Hit (FUN_141b4c510): a damage record on a victim, by the local player or on the victim already shown, calls AS
/// setHP with the victim's id, previous, current and max HP, name, race name, level and the HP as plain digits
/// (StringFormat "{0}"). A new target sets the texts, snaps the bar to the previous HP and plays "_show" (scale 0.8 to 1.1
/// to 1, alpha 0.5 to 1 over frames 1-7), then the current HP, still without a tween. The same target only re-arms the
/// 3000 ms timer and tweens the bar.</item>
/// <item>Per frame (UpdateHp): a changed current HP of the shown victim calls AS update, which tweens the bar but keeps
/// the timer.</item>
/// <item>HP 0 (processLastDamage): the timer stops and the bar tweens to 0; when it is empty the name turns 0x707070
/// and "_hide" plays 500 ms later. The timer running out plays "_hide" too: alpha 1 to 0 over frames 12-29, hidden at 30.</item>
/// <item>Bar (ActiveStatusBar.setValue, fullwidth 303): the front mask and the trailing mask go to 303 x cur / max (at
/// least 1, 0 at cur 0) in 300 ms with Regular (quadratic) easing, the front easeOut and the trail easeIn on a drop and
/// the other way on a rise, with the end glints at their right edges fading as 1 - t / 300 ms; a change of 3 px or
/// less, or an untweened set, moves both at once with the glints off.</item>
/// </list>
/// </summary>
public sealed class TargetFrame
{
    /// <summary>A victim as FUN_141b4c510 sends it (GetHpBarValues, the slot's args): the object id string and the HP.</summary>
    public readonly record struct Victim(string Id, string Name, string Type, int Level, long Prev, long Cur, long Max);

    const int PosY = 55;                    // m_cacheAttr["m_normal"].posY
    const float FullWidth = 303;            // m_progressBar maxWidth (normal)
    const float TweenMs = 300, IgnoreTween = 3;
    const double DisappearMs = 3000, AfterLastMs = 500;
    const int HideEnd = 30;                 // the frame that sets visible = false

    readonly MovieClipNode entity, gauge, bar, level;
    readonly MovieClipNode.Child mask, leadMask, light, leadLight;
    readonly TextNode nameText, typeText, hpText;
    readonly int nameSize, hpSize;
    readonly float nameX;

    // UpperHPBarEntity state
    string targetId = "";
    Victim data; bool hasData;
    bool lastDamage, changeTargetOnLastDamage, hiding;
    double timerMs = -1;                    // m_disappearTimer, -1 when stopped
    // ActiveStatusBar state
    float position, maskW, leadW;
    Tween front, trail;
    bool barVisible = true;

    sealed class Tween
    {
        public float From, To, T; public bool EaseIn;
        public float Value => EaseIn ? From + (To - From) * (T / TweenMs) * (T / TweenMs) : From - (To - From) * (T / TweenMs) * (T / TweenMs - 2);
    }

    /// <summary>The shown victim's object id, or null (C++ m_current / m_id).</summary>
    public string Shown => entity != null && entity.Visible && !hiding ? targetId : null;

    public TargetFrame(UiAssets assets, Node parent, float u, Vector2 stage)
    {
        try
        {
            var m = new UiMovie(assets, "uiupperhpbar");
            var root = m.BuildRoot(u);
            root.Scale = new Vector2(u, u);
            var body = root.Children.Select(c => c.Node).OfType<MovieClipNode>().FirstOrDefault();
            if (body == null) return;
            body.HideNamed("m_dummy");
            var reg = UiLayout.Align(1, stage, new Vector2(1, 90), 0, 0);      // (int) of the dialog's 1.2 x 90
            root.Position = (reg - body.Position) * u;
            entity = m.BuildClass("upperhp_entity_normal", u);
            if (entity == null) return;
            entity.Position = new Vector2(0, PosY);
            body.AddChild(entity);
            var holder = new Node2D(); holder.AddChild(root); parent.AddChild(holder);
        }
        catch (Exception e) { GD.PushWarning("target frame: " + e.Message); entity = null; return; }
        gauge = entity.FindClip("m_gauge");
        bar = gauge?.FindClip("m_progressBar");
        level = gauge?.FindClip("m_level");
        if (gauge == null || bar == null) { entity = null; return; }
        nameText = gauge.Direct("m_name")?.Node as TextNode;
        typeText = gauge.Direct("m_type")?.Node as TextNode;
        hpText = gauge.Direct("m_hp")?.Node as TextNode;
        nameX = gauge.Direct("m_name")?.Base.Origin.X ?? 0;
        nameSize = nameText?.Label.LabelSettings.FontSize ?? 0;
        hpSize = hpText?.Label.LabelSettings.FontSize ?? 0;
        mask = bar.Direct("m_mask"); leadMask = bar.Direct("m_leadmask");
        light = bar.Direct("m_light"); leadLight = bar.Direct("m_leadlight");
        maskW = leadW = position = MaskWidth(mask);                         // draw(): m_position = m_mask.width
        entity.Visible = false;
    }

    /// <summary>FUN_141b4c510 for the normal slot: <paramref name="byMe"/> is the attacker being the local player.</summary>
    public void Hit(Victim v, bool byMe)
    {
        if (entity == null) return;
        if (!byMe && Shown != v.Id) return;                                 // FUN_141b4d630
        SetHp(v);
    }

    /// <summary>UpdateHp: the shown victim's current HP each frame; only a change reaches AS update.</summary>
    public void UpdateHp(string id, long cur)
    {
        if (entity == null || !entity.Visible || id != targetId || !hasData || cur == data.Cur) return;
        Update(cur);
    }

    /// <summary>destroyEntity: the victim left the world.</summary>
    public void Destroy(string id)
    {
        if (entity != null && id == targetId) Disappear();
    }

    void SetHp(Victim v)
    {
        bool newTarget = targetId.Length == 0 || v.Id != targetId;
        if (lastDamage)
        {
            if (!barVisible && v.Cur == 0) EndTween();
            changeTargetOnLastDamage = newTarget;
            data = v; hasData = true;
            return;
        }
        data = v; hasData = true;
        targetId = v.Id;
        bool snap = newTarget || changeTargetOnLastDamage;
        if (snap)
        {
            SetNameAndType(v.Name, v.Type);
            SetLevel(v.Level);
            ApplyHp(v.Prev, v.Max, false, v.Cur.ToString());
            ShowHpBar();
        }
        else if (hiding) ShowHpBar();
        if (v.Cur == 0) { ProcessLastDamage(v.Max); return; }
        timerMs = DisappearMs;
        ApplyHp(v.Cur, v.Max, !snap, v.Cur.ToString());
        changeTargetOnLastDamage = false;
    }

    void Update(long cur)
    {
        if (lastDamage || changeTargetOnLastDamage) return;
        data = data with { Cur = cur };
        if (cur == 0) ProcessLastDamage(data.Max);
        else ApplyHp(cur, data.Max, true, cur.ToString());
    }

    void ShowHpBar()
    {
        hiding = false;
        entity.Visible = true;
        entity.GotoLabel("_show", true);
    }

    void SetNameAndType(string name, string type)
    {
        if (nameText == null) return;
        Fit(nameText, name ?? "", nameSize);
        nameText.Label.LabelSettings.FontColor = Colors.White;
        if (typeText != null)
        {
            typeText.Text = string.IsNullOrEmpty(type) ? "" : "| " + type;
            gauge.SetChildX(gauge.Direct("m_type"), nameX + nameText.TextWidth + 5);
        }
    }

    void SetLevel(int lv)
    {
        if (level == null) return;
        if (lv == 0) { level.GotoLabel("unknown"); return; }
        level.GotoLabel("normal");
        level.SetText(lv.ToString());
    }

    /// <summary>TEXTAUTOSZ_SHRINK and VALIGN_CENTER: the field's own size, shrunk until the text fits, centred.</summary>
    static void Fit(TextNode t, string text, int size)
    {
        t.Text = text;
        if (size > 0) t.Label.LabelSettings.FontSize = size;
        t.ShrinkToFit();
        t.AlignCenter();
    }

    void ProcessLastDamage(long max)
    {
        timerMs = -1;
        lastDamage = true;
        if (position == 0) { OnEndFrame(); return; }
        ApplyHp(0, max, true, "0");
        if (front == null && trail == null) EndTween();
    }

    void OnEndFrame()
    {
        Disappear();
        lastDamage = false;
    }

    /// <summary>onEndTween: the drained bar hid; grey name, then "_hide" after 500 ms.</summary>
    void EndTween()
    {
        if (nameText != null) nameText.Label.LabelSettings.FontColor = Color.Color8(0x70, 0x70, 0x70);
        ClearData();
        timerMs = AfterLastMs;
    }

    void ClearData()
    {
        timerMs = -1;
        lastDamage = false;
        hasData = false;
        targetId = "";
    }

    void Disappear()
    {
        timerMs = -1;
        hiding = true;
        entity.GotoLabel("_hide", true);
    }

    void ApplyHp(long cur, long max, bool tween, string text)
    {
        SetValue(cur, max, tween);
        if (hpText != null) Fit(hpText, text, hpSize);
    }

    // ---- ActiveStatusBar ---------------------------------------------------------------------------------------------

    void SetValue(double cur, double max, bool tween)
    {
        if (max <= 0) return;
        cur = Math.Min(max, cur);
        float w = (float)(FullWidth * (cur / max));
        if (!barVisible) { barVisible = true; bar.Visible = true; }
        if (w == position) return;
        w = Math.Max(1, w);
        position = cur != 0 ? w : 0;
        front = trail = null;
        float d = w - maskW;
        if (tween) tween = Math.Abs(d) > IgnoreTween;
        if (tween)
        {
            front = new Tween { From = maskW, To = w, EaseIn = d >= 0 };
            trail = new Tween { From = leadW, To = w, EaseIn = d < 0 };
            SetLight(light, mask, maskW, 1);
            SetLight(leadLight, leadMask, leadW, 1);
        }
        else
        {
            maskW = leadW = w;
            SetLight(light, mask, 0, -1);
            SetLight(leadLight, leadMask, 0, -1);
            if (position == 0) { barVisible = false; bar.Visible = false; }
        }
        ApplyMasks();
    }

    void SetLight(MovieClipNode.Child l, MovieClipNode.Child m, float width, float alpha)
    {
        if (l == null || m == null) return;
        if (alpha < 0) { l.Override = null; l.BaseColor = new Color(1, 1, 1, 0); return; }
        var b = l.Base;
        l.Override = new Transform2D(b.X, b.Y, new Vector2(m.Base.Origin.X + width, b.Origin.Y));
        l.BaseColor = new Color(1, 1, 1, alpha);
    }

    void ApplyMasks()
    {
        SetMaskWidth(mask, maskW);
        SetMaskWidth(leadMask, leadW);
        bar.GotoFrame(bar.Frame);
    }

    static float MaskWidth(MovieClipNode.Child m) => m == null ? 0 : m.MaskBounds.Size.X * m.Base.X.X;

    static void SetMaskWidth(MovieClipNode.Child m, float w)
    {
        if (m == null || m.MaskBounds.Size.X <= 0) return;
        var b = m.Base;
        m.Override = new Transform2D(new Vector2(w / m.MaskBounds.Size.X, 0), b.Y, b.Origin);
    }

    /// <summary>The tweens, the timers and the end of "_hide"; call once a frame.</summary>
    public void Process(double delta)
    {
        if (entity == null) return;
        double ms = delta * 1000;
        if (front != null || trail != null)
        {
            Tween done = null;
            foreach (var t in new[] { front, trail })
            {
                if (t == null) continue;
                t.T = (float)Math.Min(TweenMs, t.T + ms);
            }
            if (front != null) maskW = front.Value;
            if (trail != null) leadW = trail.Value;
            float a = 1 - (front ?? trail).T / TweenMs;
            SetLight(light, mask, maskW, a);
            SetLight(leadLight, leadMask, leadW, a);
            if (front != null && front.T >= TweenMs) { done = front; front = null; }
            if (trail != null && trail.T >= TweenMs) { done = trail; trail = null; }
            ApplyMasks();
            if (done != null && front == null && trail == null)
            {
                if (maskW == leadW) SetLight(light, mask, 0, -1);
                if (leadW == FullWidth) SetLight(leadLight, leadMask, 0, -1);
                if (position == 0)
                {
                    barVisible = false; bar.Visible = false;            // ComponentEvent.HIDE
                    if (lastDamage) EndTween();
                }
            }
        }
        if (timerMs >= 0)
        {
            timerMs -= ms;
            if (timerMs <= 0) Disappear();
        }
        if (hiding && entity.Frame >= HideEnd)
        {
            hiding = false;
            entity.Visible = false;                                         // frame 30: visible = false, then onClose
            ClearData();
        }
    }
}
