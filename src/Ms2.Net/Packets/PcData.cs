using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// SendOp::CharacterInfo (0x1E): another player's character window, UIPopUpMenu::SendCharacterInfo (uipartydialog.cpp:3135,
/// the popup's View Character): Encode8 of the character id (m_a8). Nothing else follows.
/// </summary>
public sealed record CharacterInfoRequest(long CharacterId) : IOutPacket {
    public Outbound Op => Outbound.CharacterInfo;
    public void Write(ByteWriter w) => w.WriteLong(CharacterId);
}

/// <summary>
/// RecvOp CharacterInfo (0x78), the answer UIPCInfoDialog::vfunc_79 (uipcinfodialog.cpp:505) reads: i64 the character id
/// (dropped by the dialog unless it is its m_188, the last request), u8 the result; with result 1 a CPCDataPack
/// (<see cref="PcDataPack"/>) whose record is <see cref="Info"/> (FUN_14132c330, :289). Any other result carries nothing more
/// the client reads; <see cref="Info"/> is null then.
/// </summary>
public sealed record CharacterInfoAnswer(long CharacterId, byte Result, PcInfo Info) {
    public static CharacterInfoAnswer Read(ByteReader r) {
        long id = r.ReadLong();
        byte result = r.ReadByte();
        return new CharacterInfoAnswer(id, result, result == 1 ? PcDataPack.Read(r).ToPcInfo() : null);
    }
}

/// <summary>
/// CPCDataPack::Read (commonfiles/pcdata.cpp:181): i64 m_n18, i64 m_n20, i64 m_n28, then three buffers, each an i32 size and
/// its bytes: the record (<see cref="PcData"/>, ReadRecord), the items (equips, the title layer's period, the layer-2
/// outfits) and the extra items (gems), both read by ReadItems (:259). The three longs are not read by the window.
/// </summary>
public sealed class PcDataPack {
    public long Value18, Value20, Value28;
    public PcData Record;
    /// <summary>ReadItems' first list (aEquip): u8 count, then per item i32 id, i64 uid, u8 equip slot (FUN_140526c10;
    /// WriteItems writes FUN_140526c30 there), i32 rarity, CItem::ReadFromStream. An id the item data does not know
    /// (CItem::FUN_140523c40 fails) ends the list there with no body read, and reading goes on after it.</summary>
    public readonly List<EquipItem> Equips = new();
    /// <summary>ReadItems' bool and two i64 after the equips: the title layer's active flag and period (m_508, m_510, m_520).</summary>
    public bool CodiActive; public long CodiStart, CodiEnd;
    /// <summary>ReadItems' last list (aMore): the equips' layout, each made SetVariant(2) (the layer-2 outfits).</summary>
    public readonly List<EquipItem> Skins = new();
    /// <summary>The extra buffer (aExtra): u8 count, then per item u8 (the writer's GetSlot; the reader keeps it nowhere), i32
    /// id, i64 uid, i32 rarity, CItem::ReadFromStream. An unknown id ends the read (return).</summary>
    public readonly List<EquipItem> Gems = new();

    public static PcDataPack Read(ByteReader r) {
        var p = new PcDataPack { Value18 = r.ReadLong(), Value20 = r.ReadLong(), Value28 = r.ReadLong() };
        var record = new ByteReader(r.ReadBytes(r.ReadInt()));
        var items = new ByteReader(r.ReadBytes(r.ReadInt()));
        var extra = new ByteReader(r.ReadBytes(r.ReadInt()));
        p.Record = PcData.Read(record);
        ReadItemList(items, p.Equips);
        p.CodiActive = items.ReadBool(); p.CodiStart = items.ReadLong(); p.CodiEnd = items.ReadLong();
        ReadItemList(items, p.Skins);
        int n = extra.ReadByte();
        for (int i = 0; i < n; i++) {
            var gem = ItemReader.ReadKnownBadge(extra);
            if (gem == null) break;
            gem.BadgeSlot = 0;                  // the u8 is read into nUnused: SetSlot is not called
            p.Gems.Add(gem);
        }
        return p;
    }

