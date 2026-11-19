#if MS2_DEVTOOLS
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ms2.Devtools;

/// <summary>Builds the response line for a request: {"id":..,"ok":true,"result":..} or {"id":..,"ok":false,"error":".."}.</summary>
public static class Response
{
    public const int MaxBytes = 8 << 20;

    public static string Ok(JsonNode id, JsonNode result)
    {
        var o = new JsonObject { ["id"] = id?.DeepClone(), ["ok"] = true, ["result"] = result };
        string s = o.ToJsonString();
        return s.Length > MaxBytes ? Error(id, "result larger than " + MaxBytes + " bytes; narrow the request (depth, filter, props)") : s;
    }

    public static string Error(JsonNode id, string message) =>
        new JsonObject { ["id"] = id?.DeepClone(), ["ok"] = false, ["error"] = message }.ToJsonString();
}
#endif
