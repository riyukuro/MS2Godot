using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// The client's connection errors outside the field (the login, character select and create scenes), the window service's
/// two socket messages:
/// <list type="bullet">
/// <item>0x467, a connect that failed (CClientPlatformBase::vfunc_31, ms2clientplatformbase.cpp:295, from the connect
/// event with an error, gamesocket.cpp:176): the box <see cref="ConnectFailedId"/> with the error code, whose OK runs the
/// scene's vfunc_64 (FUN_141902530, uicommon.cpp:1941); on character select that pops the list up again
/// (UISceneLobbySelect::vfunc_64, uiscenelobbyselect.cpp:470), on the login scene the dialog's failure state
/// (UISceneLogin::vfunc_64, uiscenelogin.cpp:306). windowservice.cpp:606-621.</item>
/// <item>0x465, a connection the socket closed with an error (CGameSocket::OnClose, gamesocket.cpp:221-236: only with
/// m_bDisconnect 0, an error and the state connected; a clean close by the peer has error 0 and only logs). Outside the
/// field no reconnect starts (ShouldReconnect needs scene 0x620000c, reconnectionmanagercli.cpp:58-59), so the scene goes
/// to the login scene (FUN_1417e25a0(0x6200009)) and the box <see cref="DisconnectedId"/> shows the code's reason and the
/// code. windowservice.cpp:454-545.</item>
/// </list>
/// </summary>
public static class FrontEndConnection
{
    /// <summary>s_login_err_connect, formatted with the error code (BlockFormat&lt;int&gt;(0x20e, code)).</summary>
    public const int ConnectFailedId = 0x20e;
    /// <summary>s_login_err_disconnected, formatted with "reason : code" (FormatStringId(0x20f, ...)).</summary>
    public const int DisconnectedId = 0x20f;
    /// <summary>s_notice_bypass: the text in place of <see cref="DisconnectedId"/> when DAT_143ab2858 is set, which only the
    /// BypassKey packet sets (CNetworkService::OnBypassKey, networkservice.cpp:891-895). Neither server sends it and the
    /// port does not read it, so the flag stays 0 and this text is not used.</summary>
    public const int BypassDisconnectedId = 0xc69;

    /// <summary>The reason string id of a socket error code (windowservice.cpp:487-531); 0 (s_empty_string) for a code
    /// the switch does not name, as the client's nMsgId starts at 0.</summary>
    public static int ReasonId(int code) => code switch
    {
        0x2714 => 0xd29, 0x271d => 0xd2a, 0x271e => 0xd2b, 0x2726 => 0xd2c, 0x2728 => 0xd2d, 0x2733 => 0xd2e,
        0x2734 => 0xd2f, 0x2735 => 0xd30, 0x2736 => 0xd28, 0x2737 => 0xd31, 0x2738 => 0xd32, 0x2739 => 0xd28,
        0x273a => 0xd28, 0x273b => 0xd28, 0x273c => 0xd28, 0x273d => 0xd28, 0x273e => 0xd28, 0x273f => 0xd28,
        0x2740 => 0xd33, 0x2741 => 0xd28, 0x2742 => 0xd34, 0x2743 => 0xd35, 0x2744 => 0xd36, 0x2745 => 0xd37,
        0x2746 => 0xd38, 0x2747 => 0xd39, 0x2748 => 0xd3a, 0x2749 => 0xd28, 0x274a => 0xd3b, 0x274c => 0xd3c,
        0x274d => 0xd3d, 0x2750 => 0xd3e, 0x2751 => 0xd3f, 0x2753 => 0xd40, 0x276b => 0xd28, 0x276c => 0xd28,
        0x276d => 0xd28, 0x2775 => 0xd41, 0x277d => 0xd28, 0x2af9 => 0xd42, 0x2afa => 0xd43, 0x2afb => 0xd28,
        0x2afc => 0xd28,
        _ => 0,
    };

    /// <summary>Every code the reason switch names, in its order (the whole list the tests check).</summary>
    public static readonly int[] ReasonCodes =
    {
        0x2714, 0x271d, 0x271e, 0x2726, 0x2728, 0x2733, 0x2734, 0x2735, 0x2736, 0x2737, 0x2738, 0x2739, 0x273a, 0x273b,
        0x273c, 0x273d, 0x273e, 0x273f, 0x2740, 0x2741, 0x2742, 0x2743, 0x2744, 0x2745, 0x2746, 0x2747, 0x2748, 0x2749,
        0x274a, 0x274c, 0x274d, 0x2750, 0x2751, 0x2753, 0x276b, 0x276c, 0x276d, 0x2775, 0x277d, 0x2af9, 0x2afa, 0x2afb,
        0x2afc,
    };

    /// <summary>The 0x465 box text: the reason (GetStringCommon), " : " after it when it is not empty, then the code in
    /// decimal, formatted into s_login_err_disconnected (windowservice.cpp:532-541).</summary>
    public static string Disconnected(int code, Func<int, string> common)
    {
        string reason = common(ReasonId(code)) ?? "";
        if (reason.Length > 0) reason += " : ";
        return ClientFormat.Format(common(DisconnectedId) ?? "", reason + code.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The 0x467 box text: s_login_err_connect with the code (BlockFormat&lt;int&gt;(0x20e, code)).</summary>
    public static string ConnectFailed(int code, Func<int, string> common) => ClientFormat.Format(common(ConnectFailedId) ?? "", code);

    /// <summary>
    /// The socket error code the client's socket would report for <paramref name="e"/>, the error that ended a connection:
    /// the Winsock code of the first <see cref="System.Net.Sockets.SocketException"/> in the chain (.NET reports the Winsock
    /// numbers on every platform), 0 for a clean close by the peer (the read returned nothing, FD_CLOSE with error 0) and
    /// for anything else.
    /// </summary>
    public static int CodeOf(Exception e)
    {
        for (var x = e; x != null; x = x.InnerException)
            if (x is System.Net.Sockets.SocketException s) return (int)s.SocketErrorCode;
        return 0;
    }
}
