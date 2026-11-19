using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot.Ui;

/// <summary>
/// A capture of the loading screen (res://ui/loadingshot.tscn), run from its movie (<see cref="LoadingMovie"/>):
/// <list type="bullet">
/// <item>--shots DIR: where the shots go (loading_&lt;ms&gt;.png);</item>
/// <item>--map ID: the loading picture of that map (LoadingImageTable.Pick with a fixed seed; the candidates are printed);
/// --picture FILE: that picture (a file of LoadingImageTable.ImageArchive) instead; neither: no picture, so the screenshot;</item>
/// <item>--screenshot PNG: the scene screenshot shown without a picture;</item>
/// <item>--tip TEXT, --guide (showGuide 1), --noframe (showFrame false), --progress COUNT,TOTAL.</item>
/// </list>
/// </summary>
public partial class LoadingShot : Node
{
    string shotDir, picture, screenShot, tip = "";
    bool guide, frame = true;
    int count = -1, total;
    LoadingMovie movie;
    double t;
    int next;
    static readonly int[] ShotsMs = { 100, 500, 1000, 2000 };

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        int map = 0;
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--shots": shotDir = args[++i]; break;
                case "--map": map = int.Parse(args[++i]); break;
                case "--picture": picture = args[++i]; break;
                case "--screenshot": screenShot = args[++i]; break;
                case "--tip": tip = args[++i]; break;
                case "--guide": guide = true; break;
                case "--noframe": frame = false; break;
                case "--progress": var p = args[++i].Split(','); count = int.Parse(p[0]); total = int.Parse(p[1]); break;
            }
        var packs = PackSet.Shared(Settings.DataDir);
        var assets = new UiAssets(packs);
        if (map != 0 && picture == null)
        {
            var table = new LoadingImageTable(packs);
            GD.Print($"loading pictures of {map}: {string.Join(", ", table.Candidates(map, System.DateTime.Now))}");
            picture = table.Pick(map, new System.Random(0));
        }
        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        movie = new LoadingMovie(assets, ScreenMode.PixelSize(GetWindow()));
        layer.AddChild(movie);
        GD.Print($"loading movie: load={movie.Dialog.Loaded}, compiled bodies {Ms2.Avm2.GeneratedCode.Count}");
        movie.Start(picture, screenShot != null ? LoadPng(screenShot) : null, tip, guide, frame);
        if (count >= 0) movie.Progress(count, total);
    }

    static Texture2D LoadPng(string path) => Image.LoadFromFile(path) is { } img ? ImageTexture.CreateFromImage(img) : null;

    public override void _Process(double delta)
    {
        t += delta * 1000;
        if (next >= ShotsMs.Length) { GetTree().Quit(); return; }
        if (t < ShotsMs[next]) return;
        if (shotDir != null)
        {
            System.IO.Directory.CreateDirectory(shotDir);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, $"loading_{ShotsMs[next]:D4}ms.png"));
        }
        next++;
    }
}
