using System.Diagnostics;
using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// for-in and for-each step through an object's keys by index (hasnext2, nextname, nextvalue), each step reading the
/// key list as it is now. A loop over N keys reads the list 2N times: a list built for each read is N² work and N² bytes;
/// one kept while the object is unchanged is N. A change during the loop must still show at the next step, as it did.
/// </summary>
public class EnumAllocTests
{
    static Runtime Rt0()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        _ = new Flash.Player(rt, layout);
        Runtime.Current = rt;
        return rt;
    }

    // the steps of hasnext2 then nextname (for-in) or nextvalue (for-each), as the interpreter and the compiled code run them
    static void Loop(Runtime rt, object o, bool values, Action<object> step)
    {
        object obj = o, i = Rt.Box(0);
        while (Gen.HasNext2(ref obj, ref i) is true)
            step(values ? Gen.NextValue(rt, obj, i) : Gen.NextName(rt, obj, i));
    }

    const int N = 200;

    static AsObject NewObject(Runtime rt)
    {
        var o = rt.ObjectClass.Construct();
        for (int k = 0; k < N; k++) o.SetDynamic("k" + k, Rt.Box(k));
        return o;
    }

    static AsObject NewArray(Runtime rt) => new AsArray(rt, Enumerable.Range(0, N).Select(k => Rt.Box(k)));

    static AsObject NewDictionary(Runtime rt)
    {
        var d = rt.GetClass("flash.utils.Dictionary").Construct();
        for (int k = 0; k < N; k++) d.TrySetKey(Rt.Box(k * 3), Rt.Box(k));
        return d;
    }

    static readonly (string name, Func<Runtime, AsObject> make)[] Kinds = { ("Object", NewObject), ("Array", NewArray), ("Dictionary", NewDictionary) };

    /// <summary>A first loop over a new 200-key object (its key list is built in the window) and a loop again.</summary>
    [Test]
    public void LoopOverKeysAllocatesOnce([Values(false, true)] bool values)
    {
        var rt = Rt0();
        var got = new List<(string name, long first, long again)>();
        foreach (var (name, make) in Kinds)
        {
            int seen = 0;
            Loop(rt, make(rt), values, _ => seen++);
            Assert.That(seen, Is.EqualTo(N), name);
            long first = Alloc.Least(() =>
            {
                var o = make(rt);
                long before = GC.GetAllocatedBytesForCurrentThread();
                Loop(rt, o, values, _ => { });
                return GC.GetAllocatedBytesForCurrentThread() - before;
            });
            var same = make(rt);
            Loop(rt, same, values, _ => { });
            long again = Alloc.Least(() => Loop(rt, same, values, _ => { }));
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < 20; r++) Loop(rt, make(rt), values, _ => { });
            TestContext.WriteLine($"{name} {(values ? "for-each" : "for-in")} over {N} keys: first loop {first} B, again {again} B, {sw.Elapsed.TotalMilliseconds / 20:F3} ms per new object");
            got.Add((name, first, again));
        }
        // the key list once: N keys at up to 32 B (a list slot and a key string), never N lists
        foreach (var (name, first, again) in got)
        {
            Assert.That(first, Is.LessThan(N * 32 + 4096), name);
            Assert.That(again, Is.LessThan(1024), name);
        }
    }

    static string Walk(Runtime rt, AsObject o, Action<string> mutate)
    {
        var seen = new List<string>();
        Loop(rt, o, false, k => { var s = Rt.ToString(k); seen.Add(s); mutate(s); });
        return string.Join(",", seen);
    }

    /// <summary>Keys added, deleted or filled in during the loop show from the next step, read by index as they always were
    /// (the expected strings are what the loop gave before its key list was kept). An array is checked with the list's own
/// change count and with the walk a runtime without it uses.</summary>
    [Test]
    public void ChangesDuringTheLoopShowAtTheNextStep([Values(false, true)] bool noListVersion)
    {
        var rt = Rt0();
        AsArray.NoListVersion = noListVersion;
        try { Pins(rt); }
        finally { AsArray.NoListVersion = false; }
    }

    static void Pins(Runtime rt)
    {

        var o = rt.ObjectClass.Construct();
        foreach (var k in new[] { "a", "b", "c", "d", "e" }) o.SetDynamic(k, Rt.True);
        Assert.That(Walk(rt, o, k => { if (k == "b") { o.DeleteDynamic("a"); o.SetDynamic("f", Rt.True); } if (k == "e") o.DeleteDynamic("f"); }),
            Is.EqualTo("a,b,d,e"));

        var a = new AsArray(rt, new[] { Rt.Box(0), Rt.Box(1), Rt.Undef, Rt.Box(3), Rt.Box(4) });
        a.SetDynamic("x", Rt.True);
        Assert.That(Walk(rt, a, k =>
        {
            if (k == "1") a.Items[2] = Rt.Box(2);
            if (k == "3") { a.Items[0] = Rt.Undef; a.Items.Add(Rt.Box(5)); }
            if (k == "5") a.SetDynamic("y", Rt.True);
        }), Is.EqualTo("0,1,2,3,5,x,y"));

        var d = rt.GetClass("flash.utils.Dictionary").Construct();
        for (int k = 1; k <= 5; k++) d.TrySetKey(Rt.Box(k), Rt.True);
        Assert.That(Walk(rt, d, k => { if (k == "2") { d.TryDeleteKey(Rt.Box(3), out _); d.TrySetKey(Rt.Box(6), Rt.True); } if (k == "4") d.TrySetKey("2", Rt.False); }),
            Is.EqualTo("1,2,4,5,6"));

        // loops inside loops, over the same object and over more objects than one at a time
        var outer = new List<string>();
        var others = Enumerable.Range(0, 6).Select(_ => { var x = rt.ObjectClass.Construct(); x.SetDynamic("p", Rt.True); x.SetDynamic("q", Rt.True); return x; }).ToArray();
        var self = rt.ObjectClass.Construct();
        self.SetDynamic("m", Rt.True); self.SetDynamic("n", Rt.True);
        Loop(rt, self, false, k =>
        {
            outer.Add(Rt.ToString(k) + ":" + Walk(rt, self, _ => { }));
            foreach (var x in others) outer.Add(Walk(rt, x, s => { if (s == "p") x.DeleteDynamic("q"); }));
            foreach (var x in others) x.SetDynamic("q", Rt.True);
        });
        Assert.That(string.Join(" ", outer), Is.EqualTo("m:m,n p p p p p p n:m,n p p p p p p"));
        Assert.That(rt.Errors, Is.Empty);
    }
}
