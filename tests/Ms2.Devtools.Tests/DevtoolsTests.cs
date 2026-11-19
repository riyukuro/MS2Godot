using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Ms2.Devtools;
using NUnit.Framework;

namespace Ms2.Devtools.Tests;

public class ProtocolTests
{
    [Test]
    public void ParsesCommandTokenIdAndArguments()
    {
        var r = Request.Parse("{\"id\":7,\"cmd\":\"tree\",\"token\":\"abc\",\"path\":\"/root\",\"depth\":2,\"names\":[\"a\",\"b\"],\"csharp\":true}");
        Assert.That(r.ParseError, Is.Null);
        Assert.That(r.Cmd, Is.EqualTo("tree"));
        Assert.That(r.Token, Is.EqualTo("abc"));
        Assert.That(r.Id.GetValue<int>(), Is.EqualTo(7));
        Assert.That(r.GetString("path"), Is.EqualTo("/root"));
        Assert.That(r.GetInt("depth"), Is.EqualTo(2));
        Assert.That(r.GetStrings("names"), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(r.GetBool("csharp"), Is.True);
        Assert.That(r.GetInt("missing", 5), Is.EqualTo(5));
        Assert.That(r.GetInt("path", 9), Is.EqualTo(9), "a wrong type reads as absent");
    }

    [TestCase("")]
    [TestCase("not json")]
    [TestCase("[1,2]")]
    [TestCase("{\"token\":\"x\"}")]
    public void BadLinesBecomeParseErrorsNotExceptions(string line)
    {
        var r = Request.Parse(line);
        Assert.That(r.ParseError, Is.Not.Null);
    }

    [Test]
    public void ResponsesCarryTheIdAndRefuseHugeResults()
    {
        var ok = JsonNode.Parse(Response.Ok(JsonValue.Create(3), new JsonObject { ["a"] = 1 }));
        Assert.That(ok["id"].GetValue<int>(), Is.EqualTo(3));
        Assert.That(ok["ok"].GetValue<bool>(), Is.True);
        var big = JsonNode.Parse(Response.Ok(null, JsonValue.Create(new string('x', Response.MaxBytes + 1))));
        Assert.That(big["ok"].GetValue<bool>(), Is.False);
        var err = JsonNode.Parse(Response.Error(JsonValue.Create("q"), "boom"));
        Assert.That(err["error"].GetValue<string>(), Is.EqualTo("boom"));
    }
}

public class ServerTests
{
    static JsonNode Send(DebugServer s, string line)
    {
        // plays the main thread: answers whatever the socket thread queued
        var task = Task.Run(() => s.Handle(line));
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < until) { s.Queue.Drain(r => new JsonObject { ["echo"] = r.Cmd }, 5); Thread.Sleep(1); }
        return JsonNode.Parse(task.Result);
    }

    [Test]
    public void ARequestWithoutTheTokenIsRefusedAndNeverQueued()
    {
        using var s = new DebugServer("secret");
        var task = Task.Run(() => s.Handle("{\"cmd\":\"tree\",\"token\":\"wrong\"}"));
        Assert.That(task.Wait(2000), Is.True, "answered by the socket thread without the main thread");
        Assert.That(s.Queue.Idle, Is.True);
        var j = JsonNode.Parse(task.Result);
        Assert.That(j["ok"].GetValue<bool>(), Is.False);
        Assert.That(j["error"].GetValue<string>(), Does.Contain("token"));
        var none = Task.Run(() => s.Handle("{\"cmd\":\"tree\"}")).Result;
        Assert.That(JsonNode.Parse(none)["ok"].GetValue<bool>(), Is.False);
        Assert.That(s.Queue.Idle, Is.True);
    }

    [Test]
    public void ARequestWithTheTokenIsAnsweredOnTheDrainingThread()
    {
        using var s = new DebugServer("secret");
        var j = Send(s, "{\"id\":1,\"cmd\":\"ping\",\"token\":\"secret\"}");
        Assert.That(j["ok"].GetValue<bool>(), Is.True);
        Assert.That(j["result"]["echo"].GetValue<string>(), Is.EqualTo("ping"));
    }

