using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound 0x44 (SkillPoint), CSceneStateGame::OnSkillPoint @0x14172f210 (scenestategame.cpp:4241): i32 (DAT_143ab2954), i32
/// source count of { i32 source, i32 count of { i16 rank (read unsigned), i32 points } }, then i32 (MyPC SetScore, m_n179c).
/// GMS2 (all 1862 recorded packets; upstream SkillPoint.WriteTo): the same, without the trailing i32, which is 0 in all 614 KMS recordings
/// (build 2520 and up); Extra stays 0. STILL-OPEN: the GMS2 client's OnSkillPoint was not read.
/// </summary>
public sealed class SkillPointPacket {
    public int First { get; init; }
    /// <summary>Per source, its (rank, points) entries in packet order.</summary>
    public IReadOnlyList<(int Source, IReadOnlyList<(int Rank, int Points)> Entries)> Sources { get; init; } =
        Array.Empty<(int, IReadOnlyList<(int, int)>)>();
    /// <summary>The last i32: the points every rank's total adds (MyPC::GetRemainingSkillPoints, mypc.cpp:3965).</summary>
    public int Extra { get; init; }

    public static SkillPointPacket Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static SkillPointPacket Read(ByteReader r, ProtocolProfile profile) {
        int first = r.ReadInt();
        uint groups = (uint)r.ReadInt();
        var list = new List<(int, IReadOnlyList<(int, int)>)>();
        for (uint i = 0; i < groups; i++) {
            int source = r.ReadInt();
            uint n = (uint)r.ReadInt();
            var e = new List<(int, int)>();
            for (uint j = 0; j < n; j++) {
                int rank = (ushort)r.ReadShort();
                e.Add((rank, r.ReadInt()));
            }
            list.Add((source, e));
        }
        return new SkillPointPacket { First = first, Sources = list, Extra = profile.IsGms2 ? 0 : r.ReadInt() };
    }

    public static byte[] Write(int first, IEnumerable<(int Source, IEnumerable<(int Rank, int Points)> Entries)> sources, int extra) {
        var w = new ByteWriter();
        w.WriteInt(first);
        var s = sources.ToList();
        w.WriteInt(s.Count);
        foreach (var (src, entries) in s) {
            var e = entries.ToList();
            w.WriteInt(src); w.WriteInt(e.Count);
            foreach (var (rank, pts) in e) { w.WriteShort((short)rank); w.WriteInt(pts); }
        }
        w.WriteInt(extra);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }
}

/// <summary>One saved skill tab (CSkillBookTreeTabConfig::Read @0x14026a860, unit_14026a1b0_commonstructpriceconfigs.cpp:150):
/// i64 id, unicode name (Left(10)), i32 count of { i32 skill, i32 points }.</summary>
public sealed record SkillTreeTab(long Id, string Name, IReadOnlyList<(int Skill, int Points)> Slots) {
    public static SkillTreeTab Read(ByteReader r) {
        long id = r.ReadLong();
        string name = r.ReadUnicodeString();
        if (name.Length > 10) name = name[..10];
        uint n = (uint)r.ReadInt();
        var slots = new List<(int, int)>();
        for (uint i = 0; i < n; i++) slots.Add((r.ReadInt(), r.ReadInt()));
        return new SkillTreeTab(id, name, slots);
    }

    public void Write(ByteWriter w) {
        w.WriteLong(Id); w.WriteUnicodeString(Name); w.WriteInt(Slots.Count);
        foreach (var (s, p) in Slots) { w.WriteInt(s); w.WriteInt(p); }
    }
}

/// <summary>
/// Inbound 0x10C (SkillBookTree), UISkillBookTreeDialog::vfunc_79 @0x140ec7890 (uiskillbookautodistributedialog.cpp:1962): u8 mode.
/// Mode 0 (the tabs): i32 max tabs, i64 selected tab id, i32 count of tabs. Mode 4 (paid tab): i32 max tabs. Mode 1 (saved):
/// i64 selected tab, i64 tab, i32 rank. Mode 2 (renamed): i64 tab, unicode name, u8 result.
/// </summary>
public sealed class SkillBookTreePacket {
    public int Mode { get; init; }
    public int MaxTabs { get; init; }
    public long TabId { get; init; }
    public long SavedTabId { get; init; }
    public int Rank { get; init; }
    public string Name { get; init; } = "";
    public int Result { get; init; }
    public IReadOnlyList<SkillTreeTab> Tabs { get; init; } = Array.Empty<SkillTreeTab>();

