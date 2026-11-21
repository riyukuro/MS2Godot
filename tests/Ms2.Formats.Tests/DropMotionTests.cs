using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>The drop table (CItemDataTable::LoadDropStyle / LoadDropItemAsset / LoadGrade*) and CDropItem's motion (dropitem.cpp), from the client's code.</summary>
public class DropMotionTests {
    const string Xml = @"<ms2>
  <dropStyle>
    <v typeID='1' scale='1.0' bias='55.0' shadowscale='100' shadowbias='5' dropani='0,0,1' dropanispeed='3' beani='0,1,0' beanispeed='0.2' getani='1,0,0' getanispeed='2' bounceheight='30' dropSoundID='09100001' getSoundID='09200001' />
    <v typeID='2' scale='1.5' bounceheight='0' bounceperiod='0' />
    <v typeID='11' bias='30' />
    <v typeID='12' bias='31' />
  </dropStyle>
  <dropItemAsset>
    <Item itemID='90000001'><v typeID='11' min='1' /><v typeID='12' min='20' /></Item>
  </dropItemAsset>
  <gradeNif id='0' nif='Item_Box_A01' /><gradeNif id='2' nif='Eff_Drop_Rare' />
  <gradePetNif id='2' nif='Eff_PetDrop_Rare' />
  <gradeEffect id='2' effect='Item/Eff_DropEffect_Rare.xml' />
</ms2>";

    static ItemDropTable Table() => new(XElement.Parse(Xml));

    [Test] public void TheStyleRowKeepsItsAttributes() {
        var s = Table().Style(1);
        Assert.That((s.RestHeight, s.Scale, s.ShadowScale, s.ShadowBias, s.DropAniSpeed, s.BeAniSpeed, s.GetAniSpeed, s.BounceHeight), Is.EqualTo((55f, 1f, 100f, 5f, 3f, 0.2f, 2f, 30f)));
        Assert.That(s.DropAni, Is.EqualTo(new[] { 0f, 0f, 1f })); Assert.That(s.BeAni, Is.EqualTo(new[] { 0f, 1f, 0f })); Assert.That(s.GetAni, Is.EqualTo(new[] { 1f, 0f, 0f }));
        Assert.That((s.DropSoundId, s.GetSoundId), Is.EqualTo(("09100001", "09200001")));
        var d = Table().Style(2);
        Assert.That((d.RestHeight, d.BouncePeriod, d.BounceHeight), Is.EqualTo((50f, 0f, 0f)), "an absent bias keeps 50; a written 0 stays 0");
        Assert.That(Table().Style(99), Is.Null);
    }

    [Test] public void TheLastRowThatFitsTheAmountDecides() {
        var t = Table();
        Assert.That(t.ForItem(90000001, 1).TypeId, Is.EqualTo(11));
        Assert.That(t.ForItem(90000001, 19).TypeId, Is.EqualTo(11));
        Assert.That(t.ForItem(90000001, 20).TypeId, Is.EqualTo(12));
        Assert.That(t.ForItem(90000001, 0), Is.Null, "no row has a minimum of 0");
        Assert.That(t.ForItem(123, 5), Is.Null, "an item with no row");
    }

    [Test] public void TheLookFollowsInitDrop() {
        var t = Table();
        Assert.That(t.Look(90000002, 25, 0, false), Is.Not.Null.And.Property("IsMeso").True, "any meso id takes the row of 90000001");
        Assert.That(t.Look(90000001, 25, 0, false).Style.TypeId, Is.EqualTo(12));
        Assert.That(t.Look(90000001, 0, 0, false), Is.Null, "no row fits: no drop");
        var item = t.Look(20000001, 1, 2, false);
        Assert.That((item.Style.TypeId, item.Asset, item.IsMeso), Is.EqualTo((1, "Eff_Drop_Rare", false)));
        var pet = t.Look(20000001, 1, 2, true);
        Assert.That((pet.Style.TypeId, pet.Asset), Is.EqualTo((2, "Eff_PetDrop_Rare")));
        Assert.That(t.Look(0x55d4a89, 1, 0, false), Is.Null, "a special item with no row of its own");
    }

    [Test] public void TheGradeNames() {
        var t = Table();
        Assert.That((t.GradeNif(0), t.GradeNif(2), t.GradePetNif(2), t.GradeEffect(2)), Is.EqualTo(("Item_Box_A01", "Eff_Drop_Rare", "Eff_PetDrop_Rare", "Item/Eff_DropEffect_Rare.xml")));
        Assert.That((t.GradeNif(7), t.GradeEffect(0)), Is.EqualTo(("", "")));
    }

