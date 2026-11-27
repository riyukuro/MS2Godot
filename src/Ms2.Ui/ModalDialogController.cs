namespace Ms2.Ui;

/// <summary>
/// UIModalDialog (uimodaldialog.cpp:25-100), the base of the dialogs that hold the player while they are up. Showing the
/// entity (vfunc_8 @0x141a55380, :34) takes the move and attack channels of s_keyInput with the dialog's type id as its layer
/// (vfunc_47 @0x141a556d0 = FUN_141901470, <see cref="KeyInput"/>) and sets the modal dialog id (SetModalDialogId); hiding or
/// closing it (vfunc_8(0), vfunc_67 @0x141a55580, vfunc_68 @0x141a555e0) gives them back (ClearModalDialogId). While it is up the own
/// pc holds state flag 0x20 (no move, jump, cast or pickup key: <see cref="InputBlock.OwnKeysBlocked"/>) and the action keys are
/// off (<see cref="InputBlock.ActionKeys"/>).
/// </summary>
public abstract class ModalDialogController : UiController
{
    /// <summary>DAT_143af4400 (SetModalDialogId, @0x141a55650): the type id of the modal dialog shown, 0 for none.</summary>
    public static int ModalDialogId { get; private set; }

    /// <summary>IsModalDialogShown (@0x141a55620).</summary>
    public static bool IsModalDialogShown => ModalDialogId != 0;

    /// <summary>vfunc_48: the dialog's UI type id, its layer in s_keyInput.</summary>
    public abstract int TypeId { get; }

    /// <summary>This instance took the layer and has not given it back. A dialog that closes with its tween and is reopened under the
    /// same type id fires the old instance's Unloaded after the new one took the layer; without this the old one's Release
    /// would erase the new one's layer (the client has one dialog object per id, so it has no such race).</summary>
    bool tookLayer;

    protected ModalDialogController() { Unloaded += Release; }

    /// <summary>vfunc_8's visibility: vfunc_47(nVisible == 0), then the modal dialog id.</summary>
    public override void SetEntityVisible(bool visible)
    {
        if (!visible && !tookLayer) return;
        tookLayer = visible;
        KeyInput.MoveAndAttack(!visible, TypeId);
        if (visible) ModalDialogId = TypeId;
        else ClearModalDialogId();
    }

    /// <summary>vfunc_67 (@0x141a55580): vfunc_47(1), ClearModalDialogId, then UIDialog's close.</summary>
    public override void Close()
    {
        Release();
        base.Close();
    }

    /// <summary>vfunc_68 (@0x141a555e0) is vfunc_8(0): the layers are given back (a layer that is not held changes nothing).</summary>
    internal void Release()
    {
        if (!tookLayer) return;
        tookLayer = false;
        KeyInput.MoveAndAttack(true, TypeId);
        ClearModalDialogId();
    }

    /// <summary>ClearModalDialogId (@0x141a55670): only this dialog's own id.</summary>
    void ClearModalDialogId() { if (ModalDialogId == TypeId) ModalDialogId = 0; }
}
