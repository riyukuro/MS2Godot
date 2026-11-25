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
/// The mesh swap on 02000284_bf (the buckets whose reactable model is another bucket, and the map whose entity has no model of
/// its own): the reactable model is hidden at the load and in state 0, drawn in place of the entity's own in state 1, both are
/// hidden in state 2, and state 0 brings the entity's own back.
/// The review fixes, each on a field of its own:
/// <list type="bullet">
/// <item>02000332_bf: an object whose own model animates (drawn by world/FieldProps) gets no reactable twin, so its state 1
/// cannot draw both models.</item>
/// <item>02000024_bf and 02000011_bf: the NIF collision follows NifAsset (FieldBuilder.InteractCollider): the own model's
/// shapes in state 0, asset B's in state 1, kept through state 2.</item>
/// <item>02000011_bf and 02000024_bf: a fade target (Transparency) with a reactable model: a fade of the own model ends at the swap, the
/// reactable model fades in state 1, and in state 2 the hidden draws are not picked (no zero-basis transform is inverted).
/// With a renderer (not --headless) the same ray picks the drawn model in state 0.</item>
/// <item>02000348_bf: an interact mesh hidden at the load is drawn from its registration (its vfunc_3(0) sets IsVisible).</item>
/// </list>
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
        draws = new FieldInteractDraws(fb.InteractMeshDraws, fb.Colliders, fb.InteractReactableDraws, fb.InteractColliders);
        AddChild(draws.Fader);
        live = new LiveInteractObjects(new Host { Draws = draws }, InteractObjectTable.Shared(packs));
        live.Attach(map.Entities);
        draws.Bind(live, fnpcs.InteractActors);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (live == null || fails.Count > 0) { Done(); return; }
        if (++frame < 3) return;
        try { Check(); CheckMeshSwap(); CheckWatchTowers(); }
        catch (Exception e) { fails.Add(e.ToString()); }
        foreach (var (name, check) in new (string, Action)[]
                 {
                     ("drawn apart", CheckDrawnApart), ("collision", CheckCollisionSwap), ("fade", CheckFade), ("hidden at the load", CheckHiddenAtLoad),
                 })
        {
            try { check(); }
            catch (Exception e) { fails.Add($"{name}: {e}"); }
        }
        Done();
    }

    sealed class Built
    {
        public FieldMap Map;
        public FieldBuilder Fb;
        public LiveInteractObjects Live;
        public FieldInteractDraws Draws;
        public FieldFade Fade;
        public void State(LiveInteractObject o, byte state) =>
            Live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = o.Name, State = state, Kind = o.Kind });
    }

    /// <summary>A field as Field builds it for these: the animated props taken by world/FieldProps, the fade, the interact draws
    /// with their reactable draws and collision.</summary>
    Built BuildField(string xblock)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var b = new Built { Map = FieldMap.Load(packs, new FlatLibrary(packs), xblock) };
        b.Fb = new FieldBuilder(packs, AssetIndex.For(packs), builder) { DrawsInstance = new FieldProps(builder).TakesModel };
        foreach (var _ in b.Fb.BuildSteps(b.Map)) { }
        AddChild(b.Fb.World);
        b.Fade = new FieldFade(b.Fb.FadeDraws, () => null, () => null);
        AddChild(b.Fade);
        b.Draws = new FieldInteractDraws(b.Fb.InteractMeshDraws, b.Fb.Colliders, b.Fb.InteractReactableDraws, b.Fb.InteractColliders) { Fade = b.Fade };
        AddChild(b.Draws.Fader);
        b.Live = new LiveInteractObjects(new Host { Draws = b.Draws }, InteractObjectTable.Shared(packs));
        b.Live.Attach(b.Map.Entities);
        b.Draws.Bind(b.Live, null);
        return b;
    }

    /// <summary>02000332_bf: no twin where the own model is drawn elsewhere (an animated prop), so nothing draws both.</summary>
    void CheckDrawnApart()
    {
        var b = BuildField("02000332_bf");
        var apart = b.Live.All.Where(o => o.AssetType == 1 && o.AssetB.StartsWith("urn:llid:") && o.AssetB != o.AssetA && b.Fb.DrawnElsewhere.Contains(o.Entity)).ToList();
        GD.Print($"interact draws check: 02000332_bf {apart.Count} objects with a reactable model whose own model is drawn elsewhere, {b.Fb.ReactableNotDrawn} reactable models not drawn, {b.Fb.ReactableTwins} twins");
        if (apart.Count == 0) { fails.Add("02000332_bf: no object whose own model is drawn elsewhere"); return; }
        foreach (var o in apart)
        {
            if (b.Fb.InteractReactableDraws.ContainsKey(o.Entity)) fails.Add($"{o.Name}: its own model is drawn elsewhere and it has a reactable twin (both would draw)");
            b.State(o, 1);
            if (b.Draws.ShownReactable(o.Entity) == true) fails.Add($"{o.Name}: state 1 draws a reactable model beside its own animated one");
        }
        if (b.Fb.ReactableNotDrawn < apart.Count) fails.Add($"02000332_bf: {b.Fb.ReactableNotDrawn} reactable models not drawn, fewer than {apart.Count}");
    }

    /// <summary>The NIF collision follows NifAsset: 02000024_bf (both models collide) and 02000011_bf (only asset B does).</summary>
    void CheckCollisionSwap()
    {
        foreach (var (xblock, wantA) in new[] { ("02000024_bf", true), ("02000011_bf", false) })
        {
            var b = BuildField(xblock);
            var list = b.Live.All.Where(o => b.Draws.Collision(o.Entity) is { B: not null }).ToList();
            int withA = list.Count(o => b.Draws.Collision(o.Entity).A != null);
            GD.Print($"interact draws check: {xblock} {b.Fb.ReactableColliders} reactable colliders, {list.Count} objects with them ({withA} with their own model's too)");
            if (list.Count == 0 || wantA && withA == 0 || !wantA && withA == list.Count) { fails.Add($"{xblock}: {list.Count} objects with reactable collision, {withA} with their own"); continue; }
            void Expect(LiveInteractObject o, string when, bool onB)
            {
                var c = b.Draws.Collision(o.Entity);
                if (c.A != null && (c.A.Body.CollisionLayer != 0) == onB) fails.Add($"{xblock} {o.Name}: {when}: its own model's shapes in the world {c.A.Body.CollisionLayer != 0}");
                if ((c.B.Body.CollisionLayer != 0) != onB) fails.Add($"{xblock} {o.Name}: {when}: asset B's shapes in the world {c.B.Body.CollisionLayer != 0}");
                if (c.B.Body.CollisionLayer != 0 && c.B.Body.CollisionLayer != 1u << c.B.Group) fails.Add($"{xblock} {o.Name}: {when}: asset B's layer {c.B.Body.CollisionLayer}, not its group {c.B.Group}");
            }
            foreach (var o in list)
            {
                if (o.State == 0) Expect(o, "the load", false);
                b.State(o, 1);
                Expect(o, "state 1", true);
                b.State(o, 2);
                Expect(o, "state 2", true);
                b.State(o, 0);
                Expect(o, "state 0", false);
            }
        }
    }

    /// <summary>02000011_bf and 02000024_bf: BGTransparencyMgr (world/FieldFade) on fade targets with a reactable model, with
    /// and without a model of their own.</summary>
    void CheckFade()
    {
        int own = 0, ownless = 0;
        foreach (var xblock in new[] { "02000011_bf", "02000024_bf" }) CheckFade(xblock, ref own, ref ownless);
        if (own == 0 || ownless == 0) fails.Add($"fade: {own} fade targets with both models, {ownless} with the reactable one only: both kinds wanted");
    }

    void CheckFade(string xblock, ref int ownCount, ref int ownlessCount)
    {
        var b = BuildField(xblock);
        bool rendered = DisplayServer.GetName() != "headless";
        var list = b.Live.All.Where(o => b.Fb.FadeDraws.ContainsKey(o.Entity) && b.Fb.InteractReactableDraws.ContainsKey(o.Entity)).ToList();
        var both = list.Where(o => b.Fb.InteractMeshDraws.ContainsKey(o.Entity)).ToList();
        ownCount += both.Count;
        ownlessCount += list.Count - both.Count;
        GD.Print($"interact draws check: {xblock} {list.Count} fade targets with a reactable model ({both.Count} with a model of their own), rendered {rendered}");
        if (list.Count == 0) return;
        bool IsOwn(LiveInteractObject o, FieldBuilder.FadeDraw d) => b.Fb.InteractMeshDraws[o.Entity].Any(x => x.Mm == d.Mm && x.Index == d.Index);
        bool IsB(LiveInteractObject o, FieldBuilder.FadeDraw d) => b.Fb.InteractReactableDraws[o.Entity].Any(x => x.Mm == d.Mm && x.Index == d.Index);
        foreach (var o in list)
        {
            var e = o.Entity;
            var at = FieldBuilder.ToGodotPoint(o.Position);
            Vector3 from = at + Vector3.Up * 5000, dir = Vector3.Down;
            if (b.Fb.InteractMeshDraws.ContainsKey(e))
            {
                if (rendered && !b.Fade.Picks(e, from, dir)) fails.Add($"{o.Name}: state 0: the ray down onto it picks nothing");
                b.Fade.FadeOutNow(e);
                var f = b.Fade.Fading(e);
                if (f.Count == 0 || f.Any(d => !IsOwn(o, d))) fails.Add($"{o.Name}: state 0: the fade holds {f.Count} draws, not its own model's");
            }
            b.State(o, 1);
            if (b.Fade.Fading(e).Count != 0) fails.Add($"{o.Name}: state 1: a fade of its own model goes on after the swap");
            if (b.Draws.ShownOwn(e) == true || b.Draws.ShownReactable(e) != true) fails.Add($"{o.Name}: state 1: own drawn {b.Draws.ShownOwn(e)}, reactable {b.Draws.ShownReactable(e)}");
            b.Fade.FadeOutNow(e);
            var fb1 = b.Fade.Fading(e);
            if (fb1.Count == 0 || fb1.Any(d => !IsB(o, d))) fails.Add($"{o.Name}: state 1: the fade holds {fb1.Count} draws, not asset B's");
            b.State(o, 2);
            if (b.Fade.Fading(e).Count != 0) fails.Add($"{o.Name}: state 2: a fade goes on while hidden");
            if (b.Fade.Picks(e, from, dir)) fails.Add($"{o.Name}: state 2: a hidden draw is picked");
            b.Fade.FadeOutNow(e);
            if (b.Fade.Fading(e).Count != 0) fails.Add($"{o.Name}: state 2: a hidden draw fades");
            b.State(o, 0);
        }
        GD.Print($"interact draws check: {xblock} {list.Count} fade targets through the fade in state 0, the swap, the fade in state 1 and state 2");
    }

    /// <summary>02000348_bf: an interact mesh hidden at the load, drawn once registered.</summary>
    void CheckHiddenAtLoad()
    {
        var b = BuildField("02000348_bf");
        var list = b.Live.All.Where(o => o.AssetType == 1 && o.Kind != InteractObjectMessage.KindWatchTower && !o.Entity.Bool("IsVisible", true)).ToList();
        GD.Print($"interact draws check: 02000348_bf {list.Count} interact meshes hidden at the load");
        if (list.Count == 0) { fails.Add("02000348_bf: no interact mesh hidden at the load"); return; }
        foreach (var o in list)
        {
            bool own = b.Fb.InteractMeshDraws.ContainsKey(o.Entity), twin = b.Fb.InteractReactableDraws.ContainsKey(o.Entity);
            GD.Print($"interact draws check: {o.Name}: own model drawn {own}, reactable twin {twin}");
            if (!own && !twin) fails.Add($"{o.Name}: hidden at the load and neither model has draws");
            if (own && b.Draws.ShownOwn(o.Entity) != true) fails.Add($"{o.Name}: hidden at the load and not drawn once registered");
            b.State(o, 2);
            if (b.Draws.Shown(o.Entity) != false) fails.Add($"{o.Name}: state 2 still drawn");
            b.State(o, 1);
            if (b.Draws.Shown(o.Entity) != true) fails.Add($"{o.Name}: state 1 not drawn");
            if (twin && b.Draws.ShownReactable(o.Entity) != true) fails.Add($"{o.Name}: state 1 does not draw its reactable model");
        }
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
        var meshes = live.All.Where(o => o.AssetType == 1 && o.Kind == InteractObjectMessage.KindMesh && draws.ShownOwn(o.Entity) != null).ToList();
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

    /// <summary>The MS2InteractMesh asset swap (CInteractMeshObject::vfunc_3 and FUN_1413ff690, interactobject.cpp:781-819 and
    /// :909-921) on the field's instances: which model draws in each state.</summary>
    void CheckMeshSwap()
    {
        var swaps = live.All.Where(o => o.AssetType == 1 && fb.InteractReactableDraws.ContainsKey(o.Entity)).ToList();
        int ownless = swaps.Count(o => !fb.InteractMeshDraws.ContainsKey(o.Entity));
        GD.Print($"interact draws check: {XBlock} {fb.ReactableTwins} reactable twins, {swaps.Count} objects with a reactable model ({ownless} with no model of their own)");
        if (swaps.Count == 0 || ownless == 0 || ownless == swaps.Count) { fails.Add($"{XBlock}: {swaps.Count} swaps, {ownless} with no own model: both kinds wanted"); return; }
        void Expect(LiveInteractObject o, string when, bool own, bool reactable)
        {
            // the instances' switch (TriggerFade.Switch): the headless renderer keeps no instance transforms to read back
            var a = draws.ShownOwn(o.Entity);
            var b = draws.ShownReactable(o.Entity);
            if (a != null && a != own) fails.Add($"{o.Name}: {when}: its own model drawn {a}, wanted {own}");
            if (b != reactable) fails.Add($"{o.Name}: {when}: its reactable model drawn {b?.ToString() ?? "none"}, wanted {reactable}");
        }
        foreach (var o in swaps)
        {
            if (o.State == 0) Expect(o, "the load", true, false);
            State(o, 1);
            Expect(o, "state 1", false, true);
            State(o, 2);
            Expect(o, "state 2", false, false);
            State(o, 1);
            Expect(o, "state 1 after 2", false, true);
            State(o, 2);
            State(o, 0);
            Expect(o, "state 0 after 2", true, false);
        }
        GD.Print($"interact draws check: {string.Join(", ", swaps.Select(o => o.Name[..8]))} through the load and states 1, 2, 1, 2, 0");
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
