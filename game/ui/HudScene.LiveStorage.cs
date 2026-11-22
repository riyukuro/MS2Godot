using System.Linq;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>The scripted live run's bank hooks (<see cref="Ms2Godot.Net.LiveSession.CombatRun"/>): each does what a click or an answered
/// box does, through the same handler. The play path that opens the bank does not exist on the scratch server, so the open is the
/// client's own Load request followed by the dialog's open (the answer, Reset, would raise the same open).</summary>
public partial class HudScene
{
    /// <summary>The bank's Load request and the dialog open. True when the dialog is up.</summary>
    public bool StoreOpenScripted()
    {
        WatchStorage();
        storageSender?.RequestLoad();
        OpenStore();
        return StoreOpen;
    }

    /// <summary>The right click on the bag item <paramref name="uid"/>: the deposit with the client's checks and, for a stack, its count box.
    /// With <paramref name="count"/> above 0 the box is answered with that count (its yes button).</summary>
    public bool StoreDepositScripted(long uid, int count)
    {
        if (!StoreOpen) return false;
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.FindItem(uid) is not { } item) return false;
        if (count > 0) store.UseStoreIn(uid, count); else store.DepositFromInventory(item);
        return true;
    }

    /// <summary>The right click on the bank slot <paramref name="slot"/>; with <paramref name="count"/> above 0 the count box is answered with it.</summary>
    public bool StoreWithdrawScripted(int slot, int count)
    {
        if (!StoreOpen || store.Book.Slot(slot) is not { } it) return false;
        if (count > 0) store.UseStoreOut(it.Uid, count); else store.WithdrawSlot(slot);
        return true;
    }

    /// <summary>A bank slot dragged onto another (the bank's own move, mode 2).</summary>
    public bool StoreMoveScripted(int from, int to)
    {
        if (!StoreOpen) return false;
        store.SlotDroppedOn(StoreDialogController.SlotName(to), StoreDialogController.SlotName(from), store);
        return true;
    }

    /// <summary>The meso box's answer, the yes button with the typed amount.</summary>
    public bool StoreMesoScripted(bool deposit, long amount) => StoreOpen && store.OnMesoAnswer(0, (deposit ? "deposit," : "withdraw,") + amount);

    public long WalletMeso => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0;

    /// <summary>The dialog's sort button (onClickSort) and its Close.</summary>
    public bool StoreSortScripted() => StoreOpen && store.OnCallback("onClickSort", System.Array.Empty<object>());
    public bool StoreCloseScripted() { if (!StoreOpen) return false; store.Close(); return true; }
}
