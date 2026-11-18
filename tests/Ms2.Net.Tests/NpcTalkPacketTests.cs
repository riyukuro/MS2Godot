using System.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>Synthetic NpcTalk (0x4B) packets laid out as the Maple2 server's NpcTalkPacket writes them, and the client's NpcTalk / Quest sends.</summary>
public class NpcTalkPacketTests {
    static NpcTalkMessage Read(Action<ByteWriter> f) { var w = new ByteWriter(); f(w); return NpcTalkMessage.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray())); }
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test] public void RespondContinueAndClose() {
        // NpcTalkPacket.Respond: object id, NpcTalkType (u8), NpcDialogue (script id, index, button as i32)
        var open = Read(w => { w.WriteByte(1); w.WriteInt(1234); w.WriteByte(4); w.WriteInt(100); w.WriteInt(0); w.WriteInt(6); });
        Assert.That(open, Is.EqualTo(new NpcTalkMessage.Open(1234, 4, 100, 0, 6)));
        // NpcTalkPacket.Continue: type, quest id, dialogue
        var next = Read(w => { w.WriteByte(2); w.WriteByte(4); w.WriteInt(90000005); w.WriteInt(300); w.WriteInt(1); w.WriteInt(7); });
        Assert.That(next, Is.EqualTo(new NpcTalkMessage.Next(4, 90000005, 300, 1, 7)));
        Assert.That(Read(w => w.WriteByte(0)), Is.TypeOf<NpcTalkMessage.Close>());
    }

    [Test] public void RewardsReadAsTheClientReadsThem() {
        Assert.That(Read(w => { w.WriteByte(3); w.WriteByte(7); w.WriteLong(1000); }), Is.EqualTo(new NpcTalkMessage.RewardMeso(1000)));
        // the server writes the exp as an i64 (RewardExp); the client's OnRewardExp reads an i32
        Assert.That(Read(w => { w.WriteByte(3); w.WriteByte(8); w.WriteLong(450); }), Is.EqualTo(new NpcTalkMessage.RewardExp(450)));
        Assert.That(Read(w => { w.WriteByte(3); w.WriteByte(9); w.WriteInt(5); w.WriteBool(true); }), Is.EqualTo(new NpcTalkMessage.RewardAffinity(5, true)));
        Assert.That(Read(w => { w.WriteByte(3); w.WriteByte(10); w.WriteInt(2000062); w.WriteUnicodeString("movie"); }), Is.EqualTo(new NpcTalkMessage.Cutscene(2000062, "movie")));
        var dlg = (NpcTalkMessage.OpenDialog) Read(w => { w.WriteByte(3); w.WriteByte(4); w.WriteUnicodeString("JobChangeDialog"); w.WriteUnicodeString("10"); });
        Assert.That((dlg.Dialog, dlg.Argument), Is.EqualTo(("JobChangeDialog", "10")));
    }

    [Test] public void RewardItemOfAnUnknownIdReadsNoBody() {
        var was = ItemReader.Known;
        try {
            ItemReader.Known = id => id != 1;
            var m = (NpcTalkMessage.RewardItems) Read(w => { w.WriteByte(3); w.WriteByte(5); w.WriteInt(1); w.WriteInt(1); w.WriteByte(1); w.WriteInt(10); });
            Assert.That(m.Items, Is.Empty);
        } finally { ItemReader.Known = was; }
    }

    [Test] public void ModifyScriptAndUnreadModes() {
        var m = (NpcTalkMessage.ModifyScript) Read(w => { w.WriteByte(4); w.WriteUnicodeString("text"); w.WriteUnicodeString("voice"); w.WriteUnicodeString("illust"); });
        Assert.That((m.Script, m.Voice, m.Illust), Is.EqualTo(("text", "voice", "illust")));
        Assert.That(Read(w => { w.WriteByte(10); w.WriteInt(1); }), Is.EqualTo(new NpcTalkMessage.Other(10, 0)));
    }

    [Test] public void TheClientsSendsAreLaidOutAsItsWriters() {
        Assert.That(Bytes(new NpcTalkBeginRequest(0x01020304)), Is.EqualTo(new byte[] { 1, 4, 3, 2, 1 }));
        Assert.That(Bytes(new NpcTalkEndRequest()), Is.EqualTo(new byte[] { 0 }));
        Assert.That(Bytes(new NpcTalkNextRequest(2)), Is.EqualTo(new byte[] { 2, 2, 0, 0, 0 }));
        Assert.That(Bytes(new NpcTalkQuestRedirectRequest(5, 2)), Is.EqualTo(new byte[] { 7, 5, 0, 0, 0, 2, 0 }));
        Assert.That(Bytes(new QuestAcceptRequest(5, 9)), Is.EqualTo(new byte[] { 2, 5, 0, 0, 0, 9, 0, 0, 0 }));
        Assert.That(Bytes(new QuestCompleteRequest(5, 9, 0)), Is.EqualTo(new byte[] { 4, 5, 0, 0, 0, 9, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.That(Bytes(new QuestProgressRequest(5)), Is.EqualTo(new byte[] { 0x12, 5, 0, 0, 0 }));
        Assert.That(new NpcTalkBeginRequest(1).Op, Is.EqualTo(Outbound.NpcTalk));
        Assert.That(new QuestAcceptRequest(1, 1).Op, Is.EqualTo(Outbound.Quest));
    }
}
