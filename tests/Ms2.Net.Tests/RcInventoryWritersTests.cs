using System.Reflection;
using Ms2.Net;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// The inventory writers: mode 4 (drop a count), mode 5 (destroy) and mode 9 (expand a tab), all on
/// SendOp::RequestItemInventory (0x17). The client's writers are networkservice.cpp:514-517 and the KMS calls at
/// uiinventorydialog.cpp:1874-1881 (mode 9); the handler side is invenslot.cpp:1109-1140, :1379-1505. The records are
/// Ms2.Net.ItemInventoryDropRequest(long uid, int count), ItemInventoryDestroyRequest(long uid) and
/// ItemInventoryExpandRequest(byte tab).
/// </summary>
public class RcInventoryWritersTests
{
    static IOutPacket Packet(string name, params object[] args)
    {
        var t = typeof(Outbound).Assembly.GetType("Ms2.Net." + name)
             ?? typeof(Outbound).Assembly.GetType("Ms2.Net.Packets." + name);
        if (t == null) Assert.Fail($"Ms2.Net.{name} is not written yet at origin/main");
        var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Length == args.Length);
        if (ctor == null) Assert.Fail($"{t.FullName} has no constructor of {args.Length} arguments");
        return (IOutPacket)ctor.Invoke(args);
    }

    [Test]
    public void Mode4WritesTheUidAndCount()
    {
        var p = Packet("ItemInventoryDropRequest", 0x0102030405060708L, 5);
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestItemInventory));
        Assert.That(RcLane.Write(p), Is.EqualTo(RcLane.Bytes("04080706050403020105000000")));
    }

    [Test]
    public void Mode5WritesTheUid()
    {
        var p = Packet("ItemInventoryDestroyRequest", 0x0102030405060708L);
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestItemInventory));
        Assert.That(RcLane.Write(p), Is.EqualTo(RcLane.Bytes("050807060504030201")));
    }

    [Test]
    public void Mode9WritesTheTab()
    {
        var p = Packet("ItemInventoryExpandRequest", (byte)3);
        Assert.That(p.Op, Is.EqualTo(Outbound.RequestItemInventory));
        Assert.That(RcLane.Write(p), Is.EqualTo(RcLane.Bytes("0903")));
    }
}
