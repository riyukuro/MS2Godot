using System;
using Ms2.Formats.Text;
using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The housing service's build notice, Unk_MyPCHousingSvc::ShowBuildNotice (@0x1410d6590,
/// cubemapinformationservice_head.cpp:306-325) with its two message tables, GetMessageSoundKey (@0x1410d5bc0, :52-175) and
/// CubeMapInformationService::GetMessageText (@0x1410d5d80, :177-303).
/// </summary>
public static class BuildNotice
{
    /// <summary>The message ShowBuildNotice answers with CUIService::FUN_141add1f0, the lack-merat box
    /// (ShowLackMeratAsk, uiservice.cpp:1108).</summary>
    public const int LackMerat = 0x22;
    /// <summary>The message ShowBuildNotice shows nothing for.</summary>
    public const int None = 1;
    /// <summary>The common string key GetMessageText reads for message 99 (GetStringCommonByKey, :283-287).</summary>
    public const string InstallNpcCountKey = "s_ugcmap_installnpc_count";

    // the notice sound keys (the CString statics @0x1400e6420, :24-32)
    const string BuildingCount = "system_notice_error_warningBuildingCount";
    const string DefaultLand = "system_notice_error_warningDefaultLand";
    const string DefaultLandRotation = "system_notice_error_warningDefaultLandRotation";
    /// <summary>The NoBuild sound key (s_strNoBuild, :27); ConfirmPlacement's held-lift sound DAT_143acf6b8
    /// (cubecraft_2.cpp:150) is the same key.</summary>
    public const string NoBuild = "system_notice_error_warningNoBuild";
    const string NoChangeCube = "system_notice_error_warningNoChangeCube";
    const string NoTearDown = "system_notice_error_warningNoTearDown";
    const string NoWall = "system_notice_error_warningNoWall";
    const string NotThisPlace = "system_notice_error_warningNotThisPlace";
    const string OnlyWall = "system_notice_error_warningOnlyWall";

    /// <summary>GetMessageSoundKey (@0x1410d5bc0, :52-175): the message's system sound key, null for none (the function's
    /// 0 return: its listed silent messages and the default).</summary>
    public static string SoundKey(int message) => message switch
    {
        >= 2 and <= 0xc or 0x10 or 0x13 or 0x1e or 0x2b or 0x2c or 0x39 or 0x3d or 0x3e or 0x40 or 0x41 or 0x49 or 0x4b
            or 0x52 or 0x5b or 0x5c or 0x5d or 0x63 or 0x65 or >= 0x67 and <= 0x6f => NoBuild,
        0xf or 0x11 => OnlyWall,
        0x12 => NoWall,
        >= 0x14 and <= 0x19 or 0x29 or 0x4a => NoTearDown,
        0x1a or 0x20 or 0x3b or 0x3c or 0x58 or 0x5a => NotThisPlace,
        0x28 => DefaultLand,
        0x1b => DefaultLandRotation,
        0x1c or 0x1d or 0x1f or 0x35 or 0x3a or 0x3f or 0x48 => NoChangeCube,
        0x2d => BuildingCount,
        _ => null,
    };

    /// <summary>GetMessageText's common string id (:179-302) for every message but 99, which reads
    /// <see cref="InstallNpcCountKey"/> instead. The table is the client's, gaps (34, 82) and the swapped 72/73 included.</summary>
    public static int TextId(int message) => message switch
    {
        0 => 0x691,
        1 => 0,
        >= 2 and <= 33 => 0x690 + message,
        >= 35 and <= 55 => 0x68f + message,
        56 => 0x6c2,
        >= 57 and <= 59 => 0x691 + message,
        60 => 0x6c7,
        >= 61 and <= 71 => 0x690 + message,
        72 => 0x6d9,
        73 => 0x6d8,
        >= 74 and <= 78 => 0x690 + message,
        79 => 0x6e0,
        80 => 0x6e1,
        81 => 0x6df,
        83 => 0xcaa,
        >= 84 and <= 86 => 0x68f + message,
        >= 87 and <= 89 => 0xc54 + message,
        >= 90 and <= 93 => message + 9,
        >= 94 and <= 97 => 0xc50 + message,
        98 => 0xcb5,
        100 => 0xcb7,
        101 => 0x67,
        102 => 0x6ea,
        >= 103 and <= 111 => message + 1,
        _ => 0x6c2,
    };

    /// <summary>The common string key of the message's text (GetStringCommon(<see cref="TextId"/>) through
    /// CommonStringIds); null for an id the client registers no key for.</summary>
    public static string TextKey(int message) => message == 99 ? InstallNpcCountKey : CommonStringIds.Key(TextId(message));

    /// <summary>What <see cref="Show"/> did.</summary>
    public enum Outcome { Nothing, LackMeratOpen, Shown }

    /// <summary>
    /// ShowBuildNotice (@0x1410d6590, :306-325): message 0x22 opens the lack-merat box; message 1 shows nothing; any other
    /// plays its sound key (PlaySystemSound(key, "")) when it has one, then shows its text through
    /// ShowSystemNotice(text, 1, 1) unless the text is empty.
    /// <para>STILL-OPEN: the lack-merat box (CUIService::ShowLackMeratAsk, s_err_lack_merat_ask) has no port, so 0x22
    /// does nothing (<see cref="Outcome.LackMeratOpen"/>).</para>
    /// </summary>
    public static Outcome Show(int message, Action<string> playSound, Func<string, string> commonByKey, Action<string> showNotice)
    {
        if (message == LackMerat) return Outcome.LackMeratOpen;
        if (message == None) return Outcome.Nothing;
        if (SoundKey(message) is { } sound) playSound?.Invoke(sound);
        string key = TextKey(message);
        string text = key == null ? "" : commonByKey?.Invoke(key) ?? "";
        if (text.Length == 0) return Outcome.Nothing;
        showNotice?.Invoke(text);
        return Outcome.Shown;
    }

    /// <summary>OnResponseCube 0x11 / 0x12 with an error (scenestategame.cpp:10168-10172, :10186-10190): the error is the
    /// build notice when the character id that follows it is the local pc's; 0 = no notice.</summary>
    public static int OfHeldItemResponse(ResponseCubeHeldItem m, int selfObject) =>
        m != null && m.Error != 0 && m.Id == selfObject ? m.Error : 0;
}
