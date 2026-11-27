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

    sealed class OpenMenu : HomeMenuDialogController
    {
        public OpenMenu() : base(null) { MarkOpen(); }
        protected override bool TweenEnabled => false;
        public override void SaveTransform() { }   // no movie host in this test
    }

    [Test]
    public void ASceneLeaveClosesTheOpenMenuAndAFieldChangeAloneKeepsIt()
    {
        var menu = new OpenMenu();
        var leave = HomeMenuEntry.SceneLeaveHandler(() => menu);
        SceneChange.SceneLeft += leave;
        try
        {
            Assert.That(menu.IsClosed, Is.False);
            Assert.That(menu.KeptAtSceneLeave, Is.False, "0x4f7c sets only flag 0");
            // a same-server field change raises no scene leave: the menu stays up
            Assert.That(menu.IsClosed, Is.False);
            SceneChange.StateChanged();             // GFxSceneService::SetScene via the scene-state change
            Assert.That(menu.IsClosed, Is.True);
            SceneChange.GameSceneLeft();            // closed already: nothing, no throw
            Assert.That(menu.IsClosed, Is.True);
        }
        finally { SceneChange.SceneLeft -= leave; }
    }

    [Test]
    public void ASceneLeaveWithoutAnOpenMenuDoesNothing()
    {
        var leave = HomeMenuEntry.SceneLeaveHandler(() => null);
        SceneChange.SceneLeft += leave;
        try { Assert.DoesNotThrow(SceneChange.GameSceneLeft); }
        finally { SceneChange.SceneLeft -= leave; }
    }
}
