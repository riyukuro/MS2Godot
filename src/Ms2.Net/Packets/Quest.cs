using System.Linq;
// Layouts from the restored client's CQuestManager::OnQuest (questmanager_2.cpp:942) and CQuest::Init/DecodeStart/DecodeProgress/Complete (questmanager_1.cpp:127, 389-425),
// checked against the server fork's QuestPacket writers (Maple2.Server.Game/Packets/QuestPacket.cs, Quest.WriteTo).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>CQuest state values: 1 started, 2 completed (rewarded); 3 is the untaken default of a fresh CQuest (m_state(3)).</summary>
public enum QuestStateValue { Untaken = 3, Started = 1, Completed = 2 }

/// <summary>One quest as the client keeps it: CQuest::Init(id, state), Set68 (the completion count), start and complete time, tracking flag, per-condition progress.</summary>
public sealed class QuestRecord {
    public int Id; public int State; public int CompletionCount; public long StartTime, CompleteTime; public bool Tracked;
    public long[] Progress = Array.Empty<long>();
}

/// <summary>A decoded Inbound 0x52 packet (one per OnQuest mode). Unread modes are kept as <see cref="Other"/> with the mode number.</summary>
public abstract record QuestMessage {
    /// <summary>Mode 0x16: every quest the character has (id, state, completion count, start, complete, tracking, then a count and progress ints each).
    /// <see cref="Skipped"/>: the ids the client could not create (no quest data), whose progress it then does not read, so the records after
    /// one are misread; <see cref="Overrun"/>: the reads ran past the packet's end (the client's handler stops there).</summary>
    public sealed record Load(IReadOnlyList<QuestRecord> Quests) : QuestMessage {
        public IReadOnlyList<int> Skipped { get; init; } = Array.Empty<int>();
        public bool Overrun { get; init; }
    }
    /// <summary>Mode 0x17: the pinned (loaded) quest ids.</summary>
    public sealed record Pinned(IReadOnlyList<int> Ids) : QuestMessage;
    /// <summary>Mode 0x15: i32 group, i32 refresh count; the client clears all its quest lists (the server sends it as exploration progress before the states).</summary>
    public sealed record Reset(int Group, int RefreshCount) : QuestMessage;
    /// <summary>Mode 2: quest taken. i32 id, i64 start time, u8 tracking, i32 count + progress ints (DecodeStart).</summary>
    public sealed record Start(int Id, long StartTime, bool Tracked, IReadOnlyList<long> Progress) : QuestMessage;
    /// <summary>Mode 3: i32 id, i32 count + progress ints (DecodeProgress; state forced to 1).</summary>
    public sealed record Update(int Id, IReadOnlyList<long> Progress) : QuestMessage;
    /// <summary>Mode 4: i32 id, i32 completion count, i64 complete time (state becomes 2).</summary>
    public sealed record Complete(int Id, int CompletionCount, long CompleteTime) : QuestMessage;
    /// <summary>Mode 5: i32 id, i32 result; result 0 removes the quest.</summary>
    public sealed record Result(int Id, int ResultCode) : QuestMessage;
    /// <summary>Mode 6: i32 id; the quest is removed (abandon).</summary>
    public sealed record Abandon(int Id) : QuestMessage;
    /// <summary>Mode 7: i32 count + ids; each is removed with the expired notice.</summary>
    public sealed record Expired(IReadOnlyList<int> Ids) : QuestMessage;
    /// <summary>Mode 9: i32 id, u8 tracking.</summary>
    public sealed record Tracking(int Id, bool Tracked) : QuestMessage;
    /// <summary>Mode 0: i32 error code (ShowQuestErrorMessage).</summary>
    public sealed record Error(int Code) : QuestMessage;
    /// <summary>Mode 1: i32 npc id, i32 count + quest ids (the quests an npc offers).</summary>
    public sealed record Talk(int NpcId, IReadOnlyList<int> Ids) : QuestMessage;
    /// <summary>Modes 0x1f / 0x20: u8 drop flag, i32 count + ids (daily / weekly reputation counts; drop removes the type-9 quests of state 2 / 3).</summary>
    public sealed record Counts(bool Weekly, bool Drop, IReadOnlyList<int> Ids) : QuestMessage;
    /// <summary>Modes the quest book does not use (map-only or UI-only: 0x12, 0x19, 0x1a, 0x1e, 0x22, 0x23, 0x26).</summary>
    public sealed record Other(int Mode) : QuestMessage;

