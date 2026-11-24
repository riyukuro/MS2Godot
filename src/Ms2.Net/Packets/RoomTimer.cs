// Layouts from the restored client's reader, checked against the server fork's writer (RoomTimerPacket) and the recorded
// KMS2 and GMS2 bodies.
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// Inbound RoomTimer (RecvOp 0x50, commonfiles/opcodes.h:88; GMS2 0x51): CSceneStateGame hands it to RoomTimerManager::Dispatch
/// (@0x140b8c260, roomtimermanager.cpp; scenestategame.cpp:1574), which reads a u8 command:
/// 0 Start / 2 Restart: u8 timer type (1 the timer dialog 0x4e3a, 2 the gauge 0x4f61), i32 start server tick, i32 duration
/// in ms, i32 offset in seconds;
/// 1 Update: i32 start, i32 duration, i32 notice type, i32 delta in ms;
/// 3 ReadValues: i32 count, then count pairs of i32 id and i32 time (the gauge's laps; both read through vfunc_3,
/// FUN_14070f400 / FUN_14070f480);
/// 4 Stop and 5 Cancel: nothing more. Any other command reads nothing more.
/// </summary>
public sealed record RoomTimerMessage(byte Command)
{
    public const byte Start = 0, Update = 1, Restart = 2, Values = 3, Stop = 4, Cancel = 5;
    /// <summary>The timer types (RoomTimerManager::GetTimer).</summary>
    public const byte TimerDialog = 1, TimerGauge = 2;

    public byte Type { get; init; }
    public int StartTick { get; init; }
    public int Duration { get; init; }
    public int Offset { get; init; }
    /// <summary>Update: 1 the added / removed seconds notice, 2 the fixed notice (common 0x9a0), else none.</summary>
    public int NoticeType { get; init; }
    /// <summary>Update: the ms added (above 0) or removed (below 0).</summary>
    public int Delta { get; init; }
    public IReadOnlyList<(int Id, int Time)> Laps { get; init; } = Array.Empty<(int, int)>();

    public static RoomTimerMessage Read(ByteReader r)
    {
        byte command = r.ReadByte();
        var m = new RoomTimerMessage(command);
        switch (command)
        {
            case Start:
            case Restart:
                m = m with { Type = r.ReadByte(), StartTick = r.ReadInt(), Duration = r.ReadInt(), Offset = r.ReadInt() };
                break;
            case Update:
                m = m with { StartTick = r.ReadInt(), Duration = r.ReadInt(), NoticeType = r.ReadInt(), Delta = r.ReadInt() };
                break;
            case Values:
            {
                uint count = (uint)r.ReadInt();
                var laps = new List<(int, int)>();
                for (uint i = 0; i < count; i++) laps.Add((r.ReadInt(), r.ReadInt()));
                m = m with { Laps = laps };
                break;
            }
            case Stop:
            case Cancel:
                break;
            default:
                return m;
        }
        r.ExpectEnd("RoomTimer");
        return m;
    }
}
