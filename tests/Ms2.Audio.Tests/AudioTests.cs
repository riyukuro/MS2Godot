using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

public class AudioTests
{
    static string SoundDir()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        var dir = Path.Combine(data, "Sound");
        if (!File.Exists(Path.Combine(dir, "MS2Sound.fev"))) Assert.Ignore("no Sound/MS2Sound.fev under MS2_DATA");
        return dir;
    }

    // MPEG-1 layer II / III frame walker: counts frames and returns the playing time, so a payload that is not a clean
    // MPEG audio stream fails. (The client's banks hold layer II.)
    static (int frames, double seconds, int rate, int channels) WalkMpeg(byte[] b)
    {
        int[] l3 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
        int[] l2 = { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0 };
        int[] sr = { 44100, 48000, 32000, 0 };
        int p = 0, frames = 0, rate = 0, ch = 0;
        if (b.Length > 10 && b[0] == 'I' && b[1] == 'D' && b[2] == '3') p = 10 + ((b[6] << 21) | (b[7] << 14) | (b[8] << 7) | b[9]);
        while (p + 4 <= b.Length)
        {
            if (b[p] != 0xff || (b[p + 1] & 0xf8) != 0xf8 || ((b[p + 1] >> 3) & 3) != 3) break;   // sync, MPEG-1
            int layer = 4 - ((b[p + 1] >> 1) & 3);   // 2 or 3
            if (layer < 2) break;
            int bitrate = (layer == 2 ? l2 : l3)[b[p + 2] >> 4] * 1000;
            rate = sr[(b[p + 2] >> 2) & 3];
            int pad = (b[p + 2] >> 1) & 1;
            ch = (b[p + 3] >> 6) == 3 ? 1 : 2;
            if (bitrate == 0 || rate == 0) break;
            p += 144 * bitrate / rate + pad;
            frames++;
        }
        return (frames, frames * 1152.0 / Math.Max(rate, 1), rate, ch);
    }

    [Test]
    public void TriaBgmDecodesToPlayablePcm()
    {
        var lib = new SoundLibrary(SoundDir());
        Assert.That(lib.Fev.Banks, Does.Contain("MS2Sound_BGM"));
        Assert.That(lib.Fev.Defs.Count, Is.EqualTo(12281));
        // table/fielddata.xml: Tria (field 2000001) has <bgm id="35" param="5"/>
        var plan = lib.ResolveMusic(35);
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.Loop.Name, Is.EqualTo("BGM_Tria_01"));
        var w = WalkMpeg(plan.Loop.Mp3);
        Assert.That(w.frames, Is.GreaterThan(1000));
        Assert.That(w.rate, Is.EqualTo(plan.Loop.Frequency));
        Assert.That(w.channels, Is.EqualTo(2));
        Assert.That(w.seconds, Is.EqualTo(plan.Loop.Seconds).Within(1.0), "frame count matches the FSB's sample count");
        var pcm = MpegDecoder.Decode(plan.Loop);
        Assert.That(pcm.Rate, Is.EqualTo(44100));
        Assert.That(pcm.Channels, Is.EqualTo(2));
        Assert.That(pcm.Seconds, Is.EqualTo(plan.Loop.Seconds).Within(0.1), "decoded length matches the FSB sample count");
        int peak = pcm.Samples.Max(x => Math.Abs((int)x));
        Assert.That(peak, Is.InRange(3000, 32767), "decoded audio is neither silent nor clipped flat");
        TestContext.WriteLine($"decoded {pcm.Frames} frames, peak {peak}");
        TestContext.WriteLine($"cue 35 -> {plan.Loop.Name}: {plan.Loop.Mp3.Length} bytes, {w.frames} frames, {w.seconds:F1} s, loop {plan.Loop.LoopStartSample}..{plan.Loop.LoopEndSample}");
    }

    [Test]
    public void EveryFieldBgmCueResolves()
    {
        var lib = new SoundLibrary(SoundDir());
        int ok = 0;
        foreach (var cue in lib.Fev.CueIds)
        {
            var plan = lib.ResolveMusic(cue);
            if (plan == null) continue;
            Assert.That(WalkMpeg(plan.Loop.Mp3).frames, Is.GreaterThan(10), $"cue {cue}");
            ok++;
        }
        Assert.That(ok, Is.GreaterThan(100));
    }

    [Test]
    public void SomeCueHasAnIntro()
    {
        var lib = new SoundLibrary(SoundDir());
        var intro = lib.Fev.CueIds.Select(lib.ResolveMusic).FirstOrDefault(p => p?.Intro != null);
        Assert.That(intro, Is.Not.Null);
        TestContext.WriteLine($"cue {intro.Cue} -> {intro.Intro.Name} then {intro.Loop.Name}");
    }

    [Test]
    public void EventsResolveToBankSounds()
    {
        var lib = new SoundLibrary(SoundDir());
        var lvl = lib.ResolveEvent("10000000");
        Assert.That(lvl, Is.Not.Null);
        Assert.That(lvl.Name, Is.EqualTo("System_Levelup_02"));
        Assert.That(WalkMpeg(lvl.Mp3).frames, Is.GreaterThan(5));
        var amb = lib.ResolveEvent("11100001");
        Assert.That(amb.Name, Does.StartWith("AMB_Forest"));
        TestContext.WriteLine($"event 10000000 -> {lvl.Name} vol {lvl.Volume}; 11100001 -> {amb.Name} vol {amb.Volume}; events {lib.Fev.EventCount}");
    }

    [Test]
    public void FsbNamesAndLoops()
    {
        var fsb = new Fsb5(Path.Combine(SoundDir(), "MS2Sound_BGM.fsb"));
        Assert.That(fsb.Mode, Is.EqualTo(Fsb5.ModeMpeg));
        var s = fsb.Find("BGM_Tria_01");
        Assert.That(s, Is.Not.Null);
        Assert.That(s.Channels, Is.EqualTo(2));
        Assert.That(s.Frequency, Is.EqualTo(44100));
    }

    [Test]
    public void SystemSoundButtonClickIsAnEvent()
    {
        SoundDir(); // skips without game data
        var packs = new Ms2.Formats.Pack.PackSet(Environment.GetEnvironmentVariable("MS2_DATA"));
        var table = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml")));
        var lib = new SoundLibrary(SoundDir());
        var click = table.Resolve("button_click");
        Assert.That(click, Is.EqualTo("MS2Sound/Sound/system/02000002"));
        var clip = lib.ResolveEvent(click);
        Assert.That(clip.Name, Is.EqualTo("System_Button_click_01"));
        Assert.That(lib.ResolveEvent(table.Resolve("dialog_open")).Name, Does.StartWith("System_defaultDialog_open"));
        Assert.That(lib.ResolveEvent(table.Resolve("dialog_close")).Name, Does.StartWith("System_defaultDialog_close"));
        TestContext.WriteLine($"button_click -> {clip.Name} volume {clip.Volume:F2}");
    }

    [Test]
    public void FootstepsResolveToCharacterEvents()
    {
        SoundDir(); // skips without game data
        var packs = new Ms2.Formats.Pack.PackSet(Environment.GetEnvironmentVariable("MS2_DATA"));
        var lib = new SoundLibrary(SoundDir());
        var table = new GroundEffectTable(p => { try { return System.Text.Encoding.UTF8.GetString(packs.Read("Xml", p)); } catch { return null; } });
        foreach (var ground in new[] { "default", "grass", "rock" })
            foreach (var action in new[] { "walk", "run0", "run1", "land" })
            {
                var evs = table.Pick(ground, action, new Random(1));
                var names = evs.Select(e => lib.ResolveEvent(e)?.Name ?? "?").ToList();
                TestContext.WriteLine($"{ground}/{action}: {string.Join(", ", evs)} -> {string.Join(", ", names)}");
                Assert.That(names, Does.Not.Contain("?"));
            }
        Assert.That(table.Pick("grass", "run0", new Random(1)), Is.Not.Empty);
    }

    [Test]
    public void AmbientEventsCarryDistances()
    {
        var lib = new SoundLibrary(SoundDir());
        var c = lib.ResolveEvent("MS2Sound/Sound/AMB/11100003");
        Assert.That(c, Is.Not.Null);
        TestContext.WriteLine($"AMB 11100003 -> {c.Name} volume {c.Volume:F2} min {c.MinDistance} max {c.MaxDistance}");
        Assert.That(c.MaxDistance, Is.GreaterThan(c.MinDistance));
        Assert.That(c.MinDistance, Is.GreaterThan(0));
    }

    [Test]
    public void EffectAndNpcSoundEventsResolve()
    {
        var lib = new SoundLibrary(SoundDir());
        Assert.That(lib.ResolveEvent("MS2Sound/Sound/Skill/HeavyGunner/02200017"), Is.Not.Null, "effect soundEvent");
        Assert.That(lib.ResolveEvent("ko/Npc/01000022"), Is.Not.Null, "npcSoundStart");
    }
}
