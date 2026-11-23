using System.Collections;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Test helpers: a member, method or type that is not implemented yet is looked up by name, so every file
/// compiles on main and an ignored test fails with "not read yet". Each test names the member it expects;
/// the implementation may pick other names and adapt the test.
/// </summary>
static class RcLane
{
    const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static object Member(object o, string path) => TryMember(o, path, out var v) ? v : Missing(o, path);

    public static T Member<T>(object o, string path) => (T)Member(o, path);

    public static object MemberAny(object o, params string[] paths)
    {
        foreach (var path in paths) if (TryMember(o, path, out var v)) return v;
        return Missing(o, string.Join(" / ", paths));
    }

    public static T MemberAny<T>(object o, params string[] paths) => (T)MemberAny(o, paths);

    /// <summary>Sets a public field or property the implementation adds.</summary>
    public static void Set(object o, string name, object value)
    {
        var t = o.GetType();
        var p = t.GetProperty(name, MemberFlags);
        if (p != null && p.CanWrite) { p.SetValue(o, value); return; }
        var f = t.GetField(name, MemberFlags);
        if (f != null && !f.IsInitOnly) { f.SetValue(o, value); return; }
        Assert.Fail($"{t.FullName}.{name} is not written yet at origin/main");
    }

    /// <summary>A public static method the implementation adds.</summary>
    public static object Static(string typeName, string method, params object[] args)
    {
        var t = System.Type.GetType(typeName) ?? typeof(Ms2.Formats.World.DropPickup).Assembly.GetType(typeName);
        if (t == null) Assert.Fail($"{typeName} is not implemented at origin/main");
        var m = t.GetMethods(StaticFlags).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
        if (m == null) Assert.Fail($"{typeName}.{method}({args.Length} args) is not implemented at origin/main");
        return m.Invoke(null, args);
    }

    public static T Static<T>(string typeName, string method, params object[] args) => (T)Static(typeName, method, args);

    static object Missing(object o, string path)
    {
        Assert.Fail($"{(o?.GetType().FullName ?? "the reader's null row")}.{path} is not read yet at origin/main");
        return null;
    }

    static bool TryMember(object o, string path, out object value)
    {
        value = o;
        if (o == null) return false;
        foreach (var token in path.Split('.'))
        {
            if (!TryOne(ref value, token)) return false;
        }
        return true;
    }

    static bool TryOne(ref object o, string name)
    {
        var t = o.GetType();
        var p = t.GetProperty(name, MemberFlags);
        if (p != null) { o = p.GetValue(o); return true; }
        var f = t.GetField(name, MemberFlags);
        if (f != null) { o = f.GetValue(o); return true; }
        return false;
    }
}
