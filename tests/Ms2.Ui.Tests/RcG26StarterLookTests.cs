using System.Reflection;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G26 — starter look itempreset (D11): GMS2 has no itemmodel/ folder; itempreset/1/12/11200001.xml is the model source, and
/// game/scripts/character/ItemLook.cs StarterLook.Find reads itemmodel/&lt;prefix&gt;.xml only (the file is linked into
/// this project, as the Net tests link their game sources). The Ui reader ItemModelTable already falls back to
/// PerFileTable.ItemModels.
/// </summary>
public class RcG26StarterLookTests
{
    static XElement Find(PackSet packs, int id)
    {
        var m = typeof(Ms2Godot.Look.StarterLook).GetMethod("Find", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(m, Is.Not.Null, "StarterLook.Find");
        return (XElement)m.Invoke(null, new object[] { packs, new Dictionary<int, XElement>(), "itemmodel", id, "ItemModel" });
    }

    static string Asset(XElement model) =>
        (string)model?.Element("slots")?.Elements("slot").SelectMany(s => s.Elements("asset")).FirstOrDefault()?.Attribute("name");

    [Test]
    public void TheKmsItemModelIsUnchanged()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var model = Find(packs, 11200001);
        Assert.That(model, Is.Not.Null, "kms itemmodel/112.xml");
        Assert.That(Asset(model), Is.EqualTo("Data/Resource/Model/Item/1/12/11200001_C_Sapphire.nif"));
    }

    [Test]
    [Ignore("G26: ItemLook.Find reads itemmodel/<prefix>.xml only; the GMS2 itempreset fallback is unread")]
    public void TheGms2StarterLookFindsThePresetModel()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var model = Find(packs, 11200001);
        Assert.That(model, Is.Not.Null, "gms/itempreset/1/12/11200001.xml");
        Assert.That(Asset(model), Is.EqualTo("Data/Resource/Model/Item/1/12/11200001_C_Sapphire.nif"));
        // and the Ui reader already reads the same preset
        var asset = new Ms2.Ui.Items.ItemModelTable(packs).GetAssets(11200001).FirstOrDefault();
        Assert.That(asset?.Name, Is.EqualTo("Data/Resource/Model/Item/1/12/11200001_C_Sapphire.nif"));
    }
}
