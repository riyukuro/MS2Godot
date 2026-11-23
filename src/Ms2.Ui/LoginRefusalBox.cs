using System;
using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>
/// CNetworkService::OnLoginResult @0x14153c670 (networkservice.cpp:991-1066): the box each refused login result gets. Every
/// branch then runs UISceneLogin::FUN_141acf470 on the login scene, the dialog's failure state (<see cref="LoginConnectFailure.AfterRefusal"/>).
/// The analytics calls (FUN_140d86260 vfunc_23/58/59) are not ported. Results and their boxes:
/// <list type="bullet">
/// <item>7: CMsTime::SetTimeZone(zone, flag), then box type 0x10 of s_login_err_restrict (0x21a) with the start and end
/// (year, month, day, hour, minute, second each) and the reason, titled s_login_err_restrict_title (0x219).</item>
/// <item>11 and 12: the server's text ("\n" written as two characters made a line break), titled s_maintenance_title (0x8c6); 12
/// carries the maintenanceExit tag.</item>
/// <item>0x12, 0x13, 0x15, 0xf, 0x18, 0x19: a fixed text with the maintenanceExit tag, titled 0x219.</item>
/// <item>0xe and 0x14: a fixed text with the externalBlock / autoExternalBlock tag and argument "0" (FUN_140a30b80(data, 0)),
/// titled 0x219.</item>
/// <item>4 and 5 (already logged in): s_alreadyloginuser_response_kick (FUN_140cc1fb0) with the alreadyReconnect tag, no title.</item>
/// <item>0x1a: s_founderspack_need with buy / close buttons, Enter does not answer (FUN_141a024a0(0)), and
/// OnFoundersMsgBoxResult as its callback.</item>
/// <item>Any other: the id and password the client keeps are cleared (DAT_143ab29d8, DAT_143ab2a28; ours keeps none: the next
/// login reads the dialog's fields, as the client does when they are empty), then a notice of FUN_140cc1fb0(result); 0x218
/// has the error number formatted into it.</item>
/// </list>
/// </summary>
public static class LoginRefusalBox
{
    public const string MaintenanceExitTag = "maintenanceExit", AlreadyReconnectTag = "alreadyReconnect";
    public const string ExternalBlockTag = "externalBlock", AutoExternalBlockTag = "autoExternalBlock";
    /// <summary>OnFoundersMsgBoxResult @0x1415395a0 (networkservice.cpp:634-641): the page its first button opens.</summary>
    public const string FoundersUrl = "http://maplestory2.nexon.net/founders";

    /// <summary>FUN_140cc1fb0 @0x140cc1fb0: the common string of a login refusal.</summary>
    public static int RefusalString(int result) => result switch
    {
        1 or 2 => 0x210, 3 => 0x211, 4 or 5 => 0xace, 6 => 0x213, 8 => 0x216, 10 => 0x215, 16 => 0x218, 22 => 0x224,
        _ => 0x217,
    };

    static string Format(string text, params object[] args) => ClientFormat.Format(text, args);

    /// <summary>
    /// The box for a refused <paramref name="result"/>, not shown yet. <paramref name="start"/> and <paramref name="end"/> are
    /// result 7's unix seconds, shown in the zone the packet sets (<paramref name="zoneSeconds"/>). <paramref name="founders"/>
    /// is result 0x1a's callback (its button: 0 buy).
    /// </summary>
    public static MessageBoxData Build(int result, int error, string message, long start = 0, long end = 0, int zoneSeconds = 0, Func<int, MessageBoxData, bool> founders = null)
    {
        string restrictTitle = MessageBoxData.Common(0x219);
        MessageBoxData Fixed(int id, string tag) { var d = MessageBoxQueue.Create(0, MessageBoxData.Common(id), null, tag); d.Title = restrictTitle; return d; }
        switch (result)
        {
            case 7:
            {
                var zone = TimeSpan.FromSeconds(zoneSeconds);
                var s = DateTimeOffset.FromUnixTimeSeconds(start).ToOffset(zone);
                var e = DateTimeOffset.FromUnixTimeSeconds(end).ToOffset(zone);
                string text = Format(MessageBoxData.Common(0x21a), s.Year, s.Month, s.Day, s.Hour, s.Minute, s.Second,
                    e.Year, e.Month, e.Day, e.Hour, e.Minute, e.Second, message ?? "");
                var d = MessageBoxQueue.Create(0x10, text, null, "");
                d.Title = restrictTitle;
                return d;
            }
            case 11 or 12:
            {
                var d = MessageBoxQueue.Create(0, (message ?? "").Replace("\\n", "\n"), null, result == 12 ? MaintenanceExitTag : "");
                d.Title = MessageBoxData.Common(0x8c6);
                return d;
            }
            case 0x18: return Fixed(0x221, MaintenanceExitTag);
            case 0x12: return Fixed(0x21b, MaintenanceExitTag);
            case 0x15: return Fixed(0x21e, MaintenanceExitTag);
            case 0xe: { var d = Fixed(0x21f, ExternalBlockTag); d.Append(0); return d; }
            case 0xf: return Fixed(0x220, MaintenanceExitTag);
            case 0x14: { var d = Fixed(0x223, AutoExternalBlockTag); d.Append(0); return d; }
            case 4 or 5: return MessageBoxQueue.Create(0, MessageBoxData.Common(RefusalString(result)), null, AlreadyReconnectTag);
            case 0x13: return Fixed(0x222, MaintenanceExitTag);
            case 0x19:
            {
                var d = MessageBoxQueue.Create(0, MessageBoxData.CommonByKey("s_login_err_nxa_ticket") ?? "", null, MaintenanceExitTag);
                d.Title = restrictTitle;
                return d;
            }
            case 0x1a:
            {
                var d = MessageBoxQueue.Create(1, MessageBoxData.CommonByKey("s_founderspack_need") ?? "", null, "");
                d.Callback = founders ?? ((_, _) => true);
                d.EnterAllowed = false;
                d.SetButtonLabels(MessageBoxData.Common(0x47d), MessageBoxData.CommonByKey("s_gamemode_rpg_close") ?? "", "");
                return d;
            }
            default:
            {
                // STILL-OPEN: the JP region's text for error 0x4e2e (RawString 0x17) is not ported; KMS and GMS are not JP
                int id = RefusalString(result);
                return MessageBoxQueue.Create(0, id == 0x218 ? Format(MessageBoxData.Common(id), error) : MessageBoxData.Common(id), null, "");
            }
        }
    }
}
