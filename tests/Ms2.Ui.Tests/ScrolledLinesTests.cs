using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Which label line a scrolled script field draws from when the label wraps differently from the script
/// (<see cref="ScrolledLines"/>). Lines are 10 high and advance 10; the field's room holds 5 of them.</summary>
public class ScrolledLinesTests
{
    static (double, double) Line(int i) => (10, 10);
    const double Room = 50;

    [Test]
    public void TheEndShowsTheNewestLineAfterLongLinesWrapLonger()
    {
        // the script counted 40 lines (max position 36) and starts the last view at line 35; the label wrapped the long lines
        // in 3 where the script did in 2: 60 lines, the script's line 35 starts at the label's line 52
        var starts = Enumerable.Range(0, 60).ToArray();
        int line = ScrolledLines.FirstLine(starts, Line, Room, start: 52, atEnd: true);
        Assert.That(line, Is.EqualTo(55), "the last 5 lines are drawn: lines 55..59, not 52..56 (the newest lines cut off)");
        Assert.That(55 + ScrolledLines.Fit(60, Line, Room, 55), Is.EqualTo(60));
    }

    [Test]
    public void TheEndShowsTheNewestLineAfterLongLinesWrapShorter()
    {
        var starts = Enumerable.Range(0, 30).ToArray();
        // the script's last position falls on a character past what the label's lines would leave blank below
        Assert.That(ScrolledLines.FirstLine(starts, Line, Room, start: 29, atEnd: true), Is.EqualTo(25));
        Assert.That(ScrolledLines.FirstLine(starts, Line, Room, start: 29, atEnd: false), Is.EqualTo(25), "never blanks below the last line");
    }

    [Test]
    public void AViewInTheMiddleStartsAtTheLineHoldingTheScriptsFirstCharacter()
    {
        // label lines start at characters 0, 40, 80 ...: character 130 is inside the line starting at 120
        var starts = Enumerable.Range(0, 30).Select(i => i * 40).ToArray();
        Assert.That(ScrolledLines.FirstLine(starts, Line, Room, start: 130, atEnd: false), Is.EqualTo(3));
        Assert.That(ScrolledLines.FirstLine(starts, Line, Room, start: 0, atEnd: false), Is.EqualTo(0));
    }

    [Test]
    public void TallLinesAreMeasuredNotCounted()
    {
        // the last line is 30 high: only 3 lines of 10 plus it (20 + 30 = 50) fit from line n-3
        (double, double) M(int i) => i == 19 ? (30, 30) : (10, 10);
        Assert.That(ScrolledLines.LastPosition(20, M, Room), Is.EqualTo(17));
    }

    [Test]
    public void ShortTextStartsAtTheTop()
    {
        var starts = new[] { 0, 5, 9 };
        Assert.That(ScrolledLines.FirstLine(starts, Line, Room, 0, atEnd: true), Is.EqualTo(0));
        Assert.That(ScrolledLines.FirstLine(Array.Empty<int>(), Line, Room, 0, atEnd: true), Is.EqualTo(0));
    }
}
