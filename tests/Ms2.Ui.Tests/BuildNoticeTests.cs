using System.Collections.Generic;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>Unk_MyPCHousingSvc::ShowBuildNotice (@0x1410d6590, cubemapinformationservice_head.cpp:306), GetMessageSoundKey
/// (@0x1410d5bc0, :52) and GetMessageText (@0x1410d5d80, :177).</summary>
public class BuildNoticeTests
{
    /// <summary>GetMessageText's cases (:179-302) as the client lists them, 99 (the by-key case) left out; every other
    /// message takes the default 0x6c2.</summary>
    static readonly Dictionary<int, int> TextIds = new()
    {
        [0] = 0x691, [1] = 0x0, [2] = 0x692, [3] = 0x693, [4] = 0x694, [5] = 0x695, [6] = 0x696, [7] = 0x697,
        [8] = 0x698, [9] = 0x699, [10] = 0x69a, [11] = 0x69b, [12] = 0x69c, [13] = 0x69d, [14] = 0x69e, [15] = 0x69f,
        [16] = 0x6a0, [17] = 0x6a1, [18] = 0x6a2, [19] = 0x6a3, [20] = 0x6a4, [21] = 0x6a5, [22] = 0x6a6, [23] = 0x6a7,
        [24] = 0x6a8, [25] = 0x6a9, [26] = 0x6aa, [27] = 0x6ab, [28] = 0x6ac, [29] = 0x6ad, [30] = 0x6ae, [31] = 0x6af,
        [32] = 0x6b0, [33] = 0x6b1, [35] = 0x6b2, [36] = 0x6b3, [37] = 0x6b4, [38] = 0x6b5, [39] = 0x6b6, [40] = 0x6b7,
        [41] = 0x6b8, [42] = 0x6b9, [43] = 0x6ba, [44] = 0x6bb, [45] = 0x6bc, [46] = 0x6bd, [47] = 0x6be, [48] = 0x6bf,
        [49] = 0x6c0, [50] = 0x6c1, [51] = 0x6c2, [52] = 0x6c3, [53] = 0x6c4, [54] = 0x6c5, [55] = 0x6c6, [56] = 0x6c2,
        [57] = 0x6ca, [58] = 0x6cb, [59] = 0x6cc, [60] = 0x6c7, [61] = 0x6cd, [62] = 0x6ce, [63] = 0x6cf, [64] = 0x6d0,
        [65] = 0x6d1, [66] = 0x6d2, [67] = 0x6d3, [68] = 0x6d4, [69] = 0x6d5, [70] = 0x6d6, [71] = 0x6d7, [72] = 0x6d9,
        [73] = 0x6d8, [74] = 0x6da, [75] = 0x6db, [76] = 0x6dc, [77] = 0x6dd, [78] = 0x6de, [79] = 0x6e0, [80] = 0x6e1,
        [81] = 0x6df, [83] = 0xcaa, [84] = 0x6e3, [85] = 0x6e4, [86] = 0x6e5, [87] = 0xcab, [88] = 0xcac, [89] = 0xcad,
        [90] = 0x63, [91] = 0x64, [92] = 0x65, [93] = 0x66, [94] = 0xcae, [95] = 0xcaf, [96] = 0xcb0, [97] = 0xcb1,
        [98] = 0xcb5, [100] = 0xcb7, [101] = 0x67, [102] = 0x6ea, [103] = 0x68, [104] = 0x69, [105] = 0x6a, [106] = 0x6b,
        [107] = 0x6c, [108] = 0x6d, [109] = 0x6e, [110] = 0x6f, [111] = 0x70, [112] = 0x6c2,
    };

    [Test]
    public void TheTextIdIsTheClientsTableWithItsDefault()
    {
        for (int m = -2; m < 140; m++)
        {
            if (m == 99) continue;
            int want = TextIds.TryGetValue(m, out var id) ? id : 0x6c2;
            Assert.That(BuildNotice.TextId(m), Is.EqualTo(want), $"message {m}");
        }
        Assert.That(BuildNotice.TextKey(99), Is.EqualTo(BuildNotice.InstallNpcCountKey));
        Assert.That(BuildNotice.TextKey(0x39), Is.EqualTo(Ms2.Formats.Text.CommonStringIds.Key(0x6ca)));
    }

