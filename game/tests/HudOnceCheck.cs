using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/hud_once.tscn): the HUD (res://ui/hud.tscn) opened with a session (a LiveSession without a
/// connection, as the instance) makes one HUD model, so each live event its player watch subscribes to (the own character's
/// Changed, field property 5's UserTagSymbolApplied, OwnRideChanged, the key table's KeysSentToServer) reaches the HUD once
/// per model it keeps, no handler holds a model the HUD dropped, and the dialogs built with the model use the HUD's
/// (<see cref="HudScene.LiveWatchScripted"/>). Then the box dialogs that answer from the session open (the gacha machine,
/// the Lullu box, the treasure map, the multi open), the HUD leaves the tree for an empty scene (as a logout to character
/// select does), and no handler the HUD added to the session (its events and those of its members) may be left.
/// Quits with 0 when all of that holds, else 1.
/// </summary>
public partial class HudOnceCheck : Node
{
    public override void _Ready()
    {
        GetTree().Root.Size = new Vector2I(1280, 720);   // a headless window is 64x64
        typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, new Net.LiveSession());
        GetTree().Root.CallDeferred(Node.MethodName.AddChild, new HudOnceDriver());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://ui/hud.tscn");
    }
}

/// <summary>The check's driver, at the tree root so it outlives the scene changes.</summary>
public partial class HudOnceDriver : Node
{
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    int wait, stage;
    HudScene hud;
    string watch;
    bool watchOnce;
    HashSet<Delegate> before, during;
    readonly ulong startMs = Time.GetTicksMsec();

    void Finish(bool ok, string what)
    {
        if (Net.LiveSession.Instance is { } s && !s.IsInsideTree())
        {
            typeof(Net.LiveSession).GetProperty("Instance")!.SetValue(null, null);
            s.Free();
        }
        GD.Print("hud once: " + (ok ? "OK " : "FAIL ") + what);
        GetTree().Quit(ok ? 0 : 1);
        SetProcess(false);
    }

    /// <summary>Every handler on the session's events and on its members' events (one level), by event.</summary>
    static List<(string Event, Delegate Handler)> SessionHandlers(Net.LiveSession s)
    {
        var list = new List<(string, Delegate)>();
        void Of(object owner, string prefix, bool deeper)
        {
            for (var t = owner.GetType(); t != null && t != typeof(Node) && t != typeof(object); t = t.BaseType)
                foreach (var f in t.GetFields(All | BindingFlags.DeclaredOnly))
                {
                    if (f.IsLiteral || f.FieldType.IsValueType || f.FieldType == typeof(string)) continue;
                    var v = f.GetValue(owner);
                    // an event's handlers add up (+=); a hook field holds one handler, which the next HUD replaces
                    if (v is Delegate d)
                    {
                        string kind = t.GetEvent(f.Name, All | BindingFlags.DeclaredOnly) != null ? "event " : "hook ";
                        foreach (var h in d.GetInvocationList()) list.Add((kind + prefix + f.Name, h));
                        continue;
                    }
                    if (deeper && v != null && v is not GodotObject && v is not System.Collections.IEnumerable
                        && (v.GetType().Namespace ?? "").StartsWith("Ms2")) Of(v, prefix + f.Name + ".", false);
                }
        }
        Of(s, "", true);
        return list;
    }

    /// <summary>A handler the HUD's code made: on the HUD itself, a closure of its code, or a HUD dialog's controller.</summary>
    static bool HudOwned(Delegate d, HudScene hud) =>
        d.Target is { } t && (ReferenceEquals(t, hud) || t.GetType().DeclaringType == typeof(HudScene)
            || t.GetType().Namespace == "Ms2.Ui.Hud");

    void Call(string method, params object[] args)
    {
        try { typeof(HudScene).GetMethod(method, All)!.Invoke(hud, args); }
        catch (Exception e) { GD.Print($"hud once: {method}: {e.GetBaseException().Message}"); }
    }

    public override void _Process(double delta)
    {
        if (Time.GetTicksMsec() - startMs > 300_000) { Finish(false, $"timed out at stage {stage}"); return; }
        var live = Net.LiveSession.Instance;
        switch (stage)
        {
            case 0:
                before = SessionHandlers(live).Select(h => h.Handler).ToHashSet();
                stage = 1;
                return;
            case 1:
            {
                if (GetTree().CurrentScene is not HudScene h || !Field.LoadComplete) return;
                if (++wait < 30) return;
                hud = h;
                watch = hud.LiveWatchScripted(out watchOnce);
                // the box dialogs that take their answers from the session, opened as their items open them
                var tables = ((Ms2.Ui.UiModel)typeof(HudScene).GetField("hudModel", All)!.GetValue(hud)).Tables;
                foreach (var (method, id) in new[] { ("OpenGachaBoxDialog", 22001001), ("OpenLulluBoxDialog", 20300524) })
                    if (Ms2.Ui.Items.Item.Create(tables.Items, id, 1) is { } item) { item.Uid = 1; Call(method, item); }
                    else GD.Print($"hud once: no item {id} for {method}");
                Call("OpenTreasureMapDialog", "");
                Call("OpenItemMultiOpen", 20300013, 1, "", 2);
                wait = 0; stage = 2;
                return;
            }
            case 2:
            {
                if (++wait < 10) return;
                during = SessionHandlers(live).Select(x => x.Handler).ToHashSet();
                var empty = new PackedScene();
                empty.Pack(new Node());
                GetTree().ChangeSceneToPacked(empty);
                wait = 0; stage = 3;
                return;
            }
            case 3:
            {
                if (++wait < 10) return;
                var added = during.Where(d => !before.Contains(d) && HudOwned(d, hud)).ToHashSet();
                var left = SessionHandlers(live).Where(x => added.Contains(x.Handler)).ToList();
                var events = left.Where(x => x.Event.StartsWith("event ")).ToList();
                string List(IEnumerable<(string Event, Delegate Handler)> l) => string.Join(", ", l.GroupBy(x => x.Event).Select(g => $"{g.Key[(g.Key.IndexOf(' ') + 1)..]} {g.Count()}"));
                GD.Print($"hud once: hooks left on the session (one each, the next HUD's replace them): {left.Count - events.Count} [{List(left.Where(x => !x.Event.StartsWith("event ")))}]");
                bool ok = watchOnce && events.Count == 0;
                Finish(ok, $"{watch}; HUD handlers on the session {added.Count}, events left after the HUD closed {events.Count} [{List(events)}]");
                return;
            }
        }
    }
}
