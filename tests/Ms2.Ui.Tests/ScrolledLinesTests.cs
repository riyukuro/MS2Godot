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
        int line = ScrolledLines.FirstLine(starts.Length, i => starts[i], Line, Room, start: 52, atEnd: true);
        Assert.That(line, Is.EqualTo(55), "the last 5 lines are drawn: lines 55..59, not 52..56 (the newest lines cut off)");
        Assert.That(55 + ScrolledLines.Fit(60, Line, Room, 55), Is.EqualTo(60));
    }

    [Test]
    public void TheEndShowsTheNewestLineAfterLongLinesWrapShorter()
    {
        var starts = Enumerable.Range(0, 30).ToArray();
        // the script's last position falls on a character past what the label's lines would leave blank below
        Assert.That(ScrolledLines.FirstLine(starts.Length, i => starts[i], Line, Room, start: 29, atEnd: true), Is.EqualTo(25));
        Assert.That(ScrolledLines.FirstLine(starts.Length, i => starts[i], Line, Room, start: 29, atEnd: false), Is.EqualTo(25), "never blanks below the last line");
    }

    [Test]
    public void AViewInTheMiddleStartsAtTheLineHoldingTheScriptsFirstCharacter()
    {
        // label lines start at characters 0, 40, 80 ...: character 130 is inside the line starting at 120
        var starts = Enumerable.Range(0, 30).Select(i => i * 40).ToArray();
        Assert.That(ScrolledLines.FirstLine(starts.Length, i => starts[i], Line, Room, start: 130, atEnd: false), Is.EqualTo(3));
        Assert.That(ScrolledLines.FirstLine(starts.Length, i => starts[i], Line, Room, start: 0, atEnd: false), Is.EqualTo(0));
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
        Assert.That(ScrolledLines.FirstLine(starts.Length, i => starts[i], Line, Room, 0, atEnd: true), Is.EqualTo(0));
        Assert.That(ScrolledLines.FirstLine(0, i => 0, Line, Room, 0, atEnd: true), Is.EqualTo(0));
    }

    [Test]
    public void ALastLineThatFillsTheRoomExactlyFitsInBothSums()
    {
        // heights and advances are whole twips / 20 as floats (the client's line sizes): the room is the forward sum of the lines
        // from `first` (what the label's cap measures), LastPosition sums from the last line up; both must keep the newest line
        var rnd = new Random(7);
        for (int round = 0; round < 2000; round++)
        {
            int count = rnd.Next(2, 40);
            var h = Enumerable.Range(0, count).Select(_ => (double)(float)(rnd.Next(150, 400) / 20.0)).ToArray();
            var a = Enumerable.Range(0, count).Select(i => (double)(float)(h[i] + rnd.Next(0, 40) / 20.0)).ToArray();
            (double, double) M(int i) => (h[i], a[i]);
            int first = rnd.Next(0, count - 1);
            double room = 0;
            for (int i = first; i < count - 1; i++) room += a[i];   // the forward sum: the advances above the last line
            room += h[count - 1];
            Assert.That(ScrolledLines.Fit(count, M, room, first), Is.EqualTo(count - first), $"round {round}: the cap keeps every line from {first}");
            Assert.That(ScrolledLines.LastPosition(count, M, room), Is.LessThanOrEqualTo(first), $"round {round}: the first line of an exact fit is not pushed down");
        }
    }

    [Test]
    public void LastPositionIsTheSmallestFirstLineFromWhichEveryLineFits()
    {
        // against the definition (Fit from each candidate), with sizes that are not whole: heights and advances in 0.05 steps
        var rnd = new Random(7);
        for (int round = 0; round < 500; round++)
        {
            int count = rnd.Next(1, 40);
            var h = new double[count]; var a = new double[count];
            for (int i = 0; i < count; i++) { h[i] = rnd.Next(100, 500) * 0.05; a[i] = h[i] + rnd.Next(0, 4) * 0.05; }
            double room = rnd.Next(100, 2000) * 0.05;
            (double, double) M(int i) => (h[i], a[i]);
            int expect = count - 1;
            while (expect > 0 && ScrolledLines.Fit(count, M, room, expect - 1) == count - (expect - 1)) expect--;
            Assert.That(ScrolledLines.LastPosition(count, M, room), Is.EqualTo(expect), $"round {round}: {count} lines in {room}");
        }
    }

    [Test]
    public void LastPositionMeasuresOnlyTheLinesAtTheEnd()
    {
        int measured = 0;
        (double, double) M(int i) { measured++; return (10, 10); }
        Assert.That(ScrolledLines.LastPosition(100000, M, Room), Is.EqualTo(99995));
        Assert.That(measured, Is.LessThanOrEqualTo(8));
    }
}
