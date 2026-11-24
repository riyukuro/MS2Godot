using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Ms2Godot.Flow;
using Ms2Godot.World;

namespace Ms2Godot.Net;

public partial class LiveSession
{
    /// <summary>
    /// MS2_LIVE_COMBAT_STEP with MS2_LIVE_COMBAT_SHOTS=<dir>: one scripted combat run against the live server, with shots and
    /// "live: combat ..." result lines (PASS / FAIL per check). Steps, comma separated, run in the one field entry:
    /// warp:<map>: the GM warp, then the field entry (the tutorial map renders black).
    /// attack: a mob spawned by the GM chat command (MS2_LIVE_COMBAT_MOB, an npc id) beside the player, the attack key pressed
    /// once a second until the mob dies (at most 30 presses); checks: the cast was sent, damage numbers came back, the mob's
    /// exp line (its death) came back.
    /// death: mobs spawned (MS2_LIVE_COMBAT_MOB, MS2_LIVE_COMBAT_MOBS of them) until the own character dies (at most 90 s);
    /// checks: the dead flag, the tombstone, the revival dialog (or the auto-revive countdown).
    /// revive: after death, the revival dialog's free button (onRevival 1); checks: the dead flag clears.
    /// loot: an item (MS2_LIVE_COMBAT_ITEM) and meso (MS2_LIVE_COMBAT_MESO_ITEM, MS2_LIVE_COMBAT_MESO) dropped at the player by
    /// the GM item command, the pickup key held; checks: the drop appears, the item gain and the meso gain come back, the bag
    /// and the meso count change.
    /// lift (or lift:x y z): a run to the lift at MS2_LIVE_LIFT_AT (its map point, x,y,z) or the given point, then the action key; checks: the lift craft came
    /// back, the carried model, the carry idle, then a few steps of the carry walk.
    /// putdown: the action key in cube mode where the player stands; checks: the craft ended.
    /// mobloot: MS2_LIVE_COMBAT_MOB killed until a kill drops an item (MS2_LIVE_LOOT_KILLS tries), its drops on the ground, a run to
    /// each with the pickup key held; checks: the item drop, the bag and meso gain (mobloot:id names the mob; mobmeso:id is one
    /// kill whose mesos are picked up, with no item check). watchdrops: MS2_LIVE_WATCH_SECONDS of another
    /// player's drops with the pickup key held; checks: none of them is made or picked here.
    /// walkto:x y z[/x y z...] runs through map points in turn; jumpto:x y z runs to a point a level up, jumping on the way; faceto:x y z turns the player to a map point; aimcell:x y z
    /// tries the four sides of a target box's cell until the craft's cell is it, with the wheel for a box a level off the ground; scan:x0 y0 x1 y1 cell
    /// logs the ground level under each cell (negative levels as a, b, ...); keys:N presses the action key N times.
    /// </summary>
    static Field triggerFieldOf(LiveSession live) => live.triggerField != null && GodotObject.IsInstanceValid(live.triggerField) ? live.triggerField : null;

    static long BagSum(LiveSession live) { long n = 0; foreach (var i in live.Items.All) n += i.Amount; return n; }