    static void ReadItemList(ByteReader r, List<EquipItem> list) {
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) {
            var it = ItemReader.ReadKnownEquip(r);
            if (it == null) break;
            list.Add(it);
        }
    }

    /// <summary>
    /// UIPCInfoDialog::FUN_14132c330 (uipcinfodialog.cpp:289): the window's record from the pack, field by field (PCInfo &lt;-
    /// CPCData): m_18 level &lt;- m_n30, m_1c job group &lt;- m_n34, m_20 sub job &lt;- m_n38, m_28 account &lt;- m_n18, m_30
    /// character &lt;- m_n20, m_38 title &lt;- m_n3d4, m_aIds &lt;- m_arr3d8, the three stat blocks, m_3b0 &lt;- m_map390,
    /// m_3c0 = 0 (not the local record), m_3c4 gender &lt;- m_n3c, m_3d4 trophy &lt;- m_n3f8, m_3e0 picture &lt;- m_str3a0, m_3e8
    /// name &lt;- m_str28, m_3f8 guild &lt;- m_str3b0, m_400 grade &lt;- m_str3b8, m_408 motto &lt;- m_str3a8, m_3d8 &lt;- m_n408,
    /// m_3dc &lt;- m_n40c, m_530 prestige &lt;- m_n40. Then ReadItems: the equips go to m_mapEquip1 / 0 by HasSkin (as the
    /// local record's <see cref="PcInfo.Equips"/>), the gems to m_aItems, the outfits to m_mapEquip2 only while m_508.
    /// Not kept, as no reader of the window's record is ported: m_60 &lt;- m_n44, m_3c8 &lt;- m_n3fc, the guild's rank, emblem
    /// path, second name, id and emblem (m_n418, m_str428, m_str420, m_n430, m_n438), and the avatar (UIAvatarPC with the
    /// two skin colours m_an410). The attack range m_3cc / m_3d0 is the window's own sum over the equips.
    /// STILL-OPEN: m_3f0, the address, is CAddressTable::FormatBuddy(m_n20, m_n3c8, m_n3cc, m_n3d0, m_str3c0)
    /// (addresstable.cpp:229), which needs the buddy list (CBuddyManager::FindByAccount) and the ugc map rows and their
    /// string table (FUN_1408a0d30, CStringTable 3); left empty (the local record's is empty too).
    /// </summary>
    public PcInfo ToPcInfo() {
        var d = Record;
        var i = new PcInfo {
            IsLocal = false, Level = (ushort) d.Level, JobGroup = d.JobGroup, SubJob = d.SubJob, AccountId = d.AccountId,
            CharacterId = d.CharacterId, Title = d.Title, Special = new Dictionary<int, int>(d.Special), Gender = (byte) d.Gender,
            Trophy = d.Trophy, Picture = d.Picture, Name = d.Name, GuildName = d.GuildName, GuildGrade = d.GuildGrade,
            Motto = d.Motto, HomeCommendPoint = d.HomeCommendPoint, HomeCommendMonthly = d.HomeCommendMonthly,
            PrestigeLevel = d.PrestigeLevel,
            CodiActive = CodiActive, CodiStart = CodiStart, CodiEnd = CodiEnd,
            Equips = Equips, Outfit2 = CodiActive ? Skins : new List<EquipItem>(), Gems = Gems,
        };
        i.CodiItems = i.Outfit2.Count;
        foreach (uint t in d.Titles) i.Titles.Add(t);
        Array.Copy(d.Stats0, i.Stats0, PcInfo.StatCount); Array.Copy(d.Stats1, i.Stats1, PcInfo.StatCount); Array.Copy(d.Stats2, i.Stats2, PcInfo.StatCount);
        return i;
    }
}