    [Test]
    public void TheHandlerRunsOnlyWhereDrainIsCalled()
    {
        var q = new RequestQueue();
        int handlerThread = -1;
        var req = Request.Parse("{\"cmd\":\"x\",\"token\":\"t\"}");
        var waiter = Task.Run(() => q.Submit(req, 5000));
        while (q.Idle) Thread.Sleep(1);
        Assert.That(waiter.IsCompleted, Is.False, "the socket thread waits until the main thread drains");
        int mainThread = Environment.CurrentManagedThreadId;
        q.Drain(_ => { handlerThread = Environment.CurrentManagedThreadId; return JsonValue.Create(1); }, 5);
        Assert.That(handlerThread, Is.EqualTo(mainThread));
        Assert.That(waiter.Wait(2000), Is.True);
    }

    [Test]
    public void AThrowingHandlerBecomesAnErrorAndTheQueueKeepsWorking()
    {
        var q = new RequestQueue();
        var a = Task.Run(() => q.Submit(Request.Parse("{\"cmd\":\"boom\"}"), 5000));
        var b = Task.Run(() => q.Submit(Request.Parse("{\"cmd\":\"fine\"}"), 5000));
        var until = DateTime.UtcNow.AddSeconds(5);
        while ((!a.IsCompleted || !b.IsCompleted) && DateTime.UtcNow < until)
        { q.Drain(r => r.Cmd == "boom" ? throw new InvalidOperationException("nope") : JsonValue.Create(1), 5); Thread.Sleep(1); }
        Assert.That(JsonNode.Parse(a.Result)["ok"].GetValue<bool>(), Is.False);
        Assert.That(JsonNode.Parse(a.Result)["error"].GetValue<string>(), Does.Contain("nope"));
        Assert.That(JsonNode.Parse(b.Result)["ok"].GetValue<bool>(), Is.True);
    }

    [Test]
    public void ATimedOutRequestIsSkippedByTheMainThread()
    {
        var q = new RequestQueue();
        var req = Request.Parse("{\"cmd\":\"slow\"}");
        string line = q.Submit(req, 30);
        Assert.That(JsonNode.Parse(line)["error"].GetValue<string>(), Does.Contain("timed out"));
        int ran = 0;
        q.Drain(_ => { ran++; return null; }, 5);
        Assert.That(ran, Is.EqualTo(0));
    }

    [Test]
    public void DrainStopsWhenTheBudgetIsUsedButAlwaysRunsOne()
    {
        var q = new RequestQueue();
        // each submitter blocks until answered: give each its own thread, since on a runner with few cores the pool would
        // start only some of them and inject the rest after the drain below had already found the queue idle
        using var started = new CountdownEvent(6);
        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Factory.StartNew(() => { started.Signal(); return q.Submit(Request.Parse("{\"cmd\":\"w\"}"), 10000); },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Assert.That(started.Wait(5000), Is.True);
        Thread.Sleep(100);
        int n = q.Drain(_ => { Thread.Sleep(20); return null; }, 0.0001);
        Assert.That(n, Is.EqualTo(1), "a spent budget still answers one request");
        // drain until every submitter has its answer (not until the queue looks idle: a submitter may still be enqueueing)
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!tasks.All(t => t.IsCompleted) && clock.ElapsedMilliseconds < 5000) { q.Drain(_ => null, 50); Thread.Sleep(1); }
        Assert.That(tasks.All(t => t.IsCompleted), Is.True);
    }

