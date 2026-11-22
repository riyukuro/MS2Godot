using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIUGCCubeBillDialog (0x4f29, movie uiugccubebilldialog, uiugccubebilldialog.cpp) over the session's <see cref="Net.LiveSession.Bill"/>: its
/// OpenDialog, CloseDialog / RequestCloseDialog(0x4f29) and CloseDialog(0x4f27) hooks. CloseDialog and RequestCloseDialog both close here (their
/// difference is not traced).
/// </summary>
public partial class HudScene
{
    CubeBillDialogController cubeBillDialog;
    MovieDialog cubeBillMovie;

    bool CubeBillOpen => cubeBillMovie != null && cubeBillDialog?.IsClosed == false;

    void BindCubeBill(Net.LiveSession live)
    {
        var bill = live.Bill;
        bill.CloseBill = () => { if (CubeBillOpen) cubeBillDialog.Close(); };
        bill.CloseSaveSlots = () => { if (HomeSaveSlotOpen) homeSaveSlot.Close(); };
        bill.OpenBill = () => ShowCubeBill(bill);
        bill.HasItem = uid => inventory?.FindItem(uid) != null;                                            // FUN_141a02c20 (FindItemByUid)
        bill.UseItem = (uid, arg) => inventory?.FindItem(uid) is { } item && inventory.RequestUseItem(item, arg) ? 1 : 0;   // FUN_140a63880
    }

    void UnbindCubeBill(Net.LiveSession live)
    {
        var bill = live.Bill;
        bill.CloseBill = () => { };
        bill.CloseSaveSlots = () => { };
        bill.OpenBill = () => { };
        bill.HasItem = _ => false;
        bill.UseItem = (_, _) => 0;
        if (CubeBillOpen) cubeBillDialog.Close();
    }

    void ShowCubeBill(CubeBill bill)
    {
        if (cubeBillMovie != null)
        {
            cubeBillMovie.Host.Unload();
            cubeBillMovie.QueueFree();
            cubeBillMovie = null;
        }
        var c = cubeBillDialog = new CubeBillDialogController(bill);
        var m = cubeBillMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        c.Unloaded += () =>
        {
            m.Host.Unload();
            m.QueueFree();
            if (cubeBillMovie == m) cubeBillMovie = null;
        };
        uiRoot.AddChild(m);
    }
}
