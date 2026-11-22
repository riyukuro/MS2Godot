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

    /// <summary>Mode 8 as a recorded GMS2 server sends it (3 of 3 packets are this 18-byte form): type, quest id, script id, page, button set.</summary>
    [Test] public void AGms2ReloadReadsWhole() {
        var r = new ByteReader(Convert.FromHexString("08047354 9a056400 00000000 00000400 0000".Replace(" ", "")));
        Assert.That(NpcTalkMessage.Read(r), Is.EqualTo(new NpcTalkMessage.Reload(4, 0x059a5473, 100, 0, 4)));
        Assert.That(r.Available, Is.Zero);
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
        Assert.That(Read(w => { w.WriteByte(5); w.WriteInt(1); }), Is.EqualTo(new NpcTalkMessage.Other(5, 0)));
    }

    [Test] public void ModeTenCarriesATalkScript() {
        // OnTalkPacket mode 10 (uitalkcinematic.cpp:1050): i32 the npc's FIELD OBJECT id, i16 sub, then
        // CTalkScript::Deserialize (talkscript.cpp:216): i32 the script's npc id (a data id), u8 popup state, i32 popup
        // prop, u8 random pick, then the contents.
        var m = (NpcTalkMessage.SpecialNpc) Read(w => {
            w.WriteByte(10); w.WriteInt(2001); w.WriteShort(1);
            w.WriteInt(11000004); w.WriteByte(9); w.WriteInt(100); w.WriteByte(0);
            w.WriteInt(3);
            // a balloon content (kind 1): the line, its voice, its illust (ReadUnicodeStringA is u16 + UTF-16 on the wire)
            w.WriteByte(1); w.WriteUnicodeString("hello there"); w.WriteUnicodeString("voice_a"); w.WriteUnicodeString("");
            // a cinematic content (kind 0): the base, its button set, a u8 flag, then the choices when the flag is 1
            // (CCinematicContent::vfunc_6, scriptcontent.cpp:385)
            w.WriteByte(0); w.WriteUnicodeString("page"); w.WriteUnicodeString("voice_b"); w.WriteUnicodeString("illust_b");
            w.WriteInt(9); w.WriteByte(1);
            w.WriteByte(1);
            w.WriteUnicodeString("a choice"); w.WriteUnicodeString("goto"); w.WriteUnicodeString("gotoFail");
            // a second page whose flag is 0: no choices follow
            w.WriteByte(0); w.WriteUnicodeString("page two"); w.WriteUnicodeString("voice_c"); w.WriteUnicodeString("");
            w.WriteInt(3); w.WriteByte(0);
        });
        Assert.That((m.NpcId, m.Sub), Is.EqualTo((2001, (short) 1)), "the packet's id is the field object id, not the data id");
        Assert.That(m.Script.NpcId, Is.EqualTo(11000004), "the script's own npc id is a data id");
        Assert.That((m.Script.PopupState, m.Script.PopupProp, m.Script.RandomPick), Is.EqualTo(((byte) 9, 100, false)));
        Assert.That(m.Script.Contents.Count, Is.EqualTo(3));
        var line = m.Script.Contents[0];
        Assert.That((line.Kind, line.Text, line.Voice, line.Illust, line.ButtonSet, line.Choices.Count), Is.EqualTo(((byte) 1, "hello there", "voice_a", "", 0, 0)));
        var page = m.Script.Contents[1];
        Assert.That((page.Kind, page.Text, page.Voice, page.Illust, page.ButtonSet), Is.EqualTo(((byte) 0, "page", "voice_b", "illust_b", 9)));
        Assert.That(page.Choices, Is.EqualTo(new[] { ("a choice", "goto", "gotoFail") }));
        var plain = m.Script.Contents[2];
        Assert.That((plain.Kind, plain.Text, plain.ButtonSet, plain.Choices.Count), Is.EqualTo(((byte) 0, "page two", 3, 0)), "a flag-0 page reads no choices");
        Assert.That(NpcTalkMessage.SubMode(m), Is.Null);
    }

    [Test] public void EveryModeThreeSubIsKeptAsTheSubMode() {
        Assert.That(NpcTalkMessage.SubMode(Read(w => { w.WriteByte(3); w.WriteByte(1); w.WriteUnicodeString("tag"); })), Is.EqualTo(1));
        Assert.That(NpcTalkMessage.SubMode(Read(w => { w.WriteByte(3); w.WriteByte(7); w.WriteLong(1); })), Is.EqualTo(7));
        Assert.That(NpcTalkMessage.SubMode(Read(w => { w.WriteByte(3); w.WriteByte(2); })), Is.EqualTo(2), "a sub with no reader is kept too");
        Assert.That(NpcTalkMessage.SubMode(Read(w => w.WriteByte(0))), Is.Null);
        Assert.That(NpcTalkMessage.SubMode(new NpcTalkMessage.Other(10, 0)), Is.Null);
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

    /// <summary>cinematiccomponent.cpp:1082-1083: COutPacket(SendOp::ResolvePenalty = 0x44), Encode4(GetNpcId()).</summary>
    [Test] public void ResolvePenaltyIsTheOpcodeAndTheNpcObjectId() {
        Assert.That(Bytes(new ResolvePenaltyRequest(0x01020304)), Is.EqualTo(new byte[] { 4, 3, 2, 1 }));
        Assert.That((ushort)new ResolvePenaltyRequest(1).Op, Is.EqualTo(0x44));
    }

    /// <summary>cinematiccomponent.cpp:1159-1161: COutPacket(SendOp::TakeBoat = 0x45), Encode4(GetNpcId()).</summary>
    [Test] public void TakeBoatIsTheOpcodeAndTheNpcObjectId() {
        Assert.That(Bytes(new TakeBoatRequest(0x01020304)), Is.EqualTo(new byte[] { 4, 3, 2, 1 }));
        Assert.That((ushort)new TakeBoatRequest(1).Op, Is.EqualTo(0x45));
    }

    // TaxiStationAction::vfunc_1 (actionkeyobject.cpp:800-802): COutPacket(RequestTaxi), Encode1(0)
    [Test] public void RequestTaxiRegisterIsModeZeroAlone() {
        Assert.That(Bytes(new RequestTaxiRegister()), Is.EqualTo(new byte[] { 0 }));
        Assert.That((ushort)new RequestTaxiRegister().Op, Is.EqualTo(0x48));
    }

    // StateTaxiCall phase 0x69 (statetaxicalldata.cpp:282-288): Encode1(1), then WriteTaxiRequest = Encode4(id), Encode4(cost)
    [Test] public void RequestTaxiRideIsModeOneThenIdAndCost() {
        Assert.That(Bytes(new RequestTaxiRide(0x01020304)), Is.EqualTo(new byte[] { 1, 4, 3, 2, 1, 0, 0xe1, 0xf5, 5 }));
        Assert.That(Bytes(new RequestTaxiRide(2, 7)), Is.EqualTo(new byte[] { 1, 2, 0, 0, 0, 7, 0, 0, 0 }));
        Assert.That((ushort)new RequestTaxiRide(1).Op, Is.EqualTo(0x48));
    }

    // StateCashCall::SendRequest (statecashcalldata.cpp:365-376): call types 1 and 2 are mode 3, type 3 is mode 4, then the price
    [Test] public void RequestTaxiCashIsMode3Or4ThenThePrice() {
        Assert.That(Bytes(new RequestTaxiCash(1, 0x0a0b)), Is.EqualTo(new byte[] { 3, 0x0b, 0x0a, 0, 0 }));
        Assert.That(Bytes(new RequestTaxiCash(2, 5)), Is.EqualTo(new byte[] { 3, 5, 0, 0, 0 }));
        Assert.That(Bytes(new RequestTaxiCash(3, 5)), Is.EqualTo(new byte[] { 4, 5, 0, 0, 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bytes(new RequestTaxiCash(0, 5)));
    }

    // CAutoFindTaxiStation::SendRequestTaxiMode5 (autofindtaxistation.cpp:299-301): Encode1(5)
    [Test] public void RequestTaxiAutoFindIsModeFiveAlone() {
        Assert.That(Bytes(new RequestTaxiAutoFind()), Is.EqualTo(new byte[] { 5 }));
        Assert.That((ushort)new RequestTaxiAutoFind().Op, Is.EqualTo(0x48));
    }
}
