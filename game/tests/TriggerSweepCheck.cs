using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Ms2.Net;
using Ms2.Net.Wire;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless trigger sweep (res://tests/trigger_sweep.tscn): every packet the Maple2 server's trigger actions end in
/// (<see cref="TriggerSweepRoutes"/>) goes through the live session's packet loop (LiveSession.DrainGame), as the server wrote
/// it. Each writer comes out as one of:
/// <list type="bullet">
/// <item>applied: a branch of Handle took it, read it whole and marked nothing as not applied;</item>
/// <item>not applied: read, then logged as not applied (ReadNotApplied or TriggerNotApplied);</item>
/// <item>read fault: the reader threw on the server's bytes (a layout gap);</item>
/// <item>apply fault: read, then the apply threw for want of a field or a player (headless only, not a gap);</item>
/// <item>unhandled: no branch took the opcode;</item>
/// <item>tracker: no branch took it, but the field's actor tracker does (FieldTracker.Inbound, which runs before the chain in
/// the game);</item>
/// <item>needs actor: read, and the Ui trigger's emotion or face (subs 7-9, TriggerObjects.Ui.cs:100) went to no pc or npc, as a
/// headless session has none (the game hands them to the own pc and the npcs, LiveSession.OnTrigger and TriggerFace);</item>
/// <item>probe handled / probe unhandled: a writer that needs live server objects has no body here, so an empty body only
/// shows whether a branch takes the opcode.</item>
/// </list>
/// An action's state is its worst writer's. The check fails on a dropped action (not applied, read fault, unhandled or probe
/// unhandled) that <see cref="TriggerSweepGaps.Known"/> does not list, and on a listed one that is no longer dropped. With
/// MS2_SWEEP_OUT set it writes the per-writer and per-action tables there. Quits with 0 when all hold, else 1.
/// </summary>
public partial class TriggerSweepCheck : Node
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The opcodes FieldTracker.Inbound takes (src/Ms2.Net/Replay/FieldTracker.cs:111-250): the users, npcs and pets
    /// of the field, their moves, casts, stats and states.</summary>
    static readonly Inbound[] Tracked =
    {
        Inbound.ServerEnter, Inbound.RequestFieldEnter, Inbound.FieldAddUser, Inbound.FieldRemoveUser, Inbound.FieldAddNpc,
        Inbound.FieldRemoveNpc, Inbound.FieldDeadNpc, Inbound.FieldAddPet, Inbound.FieldRemovePet, Inbound.NpcControl,
    };

    public enum State { Applied, ApplyFault, Tracker, NeedsActor, ProbeHandled, NotApplied, ReadFault, Unhandled, ProbeUnhandled }

    public static bool Dropped(State s) => s >= State.NotApplied;

    /// <summary>The Ui trigger subs that need a pc or an npc of the packet's object id: the emotion list (7), the timed emotion
    /// (8) and the face emotion (9).</summary>
    static readonly string[] ActorNotes = { "Trigger:0x9/7", "Trigger:0x9/8", "Trigger:0x9/9" };

    /// <summary>The reader's faults on a short or bad body: a layout gap, not an apply fault.</summary>
    static bool IsReadFault(string fault) => fault.Contains("PacketLayoutException") || fault.Contains("EndOfStream")
        || fault.Contains("ArgumentOutOfRange") || fault.Contains("Not enough space in packet");

    public override void _Ready()
    {
        var fails = new List<string>();
        var ls = new LiveSession();
        var game = new TcpTransport();
        typeof(LiveSession).GetField("game", Private)!.SetValue(ls, game);
        var drain = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), ls, typeof(LiveSession).GetMethod("DrainGame", Private)!);
        var unhandled = new HashSet<ushort>();
        typeof(LiveSession).GetField("UnhandledOpcodes", Private)!.SetValue(ls, unhandled);
        var readNot = (HashSet<(string, int)>)typeof(LiveSession).GetField("packetsNotApplied", Private)!.GetValue(ls)!;
        var trigNot = (HashSet<int>)typeof(LiveSession).GetField("triggersNotApplied", Private)!.GetValue(ls)!;
        var tracked = Tracked.Select(game.Profile.Number).ToHashSet();
        Listen(ls);

        var byWriter = new Dictionary<string, (State State, string Detail)>();
        foreach (var b in TriggerSweepRoutes.Bodies)
        {
            int faults = ls.PacketFaults;
            unhandled.Clear(); readNot.Clear(); trigNot.Clear();
            game.Incoming.Enqueue(new RawPacket(b.Opcode, b.Hex == null ? System.Array.Empty<byte>() : System.Convert.FromHexString(b.Hex)));
            drain();
            string fault = ls.PacketFaults > faults ? ls.LastPacketFault : null;
            string notes = string.Join(" ", readNot.Select(x => $"{x.Item1}:{x.Item2}").Concat(trigNot.Select(x => $"Trigger:0x{x >> 8:x}/{x & 0xff}")));
            State s;
            if (unhandled.Contains(b.Opcode)) s = tracked.Contains(b.Opcode) ? State.Tracker : b.Hex == null ? State.ProbeUnhandled : State.Unhandled;
            else if (b.Hex == null) s = State.ProbeHandled;
            else if (fault != null) s = IsReadFault(fault) ? State.ReadFault : State.ApplyFault;
            else if (notes.Length > 0) s = notes.Split(' ').All(n => ActorNotes.Contains(n)) ? State.NeedsActor : State.NotApplied;
            else s = State.Applied;
            byWriter[b.Writer] = (s, fault ?? notes);
        }

        var known = TriggerSweepGaps.Known;
        var rows = new List<string> { "raw\tsplit\taction\troute\tstate\twriters" };
        var gapNames = new HashSet<string>();
        foreach (var a in TriggerSweepRoutes.Actions)
        {
            string state;
            if (a.Route != TriggerSweepRoutes.Route.Packet) state = a.Route.ToString();
            else
            {
                var ws = a.Writers.Split(',');
                var states = ws.SelectMany(w => byWriter.Where(kv => kv.Key == w || (!w.Contains('[') && kv.Key.StartsWith(w + "["))).Select(kv => kv.Value.State)).ToList();
                if (states.Count == 0) { fails.Add($"{a.Name}: no body for {a.Writers}"); continue; }
                var worst = states.Max();
                state = worst.ToString();
                if (Dropped(worst)) gapNames.Add(a.Name);
            }
            if (a.Route is TriggerSweepRoutes.Route.LogOnly or TriggerSweepRoutes.Route.Deep) gapNames.Add(a.Name);
            rows.Add($"{a.Raw}\t{a.Split}\t{a.Name}\t{a.Route}\t{state}\t{a.Writers}");
        }
        foreach (var n in gapNames.Where(n => !known.ContainsKey(n)).OrderBy(n => n)) fails.Add($"{n} is dropped and not listed");
        foreach (var n in known.Keys.Where(n => !gapNames.Contains(n) && !TriggerSweepGaps.Semantic.Contains(n)).OrderBy(n => n)) fails.Add($"{n} is listed but not dropped");

        game.Dispose();
        ls.Free();
        if (System.Environment.GetEnvironmentVariable("MS2_SWEEP_OUT") is { Length: > 0 } dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "writers.tsv"),
                new[] { "writer\topcode\tstate\tdetail" }.Concat(TriggerSweepRoutes.Bodies.Select(b => $"{b.Writer}\t0x{b.Opcode:X4}\t{byWriter[b.Writer].State}\t{byWriter[b.Writer].Detail}")));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "actions.tsv"), rows);
        }
        var counts = byWriter.Values.GroupBy(v => v.State).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}");
        GD.Print($"trigger sweep: {TriggerSweepRoutes.Actions.Length} actions, {byWriter.Count} writers ({string.Join(", ", counts)}), "
            + $"{gapNames.Count} dropped; " + (fails.Count == 0 ? "all listed, OK" : "FAIL " + string.Join("; ", fails)));
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>Gives each public event of the session a handler that does nothing, as the HUD does in the game (its handlers
    /// are the dialogs, which a headless check has not), so a branch that logs "not applied" only without a listener does not
    /// count its packet as dropped.</summary>
    static void Listen(LiveSession ls)
    {
        foreach (var e in typeof(LiveSession).GetEvents(BindingFlags.Public | BindingFlags.Instance))
        {
            if (e.DeclaringType != typeof(LiveSession)) continue;
            var invoke = e.EventHandlerType!.GetMethod("Invoke")!;
            var ps = invoke.GetParameters().Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType)).ToArray();
            System.Linq.Expressions.Expression body = invoke.ReturnType == typeof(void)
                ? System.Linq.Expressions.Expression.Empty() : System.Linq.Expressions.Expression.Default(invoke.ReturnType);
            e.AddEventHandler(ls, System.Linq.Expressions.Expression.Lambda(e.EventHandlerType, body, ps).Compile());
        }
    }
}
