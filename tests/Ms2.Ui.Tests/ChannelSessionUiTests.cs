using System.Xml.Linq;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// Channel session UI and data sweep: SavedServerAddress (networkservice.cpp:577-588, scenestatemove.cpp:95-110),
/// GameToGame failure channel dropdown reset
/// (uisystemmenudialog.cpp:282, uiminimapentity.cpp:1385), and field channelGroupID + channelgroupname sweep across
/// KMS2_DEV, KMS2 and GMS2.
/// </summary>
[NonParallelizable]
public class ChannelSessionUiTests
{
    static IEnumerable<(string Label, string Dir, string Lang)> DataRoots()
    {
        var d1 = Environment.GetEnvironmentVariable("MS2_DATA");
        if (!string.IsNullOrEmpty(d1) && Directory.Exists(d1))
            yield return ("MS2_DATA", d1, RegionOf(d1));
        var d2 = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (!string.IsNullOrEmpty(d2) && Directory.Exists(d2) && !string.Equals(d1, d2, StringComparison.Ordinal))
            yield return ("MS2_GMS2_DATA", d2, "en");
    }

    static string RegionOf(string dir)
    {
        using var p = new PackSet(dir);
        return p.Exists("Xml", "string/kr/stringcommon.xml") ? "kr" : "en";
    }

    [Test]
    public void SavedServerAddressSavesOnlyNonZeroChannelAndRestoresOnceForKind11()
    {
        var saved = new SavedServerAddress();
        saved.NoteServerEnter(0, "192.0.2.1", 21001);
        bool restored = false;
        Assert.That(saved.TryRestore(SavedServerAddress.ChannelMoveKind, ref restored, out _, out _), Is.False, "channel 0 does not save");

        saved.NoteServerEnter(2, "192.0.2.10", 21002);
        saved.NoteServerEnter(0, "192.0.2.99", 21099);   // entering an unchanneled instance preserves the last channeled address
        Assert.That((saved.Host, saved.Port), Is.EqualTo(("192.0.2.10", (ushort)21002)));

        Assert.That(saved.TryRestore(0, ref restored, out _, out _), Is.False, "non-0x11 move does not restore");
        Assert.That(saved.TryRestore(SavedServerAddress.ChannelMoveKind, ref restored, out var h, out var p), Is.True);
        Assert.That((h, p, restored), Is.EqualTo(("192.0.2.10", (ushort)21002, true)));
        Assert.That(saved.TryRestore(SavedServerAddress.ChannelMoveKind, ref restored, out _, out _), Is.False, "restores only once per move");
    }

    [Test]
    public void GameToGameFailureResetsSelectedChannelInSystemMenuAndMinimapAcrossDataRoots()
    {
        var roots = DataRoots().ToList();
        if (roots.Count == 0) Assert.Ignore("MS2_DATA is not set");
        foreach (var (label, dir, lang) in roots)
        {
            using var packs = new PackSet(dir);
            var src = TestData.Movies(packs);
            var tables = new GameTables(packs, lang);

            // UISystemMenuDialog::vfunc_79 (uisystemmenudialog.cpp:282) -> SelectCurrentChannel
            var menu = new SystemMenuController
            {
                MapType = 0,
                ChannelWord = tables.Common("s_word_ch") ?? "CH",
                CurrentChannel = 2,
                Channels = new[] { new ChannelRow(1), new ChannelRow(2), new ChannelRow(3) },
                ChangeChannel = _ => true,   // request went out over the wire; server later refuses with GameToGame error != 0
            };
            var (rtMenu, pMenu) = src.Open(menu.Movie, r => r.Budget = 20_000_000);
            pMenu.StageW = 1280; pMenu.StageH = 720;
            var hMenu = new UiHost(pMenu, menu, new UiModel(tables));
            Assert.That(hMenu.Load(), Is.True, $"{label} sysmenu: " + string.Join("; ", rtMenu.Errors));
            for (int i = 0; i < 10; i++) pMenu.Tick();
            var list = hMenu.Child("m_channel");
            hMenu.Set(list, "visible", true);      // SystemMenuDialog.setSelectedChannel updates m_channel only while dropdown is open
            Assert.That(Rt.ToInt32(hMenu.Get(list, "selectedIndex")), Is.EqualTo(1), label);
            hMenu.Set(list, "selectedIndex", 2);   // user picked channel 3 (index 2)
            pMenu.Tick();
            Assert.That(Rt.ToInt32(hMenu.Get(list, "selectedIndex")), Is.EqualTo(2), label);
            menu.SelectCurrentChannel();           // GameToGame error != 0 resets back to CurrentChannel (2 -> index 1)
            pMenu.Tick();
            Assert.That(Rt.ToInt32(hMenu.Get(list, "selectedIndex")), Is.EqualTo(1), $"{label}: SelectCurrentChannel restored channel 2");
            Assert.That(rtMenu.Errors, Is.Empty, label);

            // UIMinimapDialog::vfunc_79 (uiminimapentity.cpp:1385) -> ApplySelectedChannel
            var mmModel = new UiModel(tables);
            mmModel.Field.Name = "Tria";
            mmModel.Field.MapId = 2000001;
            mmModel.Field.Channel = 2;
            mmModel.Field.Channels = new[] { 1, 2, 3 };
            var mm = new MinimapController { ChangeChannel = _ => true };
            var (rtMm, pMm) = src.Open(mm.Movie, r => r.Budget = 25_000_000);
            pMm.StageW = 1280; pMm.StageH = 720;
            var hMm = new UiHost(pMm, mm, mmModel);
            Assert.That(hMm.Load(), Is.True, $"{label} minimap: " + string.Join("; ", rtMm.Errors));
            for (int i = 0; i < 6; i++) pMm.Tick();
            mm.Notify();
            mm.ApplySelectedChannel();
            pMm.Tick();
            Assert.That(rtMm.Errors, Is.Empty, label);
        }
    }

