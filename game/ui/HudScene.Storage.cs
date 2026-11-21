using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The bank: CStore (the session's <see cref="StorageBook"/>, the StorageInventory packets 0x22) and its dialog
/// UIStoreDialog (0x4e47, movie uistoredialog) with its C++ side as <see cref="StoreDialogController"/>. Live only: the
/// server opens it (CStore::OnBegin / OnReset). STILL-OPEN: the sends (item 2) and the service error of code 0xf.
/// </summary>
public partial class HudScene
{
    StoreDialogController store;        // from open until the close
    MovieDialog storeMovie;             // the movie, from open until the unload
    bool storageWatched;

    bool StoreOpen => store != null && !store.IsClosed;

    /// <summary>Subscribes once per scene: the open request and the error texts of CStore::OnError.</summary>
    void WatchStorage()
    {
        if (storageWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        storageWatched = true;
        var book = live.Storage;
        book.MaxExtended = hudModel.Tables.StoreExpandMaxSlotCount;
        book.OpenRequested += OpenStore;
        book.ErrorReceived += code =>
        {
            var t = StorageBook.ErrorText(code, book.CurrentCapacity ?? 0);
            string text = hudModel.Tables.Common(t.StringId) ?? "";
            switch (t.Kind)
            {
                case StorageErrorKind.Notice:
                    if (t.Number is int n0 && t.StringId == 0x2af) text = ClientFormat.Format(text, n0);
                    SystemText?.ShowMessage(text, 0, 0);
                    break;
                case StorageErrorKind.Box:
                    MessageBoxQueue.ShowMessageBox(text, 0, null, "");
                    break;
                case StorageErrorKind.BoxWithNumber:
                    MessageBoxQueue.ShowMessageBox(ClientFormat.Format(text, t.Number ?? 0), 0, null, "");
                    break;
                default:
                    GD.Print("storage: the service error is not ported");
                    break;
            }
        };
    }

    /// <summary>UIStoreDialog's key handling, as UIDialog's: Escape closes it.</summary>
    bool StoreKeys(InputEventKey k)
    {
        if (!StoreOpen || k.Keycode != Key.Escape) return false;
        store.Close();
        return true;
    }

    /// <summary>UIStoreDialog::vfunc_15 once per frame.</summary>
    void StoreTick()
    {
        if (StoreOpen) store.Process();
    }

    /// <summary>OpenDialog(0x4e47); vfunc_66 places it beside the open inventory, or opens the inventory beside it.</summary>
    void OpenStore()
    {
        if (StoreOpen || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        if (storeMovie != null) { storeMovie.Host.Unload(); storeMovie.QueueFree(); storeMovie = null; }
        var c = new StoreDialogController
        {
            Book = live.Storage,
            OpenInventory = () => inventory != null && !inventory.IsClosed ? inventory : null,
        };
        c.InventoryWindowPos += _ =>
        {
            if (inventory == null || inventory.IsClosed) OpenInventory();
            store?.PlaceBesideInventory();
        };
        c.Button += name => GD.Print($"storage: {name} is not ported");
        c.Unloaded += () =>
        {
            storeMovie?.Host.Unload(); storeMovie?.QueueFree(); storeMovie = null;
        };
        store = c;
        storeMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"storage: load={storeMovie.Loaded}");
        uiRoot.AddChild(storeMovie);
        c.PlaceBesideInventory();
    }
}
