using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Npc packets against recordings. Needs MS2_SNIFFS (recordings) and MS2_DATA (the player's Data folder, for the npc table the layouts depend on).</summary>
public class NpcSniffTests {
    [Test] public void NpcPacketsDecode() {
        string dir = Environment.GetEnvironmentVariable("MS2_SNIFFS"), data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(data)) Assert.Ignore("MS2_SNIFFS and MS2_DATA needed");
        using var packs = new PackSet(data);
        var table = new NpcTable(packs);
        NpcLayout Layout(int id) { var r = table.Find(id); return r == null ? default : new NpcLayout(r.IsBoss, r.HasDamagedHpBarTag); }
        int adds = 0, addsUnknownRow = 0, controls = 0, blocks = 0, removes = 0, dead = 0, bosses = 0;
        var fail = new List<string>();
        var tails = new SortedDictionary<int, int>();
        foreach (var path in Directory.GetFiles(dir, "*.msb")) {
            var f = MsbFile.Read(path);
            if (f.Build < 2520) continue;
            var npcOf = new Dictionary<int, int>();
            foreach (var p in f.Packets.Where(p => p.ServerToClient)) {
                var r = new ByteReader(p.Data);
                try {
                    switch (p.Opcode) {
                        case 0x55: {
                            int npcId = BitConverter.ToInt32(p.Data, 4);
                            if (table.Find(npcId) == null) { addsUnknownRow++; npcOf[BitConverter.ToInt32(p.Data, 0)] = npcId; break; }   // layout unknowable without the row
                            var n = FieldAddNpc.Read(r, Layout); npcOf[n.ObjectId] = n.NpcId; adds++; if (Layout(n.NpcId).IsBoss) bosses++;
                            break;
                        }
                        case 0x56: NpcObjectId.Read(r); removes++; break;
                        case 0x57: NpcObjectId.Read(r); dead++; break;
                        case 0x58: {
                            var c = NpcControl.Read(r, id => npcOf.TryGetValue(id, out int nid) && Layout(nid).IsBoss);
                            controls++; blocks += c.Blocks.Count;
                            foreach (var b in c.Blocks) tails[Math.Min(b.Tail.Length, 40)] = tails.GetValueOrDefault(Math.Min(b.Tail.Length, 40)) + 1;
                            break;
                        }
                    }
                } catch (Exception e) { if (fail.Count < 8) fail.Add($"{Path.GetFileName(path)} b{f.Build} op {p.Opcode:X2}: {e.Message[..Math.Min(70, e.Message.Length)]}"); else fail.Add(null); }
            }
        }
        TestContext.Out.WriteLine($"FieldAddNpc {adds} decoded ({bosses} bosses), {addsUnknownRow} with an npc id missing from the local table; remove {removes}, dead {dead}; NpcControl {controls} packets, {blocks} blocks; failures {fail.Count}");
        TestContext.Out.WriteLine("block tail lengths: " + string.Join(" ", tails.Select(k => $"{k.Key}:{k.Value}")));
        foreach (var s in fail.Where(s => s != null)) TestContext.Out.WriteLine("FAIL " + s);
        Assert.That(fail, Is.Empty);
    }
}
