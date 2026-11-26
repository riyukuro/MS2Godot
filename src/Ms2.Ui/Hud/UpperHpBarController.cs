using System.Globalization;
using Ms2.Avm2.Flash;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIUpperHpBar (ms2/client/ms2/uiupperhpbar.cpp, "UpperHpBar"), the C++ side of uiupperhpbar: the target HP bar at the
/// top of the screen. Four entities (accumulated damage, boss, elite, normal), each a clip the movie's createHpBar makes
/// on first use and fed through setHP / update / destroyEntity; the movie tweens, times out and hides them.
/// </summary>
public sealed class UpperHpBarController : UiController
{
    public override string Movie => "uiupperhpbar";
    protected override string EntityMethod => "createHpBar";
    protected override bool IsDialog => false;          // a UIHeadUpDisplay
    /// <summary>m_58 = 0 in the UIUpperHpBar ctor (uiupperhpbar.cpp:249): hidden at load (vfunc_8(m_58)).</summary>
    public override bool VisibleOnLoad => false;

    /// <summary>What the bar reads from an actor (PCBase / CNpc / PC).</summary>
    public sealed class Actor
    {
        public int ObjectId;
        public bool IsNpc, IsPc, Dead;
        /// <summary>CNpcData GetClass (4/6 boss slot, 3/7 elite slot) and property tags 4 (boss: no timer) and 5 (accumulated damage).</summary>
        public int NpcClass;
        public bool Tag4, Tag5;
        /// <summary>Stat 4 (HP): FUN_141670a00 the current, FUN_141670a60 the total max, FUN_141670a30 the base max.</summary>
        public long Hp, MaxHp, BaseMaxHp;
        /// <summary>CNpc FUN_141095270: the damage accumulated on an accumulated-damage npc.</summary>
        public long AccumDamage;
        /// <summary>vfunc_77 the name, vfunc_79 the level; the type text: string table 6 (job by sub job) for a PC, 17 (race) for an npc.</summary>
        public string Name = "", TypeName = "";
        public int Level, SubJob, NpcId;
        /// <summary>CNpc IsClass4Or6 &amp;&amp; vfunc_65 (the m_delayHide case of UpdateHp).</summary>
        public bool DelayHide;
    }

    /// <summary>WorldService::FindObjectById.</summary>
    public Func<int, Actor> FindActor = _ => null;
    /// <summary>g_upperTargetId: the attacker id a hit must come from to bring a new target up.</summary>
    public int LocalTargetId;
    /// <summary>CUIService IsDialogOpen(0x4e96) || IsDialogOpen(0x4ec4): no target bar while either is open.</summary>
    public Func<bool> BlockingDialogOpen = () => false;
    /// <summary>CGameOption status m_hp: the HP number on the bar.</summary>
    public bool DisplayHpValue = true;
    /// <summary>FUN_1401aa6a0(npcId): the text the accumulated-damage tag formats.</summary>
    public Func<int, string> AccumTagName = id => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>UpperHpBarEntity (uiupperhpbar.cpp:85-200).</summary>
    sealed class Entity
    {
        public string Name; public int Kind;
        public int Id; public long Hp;
        public bool Visible;
        public object Clip;
    }

    // the constructor (:203): FUN_141b4c4c0 per slot, by kind
    readonly Entity[] entities =
    {
        new() { Name = "m_accumDamage", Kind = 0 }, new() { Name = "m_boss", Kind = 1 },
        new() { Name = "m_elite", Kind = 2 }, new() { Name = "m_normal", Kind = 3 },
    };
    Entity current;            // m_current

    /// <summary>OnLoadComplete (:209): initData({displayHpValue}), no current entity. The buff filter (FUN_141b4ceb0) has no buffs here.</summary>
    public override void OnLoadComplete()
    {
        Host.Invoke("initData", Host.Obj(("displayHpValue", DisplayHpValue)));
        current = null;
    }

    /// <summary>vfunc_43 (:217): the root aligned top centre (vfunc_2(1, 0, 0)) and shown.</summary>
    public override void Layout(double stageW, double stageH)
    {
        Host.Align(Host.Entity, 1, 0, 0);
        Host.SetVisible(Host.Entity, true);
    }

    /// <summary>GetEntity (:322): accumulated damage (tag 5), boss class 4/6 or a PC, elite class 3/7, else normal.</summary>
    Entity EntityOf(Actor a)
    {
        if (a.IsNpc)
        {
            if (a.Tag5) return entities[0];
            return a.NpcClass switch { 4 or 6 => entities[1], 3 or 7 => entities[2], _ => entities[3] };
        }
        return a.IsPc ? entities[1] : entities[3];
    }

    static string IdText(int id) => id.ToString(CultureInfo.InvariantCulture);   // FUN_141b4d690

    /// <summary>
    /// GetHpBarValues (:47): max and current HP (0 when dead) with the current as plain digits; for the accumulated-damage
    /// slot a tag-5 npc shows the damage past one bar (or what is left of the first) and its stage.
    /// </summary>
    (int Stage, long Cur, long Max, string HpText, string Shield) Values(Actor a, bool accum)
    {
        long max = a.MaxHp, cur = a.Dead ? 0 : a.Hp;
        string hp = IdText64(cur), shield = "";
        int stage = 0;
        if (accum && a.IsNpc && a.Tag5)
        {
            long total = a.AccumDamage, unit = a.BaseMaxHp;
            stage = unit == 0 ? 0 : (int)(total / unit + 1);
            if (total > unit)
            {
                long over = total - unit;
                cur = unit == 0 ? 0 : over % unit;
                hp = IdText64(over);
                shield = Format("s_upper_hp_bar_accum_damage", AccumTagName(a.NpcId));
            }
            else
            {
                cur = unit - total;
                hp = IdText64(cur);
                shield = Format("s_upper_hp_bar_accum_damage_shield", AccumTagName(a.NpcId));
            }
            max = cur;
        }
        return (stage, cur, max, hp, shield);
    }

