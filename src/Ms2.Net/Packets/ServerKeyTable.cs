// Layout from the restored client's CKeyTable::OnKeyTable (keytable.cpp:285, 0x141428bb0; RecvOp::KeyTable 0x6F),
// CKeyTable::SendKeyTableMode1/SendKeysToServer/SendKeyTableMode7 (keytable.cpp:378,399,2021; SendOp::KeyTable 0x3A),
// FUN_14092f690/FUN_14092fac0/SendKeyTableMode5/6/8 (unit_14092f420_accountsessionstate.cpp:405-521), and the quick
// slot entry Unk_SsQuickEntry (ssquickentry.h: skill id, item uid, item id).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

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
    /// <summary>m_nLocked (CKeyTable::GetLocked): when true, mode 7 recv and outbound key sends are ignored (keytable.cpp:355,380,405).</summary>
    public bool Locked { get; set; }
    /// <summary>True when the bindings are the client's DefaultKey.xml (useDefault 1, or a binding count of 0).</summary>
    public bool UsesDefaultKeys { get; private set; }
    /// <summary>The bindings of the last mode 0 packet (empty when <see cref="UsesDefaultKeys"/>).</summary>
    public IReadOnlyList<KeyBinding> Bindings { get; private set; } = Array.Empty<KeyBinding>();
    /// <summary>The last packet read the pages and SetQuickSlotPage took its page, which posts NotifyShortkeyPage (mode 0 with
    /// bindings or a count of 0, mode 7). A mode 0 with useDefault 1 (keytable.cpp:293-303) reads no pages, and SetQuickSlotPage
    /// (keytable.cpp:2547) asserts and returns before the notify for a page out of range, so neither notifies.</summary>
    public bool PageNotified { get; private set; }
    /// <summary>m_nQuickSlotPage (SetQuickSlotPage).</summary>
    public int CurrentPage { get; private set; }
    public int PageCount => pages.Count;

    /// <summary>The entry at <paramref name="slot"/> of <paramref name="page"/>; empty when the page or slot holds none.</summary>
    public QuickSlotEntry Entry(int page, int slot) =>
        page >= 0 && page < pages.Count && pages[page].TryGetValue(slot, out var e) ? e : default;

    /// <summary>CKeyTable::SetQuickSlotPage (@0x141431ae0, keytable.cpp:2547-2559): switches the current page when in range.</summary>
    public bool SetCurrentPage(int page) {
        if (page < 0 || page >= pages.Count) return false;
        CurrentPage = page;
        return true;
    }

    /// <summary>
    /// CKeyTable::SetQuickSlotEntryOnPage (keytable.cpp:1629, a SetAt on the page): the skill set's quick slot edits
    /// (skillslot.cpp:470-545, :590-604) and FUN_14142f470's job item put-back write the page; outside a skill set the shown slots
    /// keep their own copy. A page that does not exist takes none; an all-zero entry empties the slot.
    /// </summary>
    public void SetEntry(int page, int slot, QuickSlotEntry e) {
        if (page < 0 || page >= pages.Count) return;
        if (e.IsEmpty) pages[page].Remove(slot); else pages[page][slot] = e;
    }

    /// <summary>CKeyTable::FindQuickSlotBySkillAndItemUid @0x14142e090 (client/ms2/keytable.cpp): the slot of the current page that
    /// holds the skill and item uid, -1 when none does, 0 when the page does not exist. The client walks a CAtlMap; with the
    /// same entry on two slots the one returned may differ.</summary>
    public int FindQuickSlotBySkillAndItemUid(int skillId, long itemUid) {
        if (CurrentPage < 0 || CurrentPage >= pages.Count) return 0;
        int found = -1;     // the lowest matching slot (no sort, no allocation)
        foreach (var (slot, e) in pages[CurrentPage])
            if (e.SkillId == skillId && e.ItemUid == itemUid && (found < 0 || slot < found)) found = slot;
        return found;
    }

    /// <summary>CKeyTable::FindQuickSlotBySkillAndItem @0x14142e150 (client/ms2/keytable.cpp:1600): the slot of the current page
    /// that holds the skill, item id and item uid, -1 when none does, 0 when the page does not exist.</summary>
    public int FindQuickSlotBySkillAndItem(int skillId, int itemId, long itemUid) {
        if (CurrentPage < 0 || CurrentPage >= pages.Count) return 0;
        int found = -1;     // the lowest matching slot (the client walks a CAtlMap; no sort, no allocation)
        foreach (var (slot, e) in pages[CurrentPage])
            if (e.SkillId == skillId && e.ItemId == itemId && e.ItemUid == itemUid && (found < 0 || slot < found)) found = slot;
        return found;
    }

    /// <summary>OnKeyTable: applies one packet; returns the mode.</summary>
    public int Apply(ByteReader r) {
        int mode = r.ReadByte();
        PageNotified = false;
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
            if (Locked) return mode;   // keytable.cpp:355-356: GetLocked() skips mode 7
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
        // SetQuickSlotPage(page, 0) asserts and keeps the old page, with no notify, when it is out of range
        if (page >= 0 && page < pages.Count) { CurrentPage = page; PageNotified = true; }
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

/// <summary>Outbound KeyTable 0x3A mode 1 (CKeyTable::SendKeyTableMode1 @0x1414294e0, keytable.cpp:378-395): one changed key binding.</summary>
public sealed record KeyTableBindRequest(KeyBinding Binding) : IOutPacket {
    public Outbound Op => Outbound.KeyTable;
    public void Write(ByteWriter w) {
        w.WriteByte(1);
        w.WriteInt(1);
        w.WriteInt(Binding.Key);
        w.WriteInt(Binding.Type);
        w.WriteLong(Binding.Code);
        w.WriteInt(Binding.Slot);
        w.WriteByte((byte)Binding.Priority);
    }
}

/// <summary>Outbound KeyTable 0x3A mode 2 (CKeyTable::SendKeysToServer @0x141429600, keytable.cpp:399-425): full key binding table.</summary>
public sealed record KeyTableSaveAllRequest(IReadOnlyList<KeyBinding> Bindings) : IOutPacket {
    public Outbound Op => Outbound.KeyTable;
    public void Write(ByteWriter w) {
        w.WriteByte(2);
        w.WriteInt(Bindings.Count);
        for (int i = 0; i < Bindings.Count; i++) {
            var b = Bindings[i];
            w.WriteInt(b.Key);
            w.WriteInt(b.Type);
            w.WriteLong(b.Code);
            w.WriteInt(b.Slot);
            w.WriteByte((byte)b.Priority);
        }
    }
}

/// <summary>Outbound KeyTable 0x3A mode 4 (FUN_14092fac0 @0x14092fac0, unit_14092f420_accountsessionstate.cpp:418,468-475): append a quick slot entry to a page (slot -1).</summary>
public sealed record KeyTableAddQuickSlotRequest(int Page, QuickSlotEntry Entry) : IOutPacket {
    public Outbound Op => Outbound.KeyTable;
    public void Write(ByteWriter w) {
        w.WriteByte(4);
        w.WriteShort((short)Page);
        w.WriteInt(Entry.SkillId);
        w.WriteInt(Entry.ItemId);
        w.WriteLong(Entry.ItemUid);
        w.WriteInt(-1);
    }
}

/// <summary>Outbound KeyTable 0x3A mode 6 (SendKeyTableMode6 @0x140930040, unit_14092f420_accountsessionstate.cpp:505-511): add a quick slot page.</summary>
public sealed record KeyTableAddPageRequest : IOutPacket {
    public Outbound Op => Outbound.KeyTable;
    public void Write(ByteWriter w) => w.WriteByte(6);
}

/// <summary>Outbound KeyTable 0x3A mode 7 (CKeyTable::SendKeyTableMode7 @0x14142f700, keytable.cpp:2021-2034): exit locked skill-set mode and request quick slots.</summary>
public sealed record KeyTableUnlockRequest : IOutPacket {
    public Outbound Op => Outbound.KeyTable;
    public void Write(ByteWriter w) => w.WriteByte(7);
}

/// <summary>Outbound KeyTable 0x3A mode 8 (SendKeyTableMode8 @0x1409300a0, unit_14092f420_accountsessionstate.cpp:514-521; CKeyTable::SetQuickSlotPage keytable.cpp:2558): select active quick slot page.</summary>
public sealed record KeyTableSetPageRequest(int Page) : IOutPacket {
    public Outbound Op => Outbound.KeyTable;
    public void Write(ByteWriter w) {
        w.WriteByte(8);
        w.WriteShort((short)Page);
    }
}
