using Godot;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The live field's booty popups (UIBootyPopup, one movie per popup cube, layer 7) over the player's function cubes:
/// CubeMapInformationService::FUN_1410c8a90 (@0x1410c8a90, cubemapinformationservice_3.cpp:3491-3520) each frame with the
/// pc's position and GetTickCountMs2, then each popup's vfunc_15 (@0x1410317a0) places its movie over the cube.
/// </summary>
public sealed partial class BootyPopupLayer : CanvasLayer
{
    readonly UiAssets assets;
    readonly ClientConstants constants;
    readonly GameTables tables;
    readonly Dictionary<BootyPopup, (BootyPopupController Controller, MovieDialog Movie)> movies = new();
    BootyPopups popups;
    FunctionCubes cubes;
    Camera3D camera;
    Viewport sizeSource;
    Vector2 size = new(1280, 720);
    float u = 1;
    readonly Func<long> tickMs = HudScene.GateTickMs ?? (() => System.Environment.TickCount64);

    /// <summary>The local player (the pc FUN_1410c8a90 measures from).</summary>
    public Player Viewer;

    public BootyPopupLayer(UiAssets assets, ClientConstants constants)
    {
        this.assets = assets; this.constants = constants;
        tables = GameTables.Shared(assets.Packs, GameOptions.StringLanguage);
        Name = "BootyPopups";
        Layer = 7;
    }
    public BootyPopupLayer() { }

    void Bind(FunctionCubes c)
    {
        Unbind();
        cubes = c;
        popups = new BootyPopups(c)
        {
            Distance = constants.Float("FunctionCubeDistance", 125),
            Duration = (int)constants.Float("BootyPopupDuration", 3000),
            TextOf = id => BootyPopup.Text(tables, id),
            Opened = (_, p) => Open(p),
            Closed = (_, p) => Drop(p),
        };
        foreach (var kv in popups.All) Open(kv.Value);
    }

    void Unbind()
    {
        if (popups == null) return;
        popups.Dispose();
        foreach (var p in movies.Keys.ToList()) Drop(p);
        popups = null;
        cubes = null;
    }

    void Open(BootyPopup p)
    {
        if (movies.ContainsKey(p)) return;
        try
        {
            var c = new BootyPopupController(p);
            var m = new MovieDialog(assets, c, new UiModel(tables), new Vector2(1280, 720)) { Visible = false };
            AddChild(m);
            m.Relayout(size);
            movies[p] = (c, m);
        }
        catch (Exception e) { GD.PushWarning("booty popup movie: " + e.Message); }
    }

    void Drop(BootyPopup p)
    {
        if (!movies.Remove(p, out var x)) return;
        x.Movie.QueueFree();
    }

    public override void _EnterTree()
    {
        sizeSource = GetViewport();
        sizeSource.SizeChanged += Relayout;
        Relayout();
    }

    public override void _ExitTree()
    {
        if (sizeSource != null) { sizeSource.SizeChanged -= Relayout; sizeSource = null; }
        Unbind();
    }

    void Relayout()
    {
        size = GetViewport().GetVisibleRect().Size;
        u = UiScale.For(size);
        foreach (var x in movies.Values) x.Movie.Relayout(size);
    }

    public override void _Process(double delta)
    {
        var grid = Viewer != null && IsInstanceValid(Viewer) ? Viewer.Cubes?.Cells.Functions : null;
        if (grid != cubes) { if (grid == null) Unbind(); else Bind(grid); }
        if (popups == null) return;
        camera ??= GetViewport().GetCamera3D();
        bool hasPc = Viewer != null && IsInstanceValid(Viewer);
        System.Numerics.Vector3? pc = null;
        if (hasPc) { var g = Viewer.GlobalPosition; pc = new System.Numerics.Vector3(g.X, -g.Z, g.Y); }
        popups.Tick(pc, unchecked((uint)tickMs()));
        if (movies.Count == 0) return;
        foreach (var (p, x) in movies)
        {
            var (w, h) = x.Controller.Size();
            p.Frame(hasPc, Project, 1f / u, w, h);
            x.Movie.Visible = p.Visible;
            if (p.Visible) x.Controller.Place(p.Position.X, p.Position.Y);
        }
    }

    /// <summary>CameraMgr FUN_140af0700: the MS2 point on the screen, null behind the camera.</summary>
    System.Numerics.Vector2? Project(System.Numerics.Vector3 at)
    {
        if (camera == null) return null;
        var g = new Vector3(at.X, at.Z, -at.Y);
        if (camera.IsPositionBehind(g)) return null;
        var s = camera.UnprojectPosition(g);
        return new System.Numerics.Vector2(s.X, s.Y);
    }
}
