using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class CubePacketTests
{
    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test]
    public void OpcodesMatchClientTable()
    {
        // commonfiles/opcodes.h:113 RecvOp::ResponseCube = 0x0069; SendOp::RequestCube = 0x0037
        Assert.That((ushort)Inbound.ResponseCube, Is.EqualTo(0x0069));
        Assert.That((ushort)Outbound.RequestCube, Is.EqualTo(0x0037));
    }

    [Test]
    public void HeldItemReleaseIsRequestCubeWithOnlyMode12()
    {
        // PC::SendRequestCube (pc.cpp:1929-1937): WriteUInt8(0x12) and nothing else
        var p = new HeldItemReleaseRequest();
        var w = new ByteWriter();
        p.Write(w);
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestCube));
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 0x12 }));
    }

    [Test]
    public void ResponseCubeMode12ReadsErrorThenId()
    {
        // OnResponseCube (scenestategame.cpp:9876) u8 mode; case 0x12 (:10176-10192): u8 error, i32 id
        var ok = ResponseCubeHeldItem.Read(Reader(w => { w.WriteByte(0x12); w.WriteByte(0); w.WriteInt(1234); }));
        Assert.That(ok, Is.EqualTo(new ResponseCubeHeldItem(0x12, 0, 1234)));
        var err = ResponseCubeHeldItem.Read(Reader(w => { w.WriteByte(0x12); w.WriteByte(3); w.WriteInt(77); }));
        Assert.That(err, Is.EqualTo(new ResponseCubeHeldItem(0x12, 3, 77)));
        Assert.That(ResponseCubeHeldItem.Read(Reader(w => { w.WriteByte(0x10); w.WriteByte(0); w.WriteInt(1); })), Is.Null);
    }

    [Test]
    public void ResponseCubeMode11ReadsFourIntsOnSuccessAndTheCharacterOnError()
    {
        // case 0x11 (scenestategame.cpp:10145-10167): u8 error; error 0: i32 id, i32 cube key, i32 item id, i32 value;
        // else i32 the character id
        var ok = ResponseCubeHeldItem.Read(Reader(w => { w.WriteByte(0x11); w.WriteByte(0); w.WriteInt(5); w.WriteInt(9); w.WriteInt(30000001); w.WriteInt(-2); }));
        Assert.That(ok, Is.EqualTo(new ResponseCubeHeldItem(0x11, 0, 5, 9, 30000001, -2)));
        var err = ResponseCubeHeldItem.Read(Reader(w => { w.WriteByte(0x11); w.WriteByte(4); w.WriteInt(77); }));
        Assert.That(err, Is.EqualTo(new ResponseCubeHeldItem(0x11, 4, 77)));
    }

    static byte[] Bytes(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    // CUgcItemLook::Read (ugcdef.cpp:1551): i64, str, str, u8, i32, i64, i64, str, i64, str, u8
    static void WriteLook(ByteWriter w, string url, byte state)
    {
        w.WriteLong(900); w.WriteUnicodeString("f"); w.WriteUnicodeString("n"); w.WriteByte(3);
        w.WriteInt(-7); w.WriteLong(11); w.WriteLong(12);
        w.WriteUnicodeString("a"); w.WriteLong(13); w.WriteUnicodeString(url); w.WriteByte(state);
    }

    [Test]
    public void SetCraftModeIsRecvOp68()
    {
        // commonfiles/opcodes.h:112 RecvOp::SetCraftMode = 0x0068
        Assert.That((ushort)Inbound.SetCraftMode, Is.EqualTo(0x0068));
    }

    [Test]
    public void SetCraftModeOffReadsOnlyTheIdAndTheMode()
    {
        // OnSetCraftMode (scenestategame.cpp:7829-7844): i32 id, u8 mode; mode 0 reads nothing more
        var m = SetCraftMode.Read(Reader(w => { w.WriteInt(41); w.WriteByte(0); }));
        Assert.That(m, Is.EqualTo(new SetCraftMode(41, 0, null, 0)));
        Assert.Throws<PacketLayoutException>(() => SetCraftMode.Read(Reader(w => { w.WriteInt(41); w.WriteByte(0); w.WriteInt(2); })));
    }

    [Test]
    public void SetCraftModeOnReadsTheCubeItemThenTheCraftMode()
    {
        // :7836-7840: CubeItemInfo(packet) (ugcmapoptions.cpp:181) then i32 the craft mode
        var m = SetCraftMode.Read(Reader(w =>
        {
            w.WriteInt(41); w.WriteByte(2);
            w.WriteInt(50400001); w.WriteLong(77); w.WriteLong(1); w.WriteByte(0);
            w.WriteInt(2);
        }));
        Assert.That((m.Id, m.CubeMode, m.CraftMode), Is.EqualTo((41, (byte)2, 2)));
        Assert.That((m.Item.ItemId, m.Item.InstanceId, m.Item.Count, m.Item.Look), Is.EqualTo((50400001, 77L, 1L, (UgcLook)null)));

        var withLook = SetCraftMode.Read(Reader(w =>
        {
            w.WriteInt(41); w.WriteByte(1);
            w.WriteInt(50400001); w.WriteLong(77); w.WriteLong(1); w.WriteByte(1); WriteLook(w, "u", 2);
            w.WriteInt(3);
        }));
        Assert.That(withLook.CraftMode, Is.EqualTo(3));
        Assert.That((withLook.Item.Look.Uid, withLook.Item.Look.Value34, withLook.Item.Look.AccountId, withLook.Item.Look.CharacterId, withLook.Item.Look.State),
            Is.EqualTo((900L, -7, 11L, 12L, (byte)2)));
    }

    [Test]
    public void CubeItemInfoWritesWhatItReads()
    {
        // CubeItemInfo::FUN_1408b39c0 (ugcmapoptions.cpp:170) and CUgcItemLook::Write (ugcdef.cpp:1522) mirror the readers
        byte[] plain = Bytes(w => { w.WriteInt(50400001); w.WriteLong(77); w.WriteLong(1); w.WriteByte(0); });
        Assert.That(Bytes(CubeItemInfo.Read(new ByteReader(plain)).Write), Is.EqualTo(plain));
        byte[] looked = Bytes(w => { w.WriteInt(50400001); w.WriteLong(77); w.WriteLong(1); w.WriteByte(1); WriteLook(w, "u", 2); });
        Assert.That(Bytes(CubeItemInfo.Read(new ByteReader(looked)).Write), Is.EqualTo(looked));
    }

    [Test]
    public void IsSameItemComparesTheItemIdThenTheUploadedLook()
    {
        // CubeItemInfo::IsSameItem (@0x1408b38c0, ugcmapoptions.cpp:142-166)
        static CubeItemInfo I(int id, long uid = 0) => new() { ItemId = id, Look = uid == 0 ? null : new UgcLook { Uid = uid } };
        Assert.That(I(7).IsSameItem(null), Is.False);
        Assert.That(I(7).IsSameItem(I(7)), Is.True);
        Assert.That(I(7).IsSameItem(I(8)), Is.False);
        Assert.That(new CubeItemInfo().IsSameItem(new CubeItemInfo()), Is.True, "two cleared items");
        Assert.That(I(7).IsSameItem(I(7, 5)), Is.False, "only the other has a look");
        Assert.That(I(7, 5).IsSameItem(I(7)), Is.False, "only this one has a look");
        Assert.That(I(7, 5).IsSameItem(I(7, 5)), Is.True);
        Assert.That(I(7, 5).IsSameItem(I(7, 6)), Is.False);
        Assert.That(I(7, 5).IsSameItem(I(8, 5)), Is.False);
        Assert.That(I(7).IsSameItem(new CubeItemInfo { ItemId = 7, Look = new UgcLook() }), Is.True, "a look with uid 0 is no uploaded look");
    }

    [Test]
    public void ALookInState1WritesAnEmptyUrl()
    {
        // CUgcItemLook::Write (ugcdef.cpp:1532-1538): m_n60 == 1 writes "" for the url
        var look = new CubeItemInfo { ItemId = 1, Look = new UgcLook { Url = "u", State = 1 } };
        byte[] got = Bytes(look.Write);
        byte[] want = Bytes(w =>
        {
            w.WriteInt(1); w.WriteLong(0); w.WriteLong(0); w.WriteByte(1);
            w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString(""); w.WriteByte(0);
            w.WriteInt(0); w.WriteLong(0); w.WriteLong(0);
            w.WriteUnicodeString(""); w.WriteLong(0); w.WriteUnicodeString(""); w.WriteByte(1);
        });
        Assert.That(got, Is.EqualTo(want));
    }

    [Test]
    public void HousingGradeIsReadForTheOwnAccountOnly()
    {
        // scenestategame.cpp:10560-10574: i64 account; own: i64 time, i64 grade (u16), i64 exp, i32 n, n i32
        var mine = ResponseCubeHousingGrade.Read(Reader(w =>
        {
            w.WriteLong(42); w.WriteLong(1700000000); w.WriteLong(0x10005); w.WriteLong(900);
            w.WriteInt(3); w.WriteInt(2); w.WriteInt(4); w.WriteInt(2);
        }), 42);
        Assert.That((mine.Mine, mine.DayChangeTime, mine.Grade, mine.Exp), Is.EqualTo((true, 1700000000L, (ushort)5, 900L)));
        Assert.That(mine.RewardedGrades, Is.EqualTo(new[] { 2, 4, 2 }));
        // another account: the client returns after the id, so the rest is left unread
        var other = ResponseCubeHousingGrade.Read(Reader(w => { w.WriteLong(41); w.WriteLong(1); w.WriteLong(2); }), 42);
        Assert.That(other.Mine, Is.False);
        Assert.That(other.RewardedGrades, Is.Empty);
    }

    [Test]
    public void HomeInfoReadsTheClientsNineFieldsInOrder()
    {
        // ugcmapoptions.cpp:291: i32 x4, string, i64 x2, u8 result, u8 open type
        var m = ResponseCubeHomeInfo.Read(Reader(w =>
        {
            w.WriteInt(7); w.WriteInt(11); w.WriteInt(-2); w.WriteInt(5); w.WriteUnicodeString("Home");
            w.WriteLong(0x123456789AL); w.WriteLong(-9); w.WriteByte(1); w.WriteByte(2);
        }));
        Assert.That(m, Is.EqualTo(new ResponseCubeHomeInfo(7, 11, -2, 5, "Home", 0x123456789AL, -9, 1, 2)));
    }

    [Test]
    public void Gms2HomeInfoHasAnIntAfterField24AndNoOpenType()
    {
        // GMS2: i32 x4, i32, string, i64 x2, u8 result
        var m = ResponseCubeHomeInfo.Read(Reader(w =>
        {
            w.WriteInt(7); w.WriteInt(62000000); w.WriteInt(2010002); w.WriteInt(5); w.WriteInt(0); w.WriteUnicodeString("Home");
            w.WriteLong(0x5EE54964L); w.WriteLong(-9); w.WriteByte(1);
        }), ProtocolProfile.Gms2);
        Assert.That(m, Is.EqualTo(new ResponseCubeHomeInfo(7, 62000000, 2010002, 5, "Home", 0x5EE54964L, -9, 1, 0)));
    }

    [TestCase("gms2")]
    [TestCase("kms2")]
    public void EveryRecordedHomeInfoReadsToTheEnd(string set)
    {
        // the field entry's 0x14 in every live-server recording reads in its profile's layout with nothing left over; the
        // debug client recording (owner object 10000001, the home named "Unknown") is no live server's and has a third layout
        var profile = set == "gms2" ? ProtocolProfile.Gms2 : ProtocolProfile.Kms2525;
        int seen = 0;
        foreach (var (file, data) in Gms2Sniffs.Inbound(set, profile.Number(Inbound.ResponseCube)))
        {
            if (data.Length == 0 || data[0] != ResponseCubeHomeInfo.Mode || file == "kmsdebug client.msb") continue;
            seen++;
            var r = new ByteReader(data);
            r.ReadByte();
            var m = ResponseCubeHomeInfo.Read(r, profile);
            Assert.That(m.PlotId, Is.EqualTo(62000000), file);
        }
        Assert.That(seen, Is.GreaterThan(0));
    }

    [Test]
    public void HomeInfoRefusesATrailingByte()
    {
        Assert.That(() => ResponseCubeHomeInfo.Read(Reader(w =>
        {
            w.WriteInt(1); w.WriteInt(1); w.WriteInt(1); w.WriteInt(1); w.WriteUnicodeString("");
            w.WriteLong(1); w.WriteLong(1); w.WriteByte(0); w.WriteByte(0); w.WriteByte(0);
        })), Throws.Exception);
    }

    [Test]
    public void HouseOwnerReadsFourLongs()
    {
        // :10590-10597: four i64, read before the account check
        var m = ResponseCubeHouseOwner.Read(Reader(w => { w.WriteLong(42); w.WriteLong(77); w.WriteLong(-3); w.WriteLong(9); }));
        Assert.That(m, Is.EqualTo(new ResponseCubeHouseOwner(42, 77, -3, 9)));
    }

    [Test]
    public void OwnerTrophiesReadAShortGradeAndPairs()
    {
        // :10746-10757: i16 grade, i32 n, n (i32 id, i32 level)
        var m = ResponseCubeOwnerTrophies.Read(Reader(w =>
        {
            w.WriteShort(6); w.WriteInt(2); w.WriteInt(2300); w.WriteInt(1); w.WriteInt(2301); w.WriteInt(3);
        }));
        Assert.That(m.OwnerGrade, Is.EqualTo(6));
        Assert.That(m.Trophies, Is.EqualTo(new[] { (2300, 1), (2301, 3) }));
        Assert.That(ResponseCubeOwnerTrophies.Read(Reader(w => { w.WriteShort(1); w.WriteInt(0); })).Trophies, Is.Empty);
    }

    static void WriteItem(ByteWriter w, int id, long uid) { w.WriteInt(id); w.WriteLong(uid); w.WriteLong(1); w.WriteByte(0); }

    static void WritePlaced(ByteWriter w, byte mode, int cell, byte noticeFlag)
    {
        w.WriteByte(mode); w.WriteByte(0); w.WriteInt(3); w.WriteInt(1234);
        if (mode == ResponseCubePlace.ModePlace) { w.WriteInt(5); w.WriteInt(2); }
        w.WriteInt(cell); w.WriteLong(99); WriteItem(w, 50100001, 7); w.WriteByte(1); w.WriteFloat(90f); w.WriteInt(11);
        if (mode != ResponseCubePlace.ModePlace) return;
        w.WriteByte(noticeFlag);
        if (noticeFlag == 1) { w.WriteUnicodeString("n"); w.WriteByte(4); }
    }

    [Test]
    public void APlaceAnswerReadsItsCubeAndOnlyAFlagOfOneHasANotice()
    {
        // CSceneStateGame::OnResponseCube 0xa (scenestategame.cpp:9984-10011): i32, i32 pc id, i32 group, i32 floor, i32 cell,
        // i64, CubeItemInfo, u8, f32, i32, u8 flag; only 1 is followed by a string and a u8
        var m = ResponseCubePlace.Read(Reader(w => WritePlaced(w, 0xa, 0x00fe0103, 1)));
        Assert.That((m.Mode, m.Error, m.CharacterId, m.Group, m.Floor, m.Cell, m.Owner), Is.EqualTo(((byte)0xa, (byte)0, 1234, 5, 2, 0x00fe0103, 99L)));
        Assert.That((m.Item.ItemId, m.Item.InstanceId, m.Liftable, m.Rotation, m.House), Is.EqualTo((50100001, 7L, true, 90f, 11)));
        Assert.That((m.NoticeText != null, m.NoticeType), Is.EqualTo((true, (byte)4)));
        foreach (byte flag in new byte[] { 0, 2 })
        {
            var plain = ResponseCubePlace.Read(Reader(w => WritePlaced(w, 0xa, 1, flag)));
            Assert.That((plain.NoticeText, plain.NoticeType), Is.EqualTo(((string)null, (byte)0)), $"flag {flag}");
        }
        Assert.That(() => ResponseCubePlace.Read(Reader(w => { WritePlaced(w, 0xa, 1, 0); w.WriteByte(0); })), Throws.Exception);   // ExpectEnd
    }

    [Test]
    public void AReplaceAnswerHasNoGroupFloorOrNotice()
    {
        // 0xf (:10102-10121): i32, i32 pc id, i32 cell, i64, CubeItemInfo, u8, f32, i32
        var m = ResponseCubePlace.Read(Reader(w => WritePlaced(w, 0xf, 0x020001, 0)));
        Assert.That((m.Mode, m.CharacterId, m.Cell, m.Item.ItemId, m.House, m.Group, m.Floor), Is.EqualTo(((byte)0xf, 1234, 0x020001, 50100001, 11, 0, 0)));
    }

    [Test]
    public void APlaceErrorIsTwoIntsAndOtherModesAreNotPlaceAnswers()
    {
        // :10012-10014 and :10122-10124: an error answer is i32, i32 pc id
        foreach (byte mode in new byte[] { 0xa, 0xf })
        {
            var m = ResponseCubePlace.Read(Reader(w => { w.WriteByte(mode); w.WriteByte(6); w.WriteInt(0); w.WriteInt(77); }));
            Assert.That(m, Is.EqualTo(new ResponseCubePlace(mode, 6, 77)));
        }
        Assert.That(ResponseCubePlace.Read(Reader(w => { w.WriteByte(0x11); w.WriteByte(0); })), Is.Null);
    }

    [Test]
    public void LoadCubesModeZeroReadsEachCubeAndItsNotice()
    {
        // commonfiles/opcodes.h:114 RecvOp::LoadCubes = 0x006A; OnLoadCubes (scenestategame.cpp:10857-10897) mode 0:
        // u8 state, i32 n, per cube i32 cell, i64 owner, CubeItemInfo, i32, i32, u8, f32, i32, u8 notice flag (1: text, u8 type)
        Assert.That((ushort)Inbound.LoadCubes, Is.EqualTo(0x006A));
        var m = LoadCubes.Read(Reader(w =>
        {
            w.WriteByte(0); w.WriteByte(2); w.WriteInt(2);
            w.WriteInt(0x00ff0102); w.WriteLong(77);
            w.WriteInt(50100001); w.WriteLong(9); w.WriteLong(1); w.WriteByte(0);
            w.WriteInt(3); w.WriteInt(4); w.WriteByte(1); w.WriteFloat(90f); w.WriteInt(5); w.WriteByte(0);
            w.WriteInt(0x00000003); w.WriteLong(78);
            w.WriteInt(50100002); w.WriteLong(10); w.WriteLong(1); w.WriteByte(0);
            w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteFloat(0f); w.WriteInt(0); w.WriteByte(1);
            w.WriteUnicodeString("x"); w.WriteByte(6);
        }));
        Assert.That(m, Is.Not.Null);
        Assert.That((m.FloorState, m.Cubes.Count), Is.EqualTo(((byte)2, 2)));
        var a = m.Cubes[0];
        Assert.That((a.Cell, a.Owner, a.Item.ItemId, a.Item.InstanceId, a.Group, a.Floor, a.Liftable, a.Rotation, a.House, a.NoticeText),
            Is.EqualTo((0x00ff0102, 77L, 50100001, 9L, 3, 4, true, 90f, 5, (string)null)));
        var b = m.Cubes[1];
        Assert.That((b.Cell, b.Item.ItemId, b.NoticeText != null, b.NoticeType), Is.EqualTo((3, 50100002, true, (byte)6)));
        Assert.That(LoadCubes.Read(Reader(w => { w.WriteByte(1); w.WriteInt(0); })), Is.Null);
    }

    [Test]
    public void LoadCubesModesOneToThreeReadThePlotStatesOwnersAndSaleStates()
    {
        // OnLoadCubes (scenestategame.cpp:10902-10944): i32 n, then mode 1: i32 plot, u8 state; mode 2: i32 plot,
        // i32 block, unicode name, i64 owner id; mode 3: i32 plot, i32 block, u8 state, i64 value
        var m1 = LoadCubesPlots.Read(Reader(w => { w.WriteByte(1); w.WriteInt(2); w.WriteInt(7); w.WriteByte(1); w.WriteInt(-3); w.WriteByte(0); }));
        Assert.That((m1.Mode, m1.States.Count, m1.Owners.Count, m1.Sales.Count), Is.EqualTo(((byte)1, 2, 0, 0)));
        Assert.That(m1.States, Is.EqualTo(new[] { (7, (byte)1), (-3, (byte)0) }));

        var m2 = LoadCubesPlots.Read(Reader(w =>
        {
            w.WriteByte(2); w.WriteInt(1);
            w.WriteInt(4); w.WriteInt(101); w.WriteUnicodeString("o"); w.WriteLong(0x1_0000_0002);
        }));
        Assert.That((m2.Mode, m2.States.Count, m2.Owners.Count, m2.Sales.Count), Is.EqualTo(((byte)2, 0, 1, 0)));
        Assert.That(m2.Owners[0], Is.EqualTo(new LoadCubesPlots.PlotOwner(4, 101, "o", 0x1_0000_0002)));

        var m3 = LoadCubesPlots.Read(Reader(w =>
        {
            w.WriteByte(3); w.WriteInt(1);
            w.WriteInt(5); w.WriteInt(102); w.WriteByte(2); w.WriteLong(-9);
        }));
        Assert.That((m3.Mode, m3.States.Count, m3.Owners.Count, m3.Sales.Count), Is.EqualTo(((byte)3, 0, 0, 1)));
        Assert.That(m3.Sales[0], Is.EqualTo(new LoadCubesPlots.PlotSale(5, 102, 2, -9)));

        // mode 0 is LoadCubes', and the switch has no default (:10944): neither is read here
        Assert.That(LoadCubesPlots.Read(Reader(w => { w.WriteByte(0); w.WriteByte(0); w.WriteInt(0); })), Is.Null);
        Assert.That(LoadCubesPlots.Read(Reader(w => { w.WriteByte(4); w.WriteInt(0); })), Is.Null);
    }

    [Test]
    public void RowSwitchAndLevelReadTheClientsTwoBytes()
    {
        // scenestategame.cpp:10619-10633: u8 row, u8 value; the switch is on only for exactly 1
        Assert.That(ResponseCubeRowSwitch.Read(Reader(w => { w.WriteByte(4); w.WriteByte(1); })), Is.EqualTo(new ResponseCubeRowSwitch(4, true)));
        Assert.That(ResponseCubeRowSwitch.Read(Reader(w => { w.WriteByte(4); w.WriteByte(2); })).On, Is.False);
        Assert.That(ResponseCubeRowLevel.Read(Reader(w => { w.WriteByte(8); w.WriteByte(3); })), Is.EqualTo(new ResponseCubeRowLevel(8, 3)));
        Assert.That(() => ResponseCubeRowLevel.Read(Reader(w => { w.WriteByte(8); w.WriteByte(3); w.WriteByte(0); })), Throws.Exception);
    }

    [Test]
    public void CraftModeOffIsRequestSetCraftModeZeroZero()
    {
        // mypc.cpp:4507-4510; SendOp::RequestSetCraftMode = 0x0036 (opcodes.h:359)
        var req = new CraftModeOffRequest();
        Assert.That((ushort)req.Op, Is.EqualTo(0x0036));
        var w = new ByteWriter(); req.Write(w);
        Assert.That(w.Buffer.AsSpan(0, w.Length).ToArray(), Is.EqualTo(new byte[] { 0, 0 }));
    }
}

