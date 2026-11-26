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
        // the own mount's loopEffect (Ridee::vfunc_12 / ~Ridee): riding, a row with one keeps it (handle in the field's
        // effects) and a row without keeps none; after the ride off the handle is gone. Returns the riding handle.
        int RideLoopScripted(LiveSession ls, string what, bool riding, int had)
        {
            if (riding)
            {
                var m = ls.OwnPlayer?.Mount;
                string loop = m?.Data.LoopEffect;
                int h = m?.LoopFx.Handle ?? 0;
                Check($"{what}: loopEffect '{loop ?? "-"}' kept as {h}", loop == null ? h == 0 : h != 0 && ls.IsKeptEffect(h));
                return h;
            }
            if (had != 0) Check($"{what}: loopEffect {had} stopped at the ride off", !ls.IsKeptEffect(had));
            return 0;
        }
        void Shows(string what)     // the frame is not one colour: nothing full screen is left over the field
        {
            var img = flow.GetViewport().GetTexture().GetImage();
            float lo = 1, hi = 0;
            for (int y = 0; y < img.GetHeight(); y += 24)
                for (int x = 0; x < img.GetWidth(); x += 24) { float l = img.GetPixel(x, y).Luminance; lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l); }
            Check($"{what}: the frame is not one colour (luminance {lo:0.00} to {hi:0.00})", hi - lo > 0.05f);
        }
        string Env(string k, string d) => System.Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : d;
        // MS2_LIVE_BUFF_SHOTS=N: the first N buff keep effects (RemoteLayer.BuffEffectPlayed, on any owner) shot that many frames
        // after they are kept (MS2_LIVE_BUFF_SHOT_FRAMES, "2" by default, "a/b/..." for more), during whatever step waits frames then
        if (int.TryParse(Env("MS2_LIVE_BUFF_SHOTS", "0"), out int buffShots) && buffShots > 0)
        {
            var at = Env("MS2_LIVE_BUFF_SHOT_FRAMES", "2").Split('/').Select(int.Parse).ToArray();
            int since = -1, taken = 0;
            string name = null;
            RemoteLayer.BuffEffectPlayed += (fx, owner, h) =>
            {
                if (h <= 0 || since >= 0 || taken >= buffShots) return;
                since = 0; name = $"buff{++taken}_{owner}_{System.IO.Path.GetFileNameWithoutExtension(fx)}";
            };
            var inner = frames;
            frames = async count =>
            {
                for (int i = 0; i < count; i++)
                {
                    await inner(1);
                    if (since < 0) continue;
                    since++;
                    if (at.Contains(since)) Shot($"{name}_f{since}.png");
                    if (since >= at.Max()) since = -1;
                }
            };
        }
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
                bool shield = itemId / 100000 == 141;      // a shield goes to the left hand as the bag puts it
                send(e.Uid, (byte)(another || shield ? Ms2.Net.Packets.EquipSlot.LH : Ms2.Net.Packets.EquipSlot.RH), 0);
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
        FieldNpcs.Placed clickNpc = null;         // mclick:hold and mclick:npc's npc
        FieldNpcs.Placed talkAgainNpc = null;     // talkagain's npc, mclick:npc's
        var serverNpcs = new System.Collections.Generic.List<FieldNpcs.Placed>();
        live.NpcAdded += npc => { npcs++; lastNpc = npc; serverNpcs.Add(npc); };
        var goneNpcs = new System.Collections.Generic.HashSet<int>();     // object ids removed by the server or counted dead by the kill step
        live.NpcRemoved += id => goneNpcs.Add(id);
        int questProgress = 0;
        live.Quests.ProgressUpdated += id => questProgress++;
        // what holds the own player's move (Player.Step's gates), for a log line
        static string MoveHolds(Player p) => p == null ? "no player" :
            $"state {p.State}, script driven {p.ScriptDriven}, server state hold {p.ServerStateHold?.Invoke() == true}, dead hold {p.DeathHoldsMove}, " +
            $"guide hold {Movement.MoveInput.GuideHoldsMove()}, input block {Ms2.Ui.InputBlock.Active}, move cover {Ms2.Ui.FieldMoveCover.Up}, taxi hook set {p.TaxiHold != null}";
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
                        if (n % 120 == 119) GD.Print($"live: combat walkto {point}: distance {dist:F0}, at {FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition)}, {MoveHolds(live.player)}");
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
            if (step.StartsWith("playfx:"))      // playfx:<effect xml>:<frames>[:<frame>/<frame>...[:dx dy]]: the effect played on the own pc as a hit's effectRemain is (RemoteLayer.PlayEffectOnObject), with the own pc's kind (1: no frame-rate drop of its swooshes), shots at the listed frames; with dx dy the pc runs toward its map position plus that offset meanwhile
            {
                var c = step[7..].Split(':');
                int gap = c.Length > 1 ? int.Parse(c[1]) : 120;
                var shotFrames = c.Length > 2 ? c[2].Split('/').Select(int.Parse).ToList() : new System.Collections.Generic.List<int>();
                string tag = System.IO.Path.GetFileNameWithoutExtension(c[0]);
                Vector3? runTo = null;
                if (c.Length > 3)
                {
                    var d = c[3].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var from = FieldPortals.ToMs2(live.OwnPlayer.GlobalPosition);
                    runTo = new Vector3((float)from.X + float.Parse(d[0]), (float)from.Z, -((float)from.Y + float.Parse(d[1])));
                }
                GD.Print($"live: combat {step}: {(live.remote?.PlayEffectOnObject(c[0], live.remote.Tracker.SelfObjectId, Ms2.Audio.SoundMusicEffectState.CasterMyPc) == true ? "played" : "not played")} at {Time.GetTicksMsec()}");
                for (int frame = 0; frame < gap; frame++)
                {
                    if (runTo is { } to) hud.DriveTowardScripted(to, false);
                    if (shotFrames.Contains(frame)) Shot($"playfx_{tag}_f{frame}.png");
                    await frames(1);
                }
                if (runTo != null) hud.DriveEnd();
                continue;
            }
            if (step.StartsWith("timescale:"))      // timescale:<x>: Engine.TimeScale for the steps after it, so a short effect's first frames fall on the shots; timescale:1 restores it
            {
                Engine.TimeScale = double.Parse(step[10..], System.Globalization.CultureInfo.InvariantCulture);
                GD.Print($"live: combat {step}: time scale {Engine.TimeScale}");
                continue;
            }
            if (step.StartsWith("wait:"))      // wait:N: N seconds of frames (a trigger's timed states)
            {
                await frames(60 * int.Parse(step[5..]));
                continue;
            }
            if (step.StartsWith("playsound:"))      // playsound:<event path>:<d>[/<d>...]: the event played d units beside the own pc in turn (along +x), each held 1 s with the rolloff printed
            {
                var p = step.Split(':');
                var audio = FieldAudio.Instance;
                if (audio == null || p.Length < 3 || live.player == null) { GD.Print($"live: combat {step}: not run"); continue; }
                foreach (var ds in p[2].Split('/'))
                {
                    if (!float.TryParse(ds, System.Globalization.CultureInfo.InvariantCulture, out float d)) continue;
                    bool played = audio.PlayEventAt(p[1], live.player.GlobalPosition + new Vector3(d, 0, 0));
                    await frames(20);
                    GD.Print($"live: combat playsound {p[1]} at {d}: {(played ? "played" : "NOT played")}; {audio.DescribeRolloff()}");
                    await frames(40);
                }
                continue;
            }
            if (step == "rolloff")      // the listener, the playing 3D voices and the nearest ambient players: distance, gain now and the old falloff's
            {
                GD.Print($"live: combat rolloff: {FieldAudio.Instance?.DescribeRolloff() ?? "no field audio"}");
                continue;
            }
            if (step.StartsWith("giveup:"))      // giveup:<quest id>: the quest log's give-up as its message box sends it (Quest 6, uiquestdialog.cpp:1813 and :1826), then until the server's answer
            {
                int id = int.Parse(step[7..]);
                live.SendTalk(new Ms2.Net.Packets.QuestAbandonRequest(id));
                await until(() => live.Quests.Get(id) is not { State: 1 }, 60 * 10); await frames(60);
                Check($"quest {id} is given up (state {live.Quests.Get(id)?.State})", live.Quests.Get(id) is not { State: 1 });
                continue;
            }
            if (step.StartsWith("gmquest:"))      // gmquest:<quest id>:<Started|Completed>[:<times>]: the server's GM quest command (a GM account), then until the quest's packet; with times, Started then Completed that many times (each completion's rewards, the fame among them)
            {
                var parts = step[8..].Split(':');
                int id = int.Parse(parts[0]);
                int times = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                async Task<bool> To(string state, int want)
                {
                    // the command starts a quest the character does not hold even when asked to complete it
                    for (int tries = 0; tries < 2 && live.Quests.Get(id)?.State != want; tries++)
                    {
                        int was = live.Quests.Get(id)?.State ?? -1;
                        hud.SubmitChatLine($"/quest {id} --state {state}");
                        await until(() => (live.Quests.Get(id)?.State ?? -1) != was, 60 * 5); await frames(20);
                    }
                    return live.Quests.Get(id)?.State == want;
                }
                if (times == 0) Check($"quest {id} is {parts[1]} (state {live.Quests.Get(id)?.State})", await To(parts[1], parts[1] == "Completed" ? 2 : 1));
                else
                {
                    int done = 0;
                    for (int i = 0; i < times; i++)
                    {
                        hud.SubmitChatLine($"/quest {id} --state Started");
                        await until(() => live.Quests.Get(id)?.State == 1, 60 * 5); await frames(20);
                        if (await To("Completed", 2)) done++;
                    }
                    Check($"quest {id} started and completed {done}/{times} times", done == times);
                }
                continue;
            }
            if (step.StartsWith("loggiveup:"))      // loggiveup:<quest id>: the quest log's give-up button (its movie's onGiveUpQuest) for an untimed quest, then until the server's answer
            {
                int id = int.Parse(step[10..]);
                Check($"the quest log takes the give-up of {id}", hud.QuestLogGiveUpScripted(id));
                await until(() => live.Quests.Get(id) is not { State: 1 }, 60 * 10); await frames(60);
                Check($"quest {id} is given up from the log (state {live.Quests.Get(id)?.State})", live.Quests.Get(id) is not { State: 1 });
                continue;
            }
            if (step == "minimapforce")      // minimapforce: the minimap's forced minimize (BuildUIData's forceMinimize), as the controller and the movie hold it
            {
                GD.Print($"live: combat minimap force: {hud.MinimapForceScripted() ?? "no minimap"}");
                continue;
            }
            if (step.StartsWith("minimapmark:"))      // minimapmark:<portal id>: whether the minimap's mark of the portal shows
            {
                int id = int.Parse(step[12..]);
                GD.Print($"live: combat minimap mark of portal {id}: {hud.MinimapPortalMarkScripted(id)?.ToString() ?? "none"}");
                continue;
            }
            if (step == "minimapportals")      // minimapportals: the minimap's portal marks against the xblock's portals
            {
                GD.Print($"live: combat minimap portals in map {live.Portals?.FieldId}: {hud.MinimapPortalMarksScripted() ?? "no minimap"}");
                continue;
            }
            if (step.StartsWith("minimapnpc:"))      // minimapnpc:<npc id>: the minimap's entity of each field npc of that id (icon, frame, layer, shown)
            {
                int id = int.Parse(step[11..]);
                int found = 0;
                foreach (var n in live.LiveNpcs)
                    if (n.Row?.Id == id) { found++; GD.Print($"live: combat minimap npc {id} ({n.ObjectId}): {hud.MinimapNpcMarkScripted(n.ObjectId) ?? "no entity"}"); }
                if (found == 0) GD.Print($"live: combat minimap npc {id}: not in the field");
                continue;
            }
            if (step == "minimapnpcs")      // minimapnpcs: the minimap's npc entities counted by icon and frame
            {
                var counts = new System.Collections.Generic.SortedDictionary<string, int>();
                foreach (var n in live.LiveNpcs)
                    if (hud.MinimapNpcMarkScripted(n.ObjectId) is { } m)
                    {
                        string k = m[..m.IndexOf(", guide", System.StringComparison.Ordinal)];
                        counts[k] = counts.GetValueOrDefault(k) + 1;
                    }
                GD.Print($"live: combat minimap npcs: {string.Join("; ", counts.Select(kv => $"{kv.Key} x{kv.Value}"))}");
                continue;
            }
            if (step.StartsWith("portalstate:"))      // portalstate:<portal id>: the field's portal of that id as the masks left it (shown, glow)
            {
                int id = int.Parse(step[12..]);
                var p = live.Portals?.All.FirstOrDefault(x => x.Id == id);
                GD.Print($"live: combat portal {id} in map {live.Portals?.FieldId}: {(p == null ? "none" : $"shown {p.Shown} glow {p.Glow} target {p.TargetMap}, drawn {Field.Active?.PortalDrawn(p.Entity)?.ToString() ?? "no effect"}")}");
                continue;
            }
            if (step.StartsWith("shot:"))      // shot:<name>: a shot of the frame as it is
            {
                Shot(step[5..] + ".png");
                continue;
            }
            if (step.StartsWith("openwin:"))      // openwin:inventory|quest|sysmenu|achieve|fame|famemenu: the window opens (the others shut), then settles for a shot
            {
                Check($"the {step[8..]} window opens", hud.OpenWindowScripted(step[8..]));
                await frames(90);
                continue;
            }
            if (step.StartsWith("achievetab:"))      // achievetab:<category id>: the open trophy window's category row clicked; what its page shows
            {
                GD.Print($"live: combat achieve tab {step[11..]}: {hud.AchieveTabScripted(int.Parse(step[11..])) ?? "no window or row"}");
                await frames(30);
                continue;
            }
            if (step.StartsWith("fametab:"))      // fametab:<alliance id>: the open fame point board's tab of that alliance clicked; what its page shows
            {
                GD.Print($"live: combat fame tab {step[8..]}: {hud.FameTabScripted(int.Parse(step[8..])) ?? "no board or tab"}");
                await frames(30);
                continue;
            }
            if (step.StartsWith("achievestate:"))      // achievestate:<trophy id>: the player's trophy as the book has it
            {
                GD.Print($"live: combat achieve {step[13..]}: {hud.AchieveStateScripted(int.Parse(step[13..]))}");
                continue;
            }
            if (step.StartsWith("achievereward:"))      // achievereward:<trophy id>: the trophy window's reward button; waits for the start grade to move
            {
                int id = int.Parse(step[14..]);
                int before = live.Achieves.Find(id)?.StartGrade ?? -1;
                Check($"trophy {id}'s reward is asked ({hud.AchieveStateScripted(id)})", hud.AchieveRewardScripted(id));
                await until(() => (live.Achieves.Find(id)?.StartGrade ?? -1) != before, 60 * 10); await frames(60);
                Check($"trophy {id}'s reward is taken ({hud.AchieveStateScripted(id)})", (live.Achieves.Find(id)?.StartGrade ?? -1) != before);
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
            if (step.StartsWith("reactable:"))      // reactable:<interact id>[:<name prefix>]: the own pc put at the nearest field interact mesh of that id (and name) whose reactable model draws apart (no use); the server's state and the model drawn checked, with a shot
            {
                var rargs = step[10..].Split(':');
                int id = int.Parse(rargs[0]);
                string prefix = rargs.Length > 1 ? rargs[1] : "";
                var io = live.InteractObjects;
                Ms2.Ui.LiveInteractObject o = null;
                await until(() => live.player != null && Field.Active?.Interacts is { } d && (o = io?.All.Where(x => x.RowId == id && x.Name.StartsWith(prefix, StringComparison.Ordinal) && d.ShownReactable(x.Entity) != null)
                    .OrderBy(x => FieldBuilder.ToGodotPoint(x.Position).DistanceSquaredTo(live.player.GlobalPosition)).FirstOrDefault()) != null, 60 * 20);
                Check($"{step}: an object of the row with a reactable model ({o?.Name}, state {o?.State})", o != null);
                if (o == null) continue;
                live.player.GlobalPosition = FieldBuilder.ToGodotPoint(o.Position) + new Vector3(0, 5, 0);
                live.player.Velocity = Vector3.Zero;
                await frames(120);
                var draws = Field.Active?.Interacts;
                LogInteract(step, o);
                bool reactable = draws?.ShownReactable(o.Entity) == true;
                Check($"{step}: state {o.State} draws the {(o.State == 1 ? "reactable" : "own")} model (reactable drawn {reactable}, asset B set {o.ShowsAssetB})",
                      o.State == 2 ? draws?.Drawn(o) == false : reactable == (o.State == 1));
                if (draws?.Collision(o.Entity) is { } col)
                    Check($"{step}: the NIF collision is the {(o.ShowsAssetB ? "reactable" : "own")} model's (own shapes in the world {col.A?.Body.CollisionLayer > 0}, reactable {col.B?.Body.CollisionLayer > 0})",
                          col.OnB == o.ShowsAssetB && (col.A == null || col.A.Body.CollisionLayer > 0 != o.ShowsAssetB) && (col.B == null || col.B.Body.CollisionLayer > 0 == o.ShowsAssetB));
                else GD.Print($"live: combat {step}: no swapped NIF collision (neither model has PhysX shapes)");
                Shot($"reactable_{id}_state{o.State}.png");
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
                // the field's fog as the load set it (ms2_fog's z, the camera bias, moves with the camera)
                string FogNow() => triggerFieldOf(live)?.FogShown ?? "no field";
                string fogBefore = FogNow();
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
                string fogAfter = FogNow();
                Check($"{step}: the skip put the field's fog back ({fogBefore} -> {fogAfter})", fogAfter == fogBefore);
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
                // a page with a screen effect (screenEffectMovie): its name logged, shots a third into the fade and a second after it
                async System.Threading.Tasks.Task FxShots(Ms2.Formats.World.NpcScriptTable.Content fx, string fxName)
                {
                    int ms = Math.Max(fx.ScreenEffectValue, 10);
                    GD.Print($"live: combat {step}: screen effect {fx.ScreenEffectMovie} {fx.ScreenEffectAction} {fx.ScreenEffectValue} on script {tfc.Talk.ScriptId} page {tfc.Talk.Page + 1} (shown {tfc.Talk.ContentAt(0) == fx})");
                    await frames(Math.Max(1, ms * 60 / 1000 / 3));
                    Shot(fxName + "_during.png");
                    await frames(ms * 60 / 1000 + 60);
                    Shot(fxName + "_after.png");
                }
                string FxName(Ms2.Formats.World.NpcScriptTable.Content fx, int pageNo) => $"talk{id}_s{tfc.Talk.ScriptId}_p{pageNo}_{fx.ScreenEffectMovie}_{fx.ScreenEffectAction}{fx.ScreenEffectValue}";
                Shot($"talk{id}_{Math.Abs(step.GetHashCode()) % 1000}_before.png");   // the field before the action key
                tfc.Talk.ActionKey();
                await until(() => tfc.Talk.Open, 60 * 5);
                Check($"{step}: the talk opened", tfc.Talk.Open);
                if (tfc.Talk.Open && tfc.Talk.ContentAt(0) is { ScreenEffectMovie: { Length: > 0 } } first)
                    await FxShots(first, FxName(first, tfc.Talk.Page + 1));
                Shot($"talk{id}_{Math.Abs(step.GetHashCode()) % 1000}_open.png");
                for (int k = 0; k < pages && tfc.Talk.Open; k++)
                {
                    GD.Print($"live: combat {step} page {k + 1}: script {tfc.Talk.ScriptId} page {tfc.Talk.Page + 1}, quest page {tfc.Talk.OnQuestPage}");
                    // a next page with a screen effect: a shot before it, then the fade's
                    var fx = tfc.Talk.ContentAt(1) is { ScreenEffectMovie: { Length: > 0 } } next ? next : null;
                    string fxName = fx == null ? null : FxName(fx, tfc.Talk.Page + 2);
                    if (fx != null) Shot(fxName + "_before.png");
                    tfc.Talk.ActionKey();
                    if (fx != null)
                    {
                        await until(() => tfc.Talk.ScriptId != 0 && tfc.Talk.ContentAt(0) == fx, 60 * 5);
                        await FxShots(fx, fxName);
                    }
                    else await frames(90);
                    if (k == 0 && tfc.Talk.Open) Shot($"talk{id}_{Math.Abs(step.GetHashCode()) % 1000}_page2.png");   // the window drawn, its second page
                }
                if (tfc.Talk.Open) { GD.Print($"live: combat {step}: still open after {pages} pages, ended"); tfc.Talk.End(); }
                await frames(120);
                continue;
            }
            if (step.StartsWith("keymove:"))   // keymove:<left|right|up|down>:<frames>: a physical arrow key held for that many frames, as a player presses it (MoveInput reads the key), then released; logs the move and the holds
            {
                var a = step.Split(':');
                var key = a[1] switch { "left" => Key.Left, "right" => Key.Right, "down" => Key.Down, _ => Key.Up };
                int hold = a.Length > 2 ? int.Parse(a[2]) : 120;
                if (live.player == null || !GodotObject.IsInstanceValid(live.player)) continue;
                var from = live.player.GlobalPosition;
                GD.Print($"live: combat {step}: before, {MoveHolds(live.player)}");
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                for (int f = 0; f < hold; f++) { await frames(1); if (f == hold / 2) GD.Print($"live: combat {step}: held, {MoveHolds(live.player)}"); }
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                await frames(10);
                float moved = new Vector2(live.player.GlobalPosition.X - from.X, live.player.GlobalPosition.Z - from.Z).Length();
                Check($"{step}: the key moved the player", moved > 50);
                GD.Print($"live: combat {step}: moved {moved:F0}, at {FieldPortals.ToMs2(live.player.GlobalPosition)}");
                continue;
            }
            if (step.StartsWith("click:"))     // click:<npc id>: mouse control on, the own pc put 600 in front of the field's npc of that id facing it, a left click at the npc's middle on screen (CMouseController: the npc pick, the walk, the talk when in reach), then the talk ended
            {
                int id = int.Parse(step.Split(':')[1]);
                var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                FieldNpcs.Placed n = null;
                await until(() => (n = live.TalkNpcs.FirstOrDefault(x => x.Row.Id == id && Alive(x))) != null, 60 * 30);
                Check($"{step}: the npc in the field", n != null);
                if (n == null || tfc?.Talk == null || live.player == null || !GodotObject.IsInstanceValid(live.player)) continue;
                bool feature = GameOptions.FeatureEnabled(0xe2);
                GD.Print($"live: combat {step}: feature 0xe2 MouseInteract {feature}, 0xaf {GameOptions.FeatureEnabled(0xaf)}");
                (live.player.GetParent() as Field)?.SetMouseControl(true);
                double yaw = n.Root.Rotation.Y;
                var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
                live.player.GlobalPosition = n.Position + front * 600 + new Vector3(0, 20, 0);
                live.player.Velocity = Vector3.Zero;
                live.player.Face(-front);
                await frames(90);
                var cam = flow.GetViewport().GetCamera3D();
                var at = n.Position + new Vector3(0, n.Row.CapsuleHeight * 0.5f, 0);
                bool shown = cam != null && !cam.IsPositionBehind(at);
                var screen = shown ? cam.UnprojectPosition(at) : Vector2.Zero;
                GD.Print($"live: combat {step}: npc {n.ObjectId} at screen {screen} (in view {shown}), player {live.player.GlobalPosition.DistanceTo(n.Position):0} away, key talks to it {tfc.Talk.FindTalkable() == n}");
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
                await frames(2);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
                await until(() => tfc.Talk.Open, 60 * 10);
                Check($"{step}: the talk opened", tfc.Talk.Open);
                GD.Print($"live: combat {step}: player {live.player.GlobalPosition.DistanceTo(n.Position):0} from the npc at the talk");
                await frames(90);
                Shot($"click{id}_open.png");
                if (tfc.Talk.Open) tfc.Talk.End();
                (live.player.GetParent() as Field)?.SetMouseControl(GameOptions.MouseControl.Value);
                await frames(120);
                continue;
            }
            if (step.StartsWith("mclick:"))    // mclick:<portal|taxi|object|banner|talkend|hold|npc>[:<portal id>]: mouse control on, the own pc put 500 from the nearest such target (or the portal of that id) facing it, a left click at the target's middle on screen; checks the hover cursor and hint first (a shot), then that the pick is kept and (but for the banner, whose action is not ported) that it runs. talkend[:<npc id>|any|plain] opens the talk with that npc by the action key, then clicks the screen while the talk shows. hold: the nearest npc with hold nodes (hold:<npc id> spawns one); its hint is s_mouse_interact_hold and the run holds on to it. npc: talkagain's npc (or the nearest plain one); its hint is s_mouse_interact_talk and the run opens its talk
            {
                var a = step.Split(':');
                string kind = a[1];
                var fld = live.player?.GetParent() as Field;
                if (fld == null || !GodotObject.IsInstanceValid(live.player)) { Check($"{step}: the own pc", false); continue; }
                fld.SetMouseControl(true);
                if (!Ms2.Ui.Hud.MessageBoxQueue.IsIdle)       // a box an earlier step left (a refused field entry) would cover the hover
                {
                    GD.Print($"live: combat {step}: a message box left open is closed (\"{Ms2.Ui.Hud.MessageBoxQueue.Current()?.Msg}\")");
                    Ms2.Ui.Hud.MessageBoxQueue.Clear(0);
                    await frames(10);
                }
                Movement.MouseMove.LastKept = Movement.MouseMove.LastRan = Ms2.Ui.MousePick.None;
                Movement.MouseMove.LastHoverKind = -1;
                var me = FieldPortals.ToMs2(live.player.GlobalPosition);
                static double D2(Ms2.Formats.Model.Vec3 x, Ms2.Formats.Model.Vec3 y) { var d = x - y; return d.X * d.X + d.Y * d.Y + d.Z * d.Z; }
                Ms2.Formats.Model.Vec3? target = null;
                double height = 0;
                var want = Ms2.Ui.MousePick.None;
                switch (kind)
                {
                    case "portal":
                        var portals = live.Portals?.All.Where(x => x.HasEntity && x.Glow && x.Kind != 1 && x.Dimension.Z > 0).ToList();
                        var portal = a.Length > 2 ? portals?.FirstOrDefault(x => x.Id == int.Parse(a[2])) : portals?.OrderBy(x => D2(x.Position, me)).FirstOrDefault();
                        if (portal != null) { target = portal.Position; height = portal.Dimension.Z; GD.Print($"live: combat {step}: portal {portal.Id} target map {portal.TargetMap}"); }
                        want = Ms2.Ui.MousePick.Portal;
                        break;
                    case "taxi":
                        if (live.TaxiAction.Stations.Count > 0) { target = live.TaxiAction.Stations.OrderBy(x => D2(x, me)).First(); height = Ms2.Ui.MouseInteract.TaxiPickSize; }
                        want = Ms2.Ui.MousePick.Taxi;
                        break;
                    case "banner":
                        if (live.UgcBanners.Count > 0) { target = live.UgcBanners.OrderBy(x => D2(x, me)).First(); height = Ms2.Ui.MouseInteract.UgcBannerPickSize; }
                        want = Ms2.Ui.MousePick.UgcBanner;
                        break;
                    case "object":
                        // one standing about level with the pc: the pick runs only once the action key reaches it (the row's
                        // interactionDistance, 3D), so one up on a pedestal is walked to but never runs, in the client too
                        var obj = live.InteractObjects?.All.Where(x => x.Row != null && x.Row.ShapeDimension.Z > 0 && live.InteractObjects.CanUse(x)
                                && Math.Abs(x.Position.Z - me.Z) < x.Row.InteractionDistance
                                // and alone: with another object nearer the pc than it the action key takes that one, and the
                                // pick (whose vfunc_3 wants its own object current) never runs, in the client too
                                && !live.InteractObjects.All.Any(y => y != x && y.Row != null && D2(y.Position, x.Position) < 4.0 * x.Row.InteractionDistance * x.Row.InteractionDistance))
                            .OrderBy(x => D2(x.Position, me)).FirstOrDefault();
                        if (obj != null) { target = obj.Position; height = obj.Row.ShapeDimension.Z; GD.Print($"live: combat {step}: object {obj.Name} row {obj.RowId} kind {obj.Kind}"); }
                        want = Ms2.Ui.MousePick.Object;
                        break;
                    case "talkend":
                        want = Ms2.Ui.MousePick.TalkCancel;
                        break;
                    case "npc":
                        // talkagain's npc (one that talks), else the nearest plain shown npc; the pick takes it only once the
                        // talk interval has passed (mouseinteract.cpp:316)
                        var tnpc = talkAgainNpc != null && GodotObject.IsInstanceValid(talkAgainNpc.Root) ? talkAgainNpc
                            : live.TalkNpcs.Where(x => x.Row.Kind == 0 && x.Root.Visible && Alive(x)).OrderBy(x => D2(FieldPortals.ToMs2(x.Position), me)).FirstOrDefault();
                        if ((flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents)?.Talk is { } ntalk) await until(() => ntalk.TalkIntervalElapsed, 60 * 3);
                        if (tnpc != null) { clickNpc = tnpc; target = FieldPortals.ToMs2(tnpc.Position); height = tnpc.Row.CapsuleHeight; GD.Print($"live: combat {step}: npc {tnpc.Row.Id} object {tnpc.ObjectId}"); }
                        want = Ms2.Ui.MousePick.Npc;
                        break;
                    case "hold":
                        clickNpc = live.TalkNpcs.Where(x => x.Root.Visible && Alive(x) && live.NpcHoldable(x.ObjectId))
                            .OrderBy(x => D2(FieldPortals.ToMs2(x.Position), me)).FirstOrDefault();
                        if (clickNpc != null) { target = FieldPortals.ToMs2(clickNpc.Position); height = clickNpc.Row.CapsuleHeight; GD.Print($"live: combat {step}: npc {clickNpc.Row.Id} object {clickNpc.ObjectId}"); }
                        want = Ms2.Ui.MousePick.Npc;
                        break;
                }
                var cam = flow.GetViewport().GetCamera3D();
                Vector2 screen;
                if (want == Ms2.Ui.MousePick.TalkCancel)
                {
                    var tfc2 = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                    if (a.Length > 2 && tfc2?.Talk != null && !tfc2.Talk.Open)
                    {
                        // talkend:<npc id>: the talk opened first with the action key, the own pc 100 in front of the npc facing it
                        // any: the field's first npc with a function kind; plain: the first with none (no shop or other window
                        // opens over the click)
                        int nid = a[2] is "any" or "plain" ? 0 : int.Parse(a[2]);
                        bool plain = a[2] == "plain";
                        // the candidates in turn until one opens a talk (not every npc without a function has one)
                        await until(() => live.TalkNpcs.Any(x => (nid == 0 ? (plain ? x.Row.Kind == 0 : x.Row.Kind != 0) && x.Root.Visible : x.Row.Id == nid) && Alive(x)), 60 * 30);
                        var cands = live.TalkNpcs.Where(x => (nid == 0 ? (plain ? x.Row.Kind == 0 : x.Row.Kind != 0) && x.Root.Visible : x.Row.Id == nid) && Alive(x)).Take(5).ToList();
                        foreach (var tn in cands)
                        {
                            GD.Print($"live: combat {step}: talk npc {tn.Row.Id}");
                            if (!GodotObject.IsInstanceValid(tn.Root)) continue;
                            double yaw = tn.Root.Rotation.Y;
                            var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
                            live.player.GlobalPosition = tn.Position + front * 100 + new Vector3(0, 20, 0);
                            live.player.Velocity = Vector3.Zero;
                            live.player.Face(-front);
                            await frames(30);
                            tfc2.Talk.ActionKey();
                            await until(() => tfc2.Talk.CinematicLoaded, 60 * 5);
                            if (tfc2.Talk.CinematicLoaded) break;
                        }
                        await frames(60);
                    }
                    Check($"{step}: the talk is open", tfc2?.Talk?.CinematicLoaded == true);
                    screen = flow.GetViewport().GetVisibleRect().Size * new Vector2(0.5f, 0.35f);
                }
                else
                {
                    Check($"{step}: a target in the field", target != null);
                    if (target == null || cam == null) { fld.SetMouseControl(GameOptions.MouseControl.Value); continue; }
                    var tg = FieldBuilder.ToGodotPoint(target.Value);
                    // a held npc's pc comes from the camera's side, which sees the npc, so the walk to it is in view
                    var away = (kind == "hold" ? cam.GlobalPosition : live.player.GlobalPosition) - tg; away.Y = 0;
                    away = away.LengthSquared() < 1 ? new Vector3(1, 0, 0) : away.Normalized();
                    // a portal's or a held npc's pc goes nearer: from 500 off the straight line to it can be behind scenery or in
                    // water in a town (the cart spawns at the pc, next to the canal in Tria)
                    live.player.GlobalPosition = tg + away * (want == Ms2.Ui.MousePick.Portal || kind == "hold" ? 250 : 500) + new Vector3(0, 20, 0);
                    live.player.Velocity = Vector3.Zero;
                    live.player.Face(-away);
                    await frames(90);
                    var mid = tg + new Vector3(0, (float)height * 0.5f, 0);
                    screen = cam.IsPositionBehind(mid) ? Vector2.Zero : cam.UnprojectPosition(mid);
                }
                // the hover first (UpdateHover every 100 ms): the mouse on the target sets the interact cursor and the pick's
                // hint; over the UI (the talk window) the hand stays (GFxManager::FUN_1422aeed0(2))
                flow.GetViewport().WarpMouse(screen);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = screen, GlobalPosition = screen });
                await frames(30);
                bool overUi = Ui.As3View.AnyMouseOverUi(flow.GetViewport());
                int wantKind = overUi ? 0 : Ms2.Ui.MouseInteract.CursorKind(want);
                int wantType = overUi ? Ms2.Ui.MouseCursorController.Hand : Ms2.Ui.MouseCursorController.Interact;
                await until(() => Movement.MouseMove.LastHoverKind == wantKind, 60);
                int cursorType = Ui.MouseCursorLayer.Cursor?.GetCursorType() ?? -1;
                Check($"{step}: the hover cursor kind {Movement.MouseMove.LastHoverKind} (want {wantKind}{(overUi ? ", over the UI" : "")}), cursor type {cursorType}",
                    Movement.MouseMove.LastHoverKind == wantKind && cursorType == wantType);
                Shot($"mhover_{kind}.png");
                if (kind is "hold" or "npc" && clickNpc != null)
                {
                    // CMouseInteractNpc::vfunc_1 (mouseinteract.cpp:252-259): s_mouse_interact_hold with the npc's name, or
                    // s_mouse_interact_talk with its action name and name
                    var ht = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
                    string wantHint = Ms2.Ui.MouseInteract.NpcHint(ht, clickNpc.Row.Id, kind == "hold");
                    GD.Print($"live: combat {step}: hint \"{Movement.MouseMove.LastHint}\" (want \"{wantHint}\")");
                    Check($"{step}: the hover hint is the {kind} hint", wantHint.Length > 0 && Movement.MouseMove.LastHint == wantHint);
                }
                GD.Print($"live: combat {step}: click at screen {screen}");
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = screen, GlobalPosition = screen });
                await frames(2);
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = screen, GlobalPosition = screen });
                await until(() => Movement.MouseMove.LastKept == want, 30);
                Check($"{step}: the {want} pick kept", Movement.MouseMove.LastKept == want);
                if (want != Ms2.Ui.MousePick.UgcBanner)
                {
                    await until(() => Movement.MouseMove.LastRan == want, 60 * 15);
                    if (Movement.MouseMove.LastRan != want && target != null)
                    {
                        // why not: how far the pc stopped and what the action key holds
                        var stopAt = FieldPortals.ToMs2(live.player.GlobalPosition);
                        var gap = stopAt - target.Value;
                        var afc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                        GD.Print($"live: combat {step}: not run; the pc at {stopAt}, {Math.Sqrt(gap.X * gap.X + gap.Y * gap.Y + gap.Z * gap.Z):F0} from the target, "
                            + $"action {afc?.CurrentAction}, object {live.InteractObjects?.Current?.Name ?? "none"}, state {live.OwnCurStateIdNow}");
                    }
                    Check($"{step}: the {want} pick ran", Movement.MouseMove.LastRan == want);
                    if (kind == "npc" && clickNpc != null
                        && (flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents)?.Talk is { } ctalk)
                    {
                        // the pick ran only with the talk the current action on that npc id (CMouseInteractNpc::vfunc_3)
                        await until(() => ctalk.Open, 60 * 5);
                        Check($"{step}: the run opens the talk with the npc (npc {ctalk.TalkActionNpcId})", ctalk.Open && ctalk.TalkActionNpcId == clickNpc.Row.Id);
                        await frames(30);                    // the talk window draws in after the open
                        Shot($"mclick_{kind}_talk.png");
                        ctalk.End();
                        await until(() => !ctalk.Open, 60 * 5);
                    }
                    if (kind == "hold" && clickNpc != null)
                    {
                        await until(() => live.player.State == Player.MoveState.Hold, 60 * 3);
                        Check($"{step}: the run holds on to the npc (state {live.player.State}, target {live.player.HoldTargetId})",
                            live.player.State == Player.MoveState.Hold && live.player.HoldTargetId == clickNpc.ObjectId);
                    }
                }
                await frames(120);
                Shot($"mclick_{kind}.png");
                fld = live.player?.GetParent() as Field;
                fld?.SetMouseControl(GameOptions.MouseControl.Value);
                continue;
            }
            if (step == "tooltip:talk")      // tooltip:talk: the own pc put in front of a talkable npc with an actionName entry and one without, until the talk (0x14) is the current action; the action tooltip shows TalkAction::vfunc_6's caption (a shot each); then the key opens the talk with the second and the tooltip hides while it is open
            {
                var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                var talk = tfc?.Talk;
                var tip = hud.ActionTooltip;
                if (talk == null || tip == null || !GodotObject.IsInstanceValid(live.player)) { Check($"{step}: the field's talk and the tooltip", false); continue; }
                var tt = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
                await until(() => live.TalkNpcs.Any(x => x.Root.Visible && Alive(x)), 60 * 30);
                var cands = live.TalkNpcs.Where(x => x.Root.Visible && Alive(x)).ToList();
                FieldNpcs.Placed last = null;
                foreach (bool named in new[] { true, false })
                {
                    bool shown = false;
                    foreach (var tn in cands.Where(x => (tt.StringTableText(0xe, x.Row.Id).Length > 0) == named).Take(6))
                    {
                        double yaw = tn.Root.Rotation.Y;
                        var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
                        live.player.GlobalPosition = tn.Position + front * 100 + new Vector3(0, 20, 0);
                        live.player.Velocity = Vector3.Zero;
                        live.player.Face(-front);
                        await frames(30);
                        await until(() => tfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Talk && talk.TalkActionNpcId == tn.Row.Id && tip.ShownAction == 0x14, 30);
                        if (tfc.CurrentAction != Ms2.Ui.ActionKeyTarget.Talk || talk.TalkActionNpcId != tn.Row.Id) continue;
                        string want = Ms2.Ui.ActionCaption.Talk(tt, tn.Row.Id, tn.Row.Name);
                        GD.Print($"live: combat {step}: npc {tn.Row.Id} (action name \"{tt.StringTableText(0xe, tn.Row.Id)}\"): tooltip action {tip.ShownAction}, "
                            + $"key \"{tip.ShownKeyText}\", caption \"{tip.ShownText}\", want \"{want}\"");
                        Check($"{step}: an npc {(named ? "with" : "without")} an action name: the tooltip shows the talk caption (\"{want}\")",
                            tip.ShownAction == 0x14 && want.Length > 0 && tip.ShownText == want);
                        Shot($"tooltip_talk_{(named ? "named" : "plain")}.png");
                        shown = true; last = tn;
                        break;
                    }
                    if (!shown) Check($"{step}: an npc {(named ? "with" : "without")} an action name takes the talk", false);
                }
                if (last == null) continue;
                tfc.ActivateCurrentAction();
                await until(() => talk.Open, 60 * 5);
                await until(() => tip.ShownAction < 0, 30);
                Check($"{step}: the tooltip hides while the talk is open (talk {talk.Open}, tooltip action {tip.ShownAction})", talk.Open && tip.ShownAction < 0);
                talk.End();
                await until(() => !talk.Open, 60 * 5);
                await frames(60);
                continue;
            }
            if (step == "tooltip:object" || step == "tooltip:board" || step == "tooltip:npc")      // tooltip:object|board|npc: the own pc put at the nearest usable interact object (InteractAction 0x12), billboard (0x18) or talkable interact npc with an interact function (0x11, the pc turned to it) in turn until that is the current action; the action tooltip shows its vfunc_6 caption at its vfunc_12 point lifted 75 (a shot); none in the field is logged as not run
            {
                var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                var tip = hud.ActionTooltip;
                if (tfc == null || tip == null || !GodotObject.IsInstanceValid(live.player)) { Check($"{step}: the field and the tooltip", false); continue; }
                var tt = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
                var me = FieldPortals.ToMs2(live.player.GlobalPosition);
                static double Dt2(Ms2.Formats.Model.Vec3 a, Ms2.Formats.Model.Vec3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);
                int code = step == "tooltip:npc" ? 0x11 : step == "tooltip:board" ? 0x18 : 0x12;
                var spots = new List<(string Name, Ms2.Formats.Model.Vec3 At, Func<string> Want)>();
                if (code == 0x11)
                    foreach (var n in (live.InteractNpcs?.All ?? new List<Ms2.Ui.InteractNpcEntry>()).Where(x => !x.Dead && x.Talkable && x.Row != null && !string.IsNullOrEmpty(x.Row.InteractFunction)).OrderBy(x => Dt2(x.Position, me)).Take(6))
                        spots.Add(($"npc {n.ObjectId} ({n.Row?.Id})", n.Position, () => Ms2.Ui.ActionCaption.Interact(tt.Common(0x634) ?? "", n.Row?.Name ?? "")));
                else
                    foreach (var o in (live.InteractObjects?.All ?? new List<Ms2.Ui.LiveInteractObject>()).Where(x => x.Row != null && live.InteractObjects.CanUse(x)
                                 && (code == 0x18 ? x.Kind == 8 : x.Kind != 8 && x.Kind != Ms2.Net.Packets.InteractObjectMessage.KindGathering))
                             .OrderBy(x => Dt2(x.Position, me)).Take(6))
                    {
                        string an = tt.StringTableText(0xb, o.RowId);
                        spots.Add(($"object {o.Name} row {o.RowId} kind {o.Kind}", o.Position,
                            () => code == 0x18 ? Ms2.Ui.ActionCaption.BillBoard(an, o.BoardText) : Ms2.Ui.ActionCaption.Interact(tt.Common(0x634) ?? "", an)));
                    }
                if (spots.Count == 0)
                {
                    var all = live.InteractNpcs?.All ?? new List<Ms2.Ui.InteractNpcEntry>();
                    string npcNote = code != 0x11 ? "" : $" ({all.Count} npcs, {all.Count(x => x.Row != null && !string.IsNullOrEmpty(x.Row.InteractFunction))} with an interact function, "
                        + $"{all.Count(x => x.Row != null && !string.IsNullOrEmpty(x.Row.InteractFunction) && x.Talkable)} of them talkable)";
                    GD.Print($"live: combat {step}: not run; none in the field{npcNote}");
                    continue;
                }
                bool shown = false;
                foreach (var sp in spots)
                {
                    live.player.GlobalPosition = FieldBuilder.ToGodotPoint(sp.At) + new Vector3(30, 5, 0);
                    live.player.Velocity = Vector3.Zero;
                    live.player.Face(new Vector3(-1, 0, 0));      // FindNearestNpc's useFront: the npc ahead of the own pc
                    await frames(20);
                    await until(() => live.ActionKeyCode(tfc.CurrentAction) == code && tip.ShownAction == code, 60);
                    GD.Print($"live: combat {step}: {sp.Name}: action {tfc.CurrentAction} (key {live.ActionKeyCode(tfc.CurrentAction)}), tooltip action {tip.ShownAction}, "
                        + $"caption \"{tip.ShownText}\", target {tip.ShownTarget}, at {tip.Place}");
                    if (tip.ShownAction != code) continue;
                    await frames(20);
                    string want = sp.Want();
                    var t = tip.ShownTarget;
                    Check($"{step}: {sp.Name}: the tooltip shows the caption (\"{want}\") at the target lifted 75",
                        want.Length > 0 && tip.ShownText == want && t is { } tv && Math.Abs(tv.X - sp.At.X) < 1 && Math.Abs(tv.Y - sp.At.Y) < 1 && Math.Abs(tv.Z - sp.At.Z - 75) < 1);
                    Shot($"tooltip_{step[8..]}.png");
                    shown = true;
                    break;
                }
                if (!shown) Check($"{step}: one of {spots.Count} takes the action key with the tooltip", false);
                continue;
            }
            if (step.StartsWith("tooltip:lift:"))      // tooltip:lift:x y z (a liftable's map point): the own pc put beside it until the lift (LiftCubeAction 0x15) is the current action; the tooltip shows GetActionCaption(0x63a, the lift item's name) at the centre (a shot); the action key lifts it (cube mode), then the put-down (PutDownLiftableAction 0xc, cube-mode map) shows common 0x63d (a shot) and the key puts it down
            {
                var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                var tip = hud.ActionTooltip;
                if (tfc == null || tip == null || !GodotObject.IsInstanceValid(live.player)) { Check($"{step}: the field and the tooltip", false); continue; }
                var tt = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
                var lp = step[13..].Split(' ');
                var liftAt = new Ms2.Formats.Model.Vec3(float.Parse(lp[0]), float.Parse(lp[1]), float.Parse(lp[2]));
                live.player.GlobalPosition = FieldBuilder.ToGodotPoint(liftAt) + new Vector3(60, 40, 0);
                live.player.Velocity = Vector3.Zero;
                live.player.Face(new Vector3(-1, 0, 0));
                await frames(30);
                await until(() => live.ActionKeyCode(tfc.CurrentAction) == 0x15 && tip.ShownAction == 0x15, 60 * 3);
                await frames(20);
                var lift = live.Liftables.Find(live.Liftables.Polled);
                string want = lift == null ? "" : Ms2.Ui.ActionCaption.Lift(tt, true, lift.ItemId);
                GD.Print($"live: combat {step}: lift '{live.Liftables.Polled}' item {lift?.ItemId}: action {tfc.CurrentAction}, tooltip action {tip.ShownAction}, "
                    + $"caption \"{tip.ShownText}\", want \"{want}\", target {tip.ShownTarget}, at {tip.Place}");
                Check($"{step}: the tooltip shows the lift caption (\"{want}\") at the centre",
                    tip.ShownAction == 0x15 && want.Length > 0 && tip.ShownText == want && tip.ShownTarget == null);
                Shot("tooltip_lift.png");
                if (tip.ShownAction != 0x15) continue;
                await PressActionKey(frames);
                await until(() => live.CubeCraft.InCubeMode, 60 * 5);
                await frames(30);
                await until(() => live.ActionKeyCode(tfc.CurrentAction) == 0xc && tip.ShownAction == 0xc, 60 * 3);
                await frames(20);
                string wantDown = tt.Common(0x63d) ?? "";
                GD.Print($"live: combat {step}: cube mode {live.CubeCraft.InCubeMode}: action {tfc.CurrentAction}, tooltip action {tip.ShownAction}, "
                    + $"caption \"{tip.ShownText}\", want \"{wantDown}\"");
                Check($"{step}: carrying, the tooltip shows the put-down caption (\"{wantDown}\")",
                    live.CubeCraft.InCubeMode && tip.ShownAction == 0xc && wantDown.Length > 0 && tip.ShownText == wantDown);
                Shot("tooltip_putdown.png");
                if (!live.CubeCraft.InCubeMode) continue;
                await PressActionKey(frames);
                await until(() => !live.CubeCraft.InCubeMode, 60 * 5);
                await frames(30);
                Check($"{step}: the put-down ended the carry and the put-down tooltip is gone", !live.CubeCraft.InCubeMode && tip.ShownAction != 0xc);
                continue;
            }
            if (step == "tooltip:taxi" || step == "tooltip:portal")      // tooltip:taxi|portal: the own pc put at the nearest taxi station (TaxiStationAction 8) or in the nearest shown portal with a box and a target (PortalAction 0x13; not a kind 1 portal, which moves at once) until that is the current action; the action tooltip shows its vfunc_6 caption (the portal's at its point lifted 75), a shot
            {
                var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                var tip = hud.ActionTooltip;
                if (tfc == null || tip == null || !GodotObject.IsInstanceValid(live.player)) { Check($"{step}: the field and the tooltip", false); continue; }
                var tt = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
                var me = FieldPortals.ToMs2(live.player.GlobalPosition);
                bool taxi = step == "tooltip:taxi";
                int code = taxi ? 8 : 0x13;
                string want = "", what = "";
                Ms2.Formats.Model.Vec3? wantTarget = null;
                if (taxi)
                {
                    if (live.TaxiAction.Stations.Count == 0) { GD.Print($"live: combat {step}: not run; no taxi station in the field"); continue; }
                    var st = live.TaxiAction.Stations.OrderBy(x => (x - me).X * (x - me).X + (x - me).Y * (x - me).Y).First();
                    live.player.GlobalPosition = FieldBuilder.ToGodotPoint(st) + new Vector3(40, 5, 0);
                    live.player.Velocity = Vector3.Zero;
                    want = tt.Common(0x64a) ?? "";
                    what = $"station at {st}";
                }
                else
                {
                    var pt = live.Portals?.All.Where(x => x.HasEntity && x.HasBox && x.Glow && x.Shown && x.Kind != 1 && x.TargetMap != 0)
                        .OrderBy(x => (x.Center - me).X * (x.Center - me).X + (x.Center - me).Y * (x.Center - me).Y).FirstOrDefault();
                    if (pt == null || !tfc.StandInPortal(pt.Id)) { GD.Print($"live: combat {step}: not run; no shown portal with a box and a target"); continue; }
                    want = Ms2.Ui.ActionCaption.Portal(tt, true, pt.Type, pt.TargetMap,
                        id => Ms2.Formats.World.FieldData.FindById(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), id));
                    wantTarget = pt.Type == 5 ? pt.Position with { Z = pt.Position.Z + 75 } : pt.Position;
                    wantTarget = wantTarget.Value with { Z = wantTarget.Value.Z + 75 };
                    what = $"portal {pt.Id} type {pt.Type} to {pt.TargetMap}";
                }
                await frames(20);
                await until(() => live.ActionKeyCode(tfc.CurrentAction) == code && tip.ShownAction == code, 60 * 2);
                await frames(20);
                GD.Print($"live: combat {step}: {what}: action {tfc.CurrentAction}, tooltip action {tip.ShownAction}, caption \"{tip.ShownText}\", want \"{want}\", "
                    + $"target {tip.ShownTarget}, at {tip.Place}");
                var tv = tip.ShownTarget;
                Check($"{step}: {what}: the tooltip shows the caption (\"{want}\"){(taxi ? " at the centre" : " at the portal lifted 75")}",
                    tip.ShownAction == code && want.Length > 0 && tip.ShownText == want
                    && (taxi ? tv == null : tv is { } v && wantTarget is { } w && Math.Abs(v.X - w.X) < 1 && Math.Abs(v.Y - w.Y) < 1 && Math.Abs(v.Z - w.Z) < 1));
                Shot($"tooltip_{step[8..]}.png");
                if (!taxi) { live.player.GlobalPosition += new Vector3(0, 0, 600); await frames(30); }
                continue;
            }
            if (step.StartsWith("tooltip:gather:"))      // tooltip:gather:<interact id>: the own pc put at that gathering object (no walk) until the action key's current action is gathering (4); the action tooltip (UIActionTooltip 0x4e5c) shows the gathering caption with the key, a shot; then the key starts the gathering and the tooltip hides while it runs (StateInteraction 0x19) and is back after it
            {
                int id = int.Parse(step[15..]);
                var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                var tip = hud.ActionTooltip;
                var io = live.InteractObjects;
                Ms2.Ui.LiveInteractObject o = null;
                await until(() => live.player != null && (o = io?.All.FirstOrDefault(x => x.RowId == id)) != null, 60 * 20);
                Check($"{step}: the gathering object in the field ({o?.Name}, state {o?.State})", o != null && tfc != null && tip != null);
                if (o == null || tfc == null || tip == null) continue;
                live.player.GlobalPosition = FieldBuilder.ToGodotPoint(o.Position) + new Vector3(40, 5, 0);
                live.player.Velocity = Vector3.Zero;
                await until(() => live.ActionKeyCode(tfc.CurrentAction) == 4 && tip.ShownAction == 4, 60 * 5);
                await frames(30);
                string cap = live.ActionKeyCaption(4);
                GD.Print($"live: combat {step}: action {tfc.CurrentAction} (key {live.ActionKeyCode(tfc.CurrentAction)}), tooltip action {tip.ShownAction}, "
                    + $"key \"{tip.ShownKeyText}\", kind {tip.ShownKind}, caption \"{tip.ShownText}\", at {tip.Place}");
                Check($"{step}: the tooltip shows the gathering caption with the key (\"{cap}\")",
                    tip.ShownAction == 4 && cap.Length > 0 && tip.ShownText == cap && tip.ShownKeyText.Length > 0 && tip.ShownKind == "key");
                Shot($"tooltip_gather_{id}.png");
                await PressActionKey(frames);
                bool running = false, hidden = false;
                await until(() => (running = live.OwnCurStateIdNow == 0x19) && (hidden = tip.ShownAction < 0), 60 * 3);
                GD.Print($"live: combat {step}: after the key: state {live.OwnCurStateIdNow}, casting {io.IsCasting}, tooltip action {tip.ShownAction}");
                if (running) Shot($"tooltip_gather_{id}_busy.png");
                Check($"{step}: the tooltip hides while the gathering runs (state 0x19)", running && hidden);
                await until(() => live.OwnCurStateIdNow != 0x19, 60 * 15);
                await until(() => tip.ShownAction == 4, 60 * 3);
                GD.Print($"live: combat {step}: after the gathering: tooltip action {tip.ShownAction}, caption \"{tip.ShownText}\"");
                Check($"{step}: the tooltip is back after the gathering", tip.ShownAction == 4 || live.ActionKeyCode(tfc.CurrentAction) != 4);
                continue;
            }
            if (step.StartsWith("hold:"))      // hold:<npc id>: spawns the npc, checks it has hold nodes (PCBase::vfunc_117), puts the own pc beside it until the action key's current action is the hold (6) with its caption, then the key holds on (StateHold 0x20 on that npc, the let-go caption) and the key again lets go
            {
                int nid = int.Parse(step[5..]);
                var hfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                if (hfc == null || !GodotObject.IsInstanceValid(live.player)) { Check($"{step}: the field", false); continue; }
                int added = npcs;
                hud.SubmitChatLine("/npc " + nid);
                await until(() => npcs > added && lastNpc?.Row?.Id == nid, 60 * 5);
                var hn = lastNpc;
                bool spawned = hn?.Row?.Id == nid && hn.ObjectId != 0;
                if (spawned) await until(() => live.NpcHoldable(hn.ObjectId), 60 * 10);
                Check($"{step}: the npc spawned with hold nodes", spawned && live.NpcHoldable(hn.ObjectId));
                if (!spawned) continue;
                var ht = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
                // beside it on each side in turn until the poll (every 100 ms) makes the hold current
                bool current = false;
                foreach (var side in new[] { new Vector3(1, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 0, -1) })
                {
                    live.player.GlobalPosition = hn.Position + side * 120 + new Vector3(0, 20, 0);
                    live.player.Velocity = Vector3.Zero;
                    live.player.Face(-side);
                    await frames(30);
                    int found = live.FindHoldTarget(live.HoldRange, out string fnode);
                    GD.Print($"live: combat {step}: side {side}: hold target {found} '{fnode}', action {hfc.CurrentAction}");
                    if (current = hfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Hold) break;
                }
                string capHold = live.HoldCaption(ht);
                GD.Print($"live: combat {step}: action {hfc.CurrentAction}, npc {live.Hold.NpcId}, caption \"{capHold}\"");
                string wantCap = nid == Ms2.Ui.HoldAction.CartNpcId ? ht.Common(Ms2.Ui.HoldAction.CaptionCartKey) ?? "" : ht.Common(Ms2.Ui.HoldAction.CaptionHold);
                Check($"{step}: the hold is the current action on the npc with the hold caption (want \"{wantCap}\")",
                    current && live.Hold.NpcId == nid && capHold.Length > 0 && capHold == wantCap);
                await until(() => hud.ActionTooltip?.ShownAction == 6, 60);
                Check($"{step}: the action tooltip shows the hold caption (\"{hud.ActionTooltip?.ShownText}\")",
                    hud.ActionTooltip is { ShownAction: 6 } ht6 && ht6.ShownText == capHold);
                Shot($"tooltip_hold_{nid}.png");
                if (!current) continue;
                // the move gate (Unk_PCMove::FUN_141683ef0): a held state flag (vfunc_63 0) closes it, and the poll drops the hold
                live.player.AddStateFlags(Ms2.Ui.AutoFindTaxiStation.StateFlag);
                await until(() => hfc.CurrentAction != Ms2.Ui.ActionKeyTarget.Hold, 30);
                bool gateShut = !live.MoveGatePasses() && hfc.CurrentAction != Ms2.Ui.ActionKeyTarget.Hold;
                live.player.RemoveStateFlags(Ms2.Ui.AutoFindTaxiStation.StateFlag);
                await until(() => hfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Hold, 30);
                bool gateOpen = live.MoveGatePasses() && hfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Hold;
                GD.Print($"live: combat {step}: move gate with a state flag: shut {gateShut}; cleared: open {gateOpen}");
                Check($"{step}: a state flag closes the move gate and the hold leaves the action key, and clearing it brings the hold back", gateShut && gateOpen);
                if (!gateOpen) continue;
                hfc.ActivateCurrentAction();
                await until(() => live.player.State == Player.MoveState.Hold, 60 * 3);
                await frames(60);
                Check($"{step}: the key holds on to the npc (state {live.player.State}, target {live.player.HoldTargetId}, {live.player.GlobalPosition.DistanceTo(hn.Position):0} from it)",
                    live.player.State == Player.MoveState.Hold && live.player.HoldTargetId == hn.ObjectId);
                Shot($"hold_{nid}.png");
                await frames(10);
                string capEnd = live.HoldCaption(ht);
                GD.Print($"live: combat {step}: in the hold: action {hfc.CurrentAction}, caption \"{capEnd}\"");
                Check($"{step}: in the hold the action is the hold with the let-go caption",
                    hfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Hold && capEnd.Length > 0 && capEnd != capHold);
                await until(() => hud.ActionTooltip?.ShownText == capEnd, 60);
                Check($"{step}: the action tooltip changes to the let-go caption (\"{hud.ActionTooltip?.ShownText}\")",
                    hud.ActionTooltip is { ShownAction: 6 } hte && hte.ShownText == capEnd);
                hfc.ActivateCurrentAction();
                await until(() => live.player.State != Player.MoveState.Hold, 60 * 3);
                await frames(60);
                Check($"{step}: the key again lets go (state {live.player.State})", live.player.State != Player.MoveState.Hold);
                continue;
            }
            if (step == "talkagain")   // talkagain: a talk with a plain npc (no function) by the action key (the poll's current action is the talk with that npc, then ActivateCurrentAction), ended; at once the key and the mouse pick refuse a new talk (UITalkManager::IsTalkIntervalElapsed, TalkCooldown), and once it has passed the pick finds the npc and the key talks again
            {
                var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                var talk = tfc?.Talk;
                if (talk == null || !GodotObject.IsInstanceValid(live.player)) { Check($"{step}: the field's talk", false); continue; }
                await until(() => live.TalkNpcs.Any(x => x.Row.Kind == 0 && x.Root.Visible && Alive(x)), 60 * 30);
                FieldNpcs.Placed npc = null;
                foreach (var tn in live.TalkNpcs.Where(x => x.Row.Kind == 0 && x.Root.Visible && Alive(x)).Take(5).ToList())
                {
                    double yaw = tn.Root.Rotation.Y;
                    var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
                    live.player.GlobalPosition = tn.Position + front * 100 + new Vector3(0, 20, 0);
                    live.player.Velocity = Vector3.Zero;
                    live.player.Face(-front);
                    await frames(30);
                    await until(() => tfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Talk && talk.TalkActionNpcId == tn.Row.Id, 30);
                    GD.Print($"live: combat {step}: npc {tn.Row.Id}: action {tfc.CurrentAction}, talk npc {talk.TalkActionNpcId}");
                    if (tfc.CurrentAction != Ms2.Ui.ActionKeyTarget.Talk || talk.TalkActionNpcId != tn.Row.Id) continue;
                    tfc.ActivateCurrentAction();
                    await until(() => talk.Open, 60 * 5);
                    if (talk.Open) { npc = tn; break; }
                }
                Check($"{step}: the talk is the current action and the key opens it (npc {npc?.Row.Id})", npc != null);
                talkAgainNpc = npc;
                if (npc == null) continue;
                await frames(30);
                talk.End();
                await until(() => !talk.Open, 60 * 5);
                Check($"{step}: the talk ended", !talk.Open);
                var cam = flow.GetViewport().GetCamera3D();
                var mid = npc.Position + new Vector3(0, npc.Row.CapsuleHeight * 0.5f, 0);
                Godot.Vector3 rayDir = cam == null ? Vector3.Zero : (mid - cam.GlobalPosition).Normalized();
                // at once: the interval has not passed
                ulong endMs = Time.GetTicksMsec();
                bool elapsedNow = talk.TalkIntervalElapsed;
                bool pickNow = cam != null && talk.FindClickTarget(cam.GlobalPosition, rayDir) == npc;
                // a poll (every 100 ms) after the end, timed by the clock: slow frames would let the interval pass
                await until(() => Time.GetTicksMsec() - endMs >= 250, 60 * 2);
                bool keyNow = tfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Talk;
                ulong pressMs = Time.GetTicksMsec() - endMs;
                tfc.ActivateCurrentAction();
                await until(() => talk.Open || Time.GetTicksMsec() - endMs >= 600, 60 * 2);
                bool openNow = talk.Open;
                GD.Print($"live: combat {step}: right after the end: interval elapsed {elapsedNow}, pick finds the npc {pickNow}, key took it {keyNow} ({pressMs} ms after the end), open {openNow}");
                Check($"{step}: right after the end the key and the pick refuse a talk", !elapsedNow && !pickNow && !keyNow && !openNow && pressMs < 1000);
                if (openNow) { talk.End(); await until(() => !talk.Open, 60 * 5); }
                await until(() => talk.TalkIntervalElapsed, 60 * 3);
                await frames(10);
                bool pickLater = cam != null && talk.FindClickTarget(cam.GlobalPosition, rayDir) == npc;
                await until(() => tfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Talk, 30);
                bool keyLater = tfc.CurrentAction == Ms2.Ui.ActionKeyTarget.Talk && talk.TalkActionNpcId == npc.Row.Id;
                tfc.ActivateCurrentAction();
                await until(() => talk.Open, 60 * 5);
                GD.Print($"live: combat {step}: after the interval: pick finds the npc {pickLater}, key took it {keyLater}, open {talk.Open}");
                Check($"{step}: after the interval the pick finds the npc and the key talks again", pickLater && keyLater && talk.Open);
                await frames(30);                            // the talk window draws in after the open
                Shot("talkagain.png");
                talk.End();
                await until(() => !talk.Open, 60 * 5);
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
            if (step == "appfocus:0" || step == "appfocus:1")      // the window loses (0) or gets (1) the focus (WM_ACTIVATEAPP, windowservice.cpp:801-831): the sound categories mute or come back, the hud video's mute stays after the focus is back
            {
                SoundBuses.AppFocus(step == "appfocus:1");
                var mix = Ms2.Formats.Options.SoundMix.Shared;
                GD.Print($"live: app focus {(mix.AppActive ? "back" : "lost")}: hud video muted {mix.MovieMuted}, movie bus {SoundBuses.BusGain(SoundBuses.Movie):F2}");
                continue;
            }
            if (step.StartsWith("movieframe:"))      // movieframe:<n>: a shot of the playing swf cut scene when its timeline reaches frame n (the same frame in two runs)
            {
                int want = int.Parse(step[11..]);
                await until(() => hud.CutSceneSwfFrame() >= want, 60 * 60);
                int got = hud.CutSceneSwfFrame();
                Check($"the cut scene swf reached frame {want} (at {got})", got >= want);
                Shot($"movieframe_{want}.png");
                continue;
            }
            if (step == "moviestate")      // what the cut scene pane holds now (the pane, the usm layer, the exit button), to compare before and after a field change
            {
                GD.Print($"live: movie state: playing {live.Movie.IsPlaying}, path {live.Movie.Path}, id {live.Movie.CutSceneId}, exit button {live.Movie.ExitShown}, usm layer shown {hud.CutSceneVideoShown}, field {live.CurrentEntry?.MapId}");
                Shot($"moviestate_{live.CurrentEntry?.MapId}.png");
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
            if (step.StartsWith("ridewarp:"))      // ridewarp:<ride id>:<map>: rides the row as rideraw does, then /warp while riding: the next field's quick slots come back unlocked, as before the ride
            {
                var a = step.Split(':');
                int rideId = int.Parse(a[1]);
                if (hud.ClearShotScripted()) GD.Print("live: combat level-up guide closed for the shots");
                int before = hud.ShownQuickSlotsScripted();
                hud.SubmitChatLine("/debug packet " + live.OwnRideBytes(true, rideId, 0x7e000100 + (rideId & 0xff)));
                await until(() => live.OwnRidee?.Id == rideId, 60 * 8);
                await frames(90);
                var on = hud.SkillSetScripted();
                Check($"ridewarp {rideId}: riding {live.OwnRidee?.Id}; {on.Text}", live.OwnRidee?.Id == rideId && on.Ok);
                int entered = 0; void Entered(int map, long ms) => entered++;
                live.FieldEntered += Entered;
                hud.SubmitChatLine("/warp " + a[2]);
                await until(() => entered > 0, 60 * 90); await frames(240);
                live.FieldEntered -= Entered;
                if (flow.GetTree().CurrentScene is Ui.HudScene next) hud = next;
                var after = hud.SkillSetScripted();
                int shown = hud.ShownQuickSlotsScripted();
                Check($"ridewarp {rideId}: map {live.CurrentEntry?.MapId} (entered {entered}), riding {live.OwnRidee?.Id.ToString() ?? "none"}; "
                    + $"{after.Text}; key table locked {live.KeyTable.Locked}; quick slots shown {shown}, {before} before the ride",
                    entered > 0 && live.OwnRidee == null && after.Ok && !live.KeyTable.Locked && shown == before && before > 0);
                Shot($"ridewarp_{rideId}.png");
                continue;
            }
            if (step.StartsWith("attendraw:"))      // attendraw:<event id>: a raw AttendGift event and an Attendance kind 7 the server's debug packet command sends (no test server holds a running one); the window opens on it
            {
                int eventId = int.Parse(step[10..]);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                bool sentEvent = hud.SubmitChatLine("/debug packet " + live.AttendEventBytes(eventId, now - 86400, now + 7 * 86400, 20302686, 20302775));
                await until(() => live.AttendDialog.Events.Any(e => e.Id == eventId), 60 * 8);
                Check($"attendraw {eventId}: the dialog holds the event (sent {sentEvent})", live.AttendDialog.Events.Any(e => e.Id == eventId));
                bool sentSelect = hud.SubmitChatLine("/debug packet " + live.AttendSelectBytes(eventId));
                await until(() => hud.AttendGiftOpenScripted, 60 * 8);
                await frames(90);
                Check($"attendraw {eventId}: the window opened (sent {sentSelect})", hud.AttendGiftOpenScripted);
                Shot($"attend_{eventId}.png");
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
                int loopFx = RideLoopScripted(live, "rideraw " + rideId, true, 0);
                var setOn = hud.SkillSetScripted();
                Check($"rideraw {rideId}: skillSetID {live.OwnPlayer?.Mount?.Data.SkillSetId}: {setOn.Text}", setOn.Ok);
                var avOn = hud.RideeAvatarScripted(string.IsNullOrEmpty(shots) ? null : System.IO.Path.Combine(shots, $"ride_{rideId}_portrait.png"));
                Check($"rideraw {rideId}: {avOn.Text}", avOn.Ok);
                hud.DriveTowardScripted(live.OwnPlayer.GlobalPosition + new Vector3(400, 0, 0), false);
                await frames(60);
                Shot($"ride_{rideId}_run.png");
                hud.DriveEnd();
                await frames(30);
                hud.SubmitChatLine("/debug packet " + live.OwnRideBytes(false, rideId, 0));
                await until(() => live.OwnRidee == null, 60 * 8);
                await frames(60);
                Check($"rideraw {rideId}: off; {hud.RideHudScripted()}", live.OwnRidee == null);
                RideLoopScripted(live, "rideraw " + rideId, false, loopFx);
                var setOff = hud.SkillSetScripted();
                Check($"rideraw {rideId}: after the ride off: {setOff.Text}", setOff.Ok);
                var avOff = hud.RideeAvatarScripted();
                Check($"rideraw {rideId}: after the ride off: {avOff.Text}", avOff.Ok);
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
                int loopFx = RideLoopScripted(live, "rideitem " + itemId, true, 0);
                hud.DriveTowardScripted(live.OwnPlayer.GlobalPosition + new Vector3(400, 0, 0), false);
                await frames(60);
                Shot($"rideitem_{itemId}_run.png");
                hud.DriveEnd();
                await frames(30);
                live.UseRideeItem(uid);
                await until(() => live.OwnRidee == null, 60 * 8);
                await frames(60);
                Check($"rideitem {itemId}: off; {hud.RideHudScripted()}", live.OwnRidee == null);
                RideLoopScripted(live, "rideitem " + itemId, false, loopFx);
                Shot($"rideitem_{itemId}_off.png");
                continue;
            }
            if (step.StartsWith("npcnotice:"))      // npcnotice:<announce|popup>: a raw NpcNotice the server's debug packet command sends (no AI of the server's data sends one); what the HUD showed, with a shot
            {
                bool popup = step[10..] == "popup";
                string before = hud.LastNpcNoticeShown;
                // the texts and illustration of npc AI nodes in the server data (AI_BullArmorRed's announce, AI_Conder_FR_Lv1's sidePopup)
                bool sent = hud.SubmitChatLine("/debug packet " + (popup ? live.NpcNoticeBytes(3, "$AI__AI_CONDER_FR_LV1__0$", 4000, "Conder_smile")
                                                                         : live.NpcNoticeBytes(0, "$BullArmorRed_DashAttack$", 10000)));
                await until(() => hud.LastNpcNoticeShown != before, 60 * 5);
                await frames(30);
                Check($"npcnotice {step[10..]}: {hud.LastNpcNoticeShown} (sent {sent})", hud.LastNpcNoticeShown != before);
                Shot($"npcnotice_{step[10..]}.png");
                continue;
            }
            if (step.StartsWith("npcride:"))      // npcride:<owner npc>:<rider npc>: two spawned npcs and raw NpcRide packets (debug packet command; neither server sends one): the rider bound to the owner's Ride_A node, mode 0's damage, mode 2's end, then the own pc as the owner (no Ride_A: the rider is not moved)
            {
                var c = step[8..].Split(':');
                int ownerNid = int.Parse(c[0]), riderNid = int.Parse(c[1]);
                async System.Threading.Tasks.Task<FieldNpcs.Placed> SpawnNpc(int nid)
                {
                    int added = npcs;
                    hud.SubmitChatLine("/npc " + nid);
                    await until(() => npcs > added && lastNpc?.Row?.Id == nid, 60 * 5);
                    await frames(30);
                    return lastNpc?.Row?.Id == nid ? lastNpc : null;
                }
                var owner = await SpawnNpc(ownerNid);
                var rider = await SpawnNpc(riderNid);
                var r = live.remote;
                if (owner == null || rider == null || r == null)
                {
                    Check($"npcride {ownerNid}:{riderNid}: spawned owner {owner?.ObjectId} rider {rider?.ObjectId}", false);
                    continue;
                }
                int oid = owner.ObjectId, rid = rider.ObjectId;
                var was = r.NpcRideOf(rid);
                bool sent = hud.SubmitChatLine("/debug packet " + live.NpcRideBytes(rid, 1, oid));
                await until(() => r.NpcRideOf(rid)?.Owner == oid, 60 * 5);
                await frames(30);
                var on = r.NpcRideOf(rid);
                var node = r.RideNodeOf(oid);
                float off = on is { } o && node is { } n ? o.At.DistanceTo(n) : -1f;
                Check($"npcride {ownerNid}:{riderNid}: rider {rid} owner {on?.Owner} (sent {sent}); owner {oid} Ride_A {(node is { } nn ? $"{nn.X:0},{nn.Y:0},{nn.Z:0}" : "none")}, " +
                    $"rider at {(on is { } a ? $"{a.At.X:0},{a.At.Y:0},{a.At.Z:0}" : "-")} ({off:0.00} off), before {(was is { } w ? $"{w.At.X:0},{w.At.Y:0},{w.At.Z:0}" : "-")}",
                    // the rider is placed in the npc update, before the owner's animation moves its Ride_A bone in the same frame,
                    // so an animated node is a frame ahead of the rider (the client reads the node's last world transform too)
                    on?.Owner == oid && node != null && off >= 0 && off < 10f);
                Shot("npcride_on.png");
                const long damage = 123456789012;
                hud.SubmitChatLine("/debug packet " + live.NpcRideBytes(rid, 0, damage));
                await until(() => r.NpcRideOf(rid)?.AccumDamage == damage, 60 * 5);
                Check($"npcride {riderNid}: mode 0 accumulated damage {r.NpcRideOf(rid)?.AccumDamage}, owner kept {r.NpcRideOf(rid)?.Owner}",
                    r.NpcRideOf(rid)?.AccumDamage == damage && r.NpcRideOf(rid)?.Owner == oid);
                hud.SubmitChatLine("/debug packet " + live.NpcRideBytes(rid, 2));
                await until(() => r.NpcRideOf(rid)?.Owner == 0, 60 * 5);
                await frames(30);
                Check($"npcride {riderNid}: mode 2 owner {r.NpcRideOf(rid)?.Owner}", r.NpcRideOf(rid)?.Owner == 0);
                Shot("npcride_off.png");
                int self = r.Tracker.SelfObjectId;
                var before = r.NpcRideOf(rid)?.At;
                hud.SubmitChatLine("/debug packet " + live.NpcRideBytes(rid, 1, self));
                await until(() => r.NpcRideOf(rid)?.Owner == self, 60 * 5);
                await frames(30);
                var still = r.NpcRideOf(rid);
                var pcPos = live.OwnPlayer?.GlobalPosition;
                Check($"npcride {riderNid}: the own pc {self} as the owner: owner {still?.Owner}, pc Ride_A {(r.RideNodeOf(self) is { } pn ? $"{pn}" : "none")}, " +
                    $"rider moved {(still is { } s2 && before is { } b0 ? s2.At.DistanceTo(b0) : -1):0.0}, {(still is { } s3 && pcPos is { } pp ? s3.At.DistanceTo(pp) : -1):0} from the pc",
                    still?.Owner == self && r.RideNodeOf(self) == null);
                hud.SubmitChatLine("/debug packet " + live.NpcRideBytes(rid, 2));
                await until(() => r.NpcRideOf(rid)?.Owner == 0, 60 * 5);
                hud.SubmitChatLine("/debug packet " + live.NpcRideBytes(987654321, 1, oid));     // no such npc: OnNpcRide's FindNpcById fails, nothing read
                await frames(30);
                continue;
            }
            if (step.StartsWith("insignia:"))      // insignia:<symbol>: a raw Insignia for the own pc (debug packet command); the own record and name tag
            {
                ushort sym = ushort.Parse(step[9..]);
                bool sent = hud.SubmitChatLine("/debug packet " + live.OwnInsigniaBytes(sym, true));
                await until(() => live.Me.Insignia == sym && live.Me.InsigniaShown, 60 * 5);
                await frames(30);
                Check($"insignia {sym}: own record {live.Me.Insignia} shown {live.Me.InsigniaShown}, own tag shows {hud.OwnTagSymbolScripted()} (sent {sent})",
                    live.Me.Insignia == sym && hud.OwnTagSymbolScripted() == sym);
                Shot($"insignia_{sym}.png");
                continue;
            }
            if (step.StartsWith("follownpc:"))      // follownpc:<trigger>:<state index>: the GM trigger reset into the state that moves the user along a path (FollowNpc); the follow and its end
            {
                string[] parts = step[10..].Split(':');
                var pc = live.OwnPlayer;
                var at0 = pc.GlobalPosition;
                bool sent = hud.SubmitChatLine($"/trigger reset {parts[0]} --state {parts[1]}");
                await until(() => pc.State == Player.MoveState.TriggerFollowNpc, 60 * 15);
                bool followed = pc.State == Player.MoveState.TriggerFollowNpc;
                int npc = pc.FollowedNpc;
                await frames(90);
                Shot("follownpc_on.png");
                var mid = pc.GlobalPosition;
                await until(() => pc.State != Player.MoveState.TriggerFollowNpc, 60 * 60);
                Check($"follownpc {step[10..]}: followed npc {npc} ({followed}, sent {sent}); moved {at0.DistanceTo(mid):F0} in 1.5 s, {at0.DistanceTo(pc.GlobalPosition):F0} in all; state now {pc.State}",
                    followed && at0.DistanceTo(pc.GlobalPosition) > 50 && pc.State != Player.MoveState.TriggerFollowNpc);
                Shot("follownpc_off.png");
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
            if (step.StartsWith("idle:"))      // idle:<ms>: WorldTimeSync's SetIdleInterval with a short interval (the Maple2 fork sends none)
            {
                live.IdleTimer.SetIdleInterval(int.Parse(step[5..]));
                GD.Print($"live: combat {step}: idle interval {live.IdleTimer.IntervalMs} ms, deadline {live.IdleTimer.DeadlineTick}, idle {live.IdleTimer.Idle}");
                continue;
            }
            if (step.StartsWith("holdkey:"))      // holdkey:<Left|Right|Up|Down|C>:<frames>: the key held down for N frames, as the player's
            {
                var kp = step[8..].Split(':');
                var key = System.Enum.Parse<Key>(kp[0]);
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                await frames(int.Parse(kp[1]));
                Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                await frames(2);
                continue;
            }
            if (step.StartsWith("notice:"))      // notice:<StringCode>:<flags>[:raw]: the GM /notice, or the whole packet through /debug packet
            {
                var np = step[7..].Split(':');
                int code = int.Parse(np[0]);
                ushort nflags = (ushort)int.Parse(np[1]);
                bool raw = np.Length > 2 && np[2] == "raw";
                var nc = hud.Notices;
                if (nc == null) { Check($"{step}: the notice routing is set up", false); continue; }
                // the server enum is the GMS2 client's table; the fork sends the KMS id of the same key
                string nkey = Ms2.Formats.Text.Gms2CommonStringIds.Key(code);
                int clientId = live.GameProfile.IsGms2 ? code : Enumerable.Range(0, 5000).FirstOrDefault(i => Ms2.Formats.Text.CommonStringIds.Key(i) == nkey);
                string want = nc.Tables.ReservedWords.ReplaceObjectWords(Ms2.Formats.Text.ArgumentFormat.Format(nc.Tables.Common(nkey ?? ""), Array.Empty<string>()));
                var route = new List<string>();
                var (sm, cl, mb, tl) = (nc.ShowMessage, nc.ChatLine, nc.MessageBox, nc.TopLayerNotify);
                nc.ShowMessage = (t, type, o) => { route.Add($"text {type} {(t == want ? "ok" : "other: " + t)}"); sm?.Invoke(t, type, o); };
                nc.ChatLine = (t, k) =>
                {
                    // the GM command's console reply comes as its own notice line; not part of the route
                    if (!t.StartsWith("Sent StringCode", StringComparison.Ordinal)) route.Add($"chat 0x{k:x} {(t == want || t.EndsWith(want) ? "ok" : "other: " + t)}");
                    cl?.Invoke(t, k);
                };
                nc.MessageBox = (t, tag) => { route.Add($"box '{tag}' {(t == want ? "ok" : "other: " + t)}"); mb?.Invoke(t, tag); };
                nc.TopLayerNotify = (t, v) => { route.Add($"top {(t == want ? "ok" : "other: " + t)}"); tl?.Invoke(t, v); };
                Ms2.Net.Packets.NoticeMessage got = null;
                Action<Ms2.Net.Packets.NoticeMessage> onN = n => { if (n.Text.Formatted && n.Text.Id == clientId) got ??= n; };
                live.Notice += onN;
                bool sentOk = hud.SubmitChatLine(raw ? "/debug packet " + live.NoticeBytes(nflags, clientId, (nflags & 0x10) != 0 ? 3000 : 5000)
                                                     : $"/notice {code} -f {nflags}");
                await until(() => got != null, 60 * 5);
                bool topUp = false;
                if ((nflags & 0x200) != 0) { await until(() => hud.TopLayerUp, 60 * 3); topUp = hud.TopLayerUp; }
                await frames(20);
                Shot($"notice-{code}-{nflags:x}{(raw ? "-raw" : "")}.png");
                live.Notice -= onN;
                (nc.ShowMessage, nc.ChatLine, nc.MessageBox, nc.TopLayerNotify) = (sm, cl, mb, tl);
                GD.Print($"live: combat {step}: sent {sentOk}, key {nkey}, client id {clientId}, packet {(got == null ? "none" : $"flags 0x{got.Flags:x} id {got.Text.Id} arg {got.Arg} top {got.TopLayerArg}")}, route [{string.Join(", ", route)}], text \"{want}\"");
                string[] expect = nflags switch
                {
                    1 => new[] { "chat 0x14 ok" },
                    4 => new[] { "text 6 ok" },
                    5 => new[] { "text 6 ok", "chat 0x14 ok" },
                    0x10 => new[] { "text 9 ok" },
                    0x40 => new[] { "box '' ok" },
                    0x200 => new[] { "top ok", "chat 0x1d ok" },
                    0x400 => new[] { "text 2 ok", "chat 0x14 ok" },
                    _ => null,
                };
                if (expect == null) { GD.Print($"live: combat {step}: no expected route for these flags"); continue; }
                Check($"notice {code} ({nkey}) flags 0x{nflags:x}{(raw ? " (raw)" : "")} shows its text where the flags say",
                    got != null && want.Length > 0 && route.SequenceEqual(expect));
                if ((nflags & 0x200) != 0)
                {
                    await until(() => !hud.TopLayerUp, 60 * 20);
                    Check($"the top layer announce loads for notice {code} and unloads at its onHideMessage", topUp && !hud.TopLayerUp);
                }
                if ((nflags & 0x40) != 0) { Ms2.Ui.Hud.MessageBoxQueue.Next(); await frames(10); }
                continue;
            }
            if (step.StartsWith("chat:"))      // one chat line, no comma in it
            {
                GD.Print($"live: combat {step}: {(hud.SubmitChatLine(step[5..]) ? "sent" : "no chat window")}");
                await frames(120);
                continue;
            }
            if (step.StartsWith("mesoburst:"))      // mesoburst:<mobs>:<npc id>: that many mobs spawned beside the player and killed together by one GM kill near (the server's death and drops), so their meso piles come in together; the pickup key is held and the step waits until no meso has come for 3 s (at most 30 s). Each pickup's sound prints as an audio voice line. The GM item command does not drop mesos (it gives them)
            {
                var mp = step[10..].Split(':');
                int mobs = int.Parse(mp[0]), mob = int.Parse(mp[1]), added = npcs, gains = 0;
                var piles = new System.Collections.Generic.List<Ms2.Net.Packets.FieldAddItem>();
                void Pile(Ms2.Net.Packets.FieldAddItem d, bool shown) { if (shown && d.IsMeso) piles.Add(d); }
                System.Action<long, int> gain = (d, k) => { if (d > 0) gains++; };
                for (int i = 0; i < mobs; i++) { hud.SubmitChatLine("/npc " + mob); await frames(6); }
                await until(() => npcs - added >= mobs, 60 * 10); await frames(10);
                // MS2_LIVE_MESOBURST_PRESSES: attack presses at the mobs first, for a server whose GM kill records no damage
                // dealer and so drops nothing (the upstream server)
                for (int i = 0, presses = int.Parse(Env("MS2_LIVE_MESOBURST_PRESSES", "0")); i < presses && lastNpc != null; i++)
                {
                    hud.FaceScripted(lastNpc.Position); await frames(4); hud.AttackScripted(); await frames(24);
                }
                live.DropAdded += Pile;
                live.MesoChanged += gain;
                live.PickupKeyHeld = true;
                hud.SubmitChatLine("/kill near --distance 600");
                int last = -1;
                for (int n = 0, quiet = 0; n < 60 * 30 && quiet < 180; n++, quiet = gains == last && piles.Count > 0 ? quiet + 1 : 0)
                {
                    last = gains;
                    await frames(1);
                }
                live.PickupKeyHeld = false;
                live.DropAdded -= Pile;
                live.MesoChanged -= gain;
                GD.Print($"live: combat {step}: {npcs - added} mob(s), {piles.Count} meso pile(s): {string.Join(" ", piles.Select(d => d.Amount))}");
                Check($"{step}: {gains} meso pickups of {piles.Count} piles", piles.Count > 1 && gains == piles.Count);
                Shot("mesoburst_picked.png");
                continue;
            }
            if (step == "gohome")      // the game menu's go home (RequestHome 3, -1), then the field entry
            {
                int entered = 0; void Entered(int map, long ms) => entered++;
                live.FieldEntered += Entered;
                live.SendHomeWarp();
                await until(() => entered > 0, 60 * 90); await frames(240);
                live.FieldEntered -= Entered;
                Check($"the field entry after go home (map {live.CurrentEntry?.MapId})", entered > 0);
                Shot("home.png");
                continue;
            }
            if (step.StartsWith("cubeplace:"))      // cubeplace:<item id>[:x:y:z]: the build mode with that cube, its place on the cell (else the one in front), the build mode off
            {
                var a = step[10..].Split(':');
                int itemId = int.Parse(a[0]);
                (int, int, int)? cell = a.Length == 4 ? (int.Parse(a[1]), int.Parse(a[2]), int.Parse(a[3])) : null;
                live.SendCraftModeStart(itemId);
                await until(() => live.CubeCraft.InCubeMode, 60 * 10); await frames(60);
                Check($"the build mode with item {itemId} (cube mode {live.CubeCraft.CubeMode}, uid {live.CubeCraft.Current?.Item?.InstanceId ?? 0})", live.CubeCraft.InCubeMode);
                int key = live.PlaceHeldCube(cell);
                GD.Print($"live: combat {step}: place sent on cell 0x{key:x}");
                await frames(180);
                Shot("placed.png");
                live.SendCraftModeOff();
                await frames(120);
                continue;
            }
            if (step.StartsWith("cubeuse"))      // cubeuse[:range]: a run to the nearest function cube, then the use of the nearest usable one (vfunc_8's request)
            {
                float range = step.Length > 8 ? float.Parse(step[8..]) : 300;
                if (live.NearestFunctionCube() is { } near)
                {
                    var at = new Vector3(near.Position.X, near.Position.Z, -near.Position.Y);
                    float dist = hud.DriveTowardScripted(at, false);
                    for (int walk = 0; walk < 60 * 25 && dist > 120; walk++) { dist = hud.DriveTowardScripted(at, false); await frames(1); }
                    hud.DriveEnd(); hud.FaceScripted(at); await frames(60);
                    GD.Print($"live: combat {step}: at {dist:F0} from {near.Id}");
                }
                var c = live.UseNearestFunctionCube(range);
                GD.Print($"live: combat {step}: {(c != null ? $"use sent for {c.Id} (item {c.ItemId}, state {c.State})" : "no usable cube in range")}");
                await frames(180);
                Shot("cubeuse.png");
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
            if (step.StartsWith("movieend:"))      // movieend:<secs>: until the playing cut scene movie has ended (one Esc does not skip), at most that long
            {
                await until(() => !live.Movie.IsPlaying, 60 * int.Parse(step[9..]));
                Check("no cut scene movie plays", !live.Movie.IsPlaying);
                await frames(60);
                continue;
            }
            if (step.StartsWith("learn:"))      // learn:<skill id>[/<skill id>...]: skill points by the GM command, the skill window opened, each learnt with the skills it requires, applied
            {
                var ids = step[6..].Split('/').Select(uint.Parse).ToArray();
                hud.SubmitChatLine("/player skillpoint 30");
                await frames(90);
                bool open = hud.SkillBookOpenScripted();
                await until(() => hud.SkillBookTreeShown, 60 * 10); await frames(60);
                foreach (var id in ids)
                {
                    string got = hud.SkillBookLearnScripted(id);
                    await frames(20);
                    GD.Print($"live: combat {step}: {id}: {got}: {hud.SkillBookSlotState(id) ?? "not in the tree"}");
                }
                GD.Print($"live: combat {step}: window {(open ? "open" : "refused")}, apply {(hud.SkillBookApplyScripted() ? "sent" : "refused")}");
                await frames(120);
                hud.EscScripted(); await frames(60);
                continue;
            }
            if (step == "hudonce")      // the HUD's live player watch counted (HudScene.LiveWatchScripted), one per event and model
            {
                var counts = hud.LiveWatchScripted(out bool once);
                Check($"each live event reaches the HUD once ({counts})", once);
                continue;
            }
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
            if (step.StartsWith("store"))      // the bank steps: storeopen, storenpc:<npc id>, storegive:N, storedeposit:N, storewithdraw:N, storemove, storemeso{deposit|withdraw}:N, storesort, storeclose
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
                    case "storenpc":      // storenpc:<npc id>: the own pc put in front of the field's bank npc (no walk), the action key; the talk without a script opens the bank (CNpc::OpenTalkUi)
                    {
                        int id = int.Parse(arg);
                        var tfc = flow.GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;
                        FieldNpcs.Placed n = null;
                        await until(() => (n = live.TalkNpcs.FirstOrDefault(x => x.Row.Id == id && Alive(x))) != null, 60 * 30);
                        Check($"{step}: the bank npc in the field (kind {n?.Row.Kind})", n != null);
                        if (n == null || tfc?.Talk == null || live.player == null || !GodotObject.IsInstanceValid(live.player)) break;
                        double yaw = n.Root.Rotation.Y;
                        var front = new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
                        live.player.GlobalPosition = n.Position + front * 120 + new Vector3(0, 20, 0);
                        live.player.Velocity = Vector3.Zero;
                        live.player.Face(-front);
                        await frames(90);
                        Shot("store_npc_before.png");
                        bool ready0 = live.Storage.Ready;
                        tfc.Talk.ActionKey();
                        await until(() => hud.StoreIsOpen && live.Storage.Ready, 60 * 10); await frames(60);
                        GD.Print($"live: combat store npc {id}: bank ready before {ready0}, talking {live.IsTalking()} (npc object {live.TalkNpcId}), talk window {tfc.Talk.Open}");
                        Check($"the bank npc's talk opened the bank ({live.Storage.Count} item(s), {live.Storage.Meso} meso)", hud.StoreIsOpen && live.Storage.Ready);
                        Check("no talk window over it", !tfc.Talk.Open);
                        Shot("store_npc_open.png");
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
                    {
                        bool talking0 = live.IsTalking();
                        GD.Print($"live: combat store close: {(hud.StoreCloseScripted() ? "sent" : "not open")}");
                        await frames(120); Shot("store_close.png");
                        GD.Print($"live: combat store close: talking {talking0} -> {live.IsTalking()}, bank open {hud.StoreIsOpen}");
                        break;
                    }
                    default: GD.Print($"live: combat: unknown step {step}"); break;
                }
                continue;
            }
            if (step.StartsWith("bag"))      // the bag steps: baggive:N (MS2_LIVE_BAG_ITEM), bagkeep (MS2_LIVE_BAG_KEEP, one item), bagopen, bagsort[:id], bagui, bagdrop:N, bagdestroy, bagexpand[:id], baguse:id[:box|yes|notice], bagselect:id[:row|open], bagyes, bagmove:id, bagmerge:id:N, baggemon:id, baggemoff:id[:drag], bagnamecheck:id:name[:used], baggacha:id[:multi], baglullu:id[:multi], bagtreasure:id[:free]
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
                    case "bagyes":      // bagyes: the message box shown now answered with its first button (a bind-on-equip box's yes)
                    {
                        var shown = hud.ShownBoxScripted();
                        GD.Print($"live: combat bag yes \"{shown?.Extra}\": {(hud.BagBoxAnswerScripted(shown, 0, shown?.Extra ?? "") ? "answered" : "no box")}");
                        await frames(30);
                        break;
                    }
                    case "baguse":      // baguse:<item id>[:box|yes|notice]: the item given when missing, its slot double clicked
                    {
                        var parts = arg.Split(':');
                        int id = int.Parse(parts[0]); string expect = parts.Length > 1 ? parts[1] : "box";
                        if (Count(id) == 0) { hud.SubmitChatLine($"/item {id}"); await until(() => Count(id) > 0, 60 * 5); await frames(30); }
                        long n0 = Count(id);
                        var notices = new System.Collections.Generic.List<string>();
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        hud.WatchBagNotices(notices.Add);
                        GD.Print($"live: combat bag use {id} ({slot}, {n0} held): {(hud.BagUseScripted(slot) ? "double clicked" : "no slot")}");
                        await frames(30);
                        hud.WatchBagNotices(null);
                        var shown = boxes.LastOrDefault();
                        GD.Print($"live: combat bag use {id}: boxes [{Tags()}] type {shown?.Type} msg \"{shown?.Msg}\" notices [{string.Join(" | ", notices)}]");
                        Shot($"bag_use_{id}.png");
                        if (expect == "notice")
                        {
                            Check($"item {id}: a notice, no box, the item kept ({n0} -> {Count(id)})", notices.Count > 0 && boxes.Count == 0 && Count(id) == n0);
                            break;
                        }
                        Check($"item {id}: a box ({Tags()}), no notice, the item kept ({n0} -> {Count(id)})", shown != null && notices.Count == 0 && Count(id) == n0);
                        if (expect == "yes")
                        {
                            var extra0 = string.Join(",", live.Items.ExtraSlots.Select(kv => $"{kv.Key}:{kv.Value}"));
                            GD.Print($"live: combat bag use {id} yes: {(hud.BagBoxAnswerScripted(shown, 0, shown?.Extra ?? "") ? "answered" : "no box")}");
                            await until(() => Count(id) != n0, 60 * 5); await frames(60);
                            var extra1 = string.Join(",", live.Items.ExtraSlots.Select(kv => $"{kv.Key}:{kv.Value}"));
                            Check($"item {id} used by the server ({n0} -> {Count(id)}), extra slots [{extra0}] -> [{extra1}]", Count(id) == n0 - 1);
                            Shot($"bag_use_{id}_yes.png");
                            if (hud.ShownBoxScripted() is { } after) { GD.Print($"live: combat bag use {id} answer box \"{after.Msg}\""); hud.BagBoxAnswerScripted(after, 0, after.Extra); }
                        }
                        else
                        {
                            int button = shown?.Extra.Length > 0 ? 1 : 0;      // a tagged ask: its no; an untagged box: its OK
                            GD.Print($"live: combat bag use {id} button {button}: {(hud.BagBoxAnswerScripted(shown, button, shown?.Extra ?? "") ? "answered" : "no box")}");
                            await frames(120);
                            Check($"item {id}: nothing used ({n0} -> {Count(id)})", Count(id) == n0);
                        }
                        await frames(30);
                        break;
                    }
                    case "bagselect":   // bagselect:<item id>[:<row>|open]: the select box item given when missing and double clicked, its list checked; a row picked and OK: the box used and the row's reward in the bag; open: closed unpicked
                    {
                        var parts = arg.Split(':');
                        int id = int.Parse(parts[0]); string what = parts.Length > 1 ? parts[1] : "0";
                        if (Count(id) == 0) { hud.SubmitChatLine($"/item {id}"); await until(() => Count(id) > 0, 60 * 5); await frames(30); }
                        long n0 = Count(id);
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        GD.Print($"live: combat bag select {id} ({slot}, {n0} held): {(hud.BagUseScripted(slot) ? "double clicked" : "no slot")}");
                        await until(() => hud.SelectItemBoxScripted() != null, 60 * 5); await frames(30);
                        var open = hud.SelectItemBoxScripted();
                        GD.Print($"live: combat bag select {id}: picks {open?.Picks} box {open?.Box}, rows [{string.Join(",", open?.Rewards.Select(e => e.ItemId) ?? Enumerable.Empty<int>())}]");
                        Shot($"bag_select_{id}.png");
                        Check($"item {id}: the select dialog lists box {open?.Box} ({open?.Rewards.Count} rows)", open is { Rewards.Count: > 0 });
                        if (open is not { } o || o.Rewards.Count == 0) { hud.SelectItemBoxCloseScripted(); break; }
                        if (what == "open")
                        {
                            hud.SelectItemBoxCloseScripted();
                            await frames(60);
                            Check($"item {id}: closed unpicked, nothing used ({n0} -> {Count(id)})", hud.SelectItemBoxScripted() == null && Count(id) == n0);
                            break;
                        }
                        int row = Math.Min(int.Parse(what), o.Rewards.Count - 1);
                        int reward = o.Rewards[row].ItemId; long r0 = Count(reward);
                        GD.Print($"live: combat bag select {id} row {row} ({reward}): {(hud.SelectItemBoxPickScripted(row) ? "picked" : "no row")}, ok {hud.SelectItemBoxOkScripted()}");
                        await until(() => Count(id) != n0 && Count(reward) > r0, 60 * 5); await frames(60);
                        Check($"item {id} used by the server ({n0} -> {Count(id)}), row {row} item {reward} in the bag ({r0} -> {Count(reward)}), the dialog closed",
                            Count(id) == n0 - 1 && Count(reward) > r0 && hud.SelectItemBoxScripted() == null);
                        Shot($"bag_select_{id}_ok.png");
                        break;
                    }
                    case "baggacha":    // baggacha:<item id>[:multi]: the capsules given (1, or 10 for multi), one double clicked, the machine's open: the capsules used by the server and the result bound
                    {
                        var parts = arg.Split(':');
                        int id = int.Parse(parts[0]); bool multi = parts.Length > 1 && parts[1] == "multi"; int need = multi ? 10 : 1;
                        int machine = hud.GachaMachineScripted(id);    // the capsule machine item a GachaCapsuleMachine open also needs held
                        foreach (int give in machine != 0 && machine != id ? new[] { id, machine } : new[] { id })
                            if (Count(give) < need) { long g0 = Count(give); hud.SubmitChatLine($"/item {give} --amount {need - g0}"); await until(() => Count(give) >= need, 60 * 5); await frames(30); }
                        long n0 = Count(id);
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        GD.Print($"live: combat gacha {id} ({slot}, {n0} held): {(hud.BagUseScripted(slot) ? "double clicked" : "no slot")}");
                        await until(() => hud.GachaBoxScripted() != null, 60 * 5); await frames(90);
                        Shot($"gacha_{id}.png");
                        Check($"item {id}: the gacha machine is open (box {hud.GachaBoxScripted()?.Box}), nothing used ({n0} -> {Count(id)})", hud.GachaBoxScripted() is { } g && g.Box == id && Count(id) == n0);
                        if (hud.GachaBoxScripted() == null) break;
                        var notices = new System.Collections.Generic.List<string>();
                        hud.WatchBagNotices(notices.Add);
                        GD.Print($"live: combat gacha {id} {(multi ? "multi" : "single")}: {(hud.GachaBoxOpenScripted(multi) ? "open clicked" : "not open")}");
                        await until(() => hud.GachaBoxScripted() is { Slots: > 0 } || hud.GachaBoxScripted() == null, 60 * 20); await frames(120);
                        hud.WatchBagNotices(null);
                        var after = hud.GachaBoxScripted();
                        GD.Print($"live: combat gacha {id}: sent {after?.Sent} slots {after?.Slots}, boxes [{Tags()}] notices [{string.Join(" | ", notices)}]");
                        Shot($"gacha_{id}_result.png");
                        Check($"item {id}: {need} used by the server ({n0} -> {Count(id)}), the result bound ({after?.Slots} slots), no box or notice",
                              Count(id) == n0 - need && after is { Slots: > 0, Sent: false } && notices.Count == 0);
                        hud.GachaBoxCloseScripted();
                        await frames(60);
                        Check($"item {id}: the machine closed", hud.GachaBoxScripted() == null);
                        break;
                    }
                    case "baglullu":    // baglullu:<item id>[:multi]: the Lullu box item, its keys and boxes given, the item double clicked, the box's open button: a key and a box used per open, the result bound
                    {
                        var parts = arg.Split(':');
                        int id = int.Parse(parts[0]); bool multi = parts.Length > 1 && parts[1] == "multi"; int need = multi ? 10 : 1;
                        var (keyItem, boxItem) = hud.LulluItemsScripted(id);
                        GD.Print($"live: combat lullu {id}: key item {keyItem}, box item {boxItem}");
                        var want = new System.Collections.Generic.Dictionary<int, long> { [id] = 1 };   // an item that is also a key or box: the sum
                        foreach (int give in new[] { keyItem, boxItem }) if (give != 0) want[give] = (want.TryGetValue(give, out long w) && give != id ? w : 0) + need;
                        foreach (var (give, n) in want)
                            if (Count(give) < n) { long g0 = Count(give); hud.SubmitChatLine($"/item {give} --amount {n - g0}"); await until(() => Count(give) >= n, 60 * 5); await frames(30); }
                        long k0 = Count(keyItem), b0 = Count(boxItem);
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        GD.Print($"live: combat lullu {id} ({slot}, keys {k0}, boxes {b0}): {(hud.BagUseScripted(slot) ? "double clicked" : "no slot")}");
                        await until(() => hud.LulluBoxScripted() != null, 60 * 5); await frames(90);
                        Shot($"lullu_{id}.png");
                        Check($"item {id}: the Lullu box is open (box {hud.LulluBoxScripted()?.Box}), nothing used (keys {k0} -> {Count(keyItem)})", hud.LulluBoxScripted() is { } l && l.Box == id && Count(keyItem) == k0);
                        if (hud.LulluBoxScripted() == null) break;
                        var notices = new System.Collections.Generic.List<string>();
                        hud.WatchBagNotices(notices.Add);
                        GD.Print($"live: combat lullu {id} {(multi ? "multi" : "single")}: {(hud.LulluBoxOpenScripted(multi ? 1 : 0) ? "open clicked" : "not open")}");
                        await until(() => hud.LulluBoxScripted() is { Slots: > 0 } || hud.LulluBoxScripted() == null, 60 * 20); await frames(120);
                        hud.WatchBagNotices(null);
                        var after = hud.LulluBoxScripted();
                        GD.Print($"live: combat lullu {id}: sent {after?.Sent} slots {after?.Slots}, keys {k0} -> {Count(keyItem)}, boxes {b0} -> {Count(boxItem)}, boxes [{Tags()}] notices [{string.Join(" | ", notices)}]");
                        Shot($"lullu_{id}_result.png");
                        long keysUsed = k0 - Count(keyItem), boxesUsed = b0 - Count(boxItem);
                        Check($"item {id}: {need} key(s) and box(es) used by the server (keys -{keysUsed}, boxes -{boxesUsed}), the result bound ({after?.Slots} slots)",
                              (keyItem == boxItem ? keysUsed == 2 * need : keysUsed == need && (boxItem == 0 || boxesUsed == need)) && after is { Slots: > 0, Sent: false });
                        hud.LulluBoxCloseScripted();
                        await frames(60);
                        Check($"item {id}: the Lullu box closed", hud.LulluBoxScripted() == null);
                        break;
                    }
                    case "bagtreasure": // bagtreasure:<map item id>[:free]: the map given when missing, the talk's open-dialog action for ItemTreasureMapDialog (as NpcTalk sub 4 raises it), the map picked: its cost; the submit: the appraise (paid: the map used and the item shown; free without the npc's free talk: refused, the map kept); back and closed
                    {
                        var parts = arg.Split(':');
                        int id = int.Parse(parts[0]); bool free = parts.Length > 1 && parts[1] == "free";
                        if (Count(id) == 0) { hud.SubmitChatLine($"/item {id}"); await until(() => Count(id) > 0, 60 * 5); await frames(30); }
                        long n0 = Count(id);
                        long uid = live.Items.All.First(e => e.Id == id).Uid;
                        live.RaiseTalkDialogOpen(Ms2.Ui.Hud.ItemTreasureMapDialogController.Name, free ? Ms2.Ui.Hud.ItemTreasureMapDialogController.FreeArgument : "");
                        await until(() => hud.TreasureMapScripted() != null, 60 * 5); await frames(90);
                        var tm = hud.TreasureMapScripted();
                        GD.Print($"live: combat treasure map {id} (uid {uid}, {n0} held): state {tm?.State} free {tm?.Free} maps [{string.Join(",", tm?.Maps ?? new())}]");
                        Shot($"treasure_{id}{(free ? "_free" : "")}.png");
                        Check($"item {id}: the treasure map dialog is open (free {tm?.Free}) and lists the map", tm is { } t0 && t0.Free == free && t0.Maps.Contains(uid));
                        if (tm is not { } t1 || !t1.Maps.Contains(uid)) { hud.TreasureMapCloseScripted(); break; }
                        GD.Print($"live: combat treasure map {id}: {(hud.TreasureMapPickScripted(t1.Maps.IndexOf(uid)) ? "picked" : "not open")}");
                        await until(() => hud.TreasureMapScripted() is { Target: > 0 }, 60 * 10); await frames(60);
                        var picked = hud.TreasureMapScripted();
                        GD.Print($"live: combat treasure map {id}: target {picked?.Target} shown {picked?.Shown}");
                        Shot($"treasure_{id}{(free ? "_free" : "")}_cost.png");
                        Check($"item {id}: the server's cost answer shows the map (target {picked?.Target}, shown {picked?.Shown})", picked is { } p1 && p1.Target == uid && p1.Shown == id);
                        if (picked is not { Target: > 0 }) { hud.TreasureMapCloseScripted(); break; }
                        GD.Print($"live: combat treasure map {id}: submit {(hud.TreasureMapSubmitScripted() ? "clicked" : "not open")}, state {hud.TreasureMapScripted()?.State}");
                        await until(() => hud.TreasureMapScripted() is { State: not 1 }, 60 * 15); await frames(90);
                        var done = hud.TreasureMapScripted();
                        GD.Print($"live: combat treasure map {id}: state {done?.State} shown {done?.Shown}, held {n0} -> {Count(id)}");
                        Shot($"treasure_{id}{(free ? "_free" : "")}_result.png");
                        if (free)
                            Check($"item {id}: the free appraise without the npc's free talk is refused: back to the list, the map kept ({n0} -> {Count(id)})",
                                  done is { State: 0, Shown: 0 } && Count(id) == n0);
                        else
                        {
                            Check($"item {id}: appraised: the map used ({n0} -> {Count(id)}), the result item {done?.Shown} shown with the back button",
                                  done is { State: 2 } d2 && d2.Shown != 0 && d2.Shown != id && Count(id) == n0 - 1);
                            hud.TreasureMapSubmitScripted();
                            await frames(60);
                            Check($"item {id}: back to the list (state {hud.TreasureMapScripted()?.State})", hud.TreasureMapScripted() is { State: 0 });
                        }
                        hud.TreasureMapCloseScripted();
                        await frames(60);
                        Check($"item {id}: the treasure map dialog closed", hud.TreasureMapScripted() == null);
                        break;
                    }
                    case "bagnamecheck": // bagnamecheck:<item id>:<name>[:used]: the name change item used, the name sent, the answer's box checked and closed
                    {
                        var parts = arg.Split(':');
                        int id = int.Parse(parts[0]); string newName = parts[1]; bool expectUsed = parts.Length > 2 && parts[2] == "used";
                        if (Count(id) == 0) { hud.SubmitChatLine($"/item {id}"); await until(() => Count(id) > 0, 60 * 5); await frames(30); }
                        long n0 = Count(id);
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        GD.Print($"live: combat name change {id} ({slot}): {(hud.BagUseScripted(slot) ? "double clicked" : "no slot")}");
                        await frames(30);
                        var input = hud.ShownBoxScripted();
                        Check($"item {id}: the name box (type {input?.Type}, tag {input?.Extra}, input max {input?.InputMaxChars})",
                              input is { Type: 10, Extra: Ms2.Ui.CharNameChange.Tag, InputMaxChars: Ms2.Ui.CharNameChange.MaxLength });
                        if (input == null) break;
                        GD.Print($"live: combat name change {id} \"{newName}\": {(hud.BagBoxAnswerScripted(input, 0, Ms2.Ui.CharNameChange.Tag + "," + newName) ? "answered" : "no box")}");
                        await until(() => hud.ShownBoxScripted() is { } b && b != input, 60 * 5); await frames(30);
                        var answer = hud.ShownBoxScripted();
                        GD.Print($"live: combat name change {id} answer: type {answer?.Type} tag \"{answer?.Extra}\" msg \"{answer?.Msg}\" args [{string.Join(",", answer?.Args ?? new())}]");
                        Shot($"name_change_{id}.png");
                        if (expectUsed)
                            Check($"\"{newName}\" is used: the plain box with the name", answer is { Type: 0, Extra: "" } && answer.Msg.Contains(newName));
                        else
                            Check($"\"{newName}\" is free: the confirm box with the item and the name", answer is { Type: 1, Extra: Ms2.Ui.CharNameChange.ConfirmTag }
                                  && answer.ArgString(1, "") == newName && answer.ArgLong(0, 0) == live.Items.All.First(e => e.Id == id).Uid);
                        if (answer != null && answer != input) hud.BagBoxAnswerScripted(answer, answer.Type == 0 ? 0 : 1, answer.Extra);    // its OK, or the confirm's no
                        await frames(120);
                        Check($"item {id}: nothing used ({n0} -> {Count(id)})", Count(id) == n0);
                        break;
                    }
                    case "bagmove":      // bagmove:<item id>: the item given when missing, its stack dragged onto the first empty cell of its tab
                    {
                        int id = int.Parse(arg);
                        if (Count(id) == 0) { hud.SubmitChatLine($"/item {id}"); await until(() => Count(id) > 0, 60 * 5); await frames(30); }
                        hud.BagSlotScripted(id, live.Items.TabOf(id));
                        var e = live.Items.All.First(x => x.Id == id);
                        short from = e.InventorySlot; int amount = e.Amount; long total = Count(id);
                        string src = hud.BagSlotOfUidScripted(e.Uid), dst = hud.BagSlotOfUidScripted(0);
                        GD.Print($"live: combat bag move {id} uid {e.Uid} {src} -> {dst}: {(hud.DragEndOnScripted(src, dst) ? "dropped" : "no slot")}");
                        await until(() => live.Items.Get(e.Uid)?.InventorySlot != from, 60 * 5); await frames(30);
                        Check($"item {id} moved by the server ({src} -> {dst}: slot {from} -> {live.Items.Get(e.Uid)?.InventorySlot}), the stack's count kept ({amount} -> {live.Items.Get(e.Uid)?.Amount}, all {total} -> {Count(id)})",
                              dst != null && hud.BagSlotOfUidScripted(e.Uid) == dst && live.Items.Get(e.Uid)?.Amount == amount && Count(id) == total);
                        Shot($"bag_move_{id}.png");
                        break;
                    }
                    case "bagmerge":      // bagmerge:<item id>:<amount>: the amount given, then the largest stack dragged onto the smallest
                    {
                        var parts = arg.Split(':'); int id = int.Parse(parts[0]); int n = int.Parse(parts[1]);
                        System.Collections.Generic.List<Ms2.Net.Packets.EquipItem> Stacks() => live.Items.All.Where(x => x.Id == id).OrderBy(x => x.Amount).ToList();
                        long n0 = Count(id);
                        hud.SubmitChatLine($"/item {id} --amount {n}");
                        await until(() => Count(id) == n0 + n, 60 * 5); await frames(30);
                        var st = Stacks();
                        GD.Print($"live: combat bag merge {id}: stacks [{string.Join(",", st.Select(x => $"{x.Uid}:{x.Amount}@{x.InventorySlot}"))}]");
                        Check($"item {id} is held in two stacks or more", st.Count >= 2);
                        if (st.Count < 2) break;
                        var small = st[0]; var big = st[^1]; int a0 = small.Amount, b0 = big.Amount; long total = Count(id);
                        hud.BagSlotScripted(id, live.Items.TabOf(id));
                        string src = hud.BagSlotOfUidScripted(big.Uid), dst = hud.BagSlotOfUidScripted(small.Uid);
                        GD.Print($"live: combat bag merge {big.Uid} ({b0}) onto {small.Uid} ({a0}): {(hud.DragEndOnScripted(src, dst) ? "dropped" : "no slot")}");
                        await until(() => (live.Items.Get(small.Uid)?.Amount ?? 0) != a0, 60 * 5); await frames(30);
                        int a1 = live.Items.Get(small.Uid)?.Amount ?? 0, b1 = live.Items.Get(big.Uid)?.Amount ?? 0;
                        Check($"the server stacked them: {small.Uid} {a0} -> {a1}, {big.Uid} {b0} -> {b1}, total {total} -> {Count(id)}",
                              a1 > a0 && b1 < b0 && a1 + b1 == a0 + b0 && Count(id) == total);
                        Shot($"bag_merge_{id}.png");
                        break;
                    }
                    case "baggemon":      // baggemon:<gem id>: the gem given when missing, the character window opened, the gem right clicked in the bag (mode 12)
                    {
                        int id = int.Parse(arg);
                        if (Count(id) == 0) { hud.SubmitChatLine($"/item {id}"); await until(() => Count(id) > 0, 60 * 5); await frames(30); }
                        long n0 = Count(id);
                        Check("the character window is open", hud.OpenCharScripted()); await frames(30);
                        hud.CharGemTabScripted(); await frames(30);
                        string slot = hud.BagSlotScripted(id, live.Items.TabOf(id));
                        GD.Print($"live: combat bag gem on {id} ({slot}): {(hud.BagRightClickScripted(slot) ? "right clicked" : "no slot")}");
                        await frames(30);
                        if (hud.ShownBoxScripted() is { Extra: Ms2.Ui.Hud.MyInfoController.BindOnGemEquipBox } bind)
                        { GD.Print($"live: combat bag gem bind box \"{bind.Msg}\": {(hud.BagBoxAnswerScripted(bind, 0, bind.Extra) ? "yes" : "no box")}"); }
                        await until(() => hud.WornGemSlotScripted(id) != null, 60 * 5); await frames(30);
                        Check($"gem {id} worn ({hud.WornGemSlotScripted(id)}), the bag {n0} -> {Count(id)}", hud.WornGemSlotScripted(id) != null && Count(id) == n0 - 1);
                        Shot($"bag_gem_on_{id}.png");
                        break;
                    }
                    case "baggemoff":      // baggemoff:<gem id>[:drag]: the worn gem's slot right clicked (GemSlot::vfunc_14), or dragged onto an empty bag cell
                    {
                        var parts = arg.Split(':'); int id = int.Parse(parts[0]); bool drag = parts.Length > 1 && parts[1] == "drag";
                        long n0 = Count(id);
                        hud.OpenCharScripted(); hud.CharGemTabScripted(); await frames(30);
                        string gem = hud.WornGemSlotScripted(id);
                        hud.BagSlotScripted(id, live.Items.TabOf(id));
                        string empty = hud.BagSlotOfUidScripted(0);
                        bool done = drag ? hud.DragEndOnScripted(gem, empty) : hud.CharRightClickScripted(gem);
                        GD.Print($"live: combat bag gem off {id} ({gem}{(drag ? " -> " + empty : "")}): {(done ? (drag ? "dropped" : "right clicked") : "no slot")}");
                        await until(() => hud.WornGemSlotScripted(id) == null, 60 * 5); await frames(30);
                        Check($"gem {id} taken off ({hud.WornGemSlotScripted(id) ?? "none worn"}), the bag {n0} -> {Count(id)}", gem != null && hud.WornGemSlotScripted(id) == null && Count(id) == n0 + 1);
                        Shot($"bag_gem_off_{id}.png");
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
                : step.StartsWith("bagfull:") ? "bagfull" : step.StartsWith("orbpick:") ? "orbpick" : step.StartsWith("unequip:") ? "unequip" : step.StartsWith("autoloot:") || step.StartsWith("autofull:") ? "autoloot" : step)
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
                case "skipmovie": case "skipmoviebutton":    // a playing cut scene movie skipped as the client does: Esc asks box 0x88c, its first button runs Exit; then until the server's stop (skipmoviebutton: the swf pane's own exit button is clicked, as onExit does)
                {
                    await until(() => live.Movie.IsPlaying, 60 * 30);
                    int id = live.Movie.CutSceneId;
                    Check("a cut scene movie plays", live.Movie.IsPlaying);
                    if (!live.Movie.IsPlaying) break;
                    int sends = live.Movie.Sends;
                    if (step == "skipmoviebutton" || Env("MS2_LIVE_SKIP_BUTTON", "") == "1")      // the exit button clicked on the movie's own m_exit instead of Esc
                    {
                        await frames(60);
                        Check("the exit button was clicked", hud.CutSceneSwfExitClickScripted());
                        GD.Print($"live: combat skip movie {id}: exit button clicked");
                    }
                    else
                    {
                        hud.Escape();
                        GD.Print($"live: combat skip movie {id}: Esc pressed");
                    }
                    await until(() => hud.ShownBoxScripted() != null, 60 * 3); await frames(45);   // the box fades in
                    var ask = hud.ShownBoxScripted();
                    Check($"the skip asks a box ({ask?.Msg})", ask != null);
                    Check("nothing is sent before the answer", live.Movie.Sends == sends);
                    Shot("skipmovie_box.png");
                    hud.Escape(); await frames(5);
                    Check("a second Esc asks no second box", hud.ShownBoxScripted() == ask);
                    GD.Print($"live: combat skip movie: box answered {(hud.BagBoxAnswerScripted(ask, 0, ask?.Extra ?? "") ? "yes" : "no box")}");
                    await frames(10);
                    Check("the answer sent the stop request", live.Movie.Sends == sends + 1 || id == 0);
                    await until(() => !live.Movie.IsPlaying, 60 * 15);
                    GD.Print($"live: combat skip movie {id}: the server {(live.Movie.IsPlaying ? "did not stop it" : "stopped it")}");
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
                    await frames(30);
                    if (hud.ShownBoxScripted() is { Extra: Ms2.Ui.Hud.InventoryController.BindOnEquipBox } bind)      // a bind-on-equip item asks first (inventory.cpp:1245)
                        GD.Print($"live: combat equip bind box \"{bind.Msg}\" yes: {(hud.BagBoxAnswerScripted(bind, 0, bind.Extra) ? "answered" : "no box")}");
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
                case "skillgate":     // an archer's skill window: the gated slots locked, no point into a gated skill (at level 10+, one into a skill it opens)
                {
                    const uint Stream = 10500001, Barrage = 10500021, Gate10 = 10500041;
                    int level = live.Me.User.Level;
                    bool open = hud.SkillBookOpenScripted();
                    await until(() => hud.SkillBookTreeShown, 60 * 10); await frames(60);
                    GD.Print($"live: combat skill gate at level {level}: window {(open ? "open" : "refused")}, tree {hud.SkillBookTreeShown}");
                    foreach (var id in new[] { Stream, Gate10, Barrage }) GD.Print($"live: combat skill gate {id}: {hud.SkillBookSlotState(id) ?? "not in the tree"}");
                    string req = hud.SkillBookRequireText(Barrage);
                    GD.Print($"live: combat skill gate {Barrage} requirements: {req}");
                    Shot($"skillgate-{level}.png");
                    Check($"the tree is shown at level {level}", hud.SkillBookTreeShown);
                    Check($"{Barrage} (level 16, {Stream} 3) is locked", hud.SkillBookSlotState(Barrage)?.StartsWith("disabled True", StringComparison.Ordinal) == true);
                    Check($"{Stream} (learnt with the rank) is open", hud.SkillBookSlotState(Stream)?.StartsWith("disabled False", StringComparison.Ordinal) == true);
                    Check($"{Gate10} (level 10) is {(level >= 10 ? "open" : "locked")}", hud.SkillBookSlotState(Gate10)?.StartsWith($"disabled {level < 10}", StringComparison.Ordinal) == true);
                    Check($"the {Barrage} tooltip's requirement shows level 16 unmet", req?.Contains("16", StringComparison.Ordinal) == true && req.Contains("ff4040", StringComparison.Ordinal));
                    bool up = hud.SkillBookPointUpScripted(Barrage);
                    await frames(30);
                    GD.Print($"live: combat skill gate {Barrage} point up ({(up ? "taken" : "refused")}): {hud.SkillBookSlotState(Barrage)}");
                    Check($"no point goes into {Barrage}", hud.SkillBookSlotState(Barrage)?.Contains("learnt False", StringComparison.Ordinal) == true);
                    if (level >= 10)
                    {
                        hud.SkillBookPointUpScripted(Gate10);
                        await frames(30);
                        GD.Print($"live: combat skill gate {Gate10} point up: {hud.SkillBookSlotState(Gate10)}");
                        Check($"a point goes into {Gate10} at level {level}", hud.SkillBookSlotState(Gate10)?.Contains("learnt True", StringComparison.Ordinal) == true);
                    }
                    break;
                }
                case "skillkey":     // a learnt active skill of the skill window right clicked (onRegisterShortcutkey): KeyTable mode 4, the server puts it on the page
                {
                    bool open = hud.SkillBookOpenScripted();
                    await until(() => hud.SkillBookTreeShown, 60 * 10); await frames(30);
                    if (hud.SkillBookPickScripted(false) == 0 && hud.SkillBookPickScripted(true) == 0)
                    {
                        // nothing learnt in the shown tree (a rank 2 window right after the awakening): the rank 1 button first
                        hud.SkillBookCallbackScripted("onChangeSubJob", 0.0);
                        await frames(30);
                        GD.Print($"live: combat skill key: no learnt skill in the shown tree, rank 1 button: {hud.SkillBookRankScripted()}");
                    }
                    uint id = hud.SkillBookPickScripted(false);
                    if (id == 0 && hud.SkillBookPickScripted(true) is var on and > 0)
                    {
                        // every learnt active skill is on the page: one taken off first by the quick slot's right click
                        int was = hud.QuickSlotOfSkill(on);
                        hud.QuickSlotRemoveScripted(was);
                        await until(() => hud.QuickSlotOfSkill(on) < 0, 60 * 5); await frames(10);
                        GD.Print($"live: combat skill key: {on} taken off quick slot {was}: {(hud.QuickSlotOfSkill(on) < 0 ? "gone" : "still there")}");
                        id = hud.QuickSlotOfSkill(on) < 0 ? on : 0;
                    }
                    GD.Print($"live: combat skill key: window {(open ? "open" : "refused")}, skill {id}, quick slot before {hud.QuickSlotOfSkill(id)}");
                    bool clicked = id != 0 && hud.SkillBookRightClickScripted(id);
                    await until(() => hud.QuickSlotOfSkill(id) >= 0, 60 * 5); await frames(10);
                    int slot = hud.QuickSlotOfSkill(id);
                    GD.Print($"live: combat skill key: right click {(clicked ? "sent" : "not sent")}, quick slot after {slot}, the bar shows {hud.QuickSlotShownSkill(slot)}");
                    Shot("skillkey.png");
                    Check("a learnt active skill off the quick slots is in the tree", id != 0);
                    Check($"the right click puts {id} on the quick slot page", clicked && slot >= 0);
                    Check("the quick slot bar shows it", slot >= 0 && hud.QuickSlotShownSkill(slot) == (int)id);
                    break;
                }
                case "skilldrag":     // a learnt active skill of the skill window dragged onto an empty quick slot: KeyTable mode 3, the server's page has it there
                {
                    bool open = hud.SkillBookOpenScripted();
                    await until(() => hud.SkillBookTreeShown, 60 * 10); await frames(30);
                    uint id = hud.SkillBookPickScripted(false);
                    if (id == 0) id = hud.SkillBookPickScripted(true);
                    int target = hud.QuickSlotEmptyScripted();
                    GD.Print($"live: combat skill drag: window {(open ? "open" : "refused")}, skill {id} (on quick slot {hud.QuickSlotOfSkill(id)}), empty slot {target}");
                    bool dragged = id != 0 && target >= 0 && hud.SkillBookDragScripted(id, target);
                    await until(() => hud.QuickSlotOfSkill(id) == target, 60 * 5); await frames(10);
                    GD.Print($"live: combat skill drag: {(dragged ? "dropped" : "not dropped")}, the server's page has it on {hud.QuickSlotOfSkill(id)}, the bar shows {hud.QuickSlotShownSkill(target)} there");
                    Shot("skilldrag.png");
                    Check("a learnt active skill and an empty quick slot", id != 0 && target >= 0);
                    Check($"the dropped skill {id} is on quick slot {target} of the server's page", dragged && hud.QuickSlotOfSkill(id) == target);
                    Check("the quick slot bar shows it", target >= 0 && hud.QuickSlotShownSkill(target) == (int)id);
                    break;
                }
                case "skillreset":     // a point applied (Job mode 9), then the reset button, its box's OK (Job mode 10): the server's book has the point, then not
                {
                    bool open = hud.SkillBookOpenScripted();
                    await until(() => hud.SkillBookTreeShown, 60 * 10); await frames(30);
                    uint id = hud.SkillBookLevelableScripted();
                    int before = hud.BookLevelScripted(id);
                    GD.Print($"live: combat skill reset: window {(open ? "open" : "refused")}, level {live.Me.User.Level}, skill {id} at {before}, {hud.SkillBookSlotState(id)}");
                    bool up = id != 0 && hud.SkillBookCallbackScripted("onSkillPointUp", (double)id);
                    bool applied = up && hud.SkillBookCallbackScripted("onApplySkill");
                    await until(() => !hud.SkillBookWaiting && hud.BookLevelScripted(id) == before + 1, 60 * 5); await frames(10);
                    int after = hud.BookLevelScripted(id);
                    GD.Print($"live: combat skill reset: apply {(applied ? "sent" : "not sent")}, the book has {id} at {after}, waiting {hud.SkillBookWaiting}");
                    Shot("skillreset-applied.png");
                    Check($"a skill takes a point at level {live.Me.User.Level}", id != 0 && up);
                    Check($"the apply's answer has {id} at {before + 1}", applied && after == before + 1);
                    bool reset = hud.SkillBookCallbackScripted("onResetSkill");
                    await until(() => Ms2Godot.Ui.MessageBoxLayer.Instance?.Shown == true, 60 * 3); await frames(10);
                    bool box = Ms2Godot.Ui.MessageBoxLayer.Instance?.Shown == true;
                    Shot("skillreset-box.png");
                    Ms2Godot.Ui.MessageBoxLayer.Instance?.Click(0);
                    await until(() => !hud.SkillBookWaiting && hud.BookLevelScripted(id) == before, 60 * 5); await frames(10);
                    int back = hud.BookLevelScripted(id);
                    GD.Print($"live: combat skill reset: button {(reset ? "taken" : "refused")}, box {(box ? "shown" : "not shown")}, the book has {id} at {back}, waiting {hud.SkillBookWaiting}, {hud.SkillBookSlotState(id)}");
                    Shot("skillreset-done.png");
                    Check("the reset button asks first", reset && box);
                    Check($"the reset's answer takes {id} back to {before}", back == before && !hud.SkillBookWaiting);
                    break;
                }
                case "subjob":     // the skill window's rank buttons: below rank 2 the rank 2 one keeps the rank 1 tree; at rank 2 the rank 1 one, then the rank 2 one
                {
                    bool open = hud.SkillBookOpenScripted();
                    await until(() => hud.SkillBookTreeShown, 60 * 10); await frames(30);
                    int rank = hud.SkillBookJobRankScripted;
                    string first = hud.SkillBookRankScripted();
                    GD.Print($"live: combat sub job: window {(open ? "open" : "refused")}, job rank {rank}, opened on: {first}");
                    if (rank < 2)
                    {
                        bool took = hud.SkillBookCallbackScripted("onChangeSubJob", 1.0);
                        await frames(30);
                        string after = hud.SkillBookRankScripted();
                        GD.Print($"live: combat sub job: rank 2 button {(took ? "taken" : "refused")}: {after}");
                        Shot($"subjob-{rank}.png");
                        Check("below rank 2 the rank 2 button keeps the rank 1 tree", after?.StartsWith("rank 1", StringComparison.Ordinal) == true && after.EndsWith("ranks 1", StringComparison.Ordinal));
                    }
                    else
                    {
                        Check("at rank 2 the window opens on the rank 2 tree", first?.StartsWith("rank 2", StringComparison.Ordinal) == true && first.EndsWith("ranks 2", StringComparison.Ordinal));
                        hud.SkillBookCallbackScripted("onChangeSubJob", 0.0);
                        await frames(30);
                        string one = hud.SkillBookRankScripted();
                        GD.Print($"live: combat sub job: rank 1 button: {one}");
                        Shot("subjob-rank1.png");
                        Check("the rank 1 button shows the rank 1 tree", one?.StartsWith("rank 1", StringComparison.Ordinal) == true && one.EndsWith("ranks 1", StringComparison.Ordinal));
                        hud.SkillBookCallbackScripted("onChangeSubJob", 1.0);
                        await frames(30);
                        string two = hud.SkillBookRankScripted();
                        GD.Print($"live: combat sub job: rank 2 button: {two}");
                        Shot("subjob-rank2.png");
                        Check("the rank 2 button shows the rank 2 tree again", two == first);
                    }
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
                    // unequip:<item id>, or MS2_LIVE_COMBAT_WEAPON without one
                    int weapon = int.Parse(step.StartsWith("unequip:") ? step["unequip:".Length..] : Env("MS2_LIVE_COMBAT_WEAPON", "0"));
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
                case "orbpick":
                {
                    // orbpick:<npc id>[:<orb id>]: an elite whose hits roll the SP and EP orb drop box (globalHitDropBoxId 100000) is
                    // spawned and hit (again, up to three spawns) until a hit drops the orb (90000009 / 90000010, any orb without an id;
                    // the GM item command gives a currency id straight to the player and cannot drop one), and the pickup key is held at
                    // it. InitDrop gives the orb the DropSPEPBall constants (LoadSpepBallConstants, dropitem.cpp:269-270, :1093-1102), so
                    // the key reaches it as far as DropSPEPBallMaxLength and the pickup flies it to DropSPEPBallTargetZPos above the
                    // player; FUN_141190900 sends it with no bag-full refusal (:954-958), auto loot takes it whatever its switches (a special id, FUN_141261340), and the
                    // server's pickup carries the EP gained for the EP orb (scenestategame.cpp:2438)
                    string[] arg = step[8..].Split(':');
                    int mob = int.Parse(arg[0]), want = arg.Length > 1 ? int.Parse(arg[1]) : 0;
                    bool Wanted(int id) => want == 0 ? Ms2.Formats.World.DropPickup.IsSpepBallId(id) : id == want;
                    var notices = new System.Collections.Generic.List<int>();
                    var made = new System.Collections.Generic.List<Ms2.Net.Packets.FieldAddItem>();
                    var sets = new System.Collections.Generic.Dictionary<int, (Ms2.Formats.World.DropConstants Set, bool SpepBall)?>();
                    var picks = new System.Collections.Generic.List<Ms2.Net.Packets.FieldPickupItem>();
                    var autoAsked = new System.Collections.Generic.HashSet<int>();
                    void Notice(int n) => notices.Add(n);
                    void Made(Ms2.Net.Packets.FieldAddItem d, bool shown) { if (shown && Ms2.Formats.World.DropPickup.IsSpepBallId(d.ItemId)) { made.Add(d); sets[d.ObjectId] = live.DropSet(d.ObjectId); } }
                    void Pick(Ms2.Net.Packets.FieldPickupItem x) => picks.Add(x);
                    void Auto(int objectId) => autoAsked.Add(objectId);
                    live.DropAdded += Made;
                    live.DropPickupRead += Pick;
                    live.DropAutoRequest += Auto;
                    live.DropNotice += Notice;
                    int press = 0, spawns = 0, presses = int.Parse(Env("MS2_LIVE_COMBAT_PRESSES", "40"));
                    for (; spawns < 3 && !made.Any(d => Wanted(d.ItemId)) && !live.Me.Dead; spawns++)
                    {
                        int added = npcs;
                        hud.SubmitChatLine("/npc " + mob);
                        await until(() => npcs > added, 60 * 15); await frames(60);
                        var target = lastNpc;
                        if (target == null || npcs == added) break;
                        for (; press < presses && !made.Any(d => Wanted(d.ItemId)) && !live.Me.Dead && GodotObject.IsInstanceValid(target.Root) && !live.NpcDead(target.ObjectId); press++)
                        { hud.FaceScripted(target.Position); await frames(20); hud.AttackScripted(); await frames(60); }
                        await until(() => made.Any(d => Wanted(d.ItemId)), 60 * 2);
                    }
                    live.DropAdded -= Made;
                    var drop = made.FirstOrDefault(d => Wanted(d.ItemId));
                    int item = drop?.ItemId ?? want;
                    GD.Print($"live: combat orbpick npc {mob}: {spawns} spawn(s), {press} press(es), orbs {string.Join(" ", made.Select(d => $"{d.ItemId}x{d.Amount}"))}{(live.Me.Dead ? ", the player died" : "")}");
                    Check($"a hit on npc {mob} dropped the orb {(want == 0 ? "" : want.ToString())}", drop != null);
                    if (drop != null)
                    {
                        var set = sets.GetValueOrDefault(drop.ObjectId);
                        GD.Print($"live: combat orbpick {item}: range {set?.Set.MaxLength}, target z {set?.Set.TargetZ}, pickup vel {set?.Set.PickUpVel}, gravity {set?.Set.PickUpGravity}, hover {set?.Set.RotateTime}s x{set?.Set.RotateVel}");
                        Check("the orb takes the SPEP ball constants", set?.SpepBall == true);
                        if (live.DropShown(drop.ObjectId) != null) { await frames(10); Shot($"orb_{item}_ground.png"); }
                        live.PickupKeyHeld = true;
                        bool shot = false;
                        for (int n = 0; n < 60 * 8 && !picks.Any(x => x.ObjectId == drop.ObjectId && x.Picked); n++)
                        {
                            if (live.DropShown(drop.ObjectId) is { } s && hud.DriveTowardScripted(s.At, false) < 50) hud.DriveEnd();
                            await frames(1);
                        }
                        hud.DriveEnd();
                        for (int n = 0; n < 60 && live.DropShown(drop.ObjectId) is { } s; n++)
                        {
                            if (!shot && s.State == Ms2.Formats.World.DropMotion.FlyToPicker) { shot = true; Shot($"orb_{item}_fly.png"); }
                            await frames(1);
                        }
                        live.PickupKeyHeld = false;
                        var got = picks.FirstOrDefault(x => x.ObjectId == drop.ObjectId && x.Picked);
                        GD.Print($"live: combat orbpick {item}: picked {got != null} by {got?.PickerId} ({(autoAsked.Contains(drop.ObjectId) ? "auto loot" : "the key")}) ep +{got?.Ep} (ep ball {got?.EpBall}), notices {string.Join(" ", notices.Select(x => $"0x{x:x}"))}, the hud EP {Ms2Godot.Ui.StubHudData.Instance.EpCurrent}/{Ms2Godot.Ui.StubHudData.Instance.EpMax}");
                        Check($"the orb {item} was picked up by the player", got != null && got.PickerId == live.SelfObjectId);
                        Check("no bag-full refusal came", !notices.Contains(Ms2.Formats.World.DropPickup.BagFullNotice));
                        if (item == Ms2.Net.Packets.FieldPickupItem.EpItemId) Check($"the EP orb's pickup carries the EP gained (+{got?.Ep})", got?.EpBall == true);
                        await frames(60); Shot($"orb_{item}_after.png");
                    }
                    live.DropPickupRead -= Pick;
                    live.DropAutoRequest -= Auto;
                    live.DropNotice -= Notice;
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
