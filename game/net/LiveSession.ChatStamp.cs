using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.ChatEmoticons;

namespace Ms2Godot.Net;

/// <summary>
/// The local player's chat emoticons (CChatEmoticonCli) from the ChatStamp packet (0x126, OnChatStamp @0x14173e440, which
/// hands the body to CChatEmoticonCli::DispatchPacket @0x140a3a700). The dialog (uichatemoticondialog.cpp) sends the list
/// request when it opens; the messenger windows send ChatSticker modes 3, 5 and 6.
/// </summary>
public partial class LiveSession
{
    ChatEmoticonState chatEmoticons;

    /// <summary>The state, built on first use over the emoticon table (the client's MyPC owns it from the character's add on).</summary>
    public ChatEmoticonState ChatEmoticons
    {
        get
        {
            if (chatEmoticons != null) return chatEmoticons;
            var table = GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage).ChatEmoticonData;
            return chatEmoticons = new ChatEmoticonState(table) { Now = () => GameSession?.ServerTime ?? 0 };
        }
    }

    /// <summary>OnChatStamp @0x14173e440 (scenestategame.cpp:5177-5180): the own character's (GetMyCharacter, from the game
    /// scene's start, not the field's MyPC) CChatEmoticonCli::DispatchPacket, so a list before the own OnAddUser is kept.</summary>
    void OnChatStamp(ChatStampMessage m)
    {
        if (m == null) return;
        ChatEmoticons.Apply(m);
        if (LiveCheck) GD.Print($"live: ChatStamp {m.GetType().Name}");
    }
}
