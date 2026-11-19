using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

[NonParallelizable]
public class FieldMoveCoverTests
{
    [TearDown] public void Down() => FieldMoveCover.Lower();

    [Test]
    public void NoInputFromTheMoveUntilTheNextFieldFaded()
    {
        FieldMoveCover.Lower();
        Assert.That(FieldMoveCover.TakesInput, Is.True);
        FieldMoveCover.Raise();                 // the move starts: the old field goes
        Assert.That(FieldMoveCover.TakesInput, Is.False);
        FieldMoveCover.Raise();                 // a GameToGame while the move's cover is up, and the next field built under it
        Assert.That(FieldMoveCover.TakesInput, Is.False);
        FieldMoveCover.Lower();                 // its loading screen faded
        Assert.That(FieldMoveCover.TakesInput, Is.True);
        FieldMoveCover.Lower();
        Assert.That(FieldMoveCover.Up, Is.False);
    }
}
