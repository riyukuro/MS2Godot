using System.Collections;
using System.Reflection;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Helpers for the GMS2 packet-layout tests. A reader or member a later change still has to add is looked up by name,
/// so the files compile on origin/main and an ignored test fails with "not read yet" (or, when an older overload
/// exists, the older overload runs and fails with the layout's own exception). The expected API is named in each test.
/// </summary>
static class RcPkt
{
    const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance;

    public static Type PacketType(string name)
    {
        var t = typeof(ProtocolProfile).Assembly.GetType("Ms2.Net.Packets." + name);
        if (t == null)
            Assert.Fail($"Ms2.Net.Packets.{name} is not read yet at origin/main");
        return t;
    }

    /// <summary>A static method by name. <paramref name="fallback"/> is tried when no overload fits the primary args, so a
    /// test can call the future GMS2 overload on main through the old one and fail for the layout's reason.</summary>
    public static object Call(string typeName, string method, object[] args, object[] fallback = null)
    {
        var t = PacketType(typeName);
        var primary = Find(t, method, args);
        if (primary != null) return primary.Invoke(null, args);
        if (fallback != null)
        {
            var fb = Find(t, method, fallback);
            if (fb != null) return fb.Invoke(null, fallback);
        }
        var sig = args == null ? "" : string.Join(", ", args.Select(a => a?.GetType().Name ?? "null"));
        Assert.Fail($"{t.FullName}.{method}({sig}) is not read yet at origin/main");
        return null;
    }

    /// <summary>Tries several method names and argument lists in order (the trigger sub-readers).</summary>
    public static object CallAny(string typeName, string[] methods, object[][] argSets)
    {
        var t = PacketType(typeName);
        foreach (var method in methods)
            foreach (var args in argSets)
            {
                var m = Find(t, method, args);
                if (m != null) return m.Invoke(null, args);
            }
        Assert.Fail($"{t.FullName}.{string.Join("/", methods)} is not read yet at origin/main");
        return null;
    }

    /// <summary>The reader's Read: the profile form first, the old one-argument form as the fallback.</summary>
    public static object Read(string typeName, ByteReader r, params object[] extra)
    {
        var args = new object[extra.Length + 1];
        args[0] = r;
        Array.Copy(extra, 0, args, 1, extra.Length);
        return Call(typeName, "Read", args, new object[] { r });
    }

    static MethodInfo Find(Type t, string name, object[] args)
    {
        foreach (var m in t.GetMethods(StaticFlags))
        {
            if (m.Name != name) continue;
            var ps = m.GetParameters();
            if (ps.Length != args.Length) continue;
            bool fits = true;
            for (int i = 0; i < ps.Length; i++)
                if (args[i] != null && !ps[i].ParameterType.IsInstanceOfType(args[i])) { fits = false; break; }
            if (fits) return m;
        }
        return null;
    }

    public static object Member(object o, string path) => TryMember(o, path, out var v) ? v : Missing(o, path);

    public static T Member<T>(object o, string path) => (T)Member(o, path);

    /// <summary>The first path that resolves (for members a later change may name slightly differently).</summary>
    public static object MemberAny(object o, params string[] paths)
    {
        foreach (var path in paths) if (TryMember(o, path, out var v)) return v;
        return Missing(o, string.Join(" / ", paths));
    }

    public static T MemberAny<T>(object o, params string[] paths) => (T)MemberAny(o, paths);

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

    /// <summary>The reader returned the wrong row kind (for example UgcOther or CinematicPacket.Other).</summary>
    public static void NotType(object o, string name, string layout)
    {
        if (o == null || o.GetType().Name == name) Assert.Fail($"the reader returned {name}, not the {layout} row");
    }

    public static byte[] Bytes(string hex) => Convert.FromHexString(hex);

    /// <summary>A recorded packet as hex, in as many parts as the generator needed.</summary>
    public static byte[] Hex(params string[] parts) => Convert.FromHexString(string.Concat(parts));

    public static ByteReader Reader(string hex) => new(Bytes(hex));
}