    /// <summary>The action key pressed once through the engine's input (a Space key down, held a few frames so the field's
    /// physics tick reads it, then the key up).</summary>
    static async Task PressActionKey(Func<int, Task> frames)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
        await frames(6);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
        await frames(2);
    }

    public static async Task CombatRun(GameFlow flow, Func<int, Task> frames, Func<Func<bool>, int, Task> until)
    {
        string steps = System.Environment.GetEnvironmentVariable("MS2_LIVE_COMBAT_STEP");
        string shots = System.Environment.GetEnvironmentVariable("MS2_LIVE_COMBAT_SHOTS");
        if (string.IsNullOrEmpty(steps) || flow.GetTree().CurrentScene is not Ui.HudScene hud || Instance is not { } live) return;
        if (!string.IsNullOrEmpty(shots)) DirAccess.MakeDirRecursiveAbsolute(shots);
        void LogInteract(string step, Ms2.Ui.LiveInteractObject o)
        {
            if (o == null) return;
            var draws = Field.Active?.Interacts;
            var at = FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition);
            GD.Print($"live: combat {step}: object {o.Name} row {o.RowId} kind {o.Kind} state {o.State} shown state {o.ShownState} " +
                     $"dialog 0x{o.DialogId:x}; drawn {draws?.Drawn(o)?.ToString() ?? "-"}, box {(draws?.Box(o) != null ? "up" : "none")}; " +
                     $"pc at {at}, {Math.Sqrt(Math.Pow(at.X - o.Position.X, 2) + Math.Pow(at.Y - o.Position.Y, 2) + Math.Pow(at.Z - o.Position.Z, 2)):F0} away");
        }

        void Shot(string name)
        {
            if (string.IsNullOrEmpty(shots)) return;
            flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(shots, name)); GD.Print($"live: shot {name} at {Time.GetTicksMsec()}");
        }
        void Check(string what, bool ok) => GD.Print($"live: combat {(ok ? "PASS" : "FAIL")}: {what}");
        void Shows(string what)     // the frame is not one colour: nothing full screen is left over the field
        {
            var img = flow.GetViewport().GetTexture().GetImage();
            float lo = 1, hi = 0;
            for (int y = 0; y < img.GetHeight(); y += 24)
                for (int x = 0; x < img.GetWidth(); x += 24) { float l = img.GetPixel(x, y).Luminance; lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l); }
            Check($"{what}: the frame is not one colour (luminance {lo:0.00} to {hi:0.00})", hi - lo > 0.05f);
        }
        string Env(string k, string d) => System.Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : d;
        // MS2_LIVE_EQUIP_DIRECT=1: when the bag refuses (the tutorial guide keeps it shut and holds the put on), the equip request
        // goes out as the bag would send it, right hand first and the second copy to the left; for a start that has the weapon
        // worn already, as a created character of another server's data
        bool EquipDirect(int itemId, bool another)
        {
            if (Env("MS2_LIVE_EQUIP_DIRECT", "0") != "1" || live.Inventory?.RequestEquip is not { } send) return false;
            var worn = live.Me.Equips;
            foreach (var e in live.Items.All)
            {
                if (e.Id != itemId || worn?.Any(w => w.Uid == e.Uid) == true) continue;
                send(e.Uid, (byte)(another ? Ms2.Net.Packets.EquipSlot.LH : Ms2.Net.Packets.EquipSlot.RH), 0);
                return true;
            }
            return false;
        }
        int aimMobs = 0, aimKills = 0, numbers = 0, kills = 0, npcs = 0, storeItem = int.Parse(Env("MS2_LIVE_STORE_ITEM", "0").Split(';')[0]);
        long mesoDelta = 0, lockUid = 0;
        int lockId = 0;
        var hitCount = new System.Collections.Generic.Dictionary<FieldNpcs.Placed, int>();
        live.DamageNumbered += (n, npc, pc) => { if (npc != null) { numbers++; hitCount[npc] = hitCount.GetValueOrDefault(npc) + 1; } };
        live.NpcExpShown += (npc, exp) => kills++;
        FieldNpcs.Placed lastNpc = null;
        var serverNpcs = new System.Collections.Generic.List<FieldNpcs.Placed>();
        live.NpcAdded += npc => { npcs++; lastNpc = npc; serverNpcs.Add(npc); };
        var goneNpcs = new System.Collections.Generic.HashSet<int>();     // object ids removed by the server or counted dead by the kill step
        live.NpcRemoved += id => goneNpcs.Add(id);
        int questProgress = 0;
        live.Quests.ProgressUpdated += id => questProgress++;
        bool Alive(FieldNpcs.Placed n) => n?.Root != null && GodotObject.IsInstanceValid(n.Root) && !goneNpcs.Contains(n.ObjectId);
        live.Quests.Changed += id => { var q = live.Quests.Get(id); GD.Print($"live: combat quest {id} state {q?.State} progress [{string.Join(" ", q?.Progress ?? System.Array.Empty<long>())}]"); };
        live.MesoChanged += (delta, kind) => mesoDelta += delta;
        await frames(240);
        int camShots = 0;
        foreach (var step in steps.Split(','))
        {
            GD.Print($"live: combat step {step}");
            if (step.StartsWith("warp:"))
            {
                int entered = 0; void Entered(int map, long ms) => entered++;
                live.FieldEntered += Entered;
                GD.Print($"live: combat {step}: {(hud.SubmitChatLine("/warp " + step[5..]) ? "sent" : "no chat window")}");
                await until(() => entered > 0, 60 * 90); await frames(240);
                live.FieldEntered -= Entered;
                Check($"the field entry after the warp (map {live.CurrentEntry?.MapId})", entered > 0);
                continue;
            }
            if (step.StartsWith("walkto:"))      // walkto:x y z[/x y z...]: a run through the map points in turn, to within 40 of each
            {
                foreach (string point in step[7..].Split('/'))
                {
                    var c = point.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var at = new Vector3(float.Parse(c[0]), float.Parse(c[2]), -float.Parse(c[1]));
                    float dist = hud.DriveTowardScripted(at, false);
                    int n = 0;
                    for (; n < 60 * 25 && dist > 40; n++)
                    {
                        dist = hud.DriveTowardScripted(at, false); await frames(1);
                        if (n % 120 == 119) GD.Print($"live: combat walkto {point}: distance {dist:F0}, at {FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition)}");
                    }
                    hud.DriveEnd();
                    GD.Print($"live: combat walkto {point}: distance {dist:F0} after {n} frames, at {FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition)}");
                }
                await frames(60);
                continue;
            }
            if (step.StartsWith("jumpto:"))      // jumpto:x y z: a run to a map point a level up, the jump key pressed every 0.75 s on the way
            {
                var c = step[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var at = new Vector3(float.Parse(c[0]), float.Parse(c[2]), -float.Parse(c[1]));
                float dist = hud.DriveTowardScripted(at, false), up = at.Y - live.OwnPlayer.GlobalPosition.Y;
                int n = 0;
                for (; n < 60 * 25 && (dist > 40 || Math.Abs(up) > 60); n++)
                {
                    dist = hud.DriveTowardScripted(at, false);
                    live.OwnPlayer.JumpHeld = n % 45 < 6;
                    await frames(1);
                    up = at.Y - live.OwnPlayer.GlobalPosition.Y;
                }
                live.OwnPlayer.JumpHeld = false;
                hud.DriveEnd();
                GD.Print($"live: combat jumpto {step[7..]}: distance {dist:F0}, height left {up:F0} after {n} frames, at {FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition)}");
                await frames(60);
                continue;
            }
            if (step.StartsWith("scan:"))      // scan:x0 y0 x1 y1 cell: the ground height under each cell centre (a ray down), one row per y
            {
                var c = step[5..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(float.Parse).ToArray();
                var space = live.OwnPlayer.GetWorld3D().DirectSpaceState;
                float baseZ = (float)FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition).Z;
                GD.Print($"live: combat scan {step[5..]}: one char per cell from x {c[0]}, the level above the player's ({baseZ:F0}) in cells of 150, '.' no ground");
                for (float y = c[3]; y >= c[1]; y -= c[4])
                {
                    var row = new System.Text.StringBuilder();
                    for (float x = c[0]; x <= c[2]; x += c[4])
                    {
                        using var query = PhysicsRayQueryParameters3D.Create(new Vector3(x, 6000, -y), new Vector3(x, -6000, -y));
                        using var hit = space.IntersectRay(query);
                        if (hit.Count == 0) { row.Append('.'); continue; }
                        int level = Mathf.RoundToInt((((Vector3)hit[PhysicsKeys.Position]).Y - baseZ) / 150f);
                        row.Append(level < -9 ? '_' : level < 0 ? (char)('a' - 1 - level) : level > 9 ? '^' : (char)('0' + level));
                    }
                    GD.Print($"live: scan y {y,6:F0} {row}");
                }
                continue;
            }
            // watchfx:N: N seconds of frames; with MS2_LIVE_FX_TRACE=1 each effect another PC's or the own skill state keeps
            // (a cast by a held key, say) is shot two frames later (at most 12 shots)
            if (step.StartsWith("watchfx:"))
            {
                int shotsLeft = 12, shotAt = -1, n = 0, frame = 0;
                string who = null;
                void Kept(string fx) { if (shotAt < 0) { shotAt = frame + 2; who = "remote"; } }
                void OwnKept(string fx) { if (shotAt < 0) { shotAt = frame + 2; who = "own"; } }
                RemoteLayer.RemoteEffectKept += Kept;
                Ui.HudScene.OwnEffectKept += OwnKept;
                for (int end = 60 * int.Parse(step[8..]); frame < end; frame++)
                {
                    if (frame == shotAt && shotsLeft-- > 0) { Shot($"{who}fx{++n}.png"); shotAt = -1; }
                    await frames(1);
                }
                RemoteLayer.RemoteEffectKept -= Kept;
                Ui.HudScene.OwnEffectKept -= OwnKept;
                continue;
            }
            if (step.StartsWith("guide:"))      // guide:<event id>[:clear][:<seconds>]: until the tutorial guide's event is open (or cleared); 60 s by default
            {
                var a = step.Split(':');
                int id = int.Parse(a[1]);
                bool clear = a.Contains("clear");
                int secs = a.Length > 2 && int.TryParse(a[^1], out int s) ? s : 60;
                int want = clear ? 3 : 2;
                await until(() => hud.GuideEventScripted(id) >= want, 60 * secs);
                int state = hud.GuideEventScripted(id);
                Check($"guide event {id} {(clear ? "cleared" : "open")} (state {state}, at {FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition)})", state >= want);
                GD.Print($"live: combat guide state: {hud.GuideStateText()}");
                continue;
            }
            if (step.StartsWith("bagwait:"))      // bagwait:<item id>[:<count>]: until the server has put the item in the bag (no item command of our own)
            {
                var a = step.Split(':');
                int id = int.Parse(a[1]), count = a.Length > 2 ? int.Parse(a[2]) : 1;
                int Has() => (int)live.Items.All.Where(e => e.Id == id).Sum(e => Math.Max(1L, (long)e.Amount)) + (live.Me.Equips?.Count(e => e.Id == id) ?? 0);
                await until(() => Has() >= count, 60 * 30);
                Check($"the server put item {id} in the bag ({Has()} of {count})", Has() >= count);
                continue;
            }
            if (step.StartsWith("wait:"))      // wait:N: N seconds of frames (a trigger's timed states)
            {
                await frames(60 * int.Parse(step[5..]));
                continue;
            }
            if (step.StartsWith("shot:"))      // shot:<name>: a shot of the frame as it is
            {
                Shot(step[5..] + ".png");
                continue;
            }
            if (step.StartsWith("keys:"))      // keys:N: the action key pressed N times, a second apart (a talk's Next pages)
            {
                for (int i = int.Parse(step[5..]); i > 0; i--) { await PressActionKey(frames); await frames(60); }
                continue;
            }
            if (step.StartsWith("aimcell:"))      // aimcell:x y z: the craft's cell made the cell of a target box at the map point
            {
                // PutDownLiftableAction takes the box's cell as GetCellPosition gives it (cut toward zero), which for a box at
                // a negative point is not always the cell the box is drawn in: the player tries the four sides of that cell.
                var c = step[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(float.Parse).ToArray();
                var cell = Ms2.Ui.CubeCell.Position(new System.Numerics.Vector3(c[0], c[1], c[2]));
                var me = FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition);
                var sides = new[] { (150f, 0f), (-150f, 0f), (0f, 150f), (0f, -150f) }
                    .OrderBy(s => new Vector2(cell.X + s.Item1 - (float)me.X, cell.Y + s.Item2 - (float)me.Y).Length());
                bool hit = false;
                foreach (var (sx, sy) in sides)
                {
                    var stand = new Vector3(cell.X + sx, live.OwnPlayer.GlobalPosition.Y, -(cell.Y + sy));
                    float dist = hud.DriveTowardScripted(stand, false);
                    for (int n = 0; n < 60 * 8 && dist > 30; n++) { dist = hud.DriveTowardScripted(stand, false); await frames(1); }
                    hud.DriveEnd();
                    hud.FaceScripted(new Vector3(cell.X, live.OwnPlayer.GlobalPosition.Y, -cell.Y));
                    await frames(30);
                    var got = live.CubeCraft.Current?.Cell;
                    for (int w = 0; w < 3 && got is { } g && g.X == cell.X && g.Y == cell.Y && g.Z != cell.Z; w++)
                    {      // the box a level off the ground: the wheel lifts or lowers the craft, as the player turns it
                        var wheel = new InputEventMouseButton { ButtonIndex = g.Z < cell.Z ? MouseButton.WheelUp : MouseButton.WheelDown, Pressed = true, Position = new Vector2(640, 360) };
                        Input.ParseInputEvent(wheel);
                        await frames(2);
                        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = wheel.ButtonIndex, Pressed = false, Position = wheel.Position });
                        await frames(20);
                        got = live.CubeCraft.Current?.Cell;
                    }
                    GD.Print($"live: combat aimcell {step[8..]}: box cell {cell}, from side {sx} {sy} (distance {dist:F0}, at {FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition)}) the craft's cell {got}");
                    if (got == cell) { hit = true; break; }
                }
                Check($"the craft's cell is the box cell {cell}", hit);
                continue;
            }
            if (step.StartsWith("tombhit:"))      // tombhit:N: a run to another player's standing tomb, the player turned to it, then the attack key N times a second apart
            {
                var tomb = live.remote?.StandingTomb();
                Check("another player's tomb stands in the field", tomb != null);
                if (tomb == null) continue;
                float dist = hud.DriveTowardScripted(tomb.GlobalPosition, false);
                for (int n = 0; n < 60 * 25 && dist > 80 && GodotObject.IsInstanceValid(tomb); n++) { dist = hud.DriveTowardScripted(tomb.GlobalPosition, false); await frames(1); }
                hud.DriveEnd(); await frames(20);
                for (int i = int.Parse(step[8..]); i > 0 && GodotObject.IsInstanceValid(tomb) && tomb.IsInsideTree(); i--)
                {
                    dist = hud.FaceScripted(tomb.GlobalPosition);
                    GD.Print($"live: combat {step}: distance {dist:F0}: {(hud.AttackScripted() ? "cast" : "refused")}");
                    await frames(60);
                }
                Shot("tombhit.png");
                continue;
            }
            if (step.StartsWith("breakhit:"))      // breakhit:N[:secs]: a run to the nearest standing breakable the tag pass hits, faced, the attack key N times a second apart; its states printed as they change for secs (60)
            {
                var arg = step[9..].Split(':');
                var own = FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition);
                Ms2.Ui.BreakableObjects.Marker target = null;
                double best = double.MaxValue;
                foreach (var m in live.Breakables.Sorted)
                {
                    if (!m.Hittable || !m.ReactionArrow || m.State != 2) continue;
                    double dx = m.Position.X - own.X, dy = m.Position.Y - own.Y, dz = m.Position.Z - own.Z, d = dx * dx + dy * dy + dz * dz;
                    if (d < best) { best = d; target = m; }
                }
                Check($"a standing breakable the tag pass hits is in the field ({live.Breakables.Count} breakables)", target != null);
                if (target == null) continue;
                var at = new Vector3((float)target.Position.X, (float)target.Position.Z, -(float)target.Position.Y);
                GD.Print($"live: combat {step}: breakable {target.Name} at {target.Position} size {target.Size} state {target.State}, {System.Math.Sqrt(best):F0} away");
                int lastState = target.State;
                var seen = new System.Collections.Generic.List<int> { lastState };
                void Track()
                {
                    if (target.State == lastState) return;
                    lastState = target.State; seen.Add(lastState);
                    GD.Print($"live: combat {step}: breakable {target.Name} state {lastState} shown {target.Shown} visible {target.Visible}");
                    Shot($"breakhit_state{seen.Count - 1}_{lastState}.png");
                }
                // the run ends at the breakable's side facing the pc's start (half its side + 80 out), not at its centre: a
                // breakable with NxCollision stands in its box (MakeNxBoxController), which the pc walks against or stands on
                double side = System.Math.Min(target.Size.X, target.Size.Y) * 0.5, top = target.Position.Z + target.Size.Z;
                double ox = own.X - target.Position.X, oy = own.Y - target.Position.Y, olen = System.Math.Sqrt(ox * ox + oy * oy);
                if (olen < 1) { ox = 1; oy = 0; olen = 1; }
                double reach = side + 80;
                var stand = new Vector3((float)(target.Position.X + ox / olen * reach), (float)target.Position.Z, -(float)(target.Position.Y + oy / olen * reach));
                float dist = hud.DriveTowardScripted(stand, false);
                for (int n = 0; n < 60 * 25 && dist > 40; n++) { dist = hud.DriveTowardScripted(stand, false); await frames(1); }
                hud.DriveEnd(); await frames(20);
                Shot("breakhit_before.png");
                (double Flat, double Z) Stood()
                {
                    var p = FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition);
                    double sx = p.X - target.Position.X, sy = p.Y - target.Position.Y;
                    return (System.Math.Sqrt(sx * sx + sy * sy), p.Z);
                }
                // a dead pc (a mob on the way) can neither push nor attack: the rest of the step would prove nothing
                Check("the pc is alive at the breakable's side", !live.Me.Dead);
                if (live.Me.Dead) continue;
                var before = Stood();
                GD.Print($"live: combat {step}: at its side {before.Flat:F0} from its centre, z {before.Z:F0} (box {(target.HasBox ? "up" : "none")}, half side {side:F0}, top {top:F0})");
                if (target.HasBox)
                {
                    // a push toward the centre for 1.5 s: the box keeps the pc out (at its side, or on its top)
                    for (int n = 0; n < 90; n++) { hud.DriveTowardScripted(at, false); await frames(1); }
                    hud.DriveEnd(); await frames(20);
                    var pushed = Stood();
                    bool outside = pushed.Flat >= side - 5 || pushed.Z >= top - 5;
                    GD.Print($"live: combat {step}: pushed toward the centre: {pushed.Flat:F0} from it, z {pushed.Z:F0}");
                    Shot("breakhit_pushed.png");
                    Check($"the box keeps the pc out ({pushed.Flat:F0} from the centre, half side {side:F0}, z {pushed.Z:F0}, top {top:F0})", outside);
                }
                for (int i = int.Parse(arg[0]); i > 0 && target.State == 2; i--)
                {
                    dist = hud.FaceScripted(at);
                    GD.Print($"live: combat {step}: distance {dist:F0}: {(hud.AttackScripted() ? "cast" : "refused")}");
                    for (int f = 0; f < 60; f++) { await frames(1); Track(); }
                }
                for (int s = 0, secs = arg.Length > 1 ? int.Parse(arg[1]) : 60; s < 60 * secs; s++) { await frames(1); Track(); }
                Check($"the server broke it (states {string.Join(" ", seen)})", seen.Contains(3) || seen.Contains(4));
                Shot("breakhit_end.png");
                continue;
            }
            if (step.StartsWith("faceto:"))      // faceto:x y z: the player turned to the map point where it stands
            {
                var c = step[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                hud.FaceScripted(new Vector3(float.Parse(c[0]), float.Parse(c[2]), -float.Parse(c[1])));
                await frames(30);
                continue;
            }
            if (step.StartsWith('~') && int.TryParse(step.AsSpan(1), out int waitSecs))      // ~N: N seconds
            {
                await frames(60 * waitSecs);
                continue;
            }
            if (step == "quests")      // the quest records not yet rewarded, the talkable npcs and the server npcs of the field
            {
                foreach (var q in live.Quests.All.Where(q => q.State != 2).OrderBy(q => q.Id))
                    GD.Print($"live: combat quests: {q.Id} state {q.State} progress [{string.Join(" ", q.Progress ?? System.Array.Empty<long>())}]");
                GD.Print($"live: combat quests: field {live.FieldId}, talk npcs {string.Join(" ", live.TalkNpcs.Select(x => x.Row.Id).Distinct().OrderBy(x => x))}, server npcs {string.Join(" ", serverNpcs.Where(Alive).Select(x => x.Row.Id).Distinct().OrderBy(x => x))}, interact objects {string.Join(" ", live.InteractObjects?.All.Select(x => $"{x.RowId}/{x.State}").Distinct() ?? Enumerable.Empty<string>())}");
                continue;
            }
            if (step.StartsWith("interact:"))      // interact:<interact id>: the own pc put at the nearest field interact object of that id (no walk), then the action key; the cast and its end are waited for
            {
                int id = int.Parse(step[9..]);
                var io = live.InteractObjects;
                Ms2.Ui.LiveInteractObject o = null;
                await until(() => live.player != null && (o = io?.All.Where(x => x.RowId == id)
                    .OrderBy(x => FieldBuilder.ToGodotPoint(x.Position).DistanceSquaredTo(live.player.GlobalPosition)).FirstOrDefault()) != null, 60 * 20);
                Check($"{step}: the object in the field ({o?.Name}, state {o?.State})", o != null);
                if (o == null) continue;
                live.player.GlobalPosition = FieldBuilder.ToGodotPoint(o.Position) + new Vector3(0, 5, 0);
                live.player.Velocity = Vector3.Zero;
                await frames(90);
                int sends = live.InteractSends;
                await PressActionKey(frames);
                await until(() => io.IsCasting || live.InteractSends > sends, 60 * 3);
                bool started = io.IsCasting || live.InteractSends > sends;
                await until(() => !io.IsCasting, 60 * 15);
                await frames(120);
                Check($"{step}: the action key used it ({live.InteractSends - sends} send(s))", started);
                LogInteract(step, o);
                Shot($"interact_{id}.png");
                continue;
            }
            if (step.StartsWith("telescope:"))      // telescope:<interact id>: the own pc put at the nearest telescope of that id, the action key; its view checked, then Esc skips it
            {
                int id = int.Parse(step[10..]);
                var io = live.InteractObjects;
                Ms2.Ui.LiveInteractObject o = null;
                await until(() => live.player != null && (o = io?.All.Where(x => x.RowId == id && x.Kind == Ms2.Net.Packets.InteractObjectMessage.KindTelescope)
                    .OrderBy(x => FieldBuilder.ToGodotPoint(x.Position).DistanceSquaredTo(live.player.GlobalPosition)).FirstOrDefault()) != null, 60 * 20);
                Check($"{step}: the telescope in the field ({o?.Name})", o != null);
                if (o == null) continue;
                live.player.GlobalPosition = FieldBuilder.ToGodotPoint(o.Position) + new Vector3(0, 5, 0);
                live.player.Velocity = Vector3.Zero;
                await frames(90);
                Shot("telescope_before.png");
                await PressActionKey(frames);
                await until(() => o.TelescopeActive, 60 * 5);
                var cine = live.Cinematic;
                var cams = o.TelescopeEntity != null ? string.Join(",", Ms2.Ui.LiveInteractObjects.TelescopeCameraIds(o.TelescopeEntity)) : "none";
                GD.Print($"live: combat {step}: found {o.TelescopeEntity?.Raw?.Id ?? "none"} cameras {cams}, key mode {cine.KeyMode}, cinematic {cine.Mode}/{cine.Id}, skip state {cine.SkipState}");
                Check($"{step}: the server's message entered the view (cameras {cams})", o.TelescopeActive);
                Check($"{step}: the cinematic sub scene with cinematic 9 and skip state 3", cine.KeyMode && cine.Mode == 1 && cine.Id == 9 && cine.SkipState == 3);
                await frames(60 * 3);
                Shot("telescope_view.png");
                hud.Escape();
                await frames(60 * 2);
                Check($"{step}: Esc skipped the view (inactive, key mode {cine.KeyMode})", !o.TelescopeActive && !cine.KeyMode);
                await frames(60);
                Shot("telescope_after.png");
                continue;
            }
            if (step.StartsWith("dialogclosed:"))      // dialogclosed:<interact id>: the UI object's dialog let go (the walk took the pc away, or it was closed)
            {
                int id = int.Parse(step[13..]);
                var o = live.InteractObjects?.All.FirstOrDefault(x => x.RowId == id);
                await until(() => o == null || o.DialogId == 0, 60 * 5);
                LogInteract(step, o);
                Check($"{step}: the object's dialog is closed (id 0x{o?.DialogId ?? -1:x})", o != null && o.DialogId == 0);
                Shot($"dialogclosed_{id}.png");
                continue;
            }
            if (step == "pickup")      // the pickup key held 4 s where the pc stands (a drop at its feet); the bag count before and after
            {
                long bag = BagSum(live);
                live.PickupKeyHeld = true;
                await frames(60 * 4);
                live.PickupKeyHeld = false;
                await frames(60);
                GD.Print($"live: combat pickup: bag {bag} -> {BagSum(live)}");
                continue;
            }
            if (step.StartsWith("talk:"))      // talk:<npc id>[:<pages>]: the own pc put in front of the field's npc of that id (no walk), the action key, then Next through the pages, the quest pages too (their accept and complete go out); the quest changes print as they come
            {
                var a = step.Split(':');
                int id = int.Parse(a[1]), pages = a.Length > 2 ? int.Parse(a[2]) : 16;
                var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                FieldNpcs.Placed n = null;
                await until(() => (n = live.TalkNpcs.FirstOrDefault(x => x.Row.Id == id && Alive(x))) != null, 60 * 30);
                Check($"{step}: the npc in the field", n != null);
                if (n == null || tfc?.Talk == null || live.player == null || !GodotObject.IsInstanceValid(live.player)) continue;
                double yaw = n.Root.Rotation.Y;
                var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
                live.player.GlobalPosition = n.Position + front * 120 + new Vector3(0, 20, 0);
                live.player.Velocity = Vector3.Zero;
                live.player.Face(-front);
                await frames(90);
                tfc.Talk.ActionKey();
                await until(() => tfc.Talk.Open, 60 * 5);
                Check($"{step}: the talk opened", tfc.Talk.Open);
                Shot($"talk{id}_{Math.Abs(step.GetHashCode()) % 1000}_open.png");
                for (int k = 0; k < pages && tfc.Talk.Open; k++)
                {
                    GD.Print($"live: combat {step} page {k + 1}: script {tfc.Talk.ScriptId} page {tfc.Talk.Page + 1}, quest page {tfc.Talk.OnQuestPage}");
                    tfc.Talk.ActionKey();
                    await frames(90);
                }
                if (tfc.Talk.Open) { GD.Print($"live: combat {step}: still open after {pages} pages, ended"); tfc.Talk.End(); }
                await frames(120);
                continue;
            }
            if (step.StartsWith("kill:"))      // kill:<npc id>[:<count>]: the field's server npcs of that id, nearest first, each walked to, faced and attacked until it dies (up to MS2_LIVE_COMBAT_PRESSES presses each)
            {
                var a = step.Split(':');
                int id = int.Parse(a[1]), count = a.Length > 2 ? int.Parse(a[2]) : 1, need = int.Parse(Env("MS2_LIVE_COMBAT_PRESSES", "20")), dead = 0;
                int? field = live.FieldId;
                for (int i = 0; i < count && live.FieldId == field; i++)
                {
                    FieldNpcs.Placed target = null;
                    await until(() => (target = serverNpcs.Where(x => x.Row.Id == id && Alive(x)).OrderBy(x => live.player.GlobalPosition.DistanceSquaredTo(x.Position)).FirstOrDefault()) != null, 60 * 30);
                    if (target == null) break;
                    float dist = hud.FaceScripted(target.Position);
                    for (int walk = 0; walk < 60 * 25 && dist > 200 && Alive(target); walk++)
                    {
                        dist = hud.FaceScripted(target.Position); hud.DriveScripted(new Vector2(0, 1), false); await frames(1);
                    }
                    hud.DriveEnd(); await frames(20);
                    if (dist > 250 && Alive(target) && Env("MS2_LIVE_WALK_ONLY", "0") == "1")      // as a player: attack from where the walk stopped
                        GD.Print($"live: combat {step}: the walk stopped at distance {dist:F0} (blocked; MS2_LIVE_WALK_ONLY puts nobody), at {live.player.GlobalPosition}");
                    else if (dist > 250 && Alive(target))
                    {      // no path finding: a wall between stops the straight walk, so the pc is put 150 from the npc on the line to it
                        var away = live.player.GlobalPosition - target.Position; away.Y = 0;
                        live.player.GlobalPosition = target.Position + (away.LengthSquared() > 1e-6f ? away.Normalized() : Vector3.Forward) * 150 + new Vector3(0, 20, 0);
                        live.player.Velocity = Vector3.Zero;
                        await frames(90);
                        dist = hud.FaceScripted(target.Position);
                        GD.Print($"live: combat {step}: the walk was blocked, put {dist:F0} from the npc");
                    }
                    int before = kills, progressBefore = questProgress, press = 0;
                    for (; press < need && Alive(target) && kills == before && questProgress == progressBefore && live.FieldId == field; press++)
                    {
                        dist = hud.FaceScripted(target.Position);
                        await frames(20);
                        hud.AttackScripted();
                        await frames(90);
                    }
                    await frames(60 * 3);
                    bool died = kills > before || questProgress > progressBefore || !Alive(target);
                    if (died) goneNpcs.Add(target.ObjectId);     // a corpse stays a while: the next pick skips it
                    GD.Print($"live: combat {step}: npc {i + 1} at distance {dist:F0}, {press} press(es), {hitCount.GetValueOrDefault(target)} number(s) on it, died {died}, own hp {live.Me.Dead switch { true => "dead", _ => "alive" }}");
                    if (died) dead++;
                }
                Check($"{step}: {dead} of {count} died", dead == count);
                continue;
            }
            if (step.StartsWith("box:"))      // box:<id>: the own pc put at the trigger box's centre (no walk; the position goes out with the next sync)
            {
                var at = triggerFieldOf(live)?.Triggers?.BoxCenter(int.Parse(step[4..]));
                if (at is { } c && live.player != null && GodotObject.IsInstanceValid(live.player))
                {
                    live.player.GlobalPosition = FieldBuilder.ToGodotPoint(c) + new Vector3(0, 5, 0);
                    live.player.Velocity = Vector3.Zero;
                }
                Check($"{step}: the own pc in the box ({at})", at != null);
                await frames(120);
                continue;
            }
            if (step.StartsWith("portal:") && flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) is FieldContents pfc)      // portal:<id|any>: into a server-listed open portal, then the action key
            {
                string pid = step[7..];
                var list = live.Portals?.All ?? (System.Collections.Generic.IReadOnlyList<Ms2.Ui.LivePortal>)Array.Empty<Ms2.Ui.LivePortal>();
                var p = pid == "any" ? list.FirstOrDefault(x => x.HasBox && x.Glow && x.TargetMap != 0) : list.FirstOrDefault(x => x.Id.ToString() == pid);
                int entered = 0; void Entered(int map, long ms) => entered++;
                live.FieldEntered += Entered;
                int sends = live.PortalSends;
                if (p != null && pfc.StandInPortal(p.Id))
                {
                    await frames(60);
                    if (pfc.LivePortalPoll()) pfc.LivePortalUse();
                    await frames(10);
                }
                bool sent = live.PortalSends > sends;
                GD.Print($"live: combat {step}: {(p == null ? "not listed here" : $"{p.Id} type {p.Type} shown {p.Shown} glow {p.Glow} -> field {p.TargetMap}")}, {(sent ? "sent" : "not sent")}");
                if (sent) { await until(() => entered > 0, 60 * 90); await frames(240); }
                live.FieldEntered -= Entered;
                Check($"the field entry through {step} (map {live.CurrentEntry?.MapId})", entered > 0);
                continue;
            }
            if (step == "watchmovie")      // a cut scene movie, not skipped: until it ends (the usm's end stops the pane) or the server stops it
            {
                await until(() => live.Movie.IsPlaying, 60 * 60);
                Check($"a cut scene movie plays ({live.Movie.Path})", live.Movie.IsPlaying);
                Shot("watchmovie_playing.png");
                await until(() => !live.Movie.IsPlaying, 60 * 240);
                Check("the movie stopped", !live.Movie.IsPlaying);
                await frames(30);
                Shows("after the movie");
                continue;
            }
            if (step == "camfree")      // the trigger camera is back in the follow mode
            {
                var cam = triggerFieldOf(live)?.TriggerCamera;
                Check($"the camera follows the pc (mode {cam?.ModeType})", cam?.ModeType == TriggerCamera.Target);
                Shot($"camfree_{live.CurrentEntry?.MapId}_{++camShots}.png");
                Shows("the follow camera");
                continue;
            }
            if (step.StartsWith("rideraw:"))      // rideraw:<ride id>: the own pc rides the row by a ResponseRide mode 0 the server's debug packet command sends, then rides off (mode 1)
            {
                int rideId = int.Parse(step[8..]);
                if (hud.ClearShotScripted()) GD.Print("live: combat level-up guide closed for the shots");
                bool sent = hud.SubmitChatLine("/debug packet " + live.OwnRideBytes(true, rideId, 0x7e000100 + (rideId & 0xff)));
                await until(() => live.OwnRidee?.Id == rideId, 60 * 8);
                await frames(90);
                Check($"rideraw {rideId}: riding (sent {sent}); {hud.RideHudScripted()}", live.OwnRidee?.Id == rideId);
                Shot($"ride_{rideId}_on.png");
                hud.DriveTowardScripted(live.OwnPlayer.GlobalPosition + new Vector3(400, 0, 0), false);
                await frames(60);
                Shot($"ride_{rideId}_run.png");
                hud.DriveEnd();
                await frames(30);
                hud.SubmitChatLine("/debug packet " + live.OwnRideBytes(false, rideId, 0));
                await until(() => live.OwnRidee == null, 60 * 8);
                await frames(60);
                Check($"rideraw {rideId}: off; {hud.RideHudScripted()}", live.OwnRidee == null);
                Shot($"ride_{rideId}_off.png");
                continue;
            }
            if (step.StartsWith("rideitem:"))      // rideitem:<item id>: the item into the bag (the server's GM item command), used as from a quickslot (the summon cast, then RequestRide), then used again to ride off
            {
                int itemId = int.Parse(step[9..]);
                if (hud.ClearShotScripted()) GD.Print("live: combat level-up guide closed for the shots");
                long Uid() { foreach (var e in live.Items.All) if (e.Id == itemId) return e.Uid; return 0; }
                if (Uid() == 0) { hud.SubmitChatLine($"/item {itemId}"); await until(() => Uid() != 0, 60 * 5); }
                long uid = Uid();
                int ridee = hud.ItemRideeScripted(itemId);
                live.UseRideeItem(uid);
                await until(() => live.OwnRidee != null, 60 * 12);
                await frames(90);
                Check($"rideitem {itemId} (uid {uid}, ridee {ridee}): riding {live.OwnRidee?.Id}; {hud.RideHudScripted()}", live.OwnRidee?.Id == ridee && ridee != 0);
                Shot($"rideitem_{itemId}_on.png");
                hud.DriveTowardScripted(live.OwnPlayer.GlobalPosition + new Vector3(400, 0, 0), false);
                await frames(60);
                Shot($"rideitem_{itemId}_run.png");
                hud.DriveEnd();
                await frames(30);
                live.UseRideeItem(uid);
                await until(() => live.OwnRidee == null, 60 * 8);
                await frames(60);
                Check($"rideitem {itemId}: off; {hud.RideHudScripted()}", live.OwnRidee == null);
                Shot($"rideitem_{itemId}_off.png");
                continue;
            }
            if (step.StartsWith("ownstate:"))      // ownstate:<hex id>[:move]: the server's raw UserState for the own pc (its debug packet command), the exit timed
            {
                string[] parts = step[9..].Split(':');
                int st = Convert.ToInt32(parts[0], 16);
                bool moveOut = parts.Length > 1 && parts[1] == "move";   // a casting-bar state left by walking (its vfunc_8)
                var row = Ms2.Ui.OwnStateExit.OfOwnState(st);
                int before = live.ServerStatesEntered, castsBefore = live.ownStateCasts;
                int faces0 = live.OwnStateFaces, dash0 = live.SwimDashEffects, dashStops0 = live.SwimDashEffectsStopped;
                string face0 = live.OwnPlayer.FaceEmotion?.Current;
                bool castState = row?.Cause == Ms2.Ui.StateExitCause.CastingBar;
                bool sent = hud.SubmitChatLine("/debug packet " + live.OwnUserStateBytes(st));
                await until(() => live.ServerStatesEntered > before, 60 * 5);
                bool entered = live.ServerStatesEntered > before;
                bool cast = live.ownStateCasts > castsBefore;
                var castReq = cast ? live.LastOwnStateCast : null;
                double expect = castState ? (castReq?.DurationMs ?? -1000) / 1000.0 : live.ServerStateExitLeft;
                bool barRan = live.CastingBarRuns;
                string enterPlayed = live.OwnStateEnterPlayed;
                ulong w0 = Time.GetTicksMsec();   // the casting bar's timer runs on the real clock (Timer::FUN_1408688e0)
                ulong f0 = Engine.GetProcessFrames();   // game time: the runs step a fixed 1/60 s a frame (--fixed-fps 60)
                var at = live.OwnPlayer.GlobalPosition;
                // a move held through the state: a held body stays put; a casting-bar state is left by it (its vfunc_8), so
                // only the :move form holds one
                if (!castState || moveOut) hud.DriveTowardScripted(at + new Vector3(600, 0, 0), false);
                // what the state plays on the body (Ms2.Ui.OwnStateExit.Enters): the model's sequences, the body's sequences
                // and its hide, frame by frame, and a shot halfway through
                var asked = new List<string>(); var shown = new List<string>(); bool wasHidden = false, shotTaken = false;
                ulong half = (ulong)(expect > 0 && !castState ? expect * 30 : 20);
                await until(() =>
                {
                    if (live.OwnStatePlayed is { } pl && (asked.Count == 0 || asked[^1] != pl)) asked.Add(pl);
                    string cur = live.OwnPlayer.CurrentAnim;
                    if (shown.Count == 0 || shown[^1] != cur) shown.Add(cur);
                    wasHidden |= live.OwnPlayer.IsHidden;
                    barRan |= live.CastingBarRuns;
                    bool shotDue = castState ? expect > 0 && Time.GetTicksMsec() - w0 >= (ulong)(expect * 500) : Engine.GetProcessFrames() - f0 >= half;
                    if (!shotTaken && shotDue) { Shot($"ownstate-{st:x}{(moveOut ? "-move" : "")}.png"); shotTaken = true; }
                    return live.Me.State != st;
                }, 60 * 12);
                double took = castState ? (Time.GetTicksMsec() - w0) / 1000.0 : (Engine.GetProcessFrames() - f0) / 60.0;
                if (moveOut)   // the walk goes on past the leave
                {
                    ulong m0 = Engine.GetProcessFrames();
                    await until(() =>
                    {
                        string cur = live.OwnPlayer.CurrentAnim;
                        if (shown.Count == 0 || shown[^1] != cur) shown.Add(cur);
                        return Engine.GetProcessFrames() - m0 >= 20;
                    }, 60);
                }
                hud.DriveEnd();
                var shift = live.OwnPlayer.GlobalPosition - at; shift.Y = 0;
                float moved = shift.Length();   // on the flat: a fall or a landing is not a move
                await frames(2);
                bool barAfter = live.CastingBarRuns;
                if (castState)
                    GD.Print($"live: combat ownstate 0x{st:x} casts: started {cast} ({castReq?.StringCommonId ?? 0:x} {castReq?.DurationMs ?? 0} ms \"{(castReq == null || castReq.StringCommonId < 0 ? "" : Ms2.Ui.Hud.MessageBoxData.Common(castReq.StringCommonId))}\"), the bar ran {barRan}, runs after {barAfter}, moved out {moveOut}");
                GD.Print($"live: combat ownstate 0x{st:x} ({row?.Class} {row?.Cause}): sent {sent}, entered {entered}, expected exit {expect:0.###} s, state now {live.Me.State} after {took:0.###} s, moved {moved:0.##}");
                var enterRow = Ms2.Ui.OwnStateExit.EnterOfOwnState(st);
                GD.Print($"live: combat ownstate 0x{st:x} plays ({enterRow?.Play} {enterRow?.What}): asked [{string.Join(", ", asked)}], body [{string.Join(", ", shown)}], hidden {wasHidden}, now hidden {live.OwnPlayer.IsHidden}");
                if (enterRow?.Play == Ms2.Ui.StateEnterPlay.Sequence && castState && expect == 0)
                    // a 0 ms cast completes in the enter and the state's first update leaves: the sequence is asked for and
                    // shown for no frame, as the client's
                    Check($"server state 0x{st:x} asks for its sequence and leaves on its first update ({enterPlayed})",
                        enterPlayed == Ms2.Ui.OwnStateExit.AutoInteractionSequence && took < 0.1);
                else if (enterRow?.Play == Ms2.Ui.StateEnterPlay.Sequence)
                    Check($"the body plays server state 0x{st:x}'s sequences as the client ({string.Join(", ", asked)})",
                        asked.Count > 0 && asked.Where(n => n != Ms2.Ui.OwnStateExit.TalkAction).All(n => shown.Contains(n.ToLowerInvariant())));
                else if (enterRow?.Play == Ms2.Ui.StateEnterPlay.Hide)
                    Check($"the body is hidden for server state 0x{st:x} and shown after it", wasHidden && !live.OwnPlayer.IsHidden);
                bool ok = entered && row?.Cause switch
                {
                    Ms2.Ui.StateExitCause.AtOnce or Ms2.Ui.StateExitCause.Fallback => live.Me.State != st && took < 0.1,
                    // StateLadder / StateRope with nothing in reach: StateIdle at once
                    Ms2.Ui.StateExitCause.Body when st is 8 or 9 => live.Me.State != st && took < 0.1,
                    Ms2.Ui.StateExitCause.InputStep => live.Me.State != st && took < 0.2,
                    Ms2.Ui.StateExitCause.Timer or Ms2.Ui.StateExitCause.SequenceEnd =>
                        expect > 0 && live.Me.State != st && System.Math.Abs(took - expect) < 0.15 && moved < 0.05f,
                    Ms2.Ui.StateExitCause.NoSelfExit => live.Me.State == st && moved < 0.05f,
                    // the bar shows for the cast's time (a 0 ms cast the movie completes at once) and its finish leaves; a
                    // walk leaves at once and stops the bar
                    Ms2.Ui.StateExitCause.CastingBar when moveOut => cast && live.Me.State != st && took < 0.5 && !barAfter && moved > 0.05f
                        && shown.Any(n => n.StartsWith("run", StringComparison.Ordinal) || n.StartsWith("walk", StringComparison.Ordinal)),
                    Ms2.Ui.StateExitCause.CastingBar => cast && live.Me.State != st && !barAfter && moved < 0.05f
                        && (expect > 0 ? barRan && System.Math.Abs(took - expect) < 0.15 : took < 0.1),
                    _ => false,
                } == true;
                Check($"the own pc leaves server state 0x{st:x} as the client ({row?.Cause})", ok);
                if (st == 0x12)   // StateStun::vfunc_4's face (statestun.cpp:112)
                    Check($"server state 0x12 plays the Stun face ({live.OwnStateFaces - faces0} play(s), last {live.LastOwnStateFace}, played {live.LastOwnStateFacePlayed}, face now {live.OwnPlayer.FaceEmotion?.Current ?? "none"})",
                        live.OwnStateFaces == faces0 + 1 && live.LastOwnStateFace == Ms2.Ui.OwnStun.Face && (live.LastOwnStateFacePlayed || face0 == Ms2.Ui.OwnStun.Face));
                if (st == 0x1c)   // StateSwimDash's dash effect at the pc after its fallback, each entry stopping the last (stateswimdash.cpp:92-108, 221-233)
                {
                    int entries = live.ServerStatesEntered - before, started = live.SwimDashEffects - dash0, stopped = live.SwimDashEffectsStopped - dashStops0;
                    Check($"server state 0x1c starts its kept dash effect at the pc on each of its {entries} entries, the last stopped first ({live.LastSwimDashEffect}, id {live.SwimDashEffectId}, started {started}, stopped {stopped})",
                        entries > 0 && started == entries && stopped == started - (dash0 == 0 ? 1 : 0)
                        && live.SwimDashEffectId != 0 && live.LastSwimDashEffect == live.SwimDashEffectName());
                }
                if (st is 0x1d or 8 or 9)   // SetShadowVisible(false) after the fallback to StateIdle (state.cpp:1038, stateladder.cpp:202, statemanager.cpp:1262)
                {
                    bool has = live.OwnPlayer.HasSimpleShadow;
                    Check($"server state 0x{st:x} leaves the own shadow off ({(has ? $"simple shadow shown {live.OwnPlayer.ShadowShown}" : "no simple shadow at this level")})",
                        !has || !live.OwnPlayer.ShadowShown);
                }
                await frames(30);
                continue;
            }
            if (step.StartsWith("talkstate:"))      // talkstate:<npc id>: a spawned npc's talk opened (NpcTalk 1), then the server's StateTalk 0x16
            {
                int nid = int.Parse(step[10..]);
                int added = npcs;
                hud.SubmitChatLine("/npc " + nid);
                await until(() => npcs > added && lastNpc?.Row?.Id == nid, 60 * 5); await frames(60);
                var npc = lastNpc;
                bool spawned = npc?.Row?.Id == nid && npc.ObjectId != 0;
                if (spawned) { hud.FaceScripted(npc.Position); live.SendTalk(new Ms2.Net.Packets.NpcTalkBeginRequest(npc.ObjectId)); }
                await until(() => live.TalkNpcId != 0, 60 * 5); await frames(10);
                // an npc with no talk script in this data closes the talk at once (FUN_141b1ff80): no talk npc, IDLE
                bool open = spawned && live.TalkNpcId == npc.ObjectId && live.IsTalking();
                int before = live.ServerStatesEntered;
                bool sent = hud.SubmitChatLine("/debug packet " + live.OwnUserStateBytes(0x16));
                await until(() => live.ServerStatesEntered > before, 60 * 5);
                var shown = new System.Collections.Generic.List<string>();
                ulong f0 = Engine.GetProcessFrames();
                bool upShot = false, idleShot = false;
                bool left = false;
                await until(() =>
                {
                    string cur = live.OwnPlayer.CurrentAnim;
                    if (shown.Count == 0 || shown[^1] != cur) shown.Add(cur);
                    ulong n = Engine.GetProcessFrames() - f0;
                    if (live.Me.State != 0x16) left = true;
                    if (!upShot && n >= 20) { Shot($"talkstate-{nid}-up.png"); upShot = true; }
                    if (!idleShot && n >= 150) { Shot($"talkstate-{nid}-idle.png"); idleShot = true; }
                    return n >= 180;
                }, 300);
                // statetalk.cpp:101-137: the talk npc's talkAni and the own TALK_UP and TALK_IDLE -> TALK_UP forced (ChatUp_A clamps),
                // its end -> phase 0x48: TALK_IDLE with the own chat balloon active, else LISTEN_IDLE (162-178, 215-222); else IDLE.
                // vfunc_8 is empty (209-211): the body stays in StateTalk.
                bool up = open && npc.Row.TalkAni && live.OwnPlayer.HasTalkUp && live.OwnPlayer.HasTalkIdle;
                string played = live.OwnStatePlayed;
                bool loops = live.OwnStatePlayedLoops;
                bool balloon = live.OwnBalloonActive();
                string idle = Ms2.Ui.OwnStateExit.TalkIdleOf(balloon);
                int phase = live.OwnTalkPhase;
                GD.Print($"live: combat talkstate {nid}: spawned {spawned}, talk open {open} (npc {live.TalkNpcId}), talkAni {npc?.Row?.TalkAni}, TALK_UP {live.OwnPlayer.TalkUpSequence ?? "none"}, sent {sent}, state {live.Me.State} (left {left}), phase 0x{phase:x}, plays {played} loops {loops}, own balloon {balloon}, body [{string.Join(", ", shown)}]");
                Check($"StateTalk with npc {nid}'s talk {(open ? "open" : "closed (no talk script)")} (talkAni {npc?.Row?.TalkAni}) plays {(up ? $"TALK_UP once, then {idle}" : "IDLE")} and holds the body as the client",
                    spawned && live.Me.State == 0x16 && !left
                    && (up ? phase == Ms2.Ui.OwnStateExit.TalkPhaseIdle && played == idle && loops
                             && shown.IndexOf(live.OwnPlayer.TalkUpSequence) is >= 0 and var ui && ui < shown.Count - 1
                           : phase == Ms2.Ui.OwnStateExit.TalkPhaseNone && played == Ms2.Ui.OwnStateExit.TalkAction));
                // StateTalk::vfunc_5 (statetalk.cpp:142-150): the state left with the talk open sends NpcTalk 0
                int ends0 = live.OwnTalkEnds;
                bool wasTalking = live.IsTalking();
                hud.SubmitChatLine("/debug packet " + live.OwnUserStateBytes(1));
                await until(() => live.Me.State != 0x16, 60 * 5);
                await frames(10);
                Check(wasTalking ? $"StateTalk left with npc {nid}'s talk open ends the talk (NpcTalk 0)" : $"StateTalk left with no talk open sends nothing (npc {nid})",
                    wasTalking ? live.OwnTalkEnds == ends0 + 1 && !live.IsTalking() : live.OwnTalkEnds == ends0);
                if (live.IsTalking()) live.EndTalk();
                await frames(60);
                continue;
            }
            if (step.StartsWith("buffstun:"))      // buffstun:<effect id>:<level>:<ms>: a raw OnBuff adding a stun effect to the own pc, then its removal
            {
                var c = step[9..].Split(':');
                uint id = uint.Parse(c[0]); ushort lv = ushort.Parse(c[1]); uint ms = uint.Parse(c[2]);
                const int key = 0x7e000001;
                int before = live.BuffStunsEntered, faces0 = live.OwnStateFaces;
                bool sent = hud.SubmitChatLine("/debug packet " + live.OwnBuffBytes(0, key, id, lv, ms + 10000));
                await until(() => live.BuffStunsEntered > before, 60 * 5);
                bool entered = live.BuffStunsEntered > before;
                var got = live.LastOwnBuff;
                GD.Print($"live: combat buffstun {id}: own buff seen {(got == null ? "none" : $"mode {got.Mode} key {got.Key:x} effect {got.State?.Id} level {got.State?.Level}")}, stun entry {live.OwnBuffStun()?.ToString() ?? "none"}");
                var asked = new List<string>(); var shown = new List<string>();
                void Watch()
                {
                    if (live.OwnStatePlayed is { } pl && (asked.Count == 0 || asked[^1] != pl)) asked.Add(pl);
                    string cur = live.OwnPlayer.CurrentAnim;
                    if (shown.Count == 0 || shown[^1] != cur) shown.Add(cur);
                }
                ulong f0 = Engine.GetProcessFrames();
                var at = live.OwnPlayer.GlobalPosition;
                hud.DriveTowardScripted(at + new Vector3(600, 0, 0), false);   // the stun holds the body against a held move
                bool shotTaken = false;
                await until(() =>
                {
                    Watch();
                    if (!shotTaken && Engine.GetProcessFrames() - f0 >= (ulong)(ms * 60 / 2000)) { Shot($"buffstun-{id}.png"); shotTaken = true; }
                    return Engine.GetProcessFrames() - f0 >= (ulong)(ms * 60 / 1000);
                }, (int)(ms * 60 / 1000) + 60);
                bool held = live.Me.State == 0x12;
                hud.DriveEnd();
                var shift = live.OwnPlayer.GlobalPosition - at; shift.Y = 0;
                hud.SubmitChatLine("/debug packet " + live.OwnBuffBytes(1, key, 0, 0, 0));
                await until(() => { Watch(); return live.Me.State != 0x12; }, 60 * 8);
                GD.Print($"live: combat buffstun {id} level {lv}: sent {sent}, entered {entered}, held {held}, moved {shift.Length():0.##}, state now {live.Me.State}, asked [{string.Join(", ", asked)}], body [{string.Join(", ", shown)}]");
                Check($"a stun effect {id} holds the own pc in StateStun, lying and standing up as the client",
                    entered && held && shift.Length() < 0.05f && live.Me.State != 0x12 && asked.Count >= 2
                    && asked.All(n => shown.Contains(n.ToLowerInvariant())) && asked[^1].StartsWith("Stnd_Up_", StringComparison.Ordinal));
                Check($"the stun buff's StateStun plays the Stun face once ({live.OwnStateFaces - faces0} play(s), last {live.LastOwnStateFace}, played {live.LastOwnStateFacePlayed})",
                    live.OwnStateFaces == faces0 + 1 && live.LastOwnStateFace == Ms2.Ui.OwnStun.Face);
                await frames(30);
                continue;
            }
            if (step.StartsWith("awaken:"))      // awaken:<job code name>: the GM job change (Job mode 2), the effect on the PC and UIChangeJobEffect
            {
                int effects0 = live.JobChangeEffects;
                GD.Print($"live: combat {step}: {(hud.SubmitChatLine("/player job " + step[7..] + " awakening") ? "sent" : "no chat window")}");
                await until(() => hud.ChangeJobEffectUp, 60 * 10);
                bool up = hud.ChangeJobEffectUp;
                await frames(60); Shot("jobchange-effect.png");
                await until(() => !hud.ChangeJobEffectUp, 60 * 20);
                GD.Print($"live: combat {step}: pc effects {live.JobChangeEffects - effects0}, window up {up}, hidden {!hud.ChangeJobEffectUp}");
                Check("the job change plays the changeJobs effect on the own pc", live.JobChangeEffects > effects0);
                Check("the job change shows UIChangeJobEffect and it hides at its end", up && !hud.ChangeJobEffectUp);
                continue;
            }
            if (step.StartsWith("chat:"))      // one chat line, no comma in it
            {
                GD.Print($"live: combat {step}: {(hud.SubmitChatLine(step[5..]) ? "sent" : "no chat window")}");
                await frames(120);
                continue;
            }
            if (step.StartsWith("aimcast:"))      // aimcast:<skill id>[:<frames to wait after>]: a mob in front, the player turned to it, then the cast
            {
                var parts = step[8..].Split(':');
                if (parts[^1] == "new") parts = parts[..^1];
                uint sid = uint.Parse(parts[0]);
                int gap = parts.Length > 1 ? int.Parse(parts[1]) : 165;
                int mob = int.Parse(Env("MS2_LIVE_COMBAT_MOB", "0")), added = npcs;
                if (step.EndsWith(":new") || aimMobs == 0 || kills > aimKills)
                {
                    aimMobs++; aimKills = kills;
                    hud.SubmitChatLine("/npc " + mob);
                    await until(() => npcs > added, 60 * 5); await frames(120);
                }
                float dist = lastNpc?.Root != null && GodotObject.IsInstanceValid(lastNpc.Root) ? hud.FaceScripted(lastNpc.Position) : -1;
                for (int walk = 0; walk < 60 * 25 && dist > 250 && lastNpc?.Root != null && GodotObject.IsInstanceValid(lastNpc.Root); walk++)
                {      // the mob spawned far away: a run to it
                    dist = hud.DriveTowardScripted(lastNpc.Position, false); await frames(1);
                }
                hud.DriveEnd(); await frames(20);
                if (lastNpc?.Root != null && GodotObject.IsInstanceValid(lastNpc.Root)) dist = hud.FaceScripted(lastNpc.Position);
                var target = lastNpc; int before = hitCount.GetValueOrDefault(target);
                GD.Print($"live: combat {step}: distance {dist:F0}: {(hud.CastSkillScripted(sid) ? "cast" : "refused")}");
                await frames(gap);
                GD.Print($"live: combat {step}: numbers on the mob {hitCount.GetValueOrDefault(target) - before}");
                Shot($"aimcast{sid}_{Math.Abs(step.GetHashCode()) % 1000}.png");
                continue;
            }
            // cast:<skill id>[:<frames to wait after>[:<frame>/<frame>...]]: the own cast, shots before, during and after;
            // more shots at the listed frames after the cast (each past 15 and before the wait's end). With
            // MS2_LIVE_FX_TRACE=1 and a frame list, the shot before and at 15 are left out and each effect the own skill
            // state keeps is shot two frames later instead (a shot stalls the frame, so timed shots miss a short effect)
            if (step.StartsWith("cast:"))
            {
                var parts = step[5..].Split(':');
                uint sid = uint.Parse(parts[0]);
                int gap = parts.Length > 1 ? int.Parse(parts[1]) : 165;
                bool onKeep = parts.Length > 2 && System.Environment.GetEnvironmentVariable("MS2_LIVE_FX_TRACE") == "1";
                if (gap > 60 && !onKeep) Shot($"cast{sid}_0.png");
                int kept = 0, keepShotAt = -1, frame = 0;
                void Kept(string fx) { kept++; if (keepShotAt < 0) keepShotAt = frame + 2; }
                if (onKeep) Ui.HudScene.OwnEffectKept += Kept;
                GD.Print($"live: combat {step}: {(hud.CastSkillScripted(sid) ? "cast" : "refused")} at {Time.GetTicksMsec()}");
                var list = parts.Length > 2 ? parts[2].Split('/').Select(int.Parse).Where(f => onKeep || f > 15).ToList() : new List<int>();
                for (; frame < gap; frame++)
                {
                    if (frame == 15 && gap > 60 && !onKeep) Shot($"cast{sid}_1.png");
                    if (frame == keepShotAt) { Shot($"cast{sid}_keep{kept}.png"); keepShotAt = -1; }
                    else if (list.Contains(frame)) Shot($"cast{sid}_f{frame}.png");
                    await frames(1);
                }
                if (onKeep) Ui.HudScene.OwnEffectKept -= Kept;
                continue;
            }
            // equip:<item id>: the item made by the GM item command and put on through the bag's equip action (as equip, with
            // the id in the step instead of MS2_LIVE_COMBAT_WEAPON)
            if (step.StartsWith("equip:"))
            {
                int item = int.Parse(step[6..]);
                GD.Print($"live: combat {step}: item {(hud.SubmitChatLine($"/item {item}") ? "sent" : "no chat window")}");
                await until(() => live.Items.All.Any(e => e.Id == item), 60 * 10); await frames(60);
                Check($"{item} is in the bag", live.Items.All.Any(e => e.Id == item));
                GD.Print($"live: combat {step}: {(hud.EquipScripted(item) || EquipDirect(item, false) ? "sent" : "not sent")}");
                await until(() => live.Me.Equips?.Any(e => e.Id == item) == true, 60 * 10); await frames(60);
                Check($"{item} is worn", live.Me.Equips?.Any(e => e.Id == item) == true);
                Shot($"equip{item}.png");
                continue;
            }
            // cooldown:<skill id>: the quick slots' cooldown answer for the skill (HudScene.CooldownScripted), logged
            if (step.StartsWith("cooldown:"))
            {
                GD.Print($"live: combat {step}: {hud.CooldownScripted(uint.Parse(step[9..]))}");
                continue;
            }
            if (step.StartsWith("level:"))
            {
                int level = int.Parse(step[6..]);
                GD.Print($"live: combat {step}: {(hud.SubmitChatLine("/player level " + level) ? "sent" : "no chat window")}");
                await until(() => live.Me.User.Level == level, 60 * 20); await frames(60);
                Check($"the character is level {level}", live.Me.User.Level == level);
                continue;
            }
            if (step.StartsWith("store"))      // the bank steps: storeopen, storegive:N, storedeposit:N, storewithdraw:N, storemove, storemeso{deposit|withdraw}:N, storesort, storeclose
            {
                int sid = storeItem;
                long Bank() { long n = 0; foreach (var e in live.Storage.All) if (e.Id == sid) n += e.Amount; return n; }
                long Bag() { long n = 0; foreach (var e in live.Items.All) if (e.Id == sid) n += e.Amount; return n; }
                string arg = step.Contains(':') ? step[(step.IndexOf(':') + 1)..] : "";
                string name = step.Contains(':') ? step[..step.IndexOf(':')] : step;
                long bag0 = Bag(), bank0 = Bank(), meso0 = live.Storage.Meso;
                int slots0 = live.Storage.Count;
                switch (name)
                {
                    case "storeopen":
                    {
                        GD.Print("live: combat store: opener not exercised (the play opener does not exist on the scratch server)");
                        GD.Print($"live: combat store open: {(hud.StoreOpenScripted() ? "open" : "not open")}");
                        await until(() => live.Storage.Ready, 60 * 10); await frames(60);
                        Check($"the bank is loaded ({live.Storage.Count} item(s), {live.Storage.Meso} meso)", live.Storage.Ready);
                        Shot("store_open.png");
                        break;
                    }
                    case "storegive":
                    {
                        foreach (var cand in Env("MS2_LIVE_STORE_ITEM", "0").Split(';'))      // the first id the GM item command gives
                        {
                            storeItem = sid = int.Parse(cand); bag0 = Bag();
                            hud.SubmitChatLine($"/item {sid} --amount {arg}");
                            await until(() => Bag() != bag0, 60 * 3); await frames(30);
                            if (Bag() != bag0) break;
                        }
                        Check($"the bag holds the item ({bag0} -> {Bag()})", Bag() == bag0 + long.Parse(arg));
                        break;
                    }
                    case "storedeposit":
                    {
                        int n = int.Parse(arg);
                        var held = live.Items.All.FirstOrDefault(e => e.Id == sid);
                        GD.Print($"live: combat store deposit {n}: {(held != null && hud.StoreDepositScripted(held.Uid, n) ? "sent" : "not sent")}");
                        await until(() => Bank() != bank0, 60 * 10); await frames(60);
                        Check($"the bank holds the deposit ({bank0} -> {Bank()}), the bag lost it ({bag0} -> {Bag()})", Bank() == bank0 + n && Bag() == bag0 - n);
                        Shot("store_deposit.png");
                        break;
                    }
                    case "storewithdraw":
                    {
                        int n = int.Parse(arg);
                        int slot = -1; foreach (var e in live.Storage.All) if (e.Id == sid) { slot = e.InventorySlot; break; }
                        GD.Print($"live: combat store withdraw {n} from slot {slot}: {(slot >= 0 && hud.StoreWithdrawScripted(slot, n) ? "sent" : "not sent")}");
                        await until(() => Bank() != bank0, 60 * 10); await frames(60);
                        Check($"the bank lost the withdrawal ({bank0} -> {Bank()}), the bag has it ({bag0} -> {Bag()})", Bank() == bank0 - n && Bag() == bag0 + n);
                        Shot("store_withdraw.png");
                        break;
                    }
                    case "storemove":      // a bank slot dragged onto the first free one (the merge/split of the bank's own slots)
                    {
                        int from = -1; foreach (var e in live.Storage.All) if (e.Id == sid) { from = e.InventorySlot; break; }
                        int to = live.Storage.EmptySlot();
                        GD.Print($"live: combat store move {from} -> {to}: {(from >= 0 && hud.StoreMoveScripted(from, to) ? "sent" : "not sent")}");
                        await until(() => live.Storage.Slot(to) != null, 60 * 10); await frames(60);
                        Check($"the item moved to bank slot {to}", live.Storage.Slot(to) != null && live.Storage.Slot(from) == null);
                        Shot("store_move.png");
                        break;
                    }
                    case "storemesodeposit":
                    case "storemesowithdraw":
                    {
                        long n = long.Parse(arg); bool dep = name == "storemesodeposit";
                        long wallet0 = hud.WalletMeso;
                        GD.Print($"live: combat store meso {name} {n}: {(hud.StoreMesoScripted(dep, n) ? "sent" : "not sent")}");
                        await until(() => live.Storage.Meso != meso0, 60 * 10); await frames(60);
                        long wantBank = dep ? meso0 + n : meso0 - n;
                        Check($"the bank meso is {live.Storage.Meso} (wanted {wantBank})", live.Storage.Meso == wantBank);
                        Check($"the wallet is {hud.WalletMeso} (was {wallet0})", hud.WalletMeso == (dep ? wallet0 - n : wallet0 + n));
                        Shot("store_" + name + ".png");
                        break;
                    }
                    case "storesort":
                        GD.Print($"live: combat store sort: {(hud.StoreSortScripted() ? "sent" : "not sent")}");
                        await frames(240); Shot("store_sort.png");
                        Check($"the bank still has {live.Storage.Count} item(s) (was {slots0})", live.Storage.Count == slots0);
                        break;
                    case "storeclose":
                        GD.Print($"live: combat store close: {(hud.StoreCloseScripted() ? "sent" : "not open")}");
                        await frames(120); Shot("store_close.png");
                        break;
                    default: GD.Print($"live: combat: unknown step {step}"); break;
                }
                continue;
            }
            if (step.StartsWith("bag"))      // the bag steps: baggive:N (MS2_LIVE_BAG_ITEM), bagkeep (MS2_LIVE_BAG_KEEP, one item), bagopen, bagsort[:id], bagui, bagdrop:N, bagdestroy, bagexpand[:id]
            {
                string arg = step.Contains(':') ? step[(step.IndexOf(':') + 1)..] : "";
                string name = step.Contains(':') ? step[..step.IndexOf(':')] : step;
                long Count(int id) { long n = 0; foreach (var e in live.Items.All) if (e.Id == id) n += e.Amount; return n; }
                var boxes = new System.Collections.Generic.List<Ms2.Ui.Hud.MessageBoxData>();
                hud.WatchBagBoxes(boxes.Add);
                string Tags() => string.Join(";", boxes.Select(b => b.Extra));
                Ms2.Ui.Hud.MessageBoxData Box(string tag) => boxes.LastOrDefault(b => b.Extra == tag);
                async Task<bool> Pointer(bool overUi)      // the mouse moved over the open bag, or onto the field away from every window
                {
                    var points = new System.Collections.Generic.List<Vector2>();
                    if (overUi) { if (hud.BagScreenPoint() is { } p) points.Add(p); }
                    else points.AddRange(new[] { new Vector2(120, 330), new Vector2(240, 220), new Vector2(400, 420), new Vector2(640, 300) });
                    foreach (var p in points)
                    {
                        Input.WarpMouse(p); await frames(12);
                        if (hud.CursorOverUiNow() == overUi) { GD.Print($"live: combat bag pointer at {p} over the UI {overUi}"); return true; }
                    }
                    return false;
                }
                switch (name)
                {
                    case "bagopen":      // the bag opened as the key does
                        Check("the bag is open", hud.OpenInventoryScripted());
                        await frames(60);
                        break;
                    case "baggive": case "bagkeep":
                    {
                        int id = int.Parse(Env(name == "baggive" ? "MS2_LIVE_BAG_ITEM" : "MS2_LIVE_BAG_KEEP", "0"));
                        long n0 = Count(id);
                        hud.SubmitChatLine(name == "baggive" ? $"/item {id} --amount {arg}" : $"/item {id}");
                        await until(() => Count(id) != n0, 60 * 5); await frames(30);
                        Check($"the bag holds item {id} ({n0} -> {Count(id)})", Count(id) == n0 + (name == "baggive" ? long.Parse(arg) : 1));
                        break;
                    }
                    case "bagsort":      // bagsort[:item id]: the tab of that item, else of MS2_LIVE_BAG_ITEM
                    {
                        int id = arg.Length > 0 ? int.Parse(arg) : int.Parse(Env("MS2_LIVE_BAG_ITEM", "0")); int tab = live.Items.TabOf(id);
                        long n0 = Count(id); int changed = 0;
                        System.Action<long> onChanged = _ => changed++;
                        live.Items.Changed += onChanged;
                        Check($"the bag shows tab {tab}", hud.BagSlotScripted(id, tab) != null);
                        GD.Print($"live: combat bag sort: {(hud.BagSortScripted() ? "clicked" : "not clicked")}");
                        await until(() => changed > 0, 60 * 5); await frames(60);
                        live.Items.Changed -= onChanged;
                        Check($"the server answered the sort of tab {tab} ({changed} change(s)), the item kept ({n0} -> {Count(id)})", changed > 0 && Count(id) == n0);
                        Shot("bag_sort.png");
                        break;
                    }
                    case "bagui":
                    {
                        int id = int.Parse(Env("MS2_LIVE_BAG_ITEM", "0")); long n0 = Count(id);
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        Check("the pointer is over the bag", await Pointer(true));
                        Check($"the drag ended on no slot ({slot})", slot != null && hud.BagDragEndScripted(slot));
                        await frames(120);
                        Check($"over the UI the item returns: no box ({Tags()}), the bag kept it ({n0} -> {Count(id)})", boxes.Count == 0 && Count(id) == n0);
                        break;
                    }
                    case "bagdrop":
                    {
                        int id = int.Parse(Env("MS2_LIVE_BAG_ITEM", "0")); long n0 = Count(id); int n = int.Parse(arg);
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        var held = live.Items.All.FirstOrDefault(e => e.Id == id);
                        Check("the pointer is over the field", await Pointer(false));
                        Check($"the drag ended on no slot ({slot})", slot != null && hud.BagDragEndScripted(slot));
                        await frames(30);
                        var dropBox = Box(Ms2.Ui.Hud.InventoryController.DropBox);
                        Check($"the stack asks in the drop box ({Tags()}), its count {dropBox?.InputDefault}", dropBox != null && dropBox.InputDefault == n0.ToString());
                        Shot("bag_dropbox.png");
                        GD.Print($"live: combat bag drop {n}: {(hud.BagBoxAnswerScripted(dropBox, 0, $"drop,{n}") ? "answered" : "no box")}");
                        await until(() => Count(id) != n0, 60 * 5); await frames(60);
                        Check($"the bag lost the drop ({n0} -> {Count(id)})", Count(id) == n0 - n);
                        Shot("bag_drop.png");
                        break;
                    }
                    case "bagdestroy":
                    {
                        int id = int.Parse(Env("MS2_LIVE_BAG_KEEP", "0")); long n0 = Count(id);
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        var held = live.Items.All.FirstOrDefault(e => e.Id == id);
                        GD.Print($"live: combat bag destroy target {held?.Uid} rule {held?.Transfer.Rule}");
                        Check("the pointer is over the field", await Pointer(false));
                        Check($"the drag ended on no slot ({slot})", slot != null && hud.BagDragEndScripted(slot));
                        await frames(30);
                        var destroyBox = Box(Ms2.Ui.Hud.InventoryController.DestroyBox);
                        Check($"an item that cannot go on the ground asks in the destroy box ({Tags()}): {destroyBox?.Msg}", destroyBox != null);
                        Shot("bag_destroybox.png");
                        GD.Print($"live: combat bag destroy: {(hud.BagBoxAnswerScripted(destroyBox, 0, Ms2.Ui.Hud.InventoryController.DestroyBox) ? "answered" : "no box")}");
                        await until(() => Count(id) != n0, 60 * 5); await frames(60);
                        Check($"the item is gone ({n0} -> {Count(id)})", Count(id) == n0 - (held?.Amount ?? 0));
                        break;
                    }
                    case "bagsearch":      // the search of MS2_LIVE_BAG_ITEM's name in another case, then of a name no item has
                    {
                        int id = int.Parse(Env("MS2_LIVE_BAG_ITEM", "0"));
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        string itemName = hud.BagItemName(slot) ?? "";
                        string query = new string(itemName.Select(ch => ch >= 'a' && ch <= 'z' ? (char)(ch - 32) : ch).ToArray());
                        GD.Print($"live: combat bag search \"{query}\": {(hud.BagSearchScripted(query) ? "searched" : "no dialog")}");
                        await frames(10);
                        var (marked, scroll) = hud.BagSearchState(slot);
                        Check($"the search finds {slot}, marked {marked}, the list at row {scroll}, no box ({Tags()})", slot != null && marked && boxes.Count == 0);
                        Shot("bag_search.png");
                        hud.BagSearchScripted("zzzz");
                        await frames(10);
                        var none = boxes.LastOrDefault();
                        Check($"a name no item has says so: {none?.Msg}", none != null && none.Msg == hud.CommonText(0x937) && !hud.BagSearchState(slot).Marked);
                        GD.Print($"live: combat bag search box: {(hud.BagBoxAnswerScripted(none, 0, none?.Extra ?? "") ? "answered" : "no box")}");
                        await frames(10);
                        break;
                    }
                    case "bagexpand":      // bagexpand[:item id], as bagsort
                    {
                        int id = arg.Length > 0 ? int.Parse(arg) : int.Parse(Env("MS2_LIVE_BAG_ITEM", "0")); int tab = live.Items.TabOf(id);
                        hud.BagSlotScripted(id, tab);
                        long merat0 = hud.WalletMerat;
                        if (merat0 < 390) { hud.SubmitChatLine("/player currency meret 390"); await until(() => hud.WalletMerat >= 390, 60 * 5); await frames(30); }
                        long merat1 = hud.WalletMerat;
                        int extra0 = live.Items.ExtraSlots.TryGetValue(tab, out var e0) ? e0 : 0;
                        GD.Print($"live: combat bag expand tab {tab} (extra {extra0}, merets {merat0} -> {merat1}): {(hud.BagExpandScripted() ? "clicked" : "no dialog")}");
                        await frames(30);
                        var expandBox = Box(Ms2.Ui.Hud.InventoryController.ExpandSlotBox);
                        Check($"the expand asks ({Tags()}): {expandBox?.Msg}", expandBox != null);
                        Shot("bag_expandbox.png");
                        GD.Print($"live: combat bag expand box: {(hud.BagBoxAnswerScripted(expandBox, 0, Ms2.Ui.Hud.InventoryController.ExpandSlotBox) ? "answered" : "no box")}");
                        await until(() => (live.Items.ExtraSlots.TryGetValue(tab, out var e) ? e : 0) != extra0, 60 * 5); await frames(90);
                        int extra1 = live.Items.ExtraSlots.TryGetValue(tab, out var e1) ? e1 : 0;
                        Check($"tab {tab} has 6 more slots ({extra0} -> {extra1})", extra1 == extra0 + 6);
                        Check($"the server took the price ({merat1} -> {hud.WalletMerat})", hud.WalletMerat == merat1 - 390);
                        Shot("bag_expand.png");
                        var done = hud.ShownBoxScripted();      // the answer (mode 0xa): box 0x2a2, closed as its OK closes it
                        Check($"the expand's answer box shows: {done?.Msg}", done != null && done.Msg == hud.CommonText(0x2a2));
                        GD.Print($"live: combat bag expand answer: {(hud.BagBoxAnswerScripted(done, 0, done?.Extra ?? "") ? "answered" : "no box")}");
                        await frames(30);
                        break;
                    }
                    default: GD.Print($"live: combat: unknown step {step}"); break;
                }
                hud.WatchBagBoxes(null);
                continue;
            }
            if (step.StartsWith("lock"))      // the item lock steps: lockgive, lockopen, lockselect, lockconfirm, lockcheck, lockunlock, lockclose
            {
                string lname = step;
                switch (lname)
                {
                    case "lockgive":
                    {
                        foreach (var cand in Env("MS2_LIVE_LOCK_ITEM", "0").Split(';'))      // the first id the GM item command gives
                        {
                            lockId = int.Parse(cand);
                            var known = live.Items.All.Select(e => e.Uid).ToHashSet();
                            hud.SubmitChatLine($"/item {lockId}");
                            await until(() => live.Items.All.Any(e => e.Id == lockId && !known.Contains(e.Uid)), 60 * 3); await frames(30);
                            if (live.Items.All.FirstOrDefault(e => e.Id == lockId && !known.Contains(e.Uid)) is { } got) { lockUid = got.Uid; break; }
                        }
                        Check($"the bag holds the item to lock (id {lockId})", lockUid != 0 && live.Items.Get(lockUid) != null);
                        break;
                    }
                    case "lockopen":
                        Check("the lock dialog is open", hud.LockOpenScripted());
                        await frames(90); Shot("lock_open.png");
                        break;
                    case "lockselect":
                    {
                        bool sent = hud.LockSelectScripted(lockUid);
                        GD.Print($"live: combat lock select: {(sent ? "sent" : "not sent")}");
                        await until(() => live.LockBook.Contains(lockUid), 60 * 10); await frames(60);
                        Check("the item is in the lock bag (the server's answer)", live.LockBook.Contains(lockUid));
                        Shot("lock_select.png");
                        break;
                    }
                    case "lockconfirm":
                    {
                        byte state0 = live.Items.Get(lockUid)?.LockState ?? 0; long at0 = live.Items.Get(lockUid)?.UnlockTime ?? 0;
                        GD.Print($"live: combat lock confirm: {(hud.LockConfirmScripted() ? "asked and answered yes" : "no box")}");
                        await until(() => !live.LockBook.Contains(lockUid), 60 * 10); await frames(60);
                        var r = hud.LockRulesScripted(lockUid);
                        GD.Print($"live: combat lock state: {state0} -> {r.State}, unlock time {at0} -> {r.UnlockAt}, server time {r.Now}");
                        Check("the item left the lock bag after the answer", !live.LockBook.Contains(lockUid));
                        Shot("lock_confirm.png");
                        break;
                    }
                    case "lockcheck":
                    {
                        var r = hud.LockRulesScripted(lockUid);
                        GD.Print($"live: combat lock rules: not locked={r.NotLocked} state={r.State} unlock time={r.UnlockAt} server time={r.Now}");
                        Check("the item is locked (a trade, a shop sale and a dismantle are refused by the lock rule)", !r.NotLocked);
                        break;
                    }
                    case "lockfree":
                    {
                        var r = hud.LockRulesScripted(lockUid);
                        GD.Print($"live: combat lock rules: not locked={r.NotLocked} state={r.State} unlock time={r.UnlockAt} server time={r.Now}");
                        Check("the item is free (the lock rule lets it through)", r.NotLocked);
                        break;
                    }
                    case "lockunlock":
                    {
                        Check("the unlock tab is up", hud.LockTabScripted(1));
                        GD.Print($"live: combat lock select (unlock): {(hud.LockSelectScripted(lockUid) ? "sent" : "not sent")}");
                        await until(() => live.LockBook.Contains(lockUid), 60 * 10); await frames(60);
                        Check("the item is in the unlock bag", live.LockBook.Contains(lockUid));
                        GD.Print($"live: combat lock unlock confirm: {(hud.LockConfirmScripted() ? "asked and answered yes" : "no box")}");
                        await until(() => !live.LockBook.Contains(lockUid), 60 * 10); await frames(60);
                        var r = hud.LockRulesScripted(lockUid);
                        GD.Print($"live: combat lock state after the unlock request: {r.State}, unlock time {r.UnlockAt}, server time {r.Now}, wait {r.UnlockAt - r.Now} s");
                        Shot("lock_unlock.png");
                        break;
                    }
                    case "lockclose":
                        hud.LockCloseScripted(); await frames(60); Shot("lock_close.png");
                        break;
                    default: GD.Print($"live: combat: unknown step {step}"); break;
                }
                continue;
            }
            if (step.StartsWith("enchant"))      // the item enchant steps: enchantopen, enchantselect, enchantpress, enchantclose
            {
                switch (step)
                {
                    case "enchantopen":
                        Check("the enchant window is open", hud.EnchantOpenScripted(Env("MS2_LIVE_ENCHANT_MODE", "")));
                        await frames(90); Shot("enchant_open.png");
                        break;
                    case "enchantselect":      // the worn weapon (MS2_LIVE_COMBAT_WEAPON, after the equip step) when MS2_LIVE_ENCHANT_WORN=1, else the first item of the bag
                    {
                        var held = hud.EnchantBagScripted();
                        GD.Print($"live: combat enchant bag: {held.Count} items");
                        int weapon = int.Parse(Env("MS2_LIVE_COMBAT_WEAPON", "0"));
                        long uid = Env("MS2_LIVE_ENCHANT_WORN", "0") == "1" ? (live.Me.Equips?.FirstOrDefault(e => e.Id == weapon)?.Uid ?? 0) : (held.Count > 0 ? held[0] : 0);
                        Check("the item is in the enchantable bag", uid != 0 && held.Contains(uid));
                        GD.Print($"live: combat enchant select {uid}: {(hud.EnchantSelectScripted(uid) ? "sent" : "not sent")}");
                        await until(() => hud.EnchantStateScripted().Selected == uid && hud.EnchantStateScripted().Ingredients > 0, 60 * 10); await frames(60);
                        var st = hud.EnchantStateScripted();
                        GD.Print($"live: combat enchant state: selected={st.Selected} state={st.State} ready={st.Ready} result item={st.ResultId} chance={st.Chance} ingredients={st.Ingredients}");
                        Check("the server's info is in (the ingredients and rates of the selected item)", st.Selected == uid && st.Ingredients > 0);
                        Shot("enchant_select.png");
                        break;
                    }
                    case "enchantgive":      // the selected item's costs into the bag (the server's GM item command)
                    {
                        foreach (var (id, n) in hud.EnchantCostsScripted())
                        {
                            GD.Print($"live: combat enchant give {id} x{n}: {(hud.SubmitChatLine($"/item {id} --amount {n}") ? "sent" : "no chat window")}");
                            await frames(60);
                        }
                        await frames(90); Shot("enchant_given.png");
                        break;
                    }
                    case "enchantpress":
                    {
                        int asked = hud.EnchantPressScripted();
                        GD.Print($"live: combat enchant press: boxes asked={asked}");
                        await frames(180);
                        var st = hud.EnchantStateScripted();
                        GD.Print($"live: combat enchant after press: selected={st.Selected} state={st.State} ready={st.Ready} result item={st.ResultId}");
                        Shot("enchant_press.png");
                        break;
                    }
                    case "enchantclose":
                        hud.EnchantCloseScripted(); await frames(60); Shot("enchant_close.png");
                        break;
                    default: GD.Print($"live: combat: unknown step {step}"); break;
                }
                continue;
            }
            switch (step.StartsWith("lift:") ? "lift" : step.StartsWith("mobloot:") || step.StartsWith("mobmeso:") ? "mobloot"
                : step.StartsWith("bagfull:") ? "bagfull" : step.StartsWith("autoloot:") || step.StartsWith("autofull:") ? "autoloot" : step)
            {
                case "attack":
                {
                    int mob = int.Parse(Env("MS2_LIVE_COMBAT_MOB", "0")), added = npcs;
                    GD.Print($"live: combat spawn: {(hud.SubmitChatLine("/npc " + mob) ? "sent" : "no chat window")}");
                    await until(() => npcs > added, 60 * 5); await frames(120);
                    Check("the mob is in the field", npcs > added);
                    Shot("attack_spawned.png");
                    int presses = 0, deadBefore = kills;
                    for (; presses < 30 && kills - deadBefore < npcs - added; presses++)
                    {
                        bool cast = hud.AttackScripted();
                        if (presses == 0) { Check("the attack key gave a cast request", cast); await frames(30); Shot("attack_cast.png"); }
                        await frames(20); if (presses < 3) Shot($"attack_press{presses}.png"); await frames(40);
                        if (presses == 1) Shot("attack_hit.png");
                    }
                    await frames(120);
                    Check($"damage numbers came back ({numbers})", numbers > 0);
                    Check($"the kill reached the client ({kills} exp line(s), {presses} press(es))", kills > deadBefore);
                    Shot("attack_end.png");
                    break;
                }
                case "aimattack":      // attack, the player turned to the mob before each press; hits (damage numbers) per press
                {
                    int mob = int.Parse(Env("MS2_LIVE_COMBAT_MOB", "0")), added = npcs;
                    hud.SubmitChatLine("/npc " + mob);
                    await until(() => npcs > added, 60 * 5); await frames(120);
                    Check("the mob is in the field", npcs > added && lastNpc != null);
                    int deadBefore = kills, hitPresses = 0, press = 0, need = int.Parse(Env("MS2_LIVE_COMBAT_PRESSES", "3"));
                    for (; press < 12 && press < need; press++)
                    {
                        if (kills > deadBefore)      // the mob died: a fresh one takes the next press, so the count is of presses at a live mob
                        {
                            deadBefore = kills; int again = npcs;
                            hud.SubmitChatLine("/npc " + mob);
                            await until(() => npcs > again, 60 * 5); await frames(120);
                        }
                        float dist = lastNpc?.Root != null && GodotObject.IsInstanceValid(lastNpc.Root) ? hud.FaceScripted(lastNpc.Position) : -1;
                        await frames(20);
                        var target = lastNpc;      // the number counts only when it lands on the mob the press aimed at
                        int before = hitCount.GetValueOrDefault(target);
                        hud.AttackScripted();
                        await until(() => hitCount.GetValueOrDefault(target) > before, 60 * 2);
                        bool hit = hitCount.GetValueOrDefault(target) > before;
                        if (hit) { hitPresses++; await frames(6); Shot($"aim_dmg_in{press}.png"); }
                        GD.Print($"live: combat aim press {press}: distance {dist:F0}, numbers on the mob {hitCount.GetValueOrDefault(target) - before}");
                        await frames(60);
                    }
                    Check($"enough presses to judge ({press}, need {need})", press >= need);
                    Check($"hits per press: {hitPresses} of {press}", press >= need && hitPresses == press);
                    break;
                }
                case "skipmovie":    // a playing cut scene movie skipped by the HUD's Esc (the port's stand-in for the movie's exit button), then until the server's stop
                {
                    await until(() => live.Movie.IsPlaying, 60 * 30);
                    int id = live.Movie.CutSceneId;
                    Check("a cut scene movie plays", live.Movie.IsPlaying);
                    if (!live.Movie.IsPlaying) break;
                    hud.Escape();
                    GD.Print($"live: combat skip movie {id}: Esc pressed");
                    await until(() => !live.Movie.IsPlaying, 60 * 15);
                    Check("the server stopped the movie", !live.Movie.IsPlaying);
                    break;
                }
                case "hitlast":      // the npc the server added last (no spawn of our own), faced and attacked until it dies or the field changes; up to MS2_LIVE_COMBAT_PRESSES presses
                {
                    await until(() => lastNpc != null, 60 * int.Parse(Env("MS2_LIVE_NPC_WAIT", "0")));     // MS2_LIVE_NPC_WAIT: seconds to wait for the server's npc
                    await frames(120);
                    // MS2_LIVE_NPC_NEAR=<units>: the own pc placed that far from the npc on the line to it first (no walk; the
                    // position goes out with the next sync, as the portal move step does)
                    if (lastNpc?.Root != null && GodotObject.IsInstanceValid(lastNpc.Root) && float.TryParse(Env("MS2_LIVE_NPC_NEAR", ""), out float near)
                        && live.player != null && GodotObject.IsInstanceValid(live.player))
                    {
                        var away = live.player.GlobalPosition - lastNpc.Position; away.Y = 0;
                        live.player.GlobalPosition = lastNpc.Position + (away.LengthSquared() > 1e-6f ? away.Normalized() : Vector3.Forward) * near + new Vector3(0, 5, 0);
                        live.player.Velocity = Vector3.Zero;
                        await frames(90);
                    }
                    var target = lastNpc; int deadBefore = kills, press = 0, need = int.Parse(Env("MS2_LIVE_COMBAT_PRESSES", "20"));
                    int? field = live.FieldId;
                    Check($"a server npc to attack ({target?.Row?.Id})", target != null);
                    for (; target != null && press < need && kills == deadBefore && live.FieldId == field && GodotObject.IsInstanceValid(target.Root); press++)
                    {
                        float dist = hud.FaceScripted(target.Position);
                        await frames(20);
                        int before = hitCount.GetValueOrDefault(target);
                        hud.AttackScripted();
                        await frames(90);
                        GD.Print($"live: combat hitlast press {press}: distance {dist:F0}, numbers on the npc {hitCount.GetValueOrDefault(target) - before}");
                    }
                    await frames(60 * 10);
                    bool gone = target != null && !GodotObject.IsInstanceValid(target.Root);
                    Check($"the npc died ({kills - deadBefore} exp line(s), removed {gone}, {press} press(es), field {field} -> {live.FieldId})", kills > deadBefore || gone || live.FieldId != field);
                    break;
                }
                case "death":
                {
                    int mob = int.Parse(Env("MS2_LIVE_COMBAT_MOB", "0")), count = int.Parse(Env("MS2_LIVE_COMBAT_MOBS", "3"));
                    for (int i = 0; i < count; i++) { hud.SubmitChatLine("/npc " + mob); await frames(20); }
                    await until(() => live.Me.Dead, 60 * 90); await frames(180);
                    Check("the own character is dead", live.Me.Dead);
                    Check("the tombstone exists", live.Tomb != null);
                    Check("the revival dialog or the auto-revive countdown is up", hud.RevivalUiUp());
                    // the tomb's model: MS2TombStoneDark while the revival is blocked (PC::FUN_14165cac0, pc.cpp:4043)
                    GD.Print($"live: combat death: revival blocked {live.OwnRevivalBlocked} (count {live.Me.RevivalConfirmCount}, flag {live.Me.RevivalFlag}, end tick {live.Me.RevivalEndTick})");
                    Shot("death.png");
                    break;
                }
                case "revive":
                {
                    Check("the revival dialog is open", hud.RevivalUiUp());
                    GD.Print($"live: combat free revive: {(hud.ReviveScripted(1) ? "sent" : "no dialog")}");
                    bool alive = false;     // seen at the revival itself: a mob that stands on the spot kills the revived character again
                    await until(() => alive = !live.Me.Dead, 60 * 15);
                    Check("the character is alive again", alive);
                    await frames(30);
                    GD.Print($"live: combat revived: penalty count {live.Me.RevivalConfirmCount}, end tick {live.Me.RevivalEndTick}");
                    Shot("revived.png");
                    break;
                }
                case "equip":
                {
                    int weapon = int.Parse(Env("MS2_LIVE_COMBAT_WEAPON", "0"));
                    GD.Print($"live: combat weapon item: {(hud.SubmitChatLine($"/item {weapon}") ? "sent" : "no chat window")}");
                    await until(() => live.Items.All.Any(e => e.Id == weapon), 60 * 10); await frames(60);
                    Check("the weapon is in the bag", live.Items.All.Any(e => e.Id == weapon));
                    GD.Print($"live: combat equip: {(hud.EquipScripted(weapon) || EquipDirect(weapon, false) ? "sent" : "not sent")}");
                    await until(() => live.Me.Equips?.Any(e => e.Id == weapon) == true, 60 * 10); await frames(60);
                    Check("the weapon is worn", live.Me.Equips?.Any(e => e.Id == weapon) == true);
                    Shot("equipped.png");
                    break;
                }
                case "equip2":     // a second copy of the weapon item, put on by the same equip action (the other hand)
                {
                    int weapon = int.Parse(Env("MS2_LIVE_COMBAT_WEAPON", "0"));
                    GD.Print($"live: combat weapon item 2: {(hud.SubmitChatLine($"/item {weapon}") ? "sent" : "no chat window")}");
                    await frames(120);
                    GD.Print($"live: combat equip 2: {(hud.EquipScripted(weapon, true) || EquipDirect(weapon, true) ? "sent" : "not sent")}");
                    await frames(120);
                    GD.Print($"live: combat worn: {string.Join(' ', live.Me.Equips?.Where(e => e.Id == weapon).Select(e => e.Slot.ToString()) ?? new string[0])}");
                    Shot("equipped2.png");
                    break;
                }
                case "lift":      // MS2_LIVE_LIFT_AT=x,y,z (the lift's map point): a run to it, then the action key in its reach
                {
                    var p = step.StartsWith("lift:") ? step[5..].Split(' ') : Env("MS2_LIVE_LIFT_AT", "0,0,0").Split(';')[0].Split(',');
                    var at = new Vector3(float.Parse(p[0]), float.Parse(p[2]), -float.Parse(p[1]));
                    float dist = hud.FaceScripted(at);
                    for (int walk = 0; walk < 60 * 25 && dist > 80; walk++)
                    {
                        dist = hud.DriveTowardScripted(at, false); await frames(1);
                    }
                    hud.DriveEnd(); hud.FaceScripted(at); await frames(60);
                    var mp = live.OwnPlayer.GlobalPosition;
                    GD.Print($"live: combat lift: distance {dist:F0} (3D {mp.DistanceTo(at):F0}) at {mp.X:F0} {-mp.Z:F0} {mp.Y:F0}, polled lift '{live.Liftables.Polled}', text field focus {Ms2.Ui.InputBlock.Active}");
                    Shot("lift_before.png");
                    await PressActionKey(frames);
                    await until(() => live.CubeCraft.InCubeMode, 60 * 5); await frames(90);
                    var me = live.OwnPlayer;
                    Check("the lift craft came back (cube mode, craft mode 2)", live.CubeCraft.InCubeMode && me.Craft.HoldsLift);
                    Check("the carried model is on the body", me.Craft.HasGuide2);
                    string idle = me.Anims.Name(Movement.ActionAnims.Act.CraftIdle);
                    Check($"the carry idle plays ({me.Anim.CurrentAnimation}, CRAFT_IDLE is {idle})", idle != null && me.Anim.CurrentAnimation == idle);
                    Shot("carry_idle.png");
                    string walkName = me.Anims.Name(Movement.ActionAnims.Act.CraftWalk), seen = "";
                    for (int i = 0; i < 50; i++) { hud.DriveScripted(new Vector2(0, 1), true); await frames(1); if (i == 40) seen = me.Anim.CurrentAnimation; }
                    Check($"the carry walk plays ({seen}, CRAFT_WALK is {walkName})", walkName != null && seen == walkName);
                    Shot("carry_walk.png");
                    hud.DriveEnd(); await frames(90);
                    break;
                }
                case "putdown":      // the action key in cube mode, where the player stands and faces
                {
                    Check("in cube mode before the put-down", live.CubeCraft.InCubeMode);
                    GD.Print($"live: combat put-down poll: state id {live.InteractPlayerStateId()}, server state {live.Me.State}, move {live.OwnPlayer?.State}, craft holds lift {live.CubeCraft.Current?.HoldsLift}");
                    await PressActionKey(frames);
                    GD.Print($"live: combat put-down after the press: state id {live.InteractPlayerStateId()}, server state {live.Me.State}, move {live.OwnPlayer?.State}");
                    await until(() => !live.CubeCraft.InCubeMode, 60 * 5); await frames(30);
                    var me = live.OwnPlayer;
                    Check("the craft ended after the put-down", !live.CubeCraft.InCubeMode && !me.Craft.HasGuide2);
                    Shot("putdown.png");
                    await frames(60 * 3);
                    GD.Print($"live: combat put-down: idle plays {me.Anim.CurrentAnimation}");
                    Shot("putdown_after.png");
                    break;
                }
                case "guidestep":     // MS2_LIVE_GUIDE_STEP: the saved tutorial step, then 6 s of guide ticks
                {
                    GD.Print($"live: combat guide step: {(hud.GuideStepScripted(int.Parse(Env("MS2_LIVE_GUIDE_STEP", "55"))) ? "set" : "no guide")}");
                    await frames(60 * 8);
                    GD.Print($"live: combat guide state: {hud.GuideStateText()}");
                    break;
                }
                case "jobchange":     // the game menu's jobchange entry (listed at the change level), its window, its change button
                {
                    await frames(60);
                    string menu = hud.ChangeJobMenuState();
                    GD.Print($"live: combat job change menu: {menu}");
                    Check("the game menu lists the job change entry", menu.StartsWith("listed True", StringComparison.Ordinal));
                    bool clicked = hud.ChangeJobMenuClickScripted();
                    await until(() => hud.ChangeJobWindowState() != null, 60 * 5); await frames(60);
                    string win = hud.ChangeJobWindowState();
                    GD.Print($"live: combat job change window: click {clicked}, {win ?? "not open"}");
                    Shot("jobchange-window.png");
                    Check("the entry opens the job change window with the sub job's row", win != null && !win.Contains("name \"\"", StringComparison.Ordinal));
                    Check("the window plays the row's preview movie", win != null && !win.EndsWith("video none", StringComparison.Ordinal));
                    bool pressed = hud.ChangeJobPressScripted();
                    await until(() => hud.ChangeJobWindowState() == null, 60 * 3);
                    Check("the change button closes the window", pressed && hud.ChangeJobWindowState() == null);
                    break;
                }
                case "openchar":
                {
                    GD.Print($"live: combat char window: {(hud.OpenCharScripted() ? "open" : "refused")}");
                    await frames(60 * 4);
                    GD.Print($"live: combat guide state: {hud.GuideStateText()}");
                    break;
                }
                case "esc":
                {
                    GD.Print($"live: combat esc: {(hud.EscScripted() ? "taken" : "not taken")}");
                    await frames(60 * 4);
                    GD.Print($"live: combat guide state: {hud.GuideStateText()}");
                    break;
                }
                case "openbag":
                {
                    GD.Print($"live: combat bag: {(hud.OpenInventoryScripted() ? "open" : "refused")}");
                    await frames(60 * 4);
                    GD.Print($"live: combat guide state: {hud.GuideStateText()}");
                    break;
                }
                case "equipclick:right": case "equipclick:double":     // MS2_LIVE_COMBAT_STEP=equipclick:right|double — the real slot click on a fresh GM item
                {
                    int weapon = int.Parse(Env("MS2_LIVE_COMBAT_WEAPON", "0"));
                    string arg = step[(step.IndexOf(':') + 1)..];
                    int button = arg == "double" ? 0 : 1;
                    if (!live.Items.All.Any(e => e.Id == weapon))
                    {
                        GD.Print($"live: combat weapon item: {(hud.SubmitChatLine($"/item {weapon}") ? "sent" : "no chat window")}");
                        await until(() => live.Items.All.Any(e => e.Id == weapon), 60 * 10); await frames(60);
                    }
                    Check("the item is in the bag", live.Items.All.Any(e => e.Id == weapon));
                    GD.Print($"live: combat guide state: {hud.GuideStateText()}");
                    GD.Print($"live: combat slot click {arg}: {(hud.SlotClickScripted(weapon, button) ? "raised" : "no slot")}");
                    await until(() => live.Me.Equips?.Any(e => e.Id == weapon) == true, 60 * 6); await frames(60);
                    GD.Print($"live: combat guide state after: {hud.GuideStateText()}");
                    Check("the item is worn after the click", live.Me.Equips?.Any(e => e.Id == weapon) == true);
                    Shot("equipclick.png");
                    break;
                }
                case "unequip":
                {
                    int weapon = int.Parse(Env("MS2_LIVE_COMBAT_WEAPON", "0"));
                    GD.Print($"live: combat unequip: {(hud.UnequipScripted(weapon) ? "sent" : "not worn")}");
                    await until(() => live.Me.Equips?.Any(e => e.Id == weapon) != true, 60 * 10); await frames(60);
                    Check("the weapon is off", live.Me.Equips?.Any(e => e.Id == weapon) != true);
                    break;
                }
                case "revivepaid":
                {
                    Check("the revival dialog is open", hud.RevivalUiUp());
                    long meso0 = live.Me.Meso;
                    GD.Print($"live: combat paid revive dialog: {hud.RevivalState()}, meso {meso0}, meso revives used {live.Me.MesoRevivalsUsed}");
                    GD.Print($"live: combat paid revive: {(hud.ReviveScripted(2) ? "sent" : "no dialog")}");
                    bool alive = false;
                    await until(() => alive = !live.Me.Dead, 60 * 15);
                    Check("the character is alive again after the paid revive", alive);
                    await frames(30);
                    GD.Print($"live: combat paid revived: meso {meso0} -> {live.Me.Meso}, meso revives used {live.Me.MesoRevivalsUsed}, " +
                        $"penalty count {live.Me.RevivalConfirmCount}, end tick {live.Me.RevivalEndTick}");
                    Shot("revived_paid.png");
                    break;
                }
                case "mobloot":
                {
                    // MS2_LIVE_COMBAT_MOB spawned and killed, again up to MS2_LIVE_LOOT_KILLS times until a kill drops an item (not a meso,
                    // a currency or a ball); then shots of the kill's drops on the ground, a run to each with the pickup key held, the bag after
                    // mobloot:<npc id> names the mob; mobmeso:<npc id> is one kill whose mesos are picked up, with no item check
                    bool mesoOnly = step.StartsWith("mobmeso:");
                    int mob = int.Parse(step.Length > 8 ? step[8..] : Env("MS2_LIVE_COMBAT_MOB", "0")), most = mesoOnly ? 1 : int.Parse(Env("MS2_LIVE_LOOT_KILLS", "8"));
                    string tag = mesoOnly ? "mobmeso" : "mobloot";
                    var made = new System.Collections.Generic.List<Ms2.Net.Packets.FieldAddItem>();
                    void Made(Ms2.Net.Packets.FieldAddItem d, bool shown) { if (shown) made.Add(d); }
                    live.DropAdded += Made;
                    static bool IsItem(Ms2.Net.Packets.FieldAddItem d) => !d.IsMeso && !Ms2.Formats.World.DropPickup.IsCurrencyId(d.ItemId) && !Ms2.Formats.World.DropPickup.IsSpecialId(d.ItemId);
                    var mine = new System.Collections.Generic.List<Ms2.Net.Packets.FieldAddItem>();
                    int kill = 0;
                    for (; kill < most && !mine.Any(IsItem); kill++)
                    {
                        int added = npcs;
                        hud.SubmitChatLine("/npc " + mob);
                        await until(() => npcs > added, 60 * 15); await frames(60);
                        var target = lastNpc;
                        if (target == null || npcs == added) { Check("the mob is in the field", false); break; }
                        int deadBefore = kills, press = 0;
                        bool Gone() => kills > deadBefore || !GodotObject.IsInstanceValid(target.Root) || live.NpcDead(target.ObjectId);
                        int presses = int.Parse(Env("MS2_LIVE_COMBAT_PRESSES", "30"));
                        for (; press < presses && !Gone(); press++) { hud.FaceScripted(target.Position); await frames(20); hud.AttackScripted(); await frames(60); }
                        // MS2_LIVE_LOOT_FINISH=1: a mob still up after the presses is finished by the GM kill near the player (the
                        // server's kill goes through the same death and the same drops, the player its damage dealer); =all kills
                        // every mob and NPC of the field instead, for a server whose kill near finds none
                        if (!Gone() && Env("MS2_LIVE_LOOT_FINISH", "0") is "1" or "all") { hud.SubmitChatLine(Env("MS2_LIVE_LOOT_FINISH", "0") == "all" ? "/kill all" : "/kill near --distance 600"); await until(Gone, 60 * 5); }
                        await until(() => made.Any(d => d.SourceObjectId == target.ObjectId), 60 * 6); await frames(60 * 3);
                        mine = made.Where(d => d.SourceObjectId == target.ObjectId).ToList();
                        GD.Print($"live: combat mobloot kill {kill}: npc {target.ObjectId} dead {Gone()} after {press} press(es), drops {string.Join(" ", mine.Select(d => $"{d.ItemId}x{d.Amount}/r{d.Rarity}"))}");
                    }
                    live.DropAdded -= Made;
                    var items = mine.Where(IsItem).ToList();
                    if (!mesoOnly) Check($"a kill dropped an item ({kill} kill(s); {string.Join(" ", items.Select(d => d.ItemId))})", items.Count > 0);
                    else Check($"the kill dropped mesos ({string.Join(" ", mine.Where(d => d.IsMeso).Select(d => d.Amount))})", mine.Any(d => d.IsMeso));
                    foreach (var d in mine)
                        if (live.DropShown(d.ObjectId) is { } s)
                            GD.Print($"live: combat mobloot drop {d.ObjectId} item {d.ItemId} rarity {d.Rarity}: state {s.State}, grade effect {s.Glow}, beEffect {s.BeEffect}, at {FieldPortals.ToMs2(s.At)} (packet {d.Position}), player at {FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition)}");
                    // a run to near the first item drop (the pickup key up), so the shot has the drops in the middle of the view
                    if ((items.Count > 0 ? items : mine).FirstOrDefault() is { } first)
                    {
                        for (int n = 0; n < 60 * 8 && live.DropShown(first.ObjectId) is { } fs && hud.DriveTowardScripted(fs.At, false) > 300; n++) await frames(1);
                        hud.DriveEnd();
                    }
                    await frames(60); Shot(tag + "_ground.png");
                    if (flow.GetViewport().GetCamera3D() is { } cam)
                        foreach (var d in mine)
                            if (live.DropShown(d.ObjectId) is { } s && !cam.IsPositionBehind(s.At))
                                GD.Print($"live: combat mobloot drop {d.ObjectId} item {d.ItemId} on the shot at {cam.UnprojectPosition(s.At)}");
                    long bag0 = BagSum(live), meso0 = mesoDelta;
                    live.PickupKeyHeld = true;
                    foreach (var d in mine.OrderBy(d => d.IsMeso ? 0 : 1))
                    {
                        for (int n = 0; n < 60 * 10 && live.DropShown(d.ObjectId) is { } s; n++)
                        {
                            float dist = hud.DriveTowardScripted(s.At, false);
                            if (dist < 50) hud.DriveEnd();
                            await frames(1);
                        }
                        hud.DriveEnd();
                        GD.Print($"live: combat mobloot pickup {d.ObjectId} item {d.ItemId}: {(live.DropShown(d.ObjectId) == null ? "gone" : "still there")}");
                    }
                    await until(() => BagSum(live) != bag0 || !items.Any(), 60 * 5); await frames(90);
                    live.PickupKeyHeld = false;
                    if (!mesoOnly) Check($"the bag took the item ({bag0} -> {BagSum(live)}; held {string.Join(" ", items.Select(i => $"{i.ItemId}:{live.Items.All.Where(e => e.Id == i.ItemId).Sum(e => (long)e.Amount)}"))})", items.Count > 0 && BagSum(live) > bag0);
                    if (mine.Any(d => d.IsMeso)) Check($"the meso count changed ({mesoDelta - meso0})", mesoDelta > meso0);
                    Shot(tag + "_picked.png");
                    GD.Print($"live: combat bag: {(hud.OpenInventoryScripted() ? "open" : "refused")}");
                    await frames(60);
                    if (items.FirstOrDefault() is { } shown && live.Inventory?.TabOfItem(shown.ItemId) is int tab)
                        GD.Print($"live: combat bag tab {tab} of item {shown.ItemId}: {(hud.InventoryTabScripted(tab) ? "shown" : "not shown")}");
                    await frames(60); Shot(tag + "_bag.png");
                    break;
                }
                case "bagfull":
                {
                    // bagfull:<item id>: the GM item command fills the item's tab (a non-stacking item takes every free cell), the same
                    // item is dropped by the player, and the pickup key is held at the drop. CDropItem::FUN_141190900's bag test refuses
                    // the request: notice 0x43 and the bag-full system sound, the drop left on the ground and the bag unchanged
                    int item = int.Parse(step[8..]);
                    var notices = new System.Collections.Generic.List<int>();
                    var sounds = new System.Collections.Generic.List<(string Key, bool Played)>();
                    var made = new System.Collections.Generic.List<Ms2.Net.Packets.FieldAddItem>();
                    void Notice(int n) => notices.Add(n);
                    void Sound(string k, bool played) => sounds.Add((k, played));
                    void Made(Ms2.Net.Packets.FieldAddItem d, bool shown) { if (shown && d.ItemId == item) made.Add(d); }
                    long bag0 = BagSum(live);
                    GD.Print($"live: combat bagfull give {item}: {(hud.SubmitChatLine($"/item {item} --amount 500") ? "sent" : "no chat window")}");
                    await until(() => BagSum(live) > bag0, 60 * 10); await frames(60 * 3);
                    int tab = live.Inventory?.TabOfItem(item) ?? -1;
                    Check($"the item's tab {tab} is full ({bag0} -> {BagSum(live)})", tab >= 0 && live.Inventory?.HasEmptyCell(tab) == false);
                    live.DropAdded += Made;
                    GD.Print($"live: combat bagfull drop {item}: {(hud.SubmitChatLine($"/item {item} --drop") ? "sent" : "no chat window")}");
                    await until(() => made.Count > 0, 60 * 10); await frames(60 * 2);
                    live.DropAdded -= Made;
                    var drop = made.FirstOrDefault();
                    Check("the dropped item is in the field", drop != null);
                    if (drop == null) break;
                    long bag1 = BagSum(live);
                    await frames(30); Shot("bagfull_ground.png");
                    live.DropNotice += Notice;
                    live.DropSystemSound += Sound;
                    live.PickupKeyHeld = true;
                    for (int n = 0; n < 60 * 8 && live.DropShown(drop.ObjectId) is { } s && !notices.Contains(Ms2.Formats.World.DropPickup.BagFullNotice); n++)
                    {
                        if (hud.DriveTowardScripted(s.At, false) < 50) hud.DriveEnd();
                        await frames(1);
                    }
                    hud.DriveEnd();
                    await frames(20); Shot("bagfull_notice.png");
                    await frames(60 * 2);
                    live.PickupKeyHeld = false;
                    live.DropNotice -= Notice;
                    live.DropSystemSound -= Sound;
                    GD.Print($"live: combat bagfull: notices {string.Join(" ", notices.Distinct().Select(n => $"0x{n:x}"))} ({notices.Count}), sounds {string.Join(" ", sounds.Distinct().Select(x => $"{x.Key}:{(x.Played ? "played" : "not played")}"))} ({sounds.Count})");
                    Check("the bag-full notice came", notices.Contains(Ms2.Formats.World.DropPickup.BagFullNotice));
                    Check("the bag-full sound played", sounds.Any(x => x.Key == Ms2.Formats.World.DropPickup.BagFullSound && x.Played));
                    Check($"the drop stays and the bag is unchanged ({bag1} -> {BagSum(live)})", live.DropShown(drop.ObjectId) != null && BagSum(live) == bag1);
                    break;
                }
                case "autoloot":
                {
                    // autoloot:<npc id>: auto loot off (the options block, as the item-get dialog sets it) while the mob is killed, up
                    // to MS2_LIVE_LOOT_KILLS times until it drops an item; then the character file's options back and a run to the drop
                    // with the pickup key up. MyPC::FUN_141530db0 requests it on its own (mypc.cpp:4724-4734) and the bag takes it.
                    // autofull:<npc id>: before the options come back the drop's tab is filled (the GM item command, the drop's own item
                    // to the last cell and stack), so the request is refused: notice 0x43, the server's refusal mode 1, which clears the
                    // request flag (scenestategame.cpp:2450-2454), and auto loot asks again once the repeat delay is over.
                    bool full = step.StartsWith("autofull:");
                    int mob = int.Parse(step[9..]), most = int.Parse(Env("MS2_LIVE_LOOT_KILLS", "8"));
                    var options = GameOptions.AutoLoot;
                    var saved = options.Clone();
                    options.CopyFrom(new Ms2.Formats.Options.GameOption.AutoLoot { Pet = false });
                    var made = new System.Collections.Generic.List<Ms2.Net.Packets.FieldAddItem>();
                    void Made(Ms2.Net.Packets.FieldAddItem d, bool shown) { if (shown) made.Add(d); }
                    static bool IsItem(Ms2.Net.Packets.FieldAddItem d) => !d.IsMeso && !Ms2.Formats.World.DropPickup.IsCurrencyId(d.ItemId) && !Ms2.Formats.World.DropPickup.IsSpecialId(d.ItemId);
                    live.DropAdded += Made;
                    Ms2.Net.Packets.FieldAddItem drop = null;
                    for (int kill = 0; kill < most && drop == null; kill++)
                    {
                        int added = npcs;
                        hud.SubmitChatLine("/npc " + mob);
                        await until(() => npcs > added, 60 * 15); await frames(60);
                        var target = lastNpc;
                        if (target == null || npcs == added) { Check("the mob is in the field", false); break; }
                        int deadBefore = kills, press = 0;
                        bool Gone() => kills > deadBefore || !GodotObject.IsInstanceValid(target.Root) || live.NpcDead(target.ObjectId);
                        int presses = int.Parse(Env("MS2_LIVE_COMBAT_PRESSES", "30"));
                        for (; press < presses && !Gone(); press++) { hud.FaceScripted(target.Position); await frames(20); hud.AttackScripted(); await frames(60); }
                        // MS2_LIVE_LOOT_FINISH=1 or all: a mob still up is finished by the GM kill, as in mobloot
                        if (!Gone() && Env("MS2_LIVE_LOOT_FINISH", "0") is "1" or "all") { hud.SubmitChatLine(Env("MS2_LIVE_LOOT_FINISH", "0") == "all" ? "/kill all" : "/kill near --distance 600"); await until(Gone, 60 * 5); }
                        await until(() => made.Any(d => d.SourceObjectId == target.ObjectId), 60 * 6); await frames(60 * 3);
                        drop = made.FirstOrDefault(d => d.SourceObjectId == target.ObjectId && IsItem(d) && live.DropShown(d.ObjectId) != null);
                        GD.Print($"live: combat autoloot kill {kill}: npc {target.ObjectId} dead {Gone()} after {press} press(es), drops {string.Join(" ", made.Where(d => d.SourceObjectId == target.ObjectId).Select(d => $"{d.ItemId}x{d.Amount}/r{d.Rarity}/kind 0x{d.DropKind:x}"))}");
                    }
                    live.DropAdded -= Made;
                    Check("a kill dropped an item that stays with auto loot off", drop != null);
                    if (drop == null) { options.CopyFrom(saved); break; }
                    var on = saved.Clone();
                    var kinds = Kinds();
                    int type = kinds.TypeOf(drop.ItemId);
                    if (drop.DropKind == 0x3ed) on.Other = true;   // such a drop needs the "other" switch, off in the default file
                    GD.Print($"live: combat autoloot drop {drop.ObjectId} item {drop.ItemId} type {type} rarity {drop.Rarity} kind 0x{drop.DropKind:x}: the options take it {on.Takes(drop.ItemId, type, drop.Rarity, drop.DropKind == 0x3ed, live.Me.User?.Level)}");
                    if (full)
                    {
                        // the room the item has in its tab: every empty cell at SlotMax and what its own stacks still take
                        int tab = live.Inventory.TabOfItem(drop.ItemId);
                        long slotMax = Math.Max(1u, kinds.SlotMax(drop.ItemId));
                        var cells = live.Inventory.Cells(tab);
                        long room = cells.Count(c => c.Empty) * slotMax + cells.Where(c => !c.Empty && c.ItemId == drop.ItemId).Sum(c => Math.Max(0, slotMax - c.Amount));
                        long had = BagSum(live);
                        GD.Print($"live: combat autofull fill tab {tab} with {room} of {drop.ItemId} (slot max {slotMax}): {(hud.SubmitChatLine($"/item {drop.ItemId} --amount {room}") ? "sent" : "no chat window")}");
                        await until(() => live.Inventory.HasEmptyCell(tab) == false, 60 * 10); await frames(60 * 2);
                        Check($"the drop's tab {tab} is full ({had} -> {BagSum(live)})", live.Inventory.HasEmptyCell(tab) == false);
                    }
                    var asks = new System.Collections.Generic.List<(uint At, string What)>();
                    void Ask(int id) { if (id == drop.ObjectId) asks.Add(((uint)Time.GetTicksMsec(), "request")); }
                    void Refused(int id, int mode) { if (id == drop.ObjectId) asks.Add(((uint)Time.GetTicksMsec(), $"refused mode {mode}")); }
                    void Notice(int n) { if (n == Ms2.Formats.World.DropPickup.BagFullNotice) asks.Add(((uint)Time.GetTicksMsec(), "notice 0x43")); }
                    var sounds = new System.Collections.Generic.List<(string Key, bool Played)>();
                    void Sound(string k, bool played) => sounds.Add((k, played));
                    live.DropAutoRequest += Ask; live.DropRefused += Refused; live.DropNotice += Notice; live.DropSystemSound += Sound;
                    long bag0 = BagSum(live);
                    live.PickupKeyHeld = false;
                    options.CopyFrom(on);
                    for (int n = 0; n < 60 * 8 && live.DropShown(drop.ObjectId) is { } s && asks.Count == 0; n++)
                    {
                        if (hud.DriveTowardScripted(s.At, false) < 40) hud.DriveEnd();
                        await frames(1);
                    }
                    hud.DriveEnd();
                    await frames(20); Shot(full ? "autofull_asked.png" : "autoloot_asked.png");
                    // MS2_LIVE_LOOT_NO_REFUSAL=1: a server that answers a full-bag request with nothing (no mode 1)
                    bool refuses = Env("MS2_LIVE_LOOT_NO_REFUSAL", "0") != "1";
                    if (full && refuses) await until(() => asks.Count(a => a.What == "request") >= 3, 60 * 8);
                    else if (full) await frames(60 * 8);
                    else await until(() => live.DropShown(drop.ObjectId) == null && BagSum(live) > bag0, 60 * 5);
                    await frames(60);
                    live.DropAutoRequest -= Ask; live.DropRefused -= Refused; live.DropNotice -= Notice; live.DropSystemSound -= Sound;
                    options.CopyFrom(saved);
                    uint t0 = asks.Count > 0 ? asks[0].At : 0;
                    GD.Print($"live: combat {(full ? "autofull" : "autoloot")}: {string.Join(", ", asks.Select(a => $"{a.What} +{a.At - t0} ms"))}; key held {live.PickupKeyHeld}; "
                        + $"sounds {string.Join(" ", sounds.Select(x => $"{x.Key}:{(x.Played ? "played" : "not played")}"))}");
                    Shot(full ? "autofull_after.png" : "autoloot_after.png");
                    if (!full)
                    {
                        Check("auto loot asked for the drop with the pickup key up", asks.Any(a => a.What == "request"));
                        Check($"the bag took the item ({bag0} -> {BagSum(live)})", BagSum(live) > bag0 && live.DropShown(drop.ObjectId) == null);
                        break;
                    }
                    var seq = asks.Where(a => a.What != "notice 0x43").Select(a => a.What).ToList();
                    if (!refuses)
                        // no refusal clears the request flag, so auto loot does not ask again (OnFieldPickupItem mode 1 is what clears it)
                        Check($"one auto request, and none after it with no refusal ({string.Join(" > ", seq)})", seq.Count == 1 && seq[0] == "request");
                    else
                    {
                        bool alternates = seq.Count >= 3 && seq.Select((w, i) => w == (i % 2 == 0 ? "request" : "refused mode 1")).All(x => x);
                        Check($"each auto request waits for the mode 1 refusal before the next ({string.Join(" > ", seq)})", alternates);
                        var reqs = asks.Where(a => a.What == "request").Select(a => a.At).ToList();
                        Check($"the next request comes after the repeat delay ({string.Join(" ", reqs.Zip(reqs.Skip(1), (x, y) => y - x))} ms)", reqs.Count >= 2 && reqs.Zip(reqs.Skip(1), (x, y) => y - x).All(d => d >= Ms2.Formats.World.DropPickup.RepeatDelayMs));
                    }
                    Check("the bag-full notice came with the requests", asks.Any(a => a.What == "notice 0x43"));
                    Check("the bag-full sound played", sounds.Any(x => x.Key == Ms2.Formats.World.DropPickup.BagFullSound && x.Played));
                    Check($"the drop stays and the bag is unchanged ({bag0} -> {BagSum(live)})", live.DropShown(drop.ObjectId) != null && BagSum(live) == bag0);
                    break;
                }
                case "watchdrops":
                {
                    // MS2_LIVE_WATCH_SECONDS of the field's drops (another player's kill): each add made or not, the pickup key held
                    // where the player stands; checks: drops came, and none owned by another player was made or picked here
                    int secs = int.Parse(Env("MS2_LIVE_WATCH_SECONDS", "90"));
                    var seen = new System.Collections.Generic.List<(Ms2.Net.Packets.FieldAddItem D, bool Made)>();
                    void Seen(Ms2.Net.Packets.FieldAddItem d, bool shown) => seen.Add((d, shown));
                    live.DropAdded += Seen;
                    long bag0 = BagSum(live), meso0 = mesoDelta;
                    bool shot = false;
                    live.PickupKeyHeld = true;
                    ulong end = Time.GetTicksMsec() + (ulong)secs * 1000;      // seconds, not frames: two clients on one machine run slow
                    while (Time.GetTicksMsec() < end)
                    {
                        await frames(30);
                        if (!shot && seen.Count > 0) { await frames(120); Shot("watch_drops.png"); shot = true; }
                    }
                    live.PickupKeyHeld = false;
                    live.DropAdded -= Seen;
                    long own = live.CharacterId;
                    var others = seen.Where(s => s.D.OwnerType <= 1 && s.D.OwnerUid != own).ToList();
                    GD.Print($"live: combat watchdrops: {seen.Count} add(s): {string.Join(" ", seen.Select(s => $"{s.D.ItemId}x{s.D.Amount}/owner{s.D.OwnerType}:{(s.D.OwnerUid == own ? "me" : s.D.OwnerUid.ToString())}/{(s.Made ? "made" : "not made")}"))}");
                    Check($"another player's drops came ({others.Count})", others.Count > 0);
                    Check("none of another player's drops was made here", others.All(s => !s.Made));
                    Check($"the bag and meso are unchanged ({bag0} -> {BagSum(live)}, meso {mesoDelta - meso0})", BagSum(live) == bag0 && mesoDelta == meso0);
                    Shot("watch_end.png");
                    break;
                }
                case "loot":
                {
                    int item = int.Parse(Env("MS2_LIVE_COMBAT_ITEM", "0")), meso = int.Parse(Env("MS2_LIVE_COMBAT_MESO", "500"));
                    int mesoItem = int.Parse(Env("MS2_LIVE_COMBAT_MESO_ITEM", "90000001"));
                    long bag0 = BagSum(live);
                    GD.Print($"live: combat drop item: {(hud.SubmitChatLine($"/item {item} --drop") ? "sent" : "no chat window")}");
                    await frames(120); Shot("loot_item_dropped.png");
                    live.PickupKeyHeld = true;      // the pickup key held (the key handler's flag)
                    await until(() => BagSum(live) != bag0, 60 * 10); await frames(60);
                    live.PickupKeyHeld = false;
                    Check($"the bag changed ({bag0} -> {BagSum(live)})", BagSum(live) != bag0);
                    Shot("loot_item_picked.png");
                    GD.Print($"live: combat drop meso: {(hud.SubmitChatLine($"/item {mesoItem} --amount {meso} --drop") ? "sent" : "no chat window")}");
                    await frames(120); Shot("loot_meso_dropped.png");
                    live.PickupKeyHeld = true;
                    await until(() => mesoDelta > 0, 60 * 10); await frames(60);
                    live.PickupKeyHeld = false;
                    Check($"the meso count changed ({mesoDelta})", mesoDelta > 0);
                    Shot("loot_meso_picked.png");
                    break;
                }
                default: GD.Print($"live: combat: unknown step {step}"); break;
            }
        }
    }
}
