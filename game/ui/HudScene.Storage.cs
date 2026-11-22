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
    StorageSender storageSender;        // CStore's requests with the client's checks, once per session

    bool StoreOpen => store != null && !store.IsClosed;

    /// <summary>Subscribes once per scene: the open request and the error texts of CStore::OnError.</summary>
    void WatchStorage()
    {
        if (storageWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        storageWatched = true;
        var book = live.Storage;
        book.MaxExtended = hudModel.Tables.StoreExpandMaxSlotCount;
        book.MaxStackOf = live.Items.MaxStackOf;
        book.OpenRequested += OpenStore;
        book.ErrorReceived += code => ShowStorageText(StorageBook.ErrorText(code, book.CurrentCapacity ?? 0));
        storageSender = new StorageSender(book)
        {
            Send = live.SendStorage,
            Notify = ShowStorageText,
            InventoryMeso = () => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0,
            FindInventoryItem = uid => live.Inventory?.FindByUid(uid),
            PlayerAlive = () => live.Me is { Dead: false },
            CanMove = it => StorageSender.BindingAllows(it, live.CharacterId),
            IsScoreItem = id => hudModel.Tables.Items.Get(id)?.IsScoreItem ?? false,
            PlayingScoreUid = () => live.PlayingScoreUid,
            SummonUid = () => live.Me?.Pet?.Uid ?? 0,
            IsPetItem = id => hudModel.Tables.Items.Get(id)?.IsPetItem ?? false,
            BindNotice = score => SystemNoticeText(hudModel.Tables.Common(score ? "s_item_err_binditem_by_playing_score" : "s_item_err_binditem_by_summon_pet") ?? ""),
            DepositMax = hudModel.Tables.StoreDepositMax,
            WithdrawMax = hudModel.Tables.StoreWithdrawMax,
        };
    }

    /// <summary>CStore::OnError's texts (and the checks of the sends that share them).</summary>
    void ShowStorageText(StorageErrorText t)
    {
        string text = hudModel.Tables.Common(t.StringId) ?? "";
        switch (t.Kind)
        {
            case StorageErrorKind.Notice:
                if (t.Number is long n0)
                    text = t.StringId == 0xee ? ClientFormat.Format(text, 1, n0) : ClientFormat.Format(text, n0);   // 0xee: (kind 1, the number)
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
            Sender = storageSender,
            Now = () => live.GameSession?.ServerTime ?? 0,
            ShiftHeld = () => Input.IsKeyPressed(Key.Shift),
            ExpandPrice = () => hudModel.Tables.StoreExpandPrice1Row,
            OpenInventory = () => inventory != null && !inventory.IsClosed ? inventory : null,
            IsTalking = () => live.IsTalking(),
            EndTalk = live.EndTalk,
            SelfId = () => live.CharacterId,
            InventoryBag = () => live.Inventory,
            TabName = tab => inventory?.Tabs.FirstOrDefault(t => t.Tab == tab).Name is { } n ? hudModel.Tables.Common("s_inventory_tab_" + n) ?? "" : "",
        };
        c.InventoryWindowPos += _ =>
        {
            if (inventory == null || inventory.IsClosed) OpenInventory();
            store?.PlaceBesideInventory();
        };
        c.NoticeText += SystemNoticeText;
        c.Button += name => GD.Print($"storage: {name} is not ported");
        storageSender.ServiceRequested += () => GD.Print("storage: the service purchase dialog is not ported");
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
