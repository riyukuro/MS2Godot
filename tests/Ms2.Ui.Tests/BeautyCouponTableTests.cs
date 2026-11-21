using System.Xml.Linq;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class BeautyCouponTableTests
{
    [Test] public void ShopsKeepTheirKnownCouponItems() {
        var doc = XDocument.Parse("<ms2><shop shopID='5'><item id='1'/><item id='2'/><item id='0'/><item id='3' skip='1'/></shop><shop shopID='0'><item id='1'/></shop><shop shopID='5'><item id='2'/></shop></ms2>");
        var t = new BeautyCouponTable(doc, e => e.Attribute("skip") == null, id => id != 1);
        Assert.That(t.CouponsOf(5), Is.EqualTo(new[] { 2u }));    // the later shop 5 replaces the earlier
        Assert.That(t.CouponsOf(0), Is.Empty);
        Assert.That(t.CouponsOf(9), Is.Empty);
    }
}
