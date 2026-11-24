using System.Reflection;
using NUnit.Framework;

namespace Ms2.Quest.Tests;

/// <summary>
/// Helpers for the GMS2 data-gap tests: the data environment variables and a reader of a member a later change
/// still has to add. A member that does not exist at origin/main makes the test fail with the gap's reason, so the ignored test
/// compiles on main and passes once the member is added.
/// </summary>
static class RcGap
{
    const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;

    public static string Data(string variable)
    {
        var d = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{variable} is not set");
        return d;
    }

    /// <summary>True when <paramref name="packs"/> is KMS data (table/kr/ layout).</summary>
    public static bool IsKr(Ms2.Formats.Pack.PackSet packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        return Ms2.Formats.Pack.TablePath.Region(e => packs.Exists("Xml", e)) == "kr";
    }

    /// <summary>
    /// The KMS data for a KMS-only test: MS2_DATA when it is KMS data (TablePath.Region == "kr"), else MS2_KMS_DATA when
    /// set; otherwise inconclusive via Assume.That because these tests read KMS-only files or tables that GMS2 does not ship.
    /// </summary>
    public static string KmsData()
    {
        var d = Data("MS2_DATA");
        using (var packs = new Ms2.Formats.Pack.PackSet(d))
            if (IsKr(packs)) return d;
        var kms = Environment.GetEnvironmentVariable("MS2_KMS_DATA");
        if (!string.IsNullOrEmpty(kms) && Directory.Exists(kms)) return kms;
        Assume.That(false, "test needs the KMS data and MS2_KMS_DATA is unset");
        return null;
    }

    public static object Member(object o, string name)
    {
        var t = o?.GetType() ?? throw new AssertionException("the reader returned no row");
        var p = t.GetProperty(name, Members);
        if (p != null) return p.GetValue(o);
        var f = t.GetField(name, Members);
        if (f != null) return f.GetValue(o);
        Assert.Fail($"{t.Name}.{name} is not read yet at origin/main");
        return null;
    }

    public static T Member<T>(object o, string name) => (T)Member(o, name);

    /// <summary>A private field of a reader (the BoldWords tables the gap's fixtures are read into).</summary>
    public static T Field<T>(object o, string name)
    {
        var f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (f == null) Assert.Fail($"{o.GetType().Name}.{name} does not exist at origin/main");
        return (T)f.GetValue(o);
    }
}
