using System.Xml.Linq;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>GMS2's item option rows (ItemOptionTables.Gms2Values) and the item file's option codes (ItemData), without data.</summary>
public class Gms2ItemOptionShapeTests
{
    // &lt;name&gt;_value_base for a value stat, &lt;name&gt;_rate_base for a rate stat in thousandths; the other kind, a 0, and a name that
    // is neither a stat nor a special are dropped
    [Test]
    public void ARowsValuesAreReadByKind()
    {
        var t = new ItemOptionTables(null, new(), new());
        var e = XElement.Parse(@"<option code=""11200001"" grade=""3"" mar_value_base=""4"" pen_rate_base=""0.054"" abp_rate_base=""0""
            wapmin_rate_base=""0.1"" ndd_value_base=""2"" pen_value_base=""7"" calibration_value_base=""9"" optionNumPick=""2,2"" />");
        Assert.That(t.Gms2Values(e), Is.EqualTo(new[] { (26, 0, 4), (31, 0, 54), (20, 0, 2) }), "mar 4, pen 0.054 -> 54, ndd 2");
    }

    // the GMS2 item files name the codes "constant" and "random"; the KMS rows' constantID and randomID win where present
    [Test]
    public void TheItemFilesOptionCodesAreReadFromEitherName()
    {
        ItemData Load(string option) => ItemData.Load(1, XElement.Parse($"<environment>{option}</environment>"));
        var gms = Load(@"<option static=""0"" random=""12"" constant=""11200001"" />");
        Assert.That((gms.ConstantId, gms.RandomId), Is.EqualTo((11200001, 12)));
        var kms = Load(@"<option constantID=""5"" randomID=""6"" constant=""7"" random=""8"" />");
        Assert.That((kms.ConstantId, kms.RandomId), Is.EqualTo((5, 6)));
    }
}
