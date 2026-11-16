using Godot;

namespace Ms2Godot;

/// <summary>
/// Local paths, never stored in the repo. Order: environment (MS2_DATA, MS2_NIFXML), then res://local.cfg,
/// then user://settings.cfg (section [paths], keys data and nifxml).
/// </summary>
public static class Settings
{
    public static string DataDir => Get("MS2_DATA", "data");
    public static string NifXml => Get("MS2_NIFXML", "nifxml");

    static string Get(string env, string key)
    {
        var v = System.Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrEmpty(v)) return v;
        foreach (var path in new[] { "res://local.cfg", "user://settings.cfg" })
        {
            var cfg = new ConfigFile();
            if (cfg.Load(path) != Error.Ok) continue;
            var s = (string)cfg.GetValue("paths", key, "");
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return null;
    }
}
