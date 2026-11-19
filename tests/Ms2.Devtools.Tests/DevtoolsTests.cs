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
        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(() => q.Submit(Request.Parse("{\"cmd\":\"w\"}"), 10000))).ToArray();
        while (Enumerable.Range(0, 100).All(_ => q.Idle) && !tasks.Any(t => t.IsCompleted)) Thread.Sleep(5);
        Thread.Sleep(100);
        int n = q.Drain(_ => { Thread.Sleep(20); return null; }, 0.0001);
        Assert.That(n, Is.EqualTo(1), "a spent budget still answers one request");
        while (!q.Idle) q.Drain(_ => null, 50);
        Assert.That(Task.WaitAll(tasks, 5000), Is.True);
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
            Write("{\"id\":1,\"cmd\":\"ping\",\"token\":\"bad\"}");
            Assert.That(JsonNode.Parse(reader.ReadLine())["ok"].GetValue<bool>(), Is.False);
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

    class Node1 { public string Name = "root"; int hidden = 5; public Node1 Next; public Node1 Back; public int Auto { get; set; } = 9; public int Computed => throw new Exception("getter ran"); public static int Counter = 3; public Action Callback = () => { }; }
    class Base { int secret = 11; }
    class Derived : Base { public int Open = 1; }
    class Holder { public List<int> Many = Enumerable.Range(0, 500).ToList(); public Dictionary<string, int> Map = new() { ["a"] = 1 }; public HashSet<string> Set = new() { "x" }; public int[] Arr = { 1, 2, 3 }; public string Long = new string('z', 5000); }

    [Test]
    public void ShowsPublicAndPrivateFieldsAndAutoProperties()
    {
        var j = new ReflectionDump().Dump(new Node1()) as JsonObject;
        Assert.That(j["Name"].GetValue<string>(), Is.EqualTo("root"));
        Assert.That(j["hidden"].GetValue<int>(), Is.EqualTo(5));
        Assert.That(j["Auto"].GetValue<int>(), Is.EqualTo(9));
        Assert.That(j.ContainsKey("Computed"), Is.False, "computed getters are not run by default");
        Assert.That(j.ContainsKey("static Counter"), Is.False);
        Assert.That(j["Callback"].GetValue<string>(), Does.StartWith("<"));
    }

    [Test]
    public void OptionsAddStaticsAndGetters()
    {
        var j = new ReflectionDump { IncludeStatics = true, CallGetters = true }.Dump(new Node1()) as JsonObject;
        Assert.That(j["static Counter"].GetValue<int>(), Is.EqualTo(3));
        Assert.That(j["Computed()"].GetValue<string>(), Does.StartWith("<error"), "a throwing getter is reported, not thrown");
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
