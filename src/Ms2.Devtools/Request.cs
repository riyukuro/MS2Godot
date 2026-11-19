#if MS2_DEVTOOLS
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ms2.Devtools;

/// <summary>
/// One request line: a JSON object with "cmd", "token" and an optional "id" (echoed back); every other member is an argument
/// of the command. Parsing never throws: a bad line becomes a <see cref="ParseError"/>.
/// </summary>
public sealed class Request
{
    public const int MaxLineBytes = 1 << 20;

    public JsonNode Id;
    public string Cmd;
    public string Token;
    public JsonObject Args;
    public string ParseError;

    /// <summary>Set by the socket thread when the caller gave up waiting: the main thread skips the request.</summary>
    public volatile bool Abandoned;

    public static Request Parse(string line)
    {
        var r = new Request();
        try
        {
            if (JsonNode.Parse(line, null, new JsonDocumentOptions { MaxDepth = 32 }) is not JsonObject o) { r.ParseError = "request is not a JSON object"; return r; }
            r.Args = o;
            r.Id = o["id"]?.DeepClone();
            r.Cmd = r.GetString("cmd");
            r.Token = r.GetString("token");
            if (string.IsNullOrEmpty(r.Cmd)) r.ParseError = "missing cmd";
        }
        catch (JsonException e) { r.ParseError = "bad JSON: " + e.Message; }
        return r;
    }

    // ---- argument readers (a wrong type reads as absent) ---------------------------------------------------------
    public string GetString(string key, string dflt = null) => Args?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : dflt;

    public int GetInt(string key, int dflt = 0)
    {
        if (Args?[key] is not JsonValue v) return dflt;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
        if (v.TryGetValue<double>(out var d) && double.IsFinite(d)) return (int)Math.Clamp(d, int.MinValue, int.MaxValue);
        return dflt;
    }

    public double GetDouble(string key, double dflt = 0)
    {
        if (Args?[key] is not JsonValue v) return dflt;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return l;
        return dflt;
    }

    public bool GetBool(string key, bool dflt = false) => Args?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : dflt;

    public bool Has(string key) => Args != null && Args.ContainsKey(key) && Args[key] != null;

    public List<string> GetStrings(string key)
    {
        var list = new List<string>();
        if (Args?[key] is JsonArray a) foreach (var n in a) { if (n is JsonValue v && v.TryGetValue<string>(out var s)) list.Add(s); }
        else if (GetString(key) is { } one) list.Add(one);
        return list;
    }
}
#endif
