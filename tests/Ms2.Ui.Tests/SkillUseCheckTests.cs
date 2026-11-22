using System.Xml.Linq;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>MyPC::FUN_141525580 (mypc.cpp:2434), vfunc_102's refusals and ride off, over hand-made skill rows.</summary>
public class SkillUseCheckTests
{
    static List<XElement> Levels(XElement e, string name, string attr) => e.Elements(name).ToList();

    static CastRecord Record(string xml, long itemUid = 0, bool hasSequence = true, int key = 0x2c)
    {
        var d = SkillData.Load(XElement.Parse(xml), Levels);
        return new CastRecord { Skill = d, Level = d.GetLevelData(1), ItemUid = itemUid, HasSequence = hasSequence, Key = key };
    }

    const string Attack = "<skill id=\"100\"><basic mainType=\"0\"/><level value=\"1\"/></skill>";
    const string Mounted = "<skill id=\"101\"><basic mainType=\"1\"/><level value=\"1\"/></skill>";
    const string Unride = "<skill id=\"102\"><basic mainType=\"0\"><kinds unrideOnUse=\"1\" disableWater=\"1\"/></basic><level value=\"1\"/></skill>";
    const string Glide = "<skill id=\"103\"><basic mainType=\"7\"/><level value=\"1\"/></skill>";
    const string Held = "<skill id=\"104\"><basic mainType=\"4\"/><level value=\"1\"/></skill>";
    const string Emotion = "<skill id=\"105\"><basic mainType=\"14\"/><level value=\"2\"/><level value=\"1\"/></skill>";
    const string Needs = "<skill id=\"106\"><basic mainType=\"0\"><condition><requireEffect code=\"9001\"/><requireEffect code=\"9002\"/></condition></basic><level value=\"1\"/></skill>";

    [Test]
    public void KindsFlagsLoad()
    {
        var d = Record(Unride).Skill;
        Assert.That(d.UnrideOnUseRaw && d.DisableWater && d.IsUnrideOnUse, Is.True);
        var plain = Record(Attack).Skill;
        Assert.That(plain.UnrideOnUseRaw || plain.DisableWater || plain.IsUnrideOnUse, Is.False);
        // main types 1, 4, 7, 11-15, 17-19 ride off whatever the flag says (skilloverlapcompare.cpp:1060)
        Assert.That(Record(Mounted).Skill.IsUnrideOnUse && Record(Glide).Skill.IsUnrideOnUse, Is.True);
    }

    [Test]
    public void OrdinarySkillGoes()
    {
        Assert.That(SkillUseCheck.Check(Record(Attack), new UseCheckWorld { Riding = true, OnWater = true }, out var a),
            Is.EqualTo(SkillUseCheck.Result.Ok));
        Assert.That(a, Is.Null);
    }

    [TestCase(SkillUseCheck.StateGrabTarget)]
    [TestCase(SkillUseCheck.StatePlayInstrument)]
    public void StatesRefuse(int state)
    {
        Assert.That(SkillUseCheck.Check(Record(Attack), new UseCheckWorld { StateId = state }, out _), Is.EqualTo(SkillUseCheck.Result.Refused));
        Assert.That(SkillUseCheck.Check(Record(Attack), new UseCheckWorld { HoldBusy = true }, out _), Is.EqualTo(SkillUseCheck.Result.Refused));
    }

