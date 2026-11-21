using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIServerListDialog::BuildChannelList's rows (the CRBMap order and the field's limit).</summary>
public class ChannelListTests
{
    [Test]
    public void RowsAreAscendingAndWithoutRepeats() =>
        Assert.That(ChannelList.Build(new[] { 3, 1, 2, 3 }, 1, 9, 0), Is.EqualTo(new[] { 1, 2, 3 }));

    [Test]
    public void ChannelsAboveTheLimitAreLeftOutExceptTheCurrentOne() =>
        Assert.That(ChannelList.Build(new[] { 1, 2, 3, 4, 5 }, 5, 2, 0), Is.EqualTo(new[] { 1, 2, 5 }));

    [Test]
    public void AGmSeesEveryChannel() =>
        Assert.That(ChannelList.Build(new[] { 1, 2, 3, 4 }, 1, 2, 50), Is.EqualTo(new[] { 1, 2, 3, 4 }));
}
