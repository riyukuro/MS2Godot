using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The guide's key conditions ask CUIService::IsTextFieldFocused (Input, unit_1413a6580.cpp; Next, unit_1413a7bc0.cpp), the
/// same flag the input gate reads (<see cref="InputBlock.TextFieldFocused"/>). Sets that static, so it runs alone.
/// </summary>
[NonParallelizable]
public class GuideTextFocusTests
{
    [TearDown]
    public void Restore() => InputBlock.TextFieldFocused = () => false;

    [Test]
    public void AKeyHeldWhileATextFieldHasTheFocusMeetsNoInputCondition()
    {
        var w = new GuideTests.World { KeyHeld = true };
        var g = new GuideManager(w);
        var input = new InputCondition { Value = 0x39 };
        Assert.That(input.Check(g), Is.EqualTo(1), "the key held, no field focused");
        InputBlock.TextFieldFocused = () => true;
        Assert.That(w.TextFocused, Is.True, "the guide reads the text-field focus");
        Assert.That(input.Check(g), Is.EqualTo(0), "the key held into a focused field");
    }
}
