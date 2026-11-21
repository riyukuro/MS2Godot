using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

[assembly: Ms2Godot.Tests.FixtureRelease]

namespace Ms2Godot.Tests;

/// <summary>When the last test of a fixture instance has finished, drops what that fixture's static fields hold (pack sets,
/// movie sources, tables, generated code), so the data of finished fixtures does not stay reachable until the process ends.
/// Fixtures run side by side, so the release happens per fixture, when its own tests are all done, never when "the next
/// fixture" starts. (NUnit runs an assembly-level suite action only for the assembly, so the end of a fixture is detected
/// by counting its tests.) A fixture with a <c>[OneTimeTearDown]</c> is released after it, and what is still held when the
/// run ends (a <c>--filter</c> run never counts a fixture down) is released then.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FixtureReleaseAttribute : Attribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Test | ActionTargets.Suite;
    // keyed by the fixture instance: a TestFixtureSource makes several of one type
    static readonly System.Collections.Generic.Dictionary<string, int> left = new();
    static readonly System.Collections.Generic.Dictionary<string, Type> pending = new();
    static readonly object gate = new();

    public void BeforeTest(ITest test) { }

    public void AfterTest(ITest test)
    {
        if (test.IsSuite)
        {
            if (test.Parent != null) return;       // only the assembly's own end
            Type[] rest;
            lock (gate) { rest = pending.Values.Distinct().ToArray(); pending.Clear(); }
            foreach (var r in rest) Release(r);
            return;
        }
        var t = test.TypeInfo?.Type;
        if (t == null) return;
        var fixture = Fixture(test);
        var key = fixture.Id;
        bool last;
        lock (gate)
        {
            if (!left.TryGetValue(key, out var n)) { n = Runnable(fixture); pending[key] = t; }
            left[key] = --n;
            last = n == 0;
            if (last && !HasTearDown(t)) pending.Remove(key);
        }
        if (last && !HasTearDown(t)) Release(t);
    }

    static bool HasTearDown(Type t) =>
        t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(m => m.GetCustomAttribute<OneTimeTearDownAttribute>(true) != null);

    static ITest Fixture(ITest test)
    {
        for (var s = test.Parent; s != null; s = s.Parent)
            if (s.TestType != null && s.TestType.EndsWith("Fixture", StringComparison.Ordinal) && s.TypeInfo?.Type == test.TypeInfo?.Type) return s;
        return test;
    }

    static int Runnable(ITest t)
    {
        if (!t.IsSuite) return t.RunState == RunState.Runnable ? 1 : 0;
        int n = 0;
        foreach (var c in t.Tests) n += Runnable(c);
        return n;
    }

    /// <summary>Only nulls the fields; it never disposes. A handle nobody else holds is freed by its finalizer after the
    /// collection below, and one the product code hands out again (the session's shared pack set) stays alive through its owner.</summary>
    const long CollectAbove = 3L << 30;

    internal static void Release(Type t)
    {
        const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var f in t.GetFields(F))
        {
            if (f.IsInitOnly || f.IsLiteral || f.FieldType.IsValueType) continue;
            var v = f.GetValue(null);
            if (v == null) continue;
            f.SetValue(null, null);
        }
        // a full collection stops every fixture running beside this one, so it is done only once the heap has grown large
        if (GC.GetTotalMemory(false) < CollectAbove) return;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }
}
