using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// UIItemLulluBoxDialog (0x4f2b, <see cref="ItemLulluBoxDialogController"/>), the Lullu box OpenItemBoxLullu and
/// OpenItemBoxLulluSimple open (@0x140d22390: GetItemUseDialog(0x4f2b), open: nothing; else SetBoxItemId and open). Live only.
/// The server's ItemScript mode 0 goes to it while it is open (its vfunc_79). STILL-OPEN: the shop link box's dialog 0x4ec1
/// and the item break dialog 0x4e8d are not in this HUD.
/// </summary>
public partial class HudScene
{
    ItemLulluBoxDialogController lulluBox;
    MovieDialog lulluBoxMovie;
    Action<ItemScriptBoxResult> lulluAnswer;
    Func<string, string> lulluWords;

    bool LulluBoxOpen => lulluBox != null && !lulluBox.IsClosed;

    /// <summary>OpenItemBoxLullu's open: the dialog with the box item's id, loaded.</summary>
    void OpenLulluBoxDialog(Item item)
    {
        if (LulluBoxOpen) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        CloseLulluBoxMovie();
        var c = new ItemLulluBoxDialogController
        {
            TagCount = tag => inventory?.BagCountOfTag(tag) ?? 0,
            FindByTag = tag => inventory?.FindFirstByTag(tag),
            RequestUse = (key, mode) => inventory?.FunctionUse(key, mode) ?? false,
            Self = () => inventory?.Wearer?.Invoke() is { } me ? (me.JobGroup, me.Gender) : null,
            Mesos = () => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0,
            PlaySound = key => FieldAudio.Instance?.PlayUi(key),
            ShowBox = box => MessageBoxQueue.Show(box),
            OpenDialog = id => GD.Print($"lullu box: dialog 0x{id:x} is not in this HUD"),
            ToggleShopTab = tab => ToggleShopTab(tab),
            ReplaceWords = lulluWords ??= SidePopupWords(),
        };
        c.NoticeText += SystemNoticeText;
        c.Unloaded += CloseLulluBoxMovie;
        c.SetBoxItemId(item.ItemId);
        lulluBox = c;
        Action<ItemScriptBoxResult> answer = r => c.OnResult(r);
        lulluAnswer = answer;
        if (live != null) live.ItemScriptBoxResulted += answer;
        lulluBoxMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"lullu box: item {item.ItemId} load={lulluBoxMovie.Loaded}");
        uiRoot.AddChild(lulluBoxMovie);
    }

    void CloseLulluBoxMovie()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null && lulluAnswer != null) live.ItemScriptBoxResulted -= lulluAnswer;
        lulluAnswer = null;
        if (lulluBoxMovie == null) return;
        var m = lulluBoxMovie;
        lulluBoxMovie = null;
        m.Host.Unload();
        m.QueueFree();
    }
}
