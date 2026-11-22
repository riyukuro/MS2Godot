using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

[TestFixture]
public class HomeMenuEntryTests
{
    [Test] public void HomeMapTypeOpens() => Assert.That(HomeMenuEntry.Decide(3, true, false), Is.True);
    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(4)]
    public void OtherMapTypesDoNot(int type) => Assert.That(HomeMenuEntry.Decide(type, true, false), Is.False);
    [Test] public void NoFieldDataDoesNot() => Assert.That(HomeMenuEntry.Decide(null, true, false), Is.False);
    [Test] public void AlreadyOpenIsNotReopened() => Assert.That(HomeMenuEntry.Decide(3, true, true), Is.False);
    [Test] public void NoLocalPlayerDoesNot() => Assert.That(HomeMenuEntry.Decide(3, false, false), Is.False);
}
