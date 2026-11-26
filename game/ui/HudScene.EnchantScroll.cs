using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The enchant scroll window UIEnchantScrollDialog (0x4ee0) with its C++ side as <see cref="EnchantScrollDialogController"/>,
/// over the session's <see cref="Ms2.Ui.Items.EnchantScrollBook"/>. Live only: the server opens it (EnchantScroll mode 0,
/// FUN_140f3b920's OpenDialog) after the scroll's item use (the EnchantScroll item function @0x140d225d0).
/// </summary>
public partial class HudScene
{
    EnchantScrollDialogController enchantScroll;    // from open until the close
    MovieDialog enchantScrollMovie;                  // the movie, from open until the unload
    bool enchantScrollWatched;
    Action unwatchEnchantScroll;                     // removed as the HUD leaves the tree

    bool EnchantScrollOpen => enchantScroll != null && !enchantScroll.IsClosed;

    void WatchEnchantScroll()
    {
        if (enchantScrollWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        enchantScrollWatched = true;
        var book = live.ScrollBook;
        book.OpenRequested += OpenEnchantScroll;
        unwatchEnchantScroll = () =>      // the session and its book outlive this HUD
        {
            book.OpenRequested -= OpenEnchantScroll;
            enchantScrollWatched = false;
        };
    }

    /// <summary>CUIService::OpenDialog(0x4ee0): the movie loads, and its OnLoadComplete runs UpdateScrollInfo.</summary>
    void OpenEnchantScroll()
    {
        if (EnchantScrollOpen) return;
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        if (enchantScrollMovie != null) { enchantScrollMovie.Host.Unload(); enchantScrollMovie.QueueFree(); enchantScrollMovie = null; }
        var book = live.ScrollBook;
        book.Send = live.SendEnchant;
        book.Find = live.Items.Get;
        book.StoreItem = i => live.Items.Apply(new ItemMessage.Update(i.Uid, i));
        var c = new EnchantScrollDialogController
        {
            Book = book,
            FindItem = uid => live.Items.Get(uid) is { } e ? InventoryController.ToItem(e, hudModel.Tables.Items) : null,
            ShiftHeld = () => Input.IsKeyPressed(Key.Shift),
            ReplaceValueWords = t => hudModel.Tables.ReservedWords.ReplaceValueWords(t, hudModel.HasLocalPlayer ? hudModel.Player.Name : null),
        };
        c.NoticeText += SystemNoticeText;
        c.InventorySlotRefresh += uid => inventory?.RefreshItemSlot(uid);
        c.Unloaded += () =>
        {
            enchantScrollMovie?.Host.Unload(); enchantScrollMovie?.QueueFree(); enchantScrollMovie = null;
        };
        enchantScroll = c;
        enchantScrollMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"enchant scroll: load={enchantScrollMovie.Loaded}");
        uiRoot.AddChild(enchantScrollMovie);
    }

    /// <summary>UIDialog's key handling: Escape closes the window (vfunc_67).</summary>
    bool EnchantScrollKeys(InputEventKey k)
    {
        if (!EnchantScrollOpen || k.Keycode != Key.Escape) return false;
        enchantScroll.Close();
        return true;
    }
}
