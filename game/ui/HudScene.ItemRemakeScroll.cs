using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The Change Attributes scroll window UIItemRemakeScrollDialog (0x4f09) as <see cref="ItemRemakeScrollDialogController"/>, fed
/// by the ChangeAttributesScroll packets. Live only: the server opens it (type 0, FUN_140f59230's OpenDialog) after the
/// scroll's item use.
/// </summary>
public partial class HudScene
{
    ItemRemakeScrollDialogController itemRemakeScroll;   // from open until the close
    MovieDialog itemRemakeScrollMovie;                   // the movie, from open until the unload
    Action unwatchItemRemakeScroll;

    bool ItemRemakeScrollOpen => itemRemakeScroll != null && !itemRemakeScroll.IsClosed;

    /// <summary>
    /// FUN_140f59230 (@0x140f59230, uiitemremakescrolldialog.cpp:356), the dialog's RecvOp 201 entry: type 0 opens the window
    /// while it is shut and the scroll is held (m_nScrollUid set first); types 2 and 3 go to the open window, which reads the item
    /// body itself; type 4 (OnRemakeError) always, with no pet row while the window is shut (m_nPet is cleared at the close).
    /// </summary>
    void WatchItemRemakeScroll()
    {
        if (Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        void On(ChangeAttributesScrollPacket p, Maple2.PacketLib.Tools.ByteReader r)
        {
            if (ItemRemakeScrollOpen)
            {
                if (p.Type == 0) return;
                itemRemakeScroll.OnPacket(p, held => ItemReader.ReadBodyCopy(r, held, live.GameProfile));
                return;
            }
            if (p.Type == 0)
            {
                if (ScrollItem(live, p.Uid) == null) return;
                OpenItemRemakeScroll(live, p.Uid);
            }
            else if (p.Type == 4 && ItemRemakeScrollDialogController.ErrorText(hudModel.Tables, 0, p.Flag, p.Code) is { } text)
                SystemNoticeText(text);
        }
        live.ChangeAttributesScrollReceived += On;
        unwatchItemRemakeScroll = () =>
        {
            live.ChangeAttributesScrollReceived -= On;
            if (ItemRemakeScrollOpen) itemRemakeScroll.Close();
            itemRemakeScroll = null;
            if (itemRemakeScrollMovie != null) { itemRemakeScrollMovie.Host.Unload(); itemRemakeScrollMovie.QueueFree(); itemRemakeScrollMovie = null; }
        };
    }

    /// <summary>g_143ab2a90.FindItemByUid: the inventory's item.</summary>
    Item ScrollItem(Ms2Godot.Net.LiveSession live, long uid) =>
        live.Items.Get(uid) is { } e ? InventoryController.ToItem(e, hudModel.Tables.Items) : null;

    /// <summary>CUIService::OpenDialog(0x4f09): the movie loads, and its OnLoadComplete reads the scroll's row.</summary>
    void OpenItemRemakeScroll(Ms2Godot.Net.LiveSession live, long scrollUid)
    {
        if (itemRemakeScrollMovie != null) { itemRemakeScrollMovie.Host.Unload(); itemRemakeScrollMovie.QueueFree(); itemRemakeScrollMovie = null; }
        var table = hudModel.Tables.Items;
        var c = new ItemRemakeScrollDialogController
        {
            ScrollUid = scrollUid,
            Send = live.SendChangeAttributesScroll,
            FindItem = uid => ScrollItem(live, uid),
            CountByTag = (tag, rarity) => (int)Ms2.Ui.Shop.ShopController.CountItemsByTag(live.Items.All, table, tag, itemUse.Error, rarity),
            // FUN_140f5d020: ReplaceItem, the inventory redraws
            Store = item => { if (item?.Record is { } record && live.Items.Get(item.Uid) != null) live.Items.Apply(new ItemMessage.Update(item.Uid, record)); },
            ShiftHeld = () => Input.IsKeyPressed(Key.Shift),
            ReplaceWords = SidePopupWords(),
        };
        c.NoticeText += SystemNoticeText;
        c.Unloaded += () =>
        {
            itemRemakeScrollMovie?.Host.Unload(); itemRemakeScrollMovie?.QueueFree(); itemRemakeScrollMovie = null;
        };
        itemRemakeScroll = c;
        BindBoxAnswers();
        itemRemakeScrollMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"item remake scroll: load={itemRemakeScrollMovie.Loaded}");
        uiRoot.AddChild(itemRemakeScrollMovie);
    }

    /// <summary>HandleRemakeOptionScrollBindPetButton's yes (uimessagebox.cpp:1809) on the last window controller.</summary>
    bool ItemRemakeScrollBindPet(int button, long itemUid, long arg) =>
        Ms2Godot.Net.LiveSession.Instance is { } live
        && ItemRemakeScrollDialogController.AnswerBindPet(itemRemakeScroll, hudModel.HasLocalPlayer,
            uid => ScrollItem(live, uid), button, itemUid, arg);

    // ---- the scripted live run's hooks (LiveSession.CombatRun's remake scroll steps) ---------------------------------

    /// <summary>The window is open with its movie loaded.</summary>
    public bool ItemRemakeScrollOpenScripted() => ItemRemakeScrollOpen && itemRemakeScrollMovie?.Loaded == true;

    /// <summary>The bag item <paramref name="uid"/> dropped on the window's item slot (OnTargetSlotDrop).</summary>
    public bool ItemRemakeScrollDropScripted(long uid)
    {
        if (!ItemRemakeScrollOpenScripted()) return false;
        itemRemakeScroll.DropTarget(uid);
        return true;
    }

    /// <summary>A movie command as the movie raises it (onClickConfirm, GetEnableLockOption, notifyslotuse).</summary>
    public bool ItemRemakeScrollCallScripted(params object[] call)
    {
        if (!ItemRemakeScrollOpenScripted()) return false;
        itemRemakeScrollMovie.Player.External?.Invoke(itemRemakeScrollMovie.Host.Id, call);
        return true;
    }

    /// <summary>The movie's onClickConfirm, with the option pick (selectedOptionIndex) for the new roll's step.</summary>
    public bool ItemRemakeScrollConfirmScripted(int pick)
    {
        if (!ItemRemakeScrollOpenScripted()) return false;
        var h = itemRemakeScrollMovie.Host;
        return ItemRemakeScrollCallScripted("onClickConfirm", h.Obj(("selectedOptionIndex", pick)));
    }

    /// <summary>The window's state for the run's log: the state, the target, the new roll, the option count, the input.</summary>
    public (bool Open, int State, long Target, long Option, int Options, bool Input) ItemRemakeScrollStateScripted() =>
        ItemRemakeScrollOpen ? (true, itemRemakeScroll.State, itemRemakeScroll.Target?.Uid ?? 0, itemRemakeScroll.Option?.Uid ?? 0,
            itemRemakeScroll.OptionCount, itemRemakeScroll.InputEnabled) : default;

    /// <summary>The window closed as its close button closes it.</summary>
    public void ItemRemakeScrollCloseScripted() { if (ItemRemakeScrollOpen) itemRemakeScroll.RequestClose(); }
}
