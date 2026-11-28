using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>A native class whose members take and return the value types the flash classes use most.</summary>
[AsClass("test.NativeAllocProbe", "flash.events.EventDispatcher")]
public class NativeAllocProbe : AsEventDispatcher
{
    public int Heard;
    [As] public double width { get; set; } = 4;
    [As] public int count { get; set; }
    [As] public string label { get; set; } = "x";
    [As] public double scaled(double a, int b) => a * b;
    [As] public void heard(object e) => Heard++;
    [As] public NativeAllocProbe other { get; set; }
}

/// <summary>
/// A native getter, setter, method call and listener call with arguments the member's own types take: the call passes the
/// values without an argument array or a box for each coerced value (Native.Caller's typed path). Each count is per call.
/// </summary>
public class NativeCallAllocTests
{
    static (Runtime rt, NativeAllocProbe p) Probe()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        _ = new Player(rt, layout);
        Runtime.Current = rt;
        var cls = rt.Bind(typeof(NativeAllocProbe));
        return (rt, (NativeAllocProbe)rt.Construct(cls, Array.Empty<object>()));
    }

    const int N = 10_000;

    [Test]
    public void GetterAllocatesNothing()
    {
        var (rt, p) = Probe();
        var w = Name.Public("width"); var c = Name.Public("count"); var l = Name.Public("label");
        Assert.That(rt.GetProperty(p, w), Is.EqualTo(4));
        long bytes = Alloc.Least(() => { for (int i = 0; i < N; i++) { rt.GetProperty(p, w); rt.GetProperty(p, c); rt.GetProperty(p, l); } });
        TestContext.WriteLine($"getters: {bytes / (3.0 * N):F1} B per get");
        Assert.That(bytes, Is.EqualTo(0));
    }

    [Test]
    public void SetterAllocatesNothing()
    {
        var (rt, p) = Probe();
        var w = Name.Public("width"); var c = Name.Public("count"); var l = Name.Public("label");
        object two = Rt.Box(2), half = 0.5, s = "y";
        rt.SetProperty(p, w, half);
        Assert.That(p.width, Is.EqualTo(0.5));
        rt.SetProperty(p, c, half);
        Assert.That(p.count, Is.EqualTo(0), "0.5 into an int property is coerced first");
        long bytes = Alloc.Least(() => { for (int i = 0; i < N; i++) { rt.SetProperty(p, w, two); rt.SetProperty(p, c, two); rt.SetProperty(p, l, s); } });
        TestContext.WriteLine($"setters: {bytes / (3.0 * N):F1} B per set");
        Assert.That((p.width, p.count, p.label), Is.EqualTo((2.0, 2, "y")));
        Assert.That(bytes, Is.EqualTo(0));
    }

    [Test]
    public void MethodCallAllocatesOnlyItsResult()
    {
        var (rt, p) = Probe();
        var m = Name.Public("scaled");
        var args = new object[] { 1.5, Rt.Box(2) };
        Assert.That(rt.CallProperty(p, m, args), Is.EqualTo(3));
        long bytes = Alloc.Least(() => { for (int i = 0; i < N; i++) rt.CallProperty(p, m, args); });
        TestContext.WriteLine($"method: {bytes / (double)N:F1} B per call");
        Assert.That(bytes, Is.EqualTo(0), "3 is a small int: its box is shared");
    }

    [Test]
    public void NativeListenerCallAllocatesNothing()
    {
        var (rt, p) = Probe();
        var listener = p.Closure(((NativeMethod)p.Class.InstanceTraits.FindPublic("heard").Method));
        p.addEventListener("ping", listener);
        var e = (AsEvent)rt.GetClass("flash.events.Event").Construct("ping");
        p.Dispatch(e);
        Assert.That(p.Heard, Is.EqualTo(1));
        const int n = 1000;
        long bytes = Alloc.Least(() => { for (int i = 0; i < n; i++) p.Dispatch(e); });
        TestContext.WriteLine($"dispatch: {bytes / (double)n:F1} B per dispatch to one native listener");
        Assert.That(bytes, Is.EqualTo(0));
    }

    /// <summary>The typed path coerces as the array path did: a value of the wrong class is the same TypeError, null and
    /// undefined go in as null, and a native call observer still sees the setter's one argument.</summary>
    [Test]
    public void TypedPathKeepsCoercionAndObserver()
    {
        var (rt, p) = Probe();
        var o = Name.Public("other");
        var e = Assert.Catch(() => rt.SetProperty(p, o, "str"));
        Assert.That(e.ToString(), Does.Contain("Error #1034: Type Coercion failed: cannot convert").And.Contain("to NativeAllocProbe (value)"));
        rt.SetProperty(p, o, p);
        Assert.That(p.other, Is.SameAs(p));
        rt.SetProperty(p, o, Rt.Undef);
        Assert.That(p.other, Is.Null);
        var seen = new List<string>();
        NativeMethod.Observer = (m, self, a) => seen.Add($"{m.Name} {a.Length} {a[0]}");
        try { rt.SetProperty(p, Name.Public("width"), Rt.Box(7)); }
        finally { NativeMethod.Observer = null; }
        Assert.That(seen, Is.EqualTo(new[] { "test.NativeAllocProbe/set width 1 7" }));
        Assert.That(p.width, Is.EqualTo(7.0));
    }
}
