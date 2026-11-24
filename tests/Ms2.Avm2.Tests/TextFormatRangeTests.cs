using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// TextField's ranged setTextFormat / getTextFormat (sfr_as3_13.cpp:2847 / :2828) and its paragraph functions
/// (getFirstCharInParagraph :1303, getParagraphLength :1394): the member sweep of the movies' TextField uses found the
/// keyword-colour pattern (getTextFormat(b, e), set the colour, setTextFormat(f, b, e)) in 700+ call sites.
/// </summary>
public class TextFormatRangeTests
{
    static AsTextField Field(string text)
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.multiline = true; tf.wordWrap = true; tf.width = 300; tf.height = 100;
        p.Stage.addChild(tf);
        tf.text = text;
        return tf;
    }

    static AsTextFormat Fmt(AsTextField tf, Action<AsTextFormat> set)
    {
        var f = (AsTextFormat)Runtime.Current.GetClass("flash.text.TextFormat").Construct();
        set(f); return f;
    }

    [Test]
    public void ColourOnARangeIsKeptPerCharAndRead()
    {
        var tf = Field("say red words now");
        tf.setTextFormat(Fmt(tf, f => f.color = Rt.Uint(0xff0000)), 4, 7);
        Assert.That(Rt.ToUint32(tf.getTextFormat(4, 7).color), Is.EqualTo(0xff0000u), "inside the range");
        Assert.That(Rt.ToUint32(tf.getTextFormat(8, 13).color), Is.EqualTo(0u), "outside it, the field's own colour");
        Assert.That(tf.getTextFormat(0, 17).color, Is.Null, "a range that mixes colours has none");
        Assert.That(tf.getTextFormat(3, 5).color, Is.Null, "a range across the edge");
        Assert.That(tf.RenderHtml, Does.Contain("red").And.Contain("FF0000").IgnoreCase);
        Assert.That(tf.text, Is.EqualTo("say red words now"));
    }

    [Test]
    public void ReadModifyWriteTheKeywordPattern()
    {
        var tf = Field("Name: Hero");
        tf.setTextFormat(Fmt(tf, f => f.bold = true), 0, 4);
        var l = tf.getTextFormat(0, 4);          // component_v1: l10 = a1.getTextFormat(l8, l9); ....color = a3; a1.setTextFormat(l10, l8, l9)
        l.color = Rt.Uint(0x00ff00);
        tf.setTextFormat(l, 0, 4);
        var after = tf.getTextFormat(0, 4);
        Assert.That(Rt.ToUint32(after.color), Is.EqualTo(0x00ff00u));
        Assert.That(Rt.ToBoolean(after.bold), Is.True);
        Assert.That(tf.getTextFormat(4, 10).bold is { } b && Rt.ToBoolean(b), Is.False);
    }

    [Test]
    public void OnlyABeginIsToTheEndAndAReversedRangeNothing()
    {
        var tf = Field("abcdef");
        tf.setTextFormat(Fmt(tf, f => f.color = Rt.Uint(0x112233)), 3);
        Assert.That(tf.getTextFormat(3, 6).color, Is.Not.Null);
        Assert.That(Rt.ToUint32(tf.getTextFormat(3).color), Is.EqualTo(0x112233u));
        Assert.That(tf.getTextFormat(0, 3).color is { } c && Rt.ToUint32(c) == 0, Is.True);
        tf.setTextFormat(Fmt(tf, f => f.color = Rt.Uint(0xabcdef)), 4, 2);
        Assert.That(Rt.ToUint32(tf.getTextFormat(3).color), Is.EqualTo(0x112233u), "begin after end changes nothing");
    }

    [Test]
    public void FormatsFromHtmlSurviveARangeEditAndAppendedHtml()
    {
        var tf = Field("");
        tf.htmlText = "<p><font color='#ffd200'>Name</font> plain text</p>";
        tf.setTextFormat(Fmt(tf, f => f.underline = true), 5, 10);
        Assert.That(Rt.ToUint32(tf.getTextFormat(0, 4).color), Is.EqualTo(0xffd200u), "the html's colour stays");
        Assert.That(Rt.ToBoolean(tf.getTextFormat(5, 10).underline), Is.True);
        tf.AppendHtml("<p>more</p>");
        Assert.That(Rt.ToUint32(tf.getTextFormat(0, 4).color), Is.EqualTo(0xffd200u), "and after appendHtml");
        Assert.That(Rt.ToBoolean(tf.getTextFormat(5, 10).underline), Is.True);
    }

    [Test]
    public void ReplaceTextKeepsTheFormatsInTheRenderedHtml()
    {
        var tf = Field("");
        tf.htmlText = "<font color='#ff0000'>red</font> tail";
        tf.replaceText(4, 8, "end");
        Assert.That(tf.text, Is.EqualTo("red end"));
        Assert.That(tf.RenderHtml, Does.Contain("FF0000").IgnoreCase, "the renderer is given the html the formats make");
    }

    [Test]
    public void PlainTextHasNoRenderHtml()
    {
        var tf = Field("plain");
        Assert.That(tf.RenderHtml, Is.Null);
    }

    [TestCase("one\rtwo\rthree", 0, 0, 4)]
    [TestCase("one\rtwo\rthree", 2, 0, 4)]
    [TestCase("one\rtwo\rthree", 3, 0, 4)]
    [TestCase("one\rtwo\rthree", 4, 4, 4)]
    [TestCase("one\rtwo\rthree", 9, 8, 5)]
    [TestCase("one\rtwo\rthree", 13, 8, 5)]
    [TestCase("one\r\ntwo", 2, 0, 5)]
    [TestCase("one\r\ntwo", 5, 5, 3)]
    [TestCase("", 0, 0, 0)]
    public void ParagraphStartAndLength(string text, int c, int start, int length)
    {
        var tf = Field(text);
        Assert.That(tf.getFirstCharInParagraph(c), Is.EqualTo(start));
        Assert.That(tf.getParagraphLength(c), Is.EqualTo(length));
    }

    [Test]
    public void ParagraphOutOfTheText()
    {
        var tf = Field("one\rtwo");
        Assert.That(tf.getFirstCharInParagraph(-1), Is.EqualTo(-1));
        Assert.That(tf.getFirstCharInParagraph(8), Is.EqualTo(-1));
        Assert.That(tf.getParagraphLength(8), Is.EqualTo(-1));
        Assert.That(tf.getParagraphLength(-3), Is.EqualTo(-1));
    }

    [Test]
    public void LineMetricsStartWhereTheLineIsDrawn()
    {
        var tf = Field("abc");
        tf.multiline = false; tf.wordWrap = false;
        tf.setTextFormat(Fmt(tf, f => f.align = "right"));
        var m = tf.getLineMetrics(0);
        var box = tf.getCharBoundaries(0);
        Assert.That(m.x, Is.EqualTo(box.x).Within(1e-9), "the first char's x");
        Assert.That(m.width, Is.EqualTo(tf.textWidth).Within(1e-9));
        Assert.That(m.x, Is.GreaterThan(2));
    }
}
