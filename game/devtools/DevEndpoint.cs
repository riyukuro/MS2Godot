#if MS2_DEVTOOLS
using System;
using System.Text.Json.Nodes;
using Godot;
using Ms2.Devtools;

namespace Ms2Godot;

/// <summary>
/// In-game debug endpoint for agents (compiled only with MS2_DEVTOOLS, started only with env MS2_DEBUG=1 by
/// <see cref="Bootstrap"/>). A loopback socket (<see cref="DebugServer"/>) queues JSON-line requests; this node answers them
/// on the main thread from _Process within a small time budget. Commands read the scene tree, materials, C# state, the
/// movies' display lists and the log, take screenshots, and feed input events. There is no eval, no method call and no
/// property write. A request that throws is answered with an error and never reaches the game.
/// Reentrancy: requests run only from this node's own _Process, never from a signal or an AS3 callback.
/// </summary>
public partial class DevEndpoint : Node
{
    public const double FrameBudgetMs = 4.0;
    public const string EnvOn = "MS2_DEBUG", EnvFile = "MS2_DEBUG_FILE", EnvOut = "MS2_DEBUG_OUT";

    DebugServer server;
    DevLogger logger;
    string infoFile;
    bool inRequest;
    long frames, busyFrames, ticks;   // cost accounting: time spent inside _Process, idle frames included

    static DevEndpoint instance;

    /// <summary>Called from the autoload's _EnterTree: does nothing unless MS2_DEBUG=1.</summary>
    public static void Bootstrap(Node host)
    {
        if (instance != null || OS.GetEnvironment(EnvOn) != "1") return;
        instance = new DevEndpoint { Name = "DevEndpoint", ProcessMode = ProcessModeEnum.Always };
        host.GetTree().Root.CallDeferred(Node.MethodName.AddChild, instance);
    }

    public override void _EnterTree()
    {
        try
        {
            server = new DebugServer();
            logger = new DevLogger();
            OS.AddLogger(logger);
            infoFile = OS.GetEnvironment(EnvFile);
            if (string.IsNullOrEmpty(infoFile)) infoFile = ProjectSettings.GlobalizePath("user://debug.json");
            server.WriteInfoFile(infoFile);
            GD.Print($"DevEndpoint: listening on 127.0.0.1:{server.Port}, info in {infoFile}");
        }
        catch (Exception e)
        {
            GD.PushError("DevEndpoint: could not start: " + e.Message);
            SetProcess(false);
        }
    }

    public override void _ExitTree()
    {
        if (logger != null) { OS.RemoveLogger(logger); logger = null; }
        server?.Dispose();
        server = null;
        if (!string.IsNullOrEmpty(infoFile)) Ms2.Devtools.DebugServer.RemoveInfoFile(infoFile);
        if (instance == this) instance = null;
    }

    public override void _Process(double delta)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        frames++;
        if (server == null || server.Queue.Idle || inRequest) { ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; return; }
        busyFrames++;
        inRequest = true;
        try { server.Queue.Drain(Dispatch, FrameBudgetMs); }
        finally { inRequest = false; ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; }
    }

    JsonNode Dispatch(Request r)
    {
        switch (r.Cmd)
        {
            case "ping": return new JsonObject { ["pid"] = System.Environment.ProcessId, ["frame"] = (long)Engine.GetProcessFrames(), ["fps"] = Engine.GetFramesPerSecond(), ["endpoint"] = new JsonObject { ["frames"] = frames, ["busyFrames"] = busyFrames, ["processMs"] = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency } };
            case "tree": return Tree(r);
            case "node": return NodeProps(r);
            case "material": return MaterialInfo(r);
            case "find": return Find(r);
            case "pick": return Pick(r);
            case "screenshot": return Screenshot(r);
            case "monitors": return Monitors(r);
            case "input": return Input(r);
            case "log": return Log(r);
            case "as3": return As3(r);
            default: throw new ArgumentException("unknown command " + r.Cmd);
        }
    }

    // ---- shared helpers ------------------------------------------------------------------------------------------

    Node Resolve(Request r, string key = "path")
    {
        string path = r.GetString(key) ?? "/root";
        var root = GetTree().Root;
        if (path == "" || path == "/") return root;
        var n = path.StartsWith("/") ? GetNodeOrNull(path) : root.GetNodeOrNull(path);
        return n ?? throw new ArgumentException("no node at " + path);
    }
}
#endif
