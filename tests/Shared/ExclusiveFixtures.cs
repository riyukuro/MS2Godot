using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

[assembly: Ms2Godot.Tests.ExclusiveFixtures]

namespace Ms2Godot.Tests;

/// <summary>Makes <c>[NonParallelizable]</c> mean what it says while the assembly's fixtures run side by side. NUnit runs a
/// non-parallel fixture inline on the thread that dispatches it, which can be while fixtures dispatched earlier are still
/// running, so a fixture that sets a process-wide hook, counter or environment variable would see the others' work. Every
/// test of a parallel fixture holds a shared lock, every test of a non-parallel fixture holds the exclusive one.
/// (The set-up of a fixture runs outside the lock: it must only fill the fixture's own fields.)</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ExclusiveFixturesAttribute : Attribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Test;
    static readonly ReaderWriterLockSlim gate = new(LockRecursionPolicy.NoRecursion);

    [ThreadStatic] static bool exclusive;

    static bool Serial(ITest test)
    {
        var t = test.TypeInfo?.Type;
        return t != null && (t.GetCustomAttribute<NonParallelizableAttribute>(true) != null
            || t.GetCustomAttribute<ParallelizableAttribute>(true) is { Scope: ParallelScope.None });
    }

    public void BeforeTest(ITest test)
    {
        exclusive = Serial(test);
        if (exclusive) gate.EnterWriteLock(); else gate.EnterReadLock();
    }

    public void AfterTest(ITest test)
    {
        if (exclusive) gate.ExitWriteLock(); else gate.ExitReadLock();
    }
}
