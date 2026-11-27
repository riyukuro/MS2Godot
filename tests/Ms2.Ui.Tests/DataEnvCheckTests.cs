using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The cases of <see cref="DataEnvCheck.Problem"/>, on a stand-in locale (no data needed).</summary>
public class DataEnvCheckTests
{
    static string Dir => TestContext.CurrentContext.TestDirectory;

    [Test]
    public void GmsDataAsMs2DataWithoutMs2Gms2DataIsReported()
    {
        var why = DataEnvCheck.Problem(Dir, null, _ => "NA");
        Assert.That(why, Does.Contain("MS2_GMS2_DATA"));
        Assert.That(DataEnvCheck.Problem(Dir, "", _ => "na"), Is.EqualTo(why));
    }

    [Test]
    public void GmsDataWithMs2Gms2DataSetIsFine() => Assert.That(DataEnvCheck.Problem(Dir, Dir, _ => "NA"), Is.Null);

    [Test]
    public void KmsDataIsFine() => Assert.That(DataEnvCheck.Problem(Dir, null, _ => "KR"), Is.Null);

    [Test]
    public void NoDataIsFineAndReadsNothing()
    {
        Func<string, string> never = _ => throw new AssertionException("the locale was read");
        Assert.That(DataEnvCheck.Problem(null, null, never), Is.Null);
        Assert.That(DataEnvCheck.Problem("", null, never), Is.Null);
        Assert.That(DataEnvCheck.Problem(Path.Combine(Dir, "no-such-folder"), null, never), Is.Null);
    }
}
