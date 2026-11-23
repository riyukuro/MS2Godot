using System.Collections;
using System.Globalization;
using System.Reflection;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// Test helpers: a record or member that is not implemented yet is looked up by name, so every file compiles on
/// main and an ignored test fails with "not read yet" instead of a compile error. Each test names the member it
/// expects; the implementation may pick other names and adapt the test.
/// </summary>
static class RcLane
{
    const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance;

    public static Type PacketType(string name)
    {
        var t = typeof(ProtocolProfile).Assembly.GetType("Ms2.Net.Packets." + name);
        if (t == null) Assert.Fail($"Ms2.Net.Packets.{name} is not read yet at origin/main");
        return t;
    }

    /// <summary>The public static Read(ByteReader) of a record that does not exist yet.</summary>
    public static object Read(string typeName, byte[] bytes)
    {
        var t = PacketType(typeName);
        var m = t.GetMethod("Read", StaticFlags, null, new[] { typeof(ByteReader) }, null);
        if (m == null) Assert.Fail($"{t.FullName}.Read(ByteReader) is not read yet at origin/main");
        var r = new ByteReader(bytes);
        var v = m.Invoke(null, new object[] { r });
        Assert.That(r.Available, Is.EqualTo(0), $"{t.Name} left bytes after the body");
        return v;
    }

    /// <summary>The profile overload Read(ByteReader, ProtocolProfile).</summary>
    public static object Read(string typeName, byte[] bytes, ProtocolProfile profile)
    {
        var t = PacketType(typeName);
        var m = t.GetMethod("Read", StaticFlags, null, new[] { typeof(ByteReader), typeof(ProtocolProfile) }, null);
        if (m == null) Assert.Fail($"{t.FullName}.Read(ByteReader, ProtocolProfile) is not read yet at origin/main");
        var r = new ByteReader(bytes);
        var v = m.Invoke(null, new object[] { r, profile });
        Assert.That(r.Available, Is.EqualTo(0), $"{t.Name} left bytes after the body");
        return v;
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

    /// <summary>The enum value of a member the implementation still has to add (Inbound.Nametag, Inbound.StateConsumeEp, ...).</summary>
    public static T EnumMember<T>(string name) where T : struct, Enum
    {
        if (!Enum.TryParse(name, out T v) || !Enum.IsDefined(typeof(T), v))
            Assert.Fail($"{typeof(T).Name}.{name} is not defined at origin/main");
        return v;
    }

    /// <summary>The GMS2 number of an inbound member (Gms2Opcodes is internal; the test reads its table).</summary>
    public static ushort Gms2Inbound(Inbound value)
    {
        var t = typeof(Inbound).Assembly.GetType("Ms2.Net.Gms2Opcodes");
        var f = t?.GetField("InboundNumbers", StaticFlags);
        if (f?.GetValue(null) is IDictionary d)
        {
            if (d.Contains(value)) return Convert.ToUInt16(d[value], CultureInfo.InvariantCulture);
            Assert.Fail($"Gms2Opcodes has no row for Inbound.{value} at origin/main");
        }
        Assert.Fail("Ms2.Net.Gms2Opcodes.InboundNumbers is not readable at origin/main");
        return 0;
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

    public static byte[] Bytes(string hex) => Convert.FromHexString(hex);

    public static byte[] Hex(params string[] parts) => Convert.FromHexString(string.Concat(parts));

    public static byte[] Write(IOutPacket packet)
    {
        var w = new ByteWriter();
        packet.Write(w);
        return w.ToArray();
    }

    public static byte[] Write(IProfileOutPacket packet, ProtocolProfile profile)
    {
        var w = new ByteWriter();
        packet.Write(w, profile);
        return w.ToArray();
    }
}
