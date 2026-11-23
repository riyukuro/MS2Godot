using System;
using Godot;

namespace Ms2Godot;

/// <summary>
/// Runs the finalizers of the engine objects' C# wrappers that are already unreachable at the quit, while the engine is
/// still up (autoload, listed first). A wrapper's finalizer calls into the engine (GodotObject.~GodotObject -> Dispose(false)
/// -> godotsharp_internal_object_get_associated_gchandle, GodotObject.base.cs:98-124). The engine's own shutdown pass
/// disposes only the wrappers whose weak reference still resolves (DisposablesTracker.cs:46-49), from
/// CSharpLanguage::finish (csharp_script.cpp:146) inside Main::cleanup. So a wrapper the GC had found dead but not yet
/// finalized was skipped there, and its finalizer later ran on the .NET finalizer thread into the freed C# language and
/// crashed the exit (SIGSEGV, exit 139: one field run in 24, two Images and an ImageTexture pending).
/// The tree's children leave it last-first, each before its own _exit_tree (node.cpp:421-427), so the first autoload's
/// <see cref="_ExitTree"/> runs after every other node's, still before Main::cleanup frees anything.
/// It also writes the worker threads' log lines on the main thread (<see cref="EngineCalls"/>) and closes their queue there.
/// </summary>
public partial class QuitFinalizers : Node
{
    public override void _EnterTree()
    {
        EngineCalls.Open();
        ProcessMode = ProcessModeEnum.Always;   // a paused tree still writes the workers' lines
    }

    public override void _Process(double delta) => EngineCalls.Drain();

    public override void _ExitTree()
    {
        EngineCalls.Close();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
