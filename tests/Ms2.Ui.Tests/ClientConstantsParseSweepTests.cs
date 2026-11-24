using System.Globalization;
using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Every constant the tables set, over the data of MS2_DATA (KMS2_DEV or KMS2) and MS2_GMS2_DATA (GMS2), read as the client
/// reads it: Constants::Lookup(int*) is atoi of the picked row's text (FUN_140718170, packetlogger.cpp:144) and
/// Lookup(float*) is (float)atof (FUN_140718450, :186), so a set key never falls back to the default
/// (GetOr, unit_1402ab440_constantsblocks.cpp:655-672). The tables write floats as "0.8f"; the strict parse this replaced
/// read those as the default.
/// </summary>
[NonParallelizable]
public class ClientConstantsParseSweepTests
{
    const int IntSentinel = int.MinValue + 7;
    const float FloatSentinel = -12345.5f;

    static readonly Regex IntPrefix = new(@"^[ \t\n\v\f\r]*([+-]?)(\d+)", RegexOptions.CultureInvariant);
    static readonly Regex FloatSuffix = new(@"^\s*[+-]?(?:\d+\.?\d*|\.\d+)f\s*$", RegexOptions.CultureInvariant);
    static readonly Regex FloatPrefix = new(@"^\s*([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)", RegexOptions.CultureInvariant);

    /// <summary>atoi as the MSVC CRT runs it (strtol, clamped to the 32-bit long).</summary>
    static int Atoi(string s)
    {
        var m = IntPrefix.Match(s);
        if (!m.Success) return 0;
        var v = System.Numerics.BigInteger.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        if (m.Groups[1].Value == "-") v = -v;
        return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
    }

    static float Atof(string s)
    {
        var m = FloatPrefix.Match(s);
        return m.Success ? (float)double.Parse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture) : 0f;
    }

    [TestCase("0.8f", 0, 0.8f)]
    [TestCase("150.f", 150, 150f)]
    [TestCase("", 0, 0f)]
    [TestCase("x", 0, 0f)]
    [TestCase(" 2", 2, 2f)]
    [TestCase("3abc", 3, 3f)]
    [TestCase("-4", -4, -4f)]
    [TestCase("1e3", 1, 1000f)]
    [TestCase(".5", 0, 0.5f)]
    [TestCase("+7.25", 7, 7.25f)]
    [TestCase("99999999999", int.MaxValue, 99999999999f)]
    public void ASetKeyReadsAsAtoiAndAtof(string text, int wantInt, float wantFloat)
    {
        var c = new ClientConstants(new Dictionary<string, string> { ["K"] = text });
        Assert.That(c.Int("K", IntSentinel), Is.EqualTo(wantInt));
        Assert.That(c.Float("K", FloatSentinel), Is.EqualTo(wantFloat));
        Assert.That((c.Int("Missing", IntSentinel), c.Float("Missing", FloatSentinel)), Is.EqualTo((IntSentinel, FloatSentinel)), "an unset key keeps the default");
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryConstantReadsAsTheClientReadsIt(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        using var packs = new PackSet(d);
        var c = FeatureElements.Constants(packs);
        var problems = new List<string>();
        var floatChanged = new List<string>();
        int keys = 0, intChanged = 0;
        foreach (var key in c.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var raw = c.String(key);
            if (raw == null) continue;
            keys++;
            int wantInt = Atoi(raw);
            float wantFloat = Atof(raw);
            int gotInt = c.Int(key, IntSentinel);
            float gotFloat = c.Float(key, FloatSentinel);
            if (gotInt != wantInt) problems.Add($"{key}=\"{raw}\": int {gotInt}, the client's {wantInt}");
            if (gotFloat != wantFloat) problems.Add($"{key}=\"{raw}\": float {gotFloat}, the client's {wantFloat}");
            // what the strict parse it replaced read
            int oldInt = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oi) ? oi : IntSentinel;
            float oldFloat = float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float of) ? of : FloatSentinel;
            // the tables' float spelling, "0.8f": the strict parse read every one as the default
            if (oldFloat != wantFloat && FloatSuffix.IsMatch(raw)) floatChanged.Add($"{key}=\"{raw}\"");
            if (oldInt != wantInt && IntPrefix.IsMatch(raw)) intChanged++;
        }
        TestContext.Progress.WriteLine($"{variable}: {keys} constants, {problems.Count} differences; {floatChanged.Count} written as \"<number>f\", " +
            $"which the strict parse read as the default: {string.Join(", ", floatChanged)}; {intChanged} with a leading number that is not a plain int");
        Assert.That(keys, Is.GreaterThan(0));
        Assert.That(problems, Is.Empty, string.Join("\n", problems.Take(60)));
    }
}
