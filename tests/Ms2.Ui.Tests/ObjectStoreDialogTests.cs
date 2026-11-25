using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The object store of a bank cube: <see cref="BankTable"/> (GetBankSlotCount), CStore::ResetId as OpenObjectStore calls it
/// (<see cref="StorageBook.ResetId"/>), the close request and <see cref="ObjectStoreDialogController"/> (UIObjectStoreDialog)
/// over the movie uiobjectstoredialog, with the use reply the servers' FunctionCubePacket would send in the cube's second state.
/// </summary>
public class ObjectStoreDialogTests
{
    static byte[] Bytes(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    [Test]
    public void TheBankRowsAreReadAndALaterRowReplaces()
    {
        var t = new BankTable(XDocument.Parse("<ms2><bankType id=\"1\" slotCount=\"6\" useCash=\"1\"/><bankType id=\"2\" slotCount=\"12\"/>" +
                                              "<bankType id=\"1\" slotCount=\"18\"/></ms2>"));
        Assert.That(t.Count, Is.EqualTo(2));
        Assert.That(t.Get(1), Is.EqualTo(new BankTable.Bank(1, 18, 0)));
        Assert.That(t.Get(3), Is.Null);
        Assert.That(new BankTable(XDocument.Parse("<root><bankType id=\"1\" slotCount=\"6\"/></root>")).Count, Is.EqualTo(0));
    }

    [TestCase(6, 6, 2, 12)]
    [TestCase(6, 5, 2, 0)]
    [TestCase(5, 6, 2, 0)]
    [TestCase(6, 6, 9, 0)]
    public void TheSlotCountIsTheBankOfATypeSixSubtypeSixItemsParam2(int type, int subtype, int param2, int slots)
    {
        var t = new BankTable(XDocument.Parse("<ms2><bankType id=\"2\" slotCount=\"12\"/></ms2>"));
        Assert.That(t.SlotCountOf(new ItemData { Type = type, Subtype = subtype, Param2 = param2 }), Is.EqualTo(slots));
        Assert.That(t.SlotCountOf(null), Is.EqualTo(0));
    }

    [Test]
    public void AnInstallBankTypeIsTheGms2BankWithoutATypeCheck()
    {
        var t = new BankTable(XDocument.Parse("<ms2><bankType id=\"2\" slotCount=\"12\"/><bankType id=\"31\" slotCount=\"24\"/></ms2>"));
        Assert.That(t.SlotCountOf(new ItemData { Type = 6, Subtype = 5, InstallBankType = 31 }), Is.EqualTo(24));
        Assert.That(t.SlotCountOf(new ItemData { Type = 6, Subtype = 6, Param2 = 2, InstallBankType = 31 }), Is.EqualTo(24));
    }

    [Test]
    public void ResetIdSizesTheStoreToTheBankOnlyWithAnOwner()
    {
        var b = new StorageBook();
        b.Apply(new StorageMessage.Begin(0, 0));
        b.Apply(new StorageMessage.Add(0, new EquipItem { Id = 20000001, Uid = 1, InventorySlot = 0, Amount = 1 }));
        Assert.That((b.Ready, b.Count), Is.EqualTo((true, 1)));
        b.ResetId(7, 12);
        Assert.That((b.Id, b.Capacity, b.SlotCount, b.Ready, b.Count), Is.EqualTo((0L, 12, 12, false, 0)));
        b.ResetId(0, 12);
        Assert.That((b.Capacity, b.SlotCount), Is.EqualTo((StorageBook.BaseCapacity, StorageBook.BaseCapacity)), "no owner: SetId's 0x24");
    }

    [Test]
    public void TheCloseIsModeSevenOnKmsAndSixOnGms2WithTheKey()
    {
        var r = new FunctionCubeStoreCloseRequest("4_66051");
        Assert.That(r.Op, Is.EqualTo(Outbound.FunctionCube));
        var key = Bytes(w => w.WriteUnicodeString("4_66051"));
        Assert.That(Bytes(w => r.Write(w, ProtocolProfile.Kms2525)), Is.EqualTo(new byte[] { 7 }.Concat(key).ToArray()));
        Assert.That(Bytes(w => r.Write(w, ProtocolProfile.Gms2)), Is.EqualTo(new byte[] { 6 }.Concat(key).ToArray()));
    }

    static List<int> ItemIds(PackSet packs)
    {
        var ids = new SortedSet<int>();
        var names = packs.List("Xml").ToList();
        var idAttr = new Regex("<item id=\"(\\d+)\"", RegexOptions.Compiled);
        foreach (var n in names.Where(n => n.StartsWith("itemdata/", StringComparison.Ordinal)))
            foreach (Match m in idAttr.Matches(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", n))))
                ids.Add(int.Parse(m.Groups[1].Value));
        foreach (var n in names.Where(n => n.StartsWith("item/", StringComparison.Ordinal)))
            if (Regex.Match(n, @"/(\d+)\.xml$") is { Success: true } m) ids.Add(int.Parse(m.Groups[1].Value));
        return ids.ToList();
    }

    /// <summary>The install items whose object is a bank cube (a FunctionUI cube of sub-kind 4).</summary>
    static List<(ItemData Item, ObjectData Object)> BankItems(PackSet packs, GameTables t) =>
        ItemIds(packs).Select(id => t.Items.Get(id)).Where(d => d != null && d.InstallObjCode != 0)
            .Select(d => (d, t.ObjectDataOf(d.InstallObjCode))).Where(x => x.Item2 is { Cube: { Enabled: true, ControlType: 3, UiFunction: 4 } }).ToList();

    // the servers' FunctionCubePacket: Add/Update (3) and the KMS / GMS2 UseFurniture (6 / 5)
    static void Reply(FunctionCubes t, ProtocolProfile profile, Action<ByteWriter> write, long me, out FunctionCubes.UseResult use)
    {
        var r = FunctionCubeReply.Read(new ByteReader(Bytes(write)), profile, t.IsNurturing);
        use = t.Apply(r, me);
    }

    sealed class Rig
    {
        public readonly List<IOutPacket> Sent = new();
        public readonly StorageBook Book = new();
        public ObjectStoreDialogController C;
        public UiHost H;
        public FunctionCubes.Cube Cube;
        public GameTables Tables;
    }

    /// <summary>A bank cube placed with an owner, switched to its second state and used by its owner: the use reply opens the
    /// store (OpenDialog 4) and the window loads over the reset store.</summary>
    static Rig Open(string variable, int which = 0)
    {
        var packs = TestData.Packs(RcGap.Data(variable));
        var r = new Rig { Tables = new GameTables(packs, "en") };
        var banks = BankItems(packs, r.Tables);
        if (banks.Count == 0) Assert.Fail("no bank cube item");
        var (item, obj) = banks[which % banks.Count];
        var cubes = new FunctionCubes();
        const long me = 7;
        r.Cube = cubes.Place(0x010203, item.Subtype, obj, item.Id, me, new Vector3(150, 150, 150));
        var profile = variable == "MS2_GMS2_DATA" ? ProtocolProfile.Gms2 : ProtocolProfile.Kms2525;
        Reply(cubes, profile, w => { w.WriteByte(3); w.WriteUnicodeString(r.Cube.Id); w.WriteInt(2); w.WriteByte(1); }, me, out _);
        Reply(cubes, profile, w => { w.WriteByte((byte)(profile.IsGms2 ? 5 : 6)); w.WriteLong(me); w.WriteUnicodeString(r.Cube.Id); w.WriteBool(true); }, me, out var use);
        Assert.That((use.Action, use.Dialog), Is.EqualTo((FunctionCubes.UseAction.OpenDialog, 4)), "the bank cube's use opens the object store");
        r.C = ObjectStoreDialogController.For(use.Cube, r.Book, r.Tables);
        r.C.Sender = new StorageSender(r.Book) { Send = r.Sent.Add };
        r.C.SendClose = r.Sent.Add;
        var src = TestData.Movies(packs);
        var (rt, p) = src.Open(r.C.Movie, x => x.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        r.H = new UiHost(p, r.C, new UiModel(r.Tables) { HasLocalPlayer = true });
        Assert.That(r.H.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        return r;
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void TheBankCubeOpensItsStoreWithTheItemsNameAndSlots(string variable)
    {
        var r = Open(variable);
        int slots = r.Tables.BankSlotCount(r.Cube.ItemId);
        Assert.That(slots, Is.GreaterThan(0), "the bank cube item has a bank");
        Assert.That(r.Book.Capacity, Is.EqualTo(slots), "ResetId with the owner: the bank's slot count");
        Assert.That((r.C.Key, r.C.ItemType), Is.EqualTo((r.Cube.Id, r.Cube.ItemId)));
        Assert.That(r.Sent.OfType<StorageLoadRequest>().Count(), Is.EqualTo(1), "OnLoadInit asks for the store that is not ready");
        Assert.That(Rt.ToString(r.H.Get(r.H.Child("m_name"), "text")), Is.EqualTo(r.Tables.ItemName(r.Cube.ItemId)));
        Assert.That(Rt.ToString(r.H.Get(r.H.Child("m_comment"), "text")), Is.EqualTo(r.Tables.ItemDescription(r.Cube.ItemId, "functionDescription") ?? ""));
        Assert.That(r.C.GetBagSlotBindings(ObjectStoreDialogController.Bag).Count(), Is.EqualTo(slots));
        Assert.That(r.C.GetBagSlotBindings(StoreDialogController.Bag), Is.Empty, "the bag is the object store's, not the bank window's");
        Assert.That(Rt.ToString(r.H.Get(r.H.Child("m_itemBag"), "bagId")), Is.EqualTo(ObjectStoreDialogController.Bag));
        TestContext.Progress.WriteLine($"{variable}: item {r.Cube.ItemId} '{r.Tables.ItemName(r.Cube.ItemId)}' {slots} slots");
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void SortAsksTheCloseAndTheWindowsCloseSendsTheKey(string variable)
    {
        var r = Open(variable);
        r.Sent.Clear();
        Assert.That(r.C.OnCallback("OnSort", Array.Empty<object>()), Is.True);
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new StorageCloseRequest(0) }));
        string key = r.C.Key;
        r.C.Close();
        Assert.That(r.Sent.Skip(1), Is.EqualTo(new IOutPacket[] { new FunctionCubeStoreCloseRequest(key) }), "the close, no bank sort");
        Assert.That((r.C.Key, r.C.ItemType), Is.EqualTo(("", 0)));
    }

    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void AStoredItemShowsInItsSlotAndWithdrawsLikeTheBanks(string variable)
    {
        var r = Open(variable);
        r.Book.Apply(new StorageMessage.Begin(0, 0));
        int id = new[] { 20000001, 20000002, 30000001 }.First(i => !string.IsNullOrEmpty(r.Tables.ItemName(i)));
        r.Book.Apply(new StorageMessage.Add(0, new EquipItem { Id = id, Uid = 5, InventorySlot = 2, Amount = 1 }));
        Assert.That(r.C.GetBinding(r.C.SlotOf(2)), Is.Not.Null);
        Assert.That(r.C.GetBinding(r.C.SlotOf(1)), Is.Null);
        Assert.That(r.C.IsDragSlot(StoreDialogController.SlotName(2)), Is.False);
        r.Sent.Clear();
        r.C.SlotDroppedOn(r.C.SlotOf(4), r.C.SlotOf(2), r.C);
        Assert.That(r.Sent, Is.EqualTo(new IOutPacket[] { new StorageMoveRequest(0, 5, 4) }), "the store's own move (mode 2)");
    }

    /// <summary>
    /// Every install item of MS2_DATA and MS2_GMS2_DATA whose object is a bank cube: the item has a bank with slots, a whole
    /// number of rows of 6, and the store reset with an owner takes that size. Prints the items and slot counts.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryBankCubeItemHasItsBankSize(string variable)
    {
        var packs = TestData.Packs(RcGap.Data(variable));
        var t = new GameTables(packs, "en");
        Assert.That(t.Banks.Count, Is.GreaterThan(0), "bankType.xml");
        var items = BankItems(packs, t);
        var bad = new List<string>();
        var sizes = new SortedDictionary<int, int>();
        foreach (var (item, _) in items)
        {
            int slots = t.BankSlotCount(item.Id);
            sizes[slots] = sizes.GetValueOrDefault(slots) + 1;
            if (slots <= 0 || slots % 6 != 0) { bad.Add($"{item.Id}: type {item.Type}/{item.Subtype} bank {item.Param2}/{item.InstallBankType} slots {slots}"); continue; }
            var b = new StorageBook();
            b.ResetId(1, slots);
            if (b.SlotCount != slots) bad.Add($"{item.Id}: store {b.SlotCount}");
        }
        TestContext.Progress.WriteLine($"{variable}: {t.Banks.Count} banks, {items.Count} bank cube items ({string.Join(", ", items.Select(x => x.Item.Id))}), slots " +
            string.Join(" ", sizes.Select(s => $"{s.Key}:{s.Value}")));
        Assert.That(bad, Is.Empty);
        Assert.That(items, Is.Not.Empty);
    }
}
