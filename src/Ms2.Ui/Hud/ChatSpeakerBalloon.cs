using Ms2.Formats.Options;

namespace Ms2.Ui.Hud;

/// <summary>
/// A chat line over its speaker's head: UIMessengerBrowser::ShowChatBalloon (@0x141a26f10, uimessenger.cpp:3743), called by
/// the UserChat handler (vfunc_84, :2940; <see cref="MessengerBrowser.ChatBalloon"/>) after the item links are coloured, and
/// PCBase::ShowChatBalloon (@0x14166fff0, pcbase.cpp:1664) on the speaker.
/// <para>
/// No local player, or no PC of the line's name (WorldService::FUN_141bd1420, worldservice_1.cpp:6198: the first of the PC
/// map m_118, the own PC among them, whose GetName is the name), shows nothing (0). The own line while the talk manager
/// is in a talk (GetTalking) is taken (1) and not shown. Else the status option of the type (FUN_14125c820,
/// gameoptionstruct.cpp:1489, <see cref="TypeShows"/>) decides: on, a party (7) line gets the common string 0xaa7
/// (s_messenger_party_chatballoon_prefix) before it, a guild (8) one 0xaa8 (s_messenger_guild_chatballoon_prefix), and the
/// balloon shows when the speaker is the local player or nearer than ChatBalloonDistance (the speaker's m_unk_bc,
/// FUN_140f4a450); 1 either way. Off: 0.
/// </para>
/// <para>
/// STILL-OPEN: PCBase::ShowChatBalloon's first call, vfunc_94(0), is empty for another PC; for the local player
/// (MyPC::vfunc_94 @0x141523010, mypc.cpp:2908) it runs the current state's vfunc_21(0), which in the idle state
/// (StateIdle::vfunc_21 @0x1417ce5b0) starts the leave timer (m_delayLeave) and moves to the idle sub state 6. The player's
/// idle sub states are not ported, so <see cref="SpeakerTurn"/> has no handler in the game.
/// </para>
/// </summary>
public sealed class ChatSpeakerBalloon
{
    readonly ChatBalloonController balloons;

    /// <summary>WorldService::GetLocalPlayer: the own PC's balloon actor, null when there is none.</summary>
    public Func<ChatBalloonController.Actor> LocalPlayer = () => null;
    /// <summary>WorldService::FUN_141bd1420: the first PC of that name (the own one included), null when none.</summary>
    public Func<string, ChatBalloonController.Actor> FindPc = _ => null;
    /// <summary>UITalkManager::GetTalking: a talk is up.</summary>
    public Func<bool> Talking = () => false;
    /// <summary>CGameOption::GetStatus.</summary>
    public Func<GameOption.Status> Status = () => null;
    /// <summary>GetStringCommon(id).</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>CReservedWordTable::ReplaceValueWords.</summary>
    public Func<string, string> ReplaceValueWords = t => t;
    /// <summary>The speaker's vfunc_94(0): the local player's state vfunc_21(0) (see the STILL-OPEN above).</summary>
    public Action<ChatBalloonController.Actor> SpeakerTurn;
    /// <summary>The ChatBalloonDistance constant.</summary>
    public float Distance = 2000;

    public ChatSpeakerBalloon(ChatBalloonController balloons) => this.balloons = balloons;

    /// <summary>The common string ids of the party and guild prefixes.</summary>
    public const int PartyPrefix = 0xaa7, GuildPrefix = 0xaa8;

    /// <summary>FUN_14125c820: the status option of a chat type's balloon; 0 normal, 7 party, 8 and 0x17 guild, else off.</summary>
    public static bool TypeShows(GameOption.Status s, int type) => type switch
    {
        0 => s.DisplayNormalChatBalloon,
        7 => s.DisplayPartyChatBalloon,
        8 or 0x17 => s.DisplayGuildChatBalloon,
        _ => false,
    };

    /// <summary>UIMessengerBrowser::ShowChatBalloon(name, text, type): 1 when taken, 0 when not.</summary>
    public int Show(string name, string text, int type)
    {
        var me = LocalPlayer();
        if (me == null) return 0;
        var speaker = FindPc(name ?? "");
        if (speaker == null) return 0;
        if (speaker == me && Talking()) return 1;
        var status = Status();
        bool show = status != null && TypeShows(status, type);
        string balloon = "";
        if (type == 7 && show) balloon = Common(PartyPrefix) ?? "";
        else if (type == 8 && show) balloon = Common(GuildPrefix) ?? "";
        balloon += text ?? "";
        float distance = speaker.Distance;
        if (!show) return 0;
        if (speaker == me || distance < Distance) ShowChatBalloon(speaker, balloon);
        return 1;
    }

    /// <summary>PCBase::ShowChatBalloon: vfunc_94(0), the value words, then the PC balloon's text and start (vfunc_2).</summary>
    public void ShowChatBalloon(ChatBalloonController.Actor speaker, string text)
    {
        SpeakerTurn?.Invoke(speaker);
        text = ReplaceValueWords(text ?? "");
        var e = balloons.PcBalloon(speaker);
        if (e == null) return;
        ChatBalloonController.SetText(e, text);
        balloons.Start(e);
    }
}
