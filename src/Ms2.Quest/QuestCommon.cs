using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Quest;

/// <summary>
/// One row of table/dungeonroom.xml (DungeonRoom::Load @0x14031d350, dungeonroom.cpp:372-404): the attributes the quest
/// dialog reads.
/// </summary>
/// <param name="Id">dungeonRoomID (m_dungeonRoomID).</param>
/// <param name="GroupType">groupType (m_groupType, dungeonroom.cpp:378), read as the number the file holds.</param>
/// <param name="MaxUserCount">maxUserCount (m_maxUserCount, :404).</param>
/// <param name="FieldIds">fieldIDs (m_fieldIDs, :399), a token list.</param>
public sealed record DungeonRoom(int Id, int GroupType, int MaxUserCount, int[] FieldIds);

/// <summary>DungeonRoomTable::LoadRooms (dungeonroom.cpp:1055-1072): the rows of table/dungeonroom.xml by room id.</summary>
public sealed class DungeonRooms
{
    public const string Archive = "Xml";
    public const string Entry = "table/dungeonroom.xml";

    readonly Dictionary<int, DungeonRoom> rooms = new();

    /// <summary>DungeonRoomTable::GetRoom: null when there is no such room.</summary>
    public DungeonRoom GetRoom(int id) => rooms.TryGetValue(id, out var r) ? r : null;

    /// <param name="available">IsElementAvailable (QuestTable.Available): FUN_14028faa0 skips the rows that fail it.</param>
    public static DungeonRooms Load(PackSet packs, Func<XElement, bool> available) =>
        Parse(XDocument.Parse(Encoding.UTF8.GetString(packs.Read(Archive, Entry)).TrimStart('﻿')), available);

    /// <summary>A later row of the same id replaces an earlier one (m_rooms[id] = room, :1068).</summary>
    public static DungeonRooms Parse(XDocument doc, Func<XElement, bool> available)
    {
        var t = new DungeonRooms();
        foreach (var e in doc.Root?.Elements("dungeonRoom") ?? Enumerable.Empty<XElement>())
        {
            if (!available(e)) continue;
            int id = 0, group = 0, max = 0;
            QuestTable.QueryUInt(e, "dungeonRoomID", ref id);
            QuestTable.QueryInt(e, "groupType", ref group);
            QuestTable.QueryUInt(e, "maxUserCount", ref max);
            var fields = new List<int>();
            foreach (var tok in ((string)e.Attribute("fieldIDs") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int f)) fields.Add(f);
            t.rooms[id] = new DungeonRoom(id, group, max, fields.ToArray());
        }
        return t;
    }
}

/// <summary>
/// What the quest buttons read from the live client (the globals the field-enter packet sets, the world and the local
/// player). STUB for the network lane: every member is supplied by the caller.
/// </summary>
public sealed class QuestButtonInputs
{
    /// <summary>WorldService::GetFieldId (uiquestdialog.cpp:1490).</summary>
    public int FieldId;
    /// <summary>CFieldData::FindField(field) != null (FUN_140744cd0 @0x140744cd0, questdata.cpp:328-335): the field is loaded.</summary>
    public Func<int, bool> FieldExists = _ => false;
    /// <summary>CFeatureManager::IsFeatureEnabled(0xdc) = DirectQuestFindParty (questmanager_1.cpp:231, featuremanager.cpp:569-570).</summary>
    public bool DirectQuestFindParty;
    /// <summary>DAT_143ab2878 (service type), set by OnRequestFieldEnter (scenestategame.cpp:2341).</summary>
    public int ServiceType;
    /// <summary>DAT_143ab287c, set by OnRequestFieldEnter (scenestategame.cpp:2342).</summary>
    public int FieldKind;
    /// <summary>DAT_143ab2880, set by OnRequestFieldEnter (scenestategame.cpp:2343): the instance id the player is in.</summary>
    public int InstanceId;
    /// <summary>DAT_1439f4a7c (read by FUN_1409312f0, unit_14092f420_accountsessionstate.cpp:970): non-zero.</summary>
    public bool Flag1439f4a7c;
    /// <summary>The local player exists and is not dead (FUN_1409312f0: GetLocalPlayer, PCBase::IsDead).</summary>
    public bool LocalPlayerAlive;
    /// <summary>WorldService::GetFieldData() is not null (FUN_1409312f0).</summary>
    public bool HasFieldData;
    /// <summary>FUN_1418fd280 (uicommon.cpp:353): the home convenience of the story quest remote complete (field 62000000).</summary>
    public bool HomeConvenience;
    /// <summary>CQuestManager::FUN_1416d0760 = m_field180 (questmanager_2.cpp:1671, set by MarkField180).</summary>
    public bool DungeonClearMarked;
    /// <summary>
    /// CPartyManager::IsMatchSearching (uiquestdialog.cpp:1511): gets Unk_ClubBuffTypeOf(room groupType) (<see cref="QuestCommon.ClubBuffTypeOf"/>).
    /// </summary>
    public Func<int, bool> IsMatchSearching = _ => false;
}

