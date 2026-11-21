using Ms2.Ui.Launch;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class CharacterSelectReturnTests
{
    [Test] public void TheFieldSwitchesToCharacterSelect() => Assert.That(CharacterSelectReturn.Applies(true), Is.True);
    [Test] public void AnyOtherSceneStays() => Assert.That(CharacterSelectReturn.Applies(false), Is.False);
}
