using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>ShowEnterLimitMessage (uidungeonroomdialog.cpp:2226): the notice of FieldEntrance result 2. Ids and keys only, no language text.</summary>
public class EnterLimitNoticeTests {
    static readonly DungeonRoom Room = new(21020001, 0, 4, new int[0], LimitPlayerLevel: 55);
    static string Common(int id) => "c" + id.ToString("x");
    static string Key(string k) => "k:" + k;
    static string Text(int reason, DungeonRoom room) => EnterLimitNotice.Text(reason, room, Common, Key);

    [Test] public void AMissingRoomIsTheSameNoticeForEveryReason() {
        for (int reason = 0; reason < 25; reason++) Assert.That(Text(reason, null), Is.EqualTo("c99a"), "reason " + reason);
    }

    [Test] public void TheKeyedReasons() {
        Assert.That(Text(14, Room), Is.EqualTo("k:s_field_limit_disable_vip"));
        Assert.That(Text(15, Room), Is.EqualTo("k:s_field_limit_gearScore"));
        Assert.That(Text(16, Room), Is.EqualTo("k:s_field_limit_clear_dungeon"));
        Assert.That(Text(18, Room), Is.EqualTo("k:s_dungeon_enter_limit_recommend_weapon"));
    }

    [Test] public void TheLevelLimitFormatsTheRoomsLevel() {
        var text = EnterLimitNotice.Text(12, Room, id => id == 0x99b ? "level {0}" : "", Key);
        Assert.That(text, Is.EqualTo("level 55"));
    }

    [Test] public void AchievementAndEffectReasonsAreNotBuiltYet() {
        Assert.That(Text(13, Room), Is.Null);
        Assert.That(Text(17, Room), Is.Null);
    }

    [Test] public void OtherReasonsShowNothing() {
        foreach (var reason in new[] { 0, 1, 11, 19, 255 }) Assert.That(Text(reason, Room), Is.EqualTo(""), "reason " + reason);
    }
}
