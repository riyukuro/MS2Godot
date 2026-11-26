using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The result side of UIEnchantScrollDialog (0x4ee0) that does not need the window. The client's dialog object lives
/// whether its window is open or not: CSceneStateGame::OnEnchantScroll (@0x141734950, scenestategame.cpp:8081) finds it
/// (FindEnchantScrollDialog) and hands mode 3 to FUN_140f3c6c0 (uienchantscrolldialog.cpp:268) either way. So a result
/// that arrives after the window was closed (the enchant pressed, then Escape before the answer) still refreshes the bag
/// slot and shows the "enchant_ok" box, and an error code still shows its notice. The HUD keeps one of these over the
/// session's <see cref="EnchantScrollBook"/> for as long as it watches the book; the window
/// (<see cref="EnchantScrollDialogController"/>) handles only its own movie.
/// </summary>
public sealed class EnchantScrollResults
{
    public EnchantScrollBook Book { get; }
    public UiModel Model;
    public Action<MessageBoxData> ShowData = d => MessageBoxQueue.Show(d);
    /// <summary>The inventory item of a uid as the UI item (the box's item entry); null when unknown.</summary>
    public Func<long, Item> FindItem = _ => null;
    /// <summary>FUN_141275260(id, 1, 1): the notice text.</summary>
    public event Action<string> NoticeText;
    /// <summary>FUN_140f3c6c0's inventory refresh (FUN_141999e00 on 0x4e2d) of the enchanted item, before the box.</summary>
    public event Action<long> InventorySlotRefresh;

    public EnchantScrollResults(EnchantScrollBook book)
    {
        Book = book;
        Book.Notice += OnNotice;
        Book.Succeeded += OnSucceeded;
    }

    /// <summary>The book's handlers removed (the HUD's teardown): the session's book outlives the HUD.</summary>
    public void Detach()
    {
        Book.Notice -= OnNotice;
        Book.Succeeded -= OnSucceeded;
    }

    /// <summary>FUN_140f3c6c0 codes 1..8 (:287-300): FUN_141275260 with the code's notice id.</summary>
    void OnNotice(int id)
    {
        string text = Model?.Tables.Common(id);
        if (!string.IsNullOrEmpty(text)) NoticeText?.Invoke(text);
    }

    /// <summary>FUN_140f3c6c0 code 0 (:276-287), after the book stored the item: the inventory refresh, then the box, whose
    /// OK runs vfunc_67 (OnMessage, :184). With the window already closed that close only resets the book, as the client's
    /// vfunc_67 on a closed dialog does (FUN_140f3d210 and the slot unregister; UIDialog's close has nothing to close).</summary>
    void OnSucceeded(long uid)
    {
        InventorySlotRefresh?.Invoke(uid);
        if (Model == null || FindItem(uid) is not { } item) return;
        ShowData(EnchantScrollDialogController.ResultBox(Model, item, Book.Close));
    }
}
