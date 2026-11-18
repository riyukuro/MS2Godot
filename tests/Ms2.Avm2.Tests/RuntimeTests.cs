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

    [Test]
    public void VisibleSetterKeepsTimelineMoves()
    {
        // a clip whose alpha the timeline animates 0 -> 0.5 -> 1, with the placement's visible flag on
        static JsonObject Cx(double a) => new() { ["mul"] = new JsonArray(1, 1, 1, a), ["add"] = new JsonArray(0, 0, 0, 0) };
        var layout = new JsonObject
        {
            ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 },
            ["symbols"] = new JsonObject { ["1"] = new JsonObject { ["kind"] = "sprite", ["timeline"] = new JsonObject { ["frames"] = 1 } } },
            ["root"] = new JsonObject
            {
                ["frames"] = 4,
                ["children"] = new JsonArray(new JsonObject
                {
                    ["id"] = 1, ["depth"] = 1, ["first"] = 0, ["name"] = "arrow", ["visible"] = true, ["color"] = Cx(0),
                    ["moves"] = new JsonArray(new JsonObject { ["frame"] = 2, ["color"] = Cx(0.5) }, new JsonObject { ["frame"] = 3, ["color"] = Cx(1) }),
                }),
            },
        };
        var rt = new Runtime();
        var p = new Player(rt, layout);
        Assert.That(p.Start(), Is.True);
        var arrow = ((AsContainer)p.Root).Children.Single(c => c.InstanceName == "arrow");
        arrow.visible = false;
        arrow.visible = true;
        Assert.That(arrow.ColorLocked || arrow.MatrixLocked, Is.False);
        for (int i = 0; i < 3 && ((AsMovieClip)p.Root).Frame < 3; i++) p.Tick();
        Assert.That(((AsMovieClip)p.Root).Frame, Is.EqualTo(3));
        Assert.That(arrow.Color.Am, Is.EqualTo(1));
        // the timeline does not set visible again on its frames
        arrow.visible = false;
        ((AsMovieClip)p.Root).gotoAndPlay(1);
        p.Tick(); p.Tick();
        Assert.That(arrow.IsVisible, Is.False);
        Assert.That(rt.Errors, Is.Empty);
    }

    AsTextField Field(Runtime rt, double w, bool wrap)
    {
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject { ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = w, ["h"] = 40 }, ["text"] = new JsonObject { ["size"] = 12, ["wordWrap"] = wrap } });
        return tf;
    }

    [Test]
    public void TextAutoSizeScaleFollowsDocViewFormat()
    {
        // shrink: the smaller axis ratio where the text is larger; text that fits keeps its size
        Assert.That(AsTextField.TextAutoSizeScale("shrink", 100, 200, 690, 156, 24), Is.EqualTo(156.0 / 200).Within(1e-9));
        Assert.That(AsTextField.TextAutoSizeScale("shrink", 800, 200, 690, 156, 24), Is.EqualTo(156.0 / 200).Within(1e-9));
        Assert.That(AsTextField.TextAutoSizeScale("shrink", 100, 100, 690, 156, 24), Is.EqualTo(1));
        // never below a 6 px line
        Assert.That(AsTextField.TextAutoSizeScale("shrink", 100, 1000, 690, 50, 24), Is.EqualTo(6.0 / 24).Within(1e-9));
        // fit grows too, but not on an axis under 20
        Assert.That(AsTextField.TextAutoSizeScale("fit", 100, 10, 200, 156, 10), Is.EqualTo(2));
        Assert.That(AsTextField.TextAutoSizeScale("fit", 10, 10, 200, 156, 10), Is.EqualTo(1));
        Assert.That(AsTextField.TextAutoSizeScale("none", 800, 800, 10, 10, 24), Is.EqualTo(1));
    }

    [Test]
    public void ReadabilityTextFollowsTheSymbolAndScriptFieldsAreReadable()
    {
        var (rt, _) = Empty();
        Assert.That(((AsTextField)rt.GetClass("flash.text.TextField").Construct()).antiAliasType, Is.EqualTo("advanced"));
        Assert.That(Field(rt, 60, false).antiAliasType, Is.EqualTo("normal"));
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject { ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 60, ["h"] = 40 }, ["text"] = new JsonObject { ["size"] = 12, ["readable"] = true } });
        Assert.That(tf.antiAliasType, Is.EqualTo("advanced"));
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
    [Test]
    public void HtmlImageCharsSizeTheirLines()
    {
        // StyledText::ParseHtml: an IMG tag is a space; ProcessImageTags sizes it by the exported image and moves it up by its
        // height + VSPACE; FinalizeLine takes its ascent and descent, the layout its width + 2 px
        var (rt, player) = Empty();
        var (height, ascent) = (TextMeasure.HeightRatio, TextMeasure.AscentRatio);
        try
        {
            TextMeasure.HeightRatio = _ => 1.0;
            TextMeasure.AscentRatio = _ => 0.8;
            player.ExportedImageSize = (movie, name) => name == "icon" ? (22, 22) : null;
            var tf = Field(rt, 200, false);
            tf.SetHtml("Title\n<img src='icon' vspace='-6'/> 5");
            Assert.That(tf.text, Is.EqualTo("Title\n  5"));
            Assert.That(tf.textWidth, Is.EqualTo(Math.Max(TextMeasure.Width("", 12, "Title"), TextMeasure.Width("", 12, " 5") + 24)));
            // line 1: 9.6 + 2.4 px; line 2: the image's 16 above and 6 below the baseline
            Assert.That(tf.textHeight, Is.EqualTo((240 + 440) / 20.0));
            // a url that names no image: the tag's size, no move up
            tf.SetHtml("x<img src='none' width='10' height='4'/>");
            Assert.That(tf.textWidth, Is.EqualTo(TextMeasure.Width("", 12, "x") + 12));
            Assert.That(tf.textHeight, Is.EqualTo((192 + 80) / 20.0));
            // plain text has no images
            tf.text = "Title\n  5";
            Assert.That(tf.textHeight, Is.EqualTo(24));
        }
        finally { (TextMeasure.HeightRatio, TextMeasure.AscentRatio) = (height, ascent); }
    }

    [Test]
    public void HtmlTextGetterWritesParagraphsAsScaleform()
    {
        // TextField::GetText(true) -> StyledText::GetHtml: one TEXTFORMAT + P block per paragraph, the last one left out when
        // empty, so html appended to the getter's value starts a new line in a multiline field
        var (rt, _) = Empty();
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject
        {
            ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 200, ["h"] = 40 },
            ["text"] = new JsonObject { ["font"] = "F", ["size"] = 12, ["color"] = "#EEEEEE", ["multiline"] = true, ["html"] = true, ["leading"] = 2, ["initial"] = "" },
        });
        Assert.That(tf.htmlText, Is.EqualTo(""));
        foreach (var row in new[] { "Active", "Long Range / Physical", "a<b" })
            tf.htmlText = tf.htmlText + "<font color='#AAAAAA'>" + System.Net.WebUtility.HtmlEncode(row) + "</font>";
        Assert.That(tf.text, Is.EqualTo("Active\rLong Range / Physical\ra<b"));
        const string P = "<TEXTFORMAT LEADING=\"2\"><P ALIGN=\"LEFT\"><FONT FACE=\"F\" SIZE=\"12\" COLOR=\"#AAAAAA\" KERNING=\"0\">";
        Assert.That(tf.htmlText, Is.EqualTo(P + "Active</FONT></P></TEXTFORMAT>" + P + "Long Range / Physical</FONT></P></TEXTFORMAT>" + P + "a&lt;b</FONT></P></TEXTFORMAT>"));
        // runs: a FONT per change of colour or size, B / I / U per run, the default format where the html sets none
        tf.htmlText = "x<b>y</b><font size='20'>z</font>";
        Assert.That(tf.htmlText, Is.EqualTo("<TEXTFORMAT LEADING=\"2\"><P ALIGN=\"LEFT\"><FONT FACE=\"F\" SIZE=\"12\" COLOR=\"#EEEEEE\" KERNING=\"0\">x<B>y</B></FONT>"
            + "<FONT FACE=\"F\" SIZE=\"20\" COLOR=\"#EEEEEE\" KERNING=\"0\">z</FONT></P></TEXTFORMAT>"));
        // an empty P makes an empty paragraph, which is written when it is not the last
        tf.htmlText = "<p></p>";
        Assert.That(tf.htmlText, Is.EqualTo("<TEXTFORMAT LEADING=\"2\"><P ALIGN=\"LEFT\"><FONT FACE=\"F\" SIZE=\"12\" COLOR=\"#EEEEEE\" KERNING=\"0\"></FONT></P></TEXTFORMAT>"));
        // text clears the html flag: the getter gives the text (textSet)
        tf.text = "plain";
        Assert.That(tf.htmlText, Is.EqualTo("plain"));
    }
}
