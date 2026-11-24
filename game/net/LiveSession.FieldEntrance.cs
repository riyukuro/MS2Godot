using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// The dungeon enter answer (0x19, CSceneStateGame::OnFieldEntrance @0x14170fc70, scenestategame.cpp:2366): result 0 stores the
/// dungeon enter states (CMyCharacter::OnDungeonEnterStatePacket, notifydebugstate.cpp:244) and tells the guide
/// (NotifyDungeonEnterState, CGuideManager::OnDungeonEnterState); result 1 and 2 are system notices.
/// <para>STILL-OPEN: the quest rules' EnterDungeon check (QuestRules, questmanager_2.cpp:527 reads the same table) still sees the
/// state as unknown, because <see cref="LiveQuestState"/> is made without the session's table; the party's dungeon-enter check
/// (partymanager.cpp:564, state at or above 10) has no caller here.</para>
/// </summary>
public partial class LiveSession
{
    readonly Dictionary<int, byte> dungeonEnterStates = new();
    bool dungeonEnterStatesSeen;

    /// <summary>CMyCharacter::GetDungeonEnterState (@0x140d9f880): the stored state, 0 for an id no packet named.</summary>
    public int DungeonEnterStateOf(int dungeonId) => dungeonEnterStates.TryGetValue(dungeonId, out var s) ? s : 0;

    /// <summary>True once an enter state packet arrived; before it the table is not known (a server that never sends it).</summary>
    public bool DungeonEnterStatesKnown => dungeonEnterStatesSeen;

    void OnFieldEntrance(FieldEntrance m)
    {
        switch (m.Result)
        {
            case 0:
                foreach (var e in m.Entries) dungeonEnterStates[e.DungeonId] = e.State;   // m_states[key] = state: ids a packet does not name keep theirs
                dungeonEnterStatesSeen = true;
                guide?.OnDungeonEnterState();
                break;
            case 1:
                SystemNoticeSink?.Invoke(MessageBoxData.Common(0x99d));
                break;
            case 2:
            {
                if (DungeonRoomOf == null) { ReadNotApplied("FieldEntrance", 2); break; }
                var text = EnterLimitNotice.Text(m.Type, DungeonRoomOf(m.Id), MessageBoxData.Common, k => MessageBoxData.CommonByKey(k) ?? "");
                if (text == null) ReadNotApplied("FieldEntrance", 2);
                else if (text.Length != 0) SystemNoticeSink?.Invoke(text);
                break;
            }
        }
    }
}
