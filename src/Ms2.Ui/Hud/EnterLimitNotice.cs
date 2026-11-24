using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// ShowEnterLimitMessage(room id, reason) (@0x1412b9870, uidungeonroomdialog.cpp:2226): the system notice the dungeon enter answer
/// (FieldEntrance result 2) shows. A room the table lacks gets common string 0x99a whatever the reason; then reason 12 is the level
/// limit (0x99b with the room's limit), 14 to 16 and 18 plain keyed strings; 13 (the limit achievement's name, FUN_14093b970) and 17 (the
/// limit effects' names) need data <see cref="DungeonRoom"/> does not hold yet, so they are not built. Any other reason shows nothing.
/// </summary>
public static class EnterLimitNotice
{
    /// <summary>The notice text; "" when the client shows none for the reason, null when the text cannot be built yet (reasons 13 and 17).</summary>
    /// <param name="room">DungeonRoomTable::GetRoom(room id), null when the table has no such row.</param>
    /// <param name="common">GetStringCommon(id).</param>
    /// <param name="commonByKey">GetStringByKey(key).</param>
    public static string Text(int reason, DungeonRoom room, Func<int, string> common, Func<string, string> commonByKey)
    {
        if (room == null) return common(0x99a);
        switch (reason)
        {
            case 12: return ClientFormat.Format(common(0x99b), room.LimitPlayerLevel);
            case 13: return null;   // STILL-OPEN: m_limitAchieveID is not read from dungeonroom.xml
            case 14: return commonByKey("s_field_limit_disable_vip");
            case 15: return commonByKey("s_field_limit_gearScore");
            case 16: return commonByKey("s_field_limit_clear_dungeon");
            case 17: return null;   // STILL-OPEN: m_limitAdditionalEffects is not read from dungeonroom.xml
            case 18: return commonByKey("s_dungeon_enter_limit_recommend_weapon");
            default: return "";
        }
    }
}
