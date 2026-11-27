using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>An effect's 2D event plays at its own volume wherever it is: CSoundEventEffect sets its position and starts it with
/// no range check (CSoundEventEffect::vfunc_4 @0x141777370, soundeventmgr.cpp:597-610), and FMOD neither attenuates nor pans a
/// 2D event, so the quietest voice choice sees its gain at any distance (<see cref="SoundClip.AudibilityAt"/>).</summary>
public class TwoDEventSoundTests
{
    [Test]
    public void TwoDEventIsHeardAtItsGainAnywhere()
    {
        var twoD = new SoundClip { Event = new FevEvent { Is3D = false } };
        foreach (float d in new[] { 0f, 299f, 2999f, 3000f, 20000f })
            Assert.That(twoD.AudibilityAt(0.8f, d), Is.EqualTo(0.8f), $"2D at {d}");
        var noEvent = new SoundClip();   // no event: the inverse distance from the default 300 / 3000 range
        Assert.That(noEvent.AudibilityAt(0.8f, 600), Is.EqualTo(0.4f).Within(1e-6f));
        Assert.That(noEvent.AudibilityAt(0.8f, 3000), Is.Zero);
    }

    /// <summary>The effects' soundEvent parts that resolve to a 2D event: none has a curve (so the voice takes the 2D branch),
    /// and the quest aura's start sound is one, heard at its gain 5000 units away. The library has the voice language the
    /// game uses for the data's region (GameOptions.Voice: Korean for KMS, English for GMS2, which ships only the English
    /// voice banks); with GMS2's voice parts resolved the census grows past the Korean-language count.</summary>
    [TestCase("MS2_DATA", 0)]
    [TestCase("MS2_GMS2_DATA", 2)]
    public void EffectTwoDEventsHaveNoCurve(string env, int voice)
    {
        var data = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore($"{env} is not set");
        var packs = AudioData.Packs(data);
        var lib = new SoundLibrary(Path.Combine(data, "Sound"), voice);
        var effects = new EffectTable(packs);
        var parts = packs.List("Xml").Where(e => e.StartsWith("effect/", StringComparison.Ordinal) && e.EndsWith(".xml", StringComparison.Ordinal))
            .SelectMany(e => effects.Get(e["effect/".Length..]) ?? [])
            .Where(p => p.Type == "soundEvent" && !string.IsNullOrEmpty(p.SoundEvent))
            .Select(p => p.SoundGroup == null ? p.SoundEvent : p.SoundGroup + "/" + p.SoundEvent).ToList();
        var (resolved, twoD, withCurve) = Census(lib, parts);
        TestContext.Out.WriteLine($"{env} ({lib.LanguageName}): {parts.Count} soundEvent parts, {resolved} resolved, {twoD} to a 2D event, {withCurve} of them with a curve");
        Assert.That(twoD, Is.GreaterThan(100));
        Assert.That(withCurve, Is.Zero);
        if (voice != 0)
        {
            int korean = Census(new SoundLibrary(Path.Combine(data, "Sound")), parts).Resolved;
            TestContext.Out.WriteLine($"{env} (Korea): {korean} resolved");
            Assert.That(resolved, Is.GreaterThan(korean), "the region's voice language resolves the voice parts");
        }
        var aura = effects.Get("ch/quest/eff_quest_fullaura_gray_start.xml").First(p => p.Type == "soundEvent");
        var auraClip = lib.ResolveEvent(aura.SoundGroup == null ? aura.SoundEvent : aura.SoundGroup + "/" + aura.SoundEvent);
        Assert.That(auraClip.Is2D, Is.True);
        Assert.That(auraClip.AudibilityAt(1, 5000), Is.EqualTo(1f));
    }

    static (int Resolved, int TwoD, int WithCurve) Census(SoundLibrary lib, List<string> events)
    {
        int resolved = 0, twoD = 0, withCurve = 0;
        foreach (var ev in events)
        {
            var clip = lib.ResolveEvent(ev);
            if (clip == null) continue;
            resolved++;
            if (!clip.Is2D) continue;
            twoD++;
            if (clip.Rolloff != null) withCurve++;
        }
        return (resolved, twoD, withCurve);
    }
}
