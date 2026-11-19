using System.Globalization;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Quest;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// ASEffect (client/ms2/aseffect.cpp:70-300): one buff icon, an "export_additional_effect" (component_v1's
/// scaleform.ms2.additional.Effect) the manager adds to its buff or debuff container. m_cooldown's timeline is the cooldown
/// ring: frames 0-29 the 500 ms appear, 30-149 the time used, 0x95 the end.
/// </summary>
public sealed class BuffEffect
{
    readonly BuffEffectManager owner;
    UiHost Host => owner.Host;
    /// <summary>m_buff.</summary>
    public AdditionalBuff Buff { get; private set; }
    /// <summary>m_value: the AS object, null until BindChildren made it.</summary>
    public object Value { get; private set; }
    object cooldown, border, textField, countField;
    /// <summary>m_e0: the tooltip align; m_e4: the sort order; m_90: the add tick.</summary>
    public int TooltipAlign, SortOrder;
    public uint StartTime;
    /// <summary>m_e8 (still counting) and m_e9 (the icon needs a refresh).</summary>
    public bool IsTimed { get; private set; }
    bool iconDirty = true;
    /// <summary>m_94, the remain text's 100 ms Unk_IntervalTimer.</summary>
    ulong lastText;
    bool textTimerStarted;

    /// <summary>The ctor @0x1409b2d70.</summary>
    public BuffEffect(BuffEffectManager owner, AdditionalBuff buff)
    {
        this.owner = owner;
        Buff = buff;
        IsTimed = buff.IsActive;
    }

    /// <summary>IsLoaded @0x1409b3300: the value is an object.</summary>
    public bool IsLoaded => Value is AsObject;

    /// <summary>IsBuffType @0x1409b37e0.</summary>
    public bool IsBuffType => Buff.LevelData.IsBuff;

    /// <summary>BindChildren @0x1409b2fb0: CreateObject("export_additional_effect"), its m_cooldown, m_buffBorder,
    /// m_textField and m_textCountField, the member function GetTooltipCallback (Effect.getTooltipData calls it), and
    /// FUN_1422c6380 (TooltipManager.registerMouseEvent).</summary>
    public void BindChildren()
    {
        if (IsLoaded) return;
        var cls = Host.RT.GetClass("export_additional_effect");
        if (cls == null) return;
        object v = null;
        Host.RT.Guard("create export_additional_effect", () => v = Host.RT.Construct(cls, Array.Empty<object>()));
        if (v is not AsObject) return;
        Value = v;
        cooldown = Host.Get(v, "m_cooldown");
        border = Host.Get(v, "m_buffBorder");
        textField = Host.Get(v, "m_textField");
        countField = Host.Get(v, "m_textCountField");
        Host.Set(v, "GetTooltipCallback", new AsPlainFunction(Host.RT, new NativeMethod("GetTooltipCallback", (_, _) => Host.ToAs(GetTooltip()))));
        TooltipManager("registerMouseEvent");
    }

    /// <summary>The dtor @0x1409b2ec0 (the last reference goes when RemoveEffect drops it): FUN_1422c63f0
    /// (TooltipManager.unregisterMouseEvent) and clearIcon when loaded.</summary>
    public void Release()
    {
        if (!IsLoaded) return;
        TooltipManager("unregisterMouseEvent");
        Host.Invoke(Value, "clearIcon");
    }

    void TooltipManager(string method)
    {
        if (Host.RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm) Host.Invoke(tm, method, Value);
    }

    /// <summary>SetBuff @0x1409b3740.</summary>
    public void SetBuff(AdditionalBuff buff)
    {
        Buff = buff;
        iconDirty = true;
        IsTimed = buff.IsActive;
    }

    /// <summary>
    /// Update @0x1409b3330 (loaded and timed only): UpdateIcon; the first 500 ms after the add frame elapsed*60/1000; an
    /// effect without a span (start == end) or with a hidden buff icon frame 0x1e; else while time is left frame
    /// 30 + 120 * used / total and, every 100 ms, the remain text; then not timed, frame 0x95 and no text.
    /// STILL-OPEN: frame 0 (the first 17 ms) goes through the AS gotoAndStop, which clamps it to the first frame.
    /// </summary>
    public void Update(uint nowMs)
    {
        if (!IsLoaded || !IsTimed) return;
        UpdateIcon();
        uint elapsed = nowMs - StartTime;
        if (elapsed < 500)
        {
            GotoFrame((int)(elapsed * 60 / 1000));
            return;
        }
        if (Buff.StartTick == Buff.EndTick || Buff.LevelData.IsHideBuffIcon)
        {
            GotoFrame(0x1e);
            return;
        }
        float remain = Buff.RemainTicks * 0.001f;           // FUN_14028d280
        if (remain > 0f)
        {
            float total = Buff.Duration * 0.001f;
            GotoFrame((int)(30f + 120f * (total - remain) / total));
            if (!textTimerStarted || nowMs - lastText >= 100)
            {
                textTimerStarted = true;
                lastText = nowMs;
                Host.Set(textField, "text", FormatRemain(remain));
            }
            return;
        }
        IsTimed = false;
        GotoFrame(0x95);
        Host.Set(textField, "text", "");
    }

