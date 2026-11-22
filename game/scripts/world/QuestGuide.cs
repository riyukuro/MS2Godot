using System.Threading.Tasks;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2Godot.Net;

namespace Ms2Godot.World;

/// <summary>
/// The quest navigator as the client draws it (CQuestNavigator): the "Eff_Quest_Navigation" beam stands at the player and turns
/// toward the target, and the HUD shows the straight-line distance text. The target is the nearest npc of the tracked quest in
/// this field, or, when the quest's npc is in another field, the portal that leads toward it along the map graph
/// (Ms2.Quest: Floyd-Warshall over the portal links of FieldMetaData.xml). There is no quest data offline, so the tracked quest
/// is a stand-in: `--quest-npc ID` names the npc it points at.
/// </summary>
public partial class QuestGuide : Node3D
{
    const string EffectArchive = "Resource/Model/Effect";
    const string BeamEntry = "etc/eff_quest_navigation.nif";

    PackSet packs; FieldMap map; Node3D player; int fieldId; int npcId;
    FieldPortals portals; NpcTable npcs; TraceStrings strings;
    (FieldMeta meta, QuestMapGraph graph)? ready;
    EffectInstance beam; Label label;
    readonly List<(Vec3 pos, int id)> spawns = new();
    readonly List<TracePortal> tracePortals = new();
    double dbg;
    public TraceResult Last { get; private set; }

    public static QuestGuide Start(Node parent, PackSet packs, ModelBuilder builder, FieldMap map, Node3D player, int fieldId, int npcId)
    {
        if (npcId == 0 || player == null) return null;
        var g = new QuestGuide { Name = "QuestGuide", packs = packs, map = map, player = player, fieldId = fieldId, npcId = npcId };
        g.portals = new FieldPortals(map);
        foreach (var e in map.Entities)
            if (e.HasMixin("SpawnPointNPC"))
                foreach (int id in FieldNpcs.NpcIds(e)) { g.spawns.Add((e.Position, id)); break; }
        try { g.npcs = new NpcTable(packs, GameOptions.StringLanguage); } catch { }
        g.strings = QuestStrings.Load(packs, GameOptions.StringLanguage);
        parent.AddChild(g);
        try
        {
            // the effect scene runtime plays the NIF's own controllers (animated alpha); the long duration keeps it looping while tracked
            var scene = EffectScene.Load(NifFile.Load(builder.Spec, packs.Read(EffectArchive, BeamEntry)));
            g.beam = new EffectInstance(scene, builder, 1e7f, 1) { FileSpace = Transform3D.Identity };
            g.beam.Visible = false;
            g.AddChild(g.beam);
        }
        catch (Exception ex) { GD.PushWarning("quest beam: " + ex.Message); }
        var layer = new CanvasLayer { Layer = 50 };
        g.label = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center, };
        g.label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        g.label.OffsetTop = 56; g.label.OffsetBottom = 96;
        g.label.AddThemeFontSizeOverride("font_size", 22);
        g.label.AddThemeColorOverride("font_outline_color", Colors.Black);
        g.label.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(g.label);
        g.AddChild(layer);
        return g;
    }

    public override void _Process(double delta)
    {
        if (ready == null)
        {
            if (packs == null) { label.Text = strings.Initializing; return; }
            try { ready = QuestMapGraph.Shared(packs); }      // built on first use, shared with the quest log and the world map
            catch (Exception e) { GD.PushWarning("quest map graph: " + e.Message); packs = null; return; }
        }
        var (meta, graph) = ready.Value;
        var pp = FieldPortals.ToMs2(player.GlobalPosition);
        var me = (pp.X, pp.Y, pp.Z);

        // the tracked quest's target map: the nearest field holding the npc (TraceQuest)
        int targetMap = meta.NpcFields.TryGetValue(npcId, out var maps) ? graph.NearestMap(fieldId, maps) : 0;
        TraceTarget? inField = null;
        if (targetMap == fieldId)
        {
            double best = double.MaxValue;
            foreach (var (pos, id) in spawns)
            {
                if (id != npcId) continue;
                double d = QuestTrace.Distance(me, (pos.X, pos.Y, pos.Z));
                if (d < best) { best = d; inField = new TraceTarget(TraceKind.Npc, pos.X, pos.Y, pos.Z); }
            }
        }
        var list = tracePortals;     // refilled each frame, read only inside Trace below
        list.Clear();
        foreach (var p in portals.All)
            if (p.TargetField != 0) list.Add(new TracePortal(p.TargetField, p.Position.X, p.Position.Y, p.Position.Z, p.Visible));
        Last = targetMap == 0 ? null : QuestTrace.Trace(graph, fieldId, targetMap, inField, list, me, strings);
        if (Last == null) { if (beam != null) beam.Visible = false; label.Text = ""; return; }

        string who = npcs?.Find(npcId)?.Name ?? $"npc {npcId}";
        string where = Last.Kind == TraceKind.Portal ? "portal" : who;
        label.Text = $"{where}   {Last.DistanceText}";
        if ((dbg += delta) > 2) { dbg = 0; if (Ms2Godot.Net.LiveSession.LogPackets) GD.Print($"quest: {Last.Kind} yaw {Last.BeamYawDeg:F0} {Last.DistanceText} beam={(beam != null)}"); }
        if (beam != null)
        {
            beam.Visible = true;
            beam.GlobalPosition = player.GlobalPosition;
            beam.Rotation = new Vector3(0, Mathf.DegToRad((float)Last.BeamYawDeg - 90), 0);     // the client's yaw about MS2 Z (= Godot Y); the effect's arrow lies 90 degrees off the model axis (checked on screen)
        }
    }
}
