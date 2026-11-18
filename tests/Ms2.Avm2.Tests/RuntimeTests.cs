using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>The value rules and the flash.* runtime without game data (an empty movie layout).</summary>
public class RuntimeTests
{
    static (Runtime, Player) Empty()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        return (rt, new Player(rt, layout));
    }

    [Test]
    public void Conversions()
    {
        Assert.That(Rt.ToString(0.1 + 0.2), Is.EqualTo("0.30000000000000004"));
        Assert.That(Rt.ToString(1e21), Is.EqualTo("1e+21"));
        Assert.That(Rt.Add("a", 1), Is.EqualTo("a1"));
        Assert.That(Rt.Add(int.MaxValue, 1), Is.EqualTo(2147483648.0));
        Assert.That(Rt.ToInt32(4294967296.0 + 5), Is.EqualTo(5));
        Assert.That(Rt.LooseEquals("1", 1), Is.True);
        Assert.That(Rt.StrictEquals("1", 1), Is.False);
        Assert.That(Rt.LessThan(double.NaN, 1), Is.Null);
    }

    [Test]
    public void DisplayListAndEvents()
    {
        var (rt, p) = Empty();
        var sprite = rt.GetClass("flash.display.Sprite");
        var a = (AsContainer)sprite.Construct();
        Assert.That(a, Is.InstanceOf<AsContainer>());
        var b = (AsDisplayObject)sprite.Construct();
        p.Stage.addChild(a);
        a.addChild(b);
        int added = 0;
        a.addEventListener("added", new AsPlainFunction(rt, new NativeMethod("f", (s, args) => { added++; return Rt.Undef; })));
        var c = (AsDisplayObject)sprite.Construct();
        a.addChild(c);
        Assert.That(added, Is.EqualTo(1));
        Assert.That(a.getChildIndex(c), Is.EqualTo(1));
        b.x = 10; a.x = 5;
        Assert.That(b.LocalToGlobal(0, 0).X, Is.EqualTo(15));
    }

    AsTextField Field(Runtime rt, double w, bool wrap)
    {
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject { ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = w, ["h"] = 40 }, ["text"] = new JsonObject { ["size"] = 12, ["wordWrap"] = wrap } });
        return tf;
    }

    [Test]
    public void EnglishWrapsOnlyAtSpacesAndHyphens()
    {
        var (rt, _) = Empty();
        var tf = Field(rt, 60, true);
        tf.text = "Character Adventure well-known";
        var lines = tf.Lines();
        foreach (var l in lines) TestContext.WriteLine("[" + l + "]");
        Assert.That(lines.Select(l => l.Trim()), Is.EqualTo(new[] { "Character", "Adventure", "well-", "known" }));
        // a word wider than the box stays whole
        tf.text = "Extraordinarily";
        Assert.That(tf.Lines(), Is.EqualTo(new[] { "Extraordinarily" }));
    }

    [Test]
    public void KoreanKeepsPerCharacterBreaking()
    {
        var (rt, _) = Empty();
        var tf = Field(rt, 40, true);
        tf.text = "\uAC00\uB098\uB2E4\uB77C\uB9C8\uBC14";
        Assert.That(tf.Lines().Count, Is.GreaterThan(1));
        Assert.That(string.Concat(tf.Lines()), Is.EqualTo("\uAC00\uB098\uB2E4\uB77C\uB9C8\uBC14"));
    }

    [Test]
    public void SingleLineCaptionShrinksThenEllipsis()
    {
        var (rt, _) = Empty();
        var tf = Field(rt, 50, false);
        tf.text = "Settings";          // a little too wide: shrinks, no cut
        double w = TextMeasure.Width("", 12, "Settings");
        var (l1, s1) = tf.Fitted();
        Assert.That(w, Is.GreaterThan(46));
        Assert.That(s1, Is.LessThan(1).And.GreaterThanOrEqualTo(AsTextField.MinFitScale));
        Assert.That(l1[0], Is.EqualTo("Settings"));
        tf.text = "Character Information Window";    // far too wide: 80% then an ellipsis
        var (l2, s2) = tf.Fitted();
        Assert.That(s2, Is.EqualTo(AsTextField.MinFitScale));
        Assert.That(l2[0], Does.EndWith("…"));
        Assert.That(TextMeasure.Width("", 12 * s2, l2[0]), Is.LessThanOrEqualTo(46));
        // the AS-visible metrics are those of the full text
        Assert.That(tf.text, Is.EqualTo("Character Information Window"));
    }
}
