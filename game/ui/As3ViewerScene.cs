using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using FlashPlayer = Ms2.Avm2.Flash.Player;
using AsName = Ms2.Avm2.Name;
using Ms2.Formats.Pack;

namespace Ms2Godot.Ui;

/// <summary>
/// Development scene for the ActionScript route: runs UI movies' own AS3 (Ms2.Avm2) and draws them with <see cref="As3View"/>.
/// Args after `--`: --movie NAME [--scenario login|minimap|expbar] [--out FILE.png] [--frames N]
/// Without --out the movie stays up and takes mouse and keyboard input.
/// </summary>
public partial class As3ViewerScene : Control
{
    string movie = "uilogindialog", scenario, outFile; int frames = 30;
    As3View view; int wait = -1;

    public override void _Ready()
    {
        var a = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < a.Length; i++)
        {
            if (a[i] == "--movie") movie = a[i + 1];
            if (a[i] == "--scenario") scenario = a[i + 1];
            if (a[i] == "--out") outFile = a[i + 1];
            if (a[i] == "--frames") frames = int.Parse(a[i + 1]);
        }
        var data = Settings.DataDir;
        if (string.IsNullOrEmpty(data)) { GD.PushError("need MS2_DATA"); GetTree().Quit(2); return; }
        RenderingServer.SetDefaultClearColor(new Color(0.35f, 0.5f, 0.35f));
        var packs = new PackSet(data);
        var assets = new UiAssets(packs);
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var (rt, p) = src.Open(movie, r => r.Budget = 20_000_000);
        rt.Trace = s => GD.Print("[as3] " + s);
        p.External = (n, args) => { GD.Print($"[ExternalInterface] {n}({string.Join(", ", args.Select(Rt.Describe))})"); return null; };
        view = new As3View(p, assets) { AutoPlay = outFile == null };
        // the game runs its movies with the stage at the window size (noScale); the movies scale themselves (Common.getUIScale)
        var size = GetViewportRect().Size;
        p.StageW = size.X; p.StageH = size.Y;
        view.StageScale = 1;
        AddChild(view);
        if (!p.Start()) GD.PushWarning("construct failed");
        for (int i = 0; i < 5; i++) p.Tick();
        // the client's dialog wrapper invokes "alignment" on the movie's entity (ASGameEntity::vfunc_25)
        if (p.FindOwner("alignment") is { } al) p.Call(al, "alignment");
        // a dialog is shown by the client's UIDialog open path (m_30->Invoke("startOpen"))
        if (p.FindOwner("startOpen") is { } op) { p.RT.SetProperty(op, AsName.Public("visible"), Rt.True); p.Call(op, "startOpen"); }
        RunScenario(p);
        foreach (var e in rt.Errors) GD.PushWarning("[as3 error] " + e);
        if (outFile != null) { for (int i = 0; i < frames; i++) p.Tick(); view.Sync(); wait = 4; }
    }

    void RunScenario(FlashPlayer p)
    {
        switch (scenario)
        {
            case "login":
            {
                var dlg = p.FindOwner("onEnterKey");
                if (dlg == null) return;
                var id = (AsDisplayObject)p.RT.GetProperty(p.RT.GetProperty(dlg, AsName.Public("m_inputID")), AsName.Public("textField"));
                var b = id.GetBounds(null);
                p.Click(b.X + b.W / 2, b.Y + b.H / 2); p.Tick();
                if (p.Focus is AsTextField ft) ft.setSelection(0, ft.Text.Length);
                foreach (var ch in "tester") p.Key(char.ToUpperInvariant(ch), ch, true);
                p.Tick();
                p.Call(dlg, "onTabKey"); p.Tick();
                foreach (var ch in "secret") p.Key(char.ToUpperInvariant(ch), ch, true);
                break;
            }
            case "minimap":
            {
                var dlg = p.FindOwner("createEntity");
                if (dlg == null) return;
                int x = 60;
                foreach (var kind in new[] { "portal", "friendly", "enemy", "taming_pet" })
                {
                    p.Call(dlg, "createEntity", p.NewObject(("mcname", kind), ("frame", kind), ("layer", kind), ("x", x), ("y", 90), ("visible", true),
                        ("showtext", false), ("showguide", false), ("name", kind)));
                    x += 40;
                }
                break;
            }
            case "expbar":
            {
                var dlg = p.FindOwner("setExp");
                if (dlg != null) p.Call(dlg, "setExp", 42.5, 10.0, "42.50%", 3);
                break;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (wait < 0 || --wait > 0) return;
        GetViewport().GetTexture().GetImage().SavePng(outFile);
        GD.Print("saved " + outFile);
        GetTree().Quit();
    }
}
