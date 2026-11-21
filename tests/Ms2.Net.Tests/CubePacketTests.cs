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
}
