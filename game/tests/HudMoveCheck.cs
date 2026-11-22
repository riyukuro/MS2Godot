using System;
using System.Reflection;
using Godot;
using Ms2Godot.Ui;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/hud_move.tscn): field moves under the HUD keep the HUD and its dialogs, as the client's
/// CUIService and dialogs live for the process and a move only unloads the world (WorldService FUN_141bbc580). It opens the
/// HUD (res://ui/hud.tscn) on 02000001_tw_tria and runs ten moves through FieldContents.ServerMove between 2000062 and
/// 2000001 (the fourth started while the third's field still loads), checking after each that
/// <list type="bullet">
/// <item>the HUD node is the same and has one field scene child;</item>
/// <item>the dialogs the client keeps are the same instances (the target bar, top bar, quest tracker and their controllers,
/// the system text's controller);</item>
/// <item>the quest navigation is opened again for the new field: a new movie, the old one freed, no freed npc and the
/// new field's portals only;</item>
/// <item>what the client's move resets is reset: the damage numbers are cleared (UIDamageText::ClearAll), the screen mask
/// and system text movies are loaded again (UISceneGame vfunc_56 / vfunc_55), a system line shown during the load is
/// shown at its end, and one parked when the next move unloads the movie is forgotten (UISystemText::vfunc_3);</item>
/// <item>the window stays in window pixels on every frame (the canvas stretch never comes on);</item>
/// <item>the Godot object and node counts after the tenth move are at most 1% above the ones after the sixth (the same
/// field, two round trips later), counted after a full GC, with the loading screen's input guide off (see _Ready).</item>
/// <item>the quest navigation's frames (its nearest-npc search with the IsDead test, the portal IsShown sync and the
/// navigator) allocate nothing after the last move.</item>
/// </list>
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class HudMoveCheck : Node
{
    public override void _Ready()
    {
        // no input guide: each start with one adds a hidden guide to the kept loading screen, as the client's movie does
        // (FieldContents.ShowInputGuide), which the count below would take for a leak
        FieldContents.ShowInputGuide = false;
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new HudMoveDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene change to the HUD.</summary>
public partial class HudMoveDriver : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly int[] Route = { 2000062, 2000001, 2000062, 2000001, 2000062, 2000001, 2000062, 2000001, 2000062, 2000001 };
    HudScene hud;
    object targetBar, upperHud, questGuide, targetSide, systemSide, systemMovie, screenMask;
    int stage = -1, wait, failures;
    readonly ulong startMs = Time.GetTicksMsec();
    long objectsBefore, nodesBefore;

    // Godot objects that only managed wrappers still hold go when the GC runs, which made the raw count swing by 4-14k
    // between runs with the same nodes; collect first, so the count is what the scene holds
    static (long Objects, long Nodes) Census()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return ((long)Performance.GetMonitor(Performance.Monitor.ObjectCount), (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
    }
    bool parkedForDrop, stretchSeen;

    static object Get(object o, string name) => o?.GetType().GetField(name, Private)?.GetValue(o);
    // read by name, so the check also builds on a tree without them (where it fails at the first move)
    int Moves => hud.GetType().GetProperty("FieldMoves")?.GetValue(hud) is int m ? m : -1;
    static int DamageCount(object d) => d?.GetType().GetProperty("Count")?.GetValue(d) is int c ? c : -1;

    void Fail(string what) { failures++; GD.Print("hud move: FAIL " + what); }

    int FieldChildren()
    {
        int n = 0;
        for (int i = 0; i < hud.GetChildCount(); i++) if (hud.GetChild(i).SceneFilePath == "res://field.tscn") n++;
        return n;
    }

    bool Settled() => Field.LoadComplete && GetTree().GetFirstNodeInGroup(FieldContents.Group) is FieldContents { Portals: not null };

    public override void _Process(double delta)
    {
        // wall time, not frames: headless frames are unthrottled, so a frame budget ran out on a loaded machine
        if (Time.GetTicksMsec() - startMs > 480_000) { Fail($"timed out at stage {stage}"); Finish(); return; }
        if (stage == -1)
        {
            if (GetTree().CurrentScene is not HudScene h || !Settled()) return;
            if (++wait < 30) return;
            hud = h; wait = 0;
            targetBar = Get(hud, "targetBar"); upperHud = Get(hud, "upperHud"); questGuide = Get(hud, "questGuide");
            targetSide = hud.TargetBar; systemSide = hud.SystemText;
            Remember();
            StartMove();
            return;
        }
        if (hud == null || !IsInstanceValid(hud) || GetTree().CurrentScene != hud) { Fail("the HUD was freed by the move"); Finish(); return; }
        // the HUD's window-pixel scope lasts while it stays, and the loading screen's own scope stacks on it (ScreenMode)
        if (!stretchSeen && GetTree().Root.ContentScaleMode == Window.ContentScaleModeEnum.CanvasItems)
        { stretchSeen = true; Fail($"the canvas stretch came on during move {stage + 1}"); }
        // the last move is started while the third's field still loads: its swap forgets the line parked during that load
        if (stage == 2 && !parkedForDrop && !Field.LoadComplete && Get(hud, "systemText") == null)
        {
            parkedForDrop = true;
            hud.SystemText.ShowMessage("parked", Ms2.Ui.Hud.SystemTextController.Info, 0);
            if (hud.SystemText.Parked.Type == 0) Fail("a line shown while the system text is unloaded is not parked");
            stage = 3;
            movesAtSwapCheck = Moves;
            FieldContents.ServerMove(GetTree(), Route[3]);
            return;
        }
        if (stage == 3 && parkedForDrop && Get(hud, "systemText") == null && hud.SystemText.Parked.Type != 0 && SwappedSince())
            Fail("the parked line survived the next move's unload");
        if (stage == 0 && !Field.LoadComplete && Get(hud, "systemText") == null && hud.SystemText.Parked.Type == 0)
            hud.SystemText.ShowMessage("during load", Ms2.Ui.Hud.SystemTextController.Info, 0);     // shown at the load's end
        if (!Settled()) { wait = 0; return; }
        if (++wait < 30) return;
        wait = 0;
        Check();
        if (stage == 5) (objectsBefore, nodesBefore) = Census();
        if (stage == Route.Length - 1)
        {
            var (now, nodes) = Census();
            GD.Print($"hud move: objects after move 6 {objectsBefore} ({nodesBefore} nodes), after move {stage + 1} {now} ({nodes} nodes)");
            if (now - objectsBefore > objectsBefore / 100) Fail($"objects grew per move: {objectsBefore} -> {now}");
            if (nodes - nodesBefore > nodesBefore / 100) Fail($"nodes grew per move: {nodesBefore} -> {nodes}");
            var (bytes, npcs) = hud.QuestNavigationFrameBytes(1000);
            GD.Print($"hud move: quest navigation {bytes} B over 1000 frames, {npcs} npcs");
            if (npcs == 0) Fail("the quest navigation holds no npc to search");
            if (bytes != 0) Fail($"the quest navigation's frames allocate {bytes} B");
            Finish();
            return;
        }
        if (stage == 2) return;             // the fourth move starts during the third's load (above); here it has ended
        Remember();
        StartMove();
    }

    int movesAtSwapCheck;
    bool SwappedSince() => Moves > movesAtSwapCheck;

    void Remember()
    {
        systemMovie = Get(hud, "systemText");
        screenMask = Get(hud, "screenMask");
        var damage = (DamageText)Get(hud, "damageText");
        damage?.OnPlayer(100, 0);           // a number over the own PC, which the move clears
        if (DamageCount(damage) == 0) Fail("no damage number was shown before the move");
        movesAtSwapCheck = Moves;
        questNavigationMovie = hud.QuestNavigationState.Movie;
    }

    Node questNavigationMovie;

    void StartMove()
    {
        stage++;
        GD.Print($"hud move: move {stage + 1} -> {Route[stage]}");
        FieldContents.ServerMove(GetTree(), Route[stage]);
    }

    void Check()
    {
        int n = stage + 1;
        if (FieldChildren() != 1) Fail($"move {n}: {FieldChildren()} field scenes under the HUD");
        if (Moves != n) Fail($"move {n}: the HUD ran {Moves} field swaps");
        if (!ReferenceEquals(Get(hud, "targetBar"), targetBar) || !ReferenceEquals(Get(hud, "upperHud"), upperHud) ||
            !ReferenceEquals(Get(hud, "questGuide"), questGuide))
            Fail($"move {n}: a kept dialog movie was made again");
        if (!ReferenceEquals(hud.TargetBar, targetSide) || !ReferenceEquals(hud.SystemText, systemSide))
            Fail($"move {n}: a kept controller was made again");
        var sys = Get(hud, "systemText");
        if (sys == null || ReferenceEquals(sys, systemMovie)) Fail($"move {n}: the system text movie was not loaded again");
        var mask = Get(hud, "screenMask");
        if (mask == null || ReferenceEquals(mask, screenMask)) Fail($"move {n}: the screen mask was not loaded again");
        if (DamageCount(Get(hud, "damageText")) != 0) Fail($"move {n}: the damage numbers were not cleared");
        if (hud.SystemText.Parked.Type != 0) Fail($"move {n}: a parked system line was not shown at the load's end");
        // the quest navigation is made again for the new field, with none of the old field's npcs or portals
        var qn = hud.QuestNavigationState;
        int portals = GetTree().GetFirstNodeInGroup(FieldContents.Group) is FieldContents { Portals: { } fp } ? fp.All.Select(p => p.Id).Distinct().Count() : -1;
        if (!qn.Opened || qn.Movie == null || ReferenceEquals(qn.Movie, questNavigationMovie))
            Fail($"move {n}: the quest navigation was not opened again");
        if (questNavigationMovie != null && IsInstanceValid(questNavigationMovie) && !questNavigationMovie.IsQueuedForDeletion())
            Fail($"move {n}: the old field's quest navigation movie was kept");
        if (qn.FreedNpcs != 0) Fail($"move {n}: the quest navigation holds {qn.FreedNpcs} freed npcs");
        if (qn.Portals != portals) Fail($"move {n}: the quest navigation holds {qn.Portals} portals, the field {portals}");
        GD.Print($"hud move: move {n} checked, field {Get(hud, "xblock")}");
    }

    void Finish()
    {
        SetProcess(false);
        GD.Print($"hud move: {(failures == 0 ? "ok" : "FAIL")}, {failures} failures");
        // let the GC release the wrappers it still holds, so the quit does not report them as leaked
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        GetTree().Quit(failures == 0 ? 0 : 1);
    }
}