    public static SkillBookTreePacket Read(ByteReader r) {
        int mode = r.ReadByte();
        switch (mode) {
            case 0: {
                int max = r.ReadInt();
                long tab = r.ReadLong();
                uint n = (uint)r.ReadInt();
                var tabs = new List<SkillTreeTab>();
                for (uint i = 0; i < n; i++) tabs.Add(SkillTreeTab.Read(r));
                return new SkillBookTreePacket { Mode = 0, MaxTabs = max, TabId = tab, Tabs = tabs };
            }
            case 4: return new SkillBookTreePacket { Mode = 4, MaxTabs = r.ReadInt() };
            case 1: return new SkillBookTreePacket { Mode = 1, TabId = r.ReadLong(), SavedTabId = r.ReadLong(), Rank = r.ReadInt() };
            case 2: return new SkillBookTreePacket { Mode = 2, TabId = r.ReadLong(), Name = r.ReadUnicodeString(), Result = r.ReadByte() };
            default: return new SkillBookTreePacket { Mode = mode };
        }
    }

    /// <summary>A mode 0 body (tests and the offline fixture).</summary>
    public static byte[] WriteTabs(int maxTabs, long tabId, IReadOnlyList<SkillTreeTab> tabs) {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteInt(maxTabs); w.WriteLong(tabId); w.WriteInt(tabs.Count);
        foreach (var t in tabs) t.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }
}

/// <summary>Outbound 0xA2 (SendOp::RequestSkillBookTree) mode 0: UISkillBookTreeDialog::OnLoadComplete @0x140ec5570
/// (uiskillbookautodistributedialog.cpp:1954) asks for the saved tabs once the movie loads. A read request; the server answers
/// with 0x10C mode 0.</summary>
public sealed record SkillBookTreeRequest : IOutPacket {
    public Outbound Op => Outbound.RequestSkillBookTree;
    public void Write(ByteWriter w) => w.WriteByte(0);
}

/// <summary>Outbound 0xA2 (SendOp::RequestSkillBookTree) mode 1: UISkillBookTreeDialog::RequestTree @0x140eca8c0
/// (uiskillbookautodistributedialog.cpp:1122) saves the tabs: u8 1, i64 the tab in use (m_nTabId), i64 the tab applied, i32 rank,
/// i32 count of tabs, each CSkillBookTreeTabConfig::Write.</summary>
public sealed record SkillBookTreeSave(long CurrentTab, long Tab, int Rank, IReadOnlyList<SkillTreeTab> Tabs) : IOutPacket {
    public Outbound Op => Outbound.RequestSkillBookTree;
    public void Write(ByteWriter w) {
        w.WriteByte(1);
        w.WriteLong(CurrentTab); w.WriteLong(Tab); w.WriteInt(Rank); w.WriteInt(Tabs.Count);
        foreach (var t in Tabs) t.Write(w);
    }
}

/// <summary>Outbound 0x25 (SendOp::Job) mode 9 (apply) or 0xb (apply, confirmed): UISkillBookTreeDialog::RequestJob @0x140ec9200
/// (uiskillbookautodistributedialog.cpp:1180): u8 mode, i32 count of { i32 skill id, i16 level, bool learned }.
/// STILL-OPEN: the GMS2 number of SendOp::Job is not mapped.</summary>
public sealed record JobSkillsRequest(int Mode, IReadOnlyList<(int SkillId, int Level, bool Learned)> Skills) : IOutPacket {
    public Outbound Op => Outbound.Job;
    public void Write(ByteWriter w) {
        w.WriteByte((byte)Mode); w.WriteInt(Skills.Count);
        foreach (var (id, level, learned) in Skills) { w.WriteInt(id); w.WriteShort((short)level); w.WriteBool(learned); }
    }
}
