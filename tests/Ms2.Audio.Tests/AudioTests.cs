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

    static bool IsKr(Ms2.Formats.Pack.PackSet packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        return Ms2.Formats.Pack.TablePath.Region(e => packs.Exists("Xml", e)) == "kr";
    }

    static bool IsKr()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new Ms2.Formats.Pack.PackSet(data);
        return IsKr(packs);
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

    /// <summary>One library per (Data/Sound folder, voice language) for the process (CSoundService loads MS2Sound.fev once):
    /// a second field's open gets the same project and the banks the first one opened, without reading the file again.</summary>
    [Test]
    public void SharedLibraryIsLoadedOncePerFolder()
    {
        var dir = SoundDir();
        var a = SoundLibrary.Shared(dir);
        var plan = a.ResolveMusic(35);
        Assert.That(plan, Is.Not.Null);
        var b = SoundLibrary.Shared(dir);
        Assert.That(b, Is.SameAs(a));
        Assert.That(b.Fev, Is.SameAs(a.Fev));
        Assert.That(b.Bank("MS2Sound_BGM"), Is.SameAs(a.Bank("MS2Sound_BGM")));
        Assert.That(b.ResolveMusic(35).Loop.Name, Is.EqualTo(plan.Loop.Name));
        // A different voice language reuses the parsed FEV project while keeping its own language banks.
        var en = SoundLibrary.Shared(dir, voiceLanguage: 2);
        Assert.That(en, Is.Not.SameAs(a));
        Assert.That(en.Fev, Is.SameAs(a.Fev));
    }

    [Test]
    public void TriaBgmDecodesToPlayablePcm()
    {
        var lib = new SoundLibrary(SoundDir());
        Assert.That(lib.Fev.Banks, Does.Contain("MS2Sound_BGM"));
        // KMS Sound/MS2Sound.fev has 12281 sound defs; GMS2 Sound/MS2Sound.fev has 12046.
        Assert.That(lib.Fev.Defs.Count, Is.EqualTo(IsKr() ? 12281 : 12046));
        // table/fielddata.xml: Tria (field 2000001) has <bgm id="35" param="5"/>
        var plan = lib.ResolveMusic(35);
        Assert.That(plan, Is.Not.Null);
        Assert.That(plan.Loop.Name, Is.EqualTo("BGM_Tria_01"));
        Assert.That(plan.Bridge, Is.Not.Null);
        Assert.That(plan.Bridge.Name, Is.EqualTo("bridge"));
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
        TestContext.WriteLine($"cue 35 -> {plan.Loop.Name} <-> {plan.Bridge.Name}: {plan.Loop.Mp3.Length} bytes, {w.frames} frames, {w.seconds:F1} s, loop {plan.Loop.LoopStartSample}..{plan.Loop.LoopEndSample}");
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
    public void SystemShopOpenSoundKeyResolvesToAnEvent()
    {
        // ToggleShopTab plays s_strBuildOpenShop through PlaySystemSound (unit_141347b10.cpp:98, :491); the key is a row of
        // table/systemsound.xml, the table FieldAudio.PlayUi resolves against.
        SoundDir(); // skips without game data
        var packs = new Ms2.Formats.Pack.PackSet(Environment.GetEnvironmentVariable("MS2_DATA"));
        var table = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml")));
        var path = table.Resolve("System_BuildOpenShop");
        Assert.That(path, Is.Not.Null, "System_BuildOpenShop has no row (or no sound id) in systemsound.xml");
        Assert.That(new SoundLibrary(SoundDir()).ResolveEvent(path), Is.Not.Null);
        TestContext.WriteLine("System_BuildOpenShop -> " + path);
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
        // KMS ships Korea voice banks (voice 0); GMS2 ships EN voice banks (voice 2, NA region default).
        var lib = new SoundLibrary(SoundDir(), voiceLanguage: IsKr() ? 0 : 2);
        Assert.That(lib.ResolveEvent("MS2Sound/Sound/Skill/HeavyGunner/02200017"), Is.Not.Null, "effect soundEvent");
        Assert.That(lib.ResolveEvent("ko/Npc/01000022"), Is.Not.Null, "npcSoundStart");
    }

    // ---- music parameters, priorities, history and segment/bridge transitions ------------------------------------

    [Test]
    public void ToolHistoryAndMusicConditionsWorkWithoutGameData()
    {
        // TToolHistory<T> cursor + truncation on Add after Get(-1) (soundeventmgr.cpp:816-853)
        var h = new ToolHistory<int>();
        Assert.That(h.Get(0, out _), Is.False);
        h.Add(10);
        h.Add(20);
        h.Add(30);
        Assert.That(h.Get(0, out int v) && v == 30, Is.True);
        Assert.That(h.Get(-1, out v) && v == 20, Is.True);
        h.Add(25);   // drops 30 after cursor
        Assert.That(h.Count, Is.EqualTo(3));
        Assert.That(h.Get(0, out v) && v == 25, Is.True);
        Assert.That(h.Get(-1, out v) && v == 20, Is.True);

        // MusicCondition ops (comp/lnks/lnk /cond/cprm)
        var eq10 = new MusicCondition { Op = MusicCondition.OpEqual, ParamId = 5, Value1 = 10f };
        Assert.That(eq10.Matches(10f), Is.True);
        Assert.That(eq10.Matches(0f), Is.False);
        var inRange = new MusicCondition { Op = MusicCondition.OpInRange, ParamId = 4, Value1 = 1f, Value2 = 5f };
        Assert.That(inRange.Matches(3f), Is.True);
        Assert.That(inRange.Matches(6f), Is.False);

        // Client GetVoice() -> FMOD language name & FSB sample prefix (soundservice.cpp:418-433, 752-764)
        Assert.That(FevProject.VoiceLanguageName(0), Is.EqualTo("Korea"));
        Assert.That(FevProject.VoiceLanguageName(1), Is.EqualTo("jp"));
        Assert.That(FevProject.VoiceLanguageName(2), Is.EqualTo("en"));
        Assert.That(FevProject.VoiceLanguageName(3), Is.EqualTo("cn"));
        Assert.That(FevProject.VoiceSamplePrefix(0), Is.EqualTo(""));
        Assert.That(FevProject.VoiceSamplePrefix(1), Is.EqualTo("jp_"));
        Assert.That(FevProject.VoiceSamplePrefix(2), Is.EqualTo("en_"));
        Assert.That(FevProject.VoiceSamplePrefix(3), Is.EqualTo("cn_"));
    }

    [Test]
    public void FevParsesMusicParametersAndSegmentTopologies()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var fev = lib.Fev;
        // comp/prms defines 3 parameters: 3 ForceChange, 4 MapParameter, 5 MusicIntermission
        Assert.That(fev.MusicParameters.Select(p => (p.Id, p.Name)).ToList(), Is.EqualTo(new[]
        {
            (3, "ForceChange"),
            (4, "MapParameter"),
            (5, "MusicIntermission"),
        }));

        // Bridge cue: Tria (35) -> seg 84 (BGM_Tria_01) <-> seg 87 (bridge) via param 5 == 10 / == 0
        var tria = fev.GetMusicCue(35);
        Assert.That(tria, Is.Not.Null);
        Assert.That(tria.StartSegmentId, Is.EqualTo(84));
        Assert.That(tria.CrossfadeMs, Is.EqualTo(2000));
        var seg84 = fev.GetMusicSegment(84);
        Assert.That(seg84.OutgoingLinks.Count, Is.EqualTo(1));
        Assert.That(seg84.OutgoingLinks[0].TargetSegmentId, Is.EqualTo(87));
        Assert.That(seg84.OutgoingLinks[0].Condition.ParamId, Is.EqualTo(5));
        Assert.That(seg84.OutgoingLinks[0].Condition.Value1, Is.EqualTo(10f));
        var seg87 = fev.GetMusicSegment(87);
        Assert.That(seg87.OutgoingLinks[0].TargetSegmentId, Is.EqualTo(84));
        Assert.That(seg87.OutgoingLinks[0].Condition.ParamId, Is.EqualTo(5));
        Assert.That(seg87.OutgoingLinks[0].Condition.Value1, Is.EqualTo(0f));

        // Intro -> loop cue whose intro name does NOT contain "intro": Login (15) -> Start_01 -> Loop_01
        var login = lib.ResolveMusic(15);
        Assert.That(login.Intro, Is.Not.Null);
        Assert.That(login.Intro.Name, Is.EqualTo("BGM_Login_Reminisce_Start_01"));
        Assert.That(login.Loop.Name, Is.EqualTo("BGM_Login_Reminisce_Loop_01"));
        Assert.That(login.Bridge, Is.Null);

        // Silence-bridge lead-in -> loop: RuneBlader Island Silence (102) starts on bridge2, then loops main
        var silenceLead = lib.ResolveMusic(102);
        Assert.That(silenceLead.Intro, Is.Not.Null);
        Assert.That(silenceLead.Intro.Name, Is.EqualTo("bridge2"));
        Assert.That(silenceLead.Loop.Name, Is.EqualTo("BGM_RuneBlader_Island_01"));

        // One-shot cue: DDSTOP_BGM_DANCE_01 (112) has 0 outgoing links
        var oneShot = lib.ResolveMusic(112);
        Assert.That(oneShot.OneShot, Is.True);
        Assert.That(oneShot.Loop.Name, Is.EqualTo("DDStop_Loop_02"));
    }

    [Test]
    public void MusicBridgeTransitionsFollowParam5Callback()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);

        // Field enter for Tria: <bgm id="35" param="5"/> (value defaults to 0.0f)
        Assert.That(mc.PlayFieldBgm(new FieldBgmSpec(BgmId: 35, BgmParam: 5, BgmValue: 0f)), Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(35));
        Assert.That(mc.CurrentSegment.Id, Is.EqualTo(84));
        Assert.That(mc.TryGetMusicParameter(5, out float p5) && p5 == 0f, Is.True);

        // Segment 84 starts: OnMusicCallback toggles param 5 from 0 -> 10
        mc.OnSegmentStarted();
        Assert.That(mc.TryGetMusicParameter(5, out p5) && p5 == 10f, Is.True);

        // Segment 84 ends: link 84 -> 87 (bridge) matches param 5 == 10
        var next = mc.AdvanceSegment();
        Assert.That(next, Is.Not.Null);
        Assert.That(next.Id, Is.EqualTo(87));
        Assert.That(lib.ResolveSegment(next).Name, Is.EqualTo("bridge"));

        // Segment 87 (bridge) starts: OnMusicCallback toggles param 5 from 10 -> 0
        mc.OnSegmentStarted();
        Assert.That(mc.TryGetMusicParameter(5, out p5) && p5 == 0f, Is.True);

        // Segment 87 ends: link 87 -> 84 (BGM_Tria_01) matches param 5 == 0
        next = mc.AdvanceSegment();
        Assert.That(next, Is.Not.Null);
        Assert.That(next.Id, Is.EqualTo(84));
        Assert.That(lib.ResolveSegment(next).Name, Is.EqualTo("BGM_Tria_01"));
    }

    [Test]
    public void IntroLoopAndOneShotSegmentTransitions()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);

        // Intro -> unconditional loop (Boss01 cue 23: seg 44 Intro -> seg 104 Loop -> seg 104 Loop)
        Assert.That(mc.PlayMusic(23, 0f, MusicController.DefaultPriority, false), Is.True);
        Assert.That(lib.ResolveSegment(mc.CurrentSegment).Name, Is.EqualTo("BGM_Boss_01_Intro"));
        mc.OnSegmentStarted();   // param 5: 0 -> 10
        var loopSeg = mc.AdvanceSegment();
        Assert.That(loopSeg, Is.Not.Null);
        Assert.That(lib.ResolveSegment(loopSeg).Name, Is.EqualTo("BGM_Boss_01_Loop"));
        Assert.That(MusicController.IsUnconditionalSelfLoop(loopSeg), Is.True);
        mc.OnSegmentStarted();   // param 5: 10 -> 0
        var loopAgain = mc.AdvanceSegment();
        Assert.That(loopAgain.Id, Is.EqualTo(loopSeg.Id));   // stays on loop unconditionally

        // One-shot (cue 112): stops when segment ends
        Assert.That(mc.PlayMusic(112, 0f, MusicController.DefaultPriority, false), Is.True);
        Assert.That(mc.IsPlaying, Is.True);
        mc.OnSegmentStarted();
        Assert.That(mc.AdvanceSegment(), Is.Null);
        Assert.That(mc.IsPlaying, Is.False);
    }

    [Test]
    public void MusicParameterPrioritiesHistoryAndRollbackMatchClient()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);

        // PlayMusic history + pop on cueId == 0 (TriggerObjectSound off, triggeractioncli.cpp:579)
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.True);
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.False, "same cue ignored without force");
        Assert.That(mc.PlayMusic(35, 0f, 0x80, true), Is.True, "same cue allowed with force");
        Assert.That(mc.PlayMusic(23, 0f, 0x80, false), Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(23));
        // Higher priority (0x40 < 0x80) blocks lower priority (0x80) until force or pop at <= 0x40
        Assert.That(mc.PlayMusic(36, 0f, 0x40, false), Is.True);
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.False, "lower priority rejected");
        Assert.That(mc.PlayMusic(0, 0f, 0x80, false), Is.True, "pop at lower priority returns true without changing");
        Assert.That(mc.CurrentCueId, Is.EqualTo(36));
        Assert.That(mc.PlayMusic(0, 0f, 0x40, false), Is.True, "pop at equal priority restores previous cue");
        Assert.That(mc.CurrentCueId, Is.EqualTo(23));

        // SetMusicParameter: param 0 is a no-op; higher priority blocks OnSegmentStarted (priority 0x80)
        mc.ResetMusicState();
        Assert.That(mc.SetMusicParameter(0, 5f, 0x80, false), Is.True);
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.True);
        Assert.That(mc.SetMusicParameter(5, 0f, 0x40, false), Is.True);   // lock param 5 = 0 at higher priority
        mc.OnSegmentStarted();   // runs at 0x80 -> rejected by priority 0x40
        Assert.That(mc.TryGetMusicParameter(5, out float p5) && p5 == 0f, Is.True);
        Assert.That(mc.AdvanceSegment(), Is.Null, "param 5 locked at 0 prevents transition to bridge (needs 10)");

        // Value -1.0f pops previous parameter value
        mc.ResetMusicState();
        Assert.That(mc.SetMusicParameter(5, 0f, 0x80, false), Is.True);
        Assert.That(mc.SetMusicParameter(5, 10f, 0x80, false), Is.True);
        Assert.That(mc.SetMusicParameter(5, -1f, 0x80, false), Is.True);
        Assert.That(mc.TryGetMusicParameter(5, out p5) && p5 == 0f, Is.True);
    }

    [Test]
    public void BossCombatBgmSwitchesAndRestoresFieldBgm()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);
        // field 2000004: <bgm id="59" param="5" boss="23" bossParam="5"/>
        var spec = new FieldBgmSpec(BgmId: 59, BgmParam: 5, BgmValue: 0f, BossBgmId: 23, BossBgmParam: 5, BossBgmValue: 0f);
        Assert.That(mc.PlayFieldBgm(spec), Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(59));

        var tracker = new BossBgmTracker(mc, spec);
        tracker.AddBoss(1001);
        Assert.That(tracker.EnterBossRange(1001), Is.True);
        Assert.That(tracker.IsBossBgmActive, Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(23));

        // Moving out of bossSoundEndDistance restores field BGM on tick
        tracker.LeaveBossRange(1001);
        Assert.That(tracker.Tick(), Is.True);
        Assert.That(tracker.IsBossBgmActive, Is.False);
        Assert.That(mc.CurrentCueId, Is.EqualTo(59));

        // Re-entering and killing the boss also restores field BGM
        Assert.That(tracker.EnterBossRange(1001), Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(23));
        Assert.That(tracker.RemoveBoss(1001), Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(59));
    }

    // ---- voice banks per language --------------------------------------------------------------------------------

    [Test]
    public void VoiceBankSelectionPerLanguageMatchesFevAndClient()
    {
        var dir = SoundDir();
        var fev = SoundLibrary.Shared(dir).Fev;

        // FEV LANG chunk lists the 5 project languages and default index 2 ("default")
        Assert.That(fev.Languages, Is.EqualTo(new[] { "Korea", "cn", "default", "en", "jp" }));
        Assert.That(fev.DefaultLanguageIndex, Is.EqualTo(2));

        // Client GetVoice() -> EventSystem::setLanguage (soundservice.cpp:418-433) -> FEV bank suffix
        Assert.That(fev.BankFileNameForVoice("MS2Sound_Voice", 0), Is.EqualTo("MS2Sound_Voice_Kor_def.fsb"));
        Assert.That(fev.BankFileNameForVoice("MS2Sound_Voice", 1), Is.EqualTo("MS2Sound_Voice_jp.fsb"));
        Assert.That(fev.BankFileNameForVoice("MS2Sound_Voice", 2), Is.EqualTo("MS2Sound_Voice_en.fsb"));
        Assert.That(fev.BankFileNameForVoice("MS2Sound_Voice", 3), Is.EqualTo("MS2Sound_Voice_cn.fsb"));
        Assert.That(fev.BankFileNameForVoice("MS2Sound_AttackVoice", 2), Is.EqualTo("MS2Sound_AttackVoice_en.fsb"));
        Assert.That(fev.BankFileNameForVoice("MS2Sound_Emotion", 0), Is.EqualTo("MS2Sound_Emotion_Kor_def.fsb"));

        // Non-voice banks have no language suffix in any language
        foreach (int voice in new[] { 0, 1, 2, 3 })
            Assert.That(fev.BankFileNameForVoice("MS2Sound_BGM", voice), Is.EqualTo("MS2Sound_BGM.fsb"));

        // The KR voice banks ship in every Data/Sound; the en ones only in some (KMS2 has them, KMS2_DEV does not). The client
        // does not pick a file itself: EventSystem::setLanguage (soundservice.cpp:418-433) names the language and FMOD opens
        // <bank>_<language>.fsb, so a missing en bank is the library's own fallback to the FEV default (_Kor_def).
        var kr = new SoundLibrary(dir, voiceLanguage: 0);
        var en = new SoundLibrary(dir, voiceLanguage: 2);
        var jp = new SoundLibrary(dir, voiceLanguage: 1);
        if (IsKr())
        {
            Assert.That(Path.GetFileName(kr.Bank("MS2Sound_Voice").Path), Is.EqualTo("MS2Sound_Voice_Kor_def.fsb"));
            bool hasEn = en.HasBankForVoice("MS2Sound_Voice", 2);
            Assert.That(Path.GetFileName(en.Bank("MS2Sound_Voice").Path), Is.EqualTo(hasEn ? "MS2Sound_Voice_en.fsb" : "MS2Sound_Voice_Kor_def.fsb"));
            if (hasEn)
            {
                var krVoice = kr.ResolveEvent("ko/Npc/01000022", new Random(0));
                var enVoice = en.ResolveEvent("ko/Npc/01000022", new Random(0));
                Assert.That(krVoice, Is.Not.Null);
                Assert.That(enVoice, Is.Not.Null);
                Assert.That(krVoice.Name, Does.StartWith("Male_Age30_Doctor_Normal_Greet_"));
                Assert.That(enVoice.Name, Is.EqualTo("en_" + krVoice.Name));
                Assert.That(WalkMpeg(krVoice.Mp3).frames, Is.GreaterThan(5));
                Assert.That(WalkMpeg(enVoice.Mp3).frames, Is.GreaterThan(5));
            }

            // Strict bank open fails when a language bank (e.g. JP) is not installed; fallback uses FEV default (_Kor_def)
            if (!jp.HasBankForVoice("MS2Sound_Voice", 1))
            {
                Assert.Throws<FileNotFoundException>(() => jp.Bank("MS2Sound_Voice", allowFallback: false));
                Assert.That(Path.GetFileName(jp.Bank("MS2Sound_Voice", allowFallback: true).Path), Is.EqualTo("MS2Sound_Voice_Kor_def.fsb"));
            }

            // Programmer sound voice tag lookup (CSoundService::OpenVoiceStreams + BuildTagTable over Voice8 / Voice9)
            // The tag banks (Voice8 / Voice9) are per language file too: assert each language only where its file is installed.
            var krTag = kr.ResolveVoiceTag("Guide_Sidepopup_Tutorial_01");
            if (krTag != null)
                Assert.That(krTag.Name, Is.EqualTo("Guide_Sidepopup_Tutorial_01"));
            if (hasEn)
            {
                var enTag = en.ResolveVoiceTag("Guide_Sidepopup_Tutorial_01");
                Assert.That(krTag, Is.Not.Null);
                Assert.That(enTag, Is.Not.Null);
                Assert.That(enTag.Name, Is.EqualTo("en_Guide_Sidepopup_Tutorial_01"));
            }
            Assert.That(jp.HasVoiceTag("Guide_Sidepopup_Tutorial_01"), Is.False, "JP returns early in OpenVoiceStreams");
        }
        else
        {
            // GMS2 Data/Sound ships only the 11 *_en.fsb voice banks (no *_Kor_def.fsb or *_jp.fsb).
            Assert.That(kr.HasBankForVoice("MS2Sound_Voice", 0), Is.False);
            Assert.That(en.HasBankForVoice("MS2Sound_Voice", 2), Is.True);
            Assert.That(Path.GetFileName(en.Bank("MS2Sound_Voice").Path), Is.EqualTo("MS2Sound_Voice_en.fsb"));
            var enVoice = en.ResolveEvent("ko/Npc/01000022", new Random(0));
            Assert.That(enVoice, Is.Not.Null);
            Assert.That(enVoice.Name, Does.StartWith("en_Male_Age30_Doctor_Normal_Greet_"));
            Assert.That(WalkMpeg(enVoice.Mp3).frames, Is.GreaterThan(5));
            Assert.Throws<FileNotFoundException>(() => kr.Bank("MS2Sound_Voice", allowFallback: false));
            Assert.Throws<FileNotFoundException>(() => jp.Bank("MS2Sound_Voice", allowFallback: false));
            Assert.That(kr.ResolveVoiceTag("Guide_Sidepopup_Tutorial_01"), Is.Null);
            var enTag = en.ResolveVoiceTag("Guide_Sidepopup_Tutorial_01");
            Assert.That(enTag, Is.Not.Null);
            Assert.That(enTag.Name, Is.EqualTo("en_Guide_Sidepopup_Tutorial_01"));
            Assert.That(jp.HasVoiceTag("Guide_Sidepopup_Tutorial_01"), Is.False, "JP returns early in OpenVoiceStreams");
        }
    }

    // ---- event groups beyond the event category ------------------------------------------------------------------

    [Test]
    public void EventGroupsDisambiguateDuplicateNamesInSameCategory()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var fev = lib.Fev;

        // OBCT kind 3 = 39 groups; KMS Sound/MS2Sound.fev has 12677 events, GMS2 Sound/MS2Sound.fev has 12467.
        Assert.That(fev.Groups.Count, Is.EqualTo(39));
        Assert.That(fev.EventCount, Is.EqualTo(IsKr() ? 12677 : 12467));

        // 01200001 exists in BOTH MS2Sound/Sound/Attack/Swing and MS2Sound/Sound/Skill/Berserker, both category "effect"
        var swing = fev.FindEvent("01200001", "MS2Sound/Sound/Attack/Swing");
        var berserker = fev.FindEvent("01200001", "MS2Sound/Sound/Skill/Berserker");
        Assert.That(swing, Is.Not.Null);
        Assert.That(berserker, Is.Not.Null);
        Assert.That(swing.Group, Is.EqualTo("MS2Sound/Sound/Attack/Swing"));
        Assert.That(berserker.Group, Is.EqualTo("MS2Sound/Sound/Skill/Berserker"));
        Assert.That(swing.Category, Is.EqualTo("effect"));
        Assert.That(berserker.Category, Is.EqualTo("effect"));
        Assert.That(swing.Defs[0], Is.Not.EqualTo(berserker.Defs[0]), "same name & same category resolve to different sound defs by group");

        // Suffix group lookup also disambiguates
        Assert.That(fev.FindEvent("01200001", "Skill/Berserker"), Is.SameAs(berserker));
        Assert.That(fev.FindEvent("01200001", "Attack/Swing"), Is.SameAs(swing));

        // Programmer-sound event "1" under MS2Sound/Sound/Voice/ko (not 6..12 digits) is parsed by the group tree
        var prog = fev.FindEvent("1", "MS2Sound/Sound/Voice/ko");
        Assert.That(prog, Is.Not.Null);
        Assert.That(prog.Category, Is.EqualTo("EmotionVoice"));
        Assert.That(fev.EventsInGroup("MS2Sound/Sound/Skill/Berserker").Count, Is.EqualTo(72));
    }

    // ---- theme crossfade (MusicTheme.CrossfadeMs) ----------------------------------------------------------------

    [Test]
    public void ThemeCrossfadeTransitionsAndZeroAllocStep()
    {
        var xf = new MusicCrossfade();

        // First track starts immediately on slot 0 without fading from silence.
        var first = xf.BeginTransition(crossfadeMs: 2000, hasActiveAudio: false);
        Assert.That(first, Is.EqualTo((0, -1, false)));
        Assert.That(xf.IsFading, Is.False);
        Assert.That(xf.Gain(0), Is.EqualTo(1f));
        Assert.That(xf.Gain(1), Is.EqualTo(0f));

        // Transition to a second cue/segment with CrossfadeMs = 2000 flips to slot 1 and crossfades linearly.
        var second = xf.BeginTransition(crossfadeMs: 2000, hasActiveAudio: true);
        Assert.That(second, Is.EqualTo((1, -1, true)));
        Assert.That(xf.IsFading, Is.True);
        Assert.That(xf.Gain(0), Is.EqualTo(1f));
        Assert.That(xf.Gain(1), Is.EqualTo(0f));

        // Halfway through 2000 ms (1.0 s): outgoing 0.5, incoming 0.5.
        Assert.That(xf.Step(1.0f), Is.EqualTo(-1));
        Assert.That(xf.IsFading, Is.True);
        Assert.That(xf.Gain(0), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(xf.Gain(1), Is.EqualTo(0.5f).Within(1e-5f));

        // Interrupted mid-crossfade by another cue (1000 ms): stops old outgoing slot 0 immediately,
        // fades interrupted slot 1 down from its current gain (0.5), ramps slot 0 from 0 -> 1.
        var third = xf.BeginTransition(crossfadeMs: 1000, hasActiveAudio: true);
        Assert.That(third, Is.EqualTo((0, 0, true)));
        Assert.That(xf.Gain(0), Is.EqualTo(0f));
        Assert.That(xf.Gain(1), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(xf.Step(0.5f), Is.EqualTo(-1));
        Assert.That(xf.Gain(0), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(xf.Gain(1), Is.EqualTo(0.25f).Within(1e-5f));

        // Completing the crossfade returns the outgoing slot (1) to stop and settles gains to 1 / 0.
        Assert.That(xf.Step(0.5f), Is.EqualTo(1));
        Assert.That(xf.IsFading, Is.False);
        Assert.That(xf.Gain(0), Is.EqualTo(1f));
        Assert.That(xf.Gain(1), Is.EqualTo(0f));

        // Zero crossfade cuts immediately without fading.
        var cut = xf.BeginTransition(crossfadeMs: 0, hasActiveAudio: true);
        Assert.That(cut.Crossfading, Is.False);
        Assert.That(xf.IsFading, Is.False);
        Assert.That(xf.ActiveGain, Is.EqualTo(1f));
        Assert.That(xf.OutgoingGain, Is.EqualTo(0f));

        // Mid-fade interrupt at t = 0 (incoming slot still silent at gain 0): reuses silent slot 1 and keeps audible
        // slot 0 fading out smoothly from 1.0 instead of cutting slot 0 and jumping silent slot 1 to 1.0.
        xf.StopAll();
        xf.BeginTransition(crossfadeMs: 2000, hasActiveAudio: false);
        xf.BeginTransition(crossfadeMs: 2000, hasActiveAudio: true);   // slot 0 = 1.0 (outgoing), slot 1 = 0.0 (incoming)
        var atZero = xf.BeginTransition(crossfadeMs: 1000, hasActiveAudio: true);
        Assert.That(atZero, Is.EqualTo((1, 1, true)));
        Assert.That(xf.Gain(0), Is.EqualTo(1f).Within(1e-5f));
        Assert.That(xf.Gain(1), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(xf.Step(0.5f), Is.EqualTo(-1));
        Assert.That(xf.Gain(0), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(xf.Gain(1), Is.EqualTo(0.5f).Within(1e-5f));

        // EndOfSegment links (TransitionType = 4, all 311 links in MS2Sound.fev) chain with 0 ms overlap.
        var endLink = new MusicLink { TransitionType = MusicLink.TransitionEndOfSegment };
        Assert.That(MusicCrossfade.LinkCrossfadeMs(endLink, 2000), Is.EqualTo(0));

        // Per-frame allocation guard (godot-csharp-perf): Step and LinkCrossfadeMs allocate 0 bytes.
        xf.BeginTransition(crossfadeMs: 2000, hasActiveAudio: true);
        xf.Step(0.001f);
        MusicCrossfade.LinkCrossfadeMs(endLink, 2000);
        long allocated = Alloc.Least(() =>
        {
            for (int i = 0; i < 100_000; i++)
            {
                if (!xf.IsFading) xf.BeginTransition(crossfadeMs: 2000, hasActiveAudio: true);
                xf.Step(0.016f);
                _ = MusicCrossfade.LinkCrossfadeMs(endLink, 2000);
            }
        });
        // Under 512 B over 100000 steps, the repo's bound, in the least of the windows (Alloc.Least): a per-step allocation
        // (at least 24 B each, 2.4 MB in all) could not hide under it.
        Assert.That(allocated, Is.LessThan(512), "MusicCrossfade per-frame stepping must not allocate");
    }

    [Test]
    public void ThemeCrossfadeUsesFevThemeCrossfadeMsOnlyOnCueChangeNotInThemeLinks()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);
        var xf = new MusicCrossfade();

        // Tria (cue 35) has CrossfadeMs = 2000 in comp/thms/thmd, and links 84 -> 87 -> 84 are TransitionType = 4 (EndOfSegment).
        Assert.That(mc.PlayFieldBgm(new FieldBgmSpec(BgmId: 35, BgmParam: 5, BgmValue: 0f)), Is.True);
        Assert.That(mc.CurrentCue.CrossfadeMs, Is.EqualTo(2000));
        xf.BeginTransition(mc.CurrentCue.CrossfadeMs, hasActiveAudio: false);
        mc.OnSegmentStarted();
        var seg84 = mc.CurrentSegment;
        Assert.That(seg84.Id, Is.EqualTo(84));
        Assert.That(seg84.OutgoingLinks[0].TransitionType, Is.EqualTo(MusicLink.TransitionEndOfSegment));

        // 84 -> 87 (bridge): sequential EndOfSegment link has 0 ms crossfade and zero overlap.
        var peeked = mc.PeekNextSegment();
        Assert.That(peeked, Is.Not.Null);
        Assert.That(peeked.Id, Is.EqualTo(87));
        Assert.That(mc.CurrentSegment.Id, Is.EqualTo(84), "PeekNextSegment must not mutate CurrentSegment");
        int link1Fade = MusicCrossfade.LinkCrossfadeMs(seg84.OutgoingLinks[0], mc.CurrentCue.CrossfadeMs);
        Assert.That(link1Fade, Is.EqualTo(0));
        var seg87 = mc.AdvanceSegment();
        Assert.That(seg87.Id, Is.EqualTo(87));
        var step87 = xf.BeginTransition(link1Fade, hasActiveAudio: true);
        Assert.That(step87.Crossfading, Is.False);
        Assert.That(xf.IsFading, Is.False);
        Assert.That(xf.OutgoingGain, Is.EqualTo(0f));
        Assert.That(xf.ActiveGain, Is.EqualTo(1f));
        mc.OnSegmentStarted();

        // 87 -> 84 (back to BGM_Tria_01): also sequential EndOfSegment with 0 ms crossfade and zero overlap.
        int link2Fade = MusicCrossfade.LinkCrossfadeMs(seg87.OutgoingLinks[0], mc.CurrentCue.CrossfadeMs);
        Assert.That(link2Fade, Is.EqualTo(0));
        var back84 = mc.AdvanceSegment();
        Assert.That(back84.Id, Is.EqualTo(84));
        var step84 = xf.BeginTransition(link2Fade, hasActiveAudio: true);
        Assert.That(step84.Crossfading, Is.False);
        Assert.That(xf.IsFading, Is.False);
        Assert.That(xf.OutgoingGain, Is.EqualTo(0f));
        Assert.That(xf.ActiveGain, Is.EqualTo(1f));

        // Cue change (35 -> 23, Boss01): crossfades over the incoming theme's CrossfadeMs (2000 ms).
        Assert.That(mc.PlayMusic(23, 0f, MusicController.DefaultPriority, false), Is.True);
        Assert.That(mc.CurrentCue.CrossfadeMs, Is.EqualTo(2000));
        var cueChange = xf.BeginTransition(mc.CurrentCue.CrossfadeMs, hasActiveAudio: true);
        Assert.That(cueChange.Crossfading, Is.True);
        Assert.That(xf.IsFading, Is.True);
        Assert.That(xf.FadeDuration, Is.EqualTo(2.0f).Within(1e-5f));

        // Zero allocation on PeekNextSegment in steady state.
        Assert.That(Alloc.Least(() => { for (int i = 0; i < 1000; i++) _ = mc.PeekNextSegment(); }), Is.EqualTo(0));
    }

    [Test]
    public void CueChangeDuringDecodeAndPredecodeWindowCannotAdvanceNewCue()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);
        var guard = new MusicPlaybackGuard();

        // Start Tria (cue 35, seg 84) and enter the last 2 s predecode window.
        Assert.That(mc.PlayFieldBgm(new FieldBgmSpec(BgmId: 35, BgmParam: 5, BgmValue: 0f)), Is.True);
        int genTria = guard.BeginCueChange();
        Assert.That(guard.AcceptDecode(genTria), Is.True);
        guard.OnSegmentPlaying(100.0, isSelfLoop: false);
        mc.OnSegmentStarted();
        Assert.That(guard.IsChained, Is.True);
        Assert.That(guard.TryBeginPredecode(97.5), Is.False);
        Assert.That(guard.TryBeginPredecode(98.5), Is.True, "enters last 2 s predecode window");
        Assert.That(guard.TryBeginPredecode(99.0), Is.False, "predecode requested once");

        // Cue changes to Boss01 (cue 23, seg 44) while Tria is in its last 2 s / predecode is in flight.
        Assert.That(mc.PlayMusic(23, 0f, MusicController.DefaultPriority, false), Is.True);
        Assert.That(mc.CurrentSegment.Id, Is.EqualTo(44));
        int genBoss = guard.BeginCueChange();
        Assert.That(guard.IsChained, Is.False, "old segment unchained before decoding new cue");
        Assert.That(guard.IsSegmentActive, Is.False);

        // Stale predecode from Tria (genTria) and stale Finished / _Process tick during Boss01's decode window are ignored.
        Assert.That(guard.AcceptDecode(genTria), Is.False);
        Assert.That(guard.TryBeginPredecode(99.5), Is.False);
        Assert.That(guard.TryAdvanceOnFinished(), Is.False);
        Assert.That(mc.CurrentCueId, Is.EqualTo(23));
        Assert.That(mc.CurrentSegment.Id, Is.EqualTo(44), "new cue segment must not be advanced by old segment Finished");

        // Once Boss01's decode finishes (genBoss), playback arms and normal segment chaining resumes (44 -> 104).
        Assert.That(guard.AcceptDecode(genBoss), Is.True);
        guard.OnSegmentPlaying(10.0, isSelfLoop: false);
        mc.OnSegmentStarted();
        Assert.That(guard.TryAdvanceOnFinished(), Is.True);
        var next = mc.AdvanceSegment();
        Assert.That(next, Is.Not.Null);
        Assert.That(next.Id, Is.EqualTo(104));
    }

    // ---- LoginBgBgmTable (table/loginbgbgm.xml) ------------------------------------------------------------------

    [Test]
    public void LoginBgBgmTableSelectionPriorityPaddingAndFallbackMatchClient()
    {
        // loginbgbgmdata.cpp:91-224: skips empty month/bg/bgmID, skips month > 12, pads short bgmParam with 5,
        // selects highest priority rows for the month and picks among ties + multiple bgm pairs.
        const string xml = """
            <ms2>
              <login clientPropertyBgDay="BG_Low.swf" month="1,2" bgmID="15" bgmParam="5" priority="1" />
              <login clientPropertyBgDay="BG_HighA.swf" month="1" bgmID="192,224" bgmParam="3" priority="10" />
              <login clientPropertyBgDay="BG_HighB.swf" month="1" bgmID="244" bgmParam="0" priority="10" />
              <login clientPropertyBgDay="BG_BadMonth.swf" month="13" bgmID="15" bgmParam="5" priority="99" />
              <login clientPropertyBgDay="BG_NoBgm.swf" month="4" bgmID="" bgmParam="5" priority="99" />
              <login clientPropertyBgDay="" month="4" bgmID="15" bgmParam="5" priority="99" />
            </ms2>
            """;
        var table = LoginBgBgmTable.Parse(xml);

        // Month 1: only priority-10 rows (HighA, HighB) compete; HighA has 2 cues with second param padded to 5.
        Assert.That(table.SelectCurrent(1, _ => 0), Is.True);
        Assert.That(table.GetBgDay(), Is.EqualTo("BG_HighA.swf"));
        Assert.That(table.GetBgm(_ => 0), Is.EqualTo((192, 3)));
        Assert.That(table.GetBgm(_ => 1), Is.EqualTo((224, 5)), "short bgmParam list padded with 5 (loginbgbgmdata.cpp:133-134)");

        Assert.That(table.SelectCurrent(1, _ => 1), Is.True);
        Assert.That(table.GetBgDay(), Is.EqualTo("BG_HighB.swf"));
        Assert.That(table.GetBgm(_ => 0), Is.EqualTo((244, 0)));

        // Month 2: only priority-1 row exists.
        Assert.That(table.SelectCurrent(2, _ => 0), Is.True);
        Assert.That(table.GetBgDay(), Is.EqualTo("BG_Low.swf"));
        Assert.That(table.GetBgm(_ => 0), Is.EqualTo((15, 5)));

        // Month 4 (rows skipped for missing bgmID/bgDay) and invalid months (0, 13): no selection -> empty / (0, 0).
        Assert.That(table.SelectCurrent(4, _ => 0), Is.False);
        Assert.That(table.GetBgDay(), Is.EqualTo(""));
        Assert.That(table.GetBgm(_ => 0), Is.EqualTo((0, 0)));
        Assert.That(table.SelectCurrent(13, _ => 0), Is.False);
        Assert.That(table.SelectCurrent(0, _ => 0), Is.False);

        // WorldService::FUN_141bc7b80 (@0x141bc7b80, worldservice_1.cpp:4856-4872): fallback to map 1 <bgm> when GetBgm().CueId == 0.
        var map1 = new FieldBgmSpec(BgmId: 192, BgmParam: 5, BgmValue: 2.5f);
        Assert.That(table.ResolveLoginBgm(map1, _ => 0), Is.EqualTo((192, 5, 2.5f)));
        Assert.That(table.SelectCurrent(1, _ => 1), Is.True);
        Assert.That(table.ResolveLoginBgm(map1, _ => 0), Is.EqualTo((244, 0, 2.5f)), "uses map 1 BgmValue with table's cue/param");

        // LoginBgBgmTable::SelectCurrent (loginbgbgmdata.cpp:164-177): server time + CMsTime zone determines month.
        // 2025-02-28 20:00:00 UTC: month 2 in UTC (offset 0), month 3 in KST (ZoneSeconds = -32400 -> 2025-03-01 05:00).
        long feb28Utc20 = new DateTimeOffset(2025, 2, 28, 20, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.That(LoginBgBgmTable.MonthForServerTime(feb28Utc20, timeZoneSet: true, calcZoneOffset: 0), Is.EqualTo(2));
        Assert.That(LoginBgBgmTable.MonthForServerTime(feb28Utc20, timeZoneSet: true, calcZoneOffset: -32400), Is.EqualTo(3));
        Assert.That(LoginBgBgmTable.MonthForServerTime(feb28Utc20, timeZoneSet: true, calcZoneOffset: 28800), Is.EqualTo(2));
    }

    [Test]
    public void LoginBgBgmTableLoadsRealXmlAndPlaysThroughMusicController()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        using var packs = new Ms2.Formats.Pack.PackSet(Environment.GetEnvironmentVariable("MS2_DATA"));
        var mc = new MusicController(lib.Fev);
        if (IsKr(packs))
        {
            var table = LoginBgBgmTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/loginbgbgm.xml")));

            // Every calendar month 1..12 has at least one row with a non-empty background and non-zero BGM cue.
            for (int month = 1; month <= 12; month++)
            {
                Assert.That(table.SelectCurrent(month, _ => 0), Is.True, $"month {month}");
                Assert.That(table.GetBgDay(), Is.Not.Empty, $"month {month} bgDay");
                Assert.That(table.GetBgm(_ => 0).CueId, Is.GreaterThan(0), $"month {month} cueId");
            }

            // Month 1 first candidate: BG_winter2020_Login.swf -> cue 15, param 5 -> BGM_Login_Reminisce_Start_01.
            Assert.That(table.SelectCurrent(1, _ => 0), Is.True);
            Assert.That(table.GetBgDay(), Is.EqualTo("BG_winter2020_Login.swf"));
            Assert.That(table.GetBgm(_ => 0), Is.EqualTo((15, 5)));

            var map1Fallback = new FieldBgmSpec(BgmId: 192, BgmParam: 5, BgmValue: 0f);
            Assert.That(mc.PlayLoginBgm(table, map1Fallback, _ => 0), Is.True);
            Assert.That(mc.CurrentCueId, Is.EqualTo(15));
            Assert.That(lib.ResolveSegment(mc.CurrentSegment).Name, Is.EqualTo("BGM_Login_Reminisce_Start_01"));

            // When no login row is selected (GetBgm == (0, 0)), falls back to map 1's own <bgm id="192" param="5"/>.
            table.SelectCurrent(0);
            Assert.That(mc.PlayLoginBgm(table, map1Fallback, _ => 0), Is.True);
            Assert.That(mc.CurrentCueId, Is.EqualTo(192));
        }
        else
        {
            // GMS2 ships no table/loginbgbgm.xml (GameFlow falls back to login map 00000001's own <bgm>):
            // map/00000001.xml <environment feature="CBT" locale="NA"> sets <bgm id="227" param="5" value="0"/>.
            Assert.That(packs.Exists("Xml", "table/loginbgbgm.xml"), Is.False);
            var emptyTable = new LoginBgBgmTable();
            Assert.That(emptyTable.SelectCurrent(1, _ => 0), Is.False);
            var map1Fallback = new FieldBgmSpec(BgmId: 227, BgmParam: 5, BgmValue: 0f);
            Assert.That(mc.PlayLoginBgm(emptyTable, map1Fallback, _ => 0), Is.True);
            Assert.That(mc.CurrentCueId, Is.EqualTo(227));
            Assert.That(lib.ResolveSegment(mc.CurrentSegment).Name, Is.EqualTo("BGM_RoadtoHome_Intro_01"));
        }
    }

    // ---- SoundMusic effect type 3 (CSoundMusicEffect, soundmusiceffectparam.cpp:125-176) -------------------------

    [Test]
    public void SoundMusicEffectAppliesCueParamFadeRestoreAndZeroAllocTick()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(35));

        var fx = new SoundMusicEffectState();
        const float categoryVol = 0.8f;

        // Default casterKind (unknown/remote caster) and explicit casterKind > 1 are rejected (soundmusiceffectparam.cpp:139-140).
        var defaultRejected = fx.Apply(mc, new SoundMusicEffectParam(CueId: 15, ParamId: 5, ParamValue: 10f, Volume: 0.5f, Duration: 1f),
            nowSeconds: 10.0, currentMusicVolume: categoryVol, categoryMusicVolume: categoryVol);
        Assert.That(defaultRejected, Is.EqualTo((false, false, false)));
        Assert.That(mc.CurrentCueId, Is.EqualTo(35));
        Assert.That(fx.IsFading, Is.False);

        var rejected = fx.Apply(mc, new SoundMusicEffectParam(CueId: 15, ParamId: 5, ParamValue: 10f, Volume: 0.5f, Duration: 1f),
            nowSeconds: 10.0, currentMusicVolume: categoryVol, categoryMusicVolume: categoryVol, casterKind: SoundMusicEffectState.CasterOther);
        Assert.That(rejected, Is.EqualTo((false, false, false)));
        Assert.That(mc.CurrentCueId, Is.EqualTo(35));
        Assert.That(fx.IsFading, Is.False);

        // Apply cue 15 + param 5 = 10 + linear volume fade to 0.5 * categoryVol over 1.0 s for local player (CasterMyPc = 1).
        var applied = fx.Apply(mc, new SoundMusicEffectParam(CueId: 15, ParamId: 5, ParamValue: 10f, Priority: 0x80, Volume: 0.5f, Duration: 1.0f),
            nowSeconds: 10.0, currentMusicVolume: categoryVol, categoryMusicVolume: categoryVol, casterKind: SoundMusicEffectState.CasterMyPc);
        Assert.That(applied, Is.EqualTo((true, true, true)));
        Assert.That(mc.CurrentCueId, Is.EqualTo(15));
        Assert.That(mc.TryGetMusicParameter(5, out float p5) && p5 == 10f, Is.True);
        Assert.That(fx.IsFading, Is.True);

        // vfunc_9 linear interpolation (soundmusiceffectparam.cpp:166-172): 0.8 -> 0.4 over [10.0, 11.0].
        Assert.That(fx.Tick(10.0, categoryVol, out float v), Is.True);
        Assert.That(v, Is.EqualTo(0.8f).Within(1e-5f));
        Assert.That(fx.Tick(10.5, categoryVol, out v), Is.True);
        Assert.That(v, Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(fx.Tick(11.0, categoryVol, out v), Is.True);
        Assert.That(v, Is.EqualTo(0.4f).Within(1e-5f));
        // Past startTime + duration: returns false and restores category volume (~CSoundMusicEffect :125-128).
        Assert.That(fx.Tick(11.01, categoryVol, out v), Is.False);
        Assert.That(v, Is.EqualTo(categoryVol).Within(1e-5f));
        Assert.That(fx.IsFading, Is.False);

        // cueID == 0 pops back to previous cue (35); volume <= 0 or duration == 0 returns FadeStarted == false.
        var popped = fx.Apply(mc, new SoundMusicEffectParam(CueId: 0, Priority: 0x80, Volume: -1f, Duration: 0f),
            nowSeconds: 12.0, currentMusicVolume: categoryVol, categoryMusicVolume: categoryVol, casterKind: 0);
        Assert.That(popped, Is.EqualTo((true, false, false)));
        Assert.That(mc.CurrentCueId, Is.EqualTo(35));
        Assert.That(fx.IsFading, Is.False);

        // Per-frame allocation guard on SoundMusicEffectState.Tick.
        fx.Apply(mc, new SoundMusicEffectParam(Volume: 0.5f, Duration: 1000f), nowSeconds: 0.0, currentMusicVolume: categoryVol, categoryMusicVolume: categoryVol, casterKind: SoundMusicEffectState.CasterSystem);
        fx.Tick(0.016, categoryVol, out _);
        long bytes = Alloc.Least(() => { for (int i = 0; i < 10_000; i++) fx.Tick(i * 0.016, categoryVol, out _); });
        Assert.That(bytes, Is.EqualTo(0), "SoundMusicEffectState.Tick must not allocate");
    }

    [Test]
    public void SoundMusicEffectStartDelayPromptsCueAndParamImmediately()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.True);

        var fx = new SoundMusicEffectState();
        const float categoryVol = 0.8f;
        // CSoundMusicEffect::vfunc_3 (soundmusiceffectparam.cpp:133-156) + CEffect::vfunc_3 (effectservice.cpp:365):
        // PlayMusic and SetMusicParameter fire at creation even with StartDelay > 0; StartDelay shifts m_dStartTime.
        var res = fx.Apply(mc,
            new SoundMusicEffectParam(CueId: 15, ParamId: 5, ParamValue: 10f, Volume: 0.5f, Duration: 1.0f, StartDelay: 2.0f),
            nowSeconds: 10.0, currentMusicVolume: categoryVol, categoryMusicVolume: categoryVol, casterKind: SoundMusicEffectState.CasterSystem);
        Assert.That(res, Is.EqualTo((true, true, true)));
        Assert.That(mc.CurrentCueId, Is.EqualTo(15), "PlayMusic prompted immediately at creation");
        Assert.That(mc.TryGetMusicParameter(5, out float p5) && p5 == 10f, Is.True, "SetMusicParameter prompted immediately at creation");
        Assert.That(fx.StartTime, Is.EqualTo(12.0).Within(1e-6), "m_dStartTime = dStart + startDelay");

        Assert.That(fx.Tick(11.0, categoryVol, out float v), Is.True);
        Assert.That(v, Is.EqualTo(0.8f).Within(1e-5f), "volume holds before startTime");
        Assert.That(fx.Tick(12.5, categoryVol, out v), Is.True);
        Assert.That(v, Is.EqualTo(0.6f).Within(1e-5f), "midpoint of [12.0, 13.0]");
        Assert.That(fx.Tick(13.01, categoryVol, out v), Is.False);
        Assert.That(v, Is.EqualTo(categoryVol).Within(1e-5f), "restores category volume past startTime + duration");
    }

    [Test]
    public void SoundMusicEffectNonFadingApplyPreservesRunningFadeAndStopRestores()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.True);

        var fx = new SoundMusicEffectState();
        const float categoryVol = 0.8f;

        // Effect A starts a 1.0 s volume fade (0.8 -> 0.4).
        var fadeRes = fx.Apply(mc, new SoundMusicEffectParam(Volume: 0.5f, Duration: 1.0f),
            nowSeconds: 0.0, currentMusicVolume: categoryVol, categoryMusicVolume: categoryVol, casterKind: SoundMusicEffectState.CasterSystem);
        Assert.That(fadeRes.FadeStarted, Is.True);
        Assert.That(fx.Tick(0.25, categoryVol, out float v), Is.True);
        Assert.That(v, Is.EqualTo(0.7f).Within(1e-5f));

        // Effect B applies a cue change with no fade (Volume = -1, Duration = 0): must NOT cancel Effect A's running fade.
        var noFadeRes = fx.Apply(mc, new SoundMusicEffectParam(CueId: 15, Volume: -1f, Duration: 0f),
            nowSeconds: 0.25, currentMusicVolume: v, categoryMusicVolume: categoryVol, casterKind: SoundMusicEffectState.CasterSystem);
        Assert.That(noFadeRes, Is.EqualTo((true, false, false)));
        Assert.That(mc.CurrentCueId, Is.EqualTo(15));
        Assert.That(fx.IsFading, Is.True, "non-fading SoundMusic effect must not cancel a running fade");
        Assert.That(fx.Tick(0.5, categoryVol, out v), Is.True);
        Assert.That(v, Is.EqualTo(0.6f).Within(1e-5f));

        // Freeing the fading effect mid-fade immediately restores category 2 volume (~CSoundMusicEffect, soundmusiceffectparam.cpp:125-128).
        float restored = fx.Stop(categoryVol);
        Assert.That(fx.IsFading, Is.False);
        Assert.That(restored, Is.EqualTo(categoryVol).Within(1e-5f));
    }

    [Test]
    public void EffectTableParsesRealSoundMusicEffectsAndDrivesMusicController()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var packs = new Ms2.Formats.Pack.PackSet(Environment.GetEnvironmentVariable("MS2_DATA"));
        var table = new Ms2.Formats.World.EffectTable(packs);

        var startParts = table.Get("cinematic/start_sound_music_effect.xml");
        var endParts = table.Get("cinematic/end_sound_music_effect.xml");
        Assert.That(startParts, Is.Not.Null);
        Assert.That(endParts, Is.Not.Null);
        Assert.That(startParts.Count, Is.EqualTo(4));
        Assert.That(endParts.Count, Is.EqualTo(4));

        // Verify CSoundMusicEffectParam::vfunc_1 attribute parsing (including TinyXML int-prefix "1.0" -> 1).
        Assert.That(startParts[0].MusicCueId, Is.EqualTo(15));
        Assert.That(startParts[0].MusicPriority, Is.EqualTo(128));
        Assert.That(startParts[0].MusicParamId, Is.EqualTo(-1));
        Assert.That(startParts[0].MusicVolume, Is.EqualTo(-1f));
        Assert.That(startParts[1].MusicReverb, Is.EqualTo("Bath"));
        Assert.That(startParts[2].MusicParamId, Is.EqualTo(1));
        Assert.That(startParts[2].MusicParamValue, Is.EqualTo(0.2f).Within(1e-5f));
        Assert.That(startParts[3].Duration, Is.EqualTo(1.0f).Within(1e-5f));
        Assert.That(startParts[3].MusicVolume, Is.EqualTo(0.5f).Within(1e-5f));

        Assert.That(endParts[0].MusicCueId, Is.EqualTo(0));
        Assert.That(endParts[1].MusicReverb, Is.EqualTo("Pre"));
        Assert.That(endParts[2].MusicParamId, Is.EqualTo(1));
        Assert.That(endParts[3].MusicVolume, Is.EqualTo(1.0f).Within(1e-5f));

        // Play start then end through MusicController + SoundMusicEffectState from Tria (cue 35) as system/cinematic (CasterSystem = 0).
        var mc = new MusicController(lib.Fev);
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.True);
        var fx = new SoundMusicEffectState();
        float vol = 1.0f;
        foreach (var p in startParts)
            fx.Apply(mc, new SoundMusicEffectParam(p.MusicCueId, p.MusicParamId, p.MusicParamValue, p.MusicPriority, p.MusicVolume, p.Duration, p.StartDelay, p.MusicReverb),
                nowSeconds: 0.0, currentMusicVolume: vol, categoryMusicVolume: 1.0f, casterKind: SoundMusicEffectState.CasterSystem);
        Assert.That(mc.CurrentCueId, Is.EqualTo(15));
        Assert.That(fx.IsFading, Is.True);
        Assert.That(fx.Tick(1.0, 1.0f, out vol), Is.True);
        Assert.That(vol, Is.EqualTo(0.5f).Within(1e-5f));

        foreach (var p in endParts)
            fx.Apply(mc, new SoundMusicEffectParam(p.MusicCueId, p.MusicParamId, p.MusicParamValue, p.MusicPriority, p.MusicVolume, p.Duration, p.StartDelay, p.MusicReverb),
                nowSeconds: 2.0, currentMusicVolume: vol, categoryMusicVolume: 1.0f, casterKind: SoundMusicEffectState.CasterSystem);
        Assert.That(mc.CurrentCueId, Is.EqualTo(35), "end_sound_music_effect cueID=0 restores Tria");
        Assert.That(fx.IsFading, Is.True);
        Assert.That(fx.Tick(2.5, 1.0f, out vol), Is.True);
        Assert.That(vol, Is.EqualTo(0.75f).Within(1e-5f));
    }

    // ---- Part 1 review fixes (ResetMusicState offset, ResolveSegment missing bank, session MusicController) -------

    [Test]
    public void Part1ReviewFixesPreserveParamOffsetHandleMissingBankAndShareSessionMusic()
    {
        var lib = SoundLibrary.Shared(SoundDir());

        // 1. CSoundEventMgr::ResetMusicState (@0x14177c8a0, soundeventmgr.cpp:1307-1323) preserves s_nParamHistoryOffset.
        var mc = new MusicController(lib.Fev);
        Assert.That(mc.SetMusicParameter(5, 0f, priority: 0x40, force: false), Is.True);
        Assert.That(mc.SetMusicParameter(5, -1f, priority: 0x80, force: false), Is.True);   // rejected pop decrements offset to -2
        mc.ResetMusicState();
        Assert.That(mc.SetMusicParameter(5, 1f, priority: 0x80, force: false), Is.True);
        Assert.That(mc.SetMusicParameter(5, 2f, priority: 0x80, force: false), Is.True);
        Assert.That(mc.SetMusicParameter(5, 3f, priority: 0x80, force: false), Is.True);
        Assert.That(mc.SetMusicParameter(5, -1f, priority: 0x80, force: false), Is.True);   // uses preserved offset -2 -> index 0 (1f)
        Assert.That(mc.TryGetMusicParameter(5, out float popped) && popped == 1f, Is.True, "paramHistoryOffset (-2) survived ResetMusicState");

        // 2. SoundLibrary.ResolveSegment returns null (does not throw) when a segment's bank file is missing.
        var missingBankSeg = new MusicSegment { Id = 999999, Bank = "MS2Sound_NoSuchBank_XYZ", Index = 0 };
        Assert.That(lib.ResolveSegment(missingBankSeg), Is.Null);

        // 3. One session MusicController per FevProject persists across fields until shutdown reset (worldservice_1.cpp:10583).
        MusicController.ResetShared();
        var s1 = MusicController.Shared(lib.Fev);
        var s2 = MusicController.Shared(lib.Fev);
        Assert.That(s2, Is.SameAs(s1));
        Assert.That(s1.PlayMusic(35, 0f, 0x80, false), Is.True);
        Assert.That(s2.CurrentCueId, Is.EqualTo(35));
        MusicController.ResetShared();
        Assert.That(s2.CurrentCueId, Is.EqualTo(0));
    }

    [Test]
    public void MatchesWaveNameRejectsEmptySampleName()
    {
        Assert.That(SoundLibrary.MatchesWaveName("", "BGM_Tria_01", ""), Is.False);
        Assert.That(SoundLibrary.MatchesWaveName(null, "BGM_Tria_01", ""), Is.False);
        Assert.That(SoundLibrary.MatchesWaveName("BGM_Tria_01", "BGM_Tria_01", ""), Is.True);
        Assert.That(SoundLibrary.MatchesWaveName("en_Male_Greet_01", "Male_Greet_01", "en_"), Is.True);
        Assert.That(SoundLibrary.MatchesWaveName("jp_Male_Greet_01", "Male_Greet_01", "en_"), Is.False);
    }

    [Test]
    public void SoundMusicEffectCasterKindFollowsMyPcVsRemotePcAndSystem()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var mc = new MusicController(lib.Fev);
        Assert.That(mc.PlayMusic(35, 0f, 0x80, false), Is.True);
        var fx = new SoundMusicEffectState();
        var cue15 = new SoundMusicEffectParam(CueId: 15, Priority: 0x80);

        // Remote PC (PCBase::vfunc_58 = 3, pcbase.cpp:1092): CasterKindForActor(false) = 3 (> 1) does NOT change the cue.
        int remoteKind = SoundMusicEffectState.CasterKindForActor(isMyPc: false);
        Assert.That(remoteKind, Is.EqualTo(SoundMusicEffectState.CasterOtherPc));
        var remoteRes = fx.Apply(mc, cue15, nowSeconds: 0.0, currentMusicVolume: 1f, categoryMusicVolume: 1f, casterKind: remoteKind);
        Assert.That(remoteRes, Is.EqualTo((false, false, false)));
        Assert.That(mc.CurrentCueId, Is.EqualTo(35), "effect cast by remote PC must not change local cue");

        // NPC / boss (CNpc::vfunc_58 = 5 / 6, unit_14108e1e0_npcactorandvibrate.cpp:654): an npc-owned hit's soundMusic part is ignored.
        foreach (int npcKind in new[] { SoundMusicEffectState.CasterNpc, SoundMusicEffectState.CasterBoss })
        {
            var npcRes = fx.Apply(mc, cue15, nowSeconds: 0.0, currentMusicVolume: 1f, categoryMusicVolume: 1f, casterKind: npcKind);
            Assert.That(npcRes, Is.EqualTo((false, false, false)), $"kind {npcKind}");
            Assert.That(mc.CurrentCueId, Is.EqualTo(35), "effect from an npc must not change the field music");
        }

        // Local player (MyPC::vfunc_58 = 1, mypc.cpp:1247): CasterKindForActor(true) = 1 (<= 1) changes the cue.
        int myPcKind = SoundMusicEffectState.CasterKindForActor(isMyPc: true);
        Assert.That(myPcKind, Is.EqualTo(SoundMusicEffectState.CasterMyPc));
        var myPcRes = fx.Apply(mc, cue15, nowSeconds: 0.0, currentMusicVolume: 1f, categoryMusicVolume: 1f, casterKind: myPcKind);
        Assert.That(myPcRes.CueChanged, Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(15), "effect cast by my PC must change local cue");

        // System/world effect (e.g. CSceneStateGame::FUN_141739450 taxi req.m_nObject = 0, scenestategame.cpp:11909,11918): applies.
        var popRes = fx.Apply(mc, new SoundMusicEffectParam(CueId: 0, Priority: 0x80), nowSeconds: 1.0, currentMusicVolume: 1f, categoryMusicVolume: 1f, casterKind: SoundMusicEffectState.CasterSystem);
        Assert.That(popRes.CueChanged, Is.True);
        Assert.That(mc.CurrentCueId, Is.EqualTo(35));
    }
}
