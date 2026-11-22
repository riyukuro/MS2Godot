using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The hit effects of a mode 1 skill damage (CSceneStateGame::FUN_14171cdf0, FUN_14145e1a0's aim, CMagicService::FUN_14146b440
/// and FUN_14146c990's throttle) and the throttle every caller shares. Client space: z up.
/// </summary>
public class DamageHitEffectTests
{
    const int OwnerId = 5, TargetId = 9, Self = 3;
    static readonly Func<int> RandMax = () => 32767;   // FUN_14109ace0 = 1: the height times 1.2

    static SkillAttackDamage Attack(string hit = "Hit.xml", string remain = "Remain.xml", int method = 0) =>
        new() { EffectHit = hit, EffectRemain = remain, AttackMethodType = method };

    static SkillDamage Damage(int hits = 1, params int[] targets)
    {
        var d = new SkillDamage { Mode = 1, OwnerId = OwnerId, Position = new Vector3S(10, 20, 30), DirectionShort = new Vector3S(1, 0, 0) };
        foreach (var t in targets.Length == 0 ? new[] { TargetId } : targets)
        {
            var h = new Hit[hits];
            for (int i = 0; i < hits; i++) h[i] = new Hit(0, 100);
            d.Targets.Add(new HitTarget(t, h, d.Position, default));
        }
        return d;
    }

    // the owner (another PC) at the origin facing rot 0 (forward (0, -1, 0)), its box 150 high; the npc target at x 100, 100 high
    static readonly Dictionary<int, DamageHitObject> Field = new()
    {
        [OwnerId] = new DamageHitObject(Vector3.Zero, 0f, new Vector3(0, 0, 75), 3),
        [TargetId] = new DamageHitObject(new Vector3(100, 0, 0), 0f, new Vector3(0, 0, 50), 5),
        [Self] = new DamageHitObject(new Vector3(0, 300, 0), 0f, new Vector3(0, 0, 75), 1),
    };
    static bool Find(int id, out DamageHitObject o) => Field.TryGetValue(id, out o);

    static List<DamageHitEffect> Run(SkillDamage d, SkillAttackDamage a, HitEffectThrottle t, uint now = 5000, int direction = 3,
        float moveDistance = 0f)
    {
        var into = new List<DamageHitEffect>();
        DamageHitEffects.Records(d, a, direction, moveDistance, Find, t, now, RandMax, into);
        return into;
    }

    [Test]
    public void AHitPlaysTheEffectHitAtTheAimedPointAndTheRemainOnTheTarget()
    {
        var fx = Run(Damage(), Attack(), new HitEffectThrottle());
        Assert.That(fx, Has.Count.EqualTo(2));
        // the smaller extent (50) times 1.2 above the target's position; direction 3 without movedistance: the owner's forward
        Assert.That(fx[0].File, Is.EqualTo("Hit.xml"));
        Assert.That(fx[0].ActorId, Is.EqualTo(0));
        Assert.That(fx[0].At.X, Is.EqualTo(100f).Within(1e-3f));
        Assert.That(fx[0].At.Y, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(fx[0].At.Z, Is.EqualTo(60f).Within(1e-3f));
        OwnTagMagics.AlongAxes(new Vector3(0, -1, 0), out var ax, out var ay, out var az);
        Assert.That((fx[0].AxisX, fx[0].AxisY, fx[0].AxisZ), Is.EqualTo((ax, ay, az)), "the transform's axes along the aimed direction");
        Assert.That(fx[0].CasterKind, Is.EqualTo(3), "another PC's");
        Assert.That(fx[1].File, Is.EqualTo("Remain.xml"));
        Assert.That(fx[1].ActorId, Is.EqualTo(TargetId), "SetActor(target)");
        Assert.That(fx[1].CasterKind, Is.EqualTo(3), "kReq is reused");
    }

    [Test]
    public void TheLocalOwnerIsKind1AndAnNpcOwnerKind5()
    {
        var d = Damage();
        d.OwnerId = Self;
        Assert.That(Run(d, Attack(), new HitEffectThrottle())[0].CasterKind, Is.EqualTo(1));
        d.OwnerId = TargetId;
        d.Targets[0] = new HitTarget(OwnerId, d.Targets[0].Hits, d.Position, default);
        Assert.That(Run(d, Attack(), new HitEffectThrottle())[0].CasterKind, Is.EqualTo(5),
            "CNpc::vfunc_58: an npc-owned hit is kind 5, which SoundMusicEffectState rejects");
    }

    [Test]
    public void TheObjectKindIsVfunc58s()
    {
        Assert.That(DamageHitEffects.KindOf(true, HitKind.Pc, false), Is.EqualTo(1), "MyPC");
        Assert.That(DamageHitEffects.KindOf(false, HitKind.Pc, false), Is.EqualTo(3), "another PC");
        Assert.That(DamageHitEffects.KindOf(false, HitKind.Npc, false), Is.EqualTo(5), "an npc");
        Assert.That(DamageHitEffects.KindOf(false, HitKind.Npc, true), Is.EqualTo(6), "a boss");
    }

    [Test]
    public void AnNpcsExtentTurnsItsCollisionOffset()
    {
        Assert.That(DamageHitEffects.NpcExtent(Vector3.Zero, 90f, 40f), Is.EqualTo(new Vector3(0, 0, 40)), "no offset: GameObject's");
        var e = DamageHitEffects.NpcExtent(new Vector3(10, 0, 30), 0f, 40f);
        Assert.That(e.X, Is.EqualTo(-10f).Within(1e-3f), "rotZ 0: turned by -180");
        Assert.That(e.Y, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(e.Z, Is.EqualTo(70f), "the height offset plus half the box");
        e = DamageHitEffects.NpcExtent(new Vector3(10, 0, 0), 270f, 0f);
        Assert.That(e.X, Is.EqualTo(0f).Within(1e-3f));
        Assert.That(e.Y, Is.EqualTo(10f).Within(1e-3f), "rotZ 270: counter-clockwise by 90");
        var target = new DamageHitObject(new Vector3(100, 0, 0), 0f, new Vector3(-10, 5, 70), 5);
        var pos = Vector3.Zero;
        var dir = Vector3.Zero;
        DamageHitEffects.Aim(target, false, Field[OwnerId], 3, 0f, RandMax, ref pos, ref dir);
        Assert.That(pos, Is.EqualTo(new Vector3(90, 5, 70)), "no owner: the whole vf37 vector");
    }

    [Test]
    public void AttackMethodType1KeepsThePacketsPointAndDirection()
    {
        var fx = Run(Damage(), Attack(method: 1), new HitEffectThrottle());
        Assert.That(fx[0].At, Is.EqualTo(new Vector3(10, 20, 30)));
        OwnTagMagics.AlongAxes(new Vector3(1, 0, 0), out var ax, out var ay, out var az);
        Assert.That((fx[0].AxisX, fx[0].AxisY, fx[0].AxisZ), Is.EqualTo((ax, ay, az)));
    }

    [Test]
    public void TheAimsDirectionFollowsTheAttacksDirection()
    {
        var target = Field[TargetId];
        var owner = Field[OwnerId];
        Vector3 Aim(int direction, float moveDistance, bool hasOwner = true)
        {
            var pos = new Vector3(10, 20, 30);
            var dir = new Vector3(0, 0, 2);
            DamageHitEffects.Aim(target, hasOwner, owner, direction, moveDistance, RandMax, ref pos, ref dir);
            return dir;
        }
        Assert.That(Aim(3, 0f), Is.EqualTo(new Vector3(0, -1, 0)), "3: the owner's forward");
        Assert.That(Aim(3, 50f), Is.EqualTo(new Vector3(0, 0, 1)), "3 with movedistance: the packet's, unit");
        Assert.That(Aim(4, 0f), Is.EqualTo(new Vector3(0, 1, 0)), "4: negated");
        Assert.That(Aim(4, 50f), Is.EqualTo(new Vector3(0, 0, -1)));
        Assert.That(Aim(1, 0f), Is.EqualTo(new Vector3(0, -1, 0)), "1: (target - owner) x up");
        Assert.That(Aim(2, 0f), Is.EqualTo(new Vector3(0, 1, 0)), "2: its negation");
        Assert.That(Aim(0, 0f), Is.EqualTo(new Vector3(0, -1, 0)), "else the forward");
        Assert.That(Aim(1, 0f, hasOwner: false), Is.EqualTo(new Vector3(0, 0, 2)), "no owner: the direction stays");
        var pos = Vector3.Zero;
        var d = Vector3.Zero;
        DamageHitEffects.Aim(target, false, owner, 3, 0f, RandMax, ref pos, ref d);
        Assert.That(pos, Is.EqualTo(new Vector3(100, 0, 50)), "no owner: the target's position plus its extent");
        DamageHitEffects.Aim(target, true, owner, 3, 0f, () => 0, ref pos, ref d);
        Assert.That(pos.Z, Is.EqualTo(40f).Within(1e-3f), "the rand draw at -1: the height times 0.8");
    }

    [Test]
    public void AnEmptyEffectHitStillGivesTheRemainAndNoHitsGiveNothing()
    {
        var fx = Run(Damage(), Attack(hit: ""), new HitEffectThrottle());
        Assert.That(fx, Has.Count.EqualTo(1));
        Assert.That(fx[0].File, Is.EqualTo("Remain.xml"));
        Assert.That(Run(Damage(hits: 0), Attack(), new HitEffectThrottle()), Is.Empty, "a record without hits");
        Assert.That(Run(Damage(1, 77), Attack(), new HitEffectThrottle()), Is.Empty, "a target not in the field");
        var d = Damage();
        d.OwnerId = 77;
        var gone = Run(d, Attack(), new HitEffectThrottle());
        Assert.That(gone, Has.Count.EqualTo(2), "an owner not in the field still plays (the pair tests only the hit object)");
        Assert.That(gone[0].CasterKind, Is.EqualTo(0), "the caster kind of no object");
        Assert.That(gone[0].At, Is.EqualTo(new Vector3(100, 0, 50)), "the aim without the owner: the target's position plus its extent");
        Assert.That(Run(Damage(), null, new HitEffectThrottle()), Is.Empty, "no attack damage (Init fails)");
        d = Damage();
        d.Mode = 0;
        Assert.That(Run(d, Attack(), new HitEffectThrottle()), Is.Empty);
    }

    [Test]
    public void TheThrottleIsTheTargetsAndSharedWithTheMagics()
    {
        var t = new HitEffectThrottle();
        Assert.That(Run(Damage(), Attack(), t), Has.Count.EqualTo(2));
        var again = Run(Damage(), Attack(), t, now: 5100);
        Assert.That(again, Has.Count.EqualTo(1), "within the object gap only the remain plays");
        Assert.That(again[0].File, Is.EqualTo("Remain.xml"));
        // an own magic's bounce-off on the same target reads the same state
        Assert.That(t.TryPlay(TargetId, "Other.xml", 5100), Is.False);
        Assert.That(Run(Damage(), Attack(), t, now: 5300), Has.Count.EqualTo(2), "past both gaps");
    }

    [Test]
    public void TheThrottleGoesWithTheObjectAndTheField()
    {
        var t = new HitEffectThrottle();
        Run(Damage(1, TargetId, OwnerId), Attack(), t);
        Assert.That(t.Count, Is.EqualTo(2));
        t.Remove(TargetId);
        Assert.That(t.Count, Is.EqualTo(1));
        Assert.That(Run(Damage(), Attack(), t, now: 5100), Has.Count.EqualTo(2), "a removed object starts over");
        t.Clear();
        Assert.That(t.Count, Is.EqualTo(0));
    }

    [Test]
    public void EveryRemoteOwnersMagicsShareTheThrottle()
    {
        var m = new RemoteTagMagics();
        m.SkillEntered(OwnerId);
        var t = new HitEffectThrottle();
        m.HitEffects = t;
        m.SkillEntered(OwnerId + 1);
        Assert.That(m.MagicsOf(OwnerId).HitEffects, Is.SameAs(t), "an owner made before");
        Assert.That(m.MagicsOf(OwnerId + 1).HitEffects, Is.SameAs(t), "an owner made after");
        var own = new OwnTagMagics { HitEffects = t };
        Assert.That(own.HitEffects, Is.SameAs(m.MagicsOf(OwnerId).HitEffects));
    }

    static SkillDamage Splash(int mode, int hits = 1, params int[] targets)
    {
        var d = new SkillDamage { Mode = (byte)mode, OwnerId = mode == 5 ? OwnerId : 0 };
        foreach (var t in targets.Length == 0 ? new[] { TargetId } : targets)
            d.Targets.Add(new HitTarget(t, new Hit[hits], new Vector3S(1, 2, 3), new Vector3(0, 1, 0)));
        return d;
    }

    static List<DamageHitEffect> RunSplash(SkillDamage d, SkillAttackDamage a, HitEffectThrottle t, uint now = 5000)
    {
        var into = new List<DamageHitEffect>();
        DamageHitEffects.SplashRecords(d, a, Find, t, now, into);
        return into;
    }

    [Test]
    public void ASplashPlaysAtTheHitObjectWithoutRotationAndTheRemainOnIt()
    {
        var fx = RunSplash(Splash(6), Attack(), new HitEffectThrottle());
        Assert.That(fx, Has.Count.EqualTo(2));
        Assert.That(fx[0], Is.EqualTo(new DamageHitEffect("Hit.xml", 0, new Vector3(100, 0, 0), Vector3.UnitX, Vector3.UnitY,
            Vector3.UnitZ, 0)), "the hit object's position with the unit axes; mode 6 has no owner: kind 0");
        Assert.That(fx[1].File, Is.EqualTo("Remain.xml"));
        Assert.That(fx[1].ActorId, Is.EqualTo(TargetId));
        Assert.That(RunSplash(Splash(6, hits: 0), Attack(), new HitEffectThrottle()), Has.Count.EqualTo(2),
            "bc80 does not read the hits");
        Assert.That(RunSplash(Splash(6, 1, 77), Attack(), new HitEffectThrottle()), Is.Empty, "a hit object not in the field");
        Assert.That(RunSplash(Splash(6), null, new HitEffectThrottle()), Is.Empty, "no attack damage");
        Assert.That(RunSplash(Splash(1), Attack(), new HitEffectThrottle()), Is.Empty, "not a splash");
    }

    static List<DamageHitEffect> RunTagged(SkillDamage d, params Vector3[] anchorsOfTarget)
    {
        var into = new List<DamageHitEffect>();
        DamageHitEffects.SplashRecords(d, Attack(), Find, new HitEffectThrottle(), 5000, into, (id, list) =>
        {
            list.Clear();
            if (id != TargetId) return false;
            list.AddRange(anchorsOfTarget);
            return true;
        }, new List<Vector3> { new(9, 9, 9) });
        return into;
    }

    [Test]
    public void ARegionSkillOnACollisionPositionNpcPlaysAtTheAnchorNearestTheOrigin()
    {
        // the record's origin is (1, 2, 3): bc80 reads +0x78, mode 5 sets it to the nearest m_arrE68 center (0x14171e3ba)
        var fx = RunTagged(Splash(5), new Vector3(50, 0, 0), new Vector3(1, 2, 13), new Vector3(1, 2, -7));
        Assert.That(fx[0].At, Is.EqualTo(new Vector3(1, 2, 13)), "the nearest anchor, the first of equals");
        Assert.That((fx[1].File, fx[1].ActorId), Is.EqualTo(("Remain.xml", TargetId)), "the remain stays on the npc");
        Assert.That(RunTagged(Splash(5, 1, Self))[0].At, Is.EqualTo(Field[Self].Position), "an untagged object: its position");
        var stop = RunTagged(Splash(5, 1, Self, TargetId, Self));
        Assert.That(stop, Has.Count.EqualTo(2), "a tagged npc without anchors ends the packet: only the first target played");
        Assert.That(stop[0].At, Is.EqualTo(Field[Self].Position));
    }

    [Test]
    public void ASplashOnACollisionPositionNpcPlaysAtTheWorldOrigin()
    {
        // CSplashDamageRecord::Init (0x14175b1c0) never fills +0x78: the ctor's Point3::ZERO (a client quirk)
        var fx = RunTagged(Splash(6), new Vector3(50, 0, 0));
        Assert.That(fx[0].At, Is.EqualTo(Vector3.Zero));
        Assert.That(RunTagged(Splash(6))[0].At, Is.EqualTo(Vector3.Zero), "mode 6 does not read the anchors");
        Assert.That(RunTagged(Splash(6, 1, Self))[0].At, Is.EqualTo(Field[Self].Position));
    }

    [Test]
    public void ANodeBoxCenterIsTheOffsetBoxMidHeightTurnedByTheNode()
    {
        // CNpc::BuildCollision's box (0 to vz high) moved by the offset, placed by vfunc_2 (rotation, scale 1)
        var c = DamageHitEffects.NodeBoxCenter(new Vector3(250, 250, 300), new Vector3(0, -75, -150),
            Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), new Vector3(10, 20, 30));
        Assert.That(Vector3.Distance(c, new Vector3(85, 20, 30)), Is.LessThan(1e-3f), $"got {c}");
        Assert.That(DamageHitEffects.NodeBoxCenter(new Vector3(1, 1, 40), Vector3.Zero, Quaternion.Identity, Vector3.One),
            Is.EqualTo(new Vector3(1, 1, 21)));
    }

    [Test]
    public void ARegionSkillsDamageTakesItsOwnersKindAndTheSharedThrottle()
    {
        var t = new HitEffectThrottle();
        var fx = RunSplash(Splash(5), Attack(), t);
        Assert.That(fx[0].CasterKind, Is.EqualTo(3), "the caster kind of the region skill's owner");
        Assert.That(fx[1].CasterKind, Is.EqualTo(3));
        var again = RunSplash(Splash(5), Attack(), t, now: 5100);
        Assert.That(again, Has.Count.EqualTo(1), "within the object gap only the remain plays");
        Assert.That(t.TryPlay(TargetId, "Other.xml", 5100), Is.False, "the magics' throttle state");
        var d = Splash(5);
        d.OwnerId = 77;
        Assert.That(RunSplash(d, Attack(), new HitEffectThrottle())[0].CasterKind, Is.EqualTo(0), "an owner gone: kind 0");
    }

    [Test]
    public void TheMotionsMoveDistanceIsRead()
    {
        var doc = XDocument.Parse("<ms2><skill id=\"100\"><basic mainType=\"1\"/><level value=\"1\">"
            + "<motion sequenceName=\"A\" movedistance=\"150\"><attack point=\"Hit_01\" direction=\"4\"/></motion>"
            + "<motion sequenceName=\"B\"><attack point=\"Hit_01\"/></motion></level></skill></ms2>");
        var s = new SkillDataTable(() => new[] { doc }, (e, n, _) => e.Elements(n).ToList()).GetSkillData(100);
        var motions = s.GetLevelData(1).Motions;
        Assert.That(motions[0].MoveDistance, Is.EqualTo(150f));
        Assert.That(motions[1].MoveDistance, Is.EqualTo(0f));
        Assert.That(motions[0].Attacks[0].Direction, Is.EqualTo(4));
        Assert.That(motions[1].Attacks[0].Direction, Is.EqualTo(3));
    }
}