    static readonly (string Key, int[] Messages)[] Sounds =
    {
        (null, new[] { 0x0, 0xd, 0xe, 0x21, 0x23, 0x24, 0x25, 0x26, 0x27, 0x2a, 0x2e, 0x2f, 0x30, 0x31, 0x32, 0x33, 0x34, 0x36, 0x37, 0x38, 0x42, 0x47, 0x53, 0x57, 0x5e, 0x5f, 0x60, 0x61, 0x66, 0x70 }),
        ("system_notice_error_warningNoBuild", new[] { 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x8, 0x9, 0xa, 0xb, 0xc, 0x10, 0x13, 0x1e, 0x2b, 0x2c, 0x39, 0x3d, 0x3e, 0x40, 0x41, 0x49, 0x4b, 0x52, 0x5b, 0x5c, 0x5d, 0x63, 0x65, 0x67, 0x68, 0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f }),
        ("system_notice_error_warningOnlyWall", new[] { 0xf, 0x11 }),
        ("system_notice_error_warningNoWall", new[] { 0x12 }),
        ("system_notice_error_warningNoTearDown", new[] { 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x29, 0x4a }),
        ("system_notice_error_warningNotThisPlace", new[] { 0x1a, 0x20, 0x3b, 0x3c, 0x58, 0x5a }),
        ("system_notice_error_warningDefaultLand", new[] { 0x28 }),
        ("system_notice_error_warningDefaultLandRotation", new[] { 0x1b }),
        ("system_notice_error_warningNoChangeCube", new[] { 0x1c, 0x1d, 0x1f, 0x35, 0x3a, 0x3f, 0x48 }),
        ("system_notice_error_warningBuildingCount", new[] { 0x2d }),
    };

    [Test]
    public void TheSoundKeyIsTheClientsTableAndNoneOtherwise()
    {
        var listed = new HashSet<int>();
        foreach (var (key, messages) in Sounds)
            foreach (int m in messages)
            {
                listed.Add(m);
                Assert.That(BuildNotice.SoundKey(m), Is.EqualTo(key), $"message 0x{m:x}");
            }
        for (int m = -2; m < 0x100; m++)
            if (!listed.Contains(m)) Assert.That(BuildNotice.SoundKey(m), Is.Null, $"message 0x{m:x}");
    }

    sealed class Calls
    {
        public readonly List<string> Sounds = new(), Notices = new(), Keys = new();
        public string Text = "t";
        public BuildNotice.Outcome Show(int m) =>
            BuildNotice.Show(m, Sounds.Add, k => { Keys.Add(k); return Text; }, Notices.Add);
    }

    [Test]
    public void ANoticePlaysItsSoundThenShowsItsText()
    {
        var c = new Calls();
        Assert.That(c.Show(0x39), Is.EqualTo(BuildNotice.Outcome.Shown));
        Assert.That(c.Sounds, Is.EqualTo(new[] { BuildNotice.NoBuild }));
        Assert.That(c.Keys, Is.EqualTo(new[] { BuildNotice.TextKey(0x39) }));
        Assert.That(c.Notices, Is.EqualTo(new[] { "t" }));
    }

    [Test]
    public void ASilentMessageStillShowsItsText()
    {
        var c = new Calls();
        Assert.That(c.Show(0), Is.EqualTo(BuildNotice.Outcome.Shown));
        Assert.That(c.Sounds, Is.Empty);
        Assert.That(c.Notices, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnEmptyTextPlaysTheSoundAndShowsNothing()
    {
        var c = new Calls { Text = "" };
        Assert.That(c.Show(0x58), Is.EqualTo(BuildNotice.Outcome.Nothing));
        Assert.That(c.Sounds, Has.Count.EqualTo(1));
        Assert.That(c.Notices, Is.Empty);
    }

    [Test]
    public void MessageOneShowsNothingAndTheLackMeratBoxIsOpen()
    {
        var c = new Calls();
        Assert.That(c.Show(BuildNotice.None), Is.EqualTo(BuildNotice.Outcome.Nothing));
        Assert.That(c.Show(BuildNotice.LackMerat), Is.EqualTo(BuildNotice.Outcome.LackMeratOpen));
        Assert.That(c.Sounds, Is.Empty);
        Assert.That(c.Keys, Is.Empty);
        Assert.That(c.Notices, Is.Empty);
    }

    [Test]
    public void TheHeldItemErrorsAreTheLocalPcsNotice()
    {
        Assert.That(BuildNotice.OfHeldItemResponse(new ResponseCubeHeldItem(0x11, 0x13, 7), 7), Is.EqualTo(0x13));
        Assert.That(BuildNotice.OfHeldItemResponse(new ResponseCubeHeldItem(0x12, 0x2d, 7), 7), Is.EqualTo(0x2d));
        Assert.That(BuildNotice.OfHeldItemResponse(new ResponseCubeHeldItem(0x12, 0x2d, 8), 7), Is.Zero);
        Assert.That(BuildNotice.OfHeldItemResponse(new ResponseCubeHeldItem(0x11, 0, 7, 1, 2, 3), 7), Is.Zero);
        Assert.That(BuildNotice.OfHeldItemResponse(null, 7), Is.Zero);
    }

    [Test]
    public void ConfirmPlacementsNoticeStopsCarryTheirNotice()
    {
        Assert.That(CubeCraftState.NoticeOf(CubeCraftState.PlacementStop.BuildNotice), Is.EqualTo(0x39));
        Assert.That(CubeCraftState.NoticeOf(CubeCraftState.PlacementStop.NoTextureLook), Is.EqualTo(0x62));
        foreach (CubeCraftState.PlacementStop s in System.Enum.GetValues(typeof(CubeCraftState.PlacementStop)))
            if (s is not (CubeCraftState.PlacementStop.BuildNotice or CubeCraftState.PlacementStop.NoTextureLook))
                Assert.That(CubeCraftState.NoticeOf(s), Is.Zero, s.ToString());
    }
}
