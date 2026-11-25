using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIItemTreasureMapDialog (0x4f5b, <see cref="ItemTreasureMapDialogController"/>), opened by an npc talk's open-dialog action
/// "ItemTreasureMapDialog" (uitalkcinematic.cpp:1221: FUN_140c55b50(argument) on the dialog, then OpenOrQueueDialog). Live
/// only. The server's ItemScript subs 2 and 3 go to it while it is open (its vfunc_79).
/// </summary>
public partial class HudScene
{
    ItemTreasureMapDialogController treasureMap;
    MovieDialog treasureMapMovie;
    Action<ItemScriptTreasureMap> treasureMapAnswer;

    bool TreasureMapOpen => treasureMap != null && !treasureMap.IsClosed;

    /// <summary>The talk's "ItemTreasureMapDialog" (no case): the free flag from the argument, then the dialog; true when it was
    /// this dialog's name.</summary>
    bool TreasureMapTalk(string dialog, string argument)
    {
        if (!string.Equals(dialog, ItemTreasureMapDialogController.Name, StringComparison.OrdinalIgnoreCase)) return false;
        if (TreasureMapOpen) { treasureMap.SetFree(argument); return true; }   // OpenOrQueueDialog keeps an open dialog
        OpenTreasureMapDialog(argument);
        return true;
    }

    void OpenTreasureMapDialog(string argument)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live == null) { GD.Print("treasure map: needs the live session"); return; }
        CloseTreasureMapMovie();
        var c = new ItemTreasureMapDialogController
        {
            CategoryItems = cat => inventory?.CategoryItems(cat) ?? System.Array.Empty<Ms2.Ui.Items.Item>(),
            FindByUid = uid => inventory?.FindItem(uid),
            RequestUse = (item, arg) => inventory?.RequestUseItem(item, arg) ?? false,
            Send = request => live.SendShop(request),
            Mesos = () => hudModel.HasLocalPlayer ? hudModel.Player.Mesos : 0,
            PlaySound = key => FieldAudio.Instance?.PlayUi(key),
            CommonNotice = id => ShowSystemText(MessageBoxData.Common(id)),
        };
        c.SetFree(argument);
        c.Unloaded += CloseTreasureMapMovie;
        treasureMap = c;
        Action<ItemScriptTreasureMap> answer = m => c.OnItemScript(m);
        treasureMapAnswer = answer;
        live.ItemScriptTreasureMapped += answer;
        treasureMapMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"treasure map: free {c.Free} load={treasureMapMovie.Loaded}");
        uiRoot.AddChild(treasureMapMovie);
    }

    void CloseTreasureMapMovie()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null && treasureMapAnswer != null) live.ItemScriptTreasureMapped -= treasureMapAnswer;
        treasureMapAnswer = null;
        if (treasureMapMovie == null) return;
        var m = treasureMapMovie;
        treasureMapMovie = null;
        m.Host.Unload();
        m.QueueFree();
    }

    /// <summary>UIDialog's key handling: Escape closes the dialog.</summary>
    bool TreasureMapKeys(InputEventKey k)
    {
        if (!TreasureMapOpen || k.Keycode != Key.Escape) return false;
        treasureMap.Close();
        return true;
    }
}