/// <summary>CPCData::Read (commonfiles/pcdata.cpp:101), the pack's record buffer, in its order.</summary>
public sealed class PcData {
    public long AccountId, CharacterId;         // m_n18, m_n20
    public string Name = "";                    // m_str28
    public short Level;                         // m_n30 (i16)
    public int JobGroup, SubJob, Gender;        // m_n34, m_n38, m_n3c
    public int PrestigeLevel;                   // m_n40
    public byte Value44;                        // m_n44
    /// <summary>m_a48, m_a160, m_a278: 0x118 bytes each, the 0x23 stats' i64 values.</summary>
    public readonly long[] Stats0 = new long[PcInfo.StatCount], Stats1 = new long[PcInfo.StatCount], Stats2 = new long[PcInfo.StatCount];
    /// <summary>m_map390: i32 count, then i16 key (as u16), i32 value.</summary>
    public readonly Dictionary<int, int> Special = new();
    public string Picture = "";                 // m_str3a0: ReadUnicodeStringA, the same u16 + UTF-16 on the wire
    public string Motto = "", GuildName = "", GuildGrade = "", HomeName = "";   // m_str3a8, m_str3b0, m_str3b8, m_str3c0
    public int HomeMapId, HomeType, HomeValue;  // m_n3c8, m_n3cc, m_n3d0: FormatBuddy's id, type and unused argument
    public int Title;                           // m_n3d4
    public readonly List<uint> Titles = new();  // m_arr3d8: i32 count, i32 each
    public int Trophy;                          // m_n3f8
    public int Value3fc;                        // m_n3fc
    public long Value400;                       // m_n400
    public int HomeCommendPoint, HomeCommendMonthly;   // m_n408, m_n40c
    public int SkinColor0, SkinColor1;          // m_an410[0], [1]
    public ushort GuildRank;                    // m_n418 (i16 as u16)
    public string GuildName2 = "", GuildEmblemPath = "";   // m_str420, m_str428
    public long GuildId, GuildEmblem;           // m_n430, m_n438

    public static PcData Read(ByteReader r) {
        var d = new PcData {
            AccountId = r.ReadLong(), CharacterId = r.ReadLong(), Name = r.ReadUnicodeString(), Level = r.ReadShort(),
            JobGroup = r.ReadInt(), SubJob = r.ReadInt(), Gender = r.ReadInt(), PrestigeLevel = r.ReadInt(), Value44 = r.ReadByte(),
        };
        foreach (var block in new[] { d.Stats0, d.Stats1, d.Stats2 })
            for (int s = 0; s < PcInfo.StatCount; s++) block[s] = r.ReadLong();
        int pairs = r.ReadInt();
        for (int i = 0; i < pairs; i++) { int key = (ushort) r.ReadShort(); d.Special[key] = r.ReadInt(); }
        d.Picture = r.ReadUnicodeString(); d.Motto = r.ReadUnicodeString(); d.GuildName = r.ReadUnicodeString();
        d.GuildGrade = r.ReadUnicodeString(); d.HomeName = r.ReadUnicodeString();
        d.HomeMapId = r.ReadInt(); d.HomeType = r.ReadInt(); d.HomeValue = r.ReadInt(); d.Title = r.ReadInt();
        int n = r.ReadInt();
        for (int i = 0; i < n; i++) d.Titles.Add((uint) r.ReadInt());
        d.Trophy = r.ReadInt(); d.Value3fc = r.ReadInt(); d.Value400 = r.ReadLong();
        d.HomeCommendPoint = r.ReadInt(); d.HomeCommendMonthly = r.ReadInt();
        d.SkinColor0 = r.ReadInt(); d.SkinColor1 = r.ReadInt();
        d.GuildRank = (ushort) r.ReadShort(); d.GuildName2 = r.ReadUnicodeString(); d.GuildEmblemPath = r.ReadUnicodeString();
        d.GuildId = r.ReadLong(); d.GuildEmblem = r.ReadLong();
        return d;
    }
}
