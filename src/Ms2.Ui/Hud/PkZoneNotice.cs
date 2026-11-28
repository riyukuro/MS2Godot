namespace Ms2.Ui.Hud;

/// <summary>
/// The pk zone enter / leave notice of UISceneGamePlay::vfunc_58 (@0x141aca570, uiscenegame.cpp:1161), run at each fade-in
/// complete. On a shadow continent field (CFieldDataCli::GetShadowContinent != 0) the move from a pk field (m_c8 set) to a
/// field with pkMode 0 shows the safe zone notice, and the move into a pk field the pk zone notice; off the shadow
/// continent (or with no field data) the flag clears with no notice.
/// </summary>
public sealed class PkZoneNotice
{
    /// <summary>s_shadowworld_pkzone_msg / _chat_msg and s_shadowworld_safezone_msg / _chat_msg.</summary>
    public const int PkZoneText = 0x96d, PkZoneChatText = 0x96e, SafeZoneText = 0x96f, SafeZoneChatText = 0x970;
    /// <summary>FUN_141275260(id, 4, 0): a system notice of type 4 with no log.</summary>
    public const int NoticeType = 4;
    /// <summary>FUN_1412751b0(text, 0x14): the chat line kind.</summary>
    public const int ChatKind = 0x14;
    /// <summary>PlaySystemSound(DAT_143af6008, "").</summary>
    public const string Sound = "System_KillSlayer";

    bool inPkZone;                          // m_c8

    /// <summary>m_c8: the last notice was the pk zone's.</summary>
    public bool InPkZone => inPkZone;

    /// <summary>The fade-in step: the (notice, chat line) common string ids to show, or null for none.</summary>
    public (int Notice, int Chat)? OnFadeInComplete(bool hasField, int shadowContinent, int pkMode)
    {
        if (!hasField || shadowContinent == 0)
        {
            inPkZone = false;
            return null;
        }
        if (inPkZone && pkMode == 0)
        {
            inPkZone = false;
            return (SafeZoneText, SafeZoneChatText);
        }
        if (!inPkZone && pkMode != 0)
        {
            inPkZone = true;
            return (PkZoneText, PkZoneChatText);
        }
        return null;
    }
}
