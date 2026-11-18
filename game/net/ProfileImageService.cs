using Godot;
using Ms2.Ui;
using FileAccess = Godot.FileAccess;
using HttpClient = Godot.HttpClient;

namespace Ms2Godot.Net;

/// <summary>
/// The files behind <see cref="ProfileImages"/>: the client's download folder (Custom/Download/Profile) is
/// user://ugc/profile/, a requested key is fetched from the web host with an HttpRequest (CUgcDownloadProfile), and the
/// upload (CSoapClient::UploadUgcFile) posts to the host's urq.aspx. One node for the run, under the live session; the
/// pictures' paths work without it (every key then stays on the default image).
/// </summary>
public sealed partial class ProfileImageService : Node
{
    /// <summary>The download folder.</summary>
    public const string Dir = "user://ugc/profile/";

    static ProfileImages images;
    static ProfileImageService instance;
    static readonly Dictionary<string, ImageTexture> textures = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The run's paths and downloads; the default image is read from the data on first use.</summary>
    public static ProfileImages Images => images ??= New();

    static ProfileImages New()
    {
        var im = new ProfileImages { Exists = name => name.Length > 0 && FileAccess.FileExists(Dir + name) };
        try
        {
            var packs = Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir);
            if (packs.Exists("Xml", "table/commonresource.xml"))
            {
                var doc = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", "table/commonresource.xml")).TrimStart('﻿'));
                im.NoProfile = (string)doc.Root?.Element("DEFAULT_PROFILE_IMAGE_PATH")?.Attribute("value") ?? "";
            }
        }
        catch (Exception e) { GD.PushWarning("profile images: no default image (" + e.Message + ")"); }
        im.DownloadRequested += (key, url, name) => instance?.Fetch(key, url, name);
        return im;
    }

    /// <summary>The service node under <paramref name="parent"/>, once.</summary>
    public static void Attach(Node parent)
    {
        if (instance != null) return;
        parent.AddChild(instance = new ProfileImageService { Name = "ProfileImages", ProcessMode = ProcessModeEnum.Always });
        _ = Images;
    }

    public override void _ExitTree()
    {
        if (instance == this) instance = null;
    }

    /// <summary>The downloaded picture of a file name, or null while it is not there.</summary>
    public static Texture2D Texture(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/') || name.Contains("..")) return null;
        if (textures.TryGetValue(name, out var t)) return t;
        if (!FileAccess.FileExists(Dir + name)) return null;
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(Dir + name));
        if (img == null || img.IsEmpty()) return null;
        return textures[name] = ImageTexture.CreateFromImage(img);
    }

    void Fetch(string key, string url, string name)
    {
        if (string.IsNullOrEmpty(Images.Host)) { Images.Downloaded(key, false); return; }
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        string part = Dir + name + ".part";
        var http = new HttpRequest { DownloadFile = part, Timeout = 20 };
        AddChild(http);
        http.RequestCompleted += (result, code, _, _) =>
        {
            bool ok = result == (long)HttpRequest.Result.Success && code == 200 && FileAccess.FileExists(part);
            if (ok) ok = DirAccess.RenameAbsolute(part, Dir + name) == Error.Ok;
            if (!ok && FileAccess.FileExists(part)) DirAccess.RemoveAbsolute(part);
            if (ok) textures.Remove(name);
            GD.Print($"profile images: {(ok ? "downloaded" : $"download failed ({result}, {code})")} {key}");
            http.QueueFree();
            Images.Downloaded(key, ok);
        };
        if (http.Request(url) != Error.Ok) { http.QueueFree(); Images.Downloaded(key, false); }
    }

    /// <summary>
    /// UploadUgcFile for a profile picture: the body posted to urq.aspx up to 3 times, 500 ms apart, until an answer of two
    /// fields comes back; <paramref name="done"/> gets that answer's code and text (6 when none came).
    /// </summary>
    public static void Upload(byte[] body, Action<int, string> done)
    {
        var url = Images.UploadUrl;
        if (instance == null || url == null) { done(6, ""); return; }
        instance.Post(url, body, 3, done);
    }

    void Post(string url, byte[] body, int tries, Action<int, string> done)
    {
        var http = new HttpRequest { Timeout = 20 };
        AddChild(http);
        http.RequestCompleted += (result, code, _, reply) =>
        {
            http.QueueFree();
            var (c, text) = result == (long)HttpRequest.Result.Success ? ProfileImages.ParseUploadReply(reply) : (6, "");
            if (c == 6 && tries > 1)
            {
                GD.Print($"profile images: upload answer {code} '{System.Text.Encoding.UTF8.GetString(reply ?? Array.Empty<byte>())}', trying again");
                GetTree().CreateTimer(0.5).Timeout += () => Post(url, body, tries - 1, done);
                return;
            }
            done(c, text);
        };
        if (http.RequestRaw(url, new[] { "Content-Type: application/octet-stream" }, HttpClient.Method.Post, body) != Error.Ok)
        { http.QueueFree(); done(6, ""); }
    }
}
