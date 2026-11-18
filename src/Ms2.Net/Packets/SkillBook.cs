// Layout from the restored client's CSceneStateGame::OnJob (scenestategame.cpp:7449; RecvOp::Job 0x49), which calls the job system's
// vftable slot 7: JobSystemCli::vfunc_7 (0x140a6e0a0) reads through JobSystem::vfunc_7 (0x140638f10) and JobSkill::Read (0x140638020).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>One skill of the skill book (JobSkill: id, then JobSkill::Read: level, changed, learned, extra).</summary>
public readonly record struct BookSkill(int SkillId, int Level, bool Changed, bool Learned, bool Extra);

/// <summary>
/// Inbound 0x49 (Job): i32 object id (FindPCById), u8 mode. Modes 0, 1, 2, 7, 8, 9, 10 and 11 all carry the skill book that
/// JobSystem::vfunc_7 reads: i32 rank, i32 job group, u8 count of { i32 skill id, i32 level, bool changed, bool learned,
/// bool extra }. Mode 3 is an error: u8 code, plus an i32 value for code 18. Other modes carry nothing the client reads.
/// </summary>
public sealed class JobPacket {
    public int ObjectId { get; private init; }
    public int Mode { get; private init; }
    /// <summary>The skill book (null for mode 3 and modes without one).</summary>
    public SkillBook Book { get; private init; }
    public int Error { get; private init; }
    public int ErrorValue { get; private init; }

    public static bool CarriesBook(int mode) => mode is 0 or 1 or 2 or 7 or 8 or 9 or 10 or 11;

    public static JobPacket Read(ByteReader r) {
        int obj = r.ReadInt();
        int mode = r.ReadByte();
        if (CarriesBook(mode)) return new JobPacket { ObjectId = obj, Mode = mode, Book = SkillBook.Read(r) };
        if (mode == 3) {
            int err = r.ReadByte();
            return new JobPacket { ObjectId = obj, Mode = mode, Error = err, ErrorValue = err == 18 ? r.ReadInt() : 0 };
        }
        return new JobPacket { ObjectId = obj, Mode = mode };
    }

    /// <summary>Writes a packet body of this layout (tests and offline fixtures).</summary>
    public static byte[] Write(int objectId, int mode, SkillBook book) {
        var w = new ByteWriter();
        w.WriteInt(objectId); w.WriteByte((byte)mode);
        if (CarriesBook(mode)) book.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }
}

/// <summary>JobSystem's state from the packet: the rank (m_nRank), the job group and the skills. The client keeps a skill only
/// when the job's skill table has it (JobSkill::Init); that filter needs the job table and is left to the reader of this book.</summary>
public sealed class SkillBook {
    public int Rank { get; init; }
    public int JobGroup { get; init; }
    public IReadOnlyList<BookSkill> Skills { get; init; } = Array.Empty<BookSkill>();

    public bool TryGet(int skillId, out BookSkill s) {
        foreach (var k in Skills) if (k.SkillId == skillId) { s = k; return true; }
        s = default; return false;
    }

    public static SkillBook Read(ByteReader r) {
        int rank = r.ReadInt(), group = r.ReadInt();
        int n = r.ReadByte();
        var list = new List<BookSkill>(n);
        for (int i = 0; i < n; i++) {
            int id = r.ReadInt(), level = r.ReadInt();
            bool changed = r.ReadBool(), learned = r.ReadBool(), extra = r.ReadBool();
            list.Add(new BookSkill(id, level, changed, learned, extra));
        }
        return new SkillBook { Rank = rank, JobGroup = group, Skills = list };
    }

    public void Write(ByteWriter w) {
        w.WriteInt(Rank); w.WriteInt(JobGroup); w.WriteByte((byte)Skills.Count);
        foreach (var s in Skills) { w.WriteInt(s.SkillId); w.WriteInt(s.Level); w.WriteBool(s.Changed); w.WriteBool(s.Learned); w.WriteBool(s.Extra); }
    }
}