    [Test] public void TheRandomIsTheClientsGenerator() {
        var a = new DropRandom(5, 6, 7); var b = new DropRandom(5, 6, 7);
        for (int i = 0; i < 20; i++) Assert.That(a.Next(), Is.EqualTo(b.Next()));
        var c = new DropRandom(5, 6, 0x400);
        Assert.That(new DropRandom(5, 6, 7).Next(), Is.Not.EqualTo(c.Next()));
        // a hand run of Unk_Random's step: s1 = 0x100005, s2 = 0x1006, s3 = 0x17
        uint s1 = 5u | 0x100000, s2 = 6u | 0x1000, s3 = 7u | 0x10;
        s1 = (s1 & 0xfffffffe) << 12 ^ (s1 << 13 ^ s1) >> 19; s2 = (s2 & 0xfffffff8) << 4 ^ (s2 << 2 ^ s2) >> 25; s3 = (s3 & 0xfffffff0) << 17 ^ (s3 << 3 ^ s3) >> 11;
        Assert.That(new DropRandom(5, 6, 7).Next(), Is.EqualTo(s1 ^ s2 ^ s3));
    }

    [Test] public void TheArcLandsOnItsTarget() {
        var arc = new DropArc();
        var from = new Vector3(0, 0, 100); var to = new Vector3(60, 80, 20);
        arc.Solve(from, to, 100f, -20f);
        var end = arc.At(arc.FlightScale);
        Assert.That((end - to).Length(), Is.LessThan(0.5f));
        Assert.That(arc.At(0), Is.EqualTo(from));
    }

    [Test] public void ATargetTooFarRaisesTheSpeed() {
        var arc = new DropArc();
        arc.Solve(Vector3.Zero, new Vector3(1000, 0, 0), 10f, -20f);
        Assert.That(arc.Speed, Is.GreaterThan(10f));
        var end = arc.At(arc.FlightScale);
        Assert.That((end - new Vector3(1000, 0, 0)).Length(), Is.LessThan(1f));
    }

    static DropConstants K() => new() { TargetZ = 100, MaxLength = 300, MoneyMaxLength = 400, PickUpVel = 400, PickUpGravity = -200, RotateTime = 0.3f, RotateVel = 1, FailTime = 0.5f, FailHeight = 40 };
    static DropStyle S() => Table().Style(1);

    static DropMotion Launched(out Vector3 rest, Func<Vector3, float?> ground = null, bool fixedThrow = false) {
        var m = new DropMotion(K(), S(), 12345, false);
        m.Launch(DropMotion.Created70(new Vector3(100, 200, 50)), DropLaunch.Default, fixedThrow, false, ground ?? (_ => 0f));
        rest = m.Arc.Target;
        return m;
    }

    [Test] public void AHeldTrapDropIsPickableOnlyAfterTheDelayPlusTheFlight() {
        // a drop of kind 0xc80 with the trap flag stays unthrown until the delay ends, then flies, then rests
        const uint delay = 3000, created = 1000;
        uint fade = DropPickup.FadeUntil(created, 0xc80, false, true, delay);
        var m = new DropMotion(K(), S(), 12345, false);
        var p = new PickupPlanner();
        bool PickableAt(uint now) {
            p.Clear();
            p.Candidates.Add(new PickupCandidate { ObjectId = 1, State = m.State, FadeUntil = fade, Distance = 10, Owner = true });
            p.Plan(now, 0, 100, 300, 0x80);
            return p.Nearest == 0;
        }
        Assert.That(DropPickup.StillHidden(created + delay - 1, fade), Is.True);
        Assert.That(DropPickup.StillHidden(created + delay, fade), Is.False);
        Assert.That(m.State, Is.EqualTo(DropMotion.Created));
        Assert.That(PickableAt(created + delay - 1), Is.False, "held, hidden");
        Assert.That(PickableAt(created + delay + 50), Is.False, "state still 6: the delay is over but nothing was thrown");
        m.Launch(DropMotion.Created70(new Vector3(100, 200, 50)), DropLaunch.Default, false, false, _ => 0f);
        uint now = created + delay;
        for (int i = 0; i < 600 && m.State == DropMotion.FlyOut; i++) {
            m.Advance(0.016f, null); now += 16;
            if (m.State == DropMotion.FlyOut) Assert.That(PickableAt(now), Is.False, "in the flight the held key sends nothing");
        }
        Assert.That(m.State, Is.EqualTo(DropMotion.Rest));
        Assert.That(now, Is.GreaterThan(created + delay + 100), "the flight takes time");
        Assert.That(PickableAt(now), Is.True, "only the resting drop is requested");
    }

    [Test] public void AFixedThrowLandsOneUnitOffOnEachAxis() {
        Launched(out var thrown, fixedThrow: true);
        Launched(out var sameSpot, fixedThrow: true);
        Assert.That(thrown, Is.EqualTo(sameSpot));
        Assert.That(thrown.X, Is.EqualTo(101f).Within(0.01f), "NiPoint3::ALL_ONES is the offset (dropitem.cpp:143)");
        Assert.That(thrown.Y, Is.EqualTo(201f).Within(0.01f));
    }