/// <summary>
/// The quest button and period rules of CQuestCommon / CQuest / CStoryQuest that UIQuestDialog::AddButtons
/// (uiquestdialog.cpp:1483) and QuestProgressStory (:1020) use.
/// </summary>
public static class QuestCommon
{
    /// <summary>The quest types that are CStoryQuest (questmanager_2.cpp:807-815, questkinds.h): epic 0, world 1, event 2, guild 6, field 8, alliance 9.</summary>
    static bool IsStory(int type) => type is 0 or 1 or 2 or 6 or 8 or 9;

    /// <summary>Unk_ClubBuffTypeOf @0x1402178b0 (clubcommon.cpp:1632): 1 and 2 give 1, 8 gives 2, 11 gives 3, 5 gives 4, else 0.</summary>
    public static int ClubBuffTypeOf(int n) => n switch { 1 or 2 => 1, 8 => 2, 11 => 3, 5 => 4, _ => 0 };

    /// <summary>
    /// FUN_14074b090 (questdata.cpp:1549): a quest with a duration, a term tag (eventTag) or a reset weekday has an end.
    /// QuestProgressStory shows the end time line only for these (uiquestdialog.cpp:1032).
    /// </summary>
    public static bool IsTimeLimited(QuestData d) => d.DurationMinutes != 0 || d.Schedule.Length != 0 || d.ResetType != 0;

    /// <summary>
    /// CQuestCommon::GetEndTime (questdata.cpp @0x1407448a0 -> QuestRules.EndTime): the end time, 0 for none; null when the
    /// term tag is not known yet.
    /// </summary>
    public static long? EndTime(QuestData d, QuestRecord r, IQuestContext ctx) =>
        new QuestRules(null, ctx).EndTime(d, r?.StartTime ?? 0);

    /// <summary>CQuestCommon::IsExpired (questdata.cpp:@0x1407447c0 -> QuestRules.IsExpired); null when the end time is not known.</summary>
    public static bool? IsExpired(QuestData d, QuestRecord r, IQuestContext ctx) =>
        new QuestRules(null, ctx).IsExpired(d, r?.StartTime ?? 0);

    /// <summary>CFieldMetaTable::FUN_1406fb520 (fieldmetatable.cpp:472): the fields an npc stands in, minus a timed one that is shut now (FUN_1406fbb50).</summary>
    public static List<int> NpcFields(FieldMeta meta, IQuestState state, int npc)
    {
        var l = new List<int>();
        if (meta.NpcFields.TryGetValue(npc, out var fields))
            foreach (int f in fields)
                if (!meta.TimedNpcFields.Contains((npc, f)) || state.NpcFieldOpen(npc, f)) l.Add(f);
        return l;
    }

