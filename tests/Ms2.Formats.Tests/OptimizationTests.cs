using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>COptimizer::LoadOptimization's ProgressiveLoading switch (optimizer.cpp:373-427).</summary>
public class OptimizationTests
{
    string dir;

    [SetUp]
    public void Make() => dir = Directory.CreateTempSubdirectory("ms2opt").FullName;

    [TearDown]
    public void Drop() => Directory.Delete(dir, true);

    bool With(string xml)
    {
        File.WriteAllText(Path.Combine(dir, Optimization.FileName), xml);
        return Optimization.ProgressiveLoading(dir);
    }

    [Test]
    public void OnWithoutTheFileOrWhenItDoesNotParse()
    {
        Assert.That(Optimization.ProgressiveLoading(dir), Is.True, "every switch is the graphic state's m_b10");
        Assert.That(With("<ms2><ProgressiveLoading value=\"0\""), Is.True);
    }

    [Test]
    public void ALoadedFileGivesItsValueAndOffWithoutIt()
    {
        Assert.That(With("<ms2><ProgressiveLoading value=\"1\"/></ms2>"), Is.True);
        Assert.That(With("<ms2><ProgressiveLoading value=\"0\"/></ms2>"), Is.False);
        Assert.That(With("<ms2><ProgressiveLoading value=\" 2x\"/></ms2>"), Is.True, "sscanf %d reads the leading digits");
        Assert.That(With("<ms2><ProgressiveLoading/></ms2>"), Is.False);
        Assert.That(With("<ms2><ProgressiveLoading value=\"on\"/></ms2>"), Is.False);
        Assert.That(With("<ms2/>"), Is.False);
    }

    [Test]
    public void TheDeclaredEncodingIsNotFollowed()
    {
        Assert.That(With("<?xml version=\"1.0\" encoding=\"euc-kr\"?><ms2><ProgressiveLoading value=\"0\"/></ms2>"), Is.False);
        Assert.That(With("\uFEFF<?xml version=\"1.0\" encoding=\"euc-kr\"?><ms2/>"), Is.False);
    }
}
