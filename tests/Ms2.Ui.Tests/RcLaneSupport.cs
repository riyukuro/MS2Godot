using System.Collections;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Helpers for the implementation tests: a member the implementation still has to add is looked up by name, so every file compiles on origin/main
/// and an ignored test fails with "not read yet". Expected names are named in each test and in
///; the implementation may adapt both when it picks other names.
/// </summary>
static class RcLane
{
    const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static Type Type(string fullName)
    {
        var t = typeof(Ms2.Ui.CubeCellMap).Assembly.GetType(fullName);
        if (t == null) Assert.Fail($"{fullName} is not implemented at origin/main");
        return t;
    }

    public static object New(string fullName, params object[] args)
    {
        var t = Type(fullName);
        var ctor = t.GetConstructors(MemberFlags).FirstOrDefault(c => c.GetParameters().Length == args.Length);
        if (ctor == null) Assert.Fail($"{fullName} has no constructor of {args.Length} arguments");
        return ctor.Invoke(args);
    }

    public static object Member(object o, string path) => TryMember(o, path, out var v) ? v : Missing(o, path);

    public static T Member<T>(object o, string path) => (T)Member(o, path);

    /// <summary>The first path that resolves (for members the implementation may name differently).</summary>
    public static object MemberAny(object o, params string[] paths)
    {
        foreach (var path in paths) if (TryMember(o, path, out var v)) return v;
        return Missing(o, string.Join(" / ", paths));
    }

    public static T MemberAny<T>(object o, params string[] paths) => (T)MemberAny(o, paths);

    public static int Int(object o, params string[] paths) =>
        Convert.ToInt32(MemberAny(o, paths), CultureInfo.InvariantCulture);

    public static long Long(object o, params string[] paths) =>
        Convert.ToInt64(MemberAny(o, paths), CultureInfo.InvariantCulture);

    public static bool Bool(object o, params string[] paths) =>
        Convert.ToBoolean(MemberAny(o, paths), CultureInfo.InvariantCulture);

    public static float Float(object o, params string[] paths) =>
        Convert.ToSingle(MemberAny(o, paths), CultureInfo.InvariantCulture);

    /// <summary>Invokes a method that does not exist yet, failing the test with its name.</summary>
    public static object Call(object o, string method, params object[] args)
    {
        var m = o.GetType().GetMethods(MemberFlags).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
        if (m == null) Assert.Fail($"{o.GetType().FullName}.{method}({args.Length} args) is not implemented at origin/main");
        return m.Invoke(o, args);
    }

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
            int bracket = token.IndexOf('[');
            string name = bracket < 0 ? token : token[..bracket];
            if (name.Length != 0 && !TryOne(ref value, name)) return false;
            while (bracket >= 0)
            {
                int close = token.IndexOf(']', bracket);
                if (close < 0) return false;
                if (!int.TryParse(token[(bracket + 1)..close], out int i)) return false;
                if (!TryIndex(ref value, i)) return false;
                bracket = token.IndexOf('[', close);
            }
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

    static bool TryIndex(ref object o, int i)
    {
        if (o is Array a) { o = a.GetValue(i); return true; }
        if (o is IList l) { o = l[i]; return true; }
        var p = o.GetType().GetProperty("Item", MemberFlags);
        if (p != null) { o = p.GetValue(o, new object[] { i }); return true; }
        int n = 0;
        if (o is IEnumerable e) foreach (var x in e) { if (n++ == i) { o = x; return true; } }
        return false;
    }

    public static int Count(object o)
    {
        if (o is ICollection c) return c.Count;
        if (o is IEnumerable e) { int n = 0; foreach (var _ in e) n++; return n; }
        return (int)MemberAny(o, "Count", "Length");
    }
}