    void GotoFrame(int frame)
    {
        if ((int)Rt.ToNumber(Host.Get(cooldown, "currentFrame")) != frame) Host.Invoke(cooldown, "gotoAndStop", frame);
    }

    /// <summary>IsOwnerAlive @0x1409b3840: false without a buff, when IsUpperTargetId(owner) is 1, or when
    /// IsUpperTargetId(caster) is 0.</summary>
    public bool IsOwnerAlive => Buff != null && !owner.IsUpperTargetId(Buff.OwnerId) && owner.IsUpperTargetId(Buff.CasterId);

    /// <summary>UpdateIcon @0x1409b3a20 (loaded, icon dirty): the border "none" for a buff, else "yellow" when
    /// <see cref="IsOwnerAlive"/>, else "red"; the overlap count above 1; no remain text; setIcon(icon).</summary>
    void UpdateIcon()
    {
        if (!IsLoaded || !iconDirty) return;
        string frame = Buff.LevelData.IsBuff ? "none" : IsOwnerAlive ? "yellow" : "red";
        Host.Invoke(border, "gotoAndStop", frame);
        Host.Set(countField, "text", Buff.OverlapCount > 1 ? Buff.OverlapCount.ToString(CultureInfo.InvariantCulture) : "");
        Host.Set(textField, "text", "");
        Host.Invoke(Value, "setIcon", Buff.LevelData.Icon);
        iconDirty = false;
    }

    /// <summary>FormatRemain @0x1409b3d50: "{0}{1}" of the whole days, hours, minutes or seconds and s_word_day / s_word_hour2 /
    /// s_word_min / s_word_sec (GetStringCommon 0x5ce, 0x5cd, 0x5ca, 0x5cb); under a second "{0:.1f}".</summary>
    string FormatRemain(float seconds)
    {
        int n = (int)seconds;
        var t = owner.Model.Tables;
        if (n >= 86400) return ClientFormat.Format("{0}{1}", n / 86400, t.Common("s_word_day") ?? "");
        if (n >= 3600) return ClientFormat.Format("{0}{1}", n / 3600, t.Common("s_word_hour2") ?? "");
        if (n >= 60) return ClientFormat.Format("{0}{1}", n / 60, t.Common("s_word_min") ?? "");
        if (n > 0) return ClientFormat.Format("{0}{1}", n, t.Common("s_word_sec") ?? "");
        return ClientFormat.Format("{0:.1f}", seconds);
    }

    /// <summary>GetTooltip @0x1409b3950: ASTooltip(2).FUN_1409e2750(level, m_e0, GetRemainTicks).</summary>
    AsTooltip GetTooltip() => BuffTooltip.Build(owner.Model.Tables, Buff.LevelData, TooltipAlign, Buff.RemainTicks);
}

/// <summary>
/// ASEffectManager (aseffect.cpp:325-560) over a panel's effect clip: a buff container (A) and a debuff container (B), each
/// effect attached on the first Update after its add, laid out in rows of <see cref="Columns"/> 42 px apart.
/// </summary>
public sealed class BuffEffectManager
{
    public readonly UiHost Host;
    public readonly UiModel Model;
    readonly object a, b;
    /// <summary>m_a0 the effects' tooltip align, m_a4 the most shown, m_ac the columns, m_b0 rows go up, m_b1 hide effects
    /// whose owner is not alive.</summary>
    public int TooltipAlign = 7, Max = 10, Columns = 0x14;
    public bool Up, Filter;
    bool dirty = true;
    /// <summary>m_b8, by key. STILL-OPEN: the client's CAtlMap hash order decides the attach order (so the order of effects
    /// with the same sort order); here it is the Dictionary's.</summary>
    readonly Dictionary<int, BuffEffect> effects = new();
    /// <summary>m_100 (buffs) and m_110 (debuffs), by sort order.</summary>
    readonly List<BuffEffect> buffs = new(), debuffs = new();
    /// <summary>GetTickCountMs2.</summary>
    public Func<uint> TickMs = () => 0;
    /// <summary>IsUpperTargetId.</summary>
    public Func<int, bool> IsUpperTargetId = _ => false;

