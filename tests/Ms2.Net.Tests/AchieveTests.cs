using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class AchieveTests {
    static ByteReader R(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray()); }

    internal static void Progress(ByteWriter w, byte state, int cur, int start, long count, params (int Key, long Value)[] values) {
        w.WriteByte(state); w.WriteInt(7); w.WriteInt(cur); w.WriteInt(start); w.WriteByte(1); w.WriteLong(count);
        w.WriteInt(values.Length);
        foreach (var (k, v) in values) { w.WriteInt(k); w.WriteLong(v); }
    }

    [Test] public void ModesAreTheAchieveManagersOnPacket() {
        Assert.That(AchieveMessage.Read(R(w => w.WriteByte(0))), Is.TypeOf<AchieveMessage.Reload>());
        Assert.That(AchieveMessage.Read(R(w => w.WriteByte(9))), Is.Null);

        var r = R(w => {
            w.WriteByte(1); w.WriteInt(2);
            w.WriteInt(21100001); w.WriteInt(1); Progress(w, 3, 2, 1, 1, (1, 1637000000L));
            w.WriteInt(22200002); w.WriteInt(3); Progress(w, 2, 3, 1, 0);
            w.WriteByte(0x5A);
        });
        var rows = (AchieveMessage.Rows) AchieveMessage.Read(r);
        Assert.That(rows.List, Has.Count.EqualTo(2));
        var (id, grade, p) = rows.List[0];
        Assert.That((id, grade, p.State, p.Value80, p.CurGrade, p.StartGrade, p.Bookmark, p.Count), Is.EqualTo((21100001, 1, (byte) 3, 7, 2, 1, true, 1L)));
        Assert.That(p.GetValue(1), Is.EqualTo(1637000000L));
        Assert.That(p.GetValue(2), Is.EqualTo(0));
        Assert.That(r.ReadByte(), Is.EqualTo(0x5A), "every row's progress is read");

        var u = (AchieveMessage.Update) AchieveMessage.Read(R(w => { w.WriteByte(2); w.WriteInt(22200002); Progress(w, 2, 4, 1, 5, (3, 1)); }));
        Assert.That((u.Id, u.Progress.CurGrade, u.Progress.Count, u.Progress.GetValue(3)), Is.EqualTo((22200002, 4, 5L, 1L)));
    }
}
