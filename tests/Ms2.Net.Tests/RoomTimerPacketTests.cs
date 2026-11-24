using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The RoomTimer reader (RoomTimerManager::Dispatch, roomtimermanager.cpp) on recorded KMS2 and GMS2 bodies (KMS2:
/// commands 0 and 1; GMS2: 0, 1, 2, 4 and 5) and on a lap list built as the server fork writes it.</summary>
public class RoomTimerPacketTests
{
    static RoomTimerMessage Read(string hex) => RoomTimerMessage.Read(new ByteReader(Convert.FromHexString(hex)));

    [Test]
    public void StartReadsTheTypeTheTickTheDurationAndTheOffset()
    {
        var m = Read("00010DBC971EA0BB0D0000000000");      // KMS2: the timer dialog, 15 minutes
        Assert.That((m.Command, m.Type, m.StartTick, m.Duration, m.Offset), Is.EqualTo((RoomTimerMessage.Start, RoomTimerMessage.TimerDialog, 0x1E97BC0D, 900_000, 0)));
        m = Read("000139468406B888000000000000");          // GMS2: 35 s
        Assert.That((m.Type, m.Duration), Is.EqualTo((RoomTimerMessage.TimerDialog, 35_000)));
    }

    [Test]
    public void UpdateReadsTheNoticeTypeAndTheDelta()
    {
        var m = Read("010DBC971E70460D0001000000D08AFFFF");    // KMS2: 30 s removed
        Assert.That((m.Command, m.StartTick, m.Duration, m.NoticeType, m.Delta), Is.EqualTo((RoomTimerMessage.Update, 0x1E97BC0D, 870_000, 1, -30_000)));
        m = Read("014EA5B77700A60E000200000060EA0000");        // GMS2: notice 2
        Assert.That((m.Duration, m.NoticeType, m.Delta), Is.EqualTo((960_000, 2, 60_000)));
    }

    [Test]
    public void RestartStopAndCancel()
    {
        var m = Read("020122749919E093040000000000");      // GMS2
        Assert.That((m.Command, m.Type, m.Duration), Is.EqualTo((RoomTimerMessage.Restart, RoomTimerMessage.TimerDialog, 300_000)));
        Assert.That(Read("04").Command, Is.EqualTo(RoomTimerMessage.Stop));
        Assert.That(Read("05").Command, Is.EqualTo(RoomTimerMessage.Cancel));
    }

    [Test]
    public void ValuesReadTheLapPairs()
    {
        var w = new ByteWriter();
        w.WriteByte(RoomTimerMessage.Values);
        w.WriteInt(2); w.WriteInt(7); w.WriteInt(30_000); w.WriteInt(3); w.WriteInt(60_000);
        var m = RoomTimerMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()));
        Assert.That(m.Laps, Is.EqualTo(new[] { (7, 30_000), (3, 60_000) }));
    }

    [Test]
    public void ATrailingByteFailsTheRead()
    {
        Assert.Throws<PacketLayoutException>(() => Read("0400"));
    }
}
