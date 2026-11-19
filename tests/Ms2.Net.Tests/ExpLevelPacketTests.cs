using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>ExpUp (CSceneStateGame::OnExpUp, scenestategame.cpp:4129) and LevelUp (OnLevelUp, :9511) into the own character.</summary>
public class ExpLevelPacketTests {
    static ByteReader Reader(ByteWriter w) => new(w.Buffer.AsSpan(0, w.Length).ToArray());

    [Test]
    public void ExpUpModeZeroSetsTheTotalAndRest() {
        var w = new ByteWriter();
        w.WriteByte(0); w.WriteLong(120); w.WriteShort(unchecked((short) 0x8001)); w.WriteLong(5000); w.WriteLong(300);
        w.WriteInt(21000001); w.WriteByte(1);
        var e = ExpUp.Read(Reader(w));
        Assert.That(e, Is.EqualTo(new ExpUp(0, 120, 0x8001, 5000, 300, 21000001, true)));
        var me = new MyCharacter();
        int changes = 0;
        me.Changed += () => changes++;
        me.Apply(e);
        Assert.That((me.Exp, me.RestExp, changes), Is.EqualTo((5000L, 300L, 1)));
    }

    [Test]
    public void ExpUpModeOneSetsOnlyTheRest() {
        var w = new ByteWriter();
        w.WriteByte(1); w.WriteLong(77);
        var me = new MyCharacter { Exp = 9 };
        me.Apply(ExpUp.Read(Reader(w)));
        Assert.That((me.Exp, me.RestExp), Is.EqualTo((9L, 77L)));
    }

    [Test]
    public void LevelUpChangesOnlyTheOwnObject() {
        var w = new ByteWriter();
        w.WriteInt(42); w.WriteShort(7);
        var l = LevelUp.Read(Reader(w));
        Assert.That(l, Is.EqualTo(new LevelUp(42, 7)));
        var me = new MyCharacter { ObjectId = 41, User = new UserBlock { Level = 1 } };
        Assert.That(me.Apply(l), Is.False);
        Assert.That(me.User.Level, Is.EqualTo(1));
        me.ObjectId = 42;
        Assert.That(me.Apply(l), Is.True);
        Assert.That(me.User.Level, Is.EqualTo(7));
    }
}
