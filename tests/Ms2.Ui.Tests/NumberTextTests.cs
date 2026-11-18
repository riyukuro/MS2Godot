using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class NumberTextTests
{
    [TestCase("100", "100")]
    [TestCase("840", "840")]
    [TestCase("1234567", "1,234,567")]
    [TestCase("-5", "-5")]
    [TestCase("-1234", "-1,234")]
    [TestCase("-123", "-,123")]
    [TestCase("12.5", "12.5")]
    [TestCase("1234.5", "1,234.5")]
    public void Meso(string s, string want) => Assert.AreEqual(want, NumberText.MesoText(s));

    [TestCase("100", "100")]
    [TestCase("840", "840")]
    [TestCase("1234567", "1234567")]
    [TestCase("1,234", "1234")]
    [TestCase("-5", "-5")]
    [TestCase("12.5", "12.5")]
    [TestCase("abc", "0")]
    public void Numeric(string s, string want) => Assert.AreEqual(want, NumberText.NumericText(s));
}
