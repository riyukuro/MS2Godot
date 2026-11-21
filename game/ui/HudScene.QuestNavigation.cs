using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// CQuestNavigator (questnavigator.cpp) and UIQuestNavigation (uiquestnavigation.cpp) in the field's HUD: the navigator
/// keeps the tracked quest and its trace, the movie draws the markers over the player, the quest npcs, interact objects and
/// the portal on the path. Both start when the field's contents are up (the client's NotifyCreateMyPC, the object notifies,
/// NotifyCompleteMyPC), and each frame runs the navigator (FUN_140c99ee0) and then the movie (vfunc_15).
/// <para>
/// STILL-OPEN: no liftable is marked or traced (the field has no lift objects here); the beam effect is not drawn from the
/// navigator (the offline beam is <see cref="World.QuestGuide"/>); the tracked quest id is kept for the HUD's life only (the
/// game option Etc m_n30 is not stored); an interact object counts as active (CInteractObject m_nState == 1) whatever its
/// server state; an npc's anchor is its root raised by its model height, not its "Eff_UI" node.
/// </para>
/// </summary>
public partial class HudScene
{
    QuestNavigator questNavigator;
    QuestNavigationController questNavigation;
    Control questNavigationSlot;
    Action unwatchQuestNavigation;
    bool questNavigationOpened;                        // tried once per HUD: an open without a quest log is not retried each frame
    static int questNavigationTracked;                 // the tracked quest id across field moves (game option Etc m_n30)
    static QuestNavigationMap questNavigationMap;      // CQuestNavigationMap's trace table, built once per run
    readonly Dictionary<int, World.FieldNpcs.Placed> questNavigationNpcs = new();
    // the field's interact objects by name: their row id and position, read once at open (the entities do not move)
    readonly Dictionary<string, (int RowId, Vec3 Position)> questNavigationProps = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, World.FieldPortals.Portal> questNavigationPortalById = new();
    readonly List<TracePortal> questNavigationPortals = new();

    void QuestNavigationSlot(Control ui) =>
        ui.AddChild(questNavigationSlot = new Control { Name = "uiquestnavigation", MouseFilter = Control.MouseFilterEnum.Ignore });

    /// <summary>The field is up: the navigator and the movie are made and every object of the field gets its marker.</summary>
    void OpenQuestNavigation(World.FieldContents fc)
    {
        questNavigationOpened = true;
        var log = QuestLogSource();
        if (log == null || questTable == null) return;
        try { questNavigationMap ??= new QuestNavigationMap(questTable, fieldMeta, QuestMapGraph.Shared(assets.Packs).Graph); }
        catch (Exception e) { GD.PushWarning("quest navigation map: " + e.Message); }
        var live = Ms2Godot.Net.LiveSession.Instance;
        int FieldNow() => live?.FieldId is int f and not 0 ? f : fc.FieldId;
        var nav = questNavigator = new QuestNavigator(questTable, log.State)
        {
            Map = questNavigationMap,
            FieldId = FieldNow,
            PlayerPosition = QuestNavigationPlayer,
            NearestNpc = QuestNavigationNearestNpc,
            NearestInteract = QuestNavigationNearestInteract,
            Portals = () => questNavigationPortals,
            CanShowBeam = () => player != null,
            LoadTracked = () => questNavigationTracked,
            SaveTracked = id => questNavigationTracked = id,
        };
        try { nav.Strings = QuestStrings.Load(assets.Packs, GameOptions.StringLanguage); }
        catch (Exception e) { GD.PushWarning("quest navigation strings: " + e.Message); }

        var c = questNavigation = new QuestNavigationController
        {
            FieldId = FieldNow,
            CanShowBeam = () => player != null,
            PlayerPosition = () => QuestNavigationPlayer() is { } p ? new QuestNavigationController.Point((float)p.X, (float)p.Y, (float)p.Z) : null,
            NpcDead = id => questNavigationNpcs.TryGetValue(id, out var n) ? !n.Root.Visible : null,
            NpcAnchor = id => questNavigationNpcs.TryGetValue(id, out var n) ? NpcAnchorOf(n) : null,
            PropActive = name => questNavigationProps.ContainsKey(name) ? true : null,
            PropPosition = name => questNavigationProps.TryGetValue(name, out var e) ? new QuestNavigationController.Point((float)e.Position.X, (float)e.Position.Y, (float)e.Position.Z) : null,
            PortalPosition = id => questNavigationPortalById.TryGetValue(id, out var p) ? new QuestNavigationController.Point((float)p.Position.X, (float)p.Position.Y, (float)p.Position.Z) : null,
            PortalShown = id => questNavigationPortalById.TryGetValue(id, out var p) && p.Visible,
            Project = ProjectQuestNavigation,
            NpcNametagOffset = World.FieldContents.Constants(assets.Packs).Float("OffsetNpcNametag", 5),
        };
        // NotifyCreateMyPC: the tracked quest back
        nav.OnCreateMyPc();
        // the object notifies: OnAddNpc, OnAddInteract, OnAddPortal
        int offlineId = 0;
        foreach (var n in fc.SpawnedNpcs)
        {
            int id = n.ObjectId != 0 ? n.ObjectId : --offlineId;      // offline npcs have no object id: a negative key of their own
            questNavigationNpcs[id] = n;
            c.AddNpc(id, n.Row.Id);
        }
        foreach (var e in fc.Map?.Entities ?? Enumerable.Empty<FieldEntity>())
        {
            if (!e.HasMixin("MS2InteractObject") || string.IsNullOrEmpty(e.Name)) continue;
            int rowId = (int)e.Number("interactID");
            if (rowId <= 0) continue;
            questNavigationProps[e.Name] = (rowId, e.Position);
            c.AddInteract(e.Name, rowId, MinimapIcons.InteractRowOf(assets.Packs, rowId).type);
        }
        foreach (var p in fc.Portals.All)
        {
            questNavigationPortalById[p.Id] = p;
            if (p.TargetField != 0) questNavigationPortals.Add(new TracePortal(p.TargetField, p.Position.X, p.Position.Y, p.Position.Z, p.Visible));
            c.AddPortal(p.Id);
        }
        // NotifyCompleteMyPC: the player's marker, the beam and the context
        c.CompleteMyPc();
        nav.OnCompleteMyPc();

        var movie = new MovieDialog(assets, c, NewModel(), ScreenMode.PixelSize(GetWindow()));
        questNavigationSlot.AddChild(movie);

        if (live != null)
        {
            // UIQuestEventListener: slot 1 on a start, slot 3 on progress, slots 2 / 5 when the quest leaves, slot 6 on tracking
            Action<int> onStarted = id => { if (log.State.Get(id) is { } r) nav.OnQuestAccepted(r); };
            Action<int> onProgress = _ => nav.OnQuestProgress();
            Action<int> onChanged = id =>
            {
                if (id == 0) { nav.OnQuestProgress(); if (!nav.IsSelected(0) && log.State.Get(nav.Result.QuestId) is not { State: 1 }) nav.OnQuestDropped(nav.Result.QuestId); return; }
                if (log.State.Get(id) is { State: 1 } r) nav.OnQuestTracking(r);
                else nav.OnQuestDropped(id);
            };
            live.Quests.Started += onStarted;
            live.Quests.ProgressUpdated += onProgress;
            live.Quests.Changed += onChanged;
            unwatchQuestNavigation = () =>
            {
                live.Quests.Started -= onStarted;
                live.Quests.ProgressUpdated -= onProgress;
                live.Quests.Changed -= onChanged;
            };
        }
    }

