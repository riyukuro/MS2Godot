using Ms2.Net;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The object store window, UIObjectStoreDialog (client/ms2/uiobjectstoredialog.cpp, UI id 0x4e78, type name
/// "ObjectStoreDialog", movie uiobjectstoredialog): the bag of a bank cube (a function cube of sub-kind 4), opened by
/// CFunctionCubeObject::OpenObjectStore (@0x141228100, functioncubeobject.cpp:1043-1055) when the server's use reply asks
/// for it. Its slots are the bank's StoreSlots over the same CStore (<see cref="StorageBook"/>), in the bag
/// "ObjectStoreDialog" (m_180, the type name): the drops, moves and withdraws are the bank window's.
/// <para>Its own parts: OnLoadComplete (@0x141a72a60) calls initData(the bag id, the store's meso, the item's name, its
/// functionDescription, 1, the bank's slot count / 6), which hides the meso row; "OnSort" (vfunc_28 @0x141a72dd0) sends
/// CStore::RequestClose; the close (vfunc_67 @0x141a72860) sends FunctionCube mode 7 with the store key instead of the
/// bank's sort, then clears the key and item type.</para>
/// <para>STILL-OPEN: the status listener's bag refresh (vfunc_oa0_7 / oa0_9: FUN_1422cca90) is the bank's per-slot push;
/// FUN_141901390 (the window count on open and close).</para>
/// </summary>
public sealed class ObjectStoreDialogController : StoreDialogController
{
    public override string Movie => "uiobjectstoredialog";
    /// <summary>GetTypeName (@0x141a725e0).</summary>
    public override string TypeName => "ObjectStoreDialog";

    public new const int UiId = 0x4e78;
    public new const string Bag = "ObjectStoreDialog";

    protected override int DialogId => UiId;
    protected override string BagId => Bag;

    /// <summary>m_190: the store key (the cube's id).</summary>
    public string Key { get; private set; } = "";
    /// <summary>m_198: the cube's item.</summary>
    public int ItemType { get; private set; }

    /// <summary>The FunctionCube close request goes out through it.</summary>
    public Action<FunctionCubeStoreCloseRequest> SendClose;

    /// <summary>vfunc_84 (@0x141a72630): the store key and the item type.</summary>
    public void SetStore(string key, int itemType)
    {
        Key = key ?? "";
        ItemType = itemType;
    }

    /// <summary>
    /// OpenObjectStore's store reset: CStore::ResetId(the cube's owner, its item), so the store is cleared, not ready and
    /// sized to the item's bank (GetBankSlotCount) when the owner is set; then vfunc_84 with the cube's id and item.
    /// </summary>
    public static ObjectStoreDialogController For(FunctionCubes.Cube cube, StorageBook book, GameTables tables)
    {
        book.ResetId(cube.Owner, tables.BankSlotCount(cube.ItemId));
        var c = new ObjectStoreDialogController { Book = book };
        c.SetStore(cube.Id, cube.ItemId);
        return c;
    }

    /// <summary>The bank's slot count of the item (GetBankSlotCount) and the rows initData shows (/ 6).</summary>
    public int SlotCount => Model?.Tables.BankSlotCount(ItemType) ?? 0;

    protected override void InitData()
    {
        var t = Model.Tables;
        Host.Invoke("initData", new object[]
        {
            BagId, Group(Book.Meso), t.ItemName(ItemType) ?? "", t.ItemDescription(ItemType, "functionDescription") ?? "", 1, SlotCount / 6,
        });
    }

    /// <summary>vfunc_67: FunctionCube mode 7 with the key; the key and item type cleared.</summary>
    protected override void OnClosing()
    {
        SendClose?.Invoke(new FunctionCubeStoreCloseRequest(Key));
        Key = "";
        ItemType = 0;
    }

    /// <summary>The object store has no vfunc_15: nothing is redrawn per frame.</summary>
    public override void Process() { }

    /// <summary>vfunc_28: "OnSort" sends CStore::RequestClose (mode 8); the slots' callbacks are the bank's.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "OnSort", StringComparison.OrdinalIgnoreCase))
        {
            Sender?.RequestClose();
            return true;
        }
        return base.OnCallback(name, args);
    }
}
