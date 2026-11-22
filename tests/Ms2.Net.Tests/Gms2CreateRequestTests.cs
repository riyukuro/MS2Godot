using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The GMS2 create request (CharacterManagement mode 1): the item block names each slot, read from the recordings.</summary>
public class Gms2CreateRequestTests {
    static readonly ProtocolProfile Gms = ProtocolProfile.Gms2;

    [Test]
    public void EveryRecordedGms2CreateRequestIsWrittenBackIdentically() {
        string root = Environment.GetEnvironmentVariable("MS2_SNIFFS");
        if (string.IsNullOrEmpty(root)) Assert.Ignore("MS2_SNIFFS needed");
        ushort op = Gms.Number(Outbound.CharacterManagement);
        int seen = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(root, "gms2-sniffs"), "*.msb", SearchOption.AllDirectories)) {
            foreach (var p in Ms2.Net.Replay.MsbFile.Read(path).Packets) {
                if (p.ServerToClient || p.Opcode != op || p.Data.Length == 0 || p.Data[0] != 1) continue;
                seen++;
                var c = CreateCharacter.Read(new ByteReader(p.Data));
                var items = CreateCharacter.DecodeItemsGms2(c.Items);
                Assert.That(CreateCharacter.EncodeItemsGms2(items), Is.EqualTo(c.Items), Path.GetFileName(path));
                // the game builds the KMS block; the profile write turns it into what was recorded
                var kms = c with { Items = CreateCharacter.EncodeItems(items) };
                var w = new ByteWriter(); kms.Write(w, Gms);
                Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(p.Data), Path.GetFileName(path));
            }
        }
        Assert.That(seen, Is.GreaterThan(0));
    }
}
