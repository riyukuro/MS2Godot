using System.Globalization;

namespace Ms2.Formats.World;

/// <summary>
/// The field's npc spawn points as WorldService::FUN_141bcfff0 (worldservice_1.cpp:5872) finds them for an npc's spawn state:
/// the entities of the EventSpawnPointNPC model (m_208's list for 0xf9867c37, which holds every entity whose model is or mixes
/// in it, RemoveEntityFromModelList's ContainsModel), in field order; the first whose SpawnPointID (0xbf395749) is the id wins,
/// and an id of 0 is no point. Its SpawnAnimation (0x2ec0ee97) is the spawn state's sequence, Walk_A when it is empty.
/// </summary>
public sealed class FieldSpawnPoints
{
    /// <summary>The model the lookup lists (0xf9867c37).</summary>
    public const string Model = "EventSpawnPointNPC";
    /// <summary>The sequence of a point whose SpawnAnimation is empty.</summary>
    public const string EmptyAnimation = "Walk_A";

    readonly Dictionary<int, string> byId = new();

    /// <summary>The points of <paramref name="entities"/> (a field's, in its order).</summary>
    public FieldSpawnPoints(IEnumerable<FieldEntity> entities)
    {
        foreach (var e in entities)
        {
            if (!e.HasMixin(Model)) continue;
            int id = Id(e);
            if (id != 0) byId.TryAdd(id, Animation(e));
        }
    }

    /// <summary>The number of point ids in the field.</summary>
    public int Count => byId.Count;

    /// <summary>The spawn state's sequence for spawn id <paramref name="id"/>: null when the field has no such point.</summary>
    public string Find(int id) => id != 0 && byId.TryGetValue(id, out var a) ? a : null;

    /// <summary>The point's SpawnPointID (its own value, else the model's default); 0 when it is no whole number.</summary>
    public static int Id(FieldEntity e) =>
        int.TryParse(e.Get("SpawnPointID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0;

    /// <summary>The point's SpawnAnimation (its own value, else the model's default, Regen_A), or <see cref="EmptyAnimation"/> when empty.</summary>
    public static string Animation(FieldEntity e) => e.Get("SpawnAnimation") is { Length: > 0 } a ? a : EmptyAnimation;
}