    [Test] public void ADropFliesOutThenRestsAtTheGroundPlusItsHeight() {
        var m = Launched(out var rest);
        Assert.That(m.State, Is.EqualTo(DropMotion.FlyOut));
        Assert.That(rest.Z, Is.EqualTo(55f).Within(0.01f), "ground 0 + bias 55");
        for (int i = 0; i < 600 && m.State == DropMotion.FlyOut; i++) m.Advance(0.016f, null);
        Assert.That(m.State, Is.EqualTo(DropMotion.Rest));
        Assert.That((m.Position - rest).Length(), Is.LessThan(0.01f));
    }

    [Test] public void TheSameObjectLandsInTheSamePlace() {
        Launched(out var a); Launched(out var b);
        Assert.That(a, Is.EqualTo(b));
        var m = new DropMotion(K(), S(), 999, false);
        m.Launch(DropMotion.Created70(new Vector3(100, 200, 50)), DropLaunch.Default, false, false, _ => 0f);
        Assert.That(m.Arc.Target, Is.Not.EqualTo(a), "another object id seeds another throw");
    }

    [Test] public void NoGroundBelowLeavesItWhereItIs() {
        var m = new DropMotion(K(), S(), 1, false);
        m.Launch(DropMotion.Created70(new Vector3(0, 0, 0)), DropLaunch.Default, false, false, _ => null);
        Assert.That(m.State, Is.EqualTo(DropMotion.Rest));
    }

    [Test] public void ARestingDropBobsOnItsPeriod() {
        var m = Launched(out var rest);
        for (int i = 0; i < 600 && m.State == DropMotion.FlyOut; i++) m.Advance(0.016f, null);
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 200; i++) { m.Advance(0.01f, null); lo = Math.Min(lo, m.Position.Z); hi = Math.Max(hi, m.Position.Z); }
        Assert.That(hi - lo, Is.GreaterThan(10f).And.LessThanOrEqualTo(30f + 0.01f), "bounceheight 30 x 0.5 each way");
        Assert.That(m.Position.X, Is.EqualTo(rest.X)); 
    }

    [Test] public void APickupFliesToThePickerHoversAndSinks() {
        var m = Launched(out _);
        for (int i = 0; i < 600 && m.State == DropMotion.FlyOut; i++) m.Advance(0.016f, null);
        var picker = new Vector3(110, 210, 0);
        m.PickedUp(picker, 3);
        Assert.That(m.State, Is.EqualTo(DropMotion.FlyToPicker));
        var seen = new List<int> { m.State };
        for (int i = 0; i < 2000 && !m.Done; i++) { m.Advance(0.016f, picker); if (seen[^1] != m.State) seen.Add(m.State); }
        Assert.That(seen, Is.EqualTo(new[] { DropMotion.FlyToPicker, DropMotion.Hover, DropMotion.Sink }));
        Assert.That(m.Done, Is.True);
    }

    [Test] public void APickerWhoLeavesTakesTheDropOut() {
        var m = Launched(out _);
        for (int i = 0; i < 600 && m.State == DropMotion.FlyOut; i++) m.Advance(0.016f, null);
        m.PickedUp(new Vector3(110, 210, 0), 0);
        m.Advance(0.016f, null);
        Assert.That(m.Done, Is.True);
    }

    [Test] public void OnlyARestingDropTakesAPickup() {
        var m = Launched(out _);
        m.PickedUp(new Vector3(1, 2, 3), 0);
        Assert.That(m.State, Is.EqualTo(DropMotion.FlyOut), "still flying out: the pickup is ignored");
    }

    [Test] public void ARefusedPickupHopsBackToRest() {
        var m = Launched(out var rest);
        for (int i = 0; i < 600 && m.State == DropMotion.FlyOut; i++) m.Advance(0.016f, null);
        m.PickFailed();
        Assert.That(m.State, Is.EqualTo(DropMotion.Regrow));
        float top = float.MinValue;
        for (int i = 0; i < 100 && m.State == DropMotion.Regrow; i++) { m.Advance(0.01f, null); top = Math.Max(top, m.Position.Z); }
        Assert.That(m.State, Is.EqualTo(DropMotion.Rest));
        Assert.That(top - rest.Z, Is.GreaterThan(30f), "the hop is FailHeight 40 high at its top");
    }

    [Test] public void AMesoThrownByAPlayerLandsBehindIt() {
        var m = new DropMotion(K(), Table().Style(11), 5, false);
        m.Launch(DropMotion.Created70(new Vector3(0, 0, 0)), new DropLaunch { Speed = 100, SourceForward = new Vector3(0, 1, 0) }, false, true, _ => 0f);
        Assert.That(m.Arc.Target.Y, Is.LessThan(0f), "50 behind the source's facing");
    }
}
