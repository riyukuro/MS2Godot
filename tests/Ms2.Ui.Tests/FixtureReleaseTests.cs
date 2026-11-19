using System;
using System.IO;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2Godot.Tests;

[TestFixture]
public class FixtureReleaseTests
{
    [Test]
    public void SharedPackSetIsNotDisposedByAFixtureRelease()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fixrel" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Resource"));
        try
        {
            var shared = PackSet.Shared(dir);
            Assert.That(FixtureReleaseAttribute.ShouldDispose(shared), Is.False, "the session's pack set is handed out again");
            Assert.That(FixtureReleaseAttribute.ShouldDispose(new PackSet(dir)), Is.True, "a fixture's own pack set is disposed");
            Assert.That(FixtureReleaseAttribute.ShouldDispose(new MemoryStream()), Is.True);
        }
        finally { Directory.Delete(dir, true); }
    }
}
