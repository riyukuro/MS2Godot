using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// The owner filters of an effect part (CEffectParam::vfunc_7 @0x14119e940, effectservice.cpp:245-259) and the soundEvent
/// part's job event and start delay (CSoundEventEffectParam::FUN_141776120 / FUN_1417762a0, soundeventmgr.cpp:378-405).
/// The Wizard's Flame Tornado cast effect (skill 10300011, e0 of its strTagEffects) has the fire sound for everyone and two
/// onlyCasterMyPC voices, one per gender, each byJob with the Wizard's (job code 30) own voice line.
/// </summary>
public class EffectSoundOwnerTests
{
    const string FireTornadoCast = "CH/Skill/Wizard/Eff_Wizard_FireTornado_Cast_01.xml";

    static EffectPart Sound(string ev) => new() { Type = "soundEvent", SoundEvent = ev, StartDelay = 0.25f };

    [Test]
    public void TheOwnerFiltersSkipAsTheClientDoes()
    {
        var me = new EffectOwner(EffectOwner.KindMyPc, 1, 30, true);
        Assert.That(new EffectPart().SkippedFor(me), Is.False, "no filter");
        Assert.That(new EffectPart { Gender = 0 }.SkippedFor(me), Is.True, "another gender");
        Assert.That(new EffectPart { Gender = 1 }.SkippedFor(me), Is.False);
        Assert.That(new EffectPart { JobCode = 40 }.SkippedFor(me), Is.True, "another job");
        Assert.That(new EffectPart { JobCode = 30 }.SkippedFor(me), Is.False);
        Assert.That(new EffectPart { OnlyCasterMyPC = true }.SkippedFor(me), Is.False);
        Assert.That(new EffectPart { OnlyCasterMyPC = true }.SkippedFor(me with { Kind = 3 }), Is.True, "another caster");
        Assert.That(new EffectPart { OnlyTargetMyPC = true }.SkippedFor(me with { TargetIsMyPc = false }), Is.True);
        var unknown = EffectOwner.OfKind(2);
        Assert.That(new EffectPart { Gender = 0 }.SkippedFor(unknown), Is.False, "an unknown gender is not tested");
        Assert.That(new EffectPart { JobCode = 40 }.SkippedFor(unknown), Is.False, "an unknown job is not tested");
    }

    [Test]
    public void AByJobPartPlaysTheJobsEventAndDelay()
    {
        var plain = Sound("90300007");
        Assert.That(plain.SoundEventFor(30, false), Is.EqualTo("90300007"), "byJob off");
        Assert.That(plain.SoundStartDelayFor(30), Is.EqualTo(0.25f));
        var byJob = Sound("90300007");
        byJob.JobEvents = new() { [30] = "90120008" };
        byJob.JobStartDelays = new() { [30] = 0.5f };
        Assert.That(byJob.SoundEventFor(30, false), Is.EqualTo("90120008"), "the job's event");
        Assert.That(byJob.SoundStartDelayFor(30), Is.EqualTo(0.5f), "the job's delay");
        Assert.That(byJob.SoundEventFor(40, false), Is.Empty, "a job without an entry plays nothing");
        Assert.That(byJob.SoundStartDelayFor(40), Is.EqualTo(0.25f));
        Assert.That(byJob.SoundEventFor(0, false), Is.EqualTo("90300007"), "job code 0: the plain event");
        Assert.That(byJob.SoundEventFor(30, true), Is.EqualTo("90300007"), "disableJobSound: the plain event");
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheFlameTornadoCastPlaysTheFireAndTheCastersJobVoiceOnly(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore(env + " is not set");
        using var packs = new PackSet(d);
        var parts = new EffectTable(packs).Get(FireTornadoCast)?.Where(p => p.Type == "soundEvent").ToList();
        Assert.That(parts, Is.Not.Null);
        Assert.That(parts, Has.Count.EqualTo(3));

        string[] Played(EffectOwner o, bool disableJobSound = false) =>
            parts.Where(p => !p.SkippedFor(o)).Select(p => p.SoundEventFor(o.JobCode, disableJobSound)).Where(e => e.Length > 0).ToArray();

        var femaleWizard = new EffectOwner(EffectOwner.KindMyPc, 1, 30, true);
        Assert.That(Played(femaleWizard), Is.EqualTo(new[] { "03200005", "91120008" }), "the fire and the female Wizard's voice");
        Assert.That(Played(femaleWizard with { Gender = 0 }), Is.EqualTo(new[] { "03200005", "90120008" }), "the male Wizard's voice");
        Assert.That(Played(femaleWizard, true), Is.EqualTo(new[] { "03200005", "91300007" }), "disableJobSound: the plain voice");
        Assert.That(Played(femaleWizard with { Kind = 3, TargetIsMyPc = false }), Is.EqualTo(new[] { "03200005" }),
            "another PC's cast: no voice (onlyCasterMyPC)");
    }
}