    /// <summary>The ctor @0x1409b5200: two flash.display.MovieClip containers added to the clip.</summary>
    public BuffEffectManager(UiHost host, UiModel model, object clip)
    {
        Host = host;
        Model = model;
        a = NewClip();
        b = NewClip();
        Host.Invoke(clip, "addChild", a);
        Host.Invoke(clip, "addChild", b);
    }

    object NewClip()
    {
        object v = null;
        if (Host.RT.GetClass("flash.display.MovieClip") is { } cls)
            Host.RT.Guard("create MovieClip", () => v = Host.RT.Construct(cls, Array.Empty<object>()));
        return v;
    }

    /// <summary>SetPosA / SetPosB: the containers' positions.</summary>
    public void SetPosA(int x, int y) { Host.Set(a, "x", x); Host.Set(a, "y", y); }
    public void SetPosB(int x, int y) { Host.Set(b, "x", x); Host.Set(b, "y", y); }

    /// <summary>SetFilter @0x1409b58f0.</summary>
    public void SetFilter(bool filter)
    {
        if (Filter == filter) return;
        dirty = true;
        Filter = filter;
    }

    /// <summary>Update @0x1409b5610: attach the unloaded effects, update the timed ones, then lay both lists out when one
    /// stopped or anything changed.</summary>
    public void Update()
    {
        uint now = TickMs();
        foreach (var e in effects.Values.ToList())
        {
            if (!e.IsLoaded) Attach(e);
            if (!e.IsTimed) continue;
            e.Update(now);
            if (!e.IsTimed) dirty = true;
        }
        if (!dirty) return;
        Layout(buffs, false);
        Layout(debuffs, Filter);
        dirty = false;
    }

    /// <summary>AddEffect @0x1409b5940: a new effect with the manager's tooltip align, the add tick and the buff's sort order,
    /// kept by key (SetAt: an effect already at the key leaves the map but stays in its list, as in the client).</summary>
    public void AddEffect(AdditionalBuff buff)
    {
        var e = new BuffEffect(this, buff) { TooltipAlign = TooltipAlign, StartTime = TickMs(), SortOrder = buff.SortOrder };
        effects[buff.Key] = e;
    }

    /// <summary>UpdateEffect @0x1409b5aa0.</summary>
    public void UpdateEffect(AdditionalBuff buff)
    {
        if (effects.TryGetValue(buff.Key, out var e)) e.SetBuff(buff);
        dirty = true;
    }

    /// <summary>RemoveEffect @0x1409b5b60: detach a loaded effect, then drop it (its dtor).</summary>
    public void RemoveEffect(int key)
    {
        if (!effects.Remove(key, out var e)) return;
        if (e.IsLoaded) Detach(e);
        e.Release();
    }

    /// <summary>Clear @0x1409b5c20.</summary>
    public void Clear()
    {
        foreach (var key in effects.Keys.ToList()) RemoveEffect(key);
    }

    /// <summary>InsertSorted @0x1409b5d10: after the last effect whose sort order is not below the new one's.</summary>
    static void InsertSorted(List<BuffEffect> list, BuffEffect e)
    {
        int i = list.Count;
        while (i > 0 && list[i - 1].SortOrder < e.SortOrder) i--;
        list.Insert(i, e);
    }

    /// <summary>Layout @0x1409b5f10: hide the effects that stopped (and with the filter those whose owner is not alive); show
    /// the first <see cref="Max"/> others at (col * 42, row * 42), rows going up with <see cref="Up"/>.</summary>
    void Layout(List<BuffEffect> list, bool filter)
    {
        int shown = 0;
        foreach (var e in list)
        {
            if (!e.IsTimed || (filter && !e.IsOwnerAlive))
            {
                Host.SetVisible(e.Value, false);
                continue;
            }
            if (shown < Max)
            {
                int col = shown % Columns, row = shown / Columns;
                Host.SetVisible(e.Value, true);
                Host.Set(e.Value, "x", col * 0x2a);
                Host.Set(e.Value, "y", Up ? row * -0x2a : row * 0x2a);
                shown++;
            }
            else Host.SetVisible(e.Value, false);
        }
    }

