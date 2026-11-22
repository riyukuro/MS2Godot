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

    [Test]
    public void CharBoundariesFollowTheFieldsAlignAndVerticalAlign()
    {
        var tf = Field("<p>gauge:</p>");
        tf.Format ??= (AsTextFormat)Runtime.Current.GetClass("flash.text.TextFormat").Construct();
        var left = tf.getCharBoundaries(0);
        tf.Format.align = "center";
        var centred = tf.getCharBoundaries(0);
        tf.Format.align = "right";
        var right = tf.getCharBoundaries(0);
        Assert.That(centred.x, Is.GreaterThan(left.x + 20), "a centred line starts right of the left edge");
        Assert.That(right.x, Is.GreaterThan(centred.x + 20));
        tf.Format.align = "left";
        tf.VerticalAlign = "bottom";
        Assert.That(tf.getCharBoundaries(0).y, Is.GreaterThan(left.y + 20), "bottom aligned text sits at the bottom of the box");
    }

    [Test]
    public void AppendTextKeepsTheHeadingsColour()
    {
        var tf = Field(Doc);
        tf.appendText("more");
        Assert.That(tf.text, Does.EndWith("more"));
        Assert.That(tf.htmlText.ToLowerInvariant(), Does.Contain("ffd200"));
    }
}
