using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class LoginConnectFailureTests
{
    [Test] public void TheDialogFailsFirstThenTheSceneMovie()
    {
        var calls = new List<string>();
        LoginConnectFailure.Run(() => calls.Add("dialog"), () => calls.Add("scene"));
        Assert.That(calls, Is.EqualTo(new[] { "dialog", "scene" }));
    }
}
