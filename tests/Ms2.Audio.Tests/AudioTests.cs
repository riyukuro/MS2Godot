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
        Assert.That(lib.Fev.Defs.Count, Is.EqualTo(12281));
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

        // Real KR vs EN voice banks both ship in Data/Sound and resolve the same NPC voice event to different banks/samples
        var kr = new SoundLibrary(dir, voiceLanguage: 0);
        var en = new SoundLibrary(dir, voiceLanguage: 2);
        Assert.That(Path.GetFileName(kr.Bank("MS2Sound_Voice").Path), Is.EqualTo("MS2Sound_Voice_Kor_def.fsb"));
        Assert.That(Path.GetFileName(en.Bank("MS2Sound_Voice").Path), Is.EqualTo("MS2Sound_Voice_en.fsb"));

        var krVoice = kr.ResolveEvent("ko/Npc/01000022", new Random(0));
        var enVoice = en.ResolveEvent("ko/Npc/01000022", new Random(0));
        Assert.That(krVoice, Is.Not.Null);
        Assert.That(enVoice, Is.Not.Null);
        Assert.That(krVoice.Name, Does.StartWith("Male_Age30_Doctor_Normal_Greet_"));
        Assert.That(enVoice.Name, Is.EqualTo("en_" + krVoice.Name));
        Assert.That(WalkMpeg(krVoice.Mp3).frames, Is.GreaterThan(5));
        Assert.That(WalkMpeg(enVoice.Mp3).frames, Is.GreaterThan(5));

        // Strict bank open fails when a language bank (e.g. JP) is not installed; fallback uses FEV default (_Kor_def)
        var jp = new SoundLibrary(dir, voiceLanguage: 1);
        if (!jp.HasBankForVoice("MS2Sound_Voice", 1))
        {
            Assert.Throws<FileNotFoundException>(() => jp.Bank("MS2Sound_Voice", allowFallback: false));
            Assert.That(Path.GetFileName(jp.Bank("MS2Sound_Voice", allowFallback: true).Path), Is.EqualTo("MS2Sound_Voice_Kor_def.fsb"));
        }

        // Programmer sound voice tag lookup (CSoundService::OpenVoiceStreams + BuildTagTable over Voice8 / Voice9)
        var krTag = kr.ResolveVoiceTag("Guide_Sidepopup_Tutorial_01");
        var enTag = en.ResolveVoiceTag("Guide_Sidepopup_Tutorial_01");
        Assert.That(krTag, Is.Not.Null);
        Assert.That(enTag, Is.Not.Null);
        Assert.That(krTag.Name, Is.EqualTo("Guide_Sidepopup_Tutorial_01"));
        Assert.That(enTag.Name, Is.EqualTo("en_Guide_Sidepopup_Tutorial_01"));
        Assert.That(jp.HasVoiceTag("Guide_Sidepopup_Tutorial_01"), Is.False, "JP returns early in OpenVoiceStreams");
    }

    // ---- event groups beyond the event category ------------------------------------------------------------------

    [Test]
    public void EventGroupsDisambiguateDuplicateNamesInSameCategory()
    {
        var lib = SoundLibrary.Shared(SoundDir());
        var fev = lib.Fev;

        // OBCT kind 3 = 39 groups, 12677 events total
        Assert.That(fev.Groups.Count, Is.EqualTo(39));
        Assert.That(fev.EventCount, Is.EqualTo(12677));

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
}