    static IReadOnlyList<int> IdList(ByteReader r) { int n = r.ReadInt(); var l = new List<int>(n); for (int i = 0; i < n; i++) l.Add(r.ReadInt()); return l; }
    static long[] Prog(ByteReader r) { int n = r.ReadInt(); var a = new long[n]; for (int i = 0; i < n; i++) a[i] = r.ReadInt(); return a; }

    public static QuestMessage Read(ByteReader r) => Read(r, null);

    /// <summary>
    /// <paramref name="hasQuestData"/>: whether the client's quest data has the id (GetQuestData). Mode 0x16 then reads as OnQuest does
    /// (questmanager_2.cpp:959): CreateQuestByType gives no quest for an id without data (FUN_14074bc90 returns type 11), and the
    /// progress count and ints are read only inside the quest's Init branch, so every record after such an id is read from the wrong
    /// offset. Null: every id counts as known.
    /// </summary>
    public static QuestMessage Read(ByteReader r, Func<int, bool> hasQuestData) {
        int mode = r.ReadByte();
        switch (mode) {
            case 0: return new Error(r.ReadInt());
            case 1: { int npc = r.ReadInt(); return new Talk(npc, IdList(r)); }
            case 2: { int id = r.ReadInt(); long t = r.ReadLong(); bool tr = r.ReadByte() != 0; return new Start(id, t, tr, Prog(r)); }
            case 3: { int id = r.ReadInt(); return new Update(id, Prog(r)); }
            case 4: return new Complete(r.ReadInt(), r.ReadInt(), r.ReadLong());
            case 5: return new Result(r.ReadInt(), r.ReadInt());
            case 6: return new Abandon(r.ReadInt());
            case 7: return new Expired(IdList(r));
            case 9: return new Tracking(r.ReadInt(), r.ReadByte() != 0);
            case 0x15: return new Reset(r.ReadInt(), r.ReadInt());
            case 0x16: {
                int n = r.ReadInt(); var l = new List<QuestRecord>(); var skipped = new List<int>(); bool overrun = false;
                try {
                    for (int i = 0; i < n; i++) {
                        var q = new QuestRecord { Id = r.ReadInt(), State = r.ReadInt(), CompletionCount = r.ReadInt(), StartTime = r.ReadLong(), CompleteTime = r.ReadLong(), Tracked = r.ReadByte() != 0 };
                        if (hasQuestData != null && !hasQuestData(q.Id)) { skipped.Add(q.Id); continue; }
                        q.Progress = Prog(r); l.Add(q);
                    }
                } catch (Exception) when (hasQuestData != null) { overrun = true; }   // CInPacket throws past the end; the quests added so far stay
                return new Load(l) { Skipped = skipped, Overrun = overrun };
            }
            case 0x17: return new Pinned(IdList(r));
            case 0x1f: case 0x20: { bool drop = r.ReadByte() != 0; return new Counts(mode == 0x20, drop, IdList(r)); }
            default: return new Other(mode);
        }
    }
}