    /// <summary>Attach @0x1409b6170: BindChildren, then into container A and the buff list, or B and the debuff list.</summary>
    void Attach(BuffEffect e)
    {
        e.BindChildren();
        if (e.IsBuffType)
        {
            if (e.IsLoaded) Host.Invoke(a, "addChild", e.Value);
            InsertSorted(buffs, e);
        }
        else
        {
            if (e.IsLoaded) Host.Invoke(b, "addChild", e.Value);
            InsertSorted(debuffs, e);
        }
        dirty = true;
    }

    /// <summary>Detach @0x1409b6370.</summary>
    void Detach(BuffEffect e)
    {
        if (e.IsBuffType)
        {
            Host.Invoke(a, "removeChild", e.Value);
            buffs.Remove(e);
        }
        else
        {
            Host.Invoke(b, "removeChild", e.Value);
            debuffs.Remove(e);
        }
        dirty = true;
    }
}

/// <summary>ASTooltip::FUN_1409e2750 (astooltip.cpp:4978): the buff tooltip.</summary>
public static class BuffTooltip
{
    /// <summary>
    /// The name in #00AEFF (a debuff #FF4040) and a line break, then the description; for an effect with a duration, time
    /// left and a shown buff icon two line breaks and s_word_remain of the time left (FormatTimeSpan, every part; under a
    /// second s_common_millisec). The text tooltip class, delays 0, the align and max width 0x198. Null without a level.
    /// </summary>
    public static AsTooltip Build(GameTables t, AdditionalLevelData level, int align, uint remainTicks)
    {
        if (level == null) return null;
        uint id = level.Parent?.Id ?? 0;
        string name = t.AdditionalDescription(id, level.Level, "name");
        string text = ClientFormat.Format(level.IsBuff ? "<FONT color='#00AEFF'>{0}</FONT>\n" : "<FONT color='#FF4040'>{0}</FONT>\n", name);
        string desc = t.AdditionalDescription(id, level.Level, "tooltipDescription");
        if (remainTicks > 0 && level.DurationTick > 0 && !level.IsHideBuffIcon)
        {
            string time = FormatTimeSpan(t, (long)(remainTicks * 0.001f));
            if (time.Length == 0 && remainTicks < 1000)
                time = CommonFormat(t, "s_common_millisec", remainTicks / 1000f);
            string remain = ClientFormat.Format(t.Common("s_word_remain") ?? "", time);
            desc += ClientFormat.Format("\n\n{0}", remain);
        }
        var tip = new AsTooltip
        {
            ["tooltip"] = "export_common_tooltip_tooltipText",
            ["text"] = text + desc,
            ["onDelay"] = 0,
            ["offDelay"] = 0,
        };
        tip.SetTooltipAlign(align);
        tip["maxWidth"] = 0x198;
        return tip;
    }

    /// <summary>FormatTimeSpan (timeutil.cpp:371) with every part: the non-zero days, hours, minutes and seconds of the span
    /// (FormatStringId 2426 s_common_day, 2429 s_common_hour, 2428 s_common_min, 2427 s_common_sec), joined by spaces.</summary>
    public static string FormatTimeSpan(GameTables t, long seconds) => FormatTimeSpan(t, seconds, true, true, true, true);

    /// <summary>FormatTimeSpan (timeutil.cpp:371, @0x14086aec0): each part whose flag is set and which is not zero.</summary>
    public static string FormatTimeSpan(GameTables t, long seconds, bool showDays, bool showHours, bool showMinutes, bool showSeconds)
    {
        var parts = new List<string>();
        long days = seconds / 86400, hours = seconds / 3600 % 24, minutes = seconds / 60 % 60, secs = seconds % 60;
        if (showDays && days > 0) parts.Add(CommonFormat(t, "s_common_day", days));
        if (showHours && hours > 0) parts.Add(CommonFormat(t, "s_common_hour", hours));
        if (showMinutes && minutes > 0) parts.Add(CommonFormat(t, "s_common_min", minutes));
        if (showSeconds && secs > 0) parts.Add(CommonFormat(t, "s_common_sec", secs));
        return string.Join(" ", parts);
    }

    /// <summary>FUN_14028dfa0 / FormatStringId: a common string formatted with the value; "" when the string is empty.</summary>
    static string CommonFormat(GameTables t, string key, object value) => ClientFormat.Format(t.Common(key) ?? "", value);
}
