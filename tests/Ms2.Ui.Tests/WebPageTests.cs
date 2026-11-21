using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class WebPageTests
{
    [TestCase("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [TestCase("  http://example.com  ", "http://example.com/")]
    public void HttpAddressesOpen(string url, string expected) => Assert.That(WebPage.Target(url), Is.EqualTo(expected));

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    [TestCase("file:///etc/passwd")]
    [TestCase("javascript:alert(1)")]
    [TestCase("example.com/no-scheme")]
    public void AnythingElseOpensNothing(string url) => Assert.That(WebPage.Target(url), Is.Null);
}
