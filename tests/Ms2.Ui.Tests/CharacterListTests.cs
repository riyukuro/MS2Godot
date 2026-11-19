using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The character list movie (uicharacterlistdialog) run by its own ActionScript (needs MS2_DATA).</summary>
public class CharacterListTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static (UiHost, CharacterListController) Load(params LobbyCharacterRow[] rows)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var c = new CharacterListController();
        c.Characters.AddRange(rows);
        var (rt, p) = src.Open(c.Movie, r => { r.Budget = 20_000_000; r.FixedWallClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc); });
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 60; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c);
    }

    static LobbyCharacterRow Row(string name, string sn, string img) =>
        new(name, 1, 10, "Lv. 1", img, "2026/11/18", "Tria", sn);

    [Test]
    public void LobbyButtonsFadeInWithThePopup()
    {
        // LobbyDialog.popup() returns false once the root sits on the "popup" label (frame 2): the buttons' tween only
        // starts when OnLoadComplete runs with the timeline still on frame 1
        var (h, c) = Load(Row("A", "11", "d.png"));
        Assert.That(h.Get(h.Child("m_btns"), "alpha"), Is.EqualTo(1.0).Within(0.01), "Quit / Options buttons shown");
        Assert.That(h.Get(h.Entity, "m_btnShowTween"), Is.Not.Null);
    }

    [Test]
    public void ANewPictureReachesItsRow()
    {
        // vfunc_ugc_1 (@0x1418ed310): updateProfile(index, GetProfileImagePath(key, 1)) on the dialog, which hands it to the
        // row's CharacterRenderer (CharacterListDialog.updateProfile -> m_charlist.getRendererAt(index).updateProfile).
        const string none = "default.png", pic = "./Custom/Download/Profile/x_1_2.png";
        var (h, c) = Load(Row("A", "11", none), Row("B", "12", none));
        string ImgPath(int i)
        {
            var r = h.Invoke(h.Entity, "getCharacterRenderer", i);
            return r == null ? "<no renderer>" : Rt.ToString(h.Get(h.Get(r, "m_profile"), "imgpath"));
        }
        TestContext.WriteLine($"before: {ImgPath(0)} | {ImgPath(1)}");
        c.OnProfileImage(12, pic);
        h.Player.Tick();
        Assert.That(c.Characters[1].ImgPath, Is.EqualTo(pic));
        Assert.That(ImgPath(1), Does.EndWith(pic));
        Assert.That(ImgPath(0), Does.Not.EndWith(pic));
    }
}