    [Test]
    public void MountedSkillRidesOffWithTheRecordsFlags()
    {
        var r = Record(Mounted, hasSequence: true, key: 0x2c);
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld { Riding = true }, out var a), Is.EqualTo(SkillUseCheck.Result.RideOff));
        var u = a as RideOffUseSkill;
        Assert.That(u, Is.Not.Null);
        Assert.That((u.Type, u.Arg, u.Value28, u.Flag2C, u.Value30), Is.EqualTo(((byte)1, (byte)0, 101, true, 0x2c)));
        // not riding: the same skill goes
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld(), out _), Is.EqualTo(SkillUseCheck.Result.Ok));
    }

    [Test]
    public void OnWaterRideOffIsPlain()
    {
        Assert.That(SkillUseCheck.Check(Record(Unride), new UseCheckWorld { Riding = true, OnWater = true }, out var a),
            Is.EqualTo(SkillUseCheck.Result.RideOff));
        Assert.That(a.GetType(), Is.EqualTo(typeof(RideOffAction)));
        Assert.That((a.Type, a.Arg), Is.EqualTo(((byte)0, (byte)0)));
    }

    [Test]
    public void ItemKindWithUidRidesOffWithTheGlideItem()
    {
        var r = Record(Glide, itemUid: 0x1234567890L, hasSequence: false, key: 7);
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld { Riding = true }, out var a), Is.EqualTo(SkillUseCheck.Result.RideOff));
        var g = a as RideOffUseGlideItem;
        Assert.That(g, Is.Not.Null);
        Assert.That((g.Type, g.Value28, g.Flag30, g.Value34), Is.EqualTo(((byte)25, 0x1234567890L, false, 7)));
        // no item uid: the skill form
        Assert.That(SkillUseCheck.Check(Record(Glide), new UseCheckWorld { Riding = true }, out var s), Is.EqualTo(SkillUseCheck.Result.RideOff));
        Assert.That(s, Is.InstanceOf<RideOffUseSkill>());
    }

    [Test]
    public void GlideUnderNoFlyRefuses()
    {
        Assert.That(SkillUseCheck.Check(Record(Glide), new UseCheckWorld { NoFly = true }, out _), Is.EqualTo(SkillUseCheck.Result.Refused));
        Assert.That(SkillUseCheck.Check(Record(Glide), new UseCheckWorld(), out _), Is.EqualTo(SkillUseCheck.Result.Ok));
    }

    [Test]
    public void HeldItemSkillMustMatch()
    {
        var r = Record(Held);
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld(), out _), Is.EqualTo(SkillUseCheck.Result.Refused));
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld { HasHeldItem = true, HeldItemSkill = 999 }, out _), Is.EqualTo(SkillUseCheck.Result.Refused));
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld { HasHeldItem = true, HeldItemSkill = 104, HeldFlag = false }, out _),
            Is.EqualTo(SkillUseCheck.Result.Refused));
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld { HasHeldItem = true, HeldItemSkill = 104 }, out _), Is.EqualTo(SkillUseCheck.Result.Ok));
    }

    [Test]
    public void EmotionMustBeLearnedAtTheLevel()
    {
        var d = SkillData.Load(XElement.Parse(Emotion), Levels);
        var r = new CastRecord { Skill = d, Level = d.GetLevelData(2) };
        (uint id, ushort level) asked = default;
        var w = new UseCheckWorld { EmotionLearned = (id, lv) => { asked = (id, lv); return lv == 1; } };
        Assert.That(SkillUseCheck.Check(r, w, out _), Is.EqualTo(SkillUseCheck.Result.Refused));
        Assert.That(asked, Is.EqualTo((105u, (ushort)2)));
        r.Level = d.GetLevelData(1);
        Assert.That(SkillUseCheck.Check(r, w, out _), Is.EqualTo(SkillUseCheck.Result.Ok));
    }

    [Test]
    public void DisableWaterRefusesOnWater()
    {
        var r = Record(Unride);
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld { OnWater = true }, out _), Is.EqualTo(SkillUseCheck.Result.Refused));
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld(), out _), Is.EqualTo(SkillUseCheck.Result.Ok));
    }

    [Test]
    public void EveryRequiredEffectMustBeEnabled()
    {
        var r = Record(Needs);
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld { HasEnabledEffect = id => id == 9001 }, out _), Is.EqualTo(SkillUseCheck.Result.Refused));
        Assert.That(SkillUseCheck.Check(r, new UseCheckWorld { HasEnabledEffect = id => id is 9001 or 9002 }, out _), Is.EqualTo(SkillUseCheck.Result.Ok));
    }
}
