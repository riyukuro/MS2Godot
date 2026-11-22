namespace Ms2.Net;

/// <summary>
/// The command line the Lith launcher starts the game exe with, and the only way to name the server and the ticket here:
/// a leading bare port, then <c>--nxapp=</c>, <c>--stage=</c>, <c>--ticket=</c>, <c>--ip=</c>, <c>--port=</c>, <c>--title=</c> and an optional
/// <c>--console</c>. <c>--ip</c> and <c>--port</c> are the login server (the bare port is the fallback for <c>--port</c>), <c>--ticket</c> the
/// login token. The others, and any unknown argument, are accepted and ignored, so the launcher's line can be pasted as is.
/// The ticket is a credential: <see cref="ToString"/> never prints it.
/// </summary>
public sealed record LauncherArgs(string Host, int Port, string Ticket)
{
    /// <summary>True when both <c>--ip</c> and a port (<c>--port</c> or the bare one) were given.</summary>
    public bool HasAddress => !string.IsNullOrEmpty(Host) && Port > 0;

    /// <summary>What is wrong with the arguments (never the values), or null. Set by <see cref="Parse"/>.</summary>
    public string Problem { get; init; }

    public override string ToString() => $"LauncherArgs {{ Host = {Host}, Port = {Port}, Ticket = {(string.IsNullOrEmpty(Ticket) ? "none" : "set")} }}";

    static bool TryPort(string s, out int port) =>
        int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port) && port is > 0 and <= 65535;

    public static LauncherArgs Parse(IReadOnlyList<string> args)
    {
        string host = null, ticket = null; int port = 0, barePort = 0; bool badPort = false, portGiven = false;
        for (int i = 0; i < (args?.Count ?? 0); i++)
        {
            string a = args[i];
            if (string.IsNullOrEmpty(a)) continue;
            if (!a.StartsWith("--", System.StringComparison.Ordinal))
            {
                // the launcher's leading bare port; a number after a bare switch (--shots 5) is that switch's value, not a port
                string prev = i > 0 ? args[i - 1] : null;
                bool afterValueSwitch = prev != null && prev.StartsWith("--", System.StringComparison.Ordinal) && prev.Length > 2 && !prev.Contains('=');
                if (!afterValueSwitch && barePort == 0 && TryPort(a, out int bp)) barePort = bp;
                continue;
            }
            int eq = a.IndexOf('=');
            if (eq < 0) continue;   // --console and other flags, "--" itself
            string key = a[2..eq], value = a[(eq + 1)..];
            switch (key)
            {
                case "ticket": ticket = value; break;
                case "ip": host = value; break;
                case "port": portGiven = true; if (TryPort(value, out int p)) port = p; else badPort = true; break;
            }
        }
        var r = new LauncherArgs(string.IsNullOrEmpty(host) ? null : host, port > 0 ? port : barePort, string.IsNullOrEmpty(ticket) ? null : ticket);
        string problem =
            badPort ? "--port is not a port number (1-65535)" :
            r.Host != null && r.Port <= 0 ? "--ip without --port" :
            r.Host == null && portGiven ? "--port without --ip" :
            r.Ticket != null && !r.HasAddress ? "--ticket without --ip and --port" : null;
        return problem == null ? r : r with { Problem = problem };
    }
}