    /// <summary>The frame: the navigator's trace, then the movie's markers from its result and context.</summary>
    void QuestNavigationTick()
    {
        if (questNavigator == null) return;
        questNavigator.Frame();
        questNavigation.Tick(questNavigator.Result, questNavigator.Context);
    }

    void CloseQuestNavigation()
    {
        unwatchQuestNavigation?.Invoke();
        unwatchQuestNavigation = null;
        questNavigation?.Unload();
        questNavigator = null;
        questNavigation = null;
    }

    (double X, double Y, double Z)? QuestNavigationPlayer()
    {
        if (player == null) player = FindPlayer(this);
        if (player == null) return null;
        var p = World.FieldPortals.ToMs2(player.GlobalPosition);
        return (p.X, p.Y, p.Z);
    }

    static QuestNavigationController.Point NpcAnchorOf(World.FieldNpcs.Placed n)
    {
        var p = World.FieldPortals.ToMs2(n.Position);
        return new QuestNavigationController.Point((float)p.X, (float)p.Y, (float)p.Z + n.Height);
    }

    /// <summary>WorldService::FindNearestNpc over the quest's npc ids: the closest shown npc of the field.</summary>
    (double X, double Y, double Z, float Dist)? QuestNavigationNearestNpc(HashSet<int> ids)
    {
        if (ids.Count == 0 || QuestNavigationPlayer() is not { } me) return null;
        (double, double, double, float)? best = null;
        float bestDist = float.MaxValue;
        foreach (var n in questNavigationNpcs.Values)
        {
            if (!n.Root.Visible || !ids.Contains(n.Row.Id)) continue;
            var p = World.FieldPortals.ToMs2(n.Position);
            float d = (float)QuestTrace.Distance(me, (p.X, p.Y, p.Z));
            if (d < bestDist) { bestDist = d; best = (p.X, p.Y, p.Z, d); }
        }
        return best;
    }

    /// <summary>WorldService::FindNearestInteractObject over the quest's interact ids.</summary>
    (double X, double Y, double Z, float Dist)? QuestNavigationNearestInteract(HashSet<int> ids)
    {
        if (ids.Count == 0 || QuestNavigationPlayer() is not { } me) return null;
        (double, double, double, float)? best = null;
        float bestDist = float.MaxValue;
        foreach (var e in questNavigationProps.Values)
        {
            if (!ids.Contains(e.RowId)) continue;
            var p = e.Position;
            float d = (float)QuestTrace.Distance(me, (p.X, p.Y, p.Z));
            if (d < bestDist) { bestDist = d; best = (p.X, p.Y, p.Z, d); }
        }
        return best;
    }

    /// <summary>The camera's projection for the markers: the stage point and the camera-pitch shift there.</summary>
    (float X, float Y, float Shift)? ProjectQuestNavigation(QuestNavigationController.Point pos)
    {
        var c = GetViewport().GetCamera3D();
        var godot = new Vector3(pos.X, pos.Z, -pos.Y);
        if (c == null || c.IsPositionBehind(godot)) return null;
        var p = c.UnprojectPosition(godot);
        field ??= FindField(this);
        return (p.X / u, p.Y / u, PlayerNameTag.PitchShift(field, godot));
    }
}
