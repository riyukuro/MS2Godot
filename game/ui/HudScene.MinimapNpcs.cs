using System.Collections.Generic;
using System.Globalization;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The minimap's npc proxies and named objects (<see cref="MinimapController.AddNpc"/>, <see cref="MinimapController.AddNamedObject"/>):
/// the field's npcs (offline its own spawns, live those of FieldAddNpc, then each add and remove), its interact objects
/// (offline the xblock's MS2InteractObject entities at the sandbox state, live WorldService's list with each state change and
/// remove), and the quest reads (the npc's quest state, the active quests' targets from the quest navigator's context).
/// </summary>
public partial class HudScene
{
    readonly Dictionary<Ms2.Ui.LiveInteractObject, MinimapController.InteractObject> minimapInteracts = new();
    readonly List<MinimapController.InteractObject> minimapInteractList = new();

    void MinimapNpcs(World.FieldContents fc, MinimapController c, Ms2.Ui.UiModel model)
    {
        var consts = World.FieldContents.Constants(assets.Packs);
        var live = Ms2Godot.Net.LiveSession.Instance;
        var talk = fc.Talk;
        Func<string, string> common = model.Tables.Common;
        c.NpcQuest = id => talk?.MinimapQuest(id, PlayerLevel, common) ?? NpcQuestMarks.Minimap.None;
        // CQuestNavigator::HasMap / HasNpc: the minimap opens before the navigator, so they read it when asked
        c.QuestTargetNpc = id => questNavigator?.Context.NpcIds.Contains(id) == true;
        c.QuestTargetObject = id => questNavigator?.Context.InteractIds.Contains(id) == true;
        c.ColorConstant = key => HexConstant(consts, key);
        c.OverlapCell = consts.Int("MinimapScaleSkipSplitPixel", 50);
        c.HouseField = HomeOptionHost.FieldIsHome(assets.Packs, fc.FieldId);

        int offlineId = 0;
        foreach (var n in fc.SpawnedNpcs)
            if (n.Row != null) c.AddNpc(MinimapNpc(n.ObjectId != 0 ? n.ObjectId : --offlineId, n));
        if (live != null)
            foreach (var n in live.LiveNpcs)
                if (n.Row != null) c.AddNpc(MinimapNpc(n.ObjectId, n));

        minimapInteracts.Clear();
        minimapInteractList.Clear();
        if (live != null)
        {
            c.InteractObjects = () =>
            {
                minimapInteractList.Clear();
                if (live.InteractObjects is { } all)
                    foreach (var o in all.All) minimapInteractList.Add(MinimapInteract(o));
                return minimapInteractList;
            };
            return;
        }
        foreach (var e in fc.Map?.Entities ?? (IEnumerable<FieldEntity>)System.Array.Empty<FieldEntity>())
        {
            if (!e.HasMixin("MS2InteractObject") || string.IsNullOrEmpty(e.Name)) continue;
            int rowId = (int)e.Number("interactID");
            if (rowId <= 0) continue;
            var (icon, type) = MinimapIcons.InteractRowOf(assets.Packs, rowId);
            minimapInteractList.Add(new MinimapController.InteractObject
            {
                Name = e.Name, RowId = rowId, Icon = icon, Type = type, State = () => data.InteractState, Position = e.Position,
            });
        }
        c.InteractObjects = () => minimapInteractList;
    }

    /// <summary>A live interact object as the minimap reads it, one wrapper per object.</summary>
    MinimapController.InteractObject MinimapInteract(Ms2.Ui.LiveInteractObject o)
    {
        if (minimapInteracts.TryGetValue(o, out var w)) { w.Position = o.Position; return w; }
        var (icon, type) = MinimapIcons.InteractRowOf(assets.Packs, o.RowId);
        w = new MinimapController.InteractObject { Name = o.Name, RowId = o.RowId, Icon = icon, Type = type, State = () => o.State, Position = o.Position };
        minimapInteracts[o] = w;
        return w;
    }

    /// <summary>A field npc as the minimap's proxy reads it: its row, name, point, dead state (CNpc IsDead) and IsProxyVisible.</summary>
    MinimapController.Npc MinimapNpc(int id, World.FieldNpcs.Placed n)
    {
        var last = default(Vec3);
        return new MinimapController.Npc
        {
            ObjectId = id,
            Row = n.Row,
            Name = Ms2.Ui.GameTables.Shared(assets.Packs, assets.Language).NpcName(n.Row.Id),
            Position = () => n.Root != null && IsInstanceValid(n.Root) ? last = World.FieldPortals.ToMs2(n.Position) : last,
            Dead = () => QuestNavigationNpcDead(id, n),
            Shown = () => n.Root != null && IsInstanceValid(n.Root) && n.Root.Visible,
        };
    }

    /// <summary>A colour constant ("0xa2ff8c" or "8cafff") as the client's uint; 0 when the tables do not set it.</summary>
    static uint HexConstant(ClientConstants k, string key)
    {
        var s = k.String(key)?.Trim();
        if (string.IsNullOrEmpty(s)) return 0;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    Action unwatchMinimapNpcs;

    /// <summary>OnFieldAddNpc / OnFieldRemoveNpc (vfunc_3 / vfunc_4 to the minimap) and the props' state changes
    /// (WorldService::FUN_141bc6af0: RefreshNamedObject) and removes (SSG_INTERACT_REMOVE: RemoveNamedObject).</summary>
    void WatchMinimapNpcs()
    {
        unwatchMinimapNpcs?.Invoke();
        unwatchMinimapNpcs = null;
        if (Net.LiveSession.Instance is not { } live) return;
        Action<World.FieldNpcs.Placed> added = n => { if (n.Row != null) minimapSide?.AddNpc(MinimapNpc(n.ObjectId, n)); };
        Action<int> removed = id => minimapSide?.RemoveNpc(id);
        Action<Ms2.Ui.LiveInteractObject> changed = o => minimapSide?.RefreshNamedObject(MinimapInteract(o));
        Action<Ms2.Ui.LiveInteractObject> gone = o => { minimapSide?.RemoveNamedObject(o.Name); minimapInteracts.Remove(o); };
        live.NpcAdded += added;
        live.NpcRemoved += removed;
        live.InteractChanged += changed;
        live.InteractGone += gone;
        unwatchMinimapNpcs = () =>
        {
            live.NpcAdded -= added;
            live.NpcRemoved -= removed;
            live.InteractChanged -= changed;
            live.InteractGone -= gone;
        };
    }
}
