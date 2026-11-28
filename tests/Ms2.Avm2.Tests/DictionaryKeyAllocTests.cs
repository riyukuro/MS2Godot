using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// A Dictionary keeps 1 and "1" as one key, so it compares number keys by their string. Small int keys read their string
/// from a shared table instead of making one for each hash and compare; an object turned into a string or a number tries
/// valueOf and toString in an order kept in a shared array, so it allocates only the string toString returns.
/// </summary>
public class DictionaryKeyAllocTests
{
    static Runtime Rt0()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        _ = new Flash.Player(rt, layout);
        Runtime.Current = rt;
        return rt;
    }

    [Test]
    public void IntKeysAllocateNothing()
    {
        var rt = Rt0();
        var d = rt.GetClass("flash.utils.Dictionary").Construct();
        var keys = Enumerable.Range(0, 1000).Select(k => Rt.Box(k)).ToArray();
        foreach (var k in keys) d.TrySetKey(k, k);
        d.TryGetKey("7", out var seven);
        Assert.That(seven, Is.EqualTo(7), "1 and \"1\" are one key");
        d.TrySetKey(7.0, Rt.Box(8));
        d.TryGetKey(Rt.Box(7), out seven);
        Assert.That(seven, Is.EqualTo(8), "7.0 and 7 are one key");
        d.TrySetKey(Rt.Box(7), Rt.Box(7));
        long bytes = Alloc.Least(() =>
        {
            for (int r = 0; r < 5; r++)
                foreach (var k in keys) { d.TryGetKey(k, out var v); d.TrySetKey(k, v); }
        });
        TestContext.WriteLine($"int keys: {bytes / 10000.0:F1} B per get or set");
        Assert.That(bytes, Is.EqualTo(0));
    }

    [Test]
    public void ObjectToStringAllocatesNoNameArray()
    {
        var rt = Rt0();
        var o = rt.ObjectClass.Construct();
        var s = Rt.ToString(o);
        long bytes = Alloc.Least(() => { for (int i = 0; i < 1000; i++) Rt.ToString(o); });
        TestContext.WriteLine($"String(obj) \"{s}\": {bytes / 1000.0:F1} B per call");
        // what is left is the string toString returns ("[object Object]" is built for each call)
        Assert.That(bytes, Is.LessThanOrEqualTo(1000L * (24 + 2 * s.Length + 8)));
    }
}
