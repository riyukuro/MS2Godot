using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/interact_draws.tscn) of the interact objects' state draws (CInteractMeshObject::vfunc_1 and
/// vfunc_3, world/FieldInteractDraws) on 02000284_bf, a field with interact actors whose kfm is a map model, interact meshes
/// and rows whose collision differs by state.
/// What the player should see: each interact actor drawn and playing its InitialSequence; an object whose row has a normal
/// collision blocking with a box from the load; in state 1 the actor plays its reactable sequence and the box follows the
/// reactable collision; in state 2 the object is gone from view and keeps its box; a removed object is gone with its box.
/// Then the watch towers of 99999906 (lookout tower actors, CInteractWatchTowerObject): a state change while the player is far
/// leaves the tower's sequence; within 2000 it plays its state's sequence (on in state 0, off otherwise), and away it keeps it.
/// Events only: nothing runs per frame. Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class InteractDrawsCheck : Node
{
    const string XBlock = "02000284_bf", TowerXBlock = "99999906";
    readonly List<string> fails = new();
    FieldBuilder fb;
    FieldNpcs fnpcs;
    LiveInteractObjects live;
    FieldInteractDraws draws;
    ModelBuilder builder;
    int frame;

    public override void _Ready()
    {
        try { Build(); }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    void Build()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), XBlock);
        fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
        foreach (var _ in fb.BuildSteps(map)) { }
        AddChild(fb.World);
        var people = new Node3D { Name = "Characters" };
        AddChild(people);
        fnpcs = new FieldNpcs(packs, AssetIndex.For(packs), builder);
        fnpcs.PlaceActors(people, map);
        draws = new FieldInteractDraws(fb.InteractMeshDraws, fb.Colliders);
        AddChild(draws.Fader);
        live = new LiveInteractObjects(new Host { Draws = draws }, InteractObjectTable.Shared(packs));
        live.Attach(map.Entities);
        draws.Bind(live, fnpcs.InteractActors);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (live == null || fails.Count > 0) { Done(); return; }
        if (++frame < 3) return;
        try { Check(); CheckWatchTowers(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        Done();
    }

    void Done()
    {
        GD.Print($"interact draws check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
        SetPhysicsProcess(false);
    }

    void State(LiveInteractObject o, byte state) =>
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = o.Name, State = state, Kind = o.Kind });

    void Check()
    {
        var actors = live.All.Where(o => o.AssetType == 2 && o.Kind == InteractObjectMessage.KindMesh).ToList();
        var meshes = live.All.Where(o => o.AssetType == 1 && o.Kind == InteractObjectMessage.KindMesh && draws.Shown(o.Entity) != null).ToList();
        int drawn = actors.Count(o => fnpcs.InteractActors.ContainsKey(o.Entity));
        GD.Print($"interact draws check: {XBlock} {live.All.Count} objects, {actors.Count} actors ({drawn} drawn), {meshes.Count} meshes with instances, {draws.Boxes} boxes");
        if (actors.Count == 0 || meshes.Count == 0) { fails.Add("the field has no interact actor or mesh"); return; }
        if (drawn != actors.Count) fails.Add($"{actors.Count - drawn} interact actors not drawn");

        // the load: vfunc_3(0) on each, the box where the row's normal collision is set
        foreach (var o in live.All.Where(o => LiveInteractObjects.MeshDraw(o, out _, out _, out _)))
            if ((draws.Box(o) != null) != (o.Row.NormalCollision != 0)) fails.Add($"{o.Name}: box {draws.Box(o) != null} at the load, normal collision {o.Row.NormalCollision}");
        foreach (var o in actors.Where(o => fnpcs.InteractActors.ContainsKey(o.Entity)))
        {
            var a = fnpcs.InteractActors[o.Entity];
            if (!a.Root.Visible) fails.Add($"{o.Name}: actor hidden at the load");
            if (a.Sequence != o.AssetA) fails.Add($"{o.Name}: actor plays {a.Sequence ?? "nothing"} at the load, not {o.AssetA}");
        }

        // state 1: the reactable sequence and collision
        var swap = actors.FirstOrDefault(o => fnpcs.InteractActors.ContainsKey(o.Entity) && o.AssetB.Length > 0 && o.AssetB != o.AssetA
                                              && o.Row.NormalCollision != o.Row.ReactableCollision);
        if (swap == null) { fails.Add("no actor with two sequences and two collisions"); return; }
        var sa = fnpcs.InteractActors[swap.Entity];
        State(swap, 1);
        if (sa.Sequence != swap.AssetB) fails.Add($"{swap.Name}: state 1 plays {sa.Sequence}, not {swap.AssetB}");
        if ((draws.Box(swap) != null) != (swap.Row.ReactableCollision != 0)) fails.Add($"{swap.Name}: state 1 box {draws.Box(swap) != null}, reactable collision {swap.Row.ReactableCollision}");
        bool boxAt1 = draws.Box(swap) != null;

        // state 2: hidden, the box and the sequence as they were
        State(swap, 2);
        if (sa.Root.Visible) fails.Add($"{swap.Name}: state 2 still drawn");
        if ((draws.Box(swap) != null) != boxAt1 || sa.Sequence != swap.AssetB) fails.Add($"{swap.Name}: state 2 changed the box or the sequence");
        var m = meshes[0];
        State(m, 2);
        if (draws.Shown(m.Entity) != false) fails.Add($"{m.Name}: mesh state 2 still drawn");
        State(m, 0);
        if (draws.Shown(m.Entity) != true) fails.Add($"{m.Name}: mesh state 0 not drawn again");

        // back to 0, then the remove: hidden and the box gone
        State(swap, 0);
        if (!sa.Root.Visible || sa.Sequence != swap.AssetA) fails.Add($"{swap.Name}: state 0 not drawn with {swap.AssetA}");
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeRemove, Name = swap.Name });
        if (sa.Root.Visible || draws.Box(swap) != null) fails.Add($"{swap.Name}: still drawn or boxed after the remove");
        GD.Print($"interact draws check: {swap.Name} ({swap.AssetA} / {swap.AssetB}, collision {swap.Row.NormalCollision} / {swap.Row.ReactableCollision}) and mesh {m.Name} through states 1, 2, 0 and the remove");
    }

    /// <summary>The watch towers: CInteractWatchTowerObject::vfunc_3 and vfunc_11 (interactobject.cpp:1721-1773) through
    /// LiveInteractObjects.Tick and FieldInteractDraws.Apply.</summary>
    void CheckWatchTowers()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), TowerXBlock);
        var people = new Node3D { Name = "Towers" };
        AddChild(people);
        var tnpcs = new FieldNpcs(packs, AssetIndex.For(packs), builder);
        tnpcs.PlaceActors(people, map);
        var tdraws = new FieldInteractDraws(new Dictionary<FieldEntity, List<(MultiMesh Mm, int Index)>>(), null);
        AddChild(tdraws.Fader);   // freed with the scene
        var host = new Host { Draws = tdraws };
        var towers = new LiveInteractObjects(host, InteractObjectTable.Shared(packs));
        towers.Attach(map.Entities);
        tdraws.Bind(towers, tnpcs.InteractActors);
        var list = towers.All.Where(o => o.Kind == InteractObjectMessage.KindWatchTower && o.AssetType == 2).ToList();
        int drawn = list.Count(o => tnpcs.InteractActors.ContainsKey(o.Entity));
        GD.Print($"interact draws check: {TowerXBlock} {list.Count} watch towers ({drawn} drawn)");
        if (list.Count == 0 || drawn != list.Count) { fails.Add($"{TowerXBlock}: {list.Count} watch towers, {drawn} drawn"); return; }
        var t = list.First(o => o.AssetA != o.AssetB && o.AssetB.Length > 0);
        var a = tnpcs.InteractActors[t.Entity];
        void Expect(string what, string sequence)
        {
            if (a.Sequence != sequence) fails.Add($"{t.Name}: {what}: plays {a.Sequence ?? "nothing"}, not {sequence}");
            if (!a.Root.Visible) fails.Add($"{t.Name}: {what}: hidden");
        }
        void Set(byte state) =>
            towers.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = t.Name, State = state, Kind = t.Kind });
        var far = new Ms2.Formats.Model.Vec3(t.Position.X + 2000, t.Position.Y, t.Position.Z);
        var near = new Ms2.Formats.Model.Vec3(t.Position.X + 1999, t.Position.Y, t.Position.Z + 5000);
        Expect("the load", t.AssetA);
        Set(1);
        Expect("state 1 with no player", t.AssetA);
        host.HasPlayer = true;
        towers.Tick(far, false);
        Expect("state 1, 2000 away", t.AssetA);
        towers.Tick(near, false);
        Expect("state 1, near", t.AssetB);
        Set(0);
        Expect("state 0, near", t.AssetA);
        Set(2);
        Expect("state 2, near", t.AssetB);
        towers.Tick(far, false);
        Set(0);
        Expect("state 0 after going away", t.AssetB);
        GD.Print($"interact draws check: watch tower {t.Name} ({t.AssetA} / {t.AssetB}) near and far through states 1, 0, 2 and 0");
    }

    /// <summary>A host with no player unless set: no checks pass, nothing is sent.</summary>
    sealed class Host : IInteractHost
    {
        public bool HasPlayer { get; set; }
        public bool IsRestricted(int kind) => false;
        public int FieldId => 0;
        public ushort Channel => 1;
        public long NowMs => 0;
        public int PlayerStateId => 1;
        public bool PlayerRiding => false;
        public int LocalObjectId => -1;
        public bool ItemExists(int itemId) => true;
        public bool IsInteractObjectShutdown(int fieldId, int rowId) => false;
        public void FlushPlayerState() { }
        public float MasteryInteractionDistance => 300;
        public QuestMaskTable.QuestStatus QuestStatusOf(int questId) => QuestMaskTable.QuestStatus.Absent;
        public bool HasRequiredItem(uint code, int rank, uint consume) => false;
        public bool HasAdditionalEffect(uint effectId, ushort level) => false;
        public string AdditionalEffectName(uint effectId, ushort level) => "";
        public bool IsPartyLeader => false;
        public int MasteryGrade(int masteryType) => 0;
        public bool CanGatherRecipe(int recipeId) => false;
        public void Send(IOutPacket packet) { }
        public string Common(int id) => "";
        public string CommonByKey(string key) => "";
        public string ItemName(uint itemCode) => "";
        public string InteractActionName(int rowId) => "";
        public string InteractNoticeText(int nameId) => "";
        public string MasteryTypeName(int type) => "";
        public string MasteryGradeName(int type, int value) => "";
        public void SystemNotice(string text, int style = 1) { }
        public void ChatNotice(string text) { }
        public bool StartCasting(ICastingEventListener listener, string text, int durationMs) => false;
        public void CancelCasting() { }
        public void PlayPlayerAnim(string animName, bool loop) { }
        public void StopPlayerInteractAnim() { }
        public void OnObjectStateChanged(LiveInteractObject obj) => Draws?.Apply(obj);
        public void OnObjectRemoved(LiveInteractObject obj) => Draws?.Remove(obj);
        public void OnObjectSuccessEffect(LiveInteractObject obj, string effectAsset) { }
        public int OnOpenUiDialog(string swf, string value) => 0;
        public void OnGuideTrigger(int triggerType, int rowId) { }
        public void StillOpen(string what) { }
        public FieldInteractDraws Draws;
    }
}
