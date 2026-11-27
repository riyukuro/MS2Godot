using System.Xml.Linq;
using Ms2.Audio;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// A dot's hit sound: &lt;DotDamage soundMaterial&gt; (m_dotDamage.m_nSoundMaterial, additionalactionadditional.cpp:1284; GMS2's
/// DotDamageProperty) is the attack material PC::vfunc_34 (pc.cpp:990-998) and CNpc::vfunc_34
/// (unit_14108e1e0_npcactorandvibrate.cpp:1908) give PlayHitSound for a dot tick, against the PC's defence material 1 or the
/// npc's m_nDefenceMaterial. The data sweep reads every effect of MS2_DATA and MS2_GMS2_DATA.
/// </summary>
[NonParallelizable]   // GameTables hooks are process-wide
public class DotSoundMaterialTests
{
    [Test]
    public void TheDotsSoundMaterialIsReadInBothForms()
    {
        var kms = AdditionalData.Load(XElement.Parse(
            "<additional id=\"7\"><level level=\"1\"><Basic durationTick=\"5000\"/><DotDamage type=\"2\" rate=\"0.1\" soundMaterial=\"100\"/></level>" +
            "<level level=\"2\"><Basic durationTick=\"5000\"/></level></additional>"), _ => true);
        Assert.That((kms.GetLevel(1).DotSoundMaterial, kms.GetLevel(2).DotSoundMaterial), Is.EqualTo((100, 0)), "0 without the element");
        var gms2 = AdditionalData.Load(AdditionalTable.FromGms2(8, XElement.Parse(
            "<ms2><level><BasicProperty level=\"1\" buffType=\"2\"/><DotDamageProperty type=\"1\" soundMaterial=\"104\"/></level></ms2>")), _ => true);
        Assert.That(gms2.GetLevel(1).DotSoundMaterial, Is.EqualTo(104));
    }

    /// <summary>
    /// Every effect level's dot sound material, with the PC's defence material and every npc row's: each pair the table has
    /// names a sound event; every material the table has rows for pairs with the PC's or some npc's defence material (a material
    /// whose rows have no sound id is silent, in the client too).
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryDotSoundMaterialHasItsHitSounds(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        using var packs = new PackSet(d);
        var soundDir = Path.Combine(d, "Sound");
        if (!File.Exists(Path.Combine(soundDir, "MS2Sound.fev"))) Assert.Ignore($"no Sound/MS2Sound.fev under {variable}");
        var lib = SoundLibrary.Shared(soundDir);
        var hitXml = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/hitsound.xml"));
        var hits = HitSoundTable.Parse(hitXml);
        // the attack materials with a row that has a sound id: every row of another (1000's all have soundID="") gives 0 from
        // GetHitSound, and PlayHitSound plays nothing for 0 (effectservice.cpp:1355-1357), the same in the client
        var rowMaterials = XDocument.Parse(hitXml.TrimStart('\uFEFF')).Root!.Elements("hitSound")
            .Where(e => int.TryParse((string)e.Attribute("soundID"), out int id) && id != 0)
            .Select(e => int.TryParse((string)e.Attribute("AttackMaterial"), out int v) ? v : 0).ToHashSet();
        var table = GameTables.NewAdditionalTable(packs);

        var ids = new SortedSet<uint>();
        foreach (var n in packs.List("Xml").Where(n => n.StartsWith("additional/", StringComparison.OrdinalIgnoreCase) && n.IndexOf('/', 11) < 0))
            foreach (var e in GameStrings.Parse(packs.Read("Xml", n)).Root?.Elements("additional") ?? Enumerable.Empty<XElement>())
                if (uint.TryParse((string)e.Attribute("id"), out var id)) ids.Add(id);
        if (ids.Count == 0)
            foreach (var n in packs.List("Xml").Where(n => n.StartsWith(AdditionalTable.Gms2Folder, StringComparison.OrdinalIgnoreCase)
                && n.IndexOf('/', AdditionalTable.Gms2Folder.Length) < 0 && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                if (uint.TryParse(Path.GetFileNameWithoutExtension(n), out var id)) ids.Add(id);
        Assert.That(ids.Count, Is.GreaterThan(1000));

        var materials = new SortedDictionary<int, int>();   // material -> levels
        foreach (var id in ids)
            if (table.GetAdditional(id) is { } a)
                foreach (var l in a.Levels)
                    if (l.DotSoundMaterial != 0) materials[l.DotSoundMaterial] = materials.GetValueOrDefault(l.DotSoundMaterial) + 1;

        var defences = new SortedSet<int> { CombatVoice.PcDefenceMaterial };
        foreach (var r in new NpcTable(packs).Rows) defences.Add(r.DefenceMaterial);

        var problems = new List<string>();
        var lines = new List<string>();
        var silent = new List<string>();
        foreach (var (m, levels) in materials)
        {
            if (!rowMaterials.Contains(m)) { silent.Add($"{m} ({levels} levels)"); continue; }
            int pairs = 0, events = 0;
            bool pc = false;
            foreach (int def in defences)
            {
                var sounds = new HashSet<int>();
                for (uint r = 0; r < 64; r++) { int s = hits.Get(m, def, () => r); if (s != 0) sounds.Add(s); }
                if (sounds.Count == 0) continue;
                pairs++;
                if (def == CombatVoice.PcDefenceMaterial) pc = true;
                foreach (int s in sounds)
                {
                    var path = HitSoundTable.EventPath(s);
                    int cut = path.LastIndexOf('/');
                    if (lib.Fev.FindEvent(path[(cut + 1)..], path[..cut]) == null) problems.Add($"material {m} defence {def}: no sound event {path}");
                    else events++;
                }
            }
            if (pairs == 0) problems.Add($"material {m} ({levels} levels): the table's rows for it match no PC or npc defence material");
            lines.Add($"{m}: {levels} levels, {pairs} defence pairs{(pc ? " (the PC's too)" : "")}, {events} events");
        }
        TestContext.Progress.WriteLine($"{variable}: {materials.Count} dot sound materials over {defences.Count} defence materials; {string.Join("; ", lines)}; "
            + $"silent (no row with a sound id, none in the client either): {string.Join(", ", silent)}; {problems.Count} problems");
        Assert.That(materials, Is.Not.Empty, "the levels' dot sound materials are read");
        Assert.That(lines, Is.Not.Empty, "some dot material has hit sounds");
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }
}
