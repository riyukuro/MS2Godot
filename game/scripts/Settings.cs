using Godot;

namespace Ms2Godot;

/// <summary>
/// Local paths, never stored in the repo. Order: environment (MS2_DATA, MS2_NIFXML), then res://local.cfg,
/// then user://settings.cfg (section [paths], keys data and nifxml). The first-run setup screen writes user://settings.cfg.
/// When nothing names a nif.xml: the repo's external/nifxml submodule (a source checkout), then the copy the setup
/// screen downloaded to user://nifxml/nif.xml (exported builds).
/// </summary>
public static class Settings
{
    public const string UserConfig = "user://settings.cfg";
    public const string DownloadedNifXml = "user://nifxml/nif.xml";

    public static string DataDir => Get("MS2_DATA", "data");
    public static string NifXml => Get("MS2_NIFXML", "nifxml") ?? SubmoduleNifXml ?? (Godot.FileAccess.FileExists(DownloadedNifXml) ? ProjectSettings.GlobalizePath(DownloadedNifXml) : null);

    /// <summary>external/nifxml/nif.xml next to the Godot project (git submodule), or null when not checked out.</summary>
    public static string SubmoduleNifXml
    {
        get
        {
            var res = ProjectSettings.GlobalizePath("res://");
            if (string.IsNullOrEmpty(res)) return null;
            var f = System.IO.Path.GetFullPath(System.IO.Path.Combine(res, "..", "external", "nifxml", "nif.xml"));
            return System.IO.File.Exists(f) ? f : null;
        }
    }

    /// <summary>Both paths are set and usable.</summary>
    public static bool Ready => DataProblem(DataDir) == null && NifProblem(NifXml) == null;

    /// <summary>Null when the folder looks like a client Data folder, else what is missing.</summary>
    public static string DataProblem(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return "No folder chosen.";
        if (!System.IO.Directory.Exists(dir)) return "The folder does not exist.";
        if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "Xml.m2d"))) return "Xml.m2d is not in this folder. Choose the client's Data folder.";
        var model = System.IO.Path.Combine(dir, "Resource", "Model");
        if (!System.IO.Directory.Exists(model) || System.IO.Directory.GetFiles(model, "*.m2d").Length == 0) return "Resource/Model has no .m2d packs.";
        return null;
    }

    public static string NifProblem(string file)
    {
        if (string.IsNullOrEmpty(file)) return "No nif.xml chosen.";
        if (!System.IO.File.Exists(file)) return "The file does not exist.";
        using var r = new System.IO.StreamReader(file);
        var head = new char[400];
        int n = r.Read(head, 0, head.Length);
        return new string(head, 0, n).Contains("<niftoolsxml") ? null : "This is not a niftools nif.xml.";
    }

    /// <summary>Writes the given paths (null keeps the current value) to user://settings.cfg.</summary>
    public static Error Save(string data, string nifxml)
    {
        var cfg = new ConfigFile();
        cfg.Load(UserConfig);
        if (data != null) cfg.SetValue("paths", "data", data);
        if (nifxml != null) cfg.SetValue("paths", "nifxml", nifxml);
        return cfg.Save(UserConfig);
    }

    static string Get(string env, string key)
    {
        var v = System.Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrEmpty(v)) return v;
        foreach (var path in new[] { "res://local.cfg", UserConfig })
        {
            var cfg = new ConfigFile();
            if (cfg.Load(path) != Error.Ok) continue;
            var s = (string)cfg.GetValue("paths", key, "");
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return null;
    }
}
