using Ms2.Net.Packets;
using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The recorded packets of one opcode for the sniff-driven GMS2 layout tests. Skips the test without MS2_SNIFFS; recordings are local and never part of the repo.</summary>
static class Gms2Sniffs {
    /// <summary>Every packet in the given direction of an opcode in the GMS2 recordings (set "gms2") or the KMS2 recordings ("kms2", builds 2520 and up), with the file name.</summary>
    public static List<(string File, byte[] Data)> Inbound(string set, ushort opcode) => Packets(set, opcode, serverToClient: true);

    /// <summary>The client-to-server packets of an opcode, see <see cref="Inbound"/>.</summary>
    public static List<(string File, byte[] Data)> Outbound(string set, ushort opcode) => Packets(set, opcode, serverToClient: false);

    static List<(string File, byte[] Data)> Packets(string set, ushort opcode, bool serverToClient) {
        string root = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("MS2_SNIFFS needed");
        // MS2_SNIFFS is the folder that holds the <set>-sniffs folders. A root that holds the recordings directly is taken as the KMS2 set only: the GMS2 tests must never read KMS2 files.
        string dir = Path.Combine(root, set + "-sniffs");
        if (!Directory.Exists(dir)) {
            if (set != "kms2" || !Directory.GetFiles(root, "*.msb").Any()) Assert.Ignore($"MS2_SNIFFS has no {set}-sniffs folder");
            dir = root;
        }
        var list = new List<(string, byte[])>();
        foreach (var path in Directory.GetFiles(dir, "*.msb")) {
            var f = MsbFile.Read(path);
            if (set == "kms2" && f.Build < 2520) continue;
            foreach (var p in f.Packets) if (p.ServerToClient == serverToClient && p.Opcode == opcode) list.Add((Path.GetFileName(path), p.Data));
        }
        return list;
    }

    /// <summary>Runs <paramref name="body"/> with the item hooks (model slot, body kind, every id read) from the GMS2 item data in MS2_DATA, then restores them; ignores the test when MS2_DATA is not a GMS2 data folder with the per-item files.</summary>
    public static void WithGms2ItemData(Action body) {
        string data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set: item bodies need the data hooks");
        using var packs = new Ms2.Formats.Pack.PackSet(data);
        if (!packs.Exists("Xml", "item/1/13/11320388.xml")) Assert.Ignore("MS2_DATA has no per-item GMS2 files");
        var (s, e, k) = (ItemReader.SlotOf, ItemReader.ExtraOf, ItemReader.Known);
        try {
            ItemReader.SlotOf = new Ms2Godot.Net.ItemModelSlots(packs).SlotOf; ItemReader.ExtraOf = new Ms2Godot.Net.ItemKinds(packs).ExtraOf; ItemReader.Known = null;
            body();
        } finally { ItemReader.SlotOf = s; ItemReader.ExtraOf = e; ItemReader.Known = k; }
    }
}