[TestFixture]
public class ResponseCubeBillTests
{
    static ByteReader Reader(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
    }

    [Test] public void CubeBillReadsRowsAsAMapByPayType()
    {
        var m = ResponseCubeBill.Read(Reader(w => { w.WriteByte(3); w.WriteInt(9); w.WriteByte(3); w.WriteLong(30); w.WriteByte(1); w.WriteLong(5); w.WriteByte(3); w.WriteLong(40); }), ResponseCubeBill.ModeCube);
        Assert.That((m.Count, m.Arg, m.Preview), Is.EqualTo((3, 9, false)));
        Assert.That(m.Amounts.Select(x => (x.Key, x.Value)), Is.EqualTo(new[] { (1, 5L), (3, 40L) }));
    }

    [Test] public void BlueprintBillHasThePreviewFlagAndTheCubeBillDoesNot()
    {
        Assert.That(ResponseCubeBill.Read(Reader(w => { w.WriteByte(0); w.WriteInt(1); w.WriteBool(true); }), ResponseCubeBill.ModeBlueprint).Preview, Is.True);
        // trailing bytes are ignored, as the client does
        Assert.That(ResponseCubeBill.Read(Reader(w => { w.WriteByte(0); w.WriteInt(1); w.WriteBool(true); }), ResponseCubeBill.ModeCube).Preview, Is.False);
    }

    [Test] public void BuyRequests()
    {
        var w = new ByteWriter(); new CubeBuyRequest(6).Write(w);
        Assert.That(w.Buffer[..w.Length], Is.EqualTo(new byte[] { 0x30, 6, 0, 0, 0 }));
        w = new ByteWriter(); new BlueprintBuyRequest(2).Write(w);
        Assert.That(w.Buffer[..w.Length], Is.EqualTo(new byte[] { 0x46, 2, 0, 0, 0, 0, 0, 0, 0 }));
        w = new ByteWriter(); new BlueprintBillAskRequest(3).Write(w);
        Assert.That(w.Buffer[..w.Length], Is.EqualTo(new byte[] { 0x42, 3, 0, 0, 0, 0, 0, 0, 0, 1 }));
    }
}
