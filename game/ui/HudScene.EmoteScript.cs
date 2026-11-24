using System.Linq;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>The scripted live run's emote steps (MS2_LIVE_EMOTE): the dialogs opened and clicked as a player would.</summary>
public partial class HudScene
{
    /// <summary>The dynamic action dialog opened (the messenger's eventEmotion); its listed count once loaded.</summary>
    public int OpenEmotionDialogScripted()
    {
        if (!DynamicActionOpen) OpenDynamicAction();
        return dynamicAction?.ListedCount ?? 0;
    }

    public int EmotionDialogListed => DynamicActionOpen ? dynamicAction.ListedCount : -1;

    /// <summary>The dialog's onSelectEmotion with that id (a click on its slot); false while the dialog is closed.</summary>
    public bool SelectEmotionScripted(uint id)
    {
        if (!DynamicActionOpen) return false;
        dynamicAction.OnCallback("onSelectEmotion", new object[] { id });
        return true;
    }

    /// <summary>The first emotion valid now (GetValidEmotions' order) whose skill row is a social emotion (main type 0xe); with
    /// <paramref name="withIdle"/> the first whose emotion in the own face item's group has an idleAnim.</summary>
    public uint FirstValidEmotionScripted(bool withIdle)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        validEmotions.Clear();
        (live?.Emotions ?? offlineEmotions).GetValid(live?.GameSession?.ServerTime ?? 0, validEmotions);
        var group = (GetTree().GetFirstNodeInGroup(Player.Group) as Player)?.FaceEmotion?.Group;
        return validEmotions.Select(e => (uint)e.Id).FirstOrDefault(id =>
            hudModel?.Tables.Skills.GetSkillData(id) is { MainType: Ms2.Ui.Skills.EmotionCast.MainTypeEmotion } row
            && (!withIdle || Ms2.Ui.Skills.EmotionCast.Animation(group, row.ParamStr, 1) != null));
    }

    /// <summary>"/" and the first reserved word (korDynamicAction) of the first emotion valid now that has a row; null for none.</summary>
    public string FirstEmotionWordScripted(uint skip)
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        validEmotions.Clear();
        (live?.Emotions ?? offlineEmotions).GetValid(live?.GameSession?.ServerTime ?? 0, validEmotions);
        foreach (var e in validEmotions)
        {
            if ((uint)e.Id == skip || hudModel?.Tables.DynamicActionReserved((uint)e.Id) is not { Count: > 0 } words) continue;
            if (hudModel.Tables.Skills.GetSkillData((uint)e.Id)?.MainType != Ms2.Ui.Skills.EmotionCast.MainTypeEmotion) continue;
            return words[0].StartsWith('/') ? words[0] : "/" + words[0];
        }
        return null;
    }

    /// <summary>The own player's state id and emotion skill id now.</summary>
    public (int State, int Emotion) OwnEmotionState() =>
        GetTree().GetFirstNodeInGroup(Player.Group) is Player me ? (me.ClientStateId, me.EmotionId) : (-1, 0);

    /// <summary>The sticker dialog opened (the messenger's eventChatEmoticonOpen) and the first sticker the player can use;
    /// 0 for none.</summary>
    public uint OpenStickerDialogScripted()
    {
        if (!ChatEmoticonOpen) OpenChatEmoticon();
        var s = ChatLog.ChatEmoticons;
        return s?.Table.Emoticons.Select(e => e.Id).OrderBy(id => id).FirstOrDefault(s.IsEmoticonUsable) ?? 0;
    }

    /// <summary>The dialog's onSelectEmoticon (the first chat window's preview), then that preview's click
    /// (SendEmoticon, ChatSticker 3); false without a window or when it is not usable.</summary>
    public bool SendStickerScripted(uint id)
    {
        if (messengers.Count == 0 || chatEmoticon == null) return false;
        chatEmoticon.OnCallback("onSelectEmoticon", new object[] { id });
        return messengers[0].Side.SendEmoticon((int)id);
    }

    /// <summary>The inventory opened on the tab holding that item and its slot double clicked (notifySlotUse, button 0);
    /// the item's uid, 0 when the bag does not hold it.</summary>
    public long UseItemScripted(int itemId)
    {
        var view = Ms2Godot.Net.LiveSession.Instance?.Inventory;
        if (view == null) return 0;
        if (inventory == null || inventory.IsClosed) OpenInventory();
        if (inventory?.Host == null) return 0;
        for (int t = 0; t < inventory.Tabs.Count; t++)
        {
            var cells = view.Cells(inventory.Tabs[t].Tab);
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].Empty || cells[i].ItemId != itemId) continue;
                inventory.Host.Set(inventory.Host.Child("m_tab"), "selectedIndex", t);
                if (view.Tab != inventory.Tabs[t].Tab || !inventory.IsDragSlot(Ms2.Ui.Hud.InventoryController.SlotName(i)))
                    return -1;                                          // the movie or the tab change is not there yet: ask again
                inventory.OnCallback("notifySlotUse", new object[] { Ms2.Ui.Hud.InventoryController.SlotName(i), 0 });
                return cells[i].Uid;
            }
        }
        return 0;
    }

    public void CloseEmoteDialogsScripted()
    {
        if (DynamicActionOpen) dynamicAction.Close();
        if (ChatEmoticonOpen) chatEmoticon.Close();
        if (inventory != null && !inventory.IsClosed) inventory.Close();
    }
}
