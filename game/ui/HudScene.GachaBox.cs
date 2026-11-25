using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// UIItemGachaBoxDialog (0x4f8b, <see cref="ItemGachaBoxDialogController"/>), the capsule machine OpenGachaBox opens
/// (@0x140d25e10: GetItemUseDialog(0x4f8b), open: nothing; else SetBoxItemId and open). Live only. The server's ItemScript
/// mode 5 goes to it while it is open (its vfunc_79).
/// STILL-OPEN: the rolling sound is not stopped on the idle state or the unload (PlaySystemSound's handle and
/// CEffectService::FUN_1411a6060 are not in FieldAudio's UI sounds); the shop link box's dialog 0x4ec1 (the Merat market).
/// </summary>
public partial class HudScene
{
    ItemGachaBoxDialogController gachaBox;
    MovieDialog gachaBoxMovie;
    Action<ItemScriptBoxResult> gachaAnswer;

    bool GachaBoxOpen => gachaBox != null && !gachaBox.IsClosed;

    /// <summary>OpenGachaBox's open: the dialog with the box item's id (SetBoxItemId), loaded.</summary>
    void OpenGachaBoxDialog(Item item)
    {
        if (GachaBoxOpen) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        CloseGachaBoxMovie();
        var c = new ItemGachaBoxDialogController
        {
            FindItemById = id => id == item.ItemId && inventory?.FindItem(item.Uid) is { } same ? same : inventory?.FindFirstById(id),
            HaveCount = id => inventory?.BagCountOf(id) ?? 0,
            RequestUse = (box, mode) => inventory?.FunctionUse(box, mode) ?? false,
            Self = () => inventory?.Wearer?.Invoke() is { } me ? (me.JobGroup, me.Gender) : null,
            Send = request => live?.SendShop(request),
            PlaySound = key => { FieldAudio.Instance?.PlayUi(key); return 0; },
            ShowBox = box => MessageBoxQueue.Show(box),
            OpenDialog = id => GD.Print($"gacha box: dialog 0x{id:x} is not in this HUD"),
        };
        c.NoticeText += SystemNoticeText;
        c.Unloaded += CloseGachaBoxMovie;
        c.SetBoxItemId(hudModel, item.ItemId);
        gachaBox = c;
        Action<ItemScriptBoxResult> answer = r => c.OnResult(r);
        gachaAnswer = answer;
        if (live != null) live.ItemScriptBoxResulted += answer;
        gachaBoxMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"gacha box: item {item.ItemId} load={gachaBoxMovie.Loaded}");
        uiRoot.AddChild(gachaBoxMovie);
    }

    void CloseGachaBoxMovie()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null && gachaAnswer != null) live.ItemScriptBoxResulted -= gachaAnswer;
        gachaAnswer = null;
        if (gachaBoxMovie == null) return;
        gachaBox?.Unload();
        var m = gachaBoxMovie;
        gachaBoxMovie = null;
        m.Host.Unload();
        m.QueueFree();
    }

    /// <summary>UIDialog's key handling: Escape closes the machine.</summary>
    bool GachaBoxKeys(InputEventKey k)
    {
        if (!GachaBoxOpen || k.Keycode != Key.Escape) return false;
        gachaBox.Close();
        return true;
    }
}
