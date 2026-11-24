using System.Globalization;
using System.Xml.Linq;
using Ms2.Audio;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// table/eventsound.xml as SoundPropertyMgr::LoadEventSound (soundproperty.cpp:591) stores it, GetEventVoice (:662) finds it
/// and CEffectService::PlayEventVoice (effectservice.cpp:1371) names the sound.
/// </summary>
[NonParallelizable]
public class EventSoundTableTests
{
    const string Xml = """
        <ms2>
          <note event="myPC_Resurrection" gender="0" job="1" onlyCasterMyPC="true" group="G" soundID="1"/>
          <eventSound event="myPC_Resurrection" gender="0" job="1" onlyCasterMyPC="true" group="MS2Sound/Voice/Male" soundID="90010006" restoreSoundID="90010007"/>
          <other event="myPC_Dead" gender="1" job=" 10x" onlyCasterMyPC="True" group="A/B" soundID="5" />
          <eventSound event="mypc_resurrection" gender="0" job="20" onlyCasterMyPC="true" group="G" soundID="6"/>
          <eventSound event="" gender="0" job="30" group="G" soundID="7"/>
          <eventSound event="Boss_Kill" gender="0" job="40" group="G"/>
          <eventSound event="Boss_Kill" gender="0" job="50" group="G" restoreSoundID="8"/>
          <eventSound event="skill_action" gender="1" job="1" onlyCasterMyPC="true" group="G" soundID="9"/>
          <eventSound event="skill_action" gender="1" job="1" onlyCasterMyPC="true" group="H" soundID="10"/>
        </ms2>
        """;

    [Test]
    public void TheRowsAreReadAsLoadEventSoundReadsThem()
    {
        var t = EventSoundTable.Parse(Xml);
        // the first element before <eventSound> is skipped; every element after it counts, whatever its name
        Assert.That(t.Get(EventSoundTable.MyPcResurrection, 0, 1, true),
            Is.EqualTo(new EventSoundTable.Voice(true, "ms2sound/voice/male", "90010006", "90010007")), "the group is lower-cased");
        // "True" is not "true" (strcmp); " 10x" is QueryIntAttribute's 10
        Assert.That(t.Get(EventSoundTable.MyPcDead, 1, 10, false), Is.EqualTo(new EventSoundTable.Voice(false, "a/b", "5", "")));
        Assert.That(t.Get(EventSoundTable.MyPcResurrection, 0, 20, true).SoundId, Is.Null, "the event name is case-sensitive");
        Assert.That(t.Get(0, 0, 30, false).SoundId, Is.Null, "an empty event is skipped");
        Assert.That(t.Get(EventSoundTable.BossKill, 0, 40, false).SoundId, Is.Null, "a row with neither id is skipped");
        Assert.That(t.Get(EventSoundTable.BossKill, 0, 50, false).RestoreSoundId, Is.EqualTo("8"), "a restore id alone is a row");
        Assert.That(t.Get(EventSoundTable.SkillAction, 1, 1, true).Group, Is.EqualTo("h"), "a later row replaces an earlier one");
        Assert.That(t.Count, Is.EqualTo(4));
    }

    [Test]
    public void PlayEventVoiceNamesTheSoundOrRestoreSoundAndKeepsCasterVoicesToTheOwnPc()
    {
        var t = EventSoundTable.Parse(Xml);
        int r = EventSoundTable.MyPcResurrection;
        Assert.That(t.VoicePath(r, 0, 1, true, 1, false), Is.EqualTo("ms2sound/voice/male/90010006"));
        Assert.That(t.VoicePath(r, 0, 1, true, 1, true), Is.EqualTo("ms2sound/voice/male/90010007"), "the job sound off plays restoreSoundID");
        Assert.That(t.VoicePath(r, 0, 1, true, 3, false), Is.Null, "onlyCasterMyPC needs object kind 1");
        Assert.That(t.VoicePath(r, 0, 1, false, 3, false), Is.Null, "another PC reads index 0, which no row fills");
        Assert.That(t.VoicePath(r, 1, 1, true, 1, false), Is.Null, "no row for the gender");
        Assert.That(t.VoicePath(EventSoundTable.MyPcDead, 1, 10, false, 3, false), Is.EqualTo("a/b/5"), "a voice that is not caster-only plays for anyone");
        Assert.That(t.VoicePath(EventSoundTable.MyPcDead, 1, 10, false, 3, true), Is.Null, "an empty restore id plays nothing");
    }

    [TestCase("Boss_Kill", 1)] [TestCase("Boss_EndKill", 2)] [TestCase("otherPC_Kill", 3)] [TestCase("myPC_Resurrection", 4)]
    [TestCase("myPC_Dead", 5)] [TestCase("skill_action", 6)] [TestCase("boss_kill", 0)] [TestCase("", 0)]
    public void TheEventNamesAreFun141793500s(string name, int type) => Assert.That(EventSoundTable.TypeOf(name), Is.EqualTo(type));

    /// <summary>
    /// Every row of the data's table, against its raw XML: each row is found under its key with its own values (or a later
    /// row's), and each voice the client can play (onlyCasterMyPC rows for the local player, kind 1) names a sound event the
    /// FEV project has, both its soundID and its restoreSoundID.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryRowIsStoredAndEveryVoiceNamesASoundEvent(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        using var packs = new PackSet(d);
        var text = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/eventsound.xml"));
        var t = EventSoundTable.Parse(text);
        var soundDir = Path.Combine(d, "Sound");
        var lib = File.Exists(Path.Combine(soundDir, "MS2Sound.fev")) ? new SoundLibrary(soundDir) : null;

