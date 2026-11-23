using System.Reflection;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Look;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G26 — starter look itempreset (D11): GMS2 has no itemmodel/ folder; itempreset/1/12/11200001.xml is the model source.
/// game/scripts/character/ItemLook.cs StarterLook (linked into this project, as the Net tests link their game sources) reads
/// the item and model rows through Ms2.Ui.Items.ItemFiles, which reads the KMS files where the pack has them and the GMS2
/// per-item files otherwise. Before, it read the KMS files only: on GMS2 data no item had a genderLimit and the female look
/// wore the male base face 10300001, hair 10200001 and the male starter outfit.
/// </summary>
public class RcG26StarterLookTests
{
    static XElement Model(PackSet packs, int id)
    {
        var m = typeof(StarterLook).GetMethod("Model", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(m, Is.Not.Null, "StarterLook.Model");
        return (XElement)m.Invoke(null, new object[] { packs, id });
    }

    static string Asset(XElement model) =>
        (string)model?.Element("slots")?.Elements("slot").SelectMany(s => s.Elements("asset")).FirstOrDefault()?.Attribute("name");

    [Test]
    public void TheKmsItemModelIsUnchanged()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var model = Model(packs, 11200001);
        Assert.That(model, Is.Not.Null, "kms itemmodel/112.xml");
        Assert.That(Asset(model), Is.EqualTo("Data/Resource/Model/Item/1/12/11200001_C_Sapphire.nif"));
    }

    [Test]
    public void TheGms2StarterLookFindsThePresetModel()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var model = Model(packs, 11200001);
        Assert.That(model, Is.Not.Null, "gms/itempreset/1/12/11200001.xml");
        Assert.That(Asset(model), Is.EqualTo("Data/Resource/Model/Item/1/12/11200001_C_Sapphire.nif"));
        // and the Ui reader reads the same preset
        var asset = new Ms2.Ui.Items.ItemModelTable(packs).GetAssets(11200001).FirstOrDefault();
        Assert.That(asset?.Name, Is.EqualTo("Data/Resource/Model/Item/1/12/11200001_C_Sapphire.nif"));
    }

    [Test]
    public void TheKmsLooksFitTheirGender() => LooksFitTheirGender(RcGap.Data("MS2_DATA"));

    [Test]
    public void TheGms2LooksFitTheirGender() => LooksFitTheirGender(RcGap.Data("MS2_GMS2_DATA"));

    /// <summary>
    /// Both genders' look for every job of table/defaultitems.xml: the face and hair are worn, and every face, hair and
    /// outfit is in the data and fits the gender (itemdata genderLimit 2 or the gender).
    /// </summary>
    static void LooksFitTheirGender(string dir)
    {
        using var packs = new PackSet(dir);
        var jobs = Ms2.Formats.Text.PackXml.Parse(packs.Read("Xml", "table/defaultitems.xml")).Root.Elements("key")
            .Select(k => int.TryParse((string)k.Attribute("jobCode"), out int j) ? j : 0).Where(j => j != 0).Distinct().ToList();
        Assert.That(jobs, Is.Not.Empty);
        var palettes = new ColorPalettes(packs);
        var files = new Ms2.Ui.Items.ItemFiles(packs);
        var wrong = new List<string>();
        foreach (var job in jobs)
            for (int gender = 0; gender < 2; gender++)
            {
                var (items, _) = StarterLook.Build(packs, palettes, gender, job);
                foreach (var slot in new[] { EquipSlot.FA, EquipSlot.HR })
                    if (items.All(i => i.Slot != slot)) wrong.Add($"job {job} gender {gender}: no {slot}");
                foreach (var it in items.Where(i => i.Slot is EquipSlot.FA or EquipSlot.HR or EquipSlot.CL or EquipSlot.PA or EquipSlot.SH))
                {
                    var data = files.Item(it.Id);
                    if (data == null) { wrong.Add($"job {job} gender {gender}: {it.Slot} {it.Id} not in the data"); continue; }
                    var limit = data.Element("environment")?.Element("limit") ?? data.Descendants("limit").FirstOrDefault();
                    int g = int.TryParse((string)limit?.Attribute("genderLimit"), out int v) ? v : 2;
                    if (g != 2 && g != gender) wrong.Add($"job {job} gender {gender}: {it.Slot} {it.Id} is of gender {g}");
                }
            }
        Assert.That(wrong, Is.Empty);
    }
}
