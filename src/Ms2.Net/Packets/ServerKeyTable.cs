// Layout from the restored client's CKeyTable::OnKeyTable (keytable.cpp:285, 0x141428bb0; RecvOp::KeyTable 0x6F) and the quick
// slot entry Unk_SsQuickEntry (ssquickentry.h: skill id, item uid, item id).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>One key binding of the KeyTable packet: the args of CKeyTable::FUN_141431c80(key, type, code, slot, priority).</summary>
public readonly record struct KeyBinding(int Key, int Type, long Code, int Slot, int Priority);

/// <summary>One quick slot (Unk_SsQuickEntry): a skill, or an item id + uid; all zero = empty.</summary>
public readonly record struct QuickSlotEntry(int SkillId, int ItemId, long ItemUid) {
    public bool IsEmpty => SkillId == 0 && ItemId == 0 && ItemUid == 0;
}

/// <summary>
/// The key bindings and quick slot pages the server sends in Inbound 0x6F, kept the way CKeyTable keeps them.
/// Mode 0 (the load): u8 useDefault; useDefault 1 = the client loads table/default/DefaultKey.xml and reads nothing more. Else
/// i32 count of { i32 key, i32 type, i64 code, i32 slot, u8 priority } (count 0 = DefaultKey.xml too), then the pages.
/// Mode 7: only the pages. Pages: u16 current page, u16 page count, per page i32 count of { i32 slot, i32 skillId, i32 itemId,
/// i64 itemUid } (FUN_1414323f0 stores each at its slot). Other modes are ignored like the client does.
/// </summary>
public sealed class ServerKeyTable {
    readonly List<Dictionary<int, QuickSlotEntry>> pages = new();

    /// <summary>True once a mode 0 packet was taken (m_nLoaded).</summary>
    public bool Loaded { get; private set; }
    /// <summary>True when the bindings are the client's DefaultKey.xml (useDefault 1, or a binding count of 0).</summary>
    public bool UsesDefaultKeys { get; private set; }
    /// <summary>The bindings of the last mode 0 packet (empty when <see cref="UsesDefaultKeys"/>).</summary>
    public IReadOnlyList<KeyBinding> Bindings { get; private set; } = Array.Empty<KeyBinding>();
    /// <summary>m_nQuickSlotPage (SetQuickSlotPage).</summary>
    public int CurrentPage { get; private set; }
    public int PageCount => pages.Count;

    /// <summary>The entry at <paramref name="slot"/> of <paramref name="page"/>; empty when the page or slot holds none.</summary>
    public QuickSlotEntry Entry(int page, int slot) =>
        page >= 0 && page < pages.Count && pages[page].TryGetValue(slot, out var e) ? e : default;

    /// <summary>CKeyTable::FindQuickSlotBySkillAndItemUid @0x14142e090 (client/ms2/keytable.cpp): the slot of the current page that
    /// holds the skill and item uid, -1 when none does, 0 when the page does not exist. The client walks a CAtlMap; with the
    /// same entry on two slots the one returned may differ.</summary>
    public int FindQuickSlotBySkillAndItemUid(int skillId, long itemUid) {
        if (CurrentPage < 0 || CurrentPage >= pages.Count) return 0;
        foreach (var (slot, e) in pages[CurrentPage].OrderBy(kv => kv.Key))
            if (e.SkillId == skillId && e.ItemUid == itemUid) return slot;
        return -1;
    }

    /// <summary>OnKeyTable: applies one packet; returns the mode.</summary>
    public int Apply(ByteReader r) {
        int mode = r.ReadByte();
        if (mode == 0) {
            int useDefault = r.ReadByte();
            if (useDefault == 1) {
                // the client clears the bindings, loads DefaultKey.xml and leaves the pages as they are
                UsesDefaultKeys = true; Bindings = Array.Empty<KeyBinding>();
            } else {
                int count = r.ReadInt();
                var list = new List<KeyBinding>(Math.Max(0, Math.Min(count, 1024)));
                for (int i = 0; i < count; i++) {
                    int key = r.ReadInt(), type = r.ReadInt();
                    long code = r.ReadLong();
                    int slot = r.ReadInt(), priority = r.ReadByte();
                    list.Add(new KeyBinding(key, type, code, slot, priority));
                }
                UsesDefaultKeys = count == 0; Bindings = list;
                ReadPages(r);
            }
            Loaded = true;
        } else if (mode == 7) {
            ReadPages(r);
        }
        return mode;
    }

    void ReadPages(ByteReader r) {
        int page = r.ReadUShort();
        int count = r.ReadUShort();
        pages.Clear();
        for (int i = 0; i < count; i++) {
            var p = new Dictionary<int, QuickSlotEntry>();
            pages.Add(p);
            int n = r.ReadInt();
            for (int j = 0; j < n; j++) {
                int slot = r.ReadInt(), skill = r.ReadInt(), item = r.ReadInt();
                long uid = r.ReadLong();
                p[slot] = new QuickSlotEntry(skill, item, uid);
            }
        }
        // SetQuickSlotPage(page, 0) asserts and keeps the old page when it is out of range
        if (page >= 0 && page < pages.Count) CurrentPage = page;
    }

    /// <summary>Writes a packet body of this layout (tests and offline fixtures).</summary>
    public static byte[] Write(int mode, bool useDefault, IReadOnlyList<KeyBinding> bindings, int currentPage,
                               IReadOnlyList<IReadOnlyList<(int Slot, QuickSlotEntry Entry)>> pageList) {
        var w = new ByteWriter();
        w.WriteByte((byte)mode);
        if (mode == 0) {
            w.WriteByte((byte)(useDefault ? 1 : 0));
            if (useDefault) return w.Buffer.AsSpan(0, w.Length).ToArray();
            w.WriteInt(bindings.Count);
            foreach (var b in bindings) { w.WriteInt(b.Key); w.WriteInt(b.Type); w.WriteLong(b.Code); w.WriteInt(b.Slot); w.WriteByte((byte)b.Priority); }
        }
        w.WriteShort((short)currentPage);
        w.WriteShort((short)pageList.Count);
        foreach (var p in pageList) {
            w.WriteInt(p.Count);
            foreach (var (slot, e) in p) { w.WriteInt(slot); w.WriteInt(e.SkillId); w.WriteInt(e.ItemId); w.WriteLong(e.ItemUid); }
        }
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }
}