        // the raw rows by key, last one winning
        var rows = new Dictionary<(int, int, int, bool), XElement>();
        int total = 0;
        foreach (var e in XDocument.Parse(text.TrimStart('﻿')).Root!.Elements("eventSound"))
        {
            total++;
            int type = EventSoundTable.TypeOf((string)e.Attribute("event") ?? "");
            Assert.That(type, Is.Not.Zero, $"row {e}: an unknown event");
            rows[(type, int.Parse((string)e.Attribute("gender")!, CultureInfo.InvariantCulture), int.Parse((string)e.Attribute("job")!, CultureInfo.InvariantCulture),
                (string)e.Attribute("onlyCasterMyPC") == "true")] = e;
        }
        var problems = new List<string>();
        var byType = new Dictionary<int, int>();
        int voices = 0, events = 0;
        foreach (var ((type, gender, job, caster), e) in rows.OrderBy(r => r.Key))
        {
            var want = new EventSoundTable.Voice(caster, ((string)e.Attribute("group") ?? "").ToLowerInvariant(), (string)e.Attribute("soundID") ?? "", (string)e.Attribute("restoreSoundID") ?? "");
            var got = t.Get(type, gender, job, caster);
            if (got != want) { problems.Add($"{type}/{gender}/{job}/{caster}: {got}, the XML {want}"); continue; }
            byType[type] = byType.GetValueOrDefault(type) + 1;
            foreach (bool off in new[] { false, true })
            {
                var path = t.VoicePath(type, gender, job, true, 1, off);
                string name = off ? want.RestoreSoundId : want.SoundId;
                if (name.Length == 0 ? path != null : path != want.Group + "/" + name) { problems.Add($"{type}/{gender}/{job}: path {path}"); continue; }
                if (path == null) continue;
                voices++;
                if (caster && t.VoicePath(type, gender, job, true, 3, off) != null) problems.Add($"{type}/{gender}/{job}: plays for object kind 3");
                if (lib == null) continue;
                int cut = path.LastIndexOf('/');
                if (lib.Fev.FindEvent(path[(cut + 1)..], path[..cut]) == null) problems.Add($"{type}/{gender}/{job}: no sound event {path}");
                else events++;
            }
        }
        TestContext.Progress.WriteLine($"{variable}: {total} rows, {rows.Count} keys ({string.Join(", ", byType.OrderBy(k => k.Key).Select(k => $"type {k.Key} {k.Value}"))}), " +
            $"{voices} voices, {(lib == null ? "no FEV" : $"{events} found in the FEV")}, {problems.Count} differences");
        Assert.That(rows, Is.Not.Empty);
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }

    [Test]
    public void ASkillTextKeyNamesItsEventAfterElevenCharacters()
    {
        Assert.That(EventSoundTable.TextKeyType("EventSound/skill_action"), Is.EqualTo(EventSoundTable.SkillAction));
        Assert.That(EventSoundTable.TextKeyType("eventsound/skill_action"), Is.EqualTo(EventSoundTable.SkillAction), "ContainsNoCase");
        Assert.That(EventSoundTable.TextKeyType("EventSound/Skill_Action"), Is.EqualTo(0), "the name compares case-sensitively");
        Assert.That(EventSoundTable.TextKeyType("EventSound"), Is.EqualTo(0), "Mid(11) of a shorter key is empty");
        Assert.That(EventSoundTable.TextKeyType("skill_action"), Is.EqualTo(0), "no EventSound in the key");
        Assert.That(EventSoundTable.TextKeyType(null), Is.EqualTo(0));
    }

    /// <summary>
    /// Every voice the own PC can play resolves to a wave in the voice banks of the data's own voice language
    /// (MS2Sound_Voice*_&lt;lang&gt;.fsb): KMS ships the Korean banks (voice 0), GMS2 only the English ones (voice 2, the NA
    /// region's index GameOptions takes for the voice).
    /// </summary>
    [TestCase("MS2_DATA", 0)]
    [TestCase("MS2_GMS2_DATA", 2)]
    public void EveryVoicePlaysAWaveOfTheDatasVoiceLanguage(string variable, int voiceLanguage)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var soundDir = Path.Combine(d, "Sound");
        if (!File.Exists(Path.Combine(soundDir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {variable}");
        using var packs = new PackSet(d);
        var text = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/eventsound.xml"));
        var t = EventSoundTable.Parse(text);
        // KMS ships Korea voice banks (voice 0); GMS2 ships EN voice banks (voice 2, NA region default).
        int effectiveVoice = AudioData.IsKr(packs) ? voiceLanguage : 2;
        var lib = SoundLibrary.Shared(soundDir, effectiveVoice);
        var paths = new HashSet<string>();
        foreach (var e in XDocument.Parse(text.TrimStart('\uFEFF')).Root!.Elements("eventSound"))
        {
            int type = EventSoundTable.TypeOf((string)e.Attribute("event") ?? "");
            int gender = int.Parse((string)e.Attribute("gender")!, CultureInfo.InvariantCulture), job = int.Parse((string)e.Attribute("job")!, CultureInfo.InvariantCulture);
            foreach (bool off in new[] { false, true })
                if (t.VoicePath(type, gender, job, true, 1, off) is { } path) paths.Add(path);
        }
        var missing = paths.Where(p => lib.ResolveEvent(p) == null).OrderBy(p => p).ToList();
        TestContext.Progress.WriteLine($"{variable}: voice language {voiceLanguage}, {paths.Count} voices, {paths.Count - missing.Count} play a wave, {missing.Count} do not");
        Assert.That(paths, Is.Not.Empty);
        Assert.That(missing, Is.Empty, string.Join("\n", missing.Take(40)));
    }
}