/// <summary>
/// The quest state a live session builds from the quest packets, the client's CQuestManager lists as far as the quest log needs them.
/// Apply each decoded message in arrival order; <see cref="Changed"/> fires after every message that altered the book.
/// </summary>
public sealed class QuestBook {
    readonly Dictionary<int, QuestRecord> quests = new();
    readonly HashSet<int> pinned = new(), daily = new(), weekly = new();
    public IReadOnlyCollection<QuestRecord> All => quests.Values;
    public IReadOnlyCollection<int> Pinned => pinned;
    public IReadOnlyCollection<int> DailyCounts => daily;
    public IReadOnlyCollection<int> WeeklyCounts => weekly;
    public int Group { get; private set; }
    public int RefreshCount { get; private set; }
    public QuestRecord Get(int id) => quests.TryGetValue(id, out var q) ? q : null;
    /// <summary>Raised after a message changed the book; the argument is the quest id, or 0 when the whole book changed.</summary>
    public event Action<int> Changed;
    /// <summary>The server's error code of the last mode 0 packet, and the quests an npc offered in the last mode 1 packet.</summary>
    public event Action<int> ErrorReceived;
    public event Action<int, IReadOnlyList<int>> NpcOffer;

    /// <summary>Per-quest table data the client reads from the quest XML: basic questType, basic repeatable, require groupID.</summary>
    public readonly record struct QuestInfoRow(int Type, int Repeatable, int GroupId);
    /// <summary>Quest table lookup (null row = quest not in the table). Without it the count packets only replace the count sets.</summary>
    public Func<int, QuestInfoRow?> QuestInfo { get; set; }

    public void Apply(QuestMessage m) {
        switch (m) {
            case QuestMessage.Reset x: quests.Clear(); pinned.Clear(); Group = x.Group; RefreshCount = x.RefreshCount; Changed?.Invoke(0); break;
            case QuestMessage.Load x: foreach (var q in x.Quests) quests[q.Id] = q; Changed?.Invoke(0); break;
            case QuestMessage.Pinned x: foreach (int id in x.Ids) pinned.Add(id); Changed?.Invoke(0); break;
            case QuestMessage.Start x: quests[x.Id] = new QuestRecord { Id = x.Id, State = 1, StartTime = x.StartTime, Tracked = x.Tracked, Progress = x.Progress.ToArray() }; Changed?.Invoke(x.Id); break;
            case QuestMessage.Update x:
                if (quests.TryGetValue(x.Id, out var u)) { u.State = 1; u.Progress = x.Progress.ToArray(); Changed?.Invoke(x.Id); }   // an unknown id returns early in OnQuest
                break;
            case QuestMessage.Complete x:
                if (quests.TryGetValue(x.Id, out var c)) { c.State = 2; c.CompletionCount = x.CompletionCount; c.CompleteTime = x.CompleteTime; Changed?.Invoke(x.Id); }
                break;
            case QuestMessage.Result x: if (x.ResultCode == 0 && quests.Remove(x.Id)) Changed?.Invoke(x.Id); break;
            case QuestMessage.Abandon x: if (quests.Remove(x.Id)) Changed?.Invoke(x.Id); break;
            case QuestMessage.Expired x: foreach (int id in x.Ids) quests.Remove(id); if (x.Ids.Count > 0) Changed?.Invoke(0); break;
            case QuestMessage.Tracking x: if (quests.TryGetValue(x.Id, out var t)) { t.Tracked = x.Tracked; Changed?.Invoke(x.Id); } break;
            case QuestMessage.Counts x: {
                var set = x.Weekly ? weekly : daily; set.Clear(); foreach (int id in x.Ids) set.Add(id);
                // OnQuest modes 0x1f/0x20 with the drop byte set: RemoveQuestsByTypeAndState(9, 2 daily / 3 weekly, skipGrouped 1)
                // (0x1416d38a0) removes every quest whose questType is 9 and repeatable equals the state, unless it has a require groupID.
                if (x.Drop && QuestInfo != null) {
                    int state = x.Weekly ? 3 : 2;
                    foreach (int id in quests.Keys.ToList())
                        if (QuestInfo(id) is { } row && row.Type == 9 && row.Repeatable == state && row.GroupId == 0) quests.Remove(id);
                }
                Changed?.Invoke(0); break; }
            case QuestMessage.Error x: ErrorReceived?.Invoke(x.Code); break;
            case QuestMessage.Talk x: NpcOffer?.Invoke(x.NpcId, x.Ids); break;
        }
    }
}
