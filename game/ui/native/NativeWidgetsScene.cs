using Godot;

namespace Ms2Godot.Ui.Native;

/// <summary>
/// A test bench for the native widget set: a window (OpenCloseTween, drag bar, close button) with a scrolling list and
/// its scroll bar, a check box, buttons and a tooltip, all skinned from the player's component_v1 / commonResource.
/// Args after `--`: --native-shots DIR saves widgets_open.png (mid open tween), widgets.png (list scrolled, row
/// selected, tooltip shown) and widgets_close.png (mid close tween), then quits.
/// </summary>
public partial class NativeWidgetsScene : Control
{
    string shotDir; int frames;
    NWindow window; NScrollingList list; NButton ok;

    public override void _EnterTree()
    {
        if (!NativeUi.Init()) { GetTree().Quit(2); return; }
        NativeUi.UseStageScale(GetWindow());
        RenderingServer.SetDefaultClearColor(new Color(0.25f, 0.3f, 0.35f));
    }

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--native-shots") shotDir = args[i + 1];
        window = GetNode<NWindow>("Window");
        list = window.GetNode<NScrollingList>("List");
        ok = window.GetNode<NButton>("Ok");
        list.SetItems(Enumerable.Range(1, 30).Select(i => $"Item {i}"));
        list.ItemClick += i => GD.Print($"item click {i}");
        ok.Click += () => window.StartClose();
        window.EndClose += () => GD.Print("end close");
        NTooltip.Attach(ok, () => "Closes the window (OpenCloseTween: 100 ms fade and drop)");
    }

    public override void _Process(double delta)
    {
        if (shotDir == null) return;
        frames++;
        if (frames == 6) Save("widgets_open.png");
        if (frames == 30) { list.ScrollTo(12); list.Select(14); ok.SetState("over"); NTooltip.ShowNow(ok, "Closes the window (OpenCloseTween: 100 ms fade and drop)"); }
        if (frames == 60) Save("widgets.png");
        if (frames == 61) window.StartClose();
        if (frames == 64) Save("widgets_close.png");
        if (frames == 90) GetTree().Quit();
    }

    void Save(string name)
    {
        DirAccess.MakeDirRecursiveAbsolute(shotDir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shotDir, name));
        GD.Print("saved " + name);
    }
}
