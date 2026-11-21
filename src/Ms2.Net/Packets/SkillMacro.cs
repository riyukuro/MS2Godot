using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>One compact skill control tab, CSkillCompactControlConfig (commonfiles/skillconfig.h): the name, the key action
/// id the tab's hot key is bound to (m_30: 0xaae61..0xaae63, 0 for none) and the ticked skills (a std::set, ascending).</summary>
public sealed record SkillMacroTab(string Name, long KeyAction, IReadOnlyList<int> Skills) {
    /// <summary>The tab as UISkillCompactControlDialog::vfunc_79 @0x140f9fee0 and CSkillSystem::FUN_141767ae0 @0x141767ae0
    /// read it: unicode name, i64 key action, i32 count of i32 skills (each inserted into the set).</summary>
    public static SkillMacroTab Read(ByteReader r) {
        string name = r.ReadUnicodeString();
        long key = r.ReadLong();
        uint n = (uint)r.ReadInt();
        var set = new SortedSet<int>(Comparer<int>.Create((a, b) => ((uint)a).CompareTo((uint)b)));
        for (uint i = 0; i < n; i++) set.Add(r.ReadInt());
        return new SkillMacroTab(name, key, set.ToArray());
    }

    public void Write(ByteWriter w) {
        w.WriteUnicodeString(Name);
        w.WriteLong(KeyAction);
        w.WriteInt(Skills.Count);
        foreach (int s in Skills) w.WriteInt(s);
    }
}

/// <summary>
/// Inbound 0xD1 (RecvOp::SkillMacro). A u8 mode first. Mode 0 is the answer to the compact skill control window's request
/// (UISkillCompactControlDialog::vfunc_79 @0x140f9fee0): i32 count of <see cref="SkillMacroTab"/>. Mode 2 is the same list for
/// the skill system (CSceneStateGame::OnSkillMacro @0x141737d60 -> CSkillSystem::FUN_141767ae0 @0x141767ae0,
/// skillslot.cpp:2530), which keeps it as m_lstCompact. Neither reader takes another mode, so any other body is kept unread.
/// </summary>
public sealed class SkillMacroPacket {
    public byte Mode { get; init; }
    public IReadOnlyList<SkillMacroTab> Tabs { get; init; } = Array.Empty<SkillMacroTab>();

    public static SkillMacroPacket Read(ByteReader r) {
        byte mode = r.ReadByte();
        if (mode != 0 && mode != 2) return new SkillMacroPacket { Mode = mode };
        uint n = (uint)r.ReadInt();
        var tabs = new List<SkillMacroTab>();
        for (uint i = 0; i < n; i++) tabs.Add(SkillMacroTab.Read(r));
        r.ExpectEnd("SkillMacro");
        return new SkillMacroPacket { Mode = mode, Tabs = tabs };
    }

    /// <summary>A mode 0 or mode 2 body (tests and the offline fixture).</summary>
    public static byte[] Write(byte mode, IReadOnlyList<SkillMacroTab> tabs) {
        var w = new ByteWriter();
        w.WriteByte(mode);
        w.WriteInt(tabs.Count);
        foreach (var t in tabs) t.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }
}

/// <summary>Outbound 0x7B (SendOp::SkillMacro) mode 0: UISkillCompactControlDialog::OnLoadComplete @0x140fa06a0 asks for the
/// saved tabs once the movie loads (Encode1(0) only). A read request; the server answers with 0xD1 mode 0.</summary>
public sealed record SkillMacroRequest : IOutPacket {
    public Outbound Op => Outbound.SkillMacro;
    public void Write(ByteWriter w) => w.WriteByte(0);
}

/// <summary>Outbound 0x7B mode 1: SendSkillMacroMode1 @0x140fa1240, sent by the window's close (vfunc_67 @0x140f9e3c0) with
/// every tab: i32 count of <see cref="SkillMacroTab"/>, the skills in the set's ascending order.</summary>
public sealed record SkillMacroSave(IReadOnlyList<SkillMacroTab> Tabs) : IOutPacket {
    public Outbound Op => Outbound.SkillMacro;
    public void Write(ByteWriter w) {
        w.WriteByte(1);
        w.WriteInt(Tabs.Count);
        foreach (var t in Tabs) t.Write(w);
    }
}