    [Test]
    public void FieldChannelGroupsAndChannelStringsSweepAcrossEveryDataRoot()
    {
        var roots = DataRoots().ToList();
        if (roots.Count == 0) Assert.Ignore("MS2_DATA is not set");
        foreach (var (label, dir, lang) in roots)
        {
            using var packs = new PackSet(dir);
            var tables = new GameTables(packs, lang);

            // Every channel common string id / key used by channel change, server enter, and CSceneStateMove
            foreach (int sid in new[] { 0x579, ChannelChange.NoticeNotAllowed, ChannelChange.NoticeEmote, ChannelChange.NoticeDead, 0x2d7 })
            {
                string key = CommonStringIds.Key(sid);
                Assert.That(key, Is.Not.Null.And.Not.Empty, $"{label}: CommonStringIds 0x{sid:x}");
                Assert.That(tables.Common(key), Is.Not.Null.And.Not.Empty, $"{label}: stringcommon {key}");
            }
            Assert.That(CommonStringIds.Key(0x2dd), Is.EqualTo("s_mas_move_connect"), $"{label}: CommonStringIds 0x2dd");
            foreach (string key in new[] { "s_reconnect_to_server", "s_reconnect_to_server_fail" })
                Assert.That(tables.Common(key), Is.Not.Null.And.Not.Empty, $"{label}: stringcommon {key}");

            // Parse channelgroupname.xml when present
            var groupNames = new Dictionary<int, string>();
            string groupPath = $"string/{lang}/channelgroupname.xml";
            if (packs.Exists("Xml", groupPath))
            {
                var doc = PackXml.Parse(packs.Read("Xml", groupPath));
                foreach (var el in doc.Root?.Elements("key") ?? Enumerable.Empty<XElement>())
                    if ((int?)el.Attribute("id") is int gid)
                        groupNames[gid] = (string)el.Attribute("name") ?? "";
                Assert.That(groupNames, Is.Not.Empty, $"{label}: {groupPath}");
            }

            // Sweep every field in FieldData.Document: ChannelGroupId fits the 8-entry DynamicChannel class table (0..7)
            var fieldDoc = FieldData.Document(packs);
            int fieldCount = 0;
            var classTable = new ushort[] { 10, 20, 30, 40, 50, 60, 70, 80 };
            foreach (var el in fieldDoc.Root!.Elements("fieldData"))
            {
                if ((int?)el.Attribute("id") is not int id) continue;
                var fd = FieldData.FindById(packs, id);
                if (fd == null) continue;
                fieldCount++;
                Assert.That(fd.ChannelGroupId, Is.InRange(0, classTable.Length - 1), $"{label}: field {id} channelGroupID {fd.ChannelGroupId}");
                int limit = classTable[(uint)fd.ChannelGroupId < (uint)classTable.Length ? fd.ChannelGroupId : 0];
                var built = ChannelList.Build(new[] { 1, 5, 15, 25 }, selected: 1, limit: limit, gmLevel: 0);
                Assert.That(built, Does.Contain(1), $"{label}: field {id}");
            }
            Assert.That(fieldCount, Is.GreaterThan(100), $"{label}: swept field count");
        }
    }
}
