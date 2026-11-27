using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>
/// Every place in game/ that runs code on another thread (Task.Run, ContinueWith, a Thread, the thread pool, Parallel,
/// WorkerThreadPool, ConfigureAwait(false), a timer) is in the reviewed table below. Its body makes no direct engine call
/// (GD, ProjectSettings, OS, the servers, a deferred call, a new engine object), or the table names the gate it runs under.
/// An engine call made on a worker after the quit reached the engine's teardown and crashed the exit (SIGSEGV, exit 139):
/// Godot frees the script languages, the servers and the message queue in Main::cleanup, after the tree has left
/// (godot_linuxbsd.cpp:122-126, scene_tree.cpp:861-863, main.cpp:5259-5382, Godot 4.7.2). A new place fails here until it
/// is reviewed and added; the calls a body makes through other methods are what the review checks (the reasons below).
/// </summary>
public class PoolThreadEngineCallTests
{
    /// <summary>A reviewed place: the file under game/, a text its statement holds, and why it is safe.</summary>
    sealed record Site(string File, string Key, string Gate, string Why);

    static readonly Site[] Reviewed =
    {
        new("audio/FieldAudio.cs", "run.Rest(run.Rate, () => entry.Rest.KeepDecoding)", "poolGate",
            "DecodeSegment, every music segment's decode (the cue's, the next one's, an outgoing voice's): MpegFrameRun, SafeDecode " +
            "and ToBytes are Ms2.Audio (no engine); the warnings, the stream build and the rest's data, loop end and loop mode " +
            "set on that stream run under poolGate while poolClosed is unset (_ExitTree sets it); the rest goes to the main thread"),
        new("audio/AmbientSounds.cs", "SafeDecode.Decode(clip, out var failure)", null,
            "MpegDecoder and the channel correlation and power share (Player3DOutput.Channels, Ms2.Audio: no engine); the warning goes through EngineCalls"),
        new("audio/AmbientSounds.cs", "started.Enqueue(() => Start(sound, pcm, stereo, data, begin, end))", null,
            "bytes only (EventFadeIn.Bytes, Ms2.Audio: no engine); the stream is built on the main thread"),
        new("scripts/SetupScreen.cs", "UiCode.Build(data, progress)", null,
            "the setup build: UiGenerator, crossgen2, src tables; the folders are read on the main thread first (UiCode.RootPath, " +
            "Precompiled.AssemblyDirs) and its log lines go through EngineCalls (DataCache, UiCode, Precompiled.Compile too)"),
        new("scripts/world/FieldContents.cs", "Npcs(packs); Regions(packs);", null,
            "src tables; the options (whose first load calls the engine) are read on the main thread first"),
        new("scripts/NavDebug.cs", "FieldNavigation.Load(", null,
            "Ms2.Navigation (no engine); the folder is globalized on the main thread"),
        new("ui/UiAssets.cs", "ui.Dependency(k)", null,
            "SwfMovie parse and font bytes (no engine objects)"),
        new("ui/HudScene.CutSceneSwf.cs", "cutSceneSwfSoundReady.Enqueue(() => FinishCutSceneSwfSound(", null,
            "the swf cut scene's stream sound: SwfMovie parse and the PCM decode (no engine); the player is built on the main thread"),
        new("net/LiveSession.cs", "live: send {pk.GetType().Name} failed", null,
            "a failed send: the warning goes through EngineCalls"),
        new("net/LiveSession.cs", "live: login send {pk.GetType().Name} failed", null,
            "a failed login send: the warning goes through EngineCalls"),
        new("net/LiveSession.cs", "ContinueWith(_ => active.Dispose()", null,
            "closes the socket (Ms2.Net, no engine)"),
        new("tests/OptionsThreadsCheck.cs", "new Thread(() =>", null,
            "the options-threads check scene: reads the options from 8 threads on purpose and joins them before it quits"),
    };

    static readonly Regex Entry = new(
        @"Task\.Run\(|Task\.Factory\.StartNew\(|\.ContinueWith\(|new (System\.Threading\.)?Thread\(|ThreadPool\.\w*QueueUserWorkItem\(|" +
        @"Parallel\.(For|ForEach|Invoke)\w*\(|WorkerThreadPool\.\w+\(|\.ConfigureAwait\(false\)|new (System\.Threading\.)?Timer\(");

    static readonly Regex EngineCall = new(
        @"\b(GD|ProjectSettings|OS|Engine|ResourceLoader|RenderingServer|DisplayServer|AudioServer|PhysicsServer[23]D|Input|Time)\.\w+|" +
        @"CallDeferred|Callable\.From|EmitSignal|\bGetTree\(\)|new (Godot\.)?\w*(Texture\w*|Image|AudioStream\w*|Mesh|Material|Shader|Node\w*)\b");

    [Test]
    public void EveryWorkerThreadEntryIsReviewedAndMakesNoDirectEngineCall()
    {
        var game = GameDir();
        var found = new List<(string File, int Line, string Body)>();
        foreach (var path in Directory.EnumerateFiles(game, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(game, path).Replace(Path.DirectorySeparatorChar, '/');
            if (rel.StartsWith(".godot/", StringComparison.Ordinal) || rel.Contains("/obj/")) continue;
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            foreach (Match m in Entry.Matches(text))
                found.Add((rel, text[..m.Index].Count(c => c == '\n') + 1, Statement(text, m.Index)));
        }

        var problems = new List<string>();
        var used = new HashSet<Site>();
        foreach (var (file, line, body) in found)
        {
            var sites = Reviewed.Where(s => s.File == file && body.Contains(s.Key, StringComparison.Ordinal)).ToList();
            if (sites.Count != 1)
            {
                problems.Add($"game/{file}:{line}: {(sites.Count == 0 ? "a worker-thread entry not in the reviewed table" : "matches several table rows")}");
                continue;
            }
            var site = sites[0];
            used.Add(site);
            if (site.Gate != null)
            {
                if (!body.Contains($"lock ({site.Gate})", StringComparison.Ordinal))
                    problems.Add($"game/{file}:{line}: the engine calls are not under lock ({site.Gate})");
            }
            else if (EngineCall.Match(body) is { Success: true } call)
                problems.Add($"game/{file}:{line}: a direct engine call on the worker: {call.Value}");
        }
        foreach (var s in Reviewed.Where(s => !used.Contains(s)))
            problems.Add($"game/{s.File}: the table row \"{s.Key.Split('\n')[0]}\" matches no entry (removed or changed: update the table)");

        Assert.That(found, Has.Count.EqualTo(Reviewed.Length), string.Join("\n", problems));
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>The statement from the entry to the close of its argument list (comments and string contents skipped for the count).</summary>
    static string Statement(string text, int at)
    {
        int open = text.IndexOf('(', at);
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { i = text.IndexOf('\n', i); if (i < 0) break; continue; }
            if (c == '"' || c == '\'')
            {
                for (i++; i < text.Length && text[i] != c; i++) if (text[i] == '\\') i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return text[at..(i + 1)];
        }
        return text[at..];
    }

    static string GameDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Ms2.Formats")) && File.Exists(Path.Combine(d.FullName, "game", "project.godot")))
                return Path.Combine(d.FullName, "game");
        Assert.Ignore("source tree not found");
        return null!;
    }
}
