using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The own PC's reaction to a hit: PC::vfunc_33 (pc.cpp:885), its push (PC::vfunc_107, pc.cpp:4272), the super armor tests
/// (AdditionalCliList::GetSuperArmor / FUN_140970c10, additionalcli_2.cpp:599-660) and vfunc_34's dot.
/// </summary>
public class HitReactionTests
{
    sealed class Body : IHitBody
    {
        public int ClientStateId { get; set; } = HitReaction.StateIdle;
        public bool Takes = true;
        public bool StateTakesHits => ClientStateId == HitReaction.StateSkill ? Push.Speed > 0f : Takes;
        public bool HitHasDuration { get; set; }
        public int HitPushPriority { get; set; }
        public PushControl Push { get; } = new();
        public Vector3 FieldPosition { get; set; }
    }

    static SkillAttackDamage Attack(int push = 0, float distance = 0f, int priority = 0, int hitImmuneBreak = 0) => new()
    {
        Push = push, PushDistance = distance, PushPriority = priority, HitImmuneBreak = hitImmuneBreak,
        PushDuration = 0.5f, PushUpDistance = 0f, PushDown = 1, HitSpeedRate = 1.5f, HitPauseTime = 0.25f,
        PushAnimation = "anim", PushFall = 1, PushEaseType = 1,
    };

