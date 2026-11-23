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
