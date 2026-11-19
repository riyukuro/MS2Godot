#if MS2_DEVTOOLS
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Ms2.Devtools;

/// <summary>
/// The debug endpoint's socket side: a loopback TCP listener on an OS-chosen port, one JSON object per line in, one per line
/// out. Every request needs the token; a request without it is refused on the socket thread and never reaches the queue. The
/// threads only parse, check and queue (<see cref="RequestQueue"/>): the main thread answers.
/// </summary>
public sealed class DebugServer : IDisposable
{
    public const int MaxConnections = 8;
    public const int RequestTimeoutMs = 15000;

    readonly TcpListener listener;
    readonly Thread acceptor;
    readonly List<TcpClient> clients = new();
    volatile bool stopped;

    public int Port { get; }
    public string Token { get; }
    public RequestQueue Queue { get; } = new();

    public DebugServer(string token = null)
    {
        Token = token ?? NewToken();
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(4);
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptor = new Thread(AcceptLoop) { IsBackground = true, Name = "debug-accept" };
        acceptor.Start();
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public bool TokenOk(string given)
    {
        if (given == null) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(Token));
    }

    /// <summary>Checks a parsed request and returns the refusal line, or null when it may be queued.</summary>
    public string Refuse(Request req)
    {
        if (req.ParseError != null) return Response.Error(req.Id, req.ParseError);
        if (!TokenOk(req.Token)) return Response.Error(req.Id, "bad token");
        return null;
    }

    void AcceptLoop()
    {
        while (!stopped)
        {
            TcpClient c;
            try { c = listener.AcceptTcpClient(); }
            catch { return; }
            int count;
            lock (clients) { clients.RemoveAll(x => !x.Connected); count = clients.Count; if (count < MaxConnections) clients.Add(c); }
            if (count >= MaxConnections) { try { c.Close(); } catch { } continue; }
            new Thread(() => Serve(c)) { IsBackground = true, Name = "debug-conn" }.Start();
        }
    }

    void Serve(TcpClient c)
    {
        try
        {
            c.NoDelay = true;
            using var stream = c.GetStream();
            var buf = new byte[8192];
            var line = new MemoryStream();
            while (!stopped)
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n <= 0) return;
                for (int i = 0; i < n; i++)
                {
                    byte b = buf[i];
                    if (b != (byte)'\n') { line.WriteByte(b); if (line.Length > Request.MaxLineBytes) { Reply(stream, Response.Error(null, "request line too long")); return; } continue; }
                    string text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
                    line.SetLength(0);
                    if (text.Trim().Length == 0) continue;
                    Reply(stream, Handle(text));
                }
            }
        }
        catch { }
        finally { try { c.Close(); } catch { } }
    }

    /// <summary>One request line to one response line (socket thread; blocks until the main thread has answered).</summary>
    public string Handle(string text)
    {
        var req = Request.Parse(text);
        return Refuse(req) ?? Queue.Submit(req, RequestTimeoutMs);
    }

    static void Reply(NetworkStream s, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        s.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Writes {port, token, pid} to <paramref name="path"/> (replacing it in one step).</summary>
    static FileStreamOptions Private()
    {
        var o = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) o.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return o;
    }

    public void WriteInfoFile(string path)
    {
        var o = new JsonObject { ["port"] = Port, ["token"] = Token, ["pid"] = Environment.ProcessId };
        string tmp = path + ".tmp";
        using (var f = new FileStream(tmp, Private()))
            f.Write(Encoding.UTF8.GetBytes(o.ToJsonString()));
        File.Move(tmp, path, true);
    }

    public void Dispose()
    {
        stopped = true;
        try { listener.Stop(); } catch { }
        lock (clients) { foreach (var c in clients) { try { c.Close(); } catch { } } clients.Clear(); }
        Queue.FailAll("endpoint stopped");
    }
}
#endif
