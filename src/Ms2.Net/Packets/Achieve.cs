using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// One trophy's progress as CAchieve::Decode (achievecondition.cpp:166) reads it: u8 state, i32 m_80 (GetValue80, read by
/// UIAchieveDialog::FUN_1418aca90; its meaning is STILL-OPEN), i32 current grade, i32 start grade, u8 bookmark, i64 the
/// condition count (m_cond.m_nCount; IsStarted = count != 0), then i32 n × (i32 grade, i64 value) into m_mapValue.
/// </summary>
public sealed record AchieveProgress(byte State, int Value80, int CurGrade, int StartGrade, bool Bookmark, long Count,
                                     IReadOnlyDictionary<int, long> Values) {
    /// <summary>GetValue (@0x140159040): the grade's value, 0 when absent.</summary>
    public long GetValue(int grade) => Values.TryGetValue(grade, out long v) ? v : 0;

    public static AchieveProgress Read(ByteReader r) {
        byte state = r.ReadByte(); int v80 = r.ReadInt(); int cur = r.ReadInt(); int start = r.ReadInt();
        bool bookmark = r.ReadByte() != 0; long count = r.ReadLong();
        int n = r.ReadInt();
        var values = new Dictionary<int, long>();
        for (int i = 0; i < n; i++) { int key = r.ReadInt(); values[key] = r.ReadLong(); }   // m_mapValue.SetAt
        return new AchieveProgress(state, v80, cur, start, bookmark, count, values);
    }
}

/// <summary>
/// Achieve (0x5E; networkservice.cpp:679 hands it to CAchieveManager::OnPacket, achievemanager.cpp:499): mode 0 resets the
/// trophy book (Reload), mode 1 the trophies the player has (i32 count, then per row i32 id, i32 grade and the progress),
/// mode 2 one trophy's new progress (i32 id and the progress). Other modes are read as null.
/// </summary>
public abstract record AchieveMessage {
    public sealed record Reload : AchieveMessage;
    public sealed record Rows(IReadOnlyList<(int Id, int Grade, AchieveProgress Progress)> List) : AchieveMessage;
    public sealed record Update(int Id, AchieveProgress Progress) : AchieveMessage;

    public static AchieveMessage Read(ByteReader r) {
        switch (r.ReadByte()) {
            case 0: return new Reload();
            case 1: {
                uint n = (uint) r.ReadInt();
                var list = new List<(int, int, AchieveProgress)>();
                for (uint i = 0; i < n; i++) {
                    int id = r.ReadInt(); int grade = r.ReadInt();
                    list.Add((id, grade, AchieveProgress.Read(r)));        // decoded for every row, kept or not
                }
                return new Rows(list);
            }
            case 2: {
                int id = r.ReadInt();
                return new Update(id, AchieveProgress.Read(r));            // the client decodes only a trophy it has
            }
        }
        return null;
    }
}
