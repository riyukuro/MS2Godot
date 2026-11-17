using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Look;

namespace Ms2Godot.Ui;

/// <summary>Character list data for the lobby UI until the network layer feeds it: the same type the character list packet decodes to.</summary>
public static class StubCharacterList
{
    public static readonly Dictionary<int, string> JobNames = new()
    {
        [1] = "Knight", [10] = "Berserker", [20] = "Wizard", [30] = "Priest", [40] = "Archer", [50] = "Heavy Gunner",
        [60] = "Thief", [70] = "Assassin", [80] = "Rune Blade", [90] = "Striker", [100] = "Soul Binder",
    };

    /// <summary>
    /// The stub characters. As in the character list packet, JobCode is the job group (table/job.xml jobGroupID: 30 wizard,
    /// 80 assassin, 10 knight) and Job the job (the group's subJob1: 300, 800, 100), the key of string table 6 (jobname.xml).
    /// </summary>
    public static List<CharacterEntry> Build() => new()
    {
        Entry(1001, "Bigleafy", 62, 30, 300, 02000001, 1, new DateTime(2021, 3, 14)),
        Entry(1002, "Maplegirl", 41, 80, 800, 02000023, 1, new DateTime(2021, 6, 2)),
        Entry(1003, "Tiny Mushroom", 12, 10, 100, 02000001, 0, new DateTime(2022, 1, 9)),
    };

    static CharacterEntry Entry(long id, string name, short level, int group, int job, int map, byte gender, DateTime created) => new()
    {
        User = new UserBlock
        {
            CharacterId = id, Name = name, Level = level, Job = job, JobCode = group, MapA = map, Gender = gender,
            CreationTime = new DateTimeOffset(created, TimeSpan.Zero).ToUnixTimeSeconds(),
        },
    };

    /// <summary>
    /// Stub data only: a character without equips or skin colour wears the starter look of its gender and job group, the
    /// look the field dresses the player in (StarterLook), so the lobby preview and the field show the same character.
    /// </summary>
    public static void Dress(PackSet packs, IEnumerable<CharacterEntry> list)
    {
        ColorPalettes palettes = null;
        foreach (var e in list)
        {
            if (e.Equips.Count > 0) continue;
            palettes ??= new ColorPalettes(packs);
            var (items, skin) = StarterLook.Build(packs, palettes, e.User.Gender, e.User.JobCode);
            e.Equips.AddRange(items);
            if (e.User.Skin == default && skin is { } s) e.User.Skin = s;
        }
    }
}
