using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The host side of the beauty shop dialog (UIBeautyShopDialog 0x4eee): when it opens, where the shop packets go and how it ends.
/// The dialog opens from the beauty item's use (<see cref="BeautyReq"/>, SendBeauty scenestategame.cpp:8692) and only while the
/// own-avatar <see cref="IBeautyCustomize"/> exists: without it the page can neither show a change nor pay, so the dialog stays
/// shut (STILL-OPEN: the Customize rebuild, step 3). Inbound 0x31 packets reach the dialog only while it is open (the client
/// registers its handler at open and removes it at close, uibeautyshopdialog.cpp:513, :519).
/// <para>Leaving (FUN_1418c9cd0 @0x1418c9cd0: the worn slot is restored by the controller, then the field camera goes back, vfunc_149,
/// and SetSubScene(<see cref="BeautyShopDialogController.ExitSubScene"/>)) closes the dialog. STILL-OPEN: the field camera call and
/// the sub scene setter are not ported; <see cref="ExitSubScene"/> is the host's hook for them.</para>
/// </summary>
public sealed class BeautyShopFlow
{
    /// <summary>The own-avatar customize of the open dialog; null until step 3 lands it, and the dialog does not open then.</summary>
    public Func<IBeautyCustomize> CreateCustomize = () => null;
    /// <summary>Builds the controller with the host-set parts (payment, bill, sales, send).</summary>
    public Func<BeautyShopDialogController> CreateController;
    /// <summary>The host shows the controller's movie.</summary>
    public Action<BeautyShopDialogController> Show;
    /// <summary>FUN_1418c9cd0's camera and sub scene part, run by the host on leaving (not ported).</summary>
    public Action ExitSubScene;
    public Action<string> Log = _ => { };

    public BeautyShopDialogController Current { get; private set; }
    public bool IsOpen => Current != null;

    /// <summary>Opens the dialog. False when it is already open, or when there is no Customize to edit.</summary>
    public bool Open()
    {
        if (IsOpen || CreateController == null) return false;
        var customize = CreateCustomize?.Invoke();
        if (customize == null) { Log("beauty shop: no own-avatar customize, the dialog stays shut"); return false; }
        var c = CreateController();
        c.Customize = customize;
        c.Left += () => { ExitSubScene?.Invoke(); c.Close(); };
        c.Closed += () => { if (Current == c) Current = null; };
        Current = c;
        Show?.Invoke(c);
        return true;
    }

    public void OnPacket(BeautyShopPacket p)
    {
        if (IsOpen) Current.OnPacket(p);
        else Log($"beauty shop: mode {p.Mode} arrived with the dialog closed");
    }
}
