using System.Reflection;
using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>A native class with a member of each call shape (<see cref="NativeCall"/>).</summary>
[AsClass("test.NativeProbe")]
public class NativeProbe : AsObject
{
    public int Made = -1;
    public readonly List<string> Log = new();
    [AsCtor] void Ctor(object[] a) { if (a.Length > 0 && a[0] is "throw") throw new InvalidOperationException("ctor"); Made = a.Length; }
    [As] public double width { get; set; } = 1.5;
    [As] public bool flag { get; set; }
    [As] public uint color { get; set; } = 7;
    [As] public string label { get; set; } = "x";
    [As] public void clear() => Log.Add("clear");
    [As] public int sum(int a, int b = 5) => a + b;
    [As] public void move(double x, double y) => Log.Add($"move {x} {y}");
    [As] public string join(string a, int b, bool c) => $"{a}|{b}|{c}";
    [As] public void put(object a, string b, double c) => Log.Add($"put {a ?? "null"} {b ?? "null"} {c}");
    [As] public double four(double a, double b, double c, double d) => a + b + c + d;
    [As] public int count(params object[] rest) => rest.Length;
    [As] public void fail() => throw new ArgumentOutOfRangeException("fail");
    [As] public static string hello() => "hi";
    [As] public static double twice(double d) => d * 2;
    [As] public static void touch() => Touched++;
    [As] public static string pair(string a, int b) => a + b;
    public static int Touched;
}

