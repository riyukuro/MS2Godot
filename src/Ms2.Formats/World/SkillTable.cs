using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.World;

/// <summary>One motion of a skill level: the animation sequence it plays, its effect file and what the attack points name.</summary>
public sealed class SkillMotion
{
    public string SequenceName;          // e.g. Attack_01_A; the sequence of the actor's kfm / the player's kf
    public string MotionEffect;          // effect xml path (Xml pack effect/...), may be null
    public List<string> HitEffects = new();   // damage effectHit paths of its attack points
}

public sealed class SkillLevelRow
{
    public int Level;
    public List<SkillMotion> Motions = new();
}

/// <summary>Skill rows from the Xml pack (skilldata/*.xml): id -> levels -> motions. Only what the replay needs.</summary>
public sealed class SkillTable
{
    readonly Dictionary<int, List<SkillLevelRow>> skills = new();
    public int Count => skills.Count;

    public SkillTable(PackSet packs)
    {
        foreach (var entry in packs.List("Xml").Where(n => n.StartsWith("skilldata/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            XDocument x;
            try { x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿')); } catch { continue; }
            foreach (var s in x.Descendants("skill"))
            {
                if (!int.TryParse((string)s.Attribute("id"), out int id)) continue;
                var levels = new List<SkillLevelRow>();
                foreach (var l in s.Elements("level"))
                {
                    var row = new SkillLevelRow { Level = int.TryParse((string)l.Attribute("value"), out int lv) ? lv : 1 };
                    foreach (var m in l.Elements("motion"))
                    {
                        var mo = new SkillMotion { SequenceName = (string)m.Attribute("sequenceName"), MotionEffect = (string)m.Attribute("motionEffect") };
                        foreach (var d in m.Descendants("damage"))
                            if ((string)d.Attribute("effectHit") is { Length: > 0 } eh) mo.HitEffects.Add(eh);
                        row.Motions.Add(mo);
                    }
                    levels.Add(row);
                }
                skills[id] = levels;
            }
        }
    }

    /// <summary>The motion a cast plays: the level's row (the nearest level at or below, else the first) and the motion index from the packet.</summary>
    public SkillMotion Motion(int skillId, int level, int motionIndex)
    {
        if (!skills.TryGetValue(skillId, out var levels) || levels.Count == 0) return null;
        var row = levels.Where(r => r.Level <= level).OrderByDescending(r => r.Level).FirstOrDefault() ?? levels[0];
        if (row.Motions.Count == 0) row = levels.FirstOrDefault(r => r.Motions.Count > 0);
        if (row == null) return null;
        return row.Motions[Math.Clamp(motionIndex, 0, row.Motions.Count - 1)];
    }

    public bool Has(int skillId) => skills.ContainsKey(skillId);
}