    [Test]
    public void BuffSuperArmorIsTheLargestThatCountsInTheField()
    {
        // <Motion superArmor superArmorApplyFieldType> (additionalactionadditional.cpp:1112-1113); FUN_1409726e0's field test
        var onType9 = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse(
            "<level level=\"1\"><Motion superArmor=\"3\" superArmorApplyFieldType=\"2\"/></level>"), null);
        var everywhere = AdditionalLevelData.Load(System.Xml.Linq.XElement.Parse(
            "<level level=\"1\"><Motion superArmor=\"1\"/></level>"), null);
        Assert.That((onType9.SuperArmor, onType9.SuperArmorApplyFieldType), Is.EqualTo((3u, 2)));
        var list = new AdditionalBuffList((id, _) => id == 7 ? onType9 : id == 8 ? everywhere : null) { UpperTargetId = 5 };
        Assert.That(list.SuperArmor(true), Is.EqualTo(0u));
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 10, 5, 0, new BuffState(0, 1000, 7, 1, 1, 1), 0));
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 11, 5, 0, new BuffState(0, 1000, 8, 1, 1, 1), 0));
        Assert.That((list.SuperArmor(true), list.SuperArmor(false)), Is.EqualTo((3u, 1u)));
        list.Apply(new BuffPacket(BuffPacket.Add, 5, 12, 5, 0, new BuffState(0, 1000, 7, 1, 1, 0), 0));   // a disabled one: no
        Assert.That(list.SuperArmor(true), Is.EqualTo(3u));

        Assert.That(AdditionalBuffList.SuperArmorApplies(0, 0, true), Is.False, "no armor never counts");
        Assert.That((AdditionalBuffList.SuperArmorApplies(2, 0, false), AdditionalBuffList.SuperArmorApplies(2, 1, false),
            AdditionalBuffList.SuperArmorApplies(2, 1, true), AdditionalBuffList.SuperArmorApplies(2, 2, false)),
            Is.EqualTo((true, true, false, false)));
        // GetSuperArmor: the cast's counts only in StateSkill
        Assert.That((HitReaction.SuperArmor(HitReaction.StateSkill, 4, 1), HitReaction.SuperArmor(HitReaction.StateIdle, 4, 1),
            HitReaction.SuperArmor(HitReaction.StateSkill, 1, 2)), Is.EqualTo((4u, 1u, 2u)));
    }

    [Test]
    public void SuperArmorHoldsByItsLevel()
    {
        var plain = Attack();
        var push = Attack(push: 1);
        var lift = Attack(push: 1); lift.PushUpDistance = 30f;
        var grab = Attack(); grab.GrabTargetType = 1;
        var breaker = Attack(); breaker.SuperArmorBreak = 1;
        Assert.That(HitReaction.SuperArmorHolds(breaker, 3, false, false, true), Is.True, "the invincible time holds first");
        Assert.That(HitReaction.SuperArmorHolds(null, 3, false, false, false), Is.False);
        Assert.That(HitReaction.SuperArmorHolds(breaker, 3, false, false, false), Is.False);
        Assert.That(HitReaction.SuperArmorHolds(plain, 0, false, false, false), Is.False);
        // 1: a push or a grab breaks it
        Assert.That((HitReaction.SuperArmorHolds(plain, 1, false, false, false), HitReaction.SuperArmorHolds(push, 1, false, false, false),
            HitReaction.SuperArmorHolds(grab, 1, false, false, false)), Is.EqualTo((true, false, false)));
        // 2: only a lifting push or a grab
        Assert.That((HitReaction.SuperArmorHolds(push, 2, false, false, false), HitReaction.SuperArmorHolds(lift, 2, false, false, false),
            HitReaction.SuperArmorHolds(grab, 2, false, false, false)), Is.EqualTo((true, false, false)));
        // 3: everything
        Assert.That((HitReaction.SuperArmorHolds(lift, 3, false, false, false), HitReaction.SuperArmorHolds(grab, 3, false, true, false)),
            Is.EqualTo((true, true)));
        // 4: not against the push itself, nor a grab
        Assert.That((HitReaction.SuperArmorHolds(push, 4, false, false, false), HitReaction.SuperArmorHolds(push, 4, false, true, false),
            HitReaction.SuperArmorHolds(grab, 4, false, false, false)), Is.EqualTo((true, false, false)));
        // the push kind is the field's: pushApplyField 1 is no push on the ground (map type 9 or 3)
        var airOnly = Attack(push: 1); airOnly.PushApplyField = 1;
        Assert.That((HitReaction.SuperArmorHolds(airOnly, 1, true, false, false), HitReaction.SuperArmorHolds(airOnly, 1, false, false, false)),
            Is.EqualTo((true, false)));
    }

    [Test]
    public void PushResistanceTakesOffByLevel()
    {
        // CEquipment::FUN_1411e0f90: 1 -> 0.5, 2 -> 1, 3 -> 1.5, 4 nothing, above 4 -> 2; never below 0
        Assert.That(new[] { 0, 1, 2, 3, 4, 5 }.Select(l => HitReaction.PushResist(3f, l)),
            Is.EqualTo(new[] { 3f, 2.5f, 2f, 1.5f, 3f, 1f }));
        Assert.That(HitReaction.PushResist(1f, 3), Is.EqualTo(0f));
    }

    [Test]
    public void PushGateStopsInTheClientsOrder()
    {
        var a = Attack(push: 1, priority: 2);
        var idle = new PushControl();
        Assert.That(HitReaction.PushGate(a, -10, HitReaction.StateIdle, false, false, idle, false), Is.EqualTo(1));
        Assert.That(HitReaction.PushGate(a, -10, HitReaction.StateIdle, false, false, idle, false, 0f), Is.EqualTo(0), "scale 0");
        Assert.That(HitReaction.PushGate(a, -10, HitReaction.StateIdle, false, true, idle, false), Is.EqualTo(0), "armor");
        Assert.That(HitReaction.PushGate(a, -10, HitReaction.StateDead, false, false, idle, false), Is.EqualTo(0), "dead");
        Assert.That(HitReaction.PushGate(a, 0, HitReaction.StateIdle, false, false, idle, false), Is.EqualTo(0), "no damage");
        Assert.That(HitReaction.PushGate(a, -10, HitReaction.StateIdle, true, false, idle, false), Is.EqualTo(0), "held");
        Assert.That(HitReaction.PushGate(null, -10, HitReaction.StateIdle, false, false, idle, false), Is.EqualTo(0), "no attack");
        Assert.That(HitReaction.PushGate(Attack(), -10, HitReaction.StateIdle, false, false, idle, false), Is.EqualTo(0), "no kind");
        var running = new PushControl { Dir = Vector3.UnitX, Priority = 2 };
        Assert.That(HitReaction.PushGate(a, -10, HitReaction.StateIdle, false, false, running, false), Is.EqualTo(0), "same priority runs");
        running.Priority = 1;
        Assert.That(HitReaction.PushGate(a, 10, HitReaction.StateIdle, false, false, running, false), Is.EqualTo(1),
            "a lower one gives way; a heal's positive total still pushes");
    }

    [Test]
    public void PushStartTakesItsDirectionFromTheKind()
    {
        var me = new Vector3(100, 0, 0);
        var recordPos = new Vector3(0, 0, 50);
        var p = new PushControl();
        var dir = Vector3.Zero;
        // 1: away from the record's position (unitized with z), distance pushdistance / 100
        Assert.That(p.Start(Attack(push: 1, distance: 300f, priority: 3), 1, me, recordPos, ref dir, 0f, null, 0), Is.True);
        var away = HitReaction.Unitize(me - recordPos);
        Assert.That((p.Dir, dir, p.Speed, p.Duration, p.Priority, p.Fall, p.Curve, p.Lift), Is.EqualTo((away, away, 3f, 0.5f, 3, 1, 1, false)));
        // 2: toward it, stopping 150 short of the attacker
        dir = Vector3.Zero;
        Assert.That(p.Start(Attack(push: 2, distance: 300f), 2, me, recordPos, ref dir, 0f, (new Vector3(300, 0, 0), Vector3.UnitY), 0), Is.True);
        Assert.That((p.Dir, p.Speed), Is.EqualTo((-away, 0.5f)));
        // 5: the record's direction, or the attacker's forward when it is zero
        dir = new Vector3(0, 2, 0);
        p.Start(Attack(push: 5, distance: 200f), 5, me, recordPos, ref dir, 0f, (Vector3.Zero, Vector3.UnitX), 0);
        Assert.That(p.Dir, Is.EqualTo(Vector3.UnitY));
        dir = Vector3.Zero;
        p.Start(Attack(push: 5, distance: 200f), 5, me, recordPos, ref dir, 0f, (Vector3.Zero, Vector3.UnitX), 0);
        Assert.That(p.Dir, Is.EqualTo(Vector3.UnitX));
        // 6: back along the hit facing (forward of rotation 90 is +x)
        dir = Vector3.Zero;
        p.Start(Attack(push: 6, distance: 200f), 6, me, recordPos, ref dir, 90f, null, 0);
        Assert.That(Vector3.Distance(p.Dir, -Vector3.UnitX), Is.LessThan(1e-6f));
        // 4: pulled to the record's position on the ground plane, the distance its length / 100
        dir = Vector3.Zero;
        p.Start(Attack(push: 4, distance: 0f), 4, me, recordPos, ref dir, 0f, null, 0);
        Assert.That((p.Dir, p.Speed), Is.EqualTo((-Vector3.UnitX, 1f)));
        // the resistance takes off before the scale; nothing starts at about 0, nor for kind 3
        dir = Vector3.Zero;
        p.Reset();
        Assert.That(p.Start(Attack(push: 1, distance: 100f), 1, me, recordPos, ref dir, 0f, null, 2), Is.False);
        Assert.That(p.Running, Is.False);
        Assert.That(p.Start(Attack(push: 3, distance: 300f), 3, me, recordPos, ref dir, 0f, null, 0), Is.False);
        Assert.That(p.Start(Attack(push: 1, distance: 300f), 1, me, recordPos, ref dir, 0f, null, 1, 0.5f), Is.True);
        Assert.That(p.Speed, Is.EqualTo(1.25f));
    }

    [Test]
    public void DecideEntersStateHitWithTheAttacksData()
    {
        var data = new StateHitData();
        var a = Attack(push: 1, distance: 300f, priority: 4);
        Assert.That(HitReaction.Decide(a, -5, HitReaction.StateIdle, true, false, 0, false, false, false, false, 1, false, data),
            Is.EqualTo(HitReaction.Change.Hit));
        Assert.That((data.SpeedRate, data.PauseTime, data.Duration, data.Distance, data.UpDistance, data.Down, data.Priority, data.Animation),
            Is.EqualTo((1.5f, 0.25f, 0.5f, 299.5f, 0f, 1, 4, "anim")), "the distance less the resistance (level 1: 0.5)");
        // no push: it takes the hit only when the state takes hits and hitImmuneBreak is above 0 (PC::vfunc_64 is 0)
        Assert.That(HitReaction.Decide(Attack(hitImmuneBreak: 1), -5, HitReaction.StateIdle, true, false, 0, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Hit));
        Assert.That(HitReaction.Decide(Attack(hitImmuneBreak: 1), -5, HitReaction.StateIdle, false, false, 0, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.None));
        Assert.That(HitReaction.Decide(Attack(), -5, HitReaction.StateIdle, true, false, 0, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.None));
        // a total of 0 or more without the grab target's push changes nothing
        Assert.That(HitReaction.Decide(a, 0, HitReaction.StateIdle, true, false, 0, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.None));
        // the super armor holds: a skill hit in state 0x2f still goes to StateIdle, a splash hit does not
        Assert.That(HitReaction.Decide(a, -5, HitReaction.State2f, true, false, 0, true, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Idle));
        Assert.That(HitReaction.Decide(a, -5, HitReaction.State2f, true, false, 0, true, false, false, false, 0, true, data),
            Is.EqualTo(HitReaction.Change.None));
        Assert.That(HitReaction.Decide(a, -5, HitReaction.StateIdle, true, false, 0, true, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.None));
        // grabbed or held: no reaction
        Assert.That(HitReaction.Decide(a, -5, HitReaction.StateIdle, true, false, 0, false, true, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.None));
        Assert.That(HitReaction.Decide(a, -5, HitReaction.StateIdle, true, false, 0, false, false, true, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.None));
    }

    [Test]
    public void StateHitWithADurationIgnoresAnImmunePushOfNoHigherPriority()
    {
        var data = new StateHitData();
        var a = Attack(push: 1, distance: 100f, priority: 2);
        a.PushPriorityHitImmune = 1;
        Assert.That(HitReaction.Decide(a, -5, HitReaction.StateHit, true, true, 2, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.None));
        Assert.That(HitReaction.Decide(a, -5, HitReaction.StateHit, true, true, 1, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Hit), "a lower priority gives way");
        Assert.That(HitReaction.Decide(a, -5, HitReaction.StateHit, true, false, 2, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Hit), "no duration");
        a.PushPriorityHitImmune = 0;
        Assert.That(HitReaction.Decide(a, -5, HitReaction.StateHit, true, true, 2, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Hit), "not immune");
    }

    [Test]
    public void GrabTargetPushesOnAPushOrGrabType2()
    {
        var data = new StateHitData();
        var push = Attack(push: 1, distance: 100f);
        var heal = 5L;   // a total not below 0: only the grab target's push goes on
        Assert.That(HitReaction.Decide(push, heal, HitReaction.StateGrabTarget, false, false, 0, true, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Idle), "the armor holds: StateIdle");
        Assert.That(HitReaction.Decide(push, heal, HitReaction.StateGrabTarget, false, false, 0, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Hit));
        var grab1 = Attack(push: 1); grab1.GrabTargetType = 1;
        Assert.That(HitReaction.Decide(grab1, heal, HitReaction.StateGrabTarget, false, false, 0, false, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.None));
        var grab2 = Attack(); grab2.GrabTargetType = 2;
        Assert.That(HitReaction.Decide(grab2, heal, HitReaction.StateGrabTarget, false, false, 0, true, false, false, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Idle));
        // outside StateGrabTarget, grabbed and held do not count there
        Assert.That(HitReaction.Decide(push, -5, HitReaction.StateGrabTarget, false, false, 0, false, true, true, false, 0, false, data),
            Is.EqualTo(HitReaction.Change.Hit));
    }

    [Test]
    public void ADotEntersStateHitOnlyWithoutArmor()
    {
        Assert.That(HitReaction.DotEntersHit(-5, 0, true, 1), Is.True);
        Assert.That((HitReaction.DotEntersHit(0, 0, true, 1), HitReaction.DotEntersHit(-5, 1, true, 1),
            HitReaction.DotEntersHit(-5, 0, false, 1), HitReaction.DotEntersHit(-5, 0, true, 0)), Is.EqualTo((false, false, false, false)));
    }

    [Test]
    public void OnSkillHitPushesFirstThenDecides()
    {
        var body = new Body { FieldPosition = new Vector3(100, 0, 0) };
        var data = new StateHitData();
        var a = Attack(push: 1, distance: 300f, priority: 1);
        Assert.That(HitReaction.OnSkillHit(body, a, -5, 0, false, false, 0, Vector3.Zero, Vector3.Zero, 0f, null, data),
            Is.EqualTo(HitReaction.Change.Hit));
        Assert.That((body.Push.Dir, body.Push.Speed, data.Distance), Is.EqualTo((Vector3.UnitX, 3f, 300f)));
        // armor 4 holds the reaction but not the push (nPush 1)
        body.Push.Reset();
        Assert.That(HitReaction.OnSkillHit(body, a, -5, 4, false, false, 0, Vector3.Zero, Vector3.Zero, 0f, null, data),
            Is.EqualTo(HitReaction.Change.None));
        Assert.That(body.Push.Running, Is.True);
        // the invincible time holds both
        body.Push.Reset();
        Assert.That(HitReaction.OnSkillHit(body, a, -5, 0, true, false, 0, Vector3.Zero, Vector3.Zero, 0f, null, data),
            Is.EqualTo(HitReaction.Change.None));
        Assert.That(body.Push.Running, Is.False);
        // a cast's StateSkill takes the hit once the push runs with a speed (StateSkill::vfunc_14 after vfunc_107)
        body.ClientStateId = HitReaction.StateSkill;
        var immune = Attack(hitImmuneBreak: 1);
        Assert.That(HitReaction.OnSkillHit(body, immune, -5, 0, false, false, 0, Vector3.Zero, Vector3.Zero, 0f, null, data),
            Is.EqualTo(HitReaction.Change.None));
        body.Push.Speed = 1f;
        Assert.That(HitReaction.OnSkillHit(body, immune, -5, 0, false, false, 0, Vector3.Zero, Vector3.Zero, 0f, null, data),
            Is.EqualTo(HitReaction.Change.Hit));
        Assert.That(HitReaction.OnSkillHit(body, null, -5, 0, false, false, 0, Vector3.Zero, Vector3.Zero, 0f, null, data),
            Is.EqualTo(HitReaction.Change.None));
    }

    [Test]
    public void AHitAllocatesNothing()
    {
        var body = new Body { FieldPosition = new Vector3(100, 0, 0) };
        var data = new StateHitData();
        var a = Attack(push: 1, distance: 300f, priority: 1);
        HitReaction.OnSkillHit(body, a, -5, 0, false, false, 0, Vector3.Zero, Vector3.Zero, 0f, (Vector3.Zero, Vector3.UnitX), data);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            body.Push.Reset();
            data.Clear();
            HitReaction.OnSkillHit(body, a, -5, 0, false, false, 0, Vector3.Zero, Vector3.Zero, 0f, (Vector3.Zero, Vector3.UnitX), data);
        }
        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
    }
}
