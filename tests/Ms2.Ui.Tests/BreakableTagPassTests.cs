using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The named sensors' loop of WorldService::FUN_141bc3560 (worldservice_1.cpp:9262-9316) and the field markers'
/// SetState (CBreakableObject @0x140ade420).</summary>
public class BreakableTagPassTests
{
    // the MS2BreakableNIF / MS2BreakableActor flats: Enabled True, reactionArrow True
    static FieldEntity Entity(string id, params (string Key, string Value)[] props) => Make(id, false, props);
    static FieldEntity Actor(string id, params (string Key, string Value)[] props) => Make(id, true, props);
    static FieldEntity At(string id, string pos) => Make(id, false, new[] { ("Position", pos) });

    static FieldEntity Make(string id, bool actor, (string Key, string Value)[] props)
    {
        var flat = new FlatModel { Name = actor ? "MS2BreakableActor" : "MS2BreakableNIF" };
        flat.Mixins.Add("MS2Breakable");
        flat.Mixins.Add(actor ? "Actor" : "Mesh");
        flat.Props["Enabled"] = "True";
        flat.Props["reactionArrow"] = "True";
        var raw = new XEntity { Id = id, ModelName = flat.Name, Name = id };
        raw.Props["Position"] = "0, -100, 0";
        raw.Props["ShapeDimensions"] = "100, 100, 150";
        raw.Props["SoundMaterial"] = "7";
        foreach (var (k, v) in props) raw.Props[k] = v;
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static BreakableObjects Field(params FieldEntity[] e)
    {
        var b = new BreakableObjects { Pick = n => n - 1 };
        b.AttachField(e);
        return b;
    }

    // a 200 x 200 x 200 box area at the origin facing -y
    static SkillHitShape Box(Vector3 at) =>
        SkillHitShape.Pose(new SkillRangeShape { Shape = 1, Width = 200, Distance = 200, Height = 200 }, 0, at, Vector3.Zero);

    static TagRecord Tag(long cast = 9001) => new() { BrokenOffence = 1, Id = new TagIdentity(cast, 10200001, 3, 2, 1) };

    static void Set(BreakableObjects b, string name, int state) =>
        b.Apply(new BreakableUpdate(1, new[] { new BreakableEntry(name, (byte)state, 0, 0, 0) }));

    [Test]
    public void TheFlatPropertiesMakeTheMarker()
    {
        var b = Field(Actor("Box1", ("InitialSequence", "Idle_A"), ("reactionSequenceName", "Dead_A,Dead_B"),
            ("reactionEffectXml", "Eff_Break"), ("SkillPauseControllFeedback", "True")), Entity("nif", ("Enabled", "False")));
        var m = b.Find("box1");
        Assert.That((m.Hittable, m.ReactionArrow, m.SkillPauseFeedback, m.HitSound, m.HasAnim, m.IdleAnim, m.DeadEffect),
            Is.EqualTo((true, true, true, 7, true, "Idle_A", "Eff_Break")));
        Assert.That(m.ReviveAnims, Is.EqualTo(new[] { "Dead_A", "Dead_B" }));
        Assert.That((m.State, m.Play, m.PlayForce), Is.EqualTo((2, "Idle_A", true)), "the attach's SetState(2): the idle, forced");
        var nif = b.Find("nif");
        Assert.That((nif.Hittable, nif.HasAnim, nif.Play), Is.EqualTo((false, false, (string)null)), "Enabled False; a NIF plays nothing");
        var box = m.Box();
        Assert.That((box.Extents, box.Center), Is.EqualTo((new Vector3(50, 50, 75), new Vector3(0, -100, 75))));
    }

    [Test]
    public void AnAreaHitsAMarkerOnceSendsBreakableAndStartsTheHitTimer()
    {
        var b = Field(Entity("box1"));
        var m = b.Find("box1");
        var pass = new TombTagPass();
        var hits = new List<BreakableHit>();
        var tag = Tag();

        Assert.That(pass.RunMarkers(b, hits), Is.EqualTo(0), "no area: no pass");
        pass.Register(tag, Box(Vector3.Zero));
        Assert.That(pass.RunMarkers(b, hits), Is.EqualTo(1));
        Assert.That(hits[0], Is.EqualTo(new BreakableHit(new BreakableRequest("box1", 9001, 10200001, 3, 2, 1), 7, new Vector3(0, -100, 0))));
        Assert.That(tag.Entered, Does.Contain(m.EntityId));
        Assert.That(pass.Count, Is.EqualTo(1), "the markers' loop leaves the areas to the tombs' loop");
        Assert.That(pass.Run(ReadOnlySpan<TombTarget>.Empty, new List<TombHit>()), Is.EqualTo(0));
        Assert.That(pass.Count, Is.EqualTo(0), "the tombs' loop empties the area list");

        pass.Register(tag, Box(Vector3.Zero));
        Assert.That(pass.RunMarkers(b, hits), Is.EqualTo(0), "the record already entered this marker");
        pass.Register(Tag(9002), Box(Vector3.Zero));
        Assert.That(pass.RunMarkers(b, hits), Is.EqualTo(0), "a new record: the hit timer (1000 ms) still runs");
        b.Update(999);
        Assert.That(pass.RunMarkers(b, hits), Is.EqualTo(0));
        b.Update(1000);
        Assert.That(pass.RunMarkers(b, hits), Is.EqualTo(1), "the timer ran out");
        Assert.That(hits[0].Request.TagId, Is.EqualTo(9002));
    }

    [Test]
    public void BeforeAndAfterTheServersStates()
    {
        // old: a marker the server broke (3) or hid (4) is not idle and sends nothing; new: a regen (5) is shown but not
        // idle either: only the server's 2 makes it hittable again (FUN_140adf6e0 moves only a tomb)
        var b = Field(Entity("box1"));
        var pass = new TombTagPass();
        var hits = new List<BreakableHit>();
        int Run(long cast) { pass.Register(Tag(cast), Box(Vector3.Zero)); int n = pass.RunMarkers(b, hits); pass.Run(default, new()); return n; }
        foreach (var (state, want) in new[] { (3, 0), (4, 0), (5, 0), (6, 0), (2, 1) })
        {
            Set(b, "BOX1", state);
            Assert.That(Run(100 + state), Is.EqualTo(want), $"state {state}");
        }
    }

    [Test]
    public void NoArrowNotHittableNoEntityOrAMissSendNothing()
    {
        var hits = new List<BreakableHit>();
        int Run(BreakableObjects b)
        {
            var pass = new TombTagPass();
            pass.Register(Tag(), Box(Vector3.Zero));
            return pass.RunMarkers(b, hits);
        }
        Assert.That(Run(Field(Entity("a", ("reactionArrow", "False")))), Is.EqualTo(0), "no reactionArrow");
        Assert.That(Run(Field(Entity("a", ("ShapeDimensions", "0, 0, 0")))), Is.EqualTo(0), "a zero size is not hittable");
        Assert.That(Run(Field(At("a", "0, -1000, 0"))), Is.EqualTo(0), "out of the area");
        var named = new BreakableObjects();
        named.AddMarker("a", 2);
        Assert.That(Run(named), Is.EqualTo(0), "a marker without an entity");
        Assert.That(Run(Field(Entity("a"))), Is.EqualTo(1));
    }

    [Test]
    public void MarkersGoInNameOrderAndOneRecordHitsEach()
    {
        var b = Field(Entity("zeta"), Entity("Alpha"), At("mid", "0, -1000, 0"));
        var pass = new TombTagPass();
        var hits = new List<BreakableHit>();
        var tag = Tag();
        pass.Register(tag, Box(Vector3.Zero));
        Assert.That(pass.RunMarkers(b, hits), Is.EqualTo(2));
        Assert.That(hits.Select(h => h.Request.Name), Is.EqualTo(new[] { "alpha", "zeta" }));
        Assert.That(tag.Entered.Count, Is.EqualTo(2));
    }

    [Test]
    public void TheTagsHitRunsOnlyForSkillPauseFeedback()
    {
        int Run(bool feedback)
        {
            var b = Field(Entity("a", ("SkillPauseControllFeedback", feedback ? "True" : "False")));
            var pass = new TombTagPass();
            int armed = 0;
            var tag = Tag();
            tag.Hit = () => armed++;
            pass.Register(tag, Box(Vector3.Zero));
            Assert.That(pass.RunMarkers(b, new List<BreakableHit>()), Is.EqualTo(1));
            return armed;
        }
        Assert.That(Run(false), Is.EqualTo(0));
        Assert.That(Run(true), Is.EqualTo(1));
    }

    [Test]
    public void TheStatesShowHidePlayAndFade()
    {
        var b = Field(Actor("a", ("InitialSequence", "Idle_A"), ("reactionSequenceName", "Dead_A,Dead_B"),
            ("reactionEffectXml", "Eff_Break")));
        var m = b.Find("a");
        m.Played();
        b.Update(5000);
        Set(b, "a", 3);
        Assert.That((m.Play, m.PlayForce, m.Effect, m.Shown, m.Visible), Is.EqualTo(("Dead_B", false, "Eff_Break", true, true)),
            "3: the dead effect and a random revive entry, not forced");
        m.Played(); m.EffectPlayed();
        Set(b, "a", 4);
        Assert.That((m.Play, m.Effect, m.Shown, m.Visible), Is.EqualTo(((string)null, (string)null, false, true)), "4: the fade starts");
        b.Update(5499);
        Assert.That(m.Visible, Is.True);
        b.Update(5500);
        Assert.That(m.Visible, Is.False, "the 500 ms fade ran out: IsVisible cleared");
        Set(b, "a", 5);
        Assert.That((m.Play, m.Shown, m.Visible), Is.EqualTo(((string)null, true, true)), "5: shown; a field marker has no regen list");
        Set(b, "a", 2);
        Assert.That((m.Play, m.PlayForce), Is.EqualTo(("Idle_A", true)));
    }

    [Test]
    public void WithNoAreaThePassAndTheClockAllocateNothing()
    {
        var b = Field(Enumerable.Range(0, 64).Select(i => Entity("m" + i)).ToArray());
        var pass = new TombTagPass();
        var hits = new List<BreakableHit>();
        pass.RunMarkers(b, hits);
        b.Update(1);
        long bytes = Alloc.Least(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                b.Update(i);
                pass.RunMarkers(b, hits);
            }
        });
        Assert.That(bytes, Is.EqualTo(0));
    }

    [Test]
    public void TheOwnCastsIdentityReachesTheRequest()
    {
        var paths = new MagicPathTable(System.Xml.Linq.XDocument.Parse("<ms2><type id=\"0\"><move delayTime=\"0\"/></type></ms2>"));
        var attack = new SkillAttackData { BrokenOffence = 1 };
        attack.Range.Shape = 1; attack.Range.Width = 200; attack.Range.Distance = 200; attack.Range.Height = 200;
        var m = new OwnTagMagics();
        var id = new TagIdentity(77, 10500001, 4, 1, 2);
        m.Add(null, attack, new List<SkillDamageTarget> { new(1, 2, 0, Array.Empty<SkillDamageLink>()) }, 0, paths, null, id);
        var pass = new TombTagPass();
        m.Tick(0, 0, new MagicOwner(Vector3.Zero, 0, true, false, false), pass);
        var hits = new List<BreakableHit>();
        Assert.That(pass.RunMarkers(Field(Entity("a")), hits), Is.EqualTo(1));
        Assert.That(hits[0].Request, Is.EqualTo(new BreakableRequest("a", 77, 10500001, 4, 1, 2)));
    }

    // the flats' defaults: MS2BreakableActor reactionArrow True, MS2BreakableNIF False (a NIF breakable takes the
    // enter pass, reactionEnter, worldservice_1.cpp:10712, not a tag area)
    [TestCase("02000088_bf", true)]
    [TestCase("02000180_bf", false)]
    public void TheClientsFieldBreakablesTakeTagHitsWhenActors(string field, bool any)
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new Ms2.Formats.Pack.PackSet(d);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), field);
        var b = new BreakableObjects();
        Assert.That(b.AttachField(map.Entities), Is.GreaterThan(0));
        var hittable = b.Sorted.Where(m => m.CanBeHit(0) && m.ReactionArrow).ToList();
        Assert.That(hittable.Count > 0, Is.EqualTo(any));
        Assert.That(hittable.All(m => m.Size.X > 0 && m.HasAnim), Is.True, "the field's Actor breakables");
        for (int i = 1; i < b.Sorted.Count; i++)
            Assert.That(string.CompareOrdinal(b.Sorted[i - 1].Name, b.Sorted[i].Name), Is.LessThan(0));
    }
}
