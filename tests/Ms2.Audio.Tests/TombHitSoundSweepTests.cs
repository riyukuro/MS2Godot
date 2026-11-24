using Ms2.Audio;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// The tombs' hit sound over the data of MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2): both tomb flats
/// (MS2TombStone, and MS2TombStoneDark while the revival is blocked, PC::FUN_14165cac0) give FUN_140adf9d0 their
/// SoundMaterial (m_nHitSound, breakableobject.cpp:584), and FUN_140ade030 plays PlayHitSound(hitter, m_nHitSound) with
/// the hitter the servers send and the tag pass uses, 1 (worldservice_1.cpp:9367). Each of the pair's hit sound ids
/// names a sound event "ms2sound/sound/attack/hit/%08d" in the FEV project.
/// </summary>
[NonParallelizable]
public class TombHitSoundSweepTests
{
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryTombFlatsHitSoundNamesASoundEvent(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        using var packs = new PackSet(d);
        var table = HitSoundTable.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/hitsound.xml")));
        var soundDir = Path.Combine(d, "Sound");
        var lib = File.Exists(Path.Combine(soundDir, "MS2Sound.fev")) ? new SoundLibrary(soundDir) : null;
        var flats = new FlatLibrary(packs);
        var problems = new List<string>();
        var lines = new List<string>();
        foreach (var name in new[] { "MS2TombStone", "MS2TombStoneDark" })
        {
            var flat = flats.Get(name);
            if (flat == null) { problems.Add($"{name}: no flat"); continue; }
            int material = flat.Props.TryGetValue("SoundMaterial", out var s) && int.TryParse(s, out int m) ? m : 0;
            // every id of the pair: the picks for 0 .. n-1 cover them all
            var ids = new HashSet<int>();
            for (uint r = 0; r < 64; r++) { int id = table.Get(1, material, () => r); if (id != 0) ids.Add(id); }
            if (ids.Count == 0) problems.Add($"{name}: SoundMaterial {material} has no hit sound for attack material 1");
            int found = 0;
            foreach (int id in ids)
            {
                var path = HitSoundTable.EventPath(id);
                int cut = path.LastIndexOf('/');
                if (lib == null) continue;
                if (lib.Fev.FindEvent(path[(cut + 1)..], path[..cut]) == null) problems.Add($"{name}: no sound event {path}");
                else found++;
            }
            lines.Add($"{name} SoundMaterial {material}: {ids.Count} hit sounds ({string.Join(", ", ids.OrderBy(i => i))}), {(lib == null ? "no FEV" : $"{found} in the FEV")}");
        }
        TestContext.Progress.WriteLine($"{variable}: {string.Join("; ", lines)}; {problems.Count} differences");
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}
