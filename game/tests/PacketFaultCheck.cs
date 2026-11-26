using System.Collections.Generic;
using System.Reflection;
using Godot;
using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Wire;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/packet_fault.tscn) of the live session's packet loop (LiveSession.DrainGame, run by _Process):
/// a LiveSession without a connection is given a transport whose queue holds a PlaySystemSound with bytes left over (its
/// reader throws) and then a good one. The first must be warned once with its opcode, name and reader, the second must
/// reach SystemSoundReceived in the same drain, and the queue must be empty. Then the normal path's allocation: 1000
/// packets of an opcode nothing handles drained, 0 bytes each. Quits with 0 when all hold, else 1.
/// </summary>
public partial class PacketFaultCheck : Node
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    static byte[] Sound(string key, int extra)
    {
        var w = new ByteWriter();
        w.WriteUnicodeString(key);
        for (int i = 0; i < extra; i++) w.WriteByte(0);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    public override void _Ready()
    {
        var fails = new List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        var ls = new LiveSession();
        var game = new TcpTransport();
        typeof(LiveSession).GetField("game", Private)!.SetValue(ls, game);
        var drain = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), ls, typeof(LiveSession).GetMethod("DrainGame", Private)!);
        var heard = new List<string>();
        ls.SystemSoundReceived += m => heard.Add(m.Key);

        ushort sound = game.Profile.Number(Inbound.PlaySystemSound);
        game.Incoming.Enqueue(new RawPacket(sound, Sound("abc", 2)));
        game.Incoming.Enqueue(new RawPacket(sound, Sound("button_click", 0)));
        drain();
        Check(ls.PacketFaults == 1, $"{ls.PacketFaults} packets warned, not 1");
        string want = $"live: packet 0x{sound:X4} PlaySystemSound (10 bytes) read by SystemSound.Read: PacketLayoutException: PlaySystemSound: 2 bytes left over";
        Check(ls.LastPacketFault == want, $"the warning was \"{ls.LastPacketFault}\"");
        Check(heard.Count == 1 && heard[0] == "button_click", $"the next packet's sound was [{string.Join(", ", heard)}]");
        Check(game.Incoming.IsEmpty, "packets left in the queue");

        // the normal path: a packet Handle goes through and leaves (no reader, no log), with its catch, allocates nothing
        ushort none = 0xFFFF;
        while (game.Profile.TryInbound(none, out _)) none--;
        var body = new byte[4];
        game.Incoming.Enqueue(new RawPacket(none, body));
        drain();   // warm
        const int n = 1000;
        for (int i = 0; i < n; i++) game.Incoming.Enqueue(new RawPacket(none, body));
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        drain();
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"{bytes} bytes for {n} packets on the normal path");
        Check(ls.PacketFaults == 1 && game.Incoming.IsEmpty, "the normal packets warned or were left");

        // the warned path's cost, for the report: one bad packet
        game.Incoming.Enqueue(new RawPacket(sound, Sound("abc", 2)));
        before = System.GC.GetAllocatedBytesForCurrentThread();
        drain();
        long faultBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;

        game.Dispose();
        ls.Free();
        GD.Print($"packet fault: normal path {bytes / (double)n:F1} B/packet ({n} packets), a warned packet {faultBytes} B; "
            + (fails.Count == 0 ? "OK" : "FAIL " + string.Join("; ", fails)));
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
