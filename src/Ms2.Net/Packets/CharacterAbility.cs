using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// CharacterAbility (0xDF; CCharacterAbilityManager::OnCharacterAbility @0x140f29ad0, uicharacterabilitydialog.cpp:206).
/// A u8 mode, then: 0 (FUN_140f29b70) the learned abilities, an unsigned i32 count of i32 ids, then the last reset's time
/// (i64) and the extra points (i32); 2 (FUN_140f29cc0) one learned ability's i32 id; 1 (FUN_140f29de0) nothing is read.
/// Other modes are read as null.
/// </summary>
public abstract record CharacterAbilityMessage {
    public sealed record Loaded(IReadOnlyList<int> Ids, long ResetTime, int Points) : CharacterAbilityMessage;
    public sealed record Learned(int Id) : CharacterAbilityMessage;
    public sealed record Reset : CharacterAbilityMessage;

    public static CharacterAbilityMessage Read(ByteReader r) {
        byte mode = r.ReadByte();
        switch (mode) {
            case 0: {
                var ids = new List<int>();
                uint n = (uint) r.ReadInt();
                for (uint i = 0; i < n; i++) ids.Add(r.ReadInt());
                long reset = r.ReadLong();
                return new Loaded(ids, reset, r.ReadInt());
            }
            case 2: return new Learned(r.ReadInt());
            case 1: return new Reset();
        }
        return null;
    }
}

/// <summary>Outbound 0x89 mode 2 (UICharacterAbilityDialog::vfunc_28 "onAddButton" @0x140f2a620,
/// uicharacterabilitydialog.cpp:422-425): u8 2, i32 the ability id. The client adds nothing itself: the server's mode 2 does.</summary>
public sealed record CharacterAbilityLearnRequest(int Id) : IOutPacket {
    public Outbound Op => Outbound.CharacterAbility;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteInt(Id); }
}

/// <summary>Outbound 0x89 mode 1 (UICharacterAbilityDialog::OnMessage @0x140f2ae70, uicharacterabilitydialog.cpp:504-506):
/// the meso reset box confirmed with the mesos for it, u8 1 alone.</summary>
public sealed record CharacterAbilityResetRequest : IOutPacket {
    public Outbound Op => Outbound.CharacterAbility;
    public void Write(ByteWriter w) => w.WriteByte(1);
}
