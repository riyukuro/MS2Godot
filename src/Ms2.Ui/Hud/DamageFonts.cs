using System.Collections.Generic;
using System.Globalization;

namespace Ms2.Ui.Hud;

/// <summary>
/// GFxMovieDamage's gem damage fonts (m_90, gfxmoviedamage.cpp): the loaded uidamagefont_&lt;id&gt; movies by gem font id,
/// each with the server time it was last asked for.
/// </summary>
public sealed class DamageFonts
{
    /// <summary>UnloadExpiredFonts' nLifeTime (gfxmoviedamage.cpp:249, the second set: nMinCount 0, 60 s).</summary>
    public const long LifeTime = 60;

    public enum LoadResult { Rejected, Loaded, Restamped }

    readonly Dictionary<int, long> stamps = new();
    readonly Dictionary<int, string> names = new();

    /// <summary>
    /// LoadDamageFont (@0x140b61900, gfxmoviedamage.cpp:210): id 0 is refused; a font already held is restamped
    /// (FUN_140e0dc30); a new one is loaded and stamped. The client stamps a new font on its loadComplete; the port loads
    /// at once, so it stamps here.
    /// </summary>
    public LoadResult Load(int id, long now)
    {
        if (id == 0) return LoadResult.Rejected;
        bool had = stamps.ContainsKey(id);
        stamps[id] = now;
        return had ? LoadResult.Restamped : LoadResult.Loaded;
    }

    public bool Loaded(int id) => id != 0 && stamps.ContainsKey(id);

    /// <summary>PC::vfunc_66(n) (pc.cpp:1253-1258): only the emotion 1 asks for the type 2 gem's font, not any other battle
    /// emotion.</summary>
    public static bool StanceLoadsFont(int emotion) => emotion == 1;

    public int Count => stamps.Count;

    /// <summary>
    /// UnloadExpiredFonts (@0x140b61ce0, :249): every font whose stamp is set and stamp + 60 &lt; now is dropped and its id
    /// added to <paramref name="into"/> (for UnloadFont). nMinCount is 0, so all expired fonts go.
    /// </summary>
    public void Expire(long now, List<int> into)
    {
        int start = into.Count;
        foreach (var (id, stamp) in stamps)
            if (stamp != 0 && stamp + LifeTime < now) into.Add(id);
        for (int i = start; i < into.Count; i++) stamps.Remove(into[i]);
    }

    /// <summary>The font id as the AS key text (FUN_140719190), made once per id.</summary>
    public string Name(int id)
    {
        if (!names.TryGetValue(id, out var s)) names[id] = s = id.ToString(CultureInfo.InvariantCulture);
        return s;
    }
}