    [Test]
    public void OverTcpTheTokenGatesTheConnectionAndLinesRoundTrip()
    {
        using var s = new DebugServer("tok");
        var pump = new CancellationTokenSource();
        var main = Task.Run(() => { while (!pump.IsCancellationRequested) { s.Queue.Drain(r => new JsonObject { ["cmd"] = r.Cmd }, 5); Thread.Sleep(1); } });
        try
        {
            using var c = new TcpClient();
            c.Connect("127.0.0.1", s.Port);
            c.ReceiveTimeout = 5000;
            var stream = c.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            void Write(string l) { var b = Encoding.UTF8.GetBytes(l + "\n"); stream.Write(b, 0, b.Length); }
            Write("{\"id\":2,\"cmd\":\"ping\",\"token\":\"tok\"}");
            var ok = JsonNode.Parse(reader.ReadLine());
            Assert.That(ok["id"].GetValue<int>(), Is.EqualTo(2));
            Assert.That(ok["result"]["cmd"].GetValue<string>(), Is.EqualTo("ping"));
        }
        finally { pump.Cancel(); main.Wait(2000); }
    }

    [Test]
    public void TheListenerIsLoopbackOnAndTheInfoFileHoldsPortTokenPid()
    {
        using var s = new DebugServer();
        Assert.That(s.Port, Is.GreaterThan(0));
        Assert.That(s.Token, Has.Length.EqualTo(32));
        string file = Path.Combine(Path.GetTempPath(), "devtools-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            s.WriteInfoFile(file);
            var j = JsonNode.Parse(File.ReadAllText(file));
            Assert.That(j["port"].GetValue<int>(), Is.EqualTo(s.Port));
            Assert.That(j["token"].GetValue<string>(), Is.EqualTo(s.Token));
            Assert.That(j["pid"].GetValue<int>(), Is.EqualTo(Environment.ProcessId));
            if (!OperatingSystem.IsWindows()) Assert.That(File.GetUnixFileMode(file), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
        finally { File.Delete(file); }
    }

    // ---- connection limits ------------------------------------------------------------------------------------------

    static TcpClient Connect(DebugServer s)
    {
        var c = new TcpClient();
        c.Connect("127.0.0.1", s.Port);
        c.ReceiveTimeout = 5000;
        return c;
    }

    static void WriteLine(TcpClient c, string l) { var b = Encoding.UTF8.GetBytes(l + "\n"); c.GetStream().Write(b, 0, b.Length); }

    /// <summary>True when the server closed the connection (end of stream or reset) within the receive timeout.</summary>
    static bool ClosedByServer(TcpClient c)
    {
        try { return c.GetStream().ReadByte() < 0; }
        catch (IOException) { return true; }
    }

    [Test]
    public void AWrongTokenOnTheFirstLineGetsAnErrorAndTheConnectionCloses()
    {
        using var s = new DebugServer("tok");
        using var c = Connect(s);
        WriteLine(c, "{\"id\":1,\"cmd\":\"ping\",\"token\":\"bad\"}");
        var reader = new StreamReader(c.GetStream(), Encoding.UTF8);
        Assert.That(JsonNode.Parse(reader.ReadLine())["ok"].GetValue<bool>(), Is.False);
        Assert.That(ClosedByServer(c), Is.True);
    }

    [Test]
    public void ADuplicateKeyIsAnErrorLineNotADroppedConnection()
    {
        var r = Request.Parse("{\"cmd\":\"a\",\"cmd\":\"b\",\"token\":\"t\"}");
        Assert.That(r.ParseError, Does.Contain("bad JSON"));
        using var s = new DebugServer("tok");
        using var c = Connect(s);
        WriteLine(c, "{\"cmd\":\"a\",\"cmd\":\"b\"}");
        var reader = new StreamReader(c.GetStream(), Encoding.UTF8);
        Assert.That(JsonNode.Parse(reader.ReadLine())["ok"].GetValue<bool>(), Is.False);
    }

    [Test]
    public void AConnectionBeyondTheCapIsClosedAndAFreedSlotIsReused()
    {
        using var s = new DebugServer("tok") { FirstLineTimeoutMs = 60000 };
        var held = new List<TcpClient>();
        try
        {
            for (int i = 0; i < DebugServer.MaxConnections; i++) { held.Add(Connect(s)); Thread.Sleep(30); }
            using var extra = Connect(s);
            Assert.That(ClosedByServer(extra), Is.True, "the ninth connection is refused");
            held[0].Close();
            Thread.Sleep(300);
            using var again = Connect(s);
            again.ReceiveTimeout = 500;
            Assert.That(() => again.GetStream().ReadByte(), Throws.InstanceOf<IOException>(), "accepted: it waits for a request instead of being closed");
        }
        finally { foreach (var c in held) c.Close(); }
    }

    [Test]
    public void ALineOverTheLimitIsAnsweredWithAnErrorAndClosed()
    {
        using var s = new DebugServer("tok") { FirstLineTimeoutMs = 60000 };
        using var c = Connect(s);
        var chunk = new byte[64 * 1024];
        Array.Fill(chunk, (byte)'x');
        var stream = c.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        try { for (int sent = 0; sent <= Request.MaxLineBytes + chunk.Length; sent += chunk.Length) stream.Write(chunk, 0, chunk.Length); }
        catch (IOException) { }
        string line = reader.ReadLine();
        Assert.That(JsonNode.Parse(line)["error"].GetValue<string>(), Does.Contain("too long"));
    }

    static bool TrickleIsClosed(byte[] bytes)
    {
        using var s = new DebugServer("tok") { FirstLineTimeoutMs = 300 };
        using var c = Connect(s);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stream = c.GetStream();
        try
        {
            for (int i = 0; i < 40 && sw.ElapsedMilliseconds < 4000; i++) { stream.Write(bytes, 0, bytes.Length); stream.Flush(); Thread.Sleep(200); }
        }
        catch (IOException) { }
        return sw.ElapsedMilliseconds < 2500;   // the writes stop failing or the loop ends only after the server closed
    }

    [Test]
    public void ABytePerReadTrickleCannotOutlastTheFirstLineDeadline() => Assert.That(TrickleIsClosed(new[] { (byte)'{' }), Is.True);

    [Test]
    public void BlankLinesTrickleCannotOutlastTheFirstLineDeadline() => Assert.That(TrickleIsClosed(new[] { (byte)'\n' }), Is.True);

    [Test]
    public void ASilentNewConnectionIsClosedAfterTheFirstLineTimeout()
    {
        using var s = new DebugServer("tok") { FirstLineTimeoutMs = 300 };
        using var c = Connect(s);
        Assert.That(ClosedByServer(c), Is.True);
    }

    [Test]
    public void AnAuthenticatedConnectionIsClosedAfterTheIdleTimeout()
    {
        using var s = new DebugServer("tok") { FirstLineTimeoutMs = 5000, IdleTimeoutMs = 300 };
        var pump = new CancellationTokenSource();
        var main = Task.Run(() => { while (!pump.IsCancellationRequested) { s.Queue.Drain(_ => new JsonObject(), 5); Thread.Sleep(1); } });
        try
        {
            using var c = Connect(s);
            WriteLine(c, "{\"id\":1,\"cmd\":\"ping\",\"token\":\"tok\"}");
            var reader = new StreamReader(c.GetStream(), Encoding.UTF8);
            Assert.That(JsonNode.Parse(reader.ReadLine())["ok"].GetValue<bool>(), Is.True);
            Assert.That(ClosedByServer(c), Is.True);
        }
        finally { pump.Cancel(); main.Wait(2000); }
    }

    [Test]
    public void DisposeAnswersWaitingRequestsWithAnError()
    {
        var s = new DebugServer("tok");
        var waiting = Task.Run(() => s.Handle("{\"id\":5,\"cmd\":\"tree\",\"token\":\"tok\"}"));
        while (s.Queue.Idle) Thread.Sleep(1);
        s.Dispose();
        Assert.That(waiting.Wait(3000), Is.True, "not left to wait out the request timeout");
        var j = JsonNode.Parse(waiting.Result);
        Assert.That(j["ok"].GetValue<bool>(), Is.False);
        Assert.That(j["error"].GetValue<string>(), Does.Contain("stopped"));
    }

    [Test]
    public void TheInfoFileIsRemovedOnlyByTheProcessItNames()
    {
        using var s = new DebugServer();
        string file = Path.Combine(Path.GetTempPath(), "devtools-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, "{\"port\":1,\"token\":\"x\",\"pid\":" + (Environment.ProcessId + 1) + "}");
            DebugServer.RemoveInfoFile(file);
            Assert.That(File.Exists(file), Is.True, "another game's file stays");
            s.WriteInfoFile(file);
            DebugServer.RemoveInfoFile(file);
            Assert.That(File.Exists(file), Is.False);
        }
        finally { File.Delete(file); }
    }
}

public class ReflectionDumpTests
{
    [Test]
    public void CustomRendererSkipsTheRootObjectButSeesNested()
    {
        var d = new ReflectionDump { Custom = o => o is Node1 ? (System.Text.Json.Nodes.JsonNode)"brief" : null };
        var root = new Node1 { Next = new Node1() };
        var j = d.Dump(root).AsObject();
        Assert.That(j["Name"]!.GetValue<string>(), Is.EqualTo("root"));   // the root is dumped in full
        Assert.That(j["Next"]!.GetValue<string>(), Is.EqualTo("brief"));  // nested objects go through the renderer
    }

#pragma warning disable CS0414 // read only through reflection
    class Node1 { public string Name = "root"; int hidden = 5; public Node1 Next; public Node1 Back; public int Auto { get; set; } = 9; public int Computed => throw new Exception("getter ran"); public static int Counter = 3; public Action Callback = () => { }; }
    class Base { int secret = 11; }
#pragma warning restore CS0414
    class Derived : Base { public int Open = 1; }
    class Holder { public List<int> Many = Enumerable.Range(0, 500).ToList(); public Dictionary<string, int> Map = new() { ["a"] = 1 }; public HashSet<string> Set = new() { "x" }; public int[] Arr = { 1, 2, 3 }; public string Long = new string('z', 5000); }

    [Test]
    public void ShowsPublicAndPrivateFieldsAndAutoProperties()
    {
        var j = new ReflectionDump().Dump(new Node1()) as JsonObject;
        Assert.That(j["Name"].GetValue<string>(), Is.EqualTo("root"));
        Assert.That(j["hidden"].GetValue<int>(), Is.EqualTo(5));
        Assert.That(j["Auto"].GetValue<int>(), Is.EqualTo(9));
        Assert.That(j.ContainsKey("Computed"), Is.False, "computed getters never run");
        Assert.That(j.ContainsKey("static Counter"), Is.False);
        Assert.That(j["Callback"].GetValue<string>(), Does.StartWith("<"));
    }

    [Test]
    public void StaticsOptionAddsStaticFields()
    {
        var j = new ReflectionDump { IncludeStatics = true }.Dump(new Node1()) as JsonObject;
        Assert.That(j["static Counter"].GetValue<int>(), Is.EqualTo(3));
        Assert.That(j.ContainsKey("Computed()"), Is.False);
    }

    class GameList : List<int>
    {
        public static bool Ran;
        public new int Count { get { Ran = true; return base.Count; } }
        public new IEnumerator<int> GetEnumerator() { Ran = true; return base.GetEnumerator(); }
    }
    class GameKey { public override string ToString() { GameList.Ran = true; return "k"; } }
    class GameCollection : System.Collections.ICollection
    {
        public int Count { get { GameList.Ran = true; return 1; } }
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public void CopyTo(Array a, int i) { GameList.Ran = true; }
        public System.Collections.IEnumerator GetEnumerator() { GameList.Ran = true; return new[] { 1 }.GetEnumerator(); }
        public int Field = 4;
    }

    [Test]
    public void GameCollectionTypesRunNoGameCode()
    {
        GameList.Ran = false;
        var j = new ReflectionDump().Dump(new GameCollection()) as JsonObject;
        Assert.That(j["Field"].GetValue<int>(), Is.EqualTo(4), "dumped as an object, not enumerated");
        var d = new ReflectionDump().Dump(new Dictionary<GameKey, int> { [new GameKey()] = 1 }) as JsonObject;
        Assert.That(((JsonObject)d["entries"]).ContainsKey("GameKey"), Is.True);
        Assert.That(ReflectionDump.Navigate(new GameCollection(), "x", out var err), Is.Null);
        Assert.That(GameList.Ran, Is.False, "no game Count, enumerator or ToString ran");
    }

    [Test]
    public void BaseClassPrivateFieldsAreIncluded()
    {
        var j = new ReflectionDump().Dump(new Derived()) as JsonObject;
        Assert.That(j["Open"].GetValue<int>(), Is.EqualTo(1));
        Assert.That(j["secret"].GetValue<int>(), Is.EqualTo(11));
    }

    [Test]
    public void CyclesAreCut()
    {
        var a = new Node1 { Name = "a" }; a.Next = new Node1 { Name = "b", Back = a }; a.Next.Next = a;
        var j = new ReflectionDump { MaxDepth = 6 }.Dump(a) as JsonObject;
        var b = j["Next"] as JsonObject;
        Assert.That(b["Name"].GetValue<string>(), Is.EqualTo("b"));
        Assert.That(((JsonObject)b["Back"]).ContainsKey("$cycle"), Is.True);
        Assert.That(((JsonObject)b["Next"]).ContainsKey("$cycle"), Is.True);
    }

    [Test]
    public void DepthLimitGivesABriefStub()
    {
        var a = new Node1 { Name = "a", Next = new Node1 { Name = "b", Next = new Node1 { Name = "c" } } };
        var j = new ReflectionDump { MaxDepth = 1 }.Dump(a) as JsonObject;
        var b = (JsonObject)j["Next"];
        Assert.That(b.ContainsKey("Name"), Is.False);
        Assert.That(b["$type"].GetValue<string>(), Is.EqualTo("Node1"));
    }

    [Test]
    public void CollectionsAndStringsAreTruncated()
    {
        var j = new ReflectionDump { MaxItems = 4, MaxStringLength = 10 }.Dump(new Holder()) as JsonObject;
        var many = (JsonArray)j["Many"];
        Assert.That(many.Count, Is.EqualTo(5), "4 items and the 'more' note");
        Assert.That(many[4].GetValue<string>(), Does.Contain("496 more"));
        Assert.That(((JsonArray)j["Arr"]).Count, Is.EqualTo(3));
        Assert.That(((JsonArray)j["Set"])[0].GetValue<string>(), Is.EqualTo("x"));
        Assert.That(((JsonObject)j["Map"])["entries"]["a"].GetValue<int>(), Is.EqualTo(1));
        Assert.That(j["Long"].GetValue<string>(), Does.StartWith("zzzzzzzzzz…"));
    }

    [Test]
    public void NavigateFollowsFieldsAndIndexes()
    {
        var h = new Holder();
        Assert.That(ReflectionDump.Navigate(h, "Many[3]", out var err), Is.EqualTo(3));
        Assert.That(err, Is.Null);
        var a = new Node1 { Name = "a", Next = new Node1 { Name = "b" } };
        Assert.That(ReflectionDump.Navigate(a, "Next.Name", out _), Is.EqualTo("b"));
        Assert.That(ReflectionDump.Navigate(a, "Next.hidden", out _), Is.EqualTo(5), "private fields too");
        Assert.That(ReflectionDump.Navigate(a, "Auto", out _), Is.EqualTo(9), "auto-property via its backing field");
        ReflectionDump.Navigate(a, "Nope", out err);
        Assert.That(err, Does.Contain("no member"));
        ReflectionDump.Navigate(h, "Many[999]", out err);
        Assert.That(err, Does.Contain("out of range"));
    }

    [Test]
    public void TheValueBudgetBoundsAHugeGraph()
    {
        var wide = new List<object>();
        for (int i = 0; i < 200; i++) wide.Add(new Holder());
        var dump = new ReflectionDump { MaxItems = 200, MaxDepth = 6, MaxValues = 500 };
        var j = dump.Dump(wide);
        Assert.That(j.ToJsonString(), Does.Contain("value budget"));
    }
}
