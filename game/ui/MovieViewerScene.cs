using Godot;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui;

/// <summary>
/// Development viewer: renders the root timeline (or a frame label) of UI movies from the player's Gfx.m2d, one PNG each.
/// Args after `--`: --movies a,b,c --out DIR [--label NAME] [--scale S]
/// </summary>
public partial class MovieViewerScene : Control
{
    string[] movies = Array.Empty<string>(); string outDir, label; float scale = 0;
    UiAssets assets; int idx = -1, wait; Node current;

    public override void _Ready()
    {
        var a = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < a.Length; i++)
        {
            if (a[i] == "--movies") movies = a[i + 1].Split(',');
            if (a[i] == "--out") outDir = a[i + 1];
            if (a[i] == "--label") label = a[i + 1];
            if (a[i] == "--scale") scale = float.Parse(a[i + 1], System.Globalization.CultureInfo.InvariantCulture);
        }
        var data = Settings.DataDir;
        if (string.IsNullOrEmpty(data) || outDir == null) { GD.PushError("need MS2_DATA and --out"); GetTree().Quit(2); return; }
        RenderingServer.SetDefaultClearColor(new Color(0.35f, 0.5f, 0.35f));
        assets = new UiAssets(new PackSet(data));
        DirAccess.MakeDirRecursiveAbsolute(outDir);
        Next();
    }

    void Next()
    {
        current?.QueueFree();
        idx++;
        if (idx >= movies.Length) { GetTree().Quit(); return; }
        try
        {
            var m = new UiMovie(assets, movies[idx]);
            var size = GetViewportRect().Size;
            float s = scale > 0 ? scale : Math.Min(size.X / (float)m.StageWidth, size.Y / (float)m.StageHeight);
            var root = m.BuildRoot(s);
            root.Scale = new Vector2(s, s);
            if (label != null) root.GotoLabel(label);
            AddChild(root); current = root;
            GD.Print($"{movies[idx]}: stage {m.StageWidth}x{m.StageHeight} scale {s:0.00}");
        }
        catch (Exception e) { GD.PushWarning($"{movies[idx]}: {e.Message}"); current = null; }
        wait = 4;
    }

    public override void _Process(double delta)
    {
        if (idx < 0 || idx >= movies.Length) return;
        if (--wait > 0) return;
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(outDir, movies[idx] + ".png"));
        Next();
    }
}
