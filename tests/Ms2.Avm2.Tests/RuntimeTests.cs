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
    public void SetChildIndexIgnoresANonChildAndAnIndexPastTheEnd()
    {
        // GFx setChildIndex: a negative index is 0; a non-child or an index at or past the count is a no-op, no error
        var (rt, p) = Empty();
        var sprite = rt.GetClass("flash.display.Sprite");
        var a = (AsContainer)sprite.Construct();
        var b = (AsDisplayObject)sprite.Construct();
        var c = (AsDisplayObject)sprite.Construct();
        p.Stage.addChild(a);
        a.addChild(b); a.addChild(c);
        var s = (AsContainer)c;
        Assert.DoesNotThrow(() => s.setChildIndex(c, 0));
        Assert.DoesNotThrow(() => a.setChildIndex(b, 2));
        Assert.That((a.getChildIndex(b), a.getChildIndex(c)), Is.EqualTo((0, 1)));
        a.setChildIndex(c, -3);
        Assert.That((a.getChildIndex(c), a.getChildIndex(b)), Is.EqualTo((0, 1)));
    }

    [Test]
    public void RenderRunsOnlyAfterStageInvalidateAndAnInvalidateInsideItIsDropped()
    {
        // MovieRoot::AdvanceFrame (sfr_as3_16z.cpp:607-611): StageInvalidated queues the render chain, cleared after it.
        // The chain holds every display object with a render listener, on the stage or not (sfr_as3_09.cpp:1089).
        var (rt, p) = Empty();
        var sprite = rt.GetClass("flash.display.Sprite");
        var on = (AsDisplayObject)sprite.Construct();
        var off = (AsDisplayObject)sprite.Construct();
        p.Stage.addChild(on);
        int onRenders = 0, offRenders = 0;
        on.addEventListener("render", new AsPlainFunction(rt, new NativeMethod("r", (_, a) =>
        {
            onRenders++;
            p.Call(p.Stage, "invalidate", System.Array.Empty<object>());
            return Rt.Undef;
        })));
        off.addEventListener("render", new AsPlainFunction(rt, new NativeMethod("r", (_, a) => { offRenders++; return Rt.Undef; })));
        for (int i = 0; i < 5; i++) p.Tick();
        Assert.That((onRenders, offRenders), Is.EqualTo((0, 0)), "no stage.invalidate: no render");
        p.Call(p.Stage, "invalidate", System.Array.Empty<object>());
        p.Tick();
        Assert.That((onRenders, offRenders), Is.EqualTo((1, 1)));
        Assert.That(p.RenderInvalidated, Is.False, "the handler's invalidate is cleared with the flag");
        p.Tick();
        Assert.That((onRenders, offRenders), Is.EqualTo((1, 1)));
    }

    [Test]
    public void ADraggedSpriteFollowsTheMouseAndAReleaseOverNothingReachesTheStage()
    {
        var (rt, p) = Empty();
        var s = (AsSprite)rt.GetClass("flash.display.Sprite").Construct();
        s.graphics.beginFill(0xffffff); s.graphics.drawRect(0, 0, 40, 40);
        s.x = 100; s.y = 50;
        p.Stage.addChild(s);
        p.MouseMove(110, 60);
        s.startDrag();
        // InteractiveObject::DoMouseDrag on a mouse move (Stage::PropagateMouseEvent) and on a frame (AvmSprite::AdvanceFrame)
        p.MouseMove(310, 160);
        Assert.That((s.x, s.y), Is.EqualTo((300.0, 150.0)), "the grab point kept");
        p.MouseX = 210; p.MouseY = 60;
        p.Tick();
        Assert.That((s.x, s.y), Is.EqualTo((200.0, 50.0)));
        s.stopDrag();
        p.MouseMove(500, 500);
        Assert.That((s.x, s.y), Is.EqualTo((200.0, 50.0)), "not after stopDrag");

        // MovieRoot::GenerateMouseEvents: mouse up, wheel and move go to the stage when nothing is under the mouse
        var seen = new List<string>();
        foreach (var type in new[] { "mouseUp", "mouseMove", "mouseWheel" })
            p.Call(p.Stage, "addEventListener", new object[] { type, new AsPlainFunction(rt, new NativeMethod(type, (_, a) => { seen.Add(type); return Rt.Undef; })) });
        p.MouseMove(700, 500);
        p.MouseButton(true);
        p.MouseButton(false);
        p.Wheel(3);
        Assert.That(seen, Is.EqualTo(new[] { "mouseMove", "mouseUp", "mouseWheel" }));
    }

    [Test]
    public void DropTargetIsTheTopmostEntityUnderTheMouseOtherThanTheDraggedSprite()
    {
        // Sprite::dropTargetGet -> MovieImpl::GetTopMostEntity(mouse, 0, testAll true, ignore the dragged sprite)
        var (rt, p) = Empty();
        var sprite = rt.GetClass("flash.display.Sprite");
        AsSprite Box(double x)
        {
            var b = (AsSprite)sprite.Construct();
            b.graphics.beginFill(0xffffff); b.graphics.drawRect(0, 0, 40, 40);
            b.x = x;
            return b;
        }
        var slot = Box(100);
        var other = Box(200);
        var dragged = Box(100);
        p.Stage.addChild(slot); p.Stage.addChild(other); p.Stage.addChild(dragged);
        p.MouseX = 120; p.MouseY = 20;
        Assert.That(dragged.dropTarget, Is.SameAs(slot), "the slot under the dragged sprite, by its drawing");
        p.MouseX = 220;
        Assert.That(dragged.dropTarget, Is.SameAs(other));
        p.MouseX = 400;
        Assert.That(dragged.dropTarget, Is.Null, "nothing under the mouse");

        // a text field in the slot is the target itself (every object counts); with mouseChildren off, the slot
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.width = 40; tf.height = 20; tf.selectable = false;
        slot.addChild(tf);
        p.MouseX = 110; p.MouseY = 10;
        Assert.That(dragged.dropTarget, Is.SameAs(tf));
        slot.mouseChildren = false;
        Assert.That(dragged.dropTarget, Is.SameAs(slot));

        // a hidden sprite or a script mask takes nothing; the slot under it does
        slot.mouseChildren = true;
        var cover = Box(100);
        p.Stage.addChildAt(cover, p.Stage.getChildIndex(dragged));
        cover.visible = false;
        Assert.That(dragged.dropTarget, Is.SameAs(tf));
        cover.visible = true;
        other.mask = cover;
        Assert.That(dragged.dropTarget, Is.SameAs(tf));
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
    public void HtmlTextWhosePlainTextIsAKeyIsTranslated()
    {
        // the translator takes plain text (caps 0): an html text is parsed and its text is the key; the result is html
        var (rt, p) = Empty();
        p.Translate = k => k == "$s_points" ? "Attribute <b>Points</b>" : null;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.FromSymbol(new JsonObject { ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["w"] = 100, ["h"] = 20 },
            ["text"] = new JsonObject { ["size"] = 12, ["html"] = true, ["initial"] = "<p align=\"left\"><font size=\"18\" color=\"#ffffff\">$s_points</font></p>" } });
        Assert.That(tf.Text, Is.EqualTo("Attribute Points"));
        Assert.That(tf.Html, Is.EqualTo("Attribute <b>Points</b>"));
        tf.htmlText = "<font color=\"#ff0000\">$s_other</font>";
        Assert.That(tf.Text, Is.EqualTo("$s_other"), "a key with no value stays");
        tf.htmlText = "<b>$s_points</b> left";
        Assert.That(tf.Text, Is.EqualTo("$s_points left"), "only a whole text is a key");
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
    public void EnglishWrapsAtSpacesAndHyphensAndBreaksAnOverlongWord()
    {
        var (rt, _) = Empty();
        var tf = Field(rt, 64, true);
        tf.text = "Character Adventure well-known";
        var lines = tf.Lines();
        foreach (var l in lines) TestContext.WriteLine("[" + l + "]");
        Assert.That(lines.Select(l => l.Trim()), Is.EqualTo(new[] { "Character", "Adventure", "well-", "known" }));
        // a word wider than the box breaks inside the word (ParagraphFormatter::CheckWordWrap, no break point on the line)
        tf.text = "Extraordinarily";                                    // wider than the box
        var parts = tf.Lines();
        Assert.That(parts.Count, Is.GreaterThan(1));
        Assert.That(string.Concat(parts), Is.EqualTo("Extraordinarily"));
        Assert.That(tf.textWidth, Is.LessThanOrEqualTo(64 - 4), "each piece fits the box less its gutters");
        // the words before it still wrap at the space
        tf.text = "go Extraordinarily";
        Assert.That(tf.Lines()[0], Is.EqualTo("go "));
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
    public void FontColorTakesAnyHexLengthAndKeepsTheLow24Bits()
    {
        // SGMLParser::ParseHexInt then TextFormat::SetColor32: the item grade colours are written as #ffRRGGBB
        static uint? Color(string html) => HtmlText.Parse(html).Formats[0].Color;
        Assert.That(Color("<font color='#ff5ad35f'>a</font>"), Is.EqualTo(0x5ad35fu));
        Assert.That(Color("<font color='#5ad35f'>a</font>"), Is.EqualTo(0x5ad35fu));
        Assert.That(Color("<font color='#fff'>a</font>"), Is.EqualTo(0xfffu));
        Assert.That(Color("<font color='#12g'>a</font>"), Is.Null);
        Assert.That(Color("<font color='#'>a</font>"), Is.Null);
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
