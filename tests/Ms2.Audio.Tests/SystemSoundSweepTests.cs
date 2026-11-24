using Ms2.Audio;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// table/systemSound.xml as CEffectService::PlaySystemSound (@0x1411a5a60, effectservice.cpp:1288) plays it, over the data of
/// MS2_DATA (KMS2_DEV or KMS2, the KR region) and MS2_GMS2_DATA (GMS2, the NA region). Every row is resolved for each
/// gender (0, 1) and each job group the row names (and 1, the job without a local player), with the job sound on and off
/// (IsJobSoundDisabled, the KR region's option). Each name the row gives must be an event at exactly the row's category
/// (GetCategory: the group, plus "/Male" or "/Female" on a byGender row), and every wave of that event must be in the
/// banks of the data's voice language. The voice is the region's (a fresh GameOption.xml sets the voice to the region
/// index, gameoptionstruct.cpp:2466-2490: KR 0, NA 2), which CSoundService passes to EventSystem::setLanguage ("Korea",
/// "en"; soundservice.cpp:418-434), so the banks are MS2Sound_*_Kor_def.fsb on KMS and MS2Sound_*_en.fsb on GMS2.
/// </summary>
[NonParallelizable]
public class SystemSoundSweepTests
{
    static readonly int[] JobGroups = { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110 };

