using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// WindowService uMsg 0x465 (windowservice.cpp:482-555) and CReconnectionManagerCli::OnReconnectPacket kind 3
/// (reconnectionmanagercli.cpp:314-325): the message box text when a connection drops without a reconnect, when a
/// reconnect gives up, or when the server refuses/ends the session.
/// </summary>
public static class DisconnectNotice
{
    public const int DisconnectFormatId = 0x20f;
    public const int BypassDisconnectFormatId = 0xc69;

    /// <summary>The client's socket error to common-string-id switch (windowservice.cpp:486-531); 0 when unmapped.</summary>
    public static int ReasonStringId(int code) => code switch
    {
        0x2714 => 0xd29,
        0x271d => 0xd2a,
        0x271e => 0xd2b,
        0x2726 => 0xd2c,
        0x2728 => 0xd2d,
        0x2733 => 0xd2e,
        0x2734 => 0xd2f,
        0x2735 => 0xd30,
        0x2736 => 0xd28,
        0x2737 => 0xd31,
        0x2738 => 0xd32,
        0x2739 or 0x273a or 0x273b or 0x273c or 0x273d or 0x273e or 0x273f => 0xd28,
        0x2740 => 0xd33,
        0x2741 => 0xd28,
        0x2742 => 0xd34,
        0x2743 => 0xd35,
        0x2744 => 0xd36,
        0x2745 => 0xd37,
        0x2746 => 0xd38,
        0x2747 => 0xd39,
        0x2748 => 0xd3a,
        0x2749 => 0xd28,
        0x274a => 0xd3b,
        0x274c => 0xd3c,
        0x274d => 0xd3d,
        0x2750 => 0xd3e,
        0x2751 => 0xd3f,
        0x2753 => 0xd40,
        0x276b or 0x276c or 0x276d => 0xd28,
        0x2775 => 0xd41,
        0x277d => 0xd28,
        0x2af9 => 0xd42,
        0x2afa => 0xd43,
        0x2afb or 0x2afc => 0xd28,
        _ => 0,
    };

    /// <summary>
    /// Formats the uMsg 0x465 disconnect message (windowservice.cpp:532-542): <c>GetStringCommon(reasonId)</c> + <c>" : "</c>
    /// (when non-empty) + <paramref name="code"/>, formatted into string <c>0x20f</c> (or <c>0xc69</c> when <paramref name="hasBypassKey"/>
    /// is true, <c>DAT_143ab2858 != 0</c>).
    /// </summary>
    public static string FormatDisconnect(int code, Func<int, string> commonById, bool hasBypassKey = false)
    {
        int reasonId = ReasonStringId(code);
        string reason = reasonId != 0 ? commonById(reasonId) ?? "" : "";
        string detail = reason.Length > 0
            ? reason + " : " + code.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : code.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int formatId = hasBypassKey ? BypassDisconnectFormatId : DisconnectFormatId;
        return ClientFormat.Format(commonById(formatId) ?? "", detail);
    }

    /// <summary>CReconnectionManagerCli::OnReconnectPacket kind 3 in game (reconnectionmanagercli.cpp:324): FormatStringId(0x20f, 0).</summary>
    public static string FormatSessionEnded(Func<int, string> commonById) =>
        ClientFormat.Format(commonById(DisconnectFormatId) ?? "", 0);
}
