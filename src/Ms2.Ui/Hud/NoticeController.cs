using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// CSceneStateGame::OnNotice (@0x141728c20, scenestategame.cpp:9216): inbound 0x71, a system notice shown where its flags
/// say. The scene's helpers are the sinks; a null sink is a window that is not open (the helpers test the window first).
/// </summary>
public sealed class NoticeController
{
    public GameTables Tables = GameTables.Empty;
    /// <summary>CServerString m_trigger (stringTrigger.xml).</summary>
    public ServerStringGroup Trigger = ServerStringGroup.Empty;
    /// <summary>CReservedWordTable::ReplaceValueWords with the player's name and gender.</summary>
    public Func<string, string> ReplaceValueWords = s => s;
    /// <summary>The client language's name for GetLocalizedText: DAT_143ab0250 (localepath.cpp:62) starts "KR"; SetLanguage
    /// (@0x1406507a0, :577) sets it, through SetLanguageByCode (:278) for the "-language" launch argument
    /// (unit_1414b1230.cpp:336). The scene passes the string language the assets were loaded with.</summary>
    public string Language = "KR";
    /// <summary>
    /// The client region (DAT_143ab0248, localepath.cpp:61) for GetLocalizedText's fallbacks. It starts at 0 (KR) and only
    /// FUN_1406509e0 (localepath.cpp:597) writes it, through SetRegionByCode (:293), which only FUN_1414b1f10
    /// (unit_1414b1230.cpp:321) calls, for the "-locale" launch argument. This port runs as the KR client
    /// (<see cref="LobbyFeatures.LocaleName"/>), so 0.
    /// </summary>
    public int Region = 0;
    /// <summary>GetServerTime (unix seconds); FUN_14028b820 makes it local time.</summary>
    public Func<long> ServerTime = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>UISystemText::ShowMessage(text, type, option) of the system text 0x4e41; null without it.</summary>
    public Action<string, int, int> ShowMessage;
    /// <summary>FUN_1412751b0: the chat log's line of the kind (UIMessengerBrowser FUN_141a247c0); null without the log.</summary>
    public Action<string, int> ChatLine;
    /// <summary>UITopLayerNotify::ShowMessage(text, value) of 0x4ef5 (<see cref="TopLayerNotifyController"/>); null without
    /// that window, and then a top layer notice shows nothing and logs nothing, as the client without it.</summary>
    public Action<string, int> TopLayerNotify;
    /// <summary>
    /// The common string key of an id the server sends. The client this port follows registers its ids in
    /// CommonString::RegisterIds (<see cref="Ms2.Formats.Text.CommonStringIds"/>); a GMS2 server sends the GMS2 client's ids
    /// (<see cref="Ms2.Formats.Text.Gms2CommonStringIds"/>), which drift from the KMS ones from 74 on, so the scene picks the
    /// table of the server's region.
    /// </summary>
    public Func<int, string> ServerStringKey = Ms2.Formats.Text.CommonStringIds.Key;
    /// <summary>ShowMessageBox(text, 0, no listener, tag).</summary>
    public Action<string, string> MessageBox;
    /// <summary>PlaySystemSound(FUN_14016a600(text, 0), ""). STILL-OPEN: no system sound player here.</summary>
    public Action<string> SystemSound;
    /// <summary>Kind 5 flags 0x40: SendRequestQuitAndDisconnect and CStateService FUN_1417e25a0(0x6200009). STILL-OPEN:
    /// neither is ported; the sink gets the call before the box.</summary>
    public Action ForcedQuit;
    /// <summary>The flags 0x80 box's FUN_140d86260()->vfunc_58(0x289, 0). STILL-OPEN: not identified.</summary>
    public Action MaintenanceExit;

    /// <summary>DAT_143ae79d8 (scenestategame.cpp:1179): the maintenance exit box's tag.</summary>
    public const string MaintenanceExitTag = "maintenanceExit";

