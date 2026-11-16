using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>One row of the npc table (Xml pack: npcdata/*.xml, npc/environment): the parts the client's packet readers and the model loader need.</summary>
public sealed class NpcRow
{
    public int Id;
    public int Friendly, Class;
    public string[] PropertyTags = Array.Empty<string>();
    public string Kfm;                       // model name, e.g. 02010001_Stan
    public float Scale = 1f;
    public float WalkSpeed, RunSpeed;
    public string[] IdleActions = Array.Empty<string>();
    public string[] MoveActions = Array.Empty<string>();
    public int Gender;
    public float CapsuleHeight = 150f;
    public string Name;                      // from string/en/npcname.xml (or kr), null when absent

    /// <summary>CNpcData::IsBoss: friendly 0 and class 3 or more.</summary>
    public bool IsBoss => Friendly == 0 && Class >= 3;
    /// <summary>Property tag 5.</summary>
    public bool HasDamagedHpBarTag => PropertyTags.Contains("damaged_hp_upper_hp_bar");
}

/// <summary>All npc rows of the player's data, read lazily from the Xml pack.</summary>
public sealed class NpcTable
{
    readonly Dictionary<int, NpcRow> rows = new();
    public int Count => rows.Count;

    public NpcTable(PackSet packs)
    {
        foreach (var entry in packs.List("Xml").Where(n => n.StartsWith("npcdata/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument x;
            try { x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿')); } catch { continue; }
            foreach (var n in x.Descendants("npc"))
            {
                if (!int.TryParse((string)n.Attribute("id"), out int id)) continue;
                var env = n.Element("environment");
                if (env == null) continue;
                var model = env.Element("model");
                var row = new NpcRow
                {
                    Id = id,
                    Friendly = Int(env, "friendly"),
                    Class = Int(env, "class"),
                    Gender = Int(env, "gender"),
                    PropertyTags = ((string)env.Attribute("propertyTags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Kfm = (string)model?.Attribute("kfm"),
                    Scale = Flt(model, "scale", 1f),
                    WalkSpeed = Flt(model, "walkSpeed", 0),
                    RunSpeed = Flt(model, "runSpeed", 0),
                    CapsuleHeight = Flt(env.Element("capsule"), "height", 150f),
                };
                var normal = env.Element("normal");
                row.IdleActions = ((string)normal?.Attribute("action") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                rows[id] = row;
            }
        }
        LoadNames(packs);
    }

    /// <summary>Fills the row names from the string table.</summary>
    void LoadNames(PackSet packs)
    {
        foreach (var lang in new[] { "en", "kr" })
        {
            string entry = $"string/{lang}/npcname.xml";
            if (!packs.Exists("Xml", entry)) continue;
            var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('\uFEFF'));
            foreach (var k in x.Descendants("key"))
                if (int.TryParse((string)k.Attribute("id"), out int id) && rows.TryGetValue(id, out var row) && row.Name == null) row.Name = (string)k.Attribute("name");
        }
    }

    static int Int(XElement e, string a) => int.TryParse((string)e?.Attribute(a), out int v) ? v : 0;
    static float Flt(XElement e, string a, float def) => float.TryParse((string)e?.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;

    public NpcRow Find(int id) => rows.GetValueOrDefault(id);
}
