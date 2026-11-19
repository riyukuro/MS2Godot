using System;
using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

[assembly: Ms2Godot.Tests.FixtureRelease]

namespace Ms2Godot.Tests;

/// <summary>When the run moves on to another fixture, drops what the previous fixture's static fields hold (pack sets, movie
/// sources, tables, generated code), so the next fixture does not run on top of it. Without this every fixture's data stays
/// reachable until the process ends. (NUnit runs an assembly-level suite action only for the assembly, so the change of
/// fixture is detected from the tests.)</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FixtureReleaseAttribute : Attribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Test;
    static Type last;
    static readonly object gate = new();

    public void BeforeTest(ITest test)
    {
        var t = test.TypeInfo?.Type;
        if (t == null) return;
        Type done;
        lock (gate) { done = last; last = t; }
        if (done != null && done != t) Release(done);
    }

    public void AfterTest(ITest test) { }

    static void Release(Type t)
    {
        const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var f in t.GetFields(F))
        {
            if (f.IsInitOnly || f.IsLiteral || f.FieldType.IsValueType) continue;
            var v = f.GetValue(null);
            if (v == null) continue;
            if (v is IDisposable d) try { d.Dispose(); } catch { }
            f.SetValue(null, null);
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }
}