    /// <summary>CQuestCommon::FUN_1407441f0 (pvpinfo.cpp:238-261): the "gotoNpc" button.</summary>
    public static bool GotoNpc(QuestData d, QuestRecord r, int field, QuestButtonInputs live, FieldMeta meta, IQuestState state)
    {
        if (!live.FieldExists(field)) return false;                         // FUN_140744cd0, :240
        if (d.GotoNpcEnable == 0) return false;                             // m_300.m_0, :245
        if (!r.Complete) return false;                                      // IsComplete, :247
        int target = d.GotoNpcField;                                        // m_300.m_4, :249
        if (target == 0) return false;
        if (NpcFields(meta, state, d.CompleteNpc).Contains(field)) return false;   // the complete npc (m_2ec) stands here, :253-255
        return target != field;                                             // :257
    }

    /// <summary>
    /// CQuest::FUN_1407443e0 (questmanager_1.cpp:245-253) over CQuestCommon::FUN_1407443e0 (pvpinfo.cpp:263-300): the
    /// "gotoDungeon" button.
    /// </summary>
    public static bool GotoDungeon(QuestData d, QuestRecord r, int field, QuestButtonInputs live, DungeonRooms rooms)
    {
        if (!live.FieldExists(field)) return false;
        int kind = d.GotoDungeonState, roomId = d.GotoDungeonRoom, fieldId = d.GotoDungeonField;
        if (roomId == 0 && fieldId == 0) return false;                      // :272
        if (roomId != 0)
        {
            var room = rooms.GetRoom(roomId);
            if (room == null) return false;                                 // :277
            if (Array.IndexOf(room.FieldIds, field) >= 0) return false;     // already in one of its fields, :279
        }
        bool ok = kind switch
        {
            1 => r.IsActive && !r.Complete,                                 // :283
            2 => r.Complete,                                                // :288
            3 => r.IsActive,                                                // :292
            _ => false,
        };
        if (!ok) return false;
        if (roomId != 0 && !Field930b70(live)) return false;                // CQuest override, questmanager_1.cpp:251
        if (fieldId != 0 && fieldId == live.InstanceId) return false;       // DAT_143ab2880, :252
        return true;
    }

    /// <summary>FUN_140930b70 (unit_14092f420_accountsessionstate.cpp:794): not service type 2 and field kind 0.</summary>
    static bool Field930b70(QuestButtonInputs live) => live.ServiceType != 2 && live.FieldKind == 0;

    /// <summary>
    /// CQuest::FUN_140744670 (questmanager_1.cpp:257-260) over CQuestCommon::FUN_140744670 (questdata.cpp:222-240): the
    /// "gotoReEntry" button.
    /// </summary>
    public static bool GotoReEntry(QuestData d, QuestRecord r, int field, QuestButtonInputs live)
    {
        if (!live.FieldExists(field)) return false;
        if (!d.UseReEntryHiddenMap) return false;                           // m_34, :228
        if (!r.IsActive) return false;
        int first = d.Fields.Length == 0 ? 0 : (int)d.Fields[0];            // GetFirstFieldId (questdata.cpp:1616)
        if (first == 0 || first == field) return false;
        return Field9312f0(live);
    }

    /// <summary>FUN_1409312f0 (unit_14092f420_accountsessionstate.cpp:970-985).</summary>
    static bool Field9312f0(QuestButtonInputs live) =>
        live.Flag1439f4a7c && live.ServiceType == 0 && live.LocalPlayerAlive && live.HasFieldData;     // FUN_140930b10 is DAT_143ab2878 != 0

