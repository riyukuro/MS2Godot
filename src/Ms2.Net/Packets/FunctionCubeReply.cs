// Inbound 0x4D FunctionCube: CSceneStateGame::OnFunctionCube (@0x1417212a0, scenestategame.cpp:3660) -> CFunctionCubeService::FUN_14122f270 (@0x14122f270, functioncubeobject.cpp:2411).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>What a FunctionCube packet carries, by the client's mode.</summary>
public enum FunctionCubeKind
{
    /// <summary>A mode the client has no case for, or one this profile's number table does not name: nothing more is read.</summary>
    Unknown,
    /// <summary>KMS 2: i32 count, count x state record.</summary>
    States,
    /// <summary>KMS 3: one state record.</summary>
    State,
    /// <summary>KMS 6: i64 owner, string cube id, u8 flag (a furniture cube is used or left).</summary>
    Use,
    /// <summary>KMS 9: i64 owner, string cube id, i64 time, i32 count (a home-made cube finished).</summary>
    LifeSkillSuccess,
    /// <summary>KMS 10: i64 owner, string cube id, i64 time.</summary>
    LifeSkillFail,
    /// <summary>KMS 0x0D: i64 owner, string cube id, i32 error (a nurturing cube's notice).</summary>
    NurtureError,
}

/// <summary>
/// One cube state record (ReadCubeState @0x141230310): string cube id, i32 state, the cube kind's own fields, u8 flag. A nurturing cube
/// (CNurturingCubeObject::vfunc_1 @0x14122b2e0) has i64 time, i64, u16 level, u16 max level, u16, i64 between the state and the flag.
/// </summary>
public sealed record FunctionCubeState(string Id, int State, bool Flag, FunctionCubeNurture Nurture = null);

/// <summary>A nurturing cube's fields in its state record, in wire order (the client keeps them at m_nTime, m_nE0, m_nLevel, m_nMaxLevel, m_nF4, m_nC0).</summary>
public sealed record FunctionCubeNurture(long Time, long E0, ushort Level, ushort MaxLevel, ushort F4, long C0);

/// <summary>
/// Inbound 0x4D (OnFunctionCube, scenestategame.cpp:3660): u8 mode, then by <see cref="Kind"/>. The KMS client's modes are 2, 3, 6, 9,
/// 10, 0x0B, 0x0C, 0x0D and 0x0E. The GMS2 server numbers from the use mode on one lower: 2, 3, 5, 8 are the recorded ones (the use, and
/// the life skill success); 9 (life skill fail) is the Maple2 server's number and is in no recording. A mode the profile's table does not
/// name reads nothing more. KMS 0x0B, 0x0C and 0x0E (a nurturing cube's daily records, MyPC::ReadDailyRecords) are not read.
/// <para>Which state record is a nurturing cube's is told by the client's cube table (the cube's kind), not by the packet: the caller
/// passes <c>isNurturing</c> (record index, cube id); without one every record is read as the base record.
/// STILL-OPEN: the cube table is not ported, so the live session passes none and the packet is only read; the GMS2 numbers of the
/// nurturing modes are in no recording.</para>
/// </summary>
public sealed record FunctionCubeReply(
    FunctionCubeKind Kind, byte Mode,
    IReadOnlyList<FunctionCubeState> States,
    long Owner, string CubeId, bool Flag, long Time, int Count, int Error)
{
    static readonly IReadOnlyList<FunctionCubeState> None = Array.Empty<FunctionCubeState>();

    static FunctionCubeReply Of(FunctionCubeKind kind, byte mode) =>
        new(kind, mode, None, 0, null, false, 0, 0, 0);

    /// <summary>What the number the profile sent means: KMS as is; on GMS2 the recorded numbers and the Maple2 server's 9.</summary>
    public static FunctionCubeKind KindOf(byte mode, ProtocolProfile profile)
    {
        if (profile.IsGms2)
            return mode switch
            {
                2 => FunctionCubeKind.States,
                3 => FunctionCubeKind.State,
                5 => FunctionCubeKind.Use,
                8 => FunctionCubeKind.LifeSkillSuccess,
                9 => FunctionCubeKind.LifeSkillFail,
                _ => FunctionCubeKind.Unknown,
            };
        return mode switch
        {
            2 => FunctionCubeKind.States,
            3 => FunctionCubeKind.State,
            6 => FunctionCubeKind.Use,
            9 => FunctionCubeKind.LifeSkillSuccess,
            10 => FunctionCubeKind.LifeSkillFail,
            0x0D => FunctionCubeKind.NurtureError,
            _ => FunctionCubeKind.Unknown,
        };
    }

    static FunctionCubeState ReadState(ByteReader r, Func<int, string, bool> isNurturing, int index)
    {
        string id = r.ReadUnicodeString();
        int state = r.ReadInt();
        FunctionCubeNurture n = null;
        if (isNurturing != null && isNurturing(index, id))
            n = new FunctionCubeNurture(r.ReadLong(), r.ReadLong(), (ushort) r.ReadShort(), (ushort) r.ReadShort(), (ushort) r.ReadShort(), r.ReadLong());
        return new FunctionCubeState(id, state, r.ReadByte() != 0, n);
    }

    public static FunctionCubeReply Read(ByteReader r, ProtocolProfile profile, Func<int, string, bool> isNurturing = null)
    {
        byte mode = r.ReadByte();
        var kind = KindOf(mode, profile);
        switch (kind)
        {
            case FunctionCubeKind.States:
            {
                int count = r.ReadInt();
                if (count < 0 || count > r.Available / 7) throw new InvalidDataException($"FunctionCube count {count}");   // a record is at least 2 + 4 + 1 bytes
                var list = new List<FunctionCubeState>(count);
                for (int i = 0; i < count; i++) list.Add(ReadState(r, isNurturing, i));
                r.ExpectEnd("FunctionCube states");
                return Of(kind, mode) with { States = list };
            }
            case FunctionCubeKind.State:
            {
                var s = ReadState(r, isNurturing, 0);
                r.ExpectEnd("FunctionCube state");
                return Of(kind, mode) with { States = new[] { s } };
            }
            case FunctionCubeKind.Use:
            {
                long owner = r.ReadLong();
                string id = r.ReadUnicodeString();
                bool flag = r.ReadByte() != 0;
                r.ExpectEnd("FunctionCube use");
                return Of(kind, mode) with { Owner = owner, CubeId = id, Flag = flag };
            }
            case FunctionCubeKind.LifeSkillSuccess:
            {
                long owner = r.ReadLong();
                string id = r.ReadUnicodeString();
                long time = r.ReadLong();
                int count = r.ReadInt();
                r.ExpectEnd("FunctionCube life skill success");
                return Of(kind, mode) with { Owner = owner, CubeId = id, Time = time, Count = count };
            }
            case FunctionCubeKind.LifeSkillFail:
            {
                long owner = r.ReadLong();
                string id = r.ReadUnicodeString();
                long time = r.ReadLong();
                r.ExpectEnd("FunctionCube life skill fail");
                return Of(kind, mode) with { Owner = owner, CubeId = id, Time = time };
            }
            case FunctionCubeKind.NurtureError:
            {
                long owner = r.ReadLong();
                string id = r.ReadUnicodeString();
                int error = r.ReadInt();
                r.ExpectEnd("FunctionCube nurture error");
                return Of(kind, mode) with { Owner = owner, CubeId = id, Error = error };
            }
            default:
                return Of(kind, mode);
        }
    }
}
