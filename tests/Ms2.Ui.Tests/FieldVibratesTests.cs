using System.Numerics;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The vibrate objects (WorldService m_5e8 and m_468, CEntityVibrator) and the tag pass's vibrate loop.</summary>
public class FieldVibratesTests
{
    static FieldEntity Entity(string id, params (string Key, string Value)[] props)
    {
        var flat = new FlatModel { Name = "ry_in_prop_safedoor_A01_" };
        flat.Mixins.Add("MS2Vibrate");
        flat.Props["Enabled"] = "True";
        flat.Props["ShapeDimensions"] = "150, 150, 150";
        var raw = new XEntity { Id = id, ModelName = flat.Name, Name = id };
        raw.Props["Position"] = "0, -100, 0";
        raw.Props["SoundMaterial"] = "7";
        raw.Props["brokenDefence"] = "50";
        raw.Props["brokenTick"] = "4000";
        raw.Props["resetTimer"] = "10000";
        raw.Props["frequency"] = "2";
        raw.Props["amp"] = "10";
        raw.Props["duration"] = "1";
        raw.Props["dampingstarttime"] = "0.5";
        foreach (var (k, v) in props) raw.Props[k] = v;
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    sealed class Log
    {
        public readonly List<string> Lines = new();
        public float Random = 0.5f;
        public FieldVibrates Field(params FieldEntity[] e)
        {
            var v = new FieldVibrates { Random01 = () => Random, RandomBit = () => 1 };
            v.CollisionSet = (x, on) => Lines.Add($"collide {x.Name} {on}");
            v.Faded = (x, show, s) => Lines.Add($"fade {x.Name} {show} {s}");
            v.VisibleSet = (x, show) => Lines.Add($"visible {x.Name} {show}");
            v.EffectPlayed = (x, p) => Lines.Add($"effect {x.Name} '{p}'");
            v.AttachField(e, 0);
            return v;
        }
    }

    static Vibrate Cast(string name, int serial, byte mode = 0, byte flag = 0) =>
        new(mode, name, 1, 70000065, 1, 0, 0, default, serial, "", "", flag);

    [Test]
    public void OnlyEnabledSizedObjectsRegisterKeyedByTheirLowercaseId()
    {
        var v = new Log().Field(Entity("AB12"), Entity("off", ("Enabled", "False")), Entity("flat", ("ShapeDimensions", "0, 0, 0")));
        Assert.That(v.Count, Is.EqualTo(1));
        Assert.That(v.Find("ab12")?.HitSound, Is.EqualTo(7));
        Assert.That(v.Find("AB12"), Is.Null, "the lookup is the name as sent, against the lowercase keys");
    }

    [Test]
    public void AnOffenceAboveTheDefenceBreaksTheObjectUntilItsReturn()
    {
        var log = new Log();
        var v = log.Field(Entity("door"));
        Assert.That(v.Apply(Cast("door", 1000), 99, 1000), Is.EqualTo(FieldVibrates.Outcome.Broke));
        Assert.That(log.Lines, Is.EqualTo(new[] { "collide door False", "effect door ''" }));
        log.Lines.Clear();
        Assert.That(v.Apply(Cast("door", 1100), 99, 1100), Is.EqualTo(FieldVibrates.Outcome.StillBroken));
        v.Update(5000);
        Assert.That(log.Lines, Is.Empty, "not past start + brokenTick");
        v.Update(5001);
        Assert.That(log.Lines, Is.EqualTo(new[] { "fade door False 0.1" }), "hidden after brokenTick when resetTimer is set");
        log.Lines.Clear();
        v.Update(15001);
        Assert.That(log.Lines, Is.EqualTo(new[] { "fade door True 0" }), "back after brokenTick + resetTimer, dropping in");
        log.Lines.Clear();
        v.Update(15501);
        Assert.That(log.Lines, Is.EqualTo(new[] { "collide door True" }), "the drop's 500 ms done: the collision back");
        Assert.That(v.Running, Is.Zero);
    }

    [Test]
    public void AnOffenceAtTheDefenceShakesAndComesBackToRest()
    {
        // the phase a quarter turn: the first offset is positive, a sign change from the start's, so a direction is drawn
        var log = new Log { Random = 0.25f };
        var v = log.Field(Entity("door"));
        var e = v.Find("door");
        Assert.That(v.Apply(Cast("door", 1000), 50, 2000), Is.EqualTo(FieldVibrates.Outcome.Shook));
        Assert.That(log.Lines, Is.EqualTo(new[] { "effect door ''" }), "no break: the collision stays");
        v.Update(2100);
        Assert.That(e.Offset, Is.Not.EqualTo(Vector3.Zero));
        Assert.That(e.Offset.Z, Is.Zero, "the shake is flat");
        v.Update(3000);
        Assert.That(e.Offset, Is.EqualTo(Vector3.Zero));
        Assert.That(v.Running, Is.Zero);
    }

    [Test]
    public void NoSkillRowDoesNothing()
    {
        var log = new Log();
        var v = log.Field(Entity("door"));
        Assert.That(v.Apply(Cast("door", 1000), null, 1000), Is.EqualTo(FieldVibrates.Outcome.NoSkill));
        Assert.That(v.Apply(Cast("nothing", 1000), 99, 1000), Is.EqualTo(FieldVibrates.Outcome.NotFound));
        Assert.That(log.Lines, Is.Empty);
    }

    [Test]
    public void HideDropsTheObjectUntilShowLetsItReturn()
    {
        var log = new Log();
        var v = log.Field(Entity("door"));
        Assert.That(v.Hide("door", 1000), Is.EqualTo(FieldVibrates.Outcome.Hidden));
        Assert.That(log.Lines, Is.EqualTo(new[] { "collide door False", "fade door False 0.1" }));
        log.Lines.Clear();
        v.Update(60000);
        Assert.That(log.Lines, Is.EqualTo(new[] { "fade door False 0.1" }), "past brokenTick the hide runs again; held: no return");
        log.Lines.Clear();
        Assert.That(v.Show("door"), Is.EqualTo(FieldVibrates.Outcome.Shown));
        v.Update(60001);
        Assert.That(log.Lines, Is.EqualTo(new[] { "fade door True 0" }));
        Assert.That(v.Show("other"), Is.EqualTo(FieldVibrates.Outcome.NotFound));
    }

    [Test]
    public void PacketsBeforeTheFieldsEntitiesWaitForThem()
    {
        var log = new Log();
        var v = log.Field();
        v.Clear();
        Assert.That(v.Apply(Cast("door", 1000), 99, 1000), Is.EqualTo(FieldVibrates.Outcome.Held));
        Assert.That(v.AttachField(new[] { Entity("door") }, 1000), Is.EqualTo(1));
        Assert.That(log.Lines, Is.EqualTo(new[] { "collide door False", "effect door ''" }));
        Assert.That(v.Holding, Is.False);
    }

    [Test]
    public void ModeTwoSetsTheVisibility()
    {
        var log = new Log();
        var v = log.Field(Entity("door"));
        Assert.That(v.Apply(Cast("door", 0, mode: 2, flag: 2), 0, 0), Is.EqualTo(FieldVibrates.Outcome.Visibility));
        Assert.That(v.Apply(Cast("door", 0, mode: 2, flag: 1), 0, 0), Is.EqualTo(FieldVibrates.Outcome.Visibility));
        Assert.That(log.Lines, Is.EqualTo(new[] { "visible door False", "visible door True" }));
    }

    static SkillHitShape Box(Vector3 at) =>
        SkillHitShape.Pose(new SkillRangeShape { Shape = 1, Width = 400, Distance = 400, Height = 400 }, 0, at, Vector3.Zero);

    [Test]
    public void AnAreaSendsVibrateOncePerRecordAndSkipsABrokenObject()
    {
        var v = new Log().Field(Entity("door"), Entity("far", ("Position", "5000, 5000, 0")));
        var pass = new TombTagPass();
        var hits = new List<VibrateHit>();
        var tag = new TagRecord { BrokenOffence = 99, Id = new TagIdentity(9001, 10200001, 3, 2, 1, 4) };
        Assert.That(pass.RunVibrates(v, Vector3.One, hits), Is.Zero, "no area: no pass");
        pass.Register(tag, Box(Vector3.Zero));
        Assert.That(pass.RunVibrates(v, new Vector3(1, 2, 3), hits), Is.EqualTo(1));
        Assert.That(hits[0], Is.EqualTo(new VibrateHit(new VibrateRequest("door", 9001, 10200001, 3, 2, 1, 4, new Vector3(1, 2, 3)), 7, new Vector3(0, -100, 0))));
        Assert.That(pass.RunVibrates(v, Vector3.One, hits), Is.Zero, "the record already entered it");
        pass.Register(new TagRecord { BrokenOffence = 99, Id = new TagIdentity(9002, 10200001, 3, 2, 1) }, Box(Vector3.Zero));
        v.Apply(Cast("door", 0), 99, 0);
        Assert.That(pass.RunVibrates(v, Vector3.One, hits), Is.Zero, "a broken object is skipped");
    }

    [Test]
    public void TheRequestWritesTheVibrateInfo()
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        new VibrateRequest("ab", 9001, 10200001, 3, 2, 1, 4, new Vector3(1, 2, 3)).Write(w);
        var r = new Maple2.PacketLib.Tools.ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That((r.ReadString(), r.ReadLong(), r.ReadInt(), r.ReadShort(), r.ReadByte(), r.ReadByte(), r.ReadInt()),
            Is.EqualTo(("ab", 9001L, 10200001, (short)3, (byte)2, (byte)1, 4)));
        Assert.That((r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.Available), Is.EqualTo((1f, 2f, 3f, 0)));
    }
}
