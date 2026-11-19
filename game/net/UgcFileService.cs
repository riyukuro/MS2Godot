using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// The files behind <see cref="UgcManager"/>: the client's Custom folders next to the exe are user://ugc/ here
/// (".\Custom\Image" is user://ugc/image/, ".\Custom\Thumbnail" user://ugc/thumbnail/; Custom\Download\Profile stays
/// <see cref="ProfileImageService.Dir"/>). Any other path (the Open File dialog's pick) is the file system's.
/// </summary>
public sealed class UgcFileService : IUgcFiles
{
    readonly Node host;

    /// <param name="host">The node the Open File dialog is added under.</param>
    public UgcFileService(Node host) { this.host = host; }

    /// <summary>The user:// path of a client path under ".\Custom", else the path as it is; a <paramref name="folder"/>
    /// path is all folder names.</summary>
    public static string Local(string path, bool folder = false)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var p = path.Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal)) p = p[2..];
        if (!p.StartsWith("custom/", StringComparison.OrdinalIgnoreCase)) return path;
        p = p["custom/".Length..];
        int slash = folder ? p.Length : p.LastIndexOf('/');
        // the folders as the client names them, any case; the file name kept
        return "user://ugc/" + (slash >= 0 ? p[..slash].ToLowerInvariant() + p[slash..] : p);
    }

    /// <summary>The file system path of a client path.</summary>
    public static string Global(string path, bool folder = false)
    {
        var l = Local(path, folder);
        return l.StartsWith("user://", StringComparison.Ordinal) || l.StartsWith("res://", StringComparison.Ordinal) ? ProjectSettings.GlobalizePath(l) : l;
    }

    public IEnumerable<UgcFileEntry> List(string folder)
    {
        var dir = new System.IO.DirectoryInfo(Global(folder, true));
        if (!dir.Exists) yield break;
        foreach (var e in dir.EnumerateFileSystemInfos())
            yield return new UgcFileEntry(e.Name, e.LastWriteTimeUtc.ToFileTimeUtc(), e.CreationTimeUtc.ToFileTimeUtc(),
                (e.Attributes & System.IO.FileAttributes.Directory) != 0);
    }

    public bool Exists(string path) => path.Length > 0 && (System.IO.File.Exists(Global(path)) || System.IO.Directory.Exists(Global(path)));

    public long? WriteTime(string path)
    {
        var f = new System.IO.FileInfo(Global(path));
        return f.Exists ? f.LastWriteTimeUtc.ToFileTimeUtc() : null;
    }

    public (int Width, int Height)? ImageSize(string path)
    {
        try
        {
            using var s = System.IO.File.OpenRead(Global(path));
            return UgcImageInfo.Size(s);
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// ConvertThumbnail: LoadFile's 0x3d when the image does not read, the old thumbnail deleted, scaled to size x size
    /// (CopySurfaces' D3DX_DEFAULT filter, a triangle filter: here the trilinear resize, which averages through the
    /// mipmaps), Save's 0x49 when the PNG is not written.
    /// </summary>
    public int MakeThumbnail(string src, string dst, int size)
    {
        var img = Image.LoadFromFile(Global(src));
        if (img == null || img.IsEmpty()) return 0x3d;
        string to = Global(dst);
        if (System.IO.File.Exists(to)) System.IO.File.Delete(to);
        if (img.IsCompressed() && img.Decompress() != Error.Ok) return 0x3f;
        img.Convert(Image.Format.Rgba8);
        img.Resize(size, size, Image.Interpolation.Trilinear);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
        return img.SavePng(to) == Error.Ok ? 0 : 0x49;
    }

    public void DeleteFiles(string folder)
    {
        var dir = new System.IO.DirectoryInfo(Global(folder, true));
        if (!dir.Exists) return;
        foreach (var f in dir.EnumerateFiles()) f.Delete();
    }

    public bool Copy(string src, string dst)
    {
        string to = Global(dst);
        if (System.IO.File.Exists(to)) return false;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
            System.IO.File.Copy(Global(src), to, false);
            return true;
        }
        catch (Exception) { return false; }
    }

    public byte[] Read(string path)
    {
        try { return System.IO.File.ReadAllBytes(Global(path)); }
        catch (Exception) { return null; }
    }

    public void Delete(string path)
    {
        try { System.IO.File.Delete(Global(path)); }
        catch (Exception) { }
    }

    /// <summary>explorer on the folder: the system's file manager (the folder made first, as explorer opens a missing one
    /// at its default).</summary>
    public void OpenFolder(string folder)
    {
        string g = Global(folder, true);
        System.IO.Directory.CreateDirectory(g);
        OS.ShellOpen(g);
    }

    /// <summary>mspaint on the file: the system's default program for it.</summary>
    public void OpenInEditor(string path) => OS.ShellOpen(Global(path));

    /// <summary>
    /// FUN_141b6e360: an Open File dialog in the client's folder (".\" is user://) for the filter's extensions; the pick's
    /// file system path, or "" when it is cancelled.
    /// </summary>
    public void PickImage(string dir, string filter, Action<string> picked)
    {
        var d = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, UseNativeDialog = false,
            CurrentDir = ProjectSettings.GlobalizePath(dir == "./" || dir == ".\\" ? "user://" : Local(dir, true)),
            Filters = new[] { "*.dds, *.png, *.jpg ; " + filter },
        };
        bool done = false;
        void Answer(string path)
        {
            if (done) return;
            done = true;
            d.QueueFree();
            picked(path);
        }
        d.FileSelected += p => Answer(p);
        d.Canceled += () => Answer("");
        host.AddChild(d);
        d.PopupCentered(new Vector2I(720, 480));
    }

    // ---- the movies' images ------------------------------------------------------------------------------------------

    static readonly Dictionary<string, (long Write, ImageTexture Texture)> textures = new(StringComparer.Ordinal);

    /// <summary>
    /// The texture of a movie url's path under Custom (the thumbnails "./Custom/Thumbnail/&lt;title&gt;.png"), null for any
    /// other path or a missing file. A file rewritten since (a thumbnail made again for a changed image) is read again.
    /// </summary>
    public static Texture2D TextureOfPath(string path)
    {
        string l = Local(path);
        if (!l.StartsWith("user://ugc/", StringComparison.Ordinal)) return null;
        string g = ProjectSettings.GlobalizePath(l);
        var f = new System.IO.FileInfo(g);
        if (!f.Exists) { textures.Remove(l); return null; }
        long write = f.LastWriteTimeUtc.Ticks;
        if (textures.TryGetValue(l, out var c) && c.Write == write) return c.Texture;
        var img = Image.LoadFromFile(g);
        if (img == null || img.IsEmpty()) return null;
        var t = ImageTexture.CreateFromImage(img);
        textures[l] = (write, t);
        return t;
    }
}
