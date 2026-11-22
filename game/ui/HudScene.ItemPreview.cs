using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The item preview window: UIItemPreviewDialog (0x4ec2) as <see cref="ItemPreviewController"/>, opened by ItemPreviewMgr::OpenPreview
/// (the shop's preview). STILL-OPEN: the avatar in the render target and everything listed on the controller.
/// </summary>
public partial class HudScene
{
    ItemPreviewController itemPreview;   // from open until the close
    MovieDialog itemPreviewMovie;        // the movie, from open until the unload

    bool ItemPreviewOpen => itemPreview != null && !itemPreview.IsClosed;

    /// <summary>ItemPreviewMgr::OpenPreview (@0x140d40120): the checks, then the window.</summary>
    void OpenItemPreview(Item item, int arg)
    {
        if (ItemPreviewOpen) itemPreview.Close();
        if (itemPreviewMovie != null) { itemPreviewMovie.Host.Unload(); itemPreviewMovie.QueueFree(); itemPreviewMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var c = new ItemPreviewController { Notice = SystemNoticeText };
        if (live?.GameSession is { } gs) c.Now = () => gs.ServerTime;
        if (!c.Open(hudModel, item, arg)) return;
        c.Unloaded += () =>
        {
            itemPreviewMovie?.Host.Unload(); itemPreviewMovie?.QueueFree(); itemPreviewMovie = null;
        };
        itemPreview = c;
        itemPreviewMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item preview: load={itemPreviewMovie.Loaded}");
        uiRoot.AddChild(itemPreviewMovie);
    }

    /// <summary>Escape closes it: UIDialog::vfunc_13 (@0x141925900, uidialog.cpp:848) runs the close (vfunc_69) when Escape is
    /// pressed, the dialog is open and neither vfunc_64 nor flag 1 holds. STILL-OPEN: those two guards for this dialog.</summary>
    bool ItemPreviewKeys(InputEventKey k)
    {
        if (!ItemPreviewOpen || k.Keycode != Key.Escape) return false;
        itemPreview.Close();
        return true;
    }
}
