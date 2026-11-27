using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>One reward item of a <see cref="DungeonRewardInfo"/> (DungeonRewardInfo::Read @0x14026c900, commonstruct_wa.cpp:343): the item id, the count, the rank and three flags.</summary>
public sealed record DungeonRewardItem(int ItemId, int Count, int Rank, bool Flag28, bool Flag29, bool Flag2B);

/// <summary>
/// DungeonRewardInfo (commonfiles/commonstruct_wa.cpp:307, Read @0x14026c900): i32 count of (u8 key, i32 value) rewards, then i32 count
/// of items (i32 id, i32 count, i32 rank, bool, bool, bool). A later entry of a key replaces the earlier one (the client's map assignment).
/// </summary>
public sealed class DungeonRewardInfo {
    /// <summary>The reward key of the meso (GetMeso @0x14026ccf0).</summary>
    public const byte MesoKey = 1;
    /// <summary>The reward key of the experience (GetExp @0x14026cbf0).</summary>
    public const byte ExpKey = 2;

    readonly SortedDictionary<byte, int> rewards = new();
    readonly List<DungeonRewardItem> items = new();

    public IReadOnlyDictionary<byte, int> Rewards => rewards;
    public IReadOnlyList<DungeonRewardItem> Items => items;

    public static DungeonRewardInfo Read(ByteReader r) {
        var info = new DungeonRewardInfo();
        int count = r.ReadInt();
        for (int i = 0; i < count; i++) {
            byte key = r.ReadByte();
            info.rewards[key] = r.ReadInt();
        }
        int itemCount = r.ReadInt();
        for (int i = 0; i < itemCount; i++)
            info.items.Add(new DungeonRewardItem(r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadBool(), r.ReadBool(), r.ReadBool()));
        return info;
    }

    /// <summary>GetReward @0x14026cd70: the value of a key, 0 without it.</summary>
    public long GetReward(byte key) => rewards.TryGetValue(key, out int v) ? (uint)v : 0;
    /// <summary>GetMeso @0x14026ccf0 (key 1; the client returns the unsigned 32 bit value).</summary>
    public long GetMeso() => GetReward(MesoKey);
    /// <summary>GetExp @0x14026cbf0 (key 2).</summary>
    public long GetExp() => GetReward(ExpKey);
    /// <summary>HasRewards @0x14026ce30: any reward value or any item.</summary>
    public bool HasRewards => rewards.Count > 0 || items.Count > 0;
}

/// <summary>
/// Inbound DungeonReward (UIDungeonRewardDialog::OnPacket @0x140e22130, uidungeonrewarddialog.cpp:386): u8 mode, then
/// 0 = the result (OnRewardPacket @0x140e22210: bool win, i32 room id, bool party, i32 seconds, i32 best score, i32 run score, 4 bytes of
/// bits (bitset 32), the main DungeonRewardInfo, the bonus DungeonRewardInfo), 1 = a round (OnRoundPacket @0x140e225a0: i32 round,
/// i32 stage, the main info), 2 = the minigame start (FUN_140e22650: the main info), 3 = the minigame end (FUN_140e226b0: bool win,
/// the main info). Another mode reads nothing (the switch has no default). The client leaves the rest of a body unread, so there is no ExpectEnd.
/// </summary>
public sealed class DungeonRewardPacket {
    public byte Mode;
    public bool Win, Party;
    public int RoomId, Seconds, BestScore, RunScore, Round, Stage;
    /// <summary>m_bits (Unk_RewardBits::Read @0x140e26800): 4 bytes, bit i is byte i/8, bit i%8.</summary>
    public byte[] Bits = new byte[4];
    /// <summary>m_1e8 and m_228: the main and the bonus rewards.</summary>
    public DungeonRewardInfo Main = new(), Bonus = new();

    public static DungeonRewardPacket Read(ByteReader r) {
        var p = new DungeonRewardPacket { Mode = r.ReadByte() };
        switch (p.Mode) {
            case 0:
                p.Win = r.ReadBool();
                p.RoomId = r.ReadInt();
                p.Party = r.ReadBool();
                p.Seconds = r.ReadInt();
                p.BestScore = r.ReadInt();
                p.RunScore = r.ReadInt();
                for (int i = 0; i < 4; i++) p.Bits[i] = r.ReadByte();
                p.Main = DungeonRewardInfo.Read(r);
                p.Bonus = DungeonRewardInfo.Read(r);
                break;
            case 1:
                p.Round = r.ReadInt();
                p.Stage = r.ReadInt();
                p.Main = DungeonRewardInfo.Read(r);
                break;
            case 2:
                p.Main = DungeonRewardInfo.Read(r);
                break;
            case 3:
                p.Win = r.ReadBool();
                p.Main = DungeonRewardInfo.Read(r);
                break;
        }
        return p;
    }

    /// <summary>Unk_RewardBits::IsBitSet @0x140e26ae0 (positions 0..31).</summary>
    public bool IsBitSet(int pos) => (uint)pos < 32 && (Bits[pos / 8] & (1 << (pos % 8))) != 0;
}

/// <summary>UIDungeonRewardDialog::SendDungeonReward (@0x140e25ff0, uidungeonrewarddialog.cpp), the close of a dungeon whose room leaves after the reward: DungeonReward u8 6.</summary>
public sealed record DungeonRewardCloseRequest : IOutPacket {
    public Outbound Op => Outbound.DungeonReward;
    public void Write(ByteWriter w) => w.WriteByte(6);
}