    static string IdText64(long v) => v.ToString(CultureInfo.InvariantCulture);
    string Format(string key, string arg) => (Model.Tables.Common(key) ?? "").Replace("{0}", arg);

    /// <summary>
    /// FUN_141b4c510 (:229): a hit on an actor. Nothing while dialog 0x4e96 or 0x4ec4 is open; a new target comes up only
    /// for a hit by <see cref="LocalTargetId"/>, else only the shown target is refreshed. The slot's clip is made by
    /// createHpBar on first use, then setHP gets the values (no timer for a tag-4 boss).
    /// </summary>
    public void Hit(Actor a, int attackerId)
    {
        if (Host?.Entity == null || a == null) return;
        if (BlockingDialogOpen()) return;
        var e = EntityOf(a);
        if (!(attackerId == LocalTargetId || (e.Visible && e.Id == a.ObjectId))) return;     // FUN_141b4d630
        if (!TryShowEntity(e)) return;
        if (e.Clip == null)
        {
            e.Clip = Host.Invoke(Host.Entity, "createHpBar", (object)e.Name);
            if (e.Clip is Ms2.Avm2.AsObject clip)
            {
                // SetClip (:92): the clip's visibility and its show / hide events
                e.Visible = Host.Get(clip, "visible") is bool v && v;
                Func<object[], object> onShow = _ => { e.Visible = true; return null; };
                Func<object[], object> onHide = _ => { e.Visible = false; return null; };
                Host.Invoke(clip, "addEventListener", "show", onShow, false, 0, false);
                Host.Invoke(clip, "addEventListener", "hide", onHide, false, 0, false);
            }
        }
        var (stage, cur, max, hp, shield) = Values(a, e.Kind == 0);
        bool timer = !(a.IsNpc && a.Tag4);
        var args = Host.Obj(("id", IdText(a.ObjectId)), ("prev", (double)max), ("cur", (double)cur), ("max", (double)a.BaseMaxHp),
            ("strName", a.Name), ("strType", a.TypeName), ("level", a.Level), ("timer", timer), ("isShowTag", a.IsNpc),
            ("tagName", shield), ("jobCode", (uint)(a.IsPc ? a.SubJob : 0)), ("step", stage), ("hpText", hp));
        e.Id = a.ObjectId;
        e.Hp = cur;
        if (e.Clip != null) Host.Invoke(e.Clip, "setHP", args);
        current = e;
    }

    /// <summary>TryShowEntity (:333): a shown tag-4 boss keeps the bar; a normal target does not replace a shown boss or
    /// elite; else every slot hides and the target's shows.</summary>
    bool TryShowEntity(Entity target)
    {
        if (current != null && current.Visible && FindActor(current.Id) is { IsNpc: true, Tag4: true }) return false;
        if (target.Kind == 3)
        {
            if (entities[1].Visible && FindActor(entities[1].Id) is { IsNpc: true }) return false;
            if (entities[0].Visible && FindActor(entities[0].Id) is { IsNpc: true }) return false;
            if (entities[2].Visible) return false;
        }
        foreach (var e in entities) SetVisible(e, false);           // HideAllEntities
        SetVisible(target, true);
        return true;
    }

    void SetVisible(Entity e, bool show)
    {
        if (e.Clip == null) return;
        if (e.Visible != show) Host.SetVisible(e.Clip, show);
        e.Visible = show;
    }

    /// <summary>vfunc_o70_2 (:221): an actor left the field; its slot's destroyEntity when it shows that actor.</summary>
    public void Remove(Actor a)
    {
        if (Host?.Entity == null || a == null) return;
        var e = EntityOf(a);
        if (e.Id != a.ObjectId) return;                            // DestroyEntity (:101)
        e.Id = 0; e.Hp = 0;
        if (e.Clip != null) Host.Invoke(e.Clip, "destroyEntity", IdText(a.ObjectId));
        current = null;
    }

    /// <summary>vfunc_15 (:219) every frame: UpdateHp (:153) per shown slot, update(cur, hpText, stage, tag) when the HP changed.</summary>
    public void Process()
    {
        if (Host?.Entity == null) return;
        foreach (var e in entities)
        {
            if (e.Clip == null || e.Id == 0 || !e.Visible) continue;
            var a = FindActor(e.Id);
            if (a == null) { e.Id = 0; e.Hp = 0; continue; }
            var (stage, cur, _, hp, shield) = Values(a, e.Kind == 0);
            if (e.Hp == cur)
            {
                if (a.IsNpc && cur > 0 && a.DelayHide) Host.Set(e.Clip, "m_delayHide", true);
                continue;
            }
            Host.Invoke(e.Clip, "update", (double)cur, hp, stage, shield);
            e.Hp = cur;
        }
    }

    /// <summary>FUN_141b4ce20 (:315): the HP number on every slot (the option changed).</summary>
    public void SetHpTextVisible(bool show)
    {
        foreach (var e in entities)
            if (e.Clip != null && Host.Child("m_gauge", e.Clip) is { } g && Host.Child("m_hp", g) is { } hp) Host.SetVisible(hp, show);
    }

    /// <summary>The object id of the target the bar shows (m_current), or 0.</summary>
    public int Shown => current is { Visible: true } c ? c.Id : 0;
}
