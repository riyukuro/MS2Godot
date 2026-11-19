using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Mastery (0xCD; CSceneStateGame::OnMastery, scenestategame.cpp:3920). Only with a local player. A u8 mode, then:
/// <list type="bullet">
/// <item>0: u8 the type, i32 the new exp, i32 a sub type (the pet taming event check);</item>
/// <item>1: a grade reward received: i32 the reward id (type * 1000 + grade, put in DAT_143ab31a0 with 1), i32 a count
/// of (i32 item id, u16 amount) for the reward notice;</item>
/// <item>2: a craft done: u16 the type, i32 a count of (i32 item id, u16 amount);</item>
/// <item>3: u16 a result shown by CMasteryManager::FUN_140b6d710;</item>
/// <item>4: i32 a count of i32 ids (DAT_143ab3128), read as null here.</item>
/// </list>
/// The client reads the item lists only when the reward notice 0x4e5d exists, which a game scene always has.
/// </summary>
public abstract record MasteryMessage {
    public sealed record ExpChanged(int Type, int Exp, int SubType) : MasteryMessage;
    public sealed record RewardReceived(int RewardId, IReadOnlyList<(int ItemId, int Amount)> Items) : MasteryMessage;
    public sealed record Crafted(int Type, IReadOnlyList<(int ItemId, int Amount)> Items) : MasteryMessage;
    public sealed record Result(int Code) : MasteryMessage;

    public static MasteryMessage Read(ByteReader r) {
        switch (r.ReadByte()) {
            case 0: {
                int type = r.ReadByte();
                int exp = r.ReadInt();
                return new ExpChanged(type, exp, r.ReadInt());
            }
            case 1: {
                int id = r.ReadInt();
                return new RewardReceived(id, ReadItems(r));
            }
            case 2: {
                int type = (ushort) r.ReadShort();
                return new Crafted(type, ReadItems(r));
            }
            case 3: return new Result((ushort) r.ReadShort());
        }
        return null;
    }

    static List<(int, int)> ReadItems(ByteReader r) {
        int n = r.ReadInt();
        var a = new List<(int, int)>();
        for (int i = 0; i < n; i++) { int id = r.ReadInt(); a.Add((id, (ushort) r.ReadShort())); }
        return a;
    }
}

/// <summary>Outbound Mastery 0x92 mode 1 (UIMasteryDialog::vfunc_28 "onReceiveItem", uimasterydialog.cpp): u8 1, i32 the
/// grade reward id (type * 1000 + grade).</summary>
public sealed record MasteryRewardRequest(int RewardId) : IOutPacket {
    public Outbound Op => Outbound.Mastery;
    public void Write(ByteWriter w) { w.WriteByte(1); w.WriteInt(RewardId); }
}

/// <summary>Outbound Mastery 0x92 mode 2 (UIMasteryDialog::OnCastingFinished for a manufacturing craft): u8 2, i32 the
/// recipe id.</summary>
public sealed record MasteryCraftRequest(int RecipeId) : IOutPacket {
    public Outbound Op => Outbound.Mastery;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteInt(RecipeId); }
}
