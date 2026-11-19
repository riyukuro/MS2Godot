using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// A worn chat balloon gem's skin on its PC's balloon (CGemChatBalloon vfunc_6/vfunc_7, the PCGemEquip apply and remove)
/// on the uipopupimageballoon movie (needs MS2_DATA).
/// </summary>
public class GemEquipTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static UiHost Load(UiController c)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return h;
    }

    [Test]
    public void TheSkinIsTheGemIdInDecimal()
    {
        Assert.That(GemEquip.ChatBalloonSkin(30500123), Is.EqualTo("30500123"));
        Assert.That(GemEquip.ChatBalloonSkin(0), Is.EqualTo("0"));
    }

    [Test]
    public void AWornGemSkinsThePcBalloon()
    {
        long now = 1000;
        var c = new ChatBalloonController { TickMs = () => now, Duration = 5000 };
        var h = Load(c);
        var pc = new ChatBalloonController.Actor { ObjectId = 7, IsPc = true };
        bool system = false, canUse = true;
        var g = new GemEquip(c, pc) { GemSystem = () => system, CanUse = _ => canUse };
        // the model load: the items stored, then applied; a field without the gem system applies none
        g.Set(new[] { (GemEquip.ChatBalloonType, new GemEquip.Gem(1, 42)), (1, new GemEquip.Gem(2, 9)) });
        Assert.That(pc.BalloonGem, Is.True);
        g.ApplyAll();
        var e = c.PcBalloon(pc);
        Assert.That(e.Skin, Is.EqualTo(""));
        system = true;
        g.ApplyAll();
        Assert.That(e.Skin, Is.EqualTo("42"));
        // an item the PC cannot use: not applied, not worn
        canUse = false;
        g.Equip(GemEquip.ChatBalloonType, new GemEquip.Gem(3, 43));
        Assert.That(e.Skin, Is.EqualTo("42"));
        Assert.That(pc.BalloonGem, Is.False);
        Assert.That(g.Item(GemEquip.ChatBalloonType)?.GemId, Is.EqualTo(43));
        canUse = true;
        g.Equip(GemEquip.ChatBalloonType, new GemEquip.Gem(3, 43));
        Assert.That(e.Skin, Is.EqualTo("43"));
        Assert.That(pc.BalloonGem, Is.True);
        // the open keeps the skin while the gem is worn
        ChatBalloonController.SetText(e, "hi");
        c.Start(e);
        c.Process(_ => (500f, 400f));
        Assert.That(e.Opened?.Skin, Is.EqualTo("43"));
        // another type's removal leaves the balloon; the chat balloon gem's resets the skin
        g.Remove(1);
        Assert.That(e.Skin, Is.EqualTo("43"));
        g.Remove(GemEquip.ChatBalloonType);
        Assert.That(e.Skin, Is.EqualTo(""));
        Assert.That(pc.BalloonGem, Is.False);
        Assert.That(g.Item(GemEquip.ChatBalloonType), Is.Null);
        _ = h;
    }

    [Test]
    public void WithoutAGemTheOpenDropsTheSkin()
    {
        long now = 1000;
        var c = new ChatBalloonController { TickMs = () => now, Duration = 5000 };
        var h = Load(c);
        var pc = new ChatBalloonController.Actor { ObjectId = 8, IsPc = true };
        var e = c.PcBalloon(pc);
        ChatBalloonController.SetSkin(e, "55");
        ChatBalloonController.SetText(e, "hi");
        c.Start(e);
        c.Process(_ => (500f, 400f));
        Assert.That(e.Opened?.Skin, Is.EqualTo(""));
        _ = h;
    }
}