    /// <summary>
    /// StringIdInterface::FUN_141024e60 (stringidinterface.cpp:101). Formatted: kind 1 is the common string of the id
    /// (StringManager FUN_14084f9c0, StringXmlCategory 1), kind 2 is "", kind 3 a PVP string (STILL-OPEN: not loaded here,
    /// so ""); each argument goes in through Unk_TextFormat, then ReplaceObjectAndValueWords. Not formatted: kind 0 is the
    /// text, kind 5 GetStringCommonByKey(text), anything else "".
    /// </summary>
    public string Text(StringIdText t)
    {
        if (t.Formatted)
        {
            string format = t.Kind switch { 1 => ServerStringKey(t.Id) is { } key ? Tables.Common(key) ?? "" : "", 2 => null, _ => "" };
            if (format == null) return "";
            string s = Ms2.Formats.Text.ArgumentFormat.Format(format, t.Args);
            return ReplaceValueWords(Tables.ReservedWords.ReplaceObjectWords(s));
        }
        return t.Kind switch { 0 => t.Text ?? "", 5 => Tables.Common(t.Text ?? "") ?? "", _ => "" };
    }

    public void OnNotice(NoticeMessage n)
    {
        string text = Text(n.Text);
        if (n.Kind == 4)
        {
            uint flags = n.Flags;
            text = Trigger.GetString(text, ReplaceValueWords);
            if ((flags & 2) != 0) return;
            if ((flags & 0x20) != 0) { SystemSound?.Invoke(text); return; }
            text = ServerStringGroup.LocalizedText(text, Language, Region);
            if ((flags & 4) != 0) SystemNotice(text, SystemTextController.Gm, false);
            else if ((flags & 0x10) != 0) ShowMessage?.Invoke(text, SystemTextController.Emergency, n.Arg);
            else if ((flags & 0x200) != 0)
            {
                if (TopLayerNotify != null) { TopLayerNotify(text, n.TopLayerArg); ChatLine?.Invoke(text, TalkKind.TopLayer); }
            }
            else if ((flags & 0x400) != 0) SystemNotice(text, SystemTextController.Warning2, true);
            if ((flags & 1) != 0) ChatLine?.Invoke(TimePrefix(flags, text), (flags & 0x100) != 0 ? TalkKind.TopLayer : TalkKind.SystemNotice);
            if (flags == 0) ChatLine?.Invoke(text, TalkKind.SystemNotice);
            if ((flags & 0x40) != 0) MessageBox?.Invoke(text, "");
            else if ((flags & 0x80) != 0) { MessageBox?.Invoke(text, MaintenanceExitTag); MaintenanceExit?.Invoke(); }
            // flags 0x800: Unk_AdditionalLog(GetLogString(), 0xb, 0x2f, text), the client's command log file; not kept here
        }
        else if (n.Kind == 5 && n.Flags == 0x40)
        {
            ForcedQuit?.Invoke();
            MessageBox?.Invoke(text, "");
        }
    }

    /// <summary>ShowSystemNotice (@0x1412751f0): only with the system text; with bLog also a chat line of kind 0x14.</summary>
    void SystemNotice(string text, int type, bool log)
    {
        if (ShowMessage == null) return;
        ShowMessage(text, type, 0);
        if (log) ChatLine?.Invoke(text, TalkKind.SystemNotice);
    }

    /// <summary>FUN_14028ee20 (commonutil.cpp:1139): with flags 8, FormatStringId(0xa9 s_timeevent_notice_chat, hour,
    /// minute, text) at the server time in local time.</summary>
    public string TimePrefix(uint flags, string text)
    {
        if ((flags & 8) == 0) return text;
        DateTime t;
        try { t = DateTimeOffset.FromUnixTimeSeconds(ServerTime()).ToLocalTime().DateTime; } catch (ArgumentOutOfRangeException) { return text; }
        return Ms2.Quest.ClientFormat.Format(Tables.Common(0xa9), t.Hour, t.Minute, text);
    }
}
