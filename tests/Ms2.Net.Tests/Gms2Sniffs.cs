using Ms2.Net.Replay;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The recorded packets of one opcode for the sniff-driven GMS2 layout tests. Skips the test without MS2_SNIFFS; recordings are local and never part of the repo.</summary>
static class Gms2Sniffs {
    /// <summary>Every server-to-client packet of an opcode in the GMS2 recordings (set "gms2") or the KMS2 recordings ("kms2", builds 2520 and up), with the file name.</summary>
    public static List<(string File, byte[] Data)> Inbound(string set, ushort opcode) {
        string root = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("MS2_SNIFFS needed");
        string dir = Directory.Exists(Path.Combine(root, set + "-sniffs")) ? Path.Combine(root, set + "-sniffs") : root;
        var list = new List<(string, byte[])>();
        foreach (var path in Directory.GetFiles(dir, "*.msb")) {
            var f = MsbFile.Read(path);
            if (set == "kms2" && f.Build < 2520) continue;
            foreach (var p in f.Packets) if (p.ServerToClient && p.Opcode == opcode) list.Add((Path.GetFileName(path), p.Data));
        }
        return list;
    }
}
