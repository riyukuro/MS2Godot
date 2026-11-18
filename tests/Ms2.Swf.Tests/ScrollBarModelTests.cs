using Ms2.Swf.Clik;
using NUnit.Framework;

namespace Ms2.Swf.Tests;

public class ScrollBarModelTests
{
    // a 22 x 66 bar symbol: the up arrow at 0, the track and the thumb at 22, the down arrow at 44, each 22 tall,
    // placed stretched 5.5 times
    static ScrollBarModel Bar() => new(22, 66, 22, 22, 22, 44, 22, 22, 22, 5.5);

    [Test]
    public void EachDrawLaysTheBarOutToItsHeightAtScaleOne()
    {
        var bar = Bar();
        Assert.That((bar.Height, bar.ScaleY), Is.EqualTo((363.0, 5.5)));       // the placed height is kept
        bar.Validate();
        Assert.That(bar.ScaleY, Is.EqualTo(1));                         // the constraints' REFLOW update ends at scale 1
        Assert.That((bar.TrackY, bar.TrackHeight, bar.DownY), Is.EqualTo((22.0, 319.0, 341.0)));
        Assert.That((bar.ContentHeight, bar.Height), Is.EqualTo((363.0, 363.0)));
        Assert.That(bar.ThumbVisible, Is.False);                        // no page size yet
        bar.SetHeight(400);
        bar.Validate();
        Assert.That((bar.ScaleY, bar.TrackHeight, bar.DownY, bar.DisplayHeight), Is.EqualTo((1.0, 356.0, 378.0, 400.0)));
    }

    [Test]
    public void InvalidationsBeforeTheFirstValidationAreOneDraw()
    {
        var bar = Bar();
        bar.SetHeight(400);
        bar.SetScrollProperties(0, 0, 0);
        bar.Validate();
        Assert.That(bar.Draws, Is.EqualTo(1));
        Assert.That((bar.ScaleY, bar.DisplayHeight), Is.EqualTo((1.0, 400.0)));
        bar.Validate();
        Assert.That(bar.Draws, Is.EqualTo(1));                          // nothing left to draw
    }

    [Test]
    public void AnEmptyRangeHidesTheThumbAndDisablesTheArrowsAndTheTrack()
    {
        var bar = Bar();
        bar.Validate();
        bar.Position = 0;
        bar.SetScrollProperties(0, 0, 0);
        bar.Validate();
        Assert.That((bar.ThumbVisible, bar.UpEnabled, bar.DownEnabled, bar.TrackEnabled), Is.EqualTo((false, false, false, false)));
    }

    [Test]
    public void APageOfAsManyLinesAsTheRangeIsHalfTheTrackAtTheTop()
    {
        var bar = Bar();
        bar.Validate();
        bar.Position = 0;
        bar.SetScrollProperties(4, 0, 4);
        bar.Validate();
        Assert.That(bar.ThumbHeight, Is.EqualTo(159.5));                // 4 / (4 + 4) of the 319 track
        Assert.That(bar.ThumbY, Is.EqualTo(22));                        // flush under the up arrow
        Assert.That((bar.ThumbVisible, bar.UpEnabled, bar.DownEnabled, bar.TrackEnabled), Is.EqualTo((true, false, true, true)));
        bar.Position = 4;
        bar.Validate();
        Assert.That(bar.ThumbY, Is.EqualTo(181.5));                     // rounded to 182, kept at the track end
        Assert.That((bar.UpEnabled, bar.DownEnabled), Is.EqualTo((true, false)));
    }

    [Test]
    public void ThePositionIsRoundedAndKeptInTheRange()
    {
        var bar = Bar();
        int scrolls = 0;
        bar.Scroll += () => scrolls++;
        bar.SetScrollProperties(3, 0, 3);
        bar.Position = 1.5;
        Assert.That(bar.Position, Is.EqualTo(2));
        bar.Position = 9;
        Assert.That(bar.Position, Is.EqualTo(3));
        bar.Position = 3;                                               // no change: no SCROLL
        bar.MouseWheel(-1);
        Assert.That(bar.Position, Is.EqualTo(3));
        bar.MouseWheel(1);
        Assert.That((bar.Position, scrolls), Is.EqualTo((2.0, 3)));
    }

    // the half-track thumb bar of APageOfAsManyLinesAsTheRangeIsHalfTheTrackAtTheTop: track 22 .. 341, thumb 159.5, 4 lines
    static ScrollBarModel FourLines()
    {
        var bar = Bar();
        bar.Validate();
        bar.Position = 0;
        bar.SetScrollProperties(4, 0, 4);
        bar.Validate();
        return bar;
    }

    [Test]
    public void TheThumbDragsByTheFractionOfTheAvailableHeight()
    {
        var bar = FourLines();
        Assert.That(bar.AvailableHeight, Is.EqualTo(159.5));
        bar.ThumbPress(30);                                             // 8 below the thumb's top
        bar.Drag(30 + 79.75);                                           // half the available height: 2
        Assert.That(bar.Position, Is.EqualTo(2));
        bar.Drag(30 + 50);                                              // 50 / 159.5 * 4 = 1.25: rounded to 1
        Assert.That(bar.Position, Is.EqualTo(1));
        bar.Drag(1000);
        Assert.That(bar.Position, Is.EqualTo(4));
        bar.ThumbPress(0);                                              // already dragging: the offset stays
        bar.Drag(30);
        Assert.That(bar.Position, Is.EqualTo(0));
        bar.EndDrag();
        bar.Drag(1000);
        Assert.That(bar.Position, Is.EqualTo(0));
    }

    [Test]
    public void ATrackPressBesideTheThumbMovesAPageTowardsTheMouse()
    {
        var bar = FourLines();
        bar.TrackPress(100, false);                                     // on the thumb (22 .. 181.5)
        Assert.That(bar.Position, Is.EqualTo(0));
        bar.TrackPress(300, false);
        Assert.That(bar.Position, Is.EqualTo(1));
        bar.Validate();                                                 // the thumb moves on the next draw
        Assert.That(bar.ThumbY, Is.EqualTo(62));                        // round(0.25 * 159.5 + 22)
        bar.TrackPress(30, false);
        Assert.That(bar.Position, Is.EqualTo(0));
    }

    [Test]
    public void AShiftTrackPressJumpsToTheCursorAndDragsTheThumbByItsMiddle()
    {
        var bar = FourLines();
        bar.TrackPress(22 + 79.75 + 79.75, true);                       // the thumb's middle at half the available height
        Assert.That((bar.Position, bar.Dragging), Is.EqualTo((2.0, true)));
        bar.Drag(22 + 79.75);                                           // the middle at the track's top: 0
        Assert.That(bar.Position, Is.EqualTo(0));
    }

    [Test]
    public void ScrollBarExTakesTheSizeUpAndCapsTheThumb()
    {
        var bar = Bar();
        bar.Ex = true;
        bar.MaxThumbSize = 50;
        bar.SetHeight(400.2);
        bar.Position = 0;
        bar.SetScrollProperties(4, 0, 4);
        bar.Validate();
        Assert.That((bar.Height, bar.TrackHeight, bar.ThumbHeight), Is.EqualTo((401.0, 357.0, 50.0)));
    }
}
