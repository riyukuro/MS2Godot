using System.Xml.Linq;

namespace Ms2.Audio;

/// <summary>
/// table/systemsound.xml: a UI sound key (button_click, dialog_open, dialog_close, ...) to the sound event the client plays
/// (SystemSoundProperty: group + soundID).
/// </summary>
public sealed class SystemSoundTable
{
    readonly Dictionary<string, (string group, string[] ids)> map = new(StringComparer.OrdinalIgnoreCase);

    public static SystemSoundTable Parse(string xml)
    {
        var t = new SystemSoundTable();
        foreach (var e in XDocument.Parse(xml).Root.Elements())
        {
            if (!e.Name.LocalName.Equals("systemSound", StringComparison.OrdinalIgnoreCase)) continue;   // the file spells one "systemSOund"
            var key = (string)e.Attribute("key");
            if (key == null) continue;
            var ids = ((string)e.Attribute("soundID") ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            t.map[key] = ((string)e.Attribute("group") ?? "", ids);
        }
        return t;
    }

    public int Count => map.Count;

    /// <summary>The event path ("MS2Sound/Sound/system/02000002") of a key, or null when the key has no sound.</summary>
    public string Resolve(string key)
    {
        if (!map.TryGetValue(key, out var v) || v.ids.Length == 0) return null;
        return v.group + "/" + v.ids[0];
    }
}

/// <summary>
/// Footsteps as the client plays them (CGroundEffectDataManager::PlayEffects): the ground's material name (the entity property
/// MapAttribute: grass, rock, ...) selects groundeffect/&lt;ground&gt;.xml, the action (walk, run0, run1, land) selects an
/// &lt;action&gt;, and every &lt;effects&gt; block of it picks one effect file by percent; the effect file's soundEvent effects are
/// the sounds.
/// </summary>
public sealed class GroundEffectTable
{
    sealed class Block { public List<string> Files = new(); public List<int> Percents = new(); }

    readonly Func<string, string> readText;
    readonly Dictionary<string, Dictionary<string, List<Block>>> grounds = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<string>> effectSounds = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="readXmlPack">reads an Xml.m2d entry by its lower-case path (e.g. "groundeffect/grass.xml"); null when missing</param>
    public GroundEffectTable(Func<string, string> readXmlPack) { readText = readXmlPack; }

    Dictionary<string, List<Block>> Ground(string name)
    {
        if (grounds.TryGetValue(name, out var g)) return g;
        g = null;
        var text = readText("groundeffect/" + name.ToLowerInvariant() + ".xml");
        if (text != null)
        {
            g = new(StringComparer.OrdinalIgnoreCase);
            foreach (var a in XDocument.Parse(text).Root.Elements("action"))
            {
                var type = (string)a.Attribute("type");
                if (type == null) continue;
                var blocks = new List<Block>();
                foreach (var eff in a.Elements("effects"))
                {
                    var b = new Block();
                    foreach (var e in eff.Elements("effect"))
                    {
                        b.Files.Add((string)e.Attribute("filePath") ?? "");
                        b.Percents.Add((int?)e.Attribute("percent") ?? 0);
                    }
                    blocks.Add(b);
                }
                g[type] = blocks;
            }
        }
        grounds[name] = g;
        return g;
    }

    /// <summary>The sound event paths (group/event) of an effect file's soundEvent parts.</summary>
    public List<string> SoundsOf(string file)
    {
        if (string.IsNullOrEmpty(file)) return new();
        if (effectSounds.TryGetValue(file, out var l)) return l;
        l = new();
        var text = readText("effect/" + file.ToLowerInvariant().Replace('\\', '/'));
        if (text != null)
            foreach (var e in XDocument.Parse(text).Descendants("effect"))
                if (string.Equals((string)e.Attribute("type"), "soundEvent", StringComparison.OrdinalIgnoreCase) && e.Attribute("event") != null)
                    l.Add(((string)e.Attribute("group") ?? "") + "/" + (string)e.Attribute("event"));
        return effectSounds[file] = l;
    }

    /// <summary>The sound event paths a step of the given action plays on the given ground (falls back to "default" for an unknown ground).</summary>
    public List<string> Pick(string ground, string action, Random rng = null) => PickFiles(ground, action, rng).SelectMany(SoundsOf).ToList();

    /// <summary>
    /// CGroundEffectDataManager::GetEffectFiles: one effect file per &lt;effects&gt; block of the action, picked by percent (the
    /// visual one and the sound one, e.g. default_e_land.xml and default_s_land.xml), "default" for an unknown ground.
    /// </summary>
    public List<string> PickFiles(string ground, string action, Random rng = null)
    {
        rng ??= Random.Shared;
        var g = Ground(string.IsNullOrEmpty(ground) ? "default" : ground) ?? Ground("default");
        var result = new List<string>();
        if (g == null || !g.TryGetValue(action, out var blocks)) return result;
        foreach (var b in blocks)
        {
            if (b.Files.Count == 0) continue;
            int pick = 0;
            if (b.Files.Count > 1)
            {
                int total = b.Percents.Sum();
                if (total <= 0) continue;
                int r = rng.Next(total);
                for (; pick < b.Percents.Count - 1 && r >= b.Percents[pick]; pick++) r -= b.Percents[pick];
            }
            if (!string.IsNullOrEmpty(b.Files[pick])) result.Add(b.Files[pick]);
        }
        return result;
    }
}
