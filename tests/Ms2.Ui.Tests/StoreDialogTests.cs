using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The bank dialog (uistoredialog) shown and filled from the store (needs MS2_DATA): initData's meso, the bag's slot
/// bindings by capacity, the "m_expand" count text, the item events, the name search and the idle per-frame cost. Texts
/// are not compared, so no language's strings.</summary>
public class StoreDialogTests
{
    static PackSet packs;
    static MovieSource src;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        return packs;
    }

    static UiModel NewModel()
    {
        var m = new UiModel(new GameTables(Packs(), "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(Packs(), f) };
        m.HasLocalPlayer = true;
        return m;
    }

    static UiHost Load(UiController c, UiModel model)
    {
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        return h;
    }

    static EquipItem Item(int id, long uid, short slot, int amount = 1) => new() { Id = id, Uid = uid, InventorySlot = slot, Amount = amount };

    static StorageBook NewBook(long meso = 0)
    {
        var b = new StorageBook();
        b.Apply(new StorageMessage.Begin(0, 0));
        b.Apply(new StorageMessage.Currency(meso));
        return b;
    }

    static int RealId(UiModel model)
    {
        int id = new[] { 20000001, 20000002, 30000001, 11300001, 12000001 }.FirstOrDefault(i => !string.IsNullOrEmpty(model.Tables.ItemName(i)));
        if (id == 0) Assert.Ignore("no known item");
        return id;
    }

    static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    [Test]
    public void TheMovieIsFilledFromTheStore()
    {
        Packs();
        var book = NewBook(1234567);
        book.Apply(new StorageMessage.Add(0, Item(20000001, 1, 0)));
        book.Apply(new StorageMessage.Add(0, Item(20000002, 2, 5)));
        var c = new StoreDialogController { Book = book };
        var h = Load(c, NewModel());
        c.Process();
        Assert.That(Digits(Rt.ToString(h.Get(h.Child("m_meso"), "text"))), Is.EqualTo("1234567"), "initData's meso");
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("2/36"), "count/capacity");
        Assert.That(c.Pending, Is.EqualTo(0));
    }

    [Test]
    public void TheBagHasOneSlotPerCapacityAndOnlyFilledSlotsBind()
    {
        Packs();
        var book = NewBook();
        var model = NewModel();
        book.Apply(new StorageMessage.Add(0, Item(RealId(model), 1, 3)));
        var c = new StoreDialogController { Book = book };
        Load(c, model);
        Assert.That(c.GetBagSlotBindings(StoreDialogController.Bag).Count(), Is.EqualTo(36));
        Assert.That(c.GetBinding(StoreDialogController.SlotName(3)), Is.Not.Null);
        Assert.That(c.GetBinding(StoreDialogController.SlotName(4)), Is.Null);
        book.Apply(new StorageMessage.Extend(6));
        Assert.That(c.GetBagSlotBindings(StoreDialogController.Bag).Count(), Is.EqualTo(42));
        Assert.That(c.Pending & StoreDialogController.PendingBag, Is.Not.EqualTo(0), "an expansion rebuilds the bag");
        c.Process();
        var h = c.Host;
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("1/42"));
    }

    [Test]
    public void ItemEventsRefreshTheCountText()
    {
        Packs();
        var book = NewBook();
        var c = new StoreDialogController { Book = book };
        var h = Load(c, NewModel());
        c.Process();
        book.Apply(new StorageMessage.Add(0, Item(20000001, 1, 0)));
        Assert.That(c.Pending, Is.EqualTo(StoreDialogController.PendingText));
        c.Process();
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("1/36"));
        book.Apply(new StorageMessage.Remove(0, 1));
        c.Process();
        Assert.That(Rt.ToString(h.Get(h.Child("m_expand"), "text")), Is.EqualTo("0/36"));
        book.Apply(new StorageMessage.Currency(77));
        Assert.That(Digits(Rt.ToString(h.Get(h.Child("m_meso"), "text"))), Is.EqualTo("77"));
    }

    [Test]
    public void TheCloseMessageClosesTheDialog()
    {
        Packs();
        var book = NewBook();
        var c = new StoreDialogController { Book = book };
        int closed = 0;
        c.Closed += () => closed++;
        Load(c, NewModel());
        book.Apply(new StorageMessage.Close());
        Assert.That(closed, Is.EqualTo(1));
        book.Apply(new StorageMessage.Close());
        Assert.That(closed, Is.EqualTo(1), "unsubscribed on close");
    }

    [Test]
    public void TheSearchScrollsToTheItemAndABoxAnswersNoMatch()
    {
        Packs();
        var model = NewModel();
        int id = RealId(model);
        var book = NewBook();
        book.Apply(new StorageMessage.Add(0, Item(id, 1, 20)));
        var boxes = new List<int>();
        var c = new StoreDialogController { Book = book, ShowBox = (_, t) => boxes.Add(t) };
        Load(c, model);
        Assert.That(c.FindSlotIndexByItemName("  " + model.Tables.ItemName(id).ToUpperInvariant() + " "), Is.EqualTo(20), "lowercased, trimmed");
        c.OnCallback("OnItemSearch", new object[] { "no such item name ☃" });
        Assert.That(boxes, Is.EqualTo(new[] { 0 }));
        boxes.Clear();
        c.OnCallback("OnItemSearch", new object[] { model.Tables.ItemName(id) });
        Assert.That(boxes, Is.Empty);
    }

    [Test]
    public void IdleProcessAllocatesNothing()
    {
        Packs();
        var c = new StoreDialogController { Book = NewBook() };
        Load(c, NewModel());
        for (int i = 0; i < 5000; i++) c.Process();     // past the tiered JIT's call counting
        // 100000 calls: one allocating call would cost 2.4 MB at the least; the Release runtime charges the thread a
        // constant (24 bytes) at some point in any long loop, so the bound is a few hundred bytes
        long a = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) c.Process();
        long window = GC.GetAllocatedBytesForCurrentThread() - a;
        Assert.That(window, Is.LessThan(256), "Process allocates per call");
    }
}
