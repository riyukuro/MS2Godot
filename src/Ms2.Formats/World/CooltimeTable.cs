using System.Globalization;
using System.Xml.Linq;

namespace Ms2.Formats.World;

/// <summary>
/// CooltimeManager (cooltimemanager.cpp), the cooldown gate of effect sounds: Table/CooltimeTable.xml's
/// &lt;v cooltimeID cooltimeTick/&gt; rows. A soundEvent part with a cooltimeID makes no sound while its id is cooling
/// (CSoundEventEffectParam::FUN_1417760d0, soundeventmgr.cpp:368-375, asked by CSoundEventEffect::vfunc_3 :463). The
/// client keeps one table for the session (CCooltimeManager::GetInstance), so a cooldown outlives a field change.
/// </summary>
public sealed class CooltimeTable
{
    readonly Dictionary<int, long> ticks = new();
    readonly Dictionary<int, long> ready = new();

    /// <summary>The ids with a cooldown.</summary>
    public int Count => ticks.Count;

    /// <summary>The cooldown of <paramref name="id"/> in ms (0 when it has none).</summary>
    public long TickOf(int id) => ticks.GetValueOrDefault(id);

    /// <summary>
    /// CooltimeManager::Load @0x140a40a40 (cooltimemanager.cpp:134-158) over the rows FUN_14028faa0 picked (one per
    /// cooltimeID, by feature and locale): a row whose id or tick is 0 is skipped, and the first row of an id is kept
    /// (std::map::insert).
    /// </summary>
    public static CooltimeTable From(IEnumerable<XElement> rows)
    {
        var t = new CooltimeTable();
        foreach (var e in rows)
        {
            uint.TryParse((string)e.Attribute("cooltimeID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id);
            uint.TryParse((string)e.Attribute("cooltimeTick"), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint tick);
            if (id == 0 || tick == 0 || id > int.MaxValue) continue;
            t.ticks.TryAdd((int)id, tick);
        }
        return t;
    }

    /// <summary>
    /// CooltimeManager::Use @0x140a40ca0 (cooltimemanager.cpp:161-182) as FUN_1417760d0 asks it: id 0 or an id with no
    /// row always plays; else it plays when <paramref name="nowMs"/> has reached the id's ready time, which then moves to
    /// now + the tick. A refused use leaves the ready time as it was.
    /// </summary>
    public bool Use(int id, long nowMs)
    {
        if (id == 0 || !ticks.TryGetValue(id, out long tick)) return true;
        if (ready.TryGetValue(id, out long at) && nowMs < at) return false;
        ready[id] = nowMs + tick;
        return true;
    }
}