    /// <summary>
    /// CStoryQuest::FUN_14180fc80 (stringtable.cpp:324-347): the story quest's remote complete is ready. Only for story
    /// quest types.
    /// </summary>
    static bool StoryRemoteReady(QuestData d, QuestRecord r, QuestButtonInputs live)
    {
        if (d.RemoteComplete == 0) return false;                            // m_32c.m_0
        if (!r.IsActive) return false;                                      // GetState() != 1
        if (!r.Complete) return false;
        int map = d.RemoteCompleteField;                                    // m_32c.m_4
        if (map != 0)
        {
            if (live.FieldId != map) return false;
            if (map == 62000000 && !live.HomeConvenience) return false;     // FUN_1418fd280
        }
        if (d.RemoteCompleteDungeonClear != 0 && !live.DungeonClearMarked) return false;   // m_32c.m_8, FUN_1416d0760
        return true;
    }

    /// <summary>
    /// CQuest::FUN_140d738c0 (unit_140d72a60_questmissionclasses.cpp:71 returns 0) overridden by CStoryQuest
    /// (stringtable.cpp:304-311, @0x14180fbe0): story quests with remoteComplete kind 1.
    /// </summary>
    public static bool Dispatch738c0(QuestData d, QuestRecord r, QuestButtonInputs live) =>
        IsStory(d.Type) && StoryRemoteReady(d, r, live) && d.RemoteComplete == 1;

    /// <summary>
    /// CQuest::FUN_140d738d0 (returns 0, :73) overridden by CStoryQuest (stringtable.cpp:314-322): remoteComplete kind 2;
    /// and by CFieldQuest (type 8, :399-406, @0x1418101a0): active and complete.
    /// </summary>
    public static bool Dispatch738d0(QuestData d, QuestRecord r, QuestButtonInputs live)
    {
        if (d.Type == 8) return r.IsActive && r.Complete;
        return IsStory(d.Type) && StoryRemoteReady(d, r, live) && d.RemoteComplete == 2;
    }

    /// <summary>
    /// CQuest::HasMultiUserRoom (questmanager_1.cpp:231-241): feature DirectQuestFindParty, the gotoDungeon button, and a
    /// room with more than one user.
    /// </summary>
    public static bool HasMultiUserRoom(QuestData d, QuestRecord r, int field, QuestButtonInputs live, DungeonRooms rooms)
    {
        if (!live.DirectQuestFindParty) return false;
        if (!GotoDungeon(d, r, field, live, rooms)) return false;
        if (d.GotoDungeonRoom == 0) return false;
        var room = rooms.GetRoom(d.GotoDungeonRoom);
        return room != null && room.MaxUserCount > 1;
    }

    /// <summary>
    /// UIQuestDialog::AddButtons (uiquestdialog.cpp:1483-1520) "gotoButton" array, in the client's order: giveup (no
    /// disableGiveup and active, :1494-1498), gotoNpc, gotoDungeon, gotoReEntry, gotoDispatch (FUN_140d738d0 or
    /// FUN_140d738c0), then partyFind or partyFindCancel for a multi user room. Null when the client sets no array: no
    /// record, or the party branch returns early because the room is missing (:1506-1510); the array is set
    /// (possibly empty) otherwise.
    /// </summary>
    public static List<string> Buttons(QuestData d, QuestRecord r, QuestButtonInputs live, FieldMeta meta, IQuestState state, DungeonRooms rooms)
    {
        if (d == null || r == null) return null;                            // :1485-1491
        int field = live.FieldId;
        var b = new List<string>();
        if (!d.DisableGiveup && r.IsActive) b.Add("giveup");
        if (GotoNpc(d, r, field, live, meta, state)) b.Add("gotoNpc");
        if (GotoDungeon(d, r, field, live, rooms)) b.Add("gotoDungeon");
        if (GotoReEntry(d, r, field, live)) b.Add("gotoReEntry");
        if (Dispatch738d0(d, r, live) || Dispatch738c0(d, r, live)) b.Add("gotoDispatch");
        if (HasMultiUserRoom(d, r, field, live, rooms))
        {
            var room = rooms.GetRoom(d.GotoDungeonRoom);
            if (room == null) return null;
            b.Add(live.IsMatchSearching(ClubBuffTypeOf(room.GroupType)) ? "partyFindCancel" : "partyFind");
        }
        return b;
    }
}