    [TestCase("MS2_DATA", 0)]
    [TestCase("MS2_GMS2_DATA", 2)]
    public void EveryRowResolvesToAWaveInTheVoiceBanks(string variable, int voice)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var soundDir = Path.Combine(d, "Sound");
        if (!File.Exists(Path.Combine(soundDir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {variable}");
        using var packs = new PackSet(d);
        var table = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml")));
        // KMS ships Korea voice banks (voice 0); GMS2 ships EN voice banks (voice 2, NA region default).
        int effectiveVoice = TablePath.Region(e => packs.Exists("Xml", e)) == "kr" ? voice : 2;
        var lib = new SoundLibrary(soundDir, effectiveVoice);
        var problems = new List<string>();
        var gaps = new List<string>();
        int rows = 0, byGender = 0, byJob = 0, voiceRows = 0, silent = 0, names = 0, waves = 0;
        var checkedPaths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, p) in table.Rows)
        {
            rows++;
            if (p.ByGender) byGender++;
            if (p.ByJob) byJob++;
            if (p.Group.Contains("/Voice/", StringComparison.OrdinalIgnoreCase)) voiceRows++;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (int gender in new[] { 0, 1 })
                foreach (int job in JobGroups.Concat(p.JobSounds.Keys).Distinct())
                    foreach (bool off in new[] { false, true })
                        if (table.Resolve(key, gender, job, off) is { } path) paths.Add(path);
            if (paths.Count == 0) { silent++; continue; }
            foreach (var path in paths)
            {
                names++;
                if (checkedPaths.TryGetValue(path, out bool ok)) { if (!ok) problems.Add($"{key}: {path} (as above)"); continue; }
                int cut = path.LastIndexOf('/');
                var ev = lib.Fev.FindEvent(path[(cut + 1)..], path[..cut]);
                string fail = null;
                if (ev == null)
                {
                    // no event of the name in any group: the client's EventSystem::getEvent fails as well and nothing plays
                    // (a data gap of the table, the same on KMS2 and GMS2; "02100105 " keeps its trailing space as the client does)
                    gaps.Add($"{key}: {path}");
                    checkedPaths[path] = true;
                    continue;
                }
                if (!ev.Group.Equals(path[..cut], StringComparison.OrdinalIgnoreCase))
                    fail = $"no event in the group (the name is in {ev.Group})";
                else
                {
                    var all = ev.Defs.SelectMany(i => lib.Fev.Defs[i].Entries).Where(e => e.Kind == 0).ToList();
                    if (all.Count == 0) fail = "the event has no wave";
                    foreach (var w in all)
                        if (lib.FromWave(w) == null) fail = $"wave {w.Wave} is in no {lib.LanguageName} bank";
                        else waves++;
                }
                checkedPaths[path] = fail == null;
                if (fail != null) problems.Add($"{key}: {path}: {fail}");
            }
        }
        TestContext.Progress.WriteLine($"{variable} (voice {lib.LanguageName}): {rows} rows, {byGender} byGender, {byJob} byJob, {voiceRows} in a voice group, "
            + $"{silent} with no sound; {names} names over the genders and jobs ({checkedPaths.Count} distinct events), {waves} waves found; {gaps.Count} names with no event in the FEV ({string.Join(", ", gaps)}); {problems.Count} differences");
        Assert.That(gaps.Count, Is.LessThanOrEqualTo(4), "more names with no event than the table's known four:\n" + string.Join("\n", gaps));
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(80)));
    }

    [TestCase("MS2_DATA", 0)]
    [TestCase("MS2_GMS2_DATA", 2)]
    public void TheBagFullSoundIsTheJobsVoiceOfEachGender(string variable, int voice)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        var soundDir = Path.Combine(d, "Sound");
        if (!File.Exists(Path.Combine(soundDir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {variable}");
        using var packs = new PackSet(d);
        var table = SystemSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/systemsound.xml")));
        // KMS ships Korea voice banks (voice 0); GMS2 ships EN voice banks (voice 2, NA region default).
        int effectiveVoice = TablePath.Region(e => packs.Exists("Xml", e)) == "kr" ? voice : 2;
        var lib = new SoundLibrary(soundDir, effectiveVoice);
        const string key = "system_notice_error_warningBag";
        // a male priest (job group 40) and a female beginner (1), then the job sound off: the soundIDs by gender
        Assert.That(table.Resolve(key, 0, 40), Is.EqualTo("MS2Sound/Sound/Voice/ko/Normal/Male/90130063"));
        Assert.That(table.Resolve(key, 1, 1), Is.EqualTo("MS2Sound/Sound/Voice/ko/Normal/Female/91000044"));
        Assert.That(table.Resolve(key, 1, 40, jobSoundDisabled: true), Is.EqualTo("MS2Sound/Sound/Voice/ko/Normal/Female/91000044"));
        foreach (var path in new[] { table.Resolve(key, 0, 40), table.Resolve(key, 1, 1), table.Resolve(key, 0, 100) })
        {
            var clip = lib.ResolveEvent(path);
            Assert.That(clip, Is.Not.Null, path);
            TestContext.Progress.WriteLine($"{variable}: {path} -> {clip.Name} ({lib.LanguageName})");
        }
    }
}

/// <summary>SystemSoundProperty::Load / GetCategory / GetSoundName, SoundPropertyMgr::FindSystemSound on small tables.</summary>
public class SystemSoundPropertyTests
{
    const string Xml = """
        <ms2>
          <other key="before" group="G" soundID="1" />
          <systemSound key="plain" group="MS2Sound/Sound/system" soundID="02000002,02000003" />
          <systemSOund key="gender" byGender="1" group="V" soundID="90000072,91000070" />
          <systemSound key="job" byGender="true" byJob="Yes" group="V" soundID="90000044,91000044" jobSoundID="1:90000044,91000044;40:90130063,91130063;50:x;60:a,b,c;7x:70a,70b;" cooltimeID="25" />
          <systemSound key="jobOnly" byJob="1" group="V" soundID="1,2" jobSoundID="40:3,4" />
          <systemSound key="zero" group="G" soundID="0" />
          <systemSound key="shop_open" group="G" soundID="5" />
          <systemSound key="PLAIN" group="G" soundID="9" />
        </ms2>
        """;

    [Test]
    public void RowsFromTheFirstSystemSoundOnAnyNameLaterKeyWins()
    {
        var t = SystemSoundTable.Parse(Xml);
        Assert.That(t.Find("before"), Is.Null);
        Assert.That(t.Find("gender"), Is.Not.Null, "the misspelt element is still a row");
        Assert.That(t.Resolve("plain"), Is.EqualTo("G/9"), "keys ignore case; the later row replaces the earlier");
    }

    [Test]
    public void GenderRowsTakeTheGendersSoundAndCategory()
    {
        var t = SystemSoundTable.Parse(Xml);
        Assert.That(t.Resolve("gender", 0, 40), Is.EqualTo("V/Male/90000072"));
        Assert.That(t.Resolve("gender", 1, 40), Is.EqualTo("V/Female/91000070"));
        Assert.That(t.Resolve("gender", 2, 40), Is.Null);
        Assert.That(t.Find("gender").Category(2), Is.EqualTo("V"));
    }

    [Test]
    public void JobRowsTakeTheJobsPairUnlessTheJobSoundIsOff()
    {
        var t = SystemSoundTable.Parse(Xml);
        var p = t.Find("job");
        Assert.That(p.JobSounds.Keys, Is.EquivalentTo(new[] { 1, 40, 7 }), "an entry without two parts or two names is skipped; the job is atoi");
        Assert.That(p.CooltimeId, Is.EqualTo(25u));
        Assert.That(t.Resolve("job", 0, 40), Is.EqualTo("V/Male/90130063"));
        Assert.That(t.Resolve("job", 1, 40), Is.EqualTo("V/Female/91130063"));
        Assert.That(t.Resolve("job", 1, 40, jobSoundDisabled: true), Is.EqualTo("V/Female/91000044"));
        Assert.That(t.Resolve("job", 0, 90), Is.EqualTo("V/Male/90000044"), "a job without a pair falls to the soundIDs");
        Assert.That(t.Find("jobOnly").JobSounds, Is.Empty, "jobSoundID is read only on a byGender and byJob row");
        Assert.That(t.Resolve("jobOnly", 1, 40), Is.EqualTo("V/1"));
    }

    [Test]
    public void ZeroIsNoSoundAndTheArgumentPrefixesTheKey()
    {
        var t = SystemSoundTable.Parse(Xml);
        Assert.That(t.Resolve("zero"), Is.Null);
        Assert.That(t.Resolve("open", arg: "shop"), Is.EqualTo("G/5"));
        Assert.That(t.Find("plain", "shop"), Is.Null, "a missing arg_name gives no row even when the plain name has one");
    }
}
