using System;
using System.Linq;
using Godot;
using Ms2.Ui;
using Ms2.Ui.ChatEmoticons;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The chat emoticon dialog: UIChatEmoticonDialog (0x4f80, movie uichatemoticondialog) over the live session's
/// CChatEmoticonCli. The messenger's eventChatEmoticonOpen toggles it (uimessenger.cpp:4058); a pick is handed to the
/// messenger windows (NotifyChatEmoticonSelected); the messenger sends ChatSticker modes 3, 5 and 6 through the chat window's SendSticker (live session only; offline nothing is sent).
/// Offline the state is empty (no packs owned) and nothing is sent.
/// STILL-OPEN: the group messenger's SendEmoticonImage (ChatStamp type 4) and eventChatEmoticonOpen
/// (uigroupmessenger.cpp:721) are not wired; the Merat shop button (0x4ec1).
/// </summary>
public partial class HudScene
{
    ChatEmoticonDialogController chatEmoticon;
    MovieDialog chatEmoticonMovie;
    Action toggleChatEmoticon;
    ChatEmoticonState offlineChatEmoticons;
    bool chatEmoticonFixture;           // --chatemoticon-fixture: every pack owned, offline

    bool ChatEmoticonOpen => chatEmoticonMovie != null && chatEmoticon?.IsClosed == false;

    ChatEmoticonState ChatEmoticonStateOf()
    {
        var live = Ms2Godot.Net.LiveSession.Instance;
        if (live != null) return live.ChatEmoticons;
        if (offlineChatEmoticons != null) return offlineChatEmoticons;
        var s = offlineChatEmoticons = new ChatEmoticonState(hudModel.Tables.ChatEmoticonData);
        if (chatEmoticonFixture)
        {
            foreach (var e in s.Table.Emoticons) if (s.FindGroup(e.GroupId) == null) s.AddGroup(e.GroupId, long.MaxValue);
        }
        return s;
    }

    /// <summary>The messenger hooks (state, toggle, send) and the state's window events; the dialog itself is built on open.</summary>
    void HookChatEmoticon()
    {
        toggleChatEmoticon = ToggleChatEmoticon;
        ChatLog.ToggleEmoticonDialog = toggleChatEmoticon;
        ChatLog.ChatEmoticons = ChatEmoticonStateOf();
        ChatLog.IsEmoticonUsable = id => id > 0 && ChatLog.ChatEmoticons.IsEmoticonUsable((uint)id);   // CChatEmoticonCli::IsEmoticonUsable
        var s = ChatLog.ChatEmoticons;
        s.MessageBox += key => MessageBoxQueue.ShowMessageBox(hudModel.Tables.Common(key) ?? "", 0, null, "");
        s.TypeInWindow += (window, flag, id) =>
        {
            foreach (var (_, c) in messengers) if (c.Name == window) c.TypeEmoticon(flag, id);
        };
    }

    void UnhookChatEmoticon()
    {
        if (ReferenceEquals(ChatLog.ToggleEmoticonDialog, toggleChatEmoticon)) ChatLog.ToggleEmoticonDialog = null;
    }

    void ChatEmoticon()
    {
        if (chatEmoticon != null) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        var t = hudModel.Tables;
        var c = chatEmoticon = new ChatEmoticonDialogController(ChatEmoticonStateOf())
        {
            CurrentMessenger = () => messengers.Count > 0 ? messengers[0].Side.Name : "",
            CommonText = id => MessageBoxData.Common(id),
            QueueOccurrence = (kind, arg) => Ms2.Ui.Guide.GuideManager.Current?.QueueOccurrence(kind, arg),
            Send = p =>
            {
                if (live != null) live.SendTalk(p);
                else GD.Print($"chat emoticon: {p.GetType().Name} not sent (offline)");
            },
        };
        c.Selected = (name, id) => { foreach (var (_, m) in messengers) m.OnChatEmoticonSelected(name, id); };   // NotifyChatEmoticonSelected
        c.Unloaded += () =>
        {
            chatEmoticonMovie?.Host.Unload();
            chatEmoticonMovie?.QueueFree();
            chatEmoticonMovie = null;
        };
    }

    /// <summary>CUIService::ToggleDialog(0x4f80): the messenger's eventChatEmoticonOpen.</summary>
    void ToggleChatEmoticon()
    {
        if (ChatEmoticonOpen) chatEmoticon.Close();
        else OpenChatEmoticon();
    }

    void OpenChatEmoticon()
    {
        ChatEmoticon();
        if (chatEmoticonMovie != null) { chatEmoticonMovie.Host.Unload(); chatEmoticonMovie.QueueFree(); chatEmoticonMovie = null; }
        chatEmoticonMovie = new MovieDialog(assets, chatEmoticon, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(chatEmoticonMovie);
    }
}
