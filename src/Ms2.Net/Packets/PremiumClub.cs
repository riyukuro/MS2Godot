// Inbound 0xED PremiumClub (KMS 0xEB): CSceneStateGame::OnPremiumClub (scenestategame.cpp:8426, @0x141739f90).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>What a PremiumClub packet carries, by the client's mode.</summary>
public enum PremiumClubKind
{
    /// <summary>A mode the client has no case for: nothing more is read.</summary>
    Unknown,
    /// <summary>Mode 0: a player's premium club end time (PC::FUN_141662d60 sets m_n1190, which PC::IsVip compares with the server time).</summary>
    Time,
    /// <summary>Mode 1 (UIVIPDialog::FUN_140fb87d0, uivipdialog.cpp:234): the benefit ids already claimed, replacing the dialog's list.</summary>
    ClaimedList,
    /// <summary>Mode 2: one more claimed benefit id; the dialog shows the claimed item's box.</summary>
    Claimed,
}

/// <summary>
/// Inbound 0xED (OnPremiumClub): u8 mode, then by <see cref="Kind"/>: Time = i32 player object id, i64 end time; ClaimedList = i32 count,
/// count x i32 benefit id; Claimed = i32 benefit id. The client's other modes (any other non-zero number) go to the VIP dialog, which
/// reads only modes 1 and 2.
/// <para>The recorded GMS2 time packet comes in two lengths: 12 bytes after the mode as the client reads it, and 8 bytes, the end time
/// alone with no player id (29 of 48 recorded time packets, all of them at login). The 8-byte form is read as <see cref="HasPlayerId"/>
/// false.</para>
/// <para>STILL-OPEN: what the 8-byte form addresses (the client reads an id first and would take the time's low half for it); the
/// client's other VIP dialog modes (the upstream server also writes 3 = package list, 4 = package purchase) are in no recording and no
/// client code, so they read nothing.</para>
/// </summary>
public sealed record PremiumClub(
    PremiumClubKind Kind, byte Mode, bool HasPlayerId, int PlayerId, long EndTime, IReadOnlyList<int> BenefitIds)
{
    static readonly IReadOnlyList<int> None = Array.Empty<int>();

    public static PremiumClubKind KindOf(byte mode) => mode switch
    {
        0 => PremiumClubKind.Time,
        1 => PremiumClubKind.ClaimedList,
        2 => PremiumClubKind.Claimed,
        _ => PremiumClubKind.Unknown,
    };

    public static PremiumClub Read(ByteReader r, ProtocolProfile profile)
    {
        byte mode = r.ReadByte();
        var kind = KindOf(mode);
        switch (kind)
        {
            case PremiumClubKind.Time:
            {
                bool hasId = !(profile.IsGms2 && r.Available == 8);
                int id = hasId ? r.ReadInt() : 0;
                long t = r.ReadLong();
                r.ExpectEnd("PremiumClub time");
                return new PremiumClub(kind, mode, hasId, id, t, None);
            }
            case PremiumClubKind.ClaimedList:
            {
                int count = r.ReadInt();
                if (count < 0 || count > r.Available / 4) throw new InvalidDataException($"PremiumClub claimed count {count}");
                var ids = new int[count];
                for (int i = 0; i < count; i++) ids[i] = r.ReadInt();
                r.ExpectEnd("PremiumClub claimed list");
                return new PremiumClub(kind, mode, false, 0, 0, ids);
            }
            case PremiumClubKind.Claimed:
            {
                int id = r.ReadInt();
                r.ExpectEnd("PremiumClub claimed");
                return new PremiumClub(kind, mode, false, 0, 0, new[] { id });
            }
            default:
                return new PremiumClub(kind, mode, false, 0, 0, None);
        }
    }
}