public class NativeCallTests
{
    static (Runtime rt, AsClass cls) Probe()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        _ = new Player(rt, layout);
        Runtime.Current = rt;
        return (rt, rt.Bind(typeof(NativeProbe)));
    }

    [Test]
    public void EveryShapeCallsTheMember()
    {
        var (rt, cls) = Probe();
        var p = (NativeProbe)rt.Construct(cls, new object[] { 1, 2 });
        Assert.That(p.Made, Is.EqualTo(2), "the native constructor got the arguments");
        Assert.That(rt.GetProperty(p, Name.Public("width")), Is.EqualTo(1.5));
        rt.SetProperty(p, Name.Public("width"), 4);
        Assert.That(p.width, Is.EqualTo(4.0), "an int set into a double property is coerced first");
        rt.SetProperty(p, Name.Public("flag"), 1);
        Assert.That(p.flag, Is.True);
        Assert.That(rt.GetProperty(p, Name.Public("color")), Is.EqualTo(7.0).Or.EqualTo(7u).Or.EqualTo(7));
        rt.SetProperty(p, Name.Public("label"), null);
        Assert.That(p.label, Is.Null, "null into a string parameter");
        rt.CallProperty(p, Name.Public("clear"), Array.Empty<object>());
        Assert.That(rt.CallProperty(p, Name.Public("sum"), new object[] { 2 }), Is.EqualTo(7), "the default fills the missing argument");
        Assert.That(rt.CallProperty(p, Name.Public("sum"), new object[] { 2, 3.9 }), Is.EqualTo(5));
        rt.CallProperty(p, Name.Public("move"), new object[] { 1, "2.5" });
        Assert.That(rt.CallProperty(p, Name.Public("join"), new object[] { "a", 3, 0 }), Is.EqualTo("a|3|False"));
        rt.CallProperty(p, Name.Public("put"), new object[] { null, null, 1 });
        Assert.That(rt.CallProperty(p, Name.Public("four"), new object[] { 1, 2, 3, 4 }), Is.EqualTo(10.0), "four parameters: the reflection call");
        Assert.That(rt.CallProperty(p, Name.Public("count"), new object[] { 1, "a", null }), Is.EqualTo(3));
        Assert.That(p.Log, Is.EqualTo(new[] { "clear", "move 1 2.5", "put null null 1" }));
        Assert.That(rt.CallProperty(cls, Name.Public("hello"), Array.Empty<object>()), Is.EqualTo("hi"));
        Assert.That(rt.CallProperty(cls, Name.Public("twice"), new object[] { 4 }), Is.EqualTo(8.0));
        int before = NativeProbe.Touched;
        rt.CallProperty(cls, Name.Public("touch"), Array.Empty<object>());
        Assert.That(NativeProbe.Touched, Is.EqualTo(before + 1));
        Assert.That(rt.CallProperty(cls, Name.Public("pair"), new object[] { "a", 2 }), Is.EqualTo("a2"));
    }

    [Test]
    public void ExceptionsComeOutAsBefore()
    {
        var (rt, cls) = Probe();
        var p = (NativeProbe)rt.Construct(cls, Array.Empty<object>());
        // a member's own exception as itself, as the reflection path unwrapped it
        Assert.Throws<ArgumentOutOfRangeException>(() => rt.CallProperty(p, Name.Public("fail"), Array.Empty<object>()));
        // a constructor's wrapped, as MethodBase.Invoke threw it
        var e = Assert.Throws<TargetInvocationException>(() => rt.Construct(cls, new object[] { "throw" }));
        Assert.That(e.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void AnArgumentOfAnotherTypeTakesTheReflectionCall()
    {
        var mi = typeof(NativeProbe).GetMethod("move");
        var c = NativeCall.Of(mi);
        Assert.That(c.Typed, Is.True);
        var p = new NativeProbe();
        c.Call(p, new object[] { 1.0, 2.0 });
        c.Call(p, new object[] { 3, 4f });   // MethodBase.Invoke widens int and float to double
        Assert.That(p.Log, Is.EqualTo(new[] { "move 1 2", "move 3 4" }));
        Assert.Throws<ArgumentException>(() => c.Call(p, new object[] { "a", 1.0 }), "a string for a double: as MethodBase.Invoke");
        Assert.That(NativeCall.Of(typeof(NativeProbe).GetMethod("four")).Typed, Is.False);
    }

    /// <summary>
    /// Every [As] member (and constructor) of every flash class: a member of up to three parameters (two for a static
    /// one) gets a typed call, so none of them compiles a reflection invoke stub, of a shape whose code is precompiled.
    /// </summary>
    [Test]
    public void EveryFlashMemberOfAnAdapterShapeIsTyped()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        int members = 0, typed = 0;
        var untyped = new List<string>();
        var notPrecompiled = new List<string>();
        // a type with object for each reference-type argument: the instantiation whose shared code a member runs
        static Type Canon(Type a) => a.IsGenericType
            ? a.GetGenericTypeDefinition().MakeGenericType(a.GetGenericArguments().Select(x => x.IsValueType ? x : typeof(object)).ToArray()) : a;
        var precompiled = NativeCall.Shapes().Select(x => x.GetType()).ToHashSet();
        foreach (var t in typeof(Player).Assembly.GetTypes().Where(t => t.GetCustomAttribute<AsClassAttribute>() != null))
        {
            var ms = t.GetMethods(all).Where(m => m.GetCustomAttribute<AsAttribute>() != null || m.GetCustomAttribute<AsCtorAttribute>() != null).ToList();
            foreach (var p in t.GetProperties(all).Where(p => p.GetCustomAttribute<AsAttribute>() != null))
            {
                if (p.GetGetMethod(true) is { } g) ms.Add(g);
                if (p.GetSetMethod(true) is { } s) ms.Add(s);
            }
            foreach (var m in ms)
            {
                members++;
                int n = m.GetParameters().Length;
                bool shape = m.IsStatic ? n <= 2 : n <= 3;
                var c = NativeCall.Of(m);
                if (c.Typed)
                {
                    typed++;
                    if (!precompiled.Contains(Canon(c.GetType()))) notPrecompiled.Add($"{t.Name}.{m.Name}: {c.GetType().Name}");
                }
                else if (shape) untyped.Add($"{t.Name}.{m.Name}");
            }
        }
        TestContext.WriteLine($"flash members {members}, typed {typed}, reflection {members - typed}");
        Assert.That(members, Is.GreaterThan(800));
        Assert.That(untyped, Is.Empty, "members of an adapter shape left on the reflection call");
        Assert.That(notPrecompiled, Is.Empty, "shapes missing from NativeCall.Shapes (their code is compiled by the JIT)");
    }
}
