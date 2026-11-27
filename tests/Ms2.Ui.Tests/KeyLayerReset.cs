using System;
using Ms2.Ui;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

// KeyInput.Shared is the client's one s_keyInput, a process-wide static: a test that shows a modal dialog and never hides
// it leaves its layers held for whatever runs next in the process. The fixtures that read the layers are all
// [NonParallelizable] (NUnit runs those alone, with no parallel worker running), so the layers are given back
// (KeyInputLayers.ReleaseAll, which sends each held layer through its channel as the dialog's hide would) before and after
// the tests of those fixtures only. A parallel fixture's test never calls ReleaseAll: the sets are plain, unlocked (the
// game is single-threaded), and two workers must not enumerate and mutate them at once, nor release a layer another
// fixture's dialog still holds mid-test.
[assembly: Ms2.Ui.Tests.KeyLayerReset]

namespace Ms2.Ui.Tests;

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class KeyLayerResetAttribute : Attribute, ITestAction
{
    public ActionTargets Targets => ActionTargets.Test;

    /// <summary>True for a test of a fixture that runs alone: the nearest suite above the test that sets a parallel scope
    /// has <see cref="ParallelScope.None"/> ([NonParallelizable]).</summary>
    public static bool Resets(ITest test)
    {
        for (var t = test; t != null; t = t.Parent)
            if (t.Properties.ContainsKey(PropertyNames.ParallelScope))
                return t.Properties.Get(PropertyNames.ParallelScope) is ParallelScope.None;
        return false;
    }

    public void BeforeTest(ITest test) { if (Resets(test)) KeyInput.ReleaseAll(); }

    public void AfterTest(ITest test) { if (Resets(test)) KeyInput.ReleaseAll(); }
}
