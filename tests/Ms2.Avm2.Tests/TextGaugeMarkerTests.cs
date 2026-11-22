using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// What the CommonTooltip movie's setItemStatus asks its status field for around the "gauge:" marker: the char's box on
/// its own line, and the marker cut out with the heading's format kept.
/// </summary>
public class TextGaugeMarkerTests
{
    static AsTextField Field(string html)
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.multiline = true; tf.wordWrap = true; tf.width = 300; tf.height = 100;
        tf.htmlText = html;
        p.Stage.addChild(tf);
        return tf;
    }

    const string Doc = "<p><font color='#ffd200'>Heading</font></p><p>gauge:</p><p></p>";

    [Test]
    public void CharBoundariesAreOnTheCharsOwnLine()
    {
        var tf = Field(Doc);
        int i = tf.text.IndexOf("gauge:", StringComparison.Ordinal);
        var first = tf.getCharBoundaries(0);
        var marker = tf.getCharBoundaries(i);
        Assert.That(i, Is.GreaterThan(0));
        Assert.That(marker.y, Is.GreaterThan(first.y + 1), "the marker is on the second line");
        Assert.That(marker.x, Is.EqualTo(first.x).Within(0.01), "and at its start");
        Assert.That(tf.getLineIndexOfChar(i), Is.EqualTo(1));
    }

    [Test]
    public void CutOutMarkerKeepsTheHeadingsColour()
    {
        var tf = Field(Doc);
        int i = tf.text.IndexOf("gauge:", StringComparison.Ordinal);
        tf.replaceText(i, i + 6, "");
        Assert.That(tf.text, Does.Not.Contain("gauge:"));
        Assert.That(tf.htmlText.ToLowerInvariant(), Does.Contain("ffd200"));
    }
}
