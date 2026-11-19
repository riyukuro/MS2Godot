using System;
using NUnit.Framework;

namespace Ms2Godot.Tests;

[TestFixture]
public class FixtureReleaseTests
{
    sealed class Holder : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    static class Statics
    {
        public static Holder Mine;
        public static readonly Holder Fixed = new();
        public static int Count = 3;
    }

    [Test]
    public void ReleaseNullsTheStaticFieldsAndNeverDisposes()
    {
        var shared = new Holder();
        Statics.Mine = shared; // stands for an instance the product code also hands out (the session's pack set)
        FixtureReleaseAttribute.Release(typeof(Statics));
        Assert.That(Statics.Mine, Is.Null, "the fixture's reference is dropped");
        Assert.That(shared.Disposed, Is.False, "a shared instance stays usable");
        Assert.That(Statics.Fixed, Is.Not.Null, "readonly fields are left alone");
        Assert.That(Statics.Count, Is.EqualTo(3), "value-type fields are left alone");
    }
}
