using Ms2.Net.Packets;

namespace Ms2Godot.Ui;

/// <summary>Character list data for the lobby UI until the network layer feeds it: the same type the character list packet decodes to.</summary>
public static class StubCharacterList
{
    public static readonly Dictionary<int, string> JobNames = new()
    {
        [1] = "Knight", [10] = "Berserker", [20] = "Wizard", [30] = "Priest", [40] = "Archer", [50] = "Heavy Gunner",
        [60] = "Thief", [70] = "Assassin", [80] = "Rune Blade", [90] = "Striker", [100] = "Soul Binder",
    };

    public static List<CharacterEntry> Build() => new()
    {
        Entry(1001, "Bigleafy", 62, 20, 02000001, 1, new DateTime(2021, 3, 14)),
        Entry(1002, "Maplegirl", 41, 70, 02000023, 1, new DateTime(2021, 6, 2)),
        Entry(1003, "Tiny Mushroom", 12, 1, 02000001, 0, new DateTime(2022, 1, 9)),
    };

    static CharacterEntry Entry(long id, string name, short level, int job, int map, byte gender, DateTime created) => new()
    {
        User = new UserBlock
        {
            CharacterId = id, Name = name, Level = level, Job = job, JobCode = job, MapA = map, Gender = gender,
            CreationTime = new DateTimeOffset(created, TimeSpan.Zero).ToUnixTimeSeconds(),
        },
    };
}
