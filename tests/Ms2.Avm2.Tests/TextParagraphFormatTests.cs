using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// The paragraph half of TextField.getTextFormat / setTextFormat and of the html: TEXTFORMAT's INDENT, BLOCKINDENT, LEADING,
/// LEFTMARGIN, RIGHTMARGIN, TABSTOPS and a P's ALIGN are paragraph formats (StyledText::ParseHtml, sfr_render_05i.cpp), read back by
/// StyledText::GetTextAndParagraphFormat (sfr_render_01c.cpp:1195) and written by GetHtml (:1308). The chat's super-chat line
/// (ClientFormat common 0x67c, indent = the icon size, tabStops [0]) is what TextAreaEx.updateBackgroundColor finds its lines by.
/// </summary>
public class TextParagraphFormatTests
{
    const string Super = "<p>plain</p><TEXTFORMAT indent='5' tabStops='0'><P>super</P></TEXTFORMAT><p>after</p>";

    static AsTextField Field(string html = null)
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        Runtime.Current = rt;
        var tf = (AsTextField)rt.GetClass("flash.text.TextField").Construct();
        tf.multiline = true; tf.wordWrap = true; tf.width = 300; tf.height = 100;
        p.Stage.addChild(tf);
        if (html != null) tf.htmlText = html;
        return tf;
    }

    static AsTextFormat Fmt(Action<AsTextFormat> set)
    {
        var f = (AsTextFormat)Runtime.Current.GetClass("flash.text.TextFormat").Construct();
        set(f); return f;
    }

    static double[] Tabs(AsTextFormat f) => f.tabStops is AsArray a ? a.Items.Select(Rt.ToNumber).ToArray() : null;

    [Test]
    public void SuperChatLineHasItsIndentAndTabStopsPlainLinesDoNot()
    {
        var tf = Field(Super);
        Assert.That(tf.text, Is.EqualTo("plain\rsuper\rafter"));
        // TextAreaEx.calcSuperChatInfo: getTextFormat(lineStart, lineEnd - 1)
        var line = tf.getTextFormat(6, 11);
        Assert.That(Rt.ToNumber(line.indent), Is.EqualTo(5));
        Assert.That(Tabs(line), Is.EqualTo(new[] { 0d }));
        var plain = tf.getTextFormat(0, 4);
        Assert.That(plain.tabStops, Is.Null);
        Assert.That(plain.indent, Is.Null);
        Assert.That(tf.getTextFormat(12, 16).tabStops, Is.Null, "the line after the super line is plain again");
    }

    [Test]
    public void ARangeOverDifferentParagraphsHasOnlyWhatTheyShare()
    {
        var tf = Field(Super);
        var mixed = tf.getTextFormat(0, 17);
        Assert.That(mixed.indent, Is.Null);
        Assert.That(mixed.tabStops, Is.Null);
        // a range that starts inside a paragraph counts only the paragraphs it covers from their first char
        Assert.That(tf.getTextFormat(7, 10).indent, Is.Null, "inside the super line, not from its start");
        Assert.That(Rt.ToNumber(tf.getTextFormat(6, 12).indent), Is.EqualTo(5), "from its first char to the next paragraph's first char");
    }

    [Test]
    public void FieldDefaultParagraphFormatIsWhatAPlainParagraphHas()
    {
        var tf = Field(Super);
        tf.defaultTextFormat = Fmt(f => { f.indent = Rt.Num(3); f.align = "center"; });
        var plain = tf.getTextFormat(0, 4);
        Assert.That(Rt.ToNumber(plain.indent), Is.EqualTo(3));
        Assert.That(plain.align, Is.EqualTo("center"));
        var line = tf.getTextFormat(6, 11);
        Assert.That(Rt.ToNumber(line.indent), Is.EqualTo(5), "the TEXTFORMAT's own indent over the default");
        Assert.That(line.align, Is.EqualTo("center"));
    }

    [Test]
    public void PAlignAndAllTextFormatAttributesAreRead()
    {
        var tf = Field("<textformat blockindent='4' leading='2' leftmargin='6' rightmargin='8' tabstops='[10,20]'><p align='right'>x</p></textformat>");
        var f = tf.getTextFormat(0, 1);
        Assert.That(f.align, Is.EqualTo("right"));
        Assert.That(Rt.ToNumber(f.blockIndent), Is.EqualTo(4));
        Assert.That(Rt.ToNumber(f.leading), Is.EqualTo(2));
        Assert.That(Rt.ToNumber(f.leftMargin), Is.EqualTo(6));
        Assert.That(Rt.ToNumber(f.rightMargin), Is.EqualTo(8));
        Assert.That(Tabs(f), Is.EqualTo(new[] { 10d, 20d }));
    }

    [Test]
    public void AValueTheParserRejectsIsSkipped()
    {
        var tf = Field("<textformat indent='x' blockindent='-3' leftmargin='-1'><p>x</p></textformat>");
        var f = tf.getTextFormat(0, 1);
        Assert.That(f.indent, Is.Null);
        Assert.That(f.blockIndent, Is.Null, "BLOCKINDENT below 0 is not taken");
        Assert.That(f.leftMargin, Is.Null);
    }

    [Test]
    public void HtmlWritesTheParagraphFormatsAndReadsThemBack()
    {
        var tf = Field(Super);
        string html = tf.GetHtml();
        Assert.That(html, Does.Contain("<TEXTFORMAT INDENT=\"5\" TABSTOPS=\"0\"><P ALIGN=\"LEFT\">"));
        Assert.That(html.Split("<TEXTFORMAT").Length, Is.EqualTo(4), "one block per paragraph");
        var again = Field(html);
        Assert.That(again.text.TrimEnd('\r'), Is.EqualTo(tf.text), "the html ends in </TEXTFORMAT>: its last line break is kept");
        Assert.That(Rt.ToNumber(again.getTextFormat(6, 11).indent), Is.EqualTo(5));
        Assert.That(Tabs(again.getTextFormat(6, 11)), Is.EqualTo(new[] { 0d }));
        Assert.That(again.getTextFormat(0, 4).tabStops, Is.Null);
    }

    [Test]
    public void TheNextLineAfterASuperLineStaysPlainWhenLinesAreAppended()
    {
        var tf = Field("<p>plain</p>");
        tf.AppendHtml("<TEXTFORMAT indent='5' tabStops='0'><P>super</P></TEXTFORMAT>");
        tf.AppendHtml("<p>after</p>");
        Assert.That(tf.text, Is.EqualTo("plain\rsuper\rafter"));
        Assert.That(Rt.ToNumber(tf.getTextFormat(6, 11).indent), Is.EqualTo(5));
        Assert.That(tf.getTextFormat(12, 16).indent, Is.Null);
        Assert.That(tf.getTextFormat(0, 4).indent, Is.Null);
    }

    [Test]
    public void ASuperChatLineAsTheChatLogBuildsItKeepsItsFormatAmongAppendedLines()
    {
        const string plain = "<P><a href='event:userSelect,Hero,0,0'><FONT color='#ffffff' size='17'>[Hero] </FONT></a><FONT color='#ffffff' size='17'>Plain line</FONT></P>";
        const string super = "<TEXTFORMAT indent='1' tabStops='0'><P><Img Src='export_common_icon_megaphone6' vspace='-7' width='30' height='25'></Img><a href='event:userSelect,Hero,0,0'><FONT color='#ffff00' size='17'>[Hero][Ch.0] </FONT></a><a href='event:moveToHome,Hero,0,0'><Img Src='export_common_icon_movetohouse' vspace='-7' width='18' height='16'></Img></a><FONT color='#ffff00' size='17'>Super chat line </FONT></P></TEXTFORMAT>";
        var tf = Field("");
        foreach (var h in new[] { plain, plain, super, plain }) tf.AppendHtml(h);
        int lines = tf.numLines;
        Assert.That(lines, Is.EqualTo(4), "one line each at this width");
        int total = 0;
        for (int i = 0; i < lines; i++)
        {
            Assert.That(tf.getLineOffset(i), Is.EqualTo(total), "line " + i + " starts where the one before it ends");
            total += tf.getLineLength(i);
        }
        Assert.That(total, Is.EqualTo(tf.text.Length), "the lengths cover the text, each with its break");
        // TextAreaEx.calcSuperChatInfo: getTextFormat(off, off + len - 1) of the line, then indent > 0 and tabStops != null
        bool Super(int i)
        {
            int off = tf.getLineOffset(i);
            var f = tf.getTextFormat(off, off + tf.getLineLength(i) - 1);
            return f != null && f.tabStops != null && f.indent is { } d && Rt.ToNumber(d) > 0;
        }
        Assert.That(Enumerable.Range(0, lines).Where(Super), Is.EqualTo(new[] { 2 }), "only the super chat line");
    }

    [TestCase("one\rtwo\rthree", 0, 0, 4)]
    [TestCase("one\rtwo\rthree", 1, 4, 4)]
    [TestCase("one\rtwo\rthree", 2, 8, 5)]
    [TestCase("one\r\ntwo", 0, 0, 5)]
    [TestCase("one\r\ntwo", 1, 5, 3)]
    [TestCase("one\r\rtwo", 1, 4, 1)]
    [TestCase("", 0, 0, 0)]
    [TestCase("one\rtwo", 2, -1, -1)]
    [TestCase("one\rtwo", -1, -1, -1)]
    public void LineOffsetIsTheTextPositionAndLengthCountsTheBreak(string text, int line, int offset, int length)
    {
        var tf = Field();
        tf.text = text;
        Assert.That(tf.getLineOffset(line), Is.EqualTo(offset));
        Assert.That(tf.getLineLength(line), Is.EqualTo(length));
    }

    [Test]
    public void WrappedLinesOfAParagraphFollowOneAnother()
    {
        var tf = Field();
        tf.width = 60;
        tf.text = "alpha beta gamma delta epsilon\rzeta";
        int n = tf.numLines;
        Assert.That(n, Is.GreaterThan(2));
        for (int i = 1; i < n; i++)
            Assert.That(tf.getLineOffset(i), Is.EqualTo(tf.getLineOffset(i - 1) + tf.getLineLength(i - 1)));
        Assert.That(tf.getLineOffset(n - 1), Is.EqualTo(31));
        Assert.That(tf.getLineLength(n - 1), Is.EqualTo(4));
    }

    [Test]
    public void SetTextFormatMergesTheParagraphFormatIntoTheParagraphsItStartsIn()
    {
        var tf = Field();
        tf.text = "one\rtwo\rthree\rfour";
        tf.setTextFormat(Fmt(f => f.indent = Rt.Num(7)), 4, 5);
        Assert.That(tf.getTextFormat(0, 3).indent, Is.Null, "the paragraph before");
        Assert.That(Rt.ToNumber(tf.getTextFormat(4, 7).indent), Is.EqualTo(7));
        // StyledText::SetParagraphFormat (sfr_render_01c.cpp:1141) merges the paragraph after the range's last one as well
        Assert.That(Rt.ToNumber(tf.getTextFormat(8, 13).indent), Is.EqualTo(7), "the client's loop also sets the next paragraph");
        Assert.That(tf.getTextFormat(14, 18).indent, Is.Null, "but no further");
        Assert.That(tf.text, Is.EqualTo("one\rtwo\rthree\rfour"));
    }

    [Test]
    public void SetTextFormatOfAnEmptyRangeSetsTheParagraphOnlyWhenItStartsThere()
    {
        var tf = Field();
        tf.text = "one\rtwo";
        tf.setTextFormat(Fmt(f => f.leading = Rt.Num(3)), 5, 5);
        Assert.That(tf.getTextFormat(4, 7).leading, Is.Null, "an empty range inside a paragraph sets nothing");
        tf.setTextFormat(Fmt(f => f.leading = Rt.Num(3)), 4, 4);
        Assert.That(Rt.ToNumber(tf.getTextFormat(4, 7).leading), Is.EqualTo(3));
        Assert.That(tf.getTextFormat(0, 3).leading, Is.Null);
    }

    [Test]
    public void ParagraphFormatsFollowTextThatIsReplaced()
    {
        var tf = Field(Super);
        tf.replaceText(0, 5, "a much longer first line");
        int at = tf.text.IndexOf("super", StringComparison.Ordinal);
        Assert.That(Rt.ToNumber(tf.getTextFormat(at, at + 5).indent), Is.EqualTo(5));
        Assert.That(tf.getTextFormat(0, 3).indent, Is.Null);
    }

    [Test]
    public void TabStopsAreWrittenAndTheWholeTextFormatKeepsTheFieldDefault()
    {
        var tf = Field();
        tf.text = "a\rb";
        tf.setTextFormat(Fmt(f => f.tabStops = new AsArray(Runtime.Current, new object[] { Rt.Num(10), Rt.Num(30) })));
        Assert.That(Tabs(tf.getTextFormat()), Is.EqualTo(new[] { 10d, 30d }));
        Assert.That(Tabs(tf.defaultTextFormat), Is.EqualTo(new[] { 10d, 30d }));
    }

    [Test]
    public void ParserFedInPiecesHasTheParagraphFormatsOfTheWhole()
    {
        string[] pieces = { "<p>a</p><textformat indent='4' tabstops='[0,10]'>", "<p>b</p>", "</textformat><p align='center'>c</p>", "<textformat leading='2'><p>d", "</p></textformat>" };
        var whole = new HtmlText.Parser(true, '\r', false, true);
        whole.Feed(string.Concat(pieces));
        var fed = new HtmlText.Parser(true, '\r', false, true);
        foreach (var piece in pieces) fed.Feed(piece);
        Assert.That(fed.Text(0), Is.EqualTo(whole.Text(0)));
        Assert.That(fed.Paragraphs.Count, Is.EqualTo(whole.Paragraphs.Count));
        for (int i = 0; i < whole.Paragraphs.Count; i++)
        {
            Assert.That(fed.Paragraphs[i].Start, Is.EqualTo(whole.Paragraphs[i].Start));
            Assert.That(fed.Paragraphs[i].Format, Is.EqualTo(whole.Paragraphs[i].Format), $"paragraph {i}");
        }
        Assert.That(whole.Paragraphs.Select(x => x.Start), Is.EqualTo(new[] { 2, 4, 6 }), "'a\\r' is 2 chars: the TEXTFORMAT's paragraph b starts at 2");
    }

    [Test]
    public void PlainHtmlHasNoParagraphFormats()
    {
        var p = new HtmlText.Parser(true, '\r', false, true);
        p.Feed("<p>a <b>b</b></p><p>c</p>");
        Assert.That(p.Paragraphs, Is.Empty);
        Assert.That(p.Styled, Is.True.Or.False);
        var tf = Field("<p>a</p><p>b</p>");
        Assert.That(tf.GetHtml(), Does.Not.Contain("INDENT"));
    }

    [Test]
    public void ATallImageLineCountsAsItsOwnHeightInTheScrollRange()
    {
        // TextAreaEx.updateBackgroundColor reads scrollV .. bottomScrollV and adds the lines' metrics up, so the range has to be the
        // one the view draws: a line is shown when its bottom is within the field (LineBuffer::IsLineVisible, sfr_render_04d.cpp:365)
        var (height, ascent) = (TextMeasure.HeightRatio, TextMeasure.AscentRatio);
        try
        {
            TextMeasure.HeightRatio = _ => 1.0;
            TextMeasure.AscentRatio = _ => 0.8;
            var tf = Field();
            tf.height = 100;
            string plain = string.Concat(Enumerable.Repeat("<p>x</p>", 9));
            tf.htmlText = plain + "<p>y</p>";
            // 12 px lines, 97 px of room: 8 shown
            Assert.That(tf.numLines, Is.EqualTo(10));
            Assert.That(tf.maxScrollV, Is.EqualTo(3));
            Assert.That(tf.bottomScrollV, Is.EqualTo(8));
            // the last line is a 60 px image: only 3 plain lines fit above it
            tf.htmlText = plain + "<p><img width='10' height='60'/></p>";
            Assert.That(tf.numLines, Is.EqualTo(10));
            Assert.That(tf.maxScrollV, Is.EqualTo(7));
            tf.scrollV = 4;
            Assert.That(tf.bottomScrollV, Is.EqualTo(9), "the image line does not fit below line 9");
            tf.scrollV = 7;
            Assert.That(tf.bottomScrollV, Is.EqualTo(10));
            Assert.That(tf.getLineMetrics(9).height, Is.EqualTo(60));
        }
        finally { (TextMeasure.HeightRatio, TextMeasure.AscentRatio) = (height, ascent); }
    }
}
